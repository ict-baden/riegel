// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.RegularExpressions;
using Riegel.Core.About;
using Riegel.Core.Comparison;
using Riegel.Core.Localization;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Model.Reports;
using Riegel.Core.Plants;
using Riegel.Core.Registers;
using Riegel.Core.Scoring;
using Riegel.Core.Services;

namespace Riegel.Core.ReportBuilding;

/// <summary>
/// Berichtsdaten aller Dokumente (spec 5): Anlagenbericht/Excel/CSV (<see cref="Build"/>), Übersicht für die
/// Geschäftsführung (<see cref="BuildSummary"/>), Soll-Konfiguration, Genehmigungsblatt und Erhebungsprotokoll. Quelle
/// sind die gespeicherten Läufe (je Rechner der maßgebliche bis zum Stichtag), die Register und die Exporte im
/// Anlagenordner; Texte aus Daten stehen in der Berichtssprache.
/// </summary>
internal sealed partial class ReportDataBuilder(RiegelServices services, ICatalogue? catalogue = null) : IReportDataBuilder
{
    /// <summary>Höchstens so viele Rechner je Befund (spec 5 O4 Abschnitt 4), dann „…“.</summary>
    public const int MaxHostsPerFinding = 10;

    /// <summary>Abweichungen je Rechner im Anhang (spec 5 O4 Anhang A).</summary>
    public const int DriftTop = 20;

    /// <summary>Katalog älter als 6 Monate (183 Tage) → Banner (spec 2.8).</summary>
    public const int StaleCatalogueDays = 183;

    private const int IncompleteBelow = Runs.RunService.IncompleteBelow;

    private readonly IRunComparer _comparer = services.Comparison;

    // ================================================================ Anlagenbericht

    public ReportData Build(ReportRequest r)
    {
        var language = r.Language;
        var date = r.EvaluationDate;
        var view = PlantView.Load(services, r.Plant, date, r.Hosts, catalogue);
        var plant = view.Plant;
        var now = services.Clock.GetUtcNow();
        var reportId = "RPT-" + FileNames.Timestamp(now);
        var cat = view.Catalogue;
        var registers = view.Registers;
        var views = RegisterViews.Build(view, date, language);

        // Vergleiche je Rechner: mit dem vorigen Lauf und mit dem freigegebenen Stand
        var comparisons = new List<(Run Run, RunComparison Comparison)>();
        foreach (var run in view.Runs)
        {
            // fehlt der Export im Anlagenordner, gibt es Statusänderungen, aber keine Rohwert-Abweichungen
            var snapshot = view.Snapshot(run);
            var target = snapshot ?? Placeholder(run);
            if (view.Run(run.PreviousRunId) is { } previous)
                comparisons.Add((run, _comparer.Compare(previous, snapshot is null ? null : view.Snapshot(previous), run, target, ComparisonBasis.Previous)));
            var (_, approvedRun) = view.Approved(run.HostName);
            if (approvedRun is not null && approvedRun.Id != run.Id)
                comparisons.Add((run, _comparer.Compare(approvedRun, snapshot is null ? null : view.Snapshot(approvedRun), run, target, ComparisonBasis.Approved)));
        }
        var changes = comparisons.SelectMany(c => HostChanges(c.Run, c.Comparison, cat, language)).Where(Reportable).OrderBy(Order).ToList();

        var machines = view.Runs.Select(run => Machine(view, run, comparisons.Where(c => c.Run.Id == run.Id).Select(c => c.Comparison).ToList(),
            changes, date, language)).ToList();
        var insurer = view.Runs.Zip(machines, (run, m) => MachineFacts.Insurer(run, m.Machine, view.Snapshot(run), m.Lifecycle, cat)).ToList();

        // Werte der Anlage: Deckel je Rechner, dann addiert; Domänencontroller ohne Wert
        var scored = view.Runs.Where(x => (x.Flags & RunFlags.DomainController) == 0).Select(x => ScoreCalculator.Totals.Of(x.Results, cat)).ToList();
        var plantTotals = ScoreCalculator.Totals.Sum(scored);
        var allTotals = ScoreCalculator.Totals.Sum(view.Runs.Select(x => ScoreCalculator.Totals.Of(x.Results, cat)));
        int share = allTotals.CheckedShare;
        bool incomplete = view.Runs.Any(x => (x.Flags & RunFlags.Incomplete) != 0) || (view.Runs.Count > 0 && share < IncompleteBelow);

        var banners = Banners(view, cat, date, language, share, incomplete, plantTotals);
        var findings = Findings(view.Runs, cat, registers, date, language);
        var exceptionViews = views.Exceptions;
        var counts = new ReportCounts(view.Runs.Count, view.Runs.Count(x => x.Summary.Score is not null),
            view.Runs.Sum(x => x.Summary.OpenHigh), view.Runs.Sum(x => x.Summary.OpenMedium), view.Runs.Sum(x => x.Summary.OpenLow),
            view.Runs.Sum(x => x.Summary.Regressions), view.Runs.Sum(x => x.Summary.NewlyOpen), view.Runs.Sum(x => x.Summary.NotCheckable),
            exceptionViews.Count(e => RegisterRules.InForce(e.State)), exceptionViews.Count(e => e.State == RegisterEntryState.Expiring),
            exceptionViews.Count(e => e.State == RegisterEntryState.Expired),
            exceptionViews.Count(e => e.State is RegisterEntryState.Pending or RegisterEntryState.ChangedAfterApproval),
            registers.Tailoring.Entries.Count, registers.Measures.Measures.Count(m => m.State == MeasureState.Planned), views.Measures.Count(m => m.Overdue),
            machines.Count(m => m.Lifecycle?.Supported == false), view.Runs.Count(x => (x.Flags & RunFlags.StaleSnapshot) != 0),
            view.Runs.Count(x => (x.Flags & RunFlags.DomainController) != 0));
        var score = new ScoreView(plantTotals.Score, plantTotals.Documented, share, incomplete, view.Runs.Any(x => x.Categories.Any(c => c.Capped)) || plantTotals.Capped);

        var header = new ReportHeader(plant.Id, plant.Name, plant.Description, reportId, r.Preset, language, date, now, AppInfo.Tool(),
            CatalogueRef(cat), view.Baselines, registers.Hashes, plant.Settings.PerformedBy, LogoPath(plant),
            plant.Settings.Confidentiality ?? WorkflowText.Common("Confidentiality_Default", language), [.. view.Runs.Select(x => x.HostName)],
            !UiLanguage.IsReviewedReportLanguage(language));
        var signOff = new SignOffData(SignOffRoles(plant, r.Preset, language), registers.Hashes.Exceptions, registers.Exceptions.Exceptions.Count,
            exceptionViews.Count(e => RegisterRules.InForce(e.State)), registers.Hashes.Tailoring, registers.Tailoring.Entries.Count,
            registers.Hashes.Measures, registers.Measures.Measures.Count, [.. view.Runs.Select(x => x.Id)], reportId);
        var method = Method(cat, view.Runs.Any(x => x.Results.Any(y => y.Source == ValueSource.DomainGpo)));
        var baselines = view.Baselines.Select(b => BaselineSection(b, view.Runs, cat, r.IncludeBaselineContent)).ToList();

        return new ReportData(header, banners, counts, score, plantTotals.Categories(noScore: false), findings, changes,
            [.. exceptionViews.Where(e => e.State == RegisterEntryState.Expired)], [.. exceptionViews.Where(e => e.State != RegisterEntryState.Expired)],
            views.Tailoring, views.Measures, machines, insurer, baselines, signOff, method);
    }

    /// <summary>Leerer Export mit dem Kopf aus dem Lauf, wenn die Datei fehlt (nur für den Statusvergleich).</summary>
    private static Snapshot Placeholder(Run run) =>
        new(SchemaIds.Snapshot, new CollectorInfo("", "", null, "", false), run.CollectedAtUtc, run.IsAdmin, SnapshotOrigin.Collector, run.Options,
            MachineFacts.Header(run, null), [], []);

    // ---------------------------------------------------------------- Banner

    private static List<Banner> Banners(PlantView view, ICatalogue cat, DateOnly date, string language, int share, bool incomplete,
        ScoreCalculator.Totals totals)
    {
        var banners = new List<Banner>();
        var runs = view.Runs;
        List<string> Hosts(RunFlags flag) => [.. runs.Where(x => (x.Flags & flag) != 0).Select(x => x.HostName)];
        if (incomplete)
        {
            var affected = runs.Where(x => (x.Flags & RunFlags.Incomplete) != 0).ToList();
            var areas = (affected.Count > 0 ? affected : runs).SelectMany(x => x.Results)
                .Where(x => x.RuleSetId == ScoreCalculator.OwnRuleSet && x.Status == ResultStatus.NotCheckable)
                .GroupBy(x => ScoreCalculator.CategoryOf(x, cat))
                .OrderByDescending(g => g.Count()).ThenBy(g => IndexOf(g.Key))
                .Select(g => g.Key).ToList();
            banners.Add(new Banner(BannerKind.IncompleteCheck, share, [.. affected.Select(x => x.HostName)], null, areas));
        }
        var stale = runs.Where(x => (x.Flags & RunFlags.StaleSnapshot) != 0).ToList();
        if (stale.Count > 0)
            banners.Add(new Banner(BannerKind.StaleSnapshot, null, [.. stale.Select(x => x.HostName)],
                stale.Max(x => date.DayNumber - DateOnly.FromDateTime(x.CollectedAtUtc.UtcDateTime).DayNumber), []));
        if (date.DayNumber - cat.ReleasedOn.DayNumber > StaleCatalogueDays)
            banners.Add(new Banner(BannerKind.StaleCatalogue, null, [], date.DayNumber - cat.ReleasedOn.DayNumber, []));
        if (!UiLanguage.IsReviewedReportLanguage(language)) banners.Add(new Banner(BannerKind.PreTranslation, null, [], null, []));
        var gpo = runs.Where(x => x.Results.Any(y => y.Source == ValueSource.DomainGpo)).Select(x => x.HostName).ToList();
        if (gpo.Count > 0) banners.Add(new Banner(BannerKind.ExperimentalGpoAttribution, null, gpo, null, []));
        if (Hosts(RunFlags.ClockSuspect) is { Count: > 0 } clock) banners.Add(new Banner(BannerKind.ClockSuspect, null, clock, null, []));
        if (Hosts(RunFlags.DomainController) is { Count: > 0 } dcs) banners.Add(new Banner(BannerKind.DomainController, null, dcs, null, []));
        return banners;
    }

    private static int IndexOf(string category)
    {
        for (int i = 0; i < RuleCategories.All.Count; i++)
            if (RuleCategories.All[i] == category) return i;
        return int.MaxValue;
    }

    // ---------------------------------------------------------------- Befunde

    /// <summary>Offene Befunde je Regel über alle Rechner, nach Schwere und Häufigkeit (spec 5 O4 Abschnitt 4).</summary>
    internal static List<AggregatedFinding> Findings(IReadOnlyList<Run> runs, ICatalogue cat, RegisterSet registers, DateOnly date, string language)
    {
        var failing = runs.SelectMany(run => run.Results.Where(x => x.Status == ResultStatus.Fail).Select(x => (Run: run, Result: x)))
            .GroupBy(x => (x.Result.RuleSetId, x.Result.RuleId));
        var list = new List<AggregatedFinding>();
        foreach (var g in failing)
        {
            var rule = cat.Find(g.Key.RuleId);
            var hosts = g.Select(x => x.Run.HostName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
            int applicable = runs.Count(run => run.Results.Any(x => x.RuleSetId == g.Key.RuleSetId && x.RuleId == g.Key.RuleId &&
                x.Status is not (ResultStatus.NotApplicable or ResultStatus.OutOfScope or ResultStatus.Info)));
            var measure = g.Select(x => x.Result.MeasureId).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(id => registers.Measures.Measures.FirstOrDefault(m => m.Id == id)).OfType<MeasureEntry>()
                .OrderBy(m => m.State == MeasureState.Planned ? 0 : 1).ThenBy(m => m.DueDate).FirstOrDefault();
            var flags = g.Aggregate(ResultFlags.None, (f, x) => f | x.Result.Flags);
            list.Add(new AggregatedFinding(g.Key.RuleSetId, g.Key.RuleId, rule?.Title.Get(language) ?? g.Key.RuleId,
                rule?.Category ?? "system", rule?.Severity ?? g.First().Result.Severity, hosts.Count, applicable, [.. hosts.Take(MaxHostsPerFinding)],
                hosts.Count > MaxHostsPerFinding,
                measure is null ? null : new MeasureSummary(measure.Id, measure.OwnerRole, measure.DueDate, RegisterRules.IsPastDue(measure, date)), flags));
        }
        return [.. list.OrderByDescending(f => f.Severity).ThenByDescending(f => f.OpenCount).ThenBy(f => f.RuleSetId, StringComparer.Ordinal)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)];
    }

    // ---------------------------------------------------------------- Änderungen

    private static IEnumerable<HostChange> HostChanges(Run run, RunComparison comparison, ICatalogue cat, string language) =>
        comparison.StatusChanges.Select(c =>
        {
            var rule = cat.Find(c.RuleId);
            var severity = rule?.Severity ?? run.Results.FirstOrDefault(x => x.RuleId == c.RuleId)?.Severity ?? Severity.Low;
            return new HostChange(run.HostName, comparison.Basis, comparison.FromRunId, comparison.ToRunId, rule?.Title.Get(language) ?? c.RuleId,
                severity, c);
        });

    /// <summary>Neue Regeln nur, wenn sie offen sind; sonst jede Änderung (Excel-Blatt „Changes“, PDF filtert „Verschlechtert“).</summary>
    private static bool Reportable(HostChange c) => c.Change.Kind != StatusChangeKind.NewRule || c.Change.To == ResultStatus.Fail;

    /// <summary>„Verschlechtert“: schlechter, Ausnahme oder Zuschnitt weggefallen, neue offene Regel (abgelaufene Ausnahmen haben einen eigenen Block).</summary>
    internal static bool IsWorse(StatusChange c) =>
        c.Kind is StatusChangeKind.Worse or StatusChangeKind.ExceptionRemoved or StatusChangeKind.TailoringRemoved ||
        (c.Kind == StatusChangeKind.NewRule && c.To == ResultStatus.Fail);

    private static (int, int, int, string, string, string) Order(HostChange c) =>
        ((int)c.Basis, KindOrder(c.Change.Kind), -(int)c.Severity, c.HostName.ToUpperInvariant(), c.Change.RuleSetId, c.Change.RuleId);

    private static int KindOrder(StatusChangeKind kind) => kind switch
    {
        StatusChangeKind.Worse => 0,
        StatusChangeKind.ExceptionRemoved => 1,
        StatusChangeKind.TailoringRemoved => 2,
        StatusChangeKind.NewRule => 3,
        StatusChangeKind.ExceptionExpired => 4,
        StatusChangeKind.ScopeChanged => 5,
        StatusChangeKind.ExceptionAdded => 6,
        StatusChangeKind.TailoringAdded => 7,
        StatusChangeKind.Better => 8,
        _ => 9,
    };

    // ---------------------------------------------------------------- Rechner

    private static MachineReport Machine(PlantView view, Run run, IReadOnlyList<RunComparison> comparisons, IReadOnlyList<HostChange> changes,
        DateOnly date, string language)
    {
        var cat = view.Catalogue;
        var entry = view.Machine(run.HostName);
        var group = view.GroupOf(run.HostName);
        var snapshot = view.Snapshot(run);
        var machine = MachineFacts.Header(run, snapshot);
        var results = run.Results.Select(x =>
        {
            var rule = cat.Find(x.RuleId);
            return new RuleResultView(x, rule?.Title.Get(language) ?? x.RuleId, rule?.Category ?? "system",
                rule?.Kind ?? (x.Status == ResultStatus.Info ? RuleKind.Inventory : RuleKind.Check), rule?.Rationale.Get(language),
                rule?.OtNote?.Get(language), rule?.Remedy?.Get(language), rule is null ? [] : [.. rule.References.Select(Reference)]);
        }).ToList();
        var collection = new CollectionInfo(run.CollectedAtUtc, date.DayNumber - DateOnly.FromDateTime(run.CollectedAtUtc.UtcDateTime).DayNumber,
            run.ImportedAtUtc, run.IsAdmin, run.Options, snapshot?.Origin ?? SnapshotOrigin.Collector, snapshot?.Collector.Name ?? "",
            snapshot?.Collector.Version ?? "", snapshot?.Collector.ExeSha256, run.Snapshot.Sha256, view.ImportOf(run));
        var worse = changes.Where(c => string.Equals(c.HostName, run.HostName, StringComparison.OrdinalIgnoreCase) && c.ToRunId == run.Id && IsWorse(c.Change))
            .ToList();
        var driftSource = comparisons.FirstOrDefault(c => c.Basis == ComparisonBasis.Previous) ?? comparisons.FirstOrDefault();
        var drift = driftSource is null ? DriftSummary.None : new DriftSummary(driftSource.Drift.Count, [.. driftSource.Drift.Take(DriftTop)]);
        var facts = run.Facts.Where(f => !f.Key.StartsWith("net.", StringComparison.Ordinal))
            .Select(f => new FactView(f.Key, CanonicalValue.Render(f.Value))).ToList();
        var network = run.Facts.Where(f => f.Key.StartsWith("net.", StringComparison.Ordinal))
            .Select(f => new FactView(f.Key, CanonicalValue.Render(f.Value))).ToList();
        var inventory = run.Results.Where(x => x.Status == ResultStatus.Info)
            .Select(x => (Result: x, Rule: cat.Find(x.RuleId)))
            .Where(x => x.Rule?.Check is InventoryCheck)
            .Select(x => new InventoryView(x.Result.RuleId, x.Rule!.Title.Get(language), ((InventoryCheck)x.Rule.Check).Renderer, x.Result.Details ?? []))
            .ToList();
        var lifecycle = MachineFacts.Lifecycle(run, machine, cat, entry?.EsuCovered ?? false, date);
        return new MachineReport(run.HostName, entry?.Label, group?.Id ?? entry?.GroupId, group?.Name, group?.Role, entry?.Identity, machine,
            collection, run.Id, run.Summary, run.Categories, results, worse, drift, facts, network, inventory, lifecycle, run.Flags);
    }

    /// <summary>Verweis als eine Zeile, z. B. „ACSC: Hardening … (2026-01), Server Message Block sessions“ oder „ATTACK: M1037“.</summary>
    internal static string Reference(RuleReference r)
    {
        var parts = new[] { r.Doc, r.Section, r.Setting, r.Id, r.Ids is { Count: > 0 } ids ? string.Join(", ", ids) : null, r.Version }
            .Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (parts.Count == 0 && !string.IsNullOrWhiteSpace(r.Url)) parts.Add(r.Url);
        return parts.Count == 0 ? r.Src : r.Src + ": " + string.Join(", ", parts);
    }

    // ---------------------------------------------------------------- Freigabe, Methode, Baselines

    /// <summary>Rollen der Freigabe; Preset asDelivered ohne eigene Übernahme-Rolle: Hersteller erstellt / geprüft, Betreiber übernommen (spec 2.17).</summary>
    private static IReadOnlyList<SignOffRole> SignOffRoles(Plant plant, ReportPreset preset, string language)
    {
        if (preset == ReportPreset.AsDelivered && !plant.Settings.SignOff.Any(s => s.Step == SignOffSteps.Accepted))
        {
            var builder = WorkflowText.Get("Role_Builder", language);
            return
            [
                new(SignOffSteps.Created, builder), new(SignOffSteps.Reviewed, builder),
                new(SignOffSteps.Accepted, WorkflowText.Get("Role_Operator", language)),
            ];
        }
        return plant.Settings.SignOff;
    }

    private static MethodInfo Method(ICatalogue cat, bool experimentalGpo)
    {
        var own = cat.Rules.Where(x => x.RuleSetId == ScoreCalculator.OwnRuleSet && x.State == RuleState.Active).ToList();
        return new MethodInfo(RuleSources.All, own.Count, own.Count(x => x.Kind == RuleKind.Check), own.Count(x => x.Kind == RuleKind.Inventory),
            Provenance(cat), Frameworks.SupportedEvidence, experimentalGpo);
    }

    /// <summary>„Herkunft der Sollwerte“ aus verified[] der aktiven check-Regeln des eigenen Katalogs (spec 5 O13).</summary>
    internal static ProvenanceCounts Provenance(ICatalogue cat)
    {
        var checks = cat.Rules.Where(x => x.RuleSetId == ScoreCalculator.OwnRuleSet && x.Kind == RuleKind.Check && x.State == RuleState.Active).ToList();
        int By(string src) => checks.Count(x => x.Verified.Any(v => string.Equals(v.Src, src, StringComparison.Ordinal)));
        return new ProvenanceCounts(checks.Count, By("ACSC"), By("MSDOCS-CCBY"), By("MSLEARN-FACT"), By("DISA"),
            checks.Count(x => x.References.Any(rf => string.Equals(rf.Src, "MSB", StringComparison.Ordinal))));
    }

    private static BaselineSection BaselineSection(BaselineRef b, IReadOnlyList<Run> runs, ICatalogue cat, bool contentIncluded)
    {
        var results = runs.SelectMany(x => x.Results).Where(x => x.RuleSetId == b.Id).ToList();
        decimal pass = results.Where(x => x.Status == ResultStatus.Pass).Sum(x => ScoreCalculator.Weight(x.Severity));
        decimal considered = results.Where(x => x.Status is ResultStatus.Pass or ResultStatus.Fail or ResultStatus.Exception).Sum(x => ScoreCalculator.Weight(x.Severity));
        return new BaselineSection(b, results.Count(x => x.Status == ResultStatus.Pass), results.Count(x => x.Status == ResultStatus.Fail),
            results.Count(x => x.Status == ResultStatus.Exception), results.Count(x => x.Status == ResultStatus.NotCheckable),
            considered > 0 ? ScoreCalculator.Totals.Percent(pass, considered) : null, contentIncluded);
    }

    private static CatalogueRef CatalogueRef(ICatalogue c) => new(c.Id, c.Version, c.ReleasedOn, c.Sha256);

    /// <summary>Logo nur aus dem Anlagenordner, höchstens 1 MB, PNG oder JPEG (spec 5 O4, 7.12); sonst keins.</summary>
    internal static string? LogoPath(Plant plant)
    {
        if (plant.Folder is not { } folder || string.IsNullOrWhiteSpace(plant.Settings.CoverLogoFile)) return null;
        var path = Path.GetFullPath(Path.Combine(folder, plant.Settings.CoverLogoFile));
        if (PlantFiles.Relative(folder, path) is null || !File.Exists(path)) return null;
        var info = new FileInfo(path);
        if (info.Length is 0 || info.Length > ReadLimits.Default.MaxLogoBytes) return null;
        Span<byte> head = stackalloc byte[8];
        using (var stream = info.OpenRead())
            if (stream.Read(head) < 8) return null;
        bool png = head.SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        bool jpeg = head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF;
        return png || jpeg ? path : null;
    }

    // ================================================================ Übersicht (O6)

    public SummaryData BuildSummary(IReadOnlyList<Plant> plants, DateOnly d, string lang)
    {
        var monthEnds = MonthEnds(d, 4);
        var rows = new List<SummaryPlantRow>();
        var banners = new List<Banner>();
        var allRuns = new List<(Run Run, ICatalogue Catalogue, RegisterSet Registers)>();
        var incompleteHosts = new List<string>();
        var staleHosts = new List<string>();
        int staleAge = 0;
        var catalogues = new List<ICatalogue>();
        foreach (var plant in plants)
        {
            var view = PlantView.Load(services, plant, d, null, catalogue);
            var cat = view.Catalogue;
            catalogues.Add(cat);
            var views = RegisterViews.Build(view, d, lang);
            var scored = ScoreCalculator.Totals.Sum(view.Runs.Where(x => (x.Flags & RunFlags.DomainController) == 0).Select(x => ScoreCalculator.Totals.Of(x.Results, cat)));
            var all = ScoreCalculator.Totals.Sum(view.Runs.Select(x => ScoreCalculator.Totals.Of(x.Results, cat)));
            var supported = view.Runs.Select(run => MachineFacts.Lifecycle(run, MachineFacts.Header(run, view.Snapshot(run)), cat,
                view.Machine(run.HostName)?.EsuCovered ?? false, d)?.Supported).Where(s => s is not null).ToList();
            bool incomplete = view.Runs.Any(x => (x.Flags & RunFlags.Incomplete) != 0) || (view.Runs.Count > 0 && all.CheckedShare < IncompleteBelow);
            var trend = monthEnds.Select(end =>
            {
                var latest = PlantView.Latest(view.AllRuns, end, null).ToList();
                return new TrendPoint(end, latest.Count == 0 ? null : latest.Sum(x => x.OpenHigh));
            }).ToList();
            rows.Add(new SummaryPlantRow(plant.Id, plant.Name, view.Runs.Count,
                supported.Count == 0 ? null : ScoreCalculator.Totals.Percent(supported.Count(s => s == true), supported.Count),
                view.Runs.Sum(x => x.Summary.OpenHigh), trend, view.Runs.Sum(x => x.Summary.Regressions),
                views.Exceptions.Count(e => e.State == RegisterEntryState.Expiring), views.Measures.Count(m => m.Overdue),
                scored.Score, scored.Documented, all.CheckedShare, incomplete));
            foreach (var run in view.Runs) allRuns.Add((run, cat, view.Registers));
            if (incomplete) incompleteHosts.AddRange(view.Runs.Where(x => (x.Flags & RunFlags.Incomplete) != 0).Select(x => x.HostName));
            foreach (var run in view.Runs.Where(x => (x.Flags & RunFlags.StaleSnapshot) != 0))
            {
                staleHosts.Add(run.HostName);
                staleAge = Math.Max(staleAge, d.DayNumber - DateOnly.FromDateTime(run.CollectedAtUtc.UtcDateTime).DayNumber);
            }
        }

        // Die fünf häufigsten offenen hohen Befunde über alle Anlagen
        var top = allRuns.SelectMany(x => x.Run.Results.Where(r => r.Status == ResultStatus.Fail && r.Severity == Severity.High)
                .Select(r => (x.Run, Result: r, x.Catalogue)))
            .GroupBy(x => (x.Result.RuleSetId, x.Result.RuleId))
            .Select(g =>
            {
                var rule = g.First().Catalogue.Find(g.Key.RuleId);
                var hosts = g.Select(x => x.Run.HostName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
                int applicable = allRuns.Count(x => x.Run.Results.Any(r => r.RuleSetId == g.Key.RuleSetId && r.RuleId == g.Key.RuleId &&
                    r.Status is not (ResultStatus.NotApplicable or ResultStatus.OutOfScope or ResultStatus.Info)));
                return new AggregatedFinding(g.Key.RuleSetId, g.Key.RuleId, rule?.Title.Get(lang) ?? g.Key.RuleId, rule?.Category ?? "system",
                    Severity.High, hosts.Count, applicable, [.. hosts.Take(MaxHostsPerFinding)], hosts.Count > MaxHostsPerFinding, null,
                    g.Aggregate(ResultFlags.None, (f, x) => f | x.Result.Flags));
            })
            .OrderByDescending(f => f.OpenCount).ThenBy(f => f.RuleSetId, StringComparer.Ordinal).ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .Take(5).ToList();

        if (incompleteHosts.Count > 0 || rows.Any(x => x.Incomplete))
        {
            var totals = ScoreCalculator.Totals.Sum(allRuns.Select(x => ScoreCalculator.Totals.Of(x.Run.Results, x.Catalogue)));
            banners.Add(new Banner(BannerKind.IncompleteCheck, totals.CheckedShare, incompleteHosts, null, []));
        }
        if (staleHosts.Count > 0) banners.Add(new Banner(BannerKind.StaleSnapshot, null, staleHosts, staleAge, []));
        var released = catalogues.Count > 0 ? catalogues.Min(c => c.ReleasedOn) : (catalogue ?? services.Catalogue.LoadEmbedded()).ReleasedOn;
        if (d.DayNumber - released.DayNumber > StaleCatalogueDays) banners.Add(new Banner(BannerKind.StaleCatalogue, null, [], d.DayNumber - released.DayNumber, []));
        if (!UiLanguage.IsReviewedReportLanguage(lang)) banners.Add(new Banner(BannerKind.PreTranslation, null, [], null, []));

        var marks = plants.Select(p => p.Settings.Confidentiality).Distinct().ToList();
        var confidentiality = marks.Count == 1 && marks[0] is { } mark ? mark : WorkflowText.Common("Confidentiality_Default", lang);
        return new SummaryData(d, lang, services.Clock.GetUtcNow(), AppInfo.Tool(), rows, top, banners, confidentiality,
            !UiLanguage.IsReviewedReportLanguage(lang));
    }

    /// <summary>Die letzten <paramref name="count"/> Monatsenden bis einschließlich <paramref name="d"/>, älteste zuerst.</summary>
    internal static IReadOnlyList<DateOnly> MonthEnds(DateOnly d, int count)
    {
        var lastOfMonth = new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
        var end = d == lastOfMonth ? d : new DateOnly(d.Year, d.Month, 1).AddDays(-1);
        var list = new List<DateOnly>();
        for (int i = 0; i < count; i++)
        {
            list.Add(end);
            end = new DateOnly(end.Year, end.Month, 1).AddDays(-1);
        }
        list.Reverse();
        return list;
    }

    // ================================================================ Soll-Konfiguration (O13)

    public TargetConfigData BuildTargetConfig(Plant p, string? groupId, string lang)
    {
        var today = services.Today;
        var view = PlantView.Load(services, p, today, null, catalogue);
        var cat = view.Catalogue;
        var text = new ExpectationText(lang, p.Settings);
        var views = RegisterViews.Build(view, today, lang);
        var rules = cat.Rules.Where(x => x.State == RuleState.Active)
            .OrderBy(x => IndexOf(x.Category)).ThenBy(x => x.RuleSetId == ScoreCalculator.OwnRuleSet ? 0 : 1).ThenBy(x => x.RuleSetId, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        var rendered = rules.ToDictionary(x => (x.RuleSetId, x.Id), x => new TargetConfigRule(x.RuleSetId, x.Id, x.Title.Get(lang), x.Category, x.Severity,
            x.Kind, text.Render(x), [.. RuleSources.ValueBacking.Where(src => x.Verified.Any(v => string.Equals(v.Src, src, StringComparison.Ordinal)))],
            x.References.Any(rf => string.Equals(rf.Src, "MSB", StringComparison.Ordinal)), text.AppliesTo(x.AppliesTo)));

        TargetConfigGroup Group(string? id, string name, MachineRole? role, IReadOnlyList<string> hosts)
        {
            bool Touches(RegisterScope s) => s.AllMachines || (id is not null && s.Groups.Any(g => string.Equals(g, id, StringComparison.OrdinalIgnoreCase))) ||
                s.Machines.Any(m => hosts.Contains(m, StringComparer.OrdinalIgnoreCase));
            bool WholeGroup(RegisterScope s) => s.AllMachines || (id is not null && s.Groups.Any(g => string.Equals(g, id, StringComparison.OrdinalIgnoreCase)));
            var tailoring = views.Tailoring.Where(t => Touches(t.Entry.Scope)).ToList();
            var removed = tailoring.Where(t => RegisterRules.InForce(t.State) && WholeGroup(t.Entry.Scope))
                .Select(t => t.Entry.RuleId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new TargetConfigGroup(id, name, role, hosts, [.. rules.Where(x => !removed.Contains(x.Id)).Select(x => rendered[(x.RuleSetId, x.Id)])],
                tailoring);
        }

        var groups = new List<TargetConfigGroup>();
        foreach (var g in p.Groups.Where(g => groupId is null || string.Equals(g.Id, groupId, StringComparison.OrdinalIgnoreCase)))
            groups.Add(Group(g.Id, g.Name, g.Role, [.. p.Machines.Where(m => string.Equals(m.GroupId, g.Id, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.HostName).Order(StringComparer.OrdinalIgnoreCase)]));
        if (groupId is not null && groups.Count == 0) throw RiegelException.Common(RglCodesCommon.NotFound, groupId);
        if (groupId is null)
        {
            var ungrouped = view.Hosts.Where(h => view.Machine(h)?.GroupId is null).ToList();
            if (ungrouped.Count > 0 || p.Groups.Count == 0) groups.Add(Group(null, WorkflowText.Common("Group_None", lang), null, ungrouped));
        }
        return new TargetConfigData(p.Id, p.Name, lang, today, services.Clock.GetUtcNow(), AppInfo.Tool(), CatalogueRef(cat), groups, view.Baselines,
            Provenance(cat), p.Settings.SignOff, view.Registers.Hashes.Tailoring,
            p.Settings.Confidentiality ?? WorkflowText.Common("Confidentiality_Default", lang), !UiLanguage.IsReviewedReportLanguage(lang));
    }

    // ================================================================ Genehmigungsblatt (O12)

    public ApprovalSheetData BuildApprovalSheet(Plant p, IReadOnlyList<string>? ids, string lang)
    {
        var today = services.Today;
        var view = PlantView.Load(services, p, today, null, catalogue);
        var cat = view.Catalogue;
        var views = RegisterViews.Build(view, today, lang);
        var registers = view.Registers;
        bool Pending(RegisterEntryState s) => s is RegisterEntryState.Pending or RegisterEntryState.ChangedAfterApproval;
        if (ids is not null)
        {
            var known = registers.Exceptions.Exceptions.Select(e => e.Id).Concat(registers.Tailoring.Entries.Select(t => t.Id));
            var unknown = ids.FirstOrDefault(id => !known.Contains(id.Trim(), StringComparer.OrdinalIgnoreCase));
            if (unknown is not null) throw RglCodesWorkflow.Error(RglCodesWorkflow.EntryNotFound, unknown, "exceptions/tailoring");
        }
        bool Selected(string id, RegisterEntryState state) =>
            ids is null ? Pending(state) : ids.Any(i => string.Equals(i.Trim(), id, StringComparison.OrdinalIgnoreCase));

        string? MeasureFor(string ruleId, RegisterScope scope)
        {
            var m = registers.Measures.Measures.Where(x => x.State == MeasureState.Planned &&
                    string.Equals(x.RuleId?.Trim(), ruleId.Trim(), StringComparison.OrdinalIgnoreCase) && RegisterRules.Overlap(x.Scope, scope, p))
                .OrderBy(x => x.DueDate).ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
            return m is null ? null : WorkflowText.Format("ApprovalSheet_Measure", lang, m.Id, m.Action, m.OwnerRole, RegisterRules.Iso(m.DueDate));
        }

        var entries = new List<ApprovalSheetEntry>();
        foreach (var e in views.Exceptions.Where(v => Selected(v.Entry.Id, v.State)))
            entries.Add(new ApprovalSheetEntry(RegisterKind.Exceptions, e.Entry.Id, e.Entry.RuleId, e.RuleTitle, e.Severity, e.Entry.Scope, e.Entry.Category,
                e.Entry.Justification, e.Entry.CompensatingMeasure, e.Entry.Reference, e.Entry.AcceptedValue, MeasureFor(e.Entry.RuleId, e.Entry.Scope),
                e.ContentSha256, e.State));
        foreach (var t in views.Tailoring.Where(v => Selected(v.Entry.Id, v.State)))
            entries.Add(new ApprovalSheetEntry(RegisterKind.Tailoring, t.Entry.Id, t.Entry.RuleId, t.RuleTitle, t.Severity, t.Entry.Scope, null,
                t.Entry.Reason, null, null, null, MeasureFor(t.Entry.RuleId, t.Entry.Scope), t.ContentSha256, t.State));

        var material = string.Join("\n", new[] { registers.Hashes.Exceptions, registers.Hashes.Tailoring }.Concat(entries.Select(x => x.Id + "|" + x.ContentSha256)));
        var sheetId = $"GB-{today.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{Hashes.Sha256Hex(material)[..6]}";

        var approvers = p.Settings.SignOff.Where(s => s.Step == SignOffSteps.Approved).ToList();
        bool riskAcceptance = entries.Any(x => x.Category == ReasonCategory.RiskAccepted && x.Severity == Severity.High);
        if (riskAcceptance)
            approvers.AddRange(p.Settings.RiskAcceptanceRoles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => new SignOffRole(SignOffSteps.Approved, r.Trim())));
        if (approvers.Count == 0) approvers.AddRange(p.Settings.SignOff);
        approvers = [.. approvers.DistinctBy(a => a.Role.Trim(), StringComparer.OrdinalIgnoreCase)];

        return new ApprovalSheetData(p.Id, p.Name, lang, today, services.Clock.GetUtcNow(), AppInfo.Tool(), sheetId, registers.Hashes.Exceptions,
            registers.Hashes.Tailoring, entries, approvers, p.Settings.Confidentiality ?? WorkflowText.Common("Confidentiality_Default", lang),
            !UiLanguage.IsReviewedReportLanguage(lang));
    }

    // ================================================================ Erhebungsprotokoll (O14)

    [GeneratedRegex(@"^\d{8}T\d{6}Z-[0-9a-f]{6}$")]
    private static partial Regex ImportId();

    /// <summary>„latest“ = neuester Import-Nachweis; sonst eine Import-ID (RGL-0411 bei falscher Form).</summary>
    public CollectionRecordData BuildCollectionRecord(Plant p, string importId, string lang)
    {
        var folder = Path.Combine(PlantStore.Folder(p), PlantFiles.Imports);
        string? file;
        if (string.Equals(importId, "latest", StringComparison.OrdinalIgnoreCase))
            file = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*" + PlantFiles.ImportExtension).Order(StringComparer.Ordinal).LastOrDefault() : null;
        else if (ImportId().IsMatch(importId ?? ""))
            file = Path.Combine(folder, importId + PlantFiles.ImportExtension);
        else
            throw RglCodesWorkflow.Error(RglCodesWorkflow.InvalidImportId, importId);
        if (file is null || !File.Exists(file)) throw RiegelException.Common(RglCodesCommon.NotFound, importId);
        var record = RiegelJson.ReadFile<ImportRecord>(file, ReadLimits.Default.MaxPlantFileBytes);
        if (record.Schema != SchemaIds.Import) throw RiegelException.Common(RglCodesCommon.UnsupportedSchema, record.Schema);
        return new CollectionRecordData(p.Id, p.Name, lang, services.Clock.GetUtcNow(), AppInfo.Tool(), record,
            p.Settings.Confidentiality ?? WorkflowText.Common("Confidentiality_Default", lang), !UiLanguage.IsReviewedReportLanguage(lang));
    }
}
