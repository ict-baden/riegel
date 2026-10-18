// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.About;
using Riegel.Core.Diagnostics;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Model.Reports;
using Riegel.Core.Services;
using Riegel.Session;

namespace Riegel.Dev;

/// <summary>
/// Nur Entwicklung: nachgebildete Dienste für die Demo-Anlage, gewählt allein über <c>RIEGEL_FAKE_SERVICES=1</c> und
/// nie in der Oberfläche erwähnt (Auftrag des Pakets app). Solange Engine und Arbeitsablauf Platzhalter sind, zeigen
/// die Seiten damit realistische Ergebnisse: eine Verschlechterung, ablaufende und beantragte Ausnahmen, einen Zuschnitt,
/// eine überfällige Maßnahme und den Rechner mit passivem Defender. Lesen und Schreiben der Dateien, Wertung und alle
/// Dokumente bleiben die echten Dienste. Die Integration schaltet das ab und erzeugt die Screenshots neu.
/// </summary>
internal static class FakeServices
{
    public static AppServices Create(RiegelServices real)
    {
        var engine = new FakeEngine(real.Catalogue);
        var catalogue = new FakeCatalogueProvider(real.Catalogue, engine);
        var registers = new FakeRegisterService(real.Registers, engine);
        var runs = new FakeRunService(real, engine, registers);
        var plants = new FakePlantStore(real, runs);
        runs.Plants = plants;
        Log.Info(FakeEngine.Describe());
        return new AppServices
        {
            Catalogue = catalogue,
            Snapshots = real.Snapshots,
            Baselines = real.Baselines,
            Plants = plants,
            Registers = registers,
            Comparison = new FakeComparer(),
            Runs = runs,
            Scoring = real.Scoring,
            ReportData = real.ReportData,
            Pdf = real.Pdf,
            Tabular = real.Tabular,
            Evidence = real.Evidence,
            Remediation = real.Remediation,
            Platform = real.Platform,
            Clock = real.Clock,
            IsFake = true,
        };
    }
}

/// <summary>Katalog mit den nachgebildeten Regeln; Laden aus Datei und Verbinden mit Baselines wie echt.</summary>
internal sealed class FakeCatalogueProvider(ICatalogueProvider real, FakeEngine engine) : ICatalogueProvider
{
    public ICatalogue LoadEmbedded() => engine.Catalogue;

    public ICatalogue Load(Stream catalogueJson, ReadLimits l) => real.Load(catalogueJson, l);

    public ICatalogue Combine(ICatalogue core, IEnumerable<ImportedBaseline> b) => real.Combine(core, b);
}

/// <summary>Register lesen und schreiben wie echt; Anwenden und Prüfen nachgebildet (spec 2.11–2.13).</summary>
internal sealed class FakeRegisterService(IRegisterService real, FakeEngine engine) : IRegisterService
{
    public RegisterSet Load(Plant p) => real.Load(p);

    public void Save(Plant p, RegisterSet r, RegisterHashes expected) => real.Save(p, r, expected);

    public RegisterSet Approve(RegisterSet r, RegisterKind kind, IReadOnlyList<string> ids, Approval a) => real.Approve(r, kind, ids, a);

    public RegisterSet ImportTemplate(RegisterSet r, string templatePath, ReadLimits l) => real.ImportTemplate(r, templatePath, l);

    public IReadOnlyList<RuleResult> Apply(IReadOnlyList<RuleResult> raw, RegisterSet r, Plant p, string host, DateOnly today)
    {
        var group = RegisterRules.GroupOf(p, host);
        var warn = p.Settings.WarnDays;
        T? Best<T>(IEnumerable<T> entries, Func<T, RegisterScope> scope, Func<T, DateOnly?> approvedOn) where T : class =>
            entries.Where(e => RegisterRules.InScope(scope(e), host, group))
                .OrderByDescending(e => RegisterRules.Specificity(scope(e), host, group)).ThenByDescending(e => approvedOn(e) ?? DateOnly.MinValue)
                .FirstOrDefault();

        var results = new List<RuleResult>(raw.Count);
        foreach (var result in raw)
        {
            var x = result;
            var tailoring = Best(r.Tailoring.Entries.Where(t => Same(t.RuleId, x.RuleId)), t => t.Scope, t => t.Approval.ApprovedOn);
            if (tailoring is not null && x.Status is not (ResultStatus.NotApplicable or ResultStatus.Info))
            {
                var state = RegisterRules.StateOf(tailoring.Approval, ContentHashes.Of(tailoring), today, warn);
                x = RegisterRules.Effective(state)
                    ? x with { Status = ResultStatus.OutOfScope, TailoringId = tailoring.Id }
                    : x with { TailoringId = tailoring.Id, Flags = x.Flags | ResultFlags.TailoringPending };
            }
            if (x.Status == ResultStatus.Fail)
            {
                var exception = Best(r.Exceptions.Exceptions.Where(e => Same(e.RuleId, x.RuleId)), e => e.Scope, e => e.Approval.ApprovedOn);
                if (exception is not null)
                {
                    var state = RegisterRules.StateOf(exception.Approval, ContentHashes.Of(exception), today, warn);
                    bool valueMatches = exception.AcceptedValue is null || exception.AcceptedValue.Trim() == "*"
                                        || CanonicalValue.AreEqual(exception.AcceptedValue, x.Actual);
                    x = state switch
                    {
                        RegisterEntryState.Valid or RegisterEntryState.Expiring when valueMatches => x with
                        {
                            Status = ResultStatus.Exception,
                            ExceptionId = exception.Id,
                            Flags = x.Flags | (state == RegisterEntryState.Expiring ? ResultFlags.ExceptionExpiring : ResultFlags.None),
                        },
                        RegisterEntryState.Valid or RegisterEntryState.Expiring => x with { ExceptionId = exception.Id, Flags = x.Flags | ResultFlags.ExceptionValueMismatch },
                        RegisterEntryState.Expired => x with { ExceptionId = exception.Id, Flags = x.Flags | ResultFlags.ExceptionExpired },
                        RegisterEntryState.ChangedAfterApproval => x with { ExceptionId = exception.Id, Flags = x.Flags | ResultFlags.ExceptionPending | ResultFlags.ExceptionChangedAfterApproval },
                        _ => x with { ExceptionId = exception.Id, Flags = x.Flags | ResultFlags.ExceptionPending },
                    };
                }
            }
            if (x.Status is ResultStatus.Fail or ResultStatus.Exception)
            {
                var measure = Best(r.Measures.Measures.Where(m => Same(m.RuleId, x.RuleId) && m.State == MeasureState.Planned), m => m.Scope, m => m.CreatedOn);
                if (measure is not null && x.Status == ResultStatus.Fail)
                    x = x with { MeasureId = measure.Id, Flags = x.Flags | (measure.DueDate < today ? ResultFlags.MeasureOverdue : ResultFlags.MeasurePlanned) };
            }
            results.Add(x);
        }
        return results;
    }

    public IReadOnlyList<ValidationIssue> Validate(RegisterSet r, ICatalogue c, Plant p, DateOnly today)
    {
        var issues = new List<ValidationIssue>();
        foreach (var e in r.Exceptions.Exceptions) issues.AddRange(RegisterRules.IssuesFor(e, engine.Catalogue.Find(e.RuleId), p.Settings));
        foreach (var t in r.Tailoring.Entries) issues.AddRange(RegisterRules.IssuesFor(t, engine.Catalogue.Find(t.RuleId)));
        foreach (var m in r.Measures.Measures) issues.AddRange(RegisterRules.IssuesFor(m, engine.Catalogue.Find(m.RuleId)));
        return issues;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Auswerten und speichern mit der nachgebildeten Engine, sonst wie der Platzhalter von workflow.</summary>
internal sealed class FakeRunService(RiegelServices real, FakeEngine engine, FakeRegisterService registers) : IRunService
{
    /// <summary>Anlagenspeicher für Läufe und Freigaben (wird nach dem Anlegen gesetzt).</summary>
    public IPlantStore Plants { get; set; } = real.Plants;

    public Run EvaluateAndStore(Plant p, string snapshotPath, RunOptions o)
    {
        var limits = o.Limits ?? ReadLimits.Default;
        var snapshot = real.Snapshots.Read(snapshotPath, limits);
        var regs = o.Registers ?? (p.Folder is not null ? registers.Load(p) : null);
        var sha = Hashes.Sha256File(snapshotPath);
        var relative = p.Folder is { } folder ? Path.GetRelativePath(folder, Path.GetFullPath(snapshotPath)).Replace('\\', '/') : Path.GetFileName(snapshotPath);

        Run? previous = null;
        string? reEvaluationOf = null;
        ApprovedState? approved = null;
        Run? approvedRun = null;
        if (p.Folder is not null)
        {
            var runs = Plants.ListRuns(p, snapshot.Machine.HostName);
            reEvaluationOf = runs.FirstOrDefault(r => r.SnapshotSha256 == sha)?.Id;
            var prev = runs.Where(r => r.SnapshotSha256 != sha && r.CollectedAtUtc < snapshot.CollectedAtUtc).LastOrDefault();
            if (prev is not null) previous = Plants.LoadRun(p, prev.Id);
            approved = Plants.LoadApproved(p, snapshot.Machine.HostName);
            if (approved is not null)
            {
                try { approvedRun = Plants.LoadRun(p, approved.RunId); }
                catch (RiegelException) { approvedRun = null; }
            }
        }

        var run = Evaluate(snapshot, o with { Plant = p, Registers = regs }, new FileRef(relative, sha), previous, approvedRun);
        run = run with { ReEvaluationOf = reEvaluationOf, PreviousRunId = previous?.Id, ApprovedRunId = approved?.RunId };
        if (o.Store && !p.ReadOnly && p.Folder is not null) Plants.SaveRun(p, run);
        return run;
    }

    public Run EvaluateOnly(Snapshot s, RunOptions o) =>
        Evaluate(s, o, new FileRef("", Hashes.Sha256Hex(RiegelJson.SerializeToUtf8Bytes(s))), null, null);

    private Run Evaluate(Snapshot s, RunOptions o, FileRef snapshotRef, Run? previous, Run? approved)
    {
        var catalogue = engine.Catalogue;
        var plant = o.Plant;
        var settings = plant?.Settings ?? PlantSettings.Default;
        var now = real.Clock.GetUtcNow();
        var date = o.EvaluationDate ?? DateOnly.FromDateTime(now.UtcDateTime);
        var host = s.Machine.HostName;
        var machine = plant?.Machines.FirstOrDefault(m => string.Equals(m.HostName, host, StringComparison.OrdinalIgnoreCase));
        var context = new EvaluationContext(date, now, o.ImportedAtUtc, settings.UpdateMaxDays, settings.UnusedAccountDays, machine?.EsuCovered ?? false, o.Language);

        var raw = engine.Evaluate(s, context);
        var results = plant is not null && o.Registers is not null ? registers.Apply(raw, o.Registers, plant, host, date) : raw;

        // Verschlechterung gegenüber vorigem Lauf oder freigegebenem Stand (spec 2.7)
        var bases = new[] { previous, approved }.Where(b => b is not null).Select(b => b!).ToList();
        static bool Good(ResultStatus s) => s is ResultStatus.Pass or ResultStatus.Exception or ResultStatus.NotApplicable or ResultStatus.OutOfScope;
        results = [.. results.Select(r =>
        {
            if (bases.Count == 0) return r;
            var old = bases.Select(b => b.Results.FirstOrDefault(x => string.Equals(x.RuleId, r.RuleId, StringComparison.OrdinalIgnoreCase))).ToList();
            if (old.All(o => o is null)) return r with { Flags = r.Flags | ResultFlags.NewRule };
            if (r.Status == ResultStatus.Fail && old.Any(o => o is not null && Good(o.Status)) && (r.Flags & ResultFlags.ExceptionExpired) == 0)
                return r with { Flags = r.Flags | ResultFlags.Regression };
            return r;
        })];

        bool isDc = s.Machine.DomainRole is 4 or 5;
        var (summary, categories) = real.Scoring.Score(results, catalogue, previous, isDc);
        int Has(ResultFlags f) => results.Count(r => (r.Flags & f) != 0);
        int newlyOpen = previous is null ? 0
            : results.Count(r => r.Status == ResultStatus.Fail && previous.Results.FirstOrDefault(x => x.RuleId == r.RuleId)?.Status is ResultStatus.NotCheckable or null);
        summary = summary with
        {
            Regressions = Has(ResultFlags.Regression),
            NewlyOpen = newlyOpen,
            ExceptionsExpiring = Has(ResultFlags.ExceptionExpiring),
            ExceptionsExpired = Has(ResultFlags.ExceptionExpired),
            ExceptionsPending = Has(ResultFlags.ExceptionPending),
            MeasuresOverdue = Has(ResultFlags.MeasureOverdue),
        };

        var flags = RunFlags.None;
        if (summary.CheckedShare < 90 || !s.IsAdmin) flags |= RunFlags.Incomplete;
        if (isDc) flags |= RunFlags.DomainController;
        if (date.DayNumber - DateOnly.FromDateTime(s.CollectedAtUtc.UtcDateTime).DayNumber > settings.StaleDays)
        {
            flags |= RunFlags.StaleSnapshot;
            results = [.. results.Select(r => r with { Flags = r.Flags | ResultFlags.StaleSnapshot })];
        }

        var hashes = o.Registers?.Hashes ?? RegisterHashes.Empty;
        var id = RunIds.Create(now, snapshotRef.Sha256, catalogue.Sha256, hashes);
        var facts = new SortedDictionary<string, JsonElement>(engine.Facts(s).ToDictionary(), StringComparer.Ordinal);
        return new Run(SchemaIds.Run, id, host, now, date, s.CollectedAtUtc, o.ImportedAtUtc, snapshotRef, AppInfo.Tool(),
            new CatalogueRef(catalogue.Id, catalogue.Version, catalogue.ReleasedOn, catalogue.Sha256), [], hashes, s.IsAdmin, s.Options,
            results, summary, categories, facts, null, null, null, flags);
    }
}

/// <summary>Anlagenspeicher wie echt; nur der Import wertet mit der nachgebildeten Engine aus.</summary>
internal sealed class FakePlantStore(RiegelServices real, FakeRunService runs) : IPlantStore
{
    public Plant Create(string dir, string name) => real.Plants.Create(dir, name);

    public Plant Open(string dir, bool readOnly) => real.Plants.Open(dir, readOnly);

    public void SavePlant(Plant p, string expectedSha256) => real.Plants.SavePlant(p, expectedSha256);

    public void SaveRun(Plant p, Run r) => real.Plants.SaveRun(p, r);

    public IReadOnlyList<RunInfo> ListRuns(Plant p, string host) => real.Plants.ListRuns(p, host);

    public Run LoadRun(Plant p, string runId) => real.Plants.LoadRun(p, runId);

    public ApprovedState? LoadApproved(Plant p, string host) => real.Plants.LoadApproved(p, host);

    public void SaveApproved(Plant p, ApprovedState a) => real.Plants.SaveApproved(p, a);

    public ImportRecord Import(Plant p, IReadOnlyList<string> paths, ImportOptions o)
    {
        if (p.ReadOnly || p.Folder is null) throw RiegelException.Common(RglCodesCommon.ReadOnlyMode);
        var limits = o.Limits ?? ReadLimits.Default;
        var now = real.Clock.GetUtcNow();
        var files = new List<ImportedFile>();
        foreach (var source in paths)
        {
            var snapshot = real.Snapshots.Read(source, limits);
            var check = real.Snapshots.VerifySidecar(source);
            var hostFolder = Path.Combine(p.Folder, "snapshots", FileNames.Host(snapshot.Machine.HostName));
            Directory.CreateDirectory(hostFolder);
            var target = Path.Combine(hostFolder, Path.GetFileName(source));
            if (!File.Exists(target))
            {
                File.Copy(source, target);
                File.WriteAllText(SnapshotFile.SidecarPath(target), SnapshotFile.SidecarLine(check.Actual, Path.GetFileName(target)));
            }
            string? runId = null;
            if (o.Evaluate) runId = runs.EvaluateAndStore(p, target, new RunOptions(ImportedAtUtc: now, Limits: limits)).Id;
            files.Add(new ImportedFile(Path.GetFileName(source), check.Actual, check.Status == HashCheckStatus.Ok, snapshot.Machine.HostName,
                snapshot.CollectedAtUtc, snapshot.Collector.Name, snapshot.Collector.Version, snapshot.Collector.ExeSha256, snapshot.IsAdmin,
                snapshot.Options, snapshot.Origin, runId));
        }
        var id = FileNames.Timestamp(now) + "-" + Hashes.Sha256Hex(string.Join("\n", files.Select(f => f.Sha256)))[..6];
        var record = new ImportRecord(SchemaIds.Import, id, now, o.CollectedByRole, o.WitnessRole, o.OperatorRole, files);
        RiegelJson.WriteFile(Path.Combine(p.Folder, "imports", id + ".import.json"), record);
        return record;
    }
}
