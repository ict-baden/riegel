// SPDX-License-Identifier: MIT
using Riegel.Commands.Alerting;
using Riegel.Commands.Resources;
using Riegel.Core.Model;
using Riegel.Core.Services;

namespace Riegel.Commands.Commands;

/// <summary>
/// Gemeinsamer Ablauf von check und import: Export lesen und prüfen, auswerten (mit oder ohne Anlage), mit vorigem
/// Lauf und freigegebenem Stand vergleichen, Tokens ableiten. Alles Fachliche machen die Dienste aus spec 7.2.
/// </summary>
internal sealed class Pipeline(CommandContext ctx)
{
    private CommandServices S => ctx.Services;

    /// <summary>
    /// Prüft die .sha256-Begleitdatei einer Exportdatei (spec 7.7: Prüfsummenfehler = Exitcode 3). Abweichend oder
    /// unlesbar → RGL-0465; fehlt sie, nur ein Hinweis (Exporte von Hand kopiert, Bordmittel-Pakete).
    /// </summary>
    public void CheckSidecar(string path)
    {
        if (Directory.Exists(path)) return;
        if (!File.Exists(path)) throw RiegelException.Common(RglCodesCommon.NotFound, path);
        var check = S.Snapshots.VerifySidecar(path);
        switch (check.Status)
        {
            case HashCheckStatus.Ok:
                return;
            case HashCheckStatus.Missing:
                ctx.Note(CliStrings.Cli_SidecarMissing, Path.GetFileName(path));
                return;
            default:
                throw RglCodesCli.Error(RglCodesCli.SidecarMismatch, path, check.Expected ?? "?", check.Actual);
        }
    }

    /// <summary>Exportdatei (.snapshot.json, .json.gz) oder Bordmittel-Ordner lesen.</summary>
    public Snapshot Read(string path)
    {
        if (Directory.Exists(path)) return S.Snapshots.FromBuiltinBundle(path, ReadLimits.Default);
        if (!File.Exists(path)) throw RiegelException.Common(RglCodesCommon.NotFound, path);
        return S.Snapshots.Read(path, ReadLimits.Default);
    }

    /// <summary>Ohne Anlage: nur auswerten und ausgeben, nichts speichern (spec 7.7).</summary>
    public RunAssessment WithoutPlant(Snapshot snapshot, string source, AlertPolicy policy)
    {
        var run = S.Runs.EvaluateOnly(snapshot, new RunOptions(Language: ctx.Language, Store: false));
        return Assessor.Assess(run, null, null, null, policy, stored: false, inPlant: false, source);
    }

    /// <summary>
    /// Mit Anlage: Register anwenden, Lauf speichern (wenn <paramref name="store"/>), mit vorigem Lauf und freigegebenem
    /// Stand vergleichen. Exporte von außerhalb des Anlagenordners kommen beim Speichern über den Import in die Anlage
    /// (snapshots/, Importnachweis), damit Lauf und Export zusammen im Nachweis liegen.
    /// </summary>
    public RunAssessment WithPlant(Plant plant, string path, Snapshot snapshot, bool store, AlertPolicy policy, string source)
    {
        Run run;
        bool inside = IsInside(plant, path);
        if (store && !inside)
        {
            var record = S.Plants.Import(plant, [path],
                new ImportOptions(null, null, plant.Settings.OperatorRole, Evaluate: true, Limits: ReadLimits.Default));
            var file = record.Files.FirstOrDefault()
                       ?? throw RiegelException.Common(RglCodesCommon.Unexpected, "import");
            run = S.Plants.LoadRun(plant, file.RunId ?? throw RiegelException.Common(RglCodesCommon.Unexpected, "import"));
        }
        else
        {
            run = S.Runs.EvaluateAndStore(plant, path, new RunOptions(Language: ctx.Language, Store: store, Limits: ReadLimits.Default));
        }
        return Assess(plant, run, snapshot, policy, store, source);
    }

    /// <summary>Gespeicherten oder frischen Lauf mit vorigem Lauf und freigegebenem Stand vergleichen und bewerten.</summary>
    public RunAssessment Assess(Plant plant, Run run, Snapshot? snapshot, AlertPolicy policy, bool stored, string source)
    {
        snapshot ??= TrySnapshotOf(plant, run);
        var previous = TryLoadRun(plant, run.PreviousRunId);
        RunComparison? vsPrevious = null, vsApproved = null;
        if (previous is not null && snapshot is not null)
            vsPrevious = S.Comparison.Compare(previous, TrySnapshotOf(plant, previous), run, snapshot, ComparisonBasis.Previous);

        var approved = S.Plants.LoadApproved(plant, run.HostName);
        if (approved is not null && approved.RunId != run.Id && snapshot is not null)
        {
            var approvedRun = TryLoadRun(plant, approved.RunId);
            if (approvedRun is not null)
                vsApproved = S.Comparison.Compare(approvedRun, TrySnapshotOf(plant, approvedRun), run, snapshot, ComparisonBasis.Approved);
            else
                ctx.Note(CliStrings.Cli_ApprovedRunMissing, run.HostName, approved.RunId);
        }
        return Assessor.Assess(run, previous, vsPrevious, vsApproved, policy, stored, inPlant: true, source);
    }

    /// <summary>Lauf laden; fehlt er (gelöscht, anderer Rechner), null.</summary>
    public Run? TryLoadRun(Plant plant, string? runId)
    {
        if (string.IsNullOrEmpty(runId)) return null;
        try { return S.Plants.LoadRun(plant, runId); }
        catch (RiegelException) { return null; }
    }

    /// <summary>Den Export eines Laufs aus dem Anlagenordner lesen; fehlt er, null (der Vergleich hat dann keine Rohwerte).</summary>
    public Snapshot? TrySnapshotOf(Plant plant, Run run)
    {
        if (plant.Folder is null || string.IsNullOrEmpty(run.Snapshot.Path)) return null;
        var path = Path.GetFullPath(Path.Combine(plant.Folder, run.Snapshot.Path.Replace('/', Path.DirectorySeparatorChar)));
        try { return File.Exists(path) || Directory.Exists(path) ? Read(path) : null; }
        catch (RiegelException) { return null; }
    }

    private static bool IsInside(Plant plant, string path)
    {
        if (plant.Folder is null) return false;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plant.Folder)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}

/// <summary>Eigener Temp-Ordner für einen frisch erfassten Export, wird danach gelöscht.</summary>
internal sealed class PrivateTemp : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("riegel-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
