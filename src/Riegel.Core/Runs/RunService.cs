// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.About;
using Riegel.Core.Comparison;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Plants;
using Riegel.Core.Services;

namespace Riegel.Core.Runs;

/// <summary>
/// Auswerten und speichern (spec 2.7, 2.8, 7.6): Export lesen, Fakten ableiten, Regeln auswerten, Register anwenden,
/// mit dem vorigen Lauf (bzw. dem freigegebenen Stand) vergleichen, Flags der Zeitbasis setzen, werten, speichern.
/// <list type="bullet">
/// <item>Vorheriger Lauf = neuester Lauf eines anderen Exports mit CollectedAtUtc ≤ dem eigenen; derselbe Export
/// erneut ausgewertet ergibt <c>reEvaluationOf</c> (kein neuer Stand).</item>
/// <item>Regression: Vergleichsbasis pass, exception, notApplicable oder outOfScope, jetzt fail – außer der Fail kommt
/// nur von einer abgelaufenen Ausnahme. Regel ohne Ergebnis in der Basis: <c>NewRule</c>.</item>
/// <item>Lauf-Flags: StaleSnapshot (staleDays), ClockSuspect, MachineIdentityChanged, Incomplete (&lt; 90 % prüfbar
/// oder ohne Admin), DomainController.</item>
/// </list>
/// Gleiche Eingaben und gleiche Uhr ergeben eine bytegleiche results.json.
/// </summary>
internal sealed class RunService(RiegelServices services, IEvaluator? evaluator = null) : IRunService
{
    /// <summary>Unter diesem Prüfumfang (Prozent) gilt ein Lauf als unvollständig (spec 2.4 Punkt 9).</summary>
    public const int IncompleteBelow = 90;

    /// <summary>Toleranz der Uhr des Zielrechners gegenüber Import- und Auswertungszeit (spec 2.8).</summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromHours(24);

    private IEvaluator Evaluator => evaluator ?? services.Evaluator;

    public Run EvaluateAndStore(Plant p, string snapshotPath, RunOptions o)
    {
        if (o.Store && p.ReadOnly) throw RglCodesWorkflow.Error(RglCodesWorkflow.ReadOnlyPlant);
        var limits = o.Limits ?? ReadLimits.Default;
        var folder = p.Folder;
        bool store = o.Store && folder is not null;
        Snapshot snapshot;
        FileRef reference;
        if (Directory.Exists(snapshotPath))
        {
            // Bordmittel-Paket: als Export gespeichert, das Paket selbst nie
            snapshot = services.Snapshots.FromBuiltinBundle(snapshotPath, limits);
            var bytes = RiegelJson.SerializeToUtf8Bytes(snapshot);
            var path = store ? PlantFiles.StoreSnapshot(folder!, snapshot, bytes, gzip: false) : null;
            reference = new FileRef(path is null ? SnapshotFile.FileName(snapshot.Machine.HostName, snapshot.CollectedAtUtc) : PlantFiles.Relative(folder!, path)!,
                Hashes.Sha256Hex(bytes));
        }
        else
        {
            snapshot = services.Snapshots.Read(snapshotPath, limits);
            var sha = Hashes.Sha256File(snapshotPath);
            var inside = folder is null ? null : PlantFiles.Relative(folder, snapshotPath);
            if (inside is null && store)
            {
                var stored = PlantFiles.StoreSnapshot(folder!, snapshot, File.ReadAllBytes(snapshotPath),
                    snapshotPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase));
                inside = PlantFiles.Relative(folder!, stored);
            }
            reference = new FileRef(inside ?? Path.GetFileName(snapshotPath), sha);
        }
        var run = Evaluate(snapshot, o with { Plant = p }, reference, limits);
        if (store) services.Plants.SaveRun(p, run);
        return run;
    }

    public Run EvaluateOnly(Snapshot s, RunOptions o) =>
        Evaluate(s, o, new FileRef("", Hashes.Sha256Hex(RiegelJson.SerializeToUtf8Bytes(s))), o.Limits ?? ReadLimits.Default);

    private Run Evaluate(Snapshot s, RunOptions o, FileRef snapshotRef, ReadLimits limits)
    {
        var plant = o.Plant;
        var settings = plant?.Settings ?? PlantSettings.Default;
        var now = services.Clock.GetUtcNow();
        var date = o.EvaluationDate ?? DateOnly.FromDateTime(now.UtcDateTime);
        var host = s.Machine.HostName;
        var (catalogue, baselines) = PlantCatalogue.For(services, plant, o.Catalogue, limits);
        var registers = o.Registers ?? (plant?.Folder is not null ? services.Registers.Load(plant) : null);
        var hashes = registers?.Hashes ?? RegisterHashes.Empty;
        var id = RunIds.Create(now, snapshotRef.Sha256, catalogue.Sha256, hashes);
        var machine = plant?.Machines.FirstOrDefault(m => string.Equals(m.HostName, host, StringComparison.OrdinalIgnoreCase));
        var context = new EvaluationContext(date, now, o.ImportedAtUtc, settings.UpdateMaxDays, settings.UnusedAccountDays,
            machine?.EsuCovered ?? false, o.Language);

        var facts = services.Facts.Derive(s, catalogue);
        var raw = Evaluator.Evaluate(s, catalogue, context);
        IReadOnlyList<RuleResult> results = plant is not null && registers is not null
            ? services.Registers.Apply(raw, registers, plant, host, date)
            : [.. raw.OrderBy(x => x.RuleSetId, StringComparer.Ordinal).ThenBy(x => x.RuleId, StringComparer.Ordinal)];

        // Vorheriger Lauf, erneute Auswertung, freigegebener Stand
        string? reEvaluationOf = null;
        Run? previous = null, approvedRun = null;
        ApprovedState? approved = null;
        if (plant?.Folder is not null)
        {
            var runs = services.Plants.ListRuns(plant, host).Where(r => r.Id != id).ToList();
            var original = runs.Where(r => r.SnapshotSha256 == snapshotRef.Sha256)
                .OrderBy(r => r.EvaluatedAtUtc).ThenBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault();
            reEvaluationOf = original is null ? null : original.ReEvaluationOf ?? original.Id;
            var previousInfo = runs.Where(r => r.SnapshotSha256 != snapshotRef.Sha256 && r.CollectedAtUtc <= s.CollectedAtUtc)
                .OrderBy(r => r.CollectedAtUtc).ThenBy(r => r.EvaluatedAtUtc).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
            previous = previousInfo is null ? null : services.Plants.LoadRun(plant, previousInfo.Id);
            approved = services.Plants.LoadApproved(plant, host);
            approvedRun = approved is null ? null : TryLoadRun(plant, approved.RunId);
        }
        var basis = previous ?? approvedRun;
        results = MarkChanges(results, basis);

        bool stale = date.DayNumber - DateOnly.FromDateTime(s.CollectedAtUtc.UtcDateTime).DayNumber > settings.StaleDays;
        if (stale) results = [.. results.Select(x => x.Status is ResultStatus.Pass or ResultStatus.Fail or ResultStatus.Exception
            ? x with { Flags = x.Flags | ResultFlags.StaleSnapshot } : x)];

        bool isDc = s.Machine.DomainRole is 4 or 5;
        var (summary, categories) = services.Scoring.Score(results, catalogue, basis, isDc);

        var flags = RunFlags.None;
        if (stale) flags |= RunFlags.StaleSnapshot;
        if (ClockSuspect(s, o.ImportedAtUtc, now, catalogue) || results.Any(x => (x.Flags & ResultFlags.ClockSuspect) != 0)) flags |= RunFlags.ClockSuspect;
        if (basis is not null && plant?.Folder is { } folder && BasisSnapshot(folder, basis, limits) is { } before &&
            RunComparer.GuidChanged(before.Machine.MachineGuid, s.Machine.MachineGuid))
            flags |= RunFlags.MachineIdentityChanged;
        if (summary.CheckedShare < IncompleteBelow || !s.IsAdmin) flags |= RunFlags.Incomplete;
        if (isDc) flags |= RunFlags.DomainController;

        return new Run(SchemaIds.Run, id, host, now, date, s.CollectedAtUtc, o.ImportedAtUtc, snapshotRef, AppInfo.Tool(),
            new CatalogueRef(catalogue.Id, catalogue.Version, catalogue.ReleasedOn, catalogue.Sha256),
            [.. baselines.OrderBy(b => b.Id, StringComparer.Ordinal)], hashes, s.IsAdmin, s.Options, results, summary, categories,
            new SortedDictionary<string, JsonElement>(facts.ToDictionary(), StringComparer.Ordinal), previous?.Id, approved?.RunId,
            reEvaluationOf, flags);
    }

    /// <summary>Regression und NewRule gegenüber der Vergleichsbasis (spec 2.7).</summary>
    internal static IReadOnlyList<RuleResult> MarkChanges(IReadOnlyList<RuleResult> results, Run? basis)
    {
        var cleaned = results.Select(x => x with { Flags = x.Flags & ~(ResultFlags.Regression | ResultFlags.NewRule) });
        if (basis is null) return [.. cleaned];
        var before = basis.Results.GroupBy(x => (x.RuleSetId, x.RuleId)).ToDictionary(g => g.Key, g => g.First().Status);
        return
        [
            .. cleaned.Select(x =>
            {
                if (!before.TryGetValue((x.RuleSetId, x.RuleId), out var old)) return x with { Flags = x.Flags | ResultFlags.NewRule };
                bool regression = x.Status == ResultStatus.Fail &&
                    old is ResultStatus.Pass or ResultStatus.Exception or ResultStatus.NotApplicable or ResultStatus.OutOfScope &&
                    (x.Flags & ResultFlags.ExceptionExpired) == 0;
                return regression ? x with { Flags = x.Flags | ResultFlags.Regression } : x;
            }),
        ];
    }

    /// <summary>
    /// Uhr des Zielrechners unplausibel (spec 2.8): mehr als 24 h nach Import oder Auswertung, vor dem letzten Start
    /// oder vor dem Veröffentlichungstag des installierten Builds + UBR.
    /// </summary>
    internal static bool ClockSuspect(Snapshot s, DateTimeOffset? importedAtUtc, DateTimeOffset evaluatedAtUtc, ICatalogue catalogue)
    {
        var collected = s.CollectedAtUtc;
        if (importedAtUtc is { } imported && collected > imported + ClockTolerance) return true;
        if (collected > evaluatedAtUtc + ClockTolerance) return true;
        if (s.Machine.LastBootUtc is { } boot && collected < boot) return true;
        if (s.Machine.Build is { } build && s.Machine.Ubr is { } ubr &&
            catalogue.Builds.Find(build, BuildTable.OsKindOf(s.Machine.ProductType))?.Releases.FirstOrDefault(r => r.Ubr == ubr) is { } release &&
            DateOnly.FromDateTime(collected.UtcDateTime) < release.Date)
            return true;
        return false;
    }

    private Run? TryLoadRun(Plant plant, string runId)
    {
        try { return services.Plants.LoadRun(plant, runId); }
        catch (RiegelException) { return null; }
    }

    private Snapshot? BasisSnapshot(string folder, Run basis, ReadLimits limits)
    {
        // Nur ein geprüfter Export im Anlagenordner, der zum Lauf passt; sonst gilt er als fehlend
        try { return services.Snapshots.Read(PlantFiles.RunSnapshot(folder, basis), limits); }
        catch (RiegelException) { return null; }
    }
}
