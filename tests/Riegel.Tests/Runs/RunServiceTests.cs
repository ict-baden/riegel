// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Plants;
using Riegel.Core.Runs;
using Riegel.Core.Services;
using Riegel.Tests.Plants;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Plants.WorkflowKit;

namespace Riegel.Tests.Runs;

/// <summary>
/// Läufe (spec 2.7, 2.8, 7.6): vorheriger Lauf, erneute Auswertung, Regression gegen Ablauf und neue Regel,
/// freigegebener Stand als Basis, MachineGuid, veraltete Erfassung, Uhr, Unvollständig, Domänencontroller,
/// Determinismus.
/// </summary>
public sealed class RunServiceTests
{
    private static readonly TestCatalogue Catalogue = new([Check("NET-08"), Check("NET-01", severity: Severity.High), Check("PS-02", "powershell", Severity.Low)]);

    private sealed class Rig : IDisposable
    {
        public Rig(Plant? plant = null)
        {
            Plant = new TempPlant(plant);
            Clock = new FixedClock();
            Services = WorkflowKit.Services(Clock);
            Runs = new RunService(Services, new ScriptedEvaluator());
        }

        public TempPlant Plant { get; }
        public FixedClock Clock { get; }
        public RiegelServices Services { get; }
        public RunService Runs { get; }

        public Run Evaluate(SnapshotBuilder snapshot, ICatalogue? catalogue = null, DateTimeOffset? importedAt = null) =>
            Runs.EvaluateAndStore(Plant.Plant, Plant.AddSnapshot(snapshot.Build()), new RunOptions(ImportedAtUtc: importedAt, Catalogue: catalogue ?? Catalogue));

        public Run Again(string snapshotPath, ICatalogue? catalogue = null) =>
            Runs.EvaluateAndStore(Plant.Plant, snapshotPath, new RunOptions(Catalogue: catalogue ?? Catalogue));

        public void Dispose() => Plant.Dispose();
    }

    private static SnapshotBuilder Hmi(DateTimeOffset collected) => SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").CollectedAt(collected);

    private static RuleResult R(Run run, string id) => run.Results.Single(r => r.RuleId == id);

    [Fact]
    public void Vorheriger_Lauf_ist_der_neueste_eines_anderen_Exports_nach_Erfassungszeit()
    {
        using var rig = new Rig();
        var september = rig.Evaluate(Hmi(new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero)).Script("NET-08", "pass|1"));
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        var october = rig.Evaluate(Hmi(new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero)).Script("NET-08", "pass|1"));
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        // ein älterer Export kommt erst später an (Stapelimport): sein Vorgänger ist der vom September
        var august = rig.Evaluate(Hmi(new(2026, 8, 1, 8, 0, 0, TimeSpan.Zero)).Script("NET-08", "pass|1"));
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        var september15 = rig.Evaluate(Hmi(new(2026, 9, 15, 8, 0, 0, TimeSpan.Zero)).Script("NET-08", "pass|1"));

        Assert.Null(september.PreviousRunId);
        Assert.Equal(september.Id, october.PreviousRunId);
        Assert.Null(august.PreviousRunId);
        Assert.Equal(september.Id, september15.PreviousRunId);
        Assert.All(new[] { september, october, august, september15 }, r => Assert.Null(r.ReEvaluationOf));
        Assert.Equal(4, rig.Services.Plants.ListRuns(rig.Plant.Plant, "HMI-TEST").Count);
    }

    [Fact]
    public void Erneute_Auswertung_desselben_Exports_ist_kein_neuer_Stand()
    {
        using var rig = new Rig();
        var first = rig.Evaluate(Hmi(new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero)).Script("NET-08", "pass|1"));
        rig.Clock.Advance(TimeSpan.FromDays(1));
        var path = rig.Plant.AddSnapshot(Hmi(new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero)).Script("NET-08", "fail|0").Build());
        var second = rig.Again(path);
        rig.Clock.Advance(TimeSpan.FromDays(1));
        var withNewCatalogue = new TestCatalogue([.. Catalogue.Rules, Check("NET-09")]);
        var reEvaluated = rig.Again(path, withNewCatalogue);
        rig.Clock.Advance(TimeSpan.FromDays(1));
        var thirdTime = rig.Again(path, withNewCatalogue);

        Assert.Null(second.ReEvaluationOf);
        Assert.Equal(second.Id, reEvaluated.ReEvaluationOf);
        Assert.Equal(second.Id, thirdTime.ReEvaluationOf);
        // der vorige Lauf bleibt der eines anderen Exports – eine erneute Auswertung meldet keine Regression ein zweites Mal
        Assert.Equal(first.Id, reEvaluated.PreviousRunId);
        Assert.Equal(first.Id, thirdTime.PreviousRunId);
        Assert.True((R(second, "NET-08").Flags & ResultFlags.Regression) != 0);
        Assert.True((R(reEvaluated, "NET-08").Flags & ResultFlags.Regression) != 0);
        Assert.True((R(reEvaluated, "NET-09").Flags & ResultFlags.NewRule) != 0);
    }

    [Fact]
    public void Regression_neue_Regel_neu_offen_und_abgelaufene_Ausnahme()
    {
        using var rig = new Rig();
        var registers = Set([Exception("EX-0001", "PS-02", All, accepted: "*").Approve(Today.AddMonths(-12), Today.AddDays(5))]);
        rig.Plant.WriteRegisters(registers);
        var before = rig.Evaluate(Hmi(new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero))
            .Script("NET-08", "pass|1").Script("NET-01", "notCheckable").Script("PS-02", "fail|0"));
        Assert.Equal(ResultStatus.Exception, R(before, "PS-02").Status);

        // zehn Tage später: Ausnahme abgelaufen, NET-08 schlechter, NET-01 erstmals prüfbar und offen, NET-09 neu
        rig.Clock.Advance(TimeSpan.FromDays(10));
        var catalogue = new TestCatalogue([.. Catalogue.Rules, Check("NET-09", severity: Severity.High)]);
        var after = rig.Evaluate(Hmi(new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero))
            .Script("NET-08", "fail|0").Script("NET-01", "fail|1").Script("PS-02", "fail|0").Script("NET-09", "fail|0"), catalogue);

        Assert.Equal(before.Id, after.PreviousRunId);
        Assert.Equal(ResultFlags.Regression, R(after, "NET-08").Flags & (ResultFlags.Regression | ResultFlags.NewRule));
        Assert.Equal(ResultFlags.None, R(after, "NET-01").Flags & ResultFlags.Regression);
        Assert.Equal(ResultFlags.ExceptionExpired, R(after, "PS-02").Flags);
        Assert.Equal(ResultFlags.NewRule, R(after, "NET-09").Flags & (ResultFlags.Regression | ResultFlags.NewRule));
        Assert.Equal(1, after.Summary.Regressions);
        Assert.Equal(2, after.Summary.NewlyOpen); // NET-01 (vorher nicht prüfbar) und NET-09 (neue Regel)
        Assert.Equal(1, after.Summary.ExceptionsExpired);
        Assert.Equal(2, after.Summary.OpenHigh);
    }

    [Fact]
    public void Aenderung_der_Ausnahme_nach_Freigabe_ist_offen_beantragt_und_eine_Regression()
    {
        using var rig = new Rig();
        var approved = Exception("EX-0001", "NET-08", All, accepted: "0").Approve();
        rig.Plant.WriteRegisters(Set([approved]));
        var before = rig.Evaluate(Hmi(new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero)).Script("NET-08", "fail|0"));
        Assert.Equal(ResultStatus.Exception, R(before, "NET-08").Status);

        rig.Plant.WriteRegisters(Set([approved with { Justification = approved.Justification + " (geändert)" }]));
        rig.Clock.Advance(TimeSpan.FromHours(1));
        var after = rig.Evaluate(Hmi(new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero)).Script("NET-08", "fail|0"));
        var result = R(after, "NET-08");
        Assert.Equal(ResultStatus.Fail, result.Status);
        Assert.Equal(ResultFlags.ExceptionPending | ResultFlags.ExceptionChangedAfterApproval | ResultFlags.Regression, result.Flags);
        Assert.Equal(1, after.Summary.ExceptionsPending);
        Assert.NotEqual(before.Registers.Exceptions, after.Registers.Exceptions);
    }

    [Fact]
    public void Ohne_vorigen_Lauf_ist_der_freigegebene_Stand_die_Basis()
    {
        using var rig = new Rig();
        var commissioning = rig.Evaluate(Hmi(new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero)).Script("NET-08", "pass|1"));
        rig.Services.Plants.SaveApproved(rig.Plant.Plant, new ApprovedState(SchemaIds.Approved, "HMI-TEST", commissioning.Id, "", "", "",
            RegisterHashes.Empty, new Approval("Qualitätssicherung", null, Today, null, "SAT-1", null)));
        // Lauf eines älteren Exports: kein voriger Lauf, Basis = Freigabe
        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        var older = rig.Evaluate(Hmi(new(2026, 8, 1, 8, 0, 0, TimeSpan.Zero)).Script("NET-08", "fail|0"));
        Assert.Null(older.PreviousRunId);
        Assert.Equal(commissioning.Id, older.ApprovedRunId);
        Assert.True((R(older, "NET-08").Flags & ResultFlags.Regression) != 0);
    }

    [Fact]
    public void Neue_MachineGuid_setzt_MachineIdentityChanged()
    {
        using var rig = new Rig();
        rig.Evaluate(Hmi(new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero)));
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        var same = rig.Evaluate(Hmi(new(2026, 9, 2, 8, 0, 0, TimeSpan.Zero)));
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        var reinstalled = rig.Evaluate(Hmi(new(2026, 9, 3, 8, 0, 0, TimeSpan.Zero)).Header(m => m with { MachineGuid = "11111111-0000-4000-8000-000000000099" }));
        Assert.Equal(RunFlags.None, same.Flags & RunFlags.MachineIdentityChanged);
        Assert.Equal(RunFlags.MachineIdentityChanged, reinstalled.Flags & RunFlags.MachineIdentityChanged);
    }

    [Fact]
    public void Veraltete_Erfassung_nach_staleDays()
    {
        using var rig = new Rig();
        var day30 = rig.Evaluate(Hmi(new(2026, 9, 18, 23, 0, 0, TimeSpan.Zero)).Script("NET-08", "pass|1"));
        Assert.Equal(RunFlags.None, day30.Flags & RunFlags.StaleSnapshot);
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        var day31 = rig.Evaluate(Hmi(new(2026, 9, 17, 8, 0, 0, TimeSpan.Zero)).Script("NET-08", "pass|1"));
        Assert.Equal(RunFlags.StaleSnapshot, day31.Flags & RunFlags.StaleSnapshot);
        Assert.Equal(ResultFlags.StaleSnapshot, R(day31, "NET-08").Flags);
        Assert.Equal(ResultFlags.None, R(day31, "NET-01").Flags); // nicht prüfbar: keine Aussage, kein Kennzeichen
    }

    [Fact]
    public void Unplausible_Uhr_des_Zielrechners()
    {
        using var rig = new Rig();
        var now = FixedClock.Default;
        var fine = rig.Evaluate(Hmi(now.AddHours(-2)), importedAt: now);
        Assert.Equal(RunFlags.None, fine.Flags & RunFlags.ClockSuspect);
        var afterImport = rig.Evaluate(Hmi(now.AddHours(-1)), importedAt: now.AddHours(-26));
        Assert.Equal(RunFlags.ClockSuspect, afterImport.Flags & RunFlags.ClockSuspect);
        var future = rig.Evaluate(Hmi(now.AddHours(25)));
        Assert.Equal(RunFlags.ClockSuspect, future.Flags & RunFlags.ClockSuspect);
        var beforeBoot = rig.Evaluate(Hmi(now.AddHours(-3)).Header(m => m with { LastBootUtc = now.AddHours(-2) }));
        Assert.Equal(RunFlags.ClockSuspect, beforeBoot.Flags & RunFlags.ClockSuspect);
        // vor dem Erscheinen des installierten Updates erfasst
        var builds = new BuildTable(SchemaIds.Builds, [new BuildEntry(17763, "Windows 10 1809", [new BuildRelease(7792, new DateOnly(2026, 10, 14), null, "B")], "https://example.invalid", Today)]);
        var beforeRelease = rig.Evaluate(Hmi(new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero)), new TestCatalogue(Catalogue.Rules, builds: builds));
        Assert.Equal(RunFlags.ClockSuspect, beforeRelease.Flags & RunFlags.ClockSuspect);
        // Kennzeichen der Engine an einem Ergebnis gilt für den Lauf
        var fromEngine = rig.Evaluate(Hmi(now.AddHours(-4)).Script("NET-08", "notCheckable||clockSuspect"));
        Assert.Equal(RunFlags.ClockSuspect, fromEngine.Flags & RunFlags.ClockSuspect);
    }

    [Fact]
    public void Unvollstaendig_unter_90_Prozent_oder_ohne_Admin_und_Domaenencontroller_ohne_Wert()
    {
        using var rig = new Rig();
        var all = Hmi(new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero)).Script("NET-08", "pass|1").Script("NET-01", "fail|1").Script("PS-02", "pass|1");
        var complete = rig.Evaluate(all);
        Assert.Equal(100, complete.Summary.CheckedShare);
        Assert.Equal(RunFlags.None, complete.Flags);

        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        var nonAdmin = rig.Evaluate(Hmi(new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero)).NonAdmin().Script("NET-08", "pass|1").Script("NET-01", "fail|1").Script("PS-02", "pass|1"));
        Assert.Equal(RunFlags.Incomplete, nonAdmin.Flags & RunFlags.Incomplete);

        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        var partial = rig.Evaluate(Hmi(new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero)).Script("NET-08", "pass|1").Script("NET-01", "fail|1"));
        Assert.Equal(67, partial.Summary.CheckedShare);
        Assert.Equal(RunFlags.Incomplete, partial.Flags & RunFlags.Incomplete);

        var dc = rig.Evaluate(SnapshotBuilder.DomainController().CollectedAt(new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero))
            .Script("NET-08", "pass|1").Script("NET-01", "fail|1").Script("PS-02", "pass|1"));
        Assert.Equal(RunFlags.DomainController, dc.Flags & RunFlags.DomainController);
        Assert.Null(dc.Summary.Score);
        Assert.Null(dc.Summary.Documented);
        Assert.Equal(1, dc.Summary.OpenHigh);
    }

    [Fact]
    public void Gleiche_Eingaben_und_feste_Uhr_ergeben_eine_bytegleiche_results_json()
    {
        string Once()
        {
            using var rig = new Rig();
            rig.Plant.WriteRegisters(Set([Exception("EX-0001", "NET-08", All, accepted: "0").Approve()],
                measures: [Measure("M-0001", "NET-01", All, Today.AddDays(9))]));
            var snapshot = Hmi(new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero)).Script("NET-08", "fail|0").Script("NET-01", "fail|1").Script("PS-02", "pass|1");
            var run = rig.Evaluate(snapshot);
            return Convert.ToBase64String(File.ReadAllBytes(rig.Plant.PathOf($"runs/HMI-TEST/{run.Id}.results.json")));
        }
        var a = Once();
        var b = Once();
        Assert.Equal(a, b);
        var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(a));
        SchemaValidator.AssertValid(text, "results.json");
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        Assert.Contains("\"id\": \"20261018T090000Z-", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Nur_auswerten_ohne_Anlage_und_ohne_Speichern()
    {
        using var rig = new Rig();
        var snapshot = Hmi(new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero)).Script("PS-02", "fail|0").Script("NET-08", "pass|1").Build();
        var only = rig.Runs.EvaluateOnly(snapshot, new RunOptions(Catalogue: Catalogue));
        Assert.Equal(["NET-01", "NET-08", "PS-02"], only.Results.Select(r => r.RuleId));
        Assert.Null(only.PreviousRunId);
        Assert.Equal("", only.Snapshot.Path);
        Assert.Equal(RegisterHashes.Empty, only.Registers);

        // mit Anlage, aber ohne Speichern: nichts im Anlagenordner, Export außerhalb bleibt außerhalb
        using var export = new TempFolder();
        var outside = SnapshotFile.Write(snapshot, export.Path);
        var notStored = rig.Runs.EvaluateAndStore(rig.Plant.Plant, outside, new RunOptions(Catalogue: Catalogue, Store: false));
        Assert.False(Directory.Exists(rig.Plant.PathOf("runs")));
        Assert.False(Directory.Exists(rig.Plant.PathOf("snapshots")));
        Assert.Equal(Path.GetFileName(outside), notStored.Snapshot.Path);

        // gespeichert: der Export wird in den Anlagenordner übernommen
        var stored = rig.Runs.EvaluateAndStore(rig.Plant.Plant, outside, new RunOptions(Catalogue: Catalogue));
        Assert.Equal($"snapshots/HMI-TEST/{Path.GetFileName(outside)}", stored.Snapshot.Path);
        Assert.True(File.Exists(rig.Plant.PathOf(stored.Snapshot.Path)));
    }
}
