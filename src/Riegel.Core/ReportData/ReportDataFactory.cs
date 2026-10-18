// SPDX-License-Identifier: MIT
using Riegel.Core.About;
using Riegel.Core.Localization;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Model.Reports;
using Riegel.Core.Resources;
using Riegel.Core.Services;

// Ordner ReportData/, Namensraum ReportBuilding: ein Namensraum „ReportData“ verdeckte sonst den Typ ReportData.
namespace Riegel.Core.ReportBuilding;

/// <summary>
/// Factory der Berichtsdaten (gehört ab dem Gerüst dem Paket workflow; Signatur fest). Der Platzhalter füllt die
/// Berichtsdaten aus den gespeicherten Läufen in einfacher Form: Kopf, Zählungen, Rechner, offene Befunde je Regel,
/// Freigabe- und Methodenangaben. Vergleich, Register-Sichten, Versicherer-Fakten und Trends kommen mit dem Paket.
/// </summary>
public static class ReportDataFactory
{
    public static IReportDataBuilder Create(RiegelServices services) => new StubReportDataBuilder(services);
}

/// <summary>Platzhalter (Gerüst).</summary>
internal sealed class StubReportDataBuilder(RiegelServices services) : IReportDataBuilder
{
    public ReportData Build(ReportRequest r)
    {
        var plant = r.Plant;
        var catalogue = services.Catalogue.LoadEmbedded();
        var registers = plant.Folder is not null ? services.Registers.Load(plant) : null;
        var now = services.Clock.GetUtcNow();
        var runs = LatestRuns(plant, r.Hosts);
        var machines = runs.Select(run => Machine(plant, run, catalogue, r.Language, r.EvaluationDate)).ToList();
        var all = runs.SelectMany(x => x.Results).ToList();
        var (plantSummary, categories) = services.Scoring.Score(all, catalogue, null, false);
        bool incomplete = runs.Any(x => (x.Flags & RunFlags.Incomplete) != 0);
        var banners = new List<Banner>();
        if (incomplete)
            banners.Add(new Banner(BannerKind.IncompleteCheck, plantSummary.CheckedShare,
                [.. runs.Where(x => (x.Flags & RunFlags.Incomplete) != 0).Select(x => x.HostName)], null, []));
        if (!UiLanguage.IsReviewedReportLanguage(r.Language)) banners.Add(new Banner(BannerKind.PreTranslation, null, [], null, []));

        var findings = all.Where(x => x.Status == ResultStatus.Fail)
            .GroupBy(x => (x.RuleSetId, x.RuleId))
            .Select(g =>
            {
                var rule = catalogue.Find(g.Key.RuleId);
                var hosts = runs.Where(run => run.Results.Any(x => x.RuleId == g.Key.RuleId && x.Status == ResultStatus.Fail))
                    .Select(run => run.HostName).Order(StringComparer.OrdinalIgnoreCase).ToList();
                return new AggregatedFinding(g.Key.RuleSetId, g.Key.RuleId, rule?.Title.Get(r.Language) ?? g.Key.RuleId,
                    rule?.Category ?? "system", g.First().Severity, g.Count(),
                    runs.Count(run => run.Results.Any(x => x.RuleId == g.Key.RuleId && x.Status is not ResultStatus.NotApplicable)),
                    [.. hosts.Take(10)], hosts.Count > 10, null, ResultFlags.None);
            })
            .OrderByDescending(f => f.Severity).ThenByDescending(f => f.OpenCount).ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ToList();

        var reportId = "RPT-" + FileNames.Timestamp(now);
        var hashes = registers?.Hashes ?? RegisterHashes.Empty;
        var header = new ReportHeader(plant.Id, plant.Name, plant.Description, reportId, r.Preset, r.Language, r.EvaluationDate, now,
            AppInfo.Tool(), new CatalogueRef(catalogue.Id, catalogue.Version, catalogue.ReleasedOn, catalogue.Sha256), [], hashes,
            plant.Settings.PerformedBy, null, plant.Settings.Confidentiality ?? CommonStrings.Confidentiality_Default,
            [.. runs.Select(x => x.HostName)], !UiLanguage.IsReviewedReportLanguage(r.Language));
        var counts = new ReportCounts(runs.Count, runs.Count(x => x.Summary.Score is not null), plantSummary.OpenHigh,
            plantSummary.OpenMedium, plantSummary.OpenLow, 0, 0, plantSummary.NotCheckable, 0, 0, 0, 0,
            registers?.Tailoring.Entries.Count ?? 0, registers?.Measures.Measures.Count(m => m.State == MeasureState.Planned) ?? 0, 0, 0,
            runs.Count(x => (x.Flags & RunFlags.StaleSnapshot) != 0), runs.Count(x => (x.Flags & RunFlags.DomainController) != 0));
        var score = new ScoreView(plantSummary.Score, plantSummary.Documented, plantSummary.CheckedShare, incomplete,
            categories.Any(c => c.Capped));
        var signOff = new SignOffData(plant.Settings.SignOff, hashes.Exceptions, registers?.Exceptions.Exceptions.Count ?? 0, 0,
            hashes.Tailoring, registers?.Tailoring.Entries.Count ?? 0, hashes.Measures, registers?.Measures.Measures.Count ?? 0,
            [.. runs.Select(x => x.Id)], reportId);
        return new ReportData(header, banners, counts, score, categories, findings, [], [], [], [], [], machines, [], [], signOff,
            Method(catalogue));
    }

    public SummaryData BuildSummary(IReadOnlyList<Plant> plants, DateOnly d, string lang)
    {
        var catalogue = services.Catalogue.LoadEmbedded();
        var rows = plants.Select(plant =>
        {
            var runs = LatestRuns(plant, null);
            var (summary, _) = services.Scoring.Score([.. runs.SelectMany(x => x.Results)], catalogue, null, false);
            var trend = Enumerable.Range(1, 4).Reverse()
                .Select(i => new TrendPoint(new DateOnly(d.Year, d.Month, 1).AddMonths(-i + 1).AddDays(-1), null)).ToList();
            return new SummaryPlantRow(plant.Id, plant.Name, runs.Count, null, summary.OpenHigh, trend, 0, 0, 0, summary.Score,
                summary.Documented, summary.CheckedShare, runs.Any(x => (x.Flags & RunFlags.Incomplete) != 0));
        }).ToList();
        return new SummaryData(d, lang, services.Clock.GetUtcNow(), AppInfo.Tool(), rows, [], [], CommonStrings.Confidentiality_Default,
            !UiLanguage.IsReviewedReportLanguage(lang));
    }

    public TargetConfigData BuildTargetConfig(Plant p, string? groupId, string lang)
    {
        var catalogue = services.Catalogue.LoadEmbedded();
        var registers = p.Folder is not null ? services.Registers.Load(p) : null;
        var rules = catalogue.Rules.Where(x => x.State == RuleState.Active)
            .Select(x => new TargetConfigRule(x.RuleSetId, x.Id, x.Title.Get(lang), x.Category, x.Severity, x.Kind, x.Check.Type,
                [.. x.Verified.Select(v => v.Src).Distinct(StringComparer.Ordinal)], x.References.Any(rf => rf.Src == "MSB"), null))
            .ToList();
        var groups = p.Groups.Where(g => groupId is null || g.Id == groupId)
            .Select(g => new TargetConfigGroup(g.Id, g.Name, g.Role,
                [.. p.Machines.Where(m => m.GroupId == g.Id).Select(m => m.HostName)], rules, []))
            .ToList();
        if (groupId is null)
            groups.Add(new TargetConfigGroup(null, CommonStrings.Group_None, null,
                [.. p.Machines.Where(m => m.GroupId is null).Select(m => m.HostName)], rules, []));
        return new TargetConfigData(p.Id, p.Name, lang, services.Today, services.Clock.GetUtcNow(), AppInfo.Tool(),
            new CatalogueRef(catalogue.Id, catalogue.Version, catalogue.ReleasedOn, catalogue.Sha256), groups, [],
            Provenance(catalogue), p.Settings.SignOff, registers?.Hashes.Tailoring ?? "", p.Settings.Confidentiality ?? CommonStrings.Confidentiality_Default,
            !UiLanguage.IsReviewedReportLanguage(lang));
    }

    public ApprovalSheetData BuildApprovalSheet(Plant p, IReadOnlyList<string>? ids, string lang)
    {
        var catalogue = services.Catalogue.LoadEmbedded();
        var registers = p.Folder is not null ? services.Registers.Load(p) : null;
        bool Selected(string id, Approval a) => ids is null ? a.ApprovalRecord is null : ids.Contains(id, StringComparer.OrdinalIgnoreCase);
        var entries = new List<ApprovalSheetEntry>();
        foreach (var e in registers?.Exceptions.Exceptions ?? [])
        {
            if (!Selected(e.Id, e.Approval)) continue;
            var rule = catalogue.Find(e.RuleId);
            entries.Add(new ApprovalSheetEntry(RegisterKind.Exceptions, e.Id, e.RuleId, rule?.Title.Get(lang) ?? e.RuleId, rule?.Severity,
                e.Scope, e.Category, e.Justification, e.CompensatingMeasure, e.Reference, e.AcceptedValue, null, ContentHashes.Of(e),
                RegisterEntryState.Pending));
        }
        foreach (var t in registers?.Tailoring.Entries ?? [])
        {
            if (!Selected(t.Id, t.Approval)) continue;
            var rule = catalogue.Find(t.RuleId);
            entries.Add(new ApprovalSheetEntry(RegisterKind.Tailoring, t.Id, t.RuleId, rule?.Title.Get(lang) ?? t.RuleId, rule?.Severity,
                t.Scope, null, t.Reason, null, null, null, null, ContentHashes.Of(t), RegisterEntryState.Pending));
        }
        var now = services.Clock.GetUtcNow();
        return new ApprovalSheetData(p.Id, p.Name, lang, services.Today, now, AppInfo.Tool(), "FB-" + FileNames.Timestamp(now),
            registers?.Hashes.Exceptions ?? "", registers?.Hashes.Tailoring ?? "", entries, p.Settings.SignOff,
            p.Settings.Confidentiality ?? CommonStrings.Confidentiality_Default, !UiLanguage.IsReviewedReportLanguage(lang));
    }

    public CollectionRecordData BuildCollectionRecord(Plant p, string importId, string lang)
    {
        var folder = Path.Combine(p.Folder ?? throw RiegelException.Common(RglCodesCommon.NotFound, p.Id), "imports");
        string? file = importId == "latest"
            ? (Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.import.json").Order(StringComparer.Ordinal).LastOrDefault() : null)
            : Path.Combine(folder, FileNames.Token(importId) + ".import.json");
        if (file is null || !File.Exists(file)) throw RiegelException.Common(RglCodesCommon.NotFound, importId);
        var record = RiegelJson.ReadFile<ImportRecord>(file, ReadLimits.Default.MaxPlantFileBytes);
        return new CollectionRecordData(p.Id, p.Name, lang, services.Clock.GetUtcNow(), AppInfo.Tool(), record,
            p.Settings.Confidentiality ?? CommonStrings.Confidentiality_Default, !UiLanguage.IsReviewedReportLanguage(lang));
    }

    private List<Run> LatestRuns(Plant plant, IReadOnlyList<string>? hosts)
    {
        if (plant.Folder is null) return [];
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in plant.Machines) names.Add(m.HostName);
        var runsFolder = Path.Combine(plant.Folder, "runs");
        if (Directory.Exists(runsFolder))
            foreach (var dir in Directory.EnumerateDirectories(runsFolder)) names.Add(Path.GetFileName(dir));
        var result = new List<Run>();
        foreach (var host in names)
        {
            if (hosts is not null && !hosts.Contains(host, StringComparer.OrdinalIgnoreCase)) continue;
            var latest = services.Plants.ListRuns(plant, host).LastOrDefault();
            if (latest is not null) result.Add(services.Plants.LoadRun(plant, latest.Id));
        }
        return result;
    }

    private static MachineReport Machine(Plant plant, Run run, ICatalogue catalogue, string language, DateOnly date)
    {
        var entry = plant.Machines.FirstOrDefault(m => string.Equals(m.HostName, run.HostName, StringComparison.OrdinalIgnoreCase));
        var group = plant.Groups.FirstOrDefault(g => g.Id == entry?.GroupId);
        var results = run.Results.Select(x =>
        {
            var rule = catalogue.Find(x.RuleId);
            return new RuleResultView(x, rule?.Title.Get(language) ?? x.RuleId, rule?.Category ?? "system", rule?.Kind ?? RuleKind.Check,
                rule?.Rationale.Get(language), rule?.OtNote?.Get(language), rule?.Remedy?.Get(language), []);
        }).ToList();
        var collection = new CollectionInfo(run.CollectedAtUtc, date.DayNumber - DateOnly.FromDateTime(run.CollectedAtUtc.UtcDateTime).DayNumber,
            run.ImportedAtUtc, run.IsAdmin, run.Options, SnapshotOrigin.Collector, "", "", null, run.Snapshot.Sha256, null);
        var machine = new MachineInfo(run.HostName, null, null, null, null, null, null, null, null, null, null, null, null);
        return new MachineReport(run.HostName, entry?.Label, entry?.GroupId, group?.Name, group?.Role, entry?.Identity, machine, collection,
            run.Id, run.Summary, run.Categories, results, [], DriftSummary.None, [], [], [], null, run.Flags);
    }

    private static MethodInfo Method(ICatalogue catalogue) =>
        new(RuleSources.All, catalogue.Rules.Count, catalogue.Rules.Count(x => x.Kind == RuleKind.Check),
            catalogue.Rules.Count(x => x.Kind == RuleKind.Inventory), Provenance(catalogue), Frameworks.SupportedEvidence, false);

    private static ProvenanceCounts Provenance(ICatalogue catalogue)
    {
        var checks = catalogue.Rules.Where(x => x.Kind == RuleKind.Check && x.State == RuleState.Active).ToList();
        int By(string src) => checks.Count(x => x.Verified.Any(v => v.Src == src));
        return new ProvenanceCounts(checks.Count, By("ACSC"), By("MSDOCS-CCBY"), By("MSLEARN-FACT"), By("DISA"),
            checks.Count(x => x.References.Any(rf => rf.Src == "MSB")));
    }
}
