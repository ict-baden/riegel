// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.About;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;

namespace Riegel.Core.Runs;

/// <summary>
/// Factory der Auswertung mit Speichern (gehört ab dem Gerüst dem Paket workflow; Signatur fest). Der Platzhalter
/// verbindet die Dienste in der Reihenfolge von spec 7.2: Export lesen, Fakten, Auswertung, Register, Wertung,
/// Lauf speichern. Regressionen, Flags der Zeitbasis und der Vergleich mit dem freigegebenen Stand fehlen noch.
/// </summary>
public static class RunsFactory
{
    public static IRunService Create(RiegelServices services) => new StubRunService(services);
}

/// <summary>Platzhalter (Gerüst).</summary>
internal sealed class StubRunService(RiegelServices services) : IRunService
{
    /// <summary>Unter diesem Prüfumfang (Prozent) gilt ein Lauf als unvollständig (spec 2.4 Punkt 9).</summary>
    public const int IncompleteBelow = 90;

    public Run EvaluateAndStore(Plant p, string snapshotPath, RunOptions o)
    {
        var limits = o.Limits ?? ReadLimits.Default;
        var snapshot = services.Snapshots.Read(snapshotPath, limits);
        var registers = o.Registers ?? (p.Folder is not null ? services.Registers.Load(p) : null);
        var fileSha = Hashes.Sha256File(snapshotPath);
        var relative = p.Folder is { } folder
            ? Path.GetRelativePath(folder, Path.GetFullPath(snapshotPath)).Replace('\\', '/')
            : Path.GetFileName(snapshotPath);
        var run = Evaluate(snapshot, o with { Plant = p, Registers = registers }, new FileRef(relative, fileSha));

        // Vorheriger Lauf: neuester eines anderen Exports; gleicher Export erneut ausgewertet: reEvaluationOf
        if (p.Folder is not null)
        {
            var runs = services.Plants.ListRuns(p, snapshot.Machine.HostName);
            var same = runs.FirstOrDefault(r => r.SnapshotSha256 == fileSha);
            var previous = runs.Where(r => r.SnapshotSha256 != fileSha && r.CollectedAtUtc <= snapshot.CollectedAtUtc).LastOrDefault();
            run = run with { ReEvaluationOf = same?.Id, PreviousRunId = previous?.Id };
        }
        if (o.Store && !p.ReadOnly && p.Folder is not null) services.Plants.SaveRun(p, run);
        return run;
    }

    public Run EvaluateOnly(Snapshot s, RunOptions o) =>
        Evaluate(s, o, new FileRef("", Hashes.Sha256Hex(RiegelJson.SerializeToUtf8Bytes(s))));

    private Run Evaluate(Snapshot s, RunOptions o, FileRef snapshotRef)
    {
        var catalogue = o.Catalogue ?? services.Catalogue.LoadEmbedded();
        var plant = o.Plant;
        var settings = plant?.Settings ?? PlantSettings.Default;
        var now = services.Clock.GetUtcNow();
        var date = o.EvaluationDate ?? DateOnly.FromDateTime(now.UtcDateTime);
        var host = s.Machine.HostName;
        var machine = plant?.Machines.FirstOrDefault(m => string.Equals(m.HostName, host, StringComparison.OrdinalIgnoreCase));
        var context = new EvaluationContext(date, now, o.ImportedAtUtc, settings.UpdateMaxDays, settings.UnusedAccountDays,
            machine?.EsuCovered ?? false, o.Language);

        var facts = services.Facts.Derive(s, catalogue);
        var raw = services.Evaluator.Evaluate(s, catalogue, context);
        var registers = o.Registers;
        var results = plant is not null && registers is not null ? services.Registers.Apply(raw, registers, plant, host, date) : raw;
        bool isDc = s.Machine.DomainRole is 4 or 5;
        var (summary, categories) = services.Scoring.Score(results, catalogue, null, isDc);

        var flags = RunFlags.None;
        if (summary.CheckedShare < IncompleteBelow || !s.IsAdmin) flags |= RunFlags.Incomplete;
        if (isDc) flags |= RunFlags.DomainController;
        if (date.DayNumber - DateOnly.FromDateTime(s.CollectedAtUtc.UtcDateTime).DayNumber > settings.StaleDays) flags |= RunFlags.StaleSnapshot;

        var hashes = registers?.Hashes ?? RegisterHashes.Empty;
        var id = RunIds.Create(now, snapshotRef.Sha256, catalogue.Sha256, hashes);
        return new Run(SchemaIds.Run, id, host, now, date, s.CollectedAtUtc, o.ImportedAtUtc, snapshotRef, AppInfo.Tool(),
            new CatalogueRef(catalogue.Id, catalogue.Version, catalogue.ReleasedOn, catalogue.Sha256), [], hashes, s.IsAdmin, s.Options,
            results, summary, categories, new SortedDictionary<string, JsonElement>(facts.ToDictionary(), StringComparer.Ordinal),
            null, null, null, flags);
    }
}
