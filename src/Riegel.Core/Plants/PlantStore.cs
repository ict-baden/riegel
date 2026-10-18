// SPDX-License-Identifier: MIT
using System.Text;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;

namespace Riegel.Core.Plants;

/// <summary>
/// Anlagenordner (spec 7.6): Anlage, Exporte, Import-Nachweise, Läufe, freigegebene Stände und Verlauf. Anlagendatei
/// und freigegebene Stände werden unter der Sperre <c>.riegel.lock</c> geschrieben (Vergleich der Prüfsumme, Ersetzen,
/// Verlaufskopie); Läufe, Exporte und Importe sind je Rechner und Zeit eindeutig und brauchen keine Sperre. Im
/// Prüfermodus (<see cref="Plant.ReadOnly"/>) lehnt jede schreibende Methode mit RGL-0401 ab.
/// </summary>
internal sealed class PlantStore(RiegelServices services) : IPlantStore
{
    /// <summary>Höchstzahl Dateien in einem Bordmittel-Paket für die Prüfsumme (feindliche Eingaben).</summary>
    private const int MaxBundleFiles = 2000;

    /// <summary>Wartezeit auf die Sperre (spec 7.6: 10 s; Tests kürzer).</summary>
    public TimeSpan LockTimeout { get; init; } = PlantLock.DefaultTimeout;

    // ---------------------------------------------------------------- Anlage

    public Plant Create(string dir, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw RglCodesWorkflow.Error(RglCodesWorkflow.FieldRequired, "name");
        var folder = Path.GetFullPath(dir);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, PlantFiles.PlantFile);
        var plant = new Plant(SchemaIds.Plant, FileNames.Token(name, "plant").ToLowerInvariant(), name.Trim(), null, [], [], PlantSettings.Default);
        string sha;
        using (PlantLock.Acquire(folder, LockTimeout))
        {
            if (File.Exists(file)) throw RiegelException.Common(RglCodesCommon.AlreadyExists, file);
            sha = PlantFiles.WriteJson(file, plant, out var bytes);
            PlantFiles.WriteHistory(folder, PlantFiles.HistoryKind.Plant, services.Clock.GetUtcNow(), bytes);
        }
        return plant with { Folder = folder, ReadOnly = false, FileSha256 = sha };
    }

    public Plant Open(string dir, bool readOnly)
    {
        var folder = Path.GetFullPath(dir);
        var file = Path.Combine(folder, PlantFiles.PlantFile);
        if (!File.Exists(file)) throw RglCodesWorkflow.Error(RglCodesWorkflow.NotAPlant, folder);
        var plant = RiegelJson.ReadFile<Plant>(file, ReadLimits.Default.MaxPlantFileBytes);
        if (plant.Schema != SchemaIds.Plant) throw RiegelException.Common(RglCodesCommon.UnsupportedSchema, plant.Schema);
        CheckPlant(plant);
        return plant with { Folder = folder, ReadOnly = readOnly, FileSha256 = Hashes.Sha256File(file) };
    }

    public void SavePlant(Plant p, string expectedSha256)
    {
        var folder = Writable(p);
        CheckPlant(p);
        var file = Path.Combine(folder, PlantFiles.PlantFile);
        var content = p with { Schema = SchemaIds.Plant };
        using (PlantLock.Acquire(folder, LockTimeout))
        {
            var current = PlantFiles.ShaOrEmpty(file);
            if (!string.Equals(current, expectedSha256 ?? "", StringComparison.OrdinalIgnoreCase))
                throw RglCodesWorkflow.Error(RglCodesWorkflow.ChangedByOthers, PlantFiles.PlantFile);
            PlantFiles.WriteJson(file, content, out var bytes);
            PlantFiles.WriteHistory(folder, PlantFiles.HistoryKind.Plant, services.Clock.GetUtcNow(), bytes);
        }
    }

    // ---------------------------------------------------------------- Import

    public ImportRecord Import(Plant p, IReadOnlyList<string> paths, ImportOptions o)
    {
        var folder = Writable(p);
        var limits = o.Limits ?? ReadLimits.Default;
        var now = services.Clock.GetUtcNow();

        // 1. Alles lesen und prüfen, bevor etwas geschrieben wird
        var prepared = new List<PreparedImport>();
        foreach (var source in paths)
        {
            if (Directory.Exists(source))
            {
                var snapshot = services.Snapshots.FromBuiltinBundle(source, limits);
                prepared.Add(new PreparedImport(Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source))),
                    snapshot, BundleSha256(source, limits), false, RiegelJson.SerializeToUtf8Bytes(snapshot), false));
                continue;
            }
            if (!File.Exists(source)) throw RiegelException.Common(RglCodesCommon.NotFound, source);
            var read = services.Snapshots.Read(source, limits);
            var check = services.Snapshots.VerifySidecar(source);
            if (check.Status == HashCheckStatus.Mismatch)
                throw RglCodesWorkflow.Error(RglCodesWorkflow.SidecarMismatch, Path.GetFileName(source), check.Expected, check.Actual);
            var content = File.ReadAllBytes(source);
            // die Datei darf sich zwischen Prüfen und Ablegen nicht ändern
            if (!string.Equals(Hashes.Sha256Hex(content), check.Actual, StringComparison.OrdinalIgnoreCase))
                throw RiegelException.Common(RglCodesCommon.HashMismatch, source);
            prepared.Add(new PreparedImport(Path.GetFileName(source), read, check.Actual, check.Status == HashCheckStatus.Ok,
                content, source.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)));
        }

        // Namenskonflikte vorab: ein anderer Export unter demselben Namen (im Ordner oder in diesem Import)
        var targets = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var item in prepared)
        {
            var target = PlantFiles.SnapshotTarget(folder, item.Snapshot, item.Gzip);
            var sha = Hashes.Sha256Hex(item.Content);
            if ((targets.TryGetValue(target, out var other) && other != sha) || (File.Exists(target) && Hashes.Sha256File(target) != sha))
                throw RglCodesWorkflow.Error(RglCodesWorkflow.SnapshotConflict, PlantFiles.Relative(folder, target));
            targets[target] = sha;
        }

        // 2. Ablegen, auswerten, Nachweis schreiben
        var id = FileNames.Timestamp(now) + "-" + Hashes.Sha256Hex(string.Join("\n", prepared.Select(x => x.Sha256)))[..6];
        var language = WorkflowText.CurrentLanguage();
        var files = new List<ImportedFile>();
        foreach (var item in prepared)
        {
            var stored = PlantFiles.StoreSnapshot(folder, item.Snapshot, item.Content, item.Gzip);
            string? runId = null;
            if (o.Evaluate)
                runId = services.Runs.EvaluateAndStore(p, stored, new RunOptions(ImportedAtUtc: now, Language: language, Limits: limits, ImportId: id)).Id;
            var s = item.Snapshot;
            files.Add(new ImportedFile(item.FileName, item.Sha256, item.SidecarOk, s.Machine.HostName, s.CollectedAtUtc, s.Collector.Name,
                s.Collector.Version, s.Collector.ExeSha256, s.IsAdmin, s.Options, s.Origin, runId));
        }
        var record = new ImportRecord(SchemaIds.Import, id, now, Clean(o.CollectedByRole), Clean(o.WitnessRole),
            Clean(o.OperatorRole) ?? p.Settings.OperatorRole, files);
        PlantFiles.WriteJson(Path.Combine(folder, PlantFiles.Imports, id + PlantFiles.ImportExtension), record, out _);
        return record;
    }

    private sealed record PreparedImport(string FileName, Snapshot Snapshot, string Sha256, bool SidecarOk, byte[] Content, bool Gzip);

    /// <summary>
    /// Prüfsumme eines Bordmittel-Pakets: SHA-256 über die Zeilen „&lt;sha256&gt;␠␠&lt;relativer Pfad&gt;“ aller Dateien,
    /// nach Pfad sortiert (wie sha256sum, „/“ als Trenner). So steht im Erhebungsprotokoll eine Prüfsumme je Paket.
    /// </summary>
    internal static string BundleSha256(string folder, ReadLimits limits)
    {
        var root = Path.GetFullPath(folder);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Relative: Path.GetRelativePath(root, f).Replace('\\', '/')))
            .OrderBy(f => f.Relative, StringComparer.Ordinal)
            .ToList();
        if (files.Count > MaxBundleFiles) throw RiegelException.Common(RglCodesCommon.LimitExceeded, folder, MaxBundleFiles);
        var listing = new StringBuilder();
        foreach (var (full, relative) in files)
        {
            if (new FileInfo(full).Length > limits.MaxTextFileBytes) throw RiegelException.Common(RglCodesCommon.LimitExceeded, relative, limits.MaxTextFileBytes);
            listing.Append(Hashes.Sha256File(full)).Append("  ").Append(relative).Append('\n');
        }
        return Hashes.Sha256Hex(listing.ToString());
    }

    // ---------------------------------------------------------------- Läufe

    public void SaveRun(Plant p, Run r)
    {
        var folder = Writable(p);
        if (!RunIds.IsValid(r.Id)) throw RglCodesWorkflow.Error(RglCodesWorkflow.InvalidRunId, r.Id);
        PlantFiles.WriteJson(PlantFiles.RunPath(folder, r.HostName, r.Id), r with { Schema = SchemaIds.Run }, out _);
    }

    public IReadOnlyList<RunInfo> ListRuns(Plant p, string host)
    {
        var runs = Path.Combine(Folder(p), PlantFiles.Runs);
        if (!Directory.Exists(runs)) return [];
        // unter Linux kann es denselben Rechner in zwei Schreibweisen geben: beide zählen
        var name = FileNames.Host(host);
        return [.. Directory.EnumerateDirectories(runs).Where(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase))
            .SelectMany(ReadRunInfos)
            .OrderBy(r => r.CollectedAtUtc).ThenBy(r => r.EvaluatedAtUtc).ThenBy(r => r.Id, StringComparer.Ordinal)];
    }

    private static List<RunInfo> ReadRunInfos(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var list = new List<RunInfo>();
        foreach (var file in Directory.EnumerateFiles(folder, "*" + PlantFiles.RunExtension))
        {
            var name = Path.GetFileName(file);
            if (!RunIds.IsValid(name[..^PlantFiles.RunExtension.Length])) continue; // fremde Dateien, keine Läufe
            var r = RiegelJson.ReadFile<Run>(file, ReadLimits.Default.MaxSnapshotBytes);
            list.Add(new RunInfo(r.Id, r.HostName, r.EvaluatedAtUtc, r.EvaluationDate, r.CollectedAtUtc, r.Snapshot.Sha256,
                r.Catalogue.Version, r.ReEvaluationOf, r.Flags, r.Summary.Score, r.Summary.OpenHigh));
        }
        return [.. list.OrderBy(r => r.CollectedAtUtc).ThenBy(r => r.EvaluatedAtUtc).ThenBy(r => r.Id, StringComparer.Ordinal)];
    }

    public Run LoadRun(Plant p, string runId)
    {
        if (!RunIds.IsValid(runId)) throw RglCodesWorkflow.Error(RglCodesWorkflow.InvalidRunId, runId);
        var file = RunFile(Folder(p), runId) ?? throw RiegelException.Common(RglCodesCommon.NotFound, runId);
        return RiegelJson.ReadFile<Run>(file, ReadLimits.Default.MaxSnapshotBytes);
    }

    /// <summary>Ergebnisdatei eines Laufs in irgendeinem Rechnerordner, null wenn es sie nicht gibt.</summary>
    private static string? RunFile(string folder, string runId)
    {
        var runs = Path.Combine(folder, PlantFiles.Runs);
        if (!Directory.Exists(runs)) return null;
        foreach (var hostFolder in Directory.EnumerateDirectories(runs).Order(StringComparer.Ordinal))
        {
            var file = Path.Combine(hostFolder, runId + PlantFiles.RunExtension);
            if (File.Exists(file)) return file;
        }
        return null;
    }

    // ---------------------------------------------------------------- Freigegebener Stand

    public ApprovedState? LoadApproved(Plant p, string host)
    {
        var file = PlantFiles.HostFile(Path.Combine(Folder(p), PlantFiles.Approved), host, ".json");
        if (!File.Exists(file)) return null;
        var state = RiegelJson.ReadFile<ApprovedState>(file, ReadLimits.Default.MaxPlantFileBytes);
        if (state.Schema != SchemaIds.Approved) throw RiegelException.Common(RglCodesCommon.UnsupportedSchema, state.Schema);
        return state;
    }

    /// <summary>
    /// Freigegebenen Stand speichern. Fehlende Prüfsummen (Export, Ergebnisse), Katalogversion und Register-Hashes
    /// ergänzt der Speicher aus dem Lauf; angegebene müssen zum Lauf passen (sonst RGL-0410). Rolle und Nachweis
    /// sind Pflicht (RGL-0408). Jede Freigabe kommt zusätzlich nach <c>history/approved/&lt;host&gt;/</c>.
    /// </summary>
    public void SaveApproved(Plant p, ApprovedState a)
    {
        var folder = Writable(p);
        if (string.IsNullOrWhiteSpace(a.Approval.ApprovedByRole) || string.IsNullOrWhiteSpace(a.Approval.ApprovalRecord))
            throw RglCodesWorkflow.Error(RglCodesWorkflow.ApprovalIncomplete);
        var run = LoadRun(p, a.RunId);
        if (!string.Equals(run.HostName, a.HostName, StringComparison.OrdinalIgnoreCase))
            throw RglCodesWorkflow.Error(RglCodesWorkflow.ApprovedStateMismatch, a.RunId, "hostName");
        var resultsSha = Hashes.Sha256File(RunFile(folder, run.Id) ?? PlantFiles.RunPath(folder, run.HostName, run.Id));
        string Fill(string given, string actual, string field) =>
            string.IsNullOrWhiteSpace(given) ? actual
            : string.Equals(given, actual, StringComparison.OrdinalIgnoreCase) ? actual
            : throw RglCodesWorkflow.Error(RglCodesWorkflow.ApprovedStateMismatch, a.RunId, field);
        var registers = a.Registers == RegisterHashes.Empty ? run.Registers : a.Registers;
        var state = a with
        {
            Schema = SchemaIds.Approved,
            HostName = run.HostName,
            SnapshotSha256 = Fill(a.SnapshotSha256, run.Snapshot.Sha256, "snapshotSha256"),
            ResultsSha256 = Fill(a.ResultsSha256, resultsSha, "resultsSha256"),
            CatalogueVersion = Fill(a.CatalogueVersion, run.Catalogue.Version, "catalogueVersion"),
            Registers = registers,
            Approval = a.Approval with { ApprovedOn = a.Approval.ApprovedOn ?? services.Today },
        };
        var file = PlantFiles.HostFile(Path.Combine(folder, PlantFiles.Approved), run.HostName, ".json");
        using (PlantLock.Acquire(folder, LockTimeout))
        {
            PlantFiles.WriteJson(file, state, out var bytes);
            PlantFiles.WriteApprovedHistory(folder, run.HostName, services.Clock.GetUtcNow(), bytes);
        }
    }

    // ---------------------------------------------------------------- Helfer

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    internal static string Folder(Plant p) => p.Folder ?? throw RglCodesWorkflow.Error(RglCodesWorkflow.NotAPlant, p.Id);

    /// <summary>Anlagenordner zum Schreiben; im Prüfermodus RGL-0401.</summary>
    internal static string Writable(Plant p) =>
        p.ReadOnly ? throw RglCodesWorkflow.Error(RglCodesWorkflow.ReadOnlyPlant) : Folder(p);

    /// <summary>Gruppen und Rechner eindeutig, Gruppenzuordnung bekannt (RGL-0412).</summary>
    private static void CheckPlant(Plant plant)
    {
        if (string.IsNullOrWhiteSpace(plant.Id)) throw RglCodesWorkflow.Error(RglCodesWorkflow.InvalidPlant, "id");
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in plant.Groups)
            if (string.IsNullOrWhiteSpace(g.Id) || !groups.Add(g.Id)) throw RglCodesWorkflow.Error(RglCodesWorkflow.InvalidPlant, "groups/" + g.Id);
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in plant.Machines)
        {
            if (string.IsNullOrWhiteSpace(m.HostName) || !hosts.Add(m.HostName)) throw RglCodesWorkflow.Error(RglCodesWorkflow.InvalidPlant, "machines/" + m.HostName);
            if (m.GroupId is not null && !groups.Contains(m.GroupId)) throw RglCodesWorkflow.Error(RglCodesWorkflow.InvalidPlant, "machines/" + m.HostName + "/groupId");
        }
    }
}
