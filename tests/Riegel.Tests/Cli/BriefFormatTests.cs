// SPDX-License-Identifier: MIT
using Riegel.Commands;
using Riegel.Commands.Alerting;
using Riegel.Core.Model;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Cli;

/// <summary>
/// <c>check --brief</c> (O11): genau die Zeile aus spec 5, invariante Leistungsdaten in jeder Sprache, Mehrzahl,
/// unbekannter Wert und die UNKNOWN-Zeile bei Fehlern.
/// </summary>
public sealed class BriefFormatTests
{
    /// <summary>Wörtlich aus spec 5, O11.</summary>
    private const string O11 =
        "RIEGEL WARNING - HMI-01: 0 worse, 3 open (1 high), 1 exception expiring, 1 measure overdue | score=72%;;;0;100 open=3 high=1 regressions=0 expiring=1 overdue=1 notcheckable=4";

    /// <summary>
    /// Der Lauf hinter O11: drei offene Befunde (einer hoch, schon im vorigen Lauf offen), eine ablaufende Ausnahme,
    /// eine überfällige Maßnahme, vier nicht prüfbar, Härtungsgrad 72 – Alarmart change ergibt WARNING.
    /// </summary>
    private static (Run Run, Run Previous) O11Runs()
    {
        IReadOnlyList<RuleResult> results =
        [
            TestRuns.Result("CRED-03", ResultStatus.Fail, Severity.High),
            TestRuns.Result("NET-05", ResultStatus.Fail, Severity.Medium, ResultFlags.MeasurePlanned | ResultFlags.MeasureOverdue),
            TestRuns.Result("AUD-07", ResultStatus.Fail, Severity.Low),
            TestRuns.Result("RDP-02", ResultStatus.Exception, Severity.Medium, ResultFlags.ExceptionExpiring),
            TestRuns.Result("DEF-01", ResultStatus.NotCheckable, Severity.High),
            TestRuns.Result("DEF-02", ResultStatus.NotCheckable, Severity.Medium),
            TestRuns.Result("ENC-01", ResultStatus.NotCheckable, Severity.High),
            TestRuns.Result("TLS-01", ResultStatus.NotCheckable, Severity.Medium),
            TestRuns.Result("SYS-01", ResultStatus.Pass, Severity.Medium),
        ];
        var previous = TestRuns.Make("HMI-01", results, collected: TestRuns.Collected.AddDays(-30));
        var run = TestRuns.Make("HMI-01", results, s => s with { ExceptionsExpiring = 1, MeasuresOverdue = 1, Score = 72 }, previousRunId: previous.Id);
        return (run, previous);
    }

    [Fact]
    public void Zeile_entspricht_O11_Zeichen_fuer_Zeichen()
    {
        using var _ = new UiCulture("en");
        var (run, previous) = O11Runs();
        var assessment = Assessor.Assess(run, previous, null, null, AlertPolicy.For(AlertMode.Change), stored: true, inPlant: true, "x");
        Assert.Equal(O11, BriefLine.Format(assessment));
        Assert.Equal(CommandApp.Warning, assessment.Outcome.ExitCode);
    }

    [Fact]
    public void Check_brief_mit_Anlage_schreibt_O11_und_endet_mit_1()
    {
        using var _ = new UiCulture("en");
        var (run, previous) = O11Runs();
        using var plant = new TempPlant();
        TestRuns.Store(plant, previous);
        using var temp = new TempFolder();
        var snapshot = SnapshotBuilder.Windows10Ltsc2019("HMI-01").WriteTo(temp.Path);

        var result = Cli.Run(Cli.With(runs: new FakeRunService(run)), "check", "--plant", plant.Folder, "--snapshot", snapshot, "--no-store", "--brief");

        Assert.True(result.Exit == 1, result.ToString());
        Assert.Equal(O11 + Environment.NewLine, result.Out);
    }

    [Theory]
    [InlineData("en", "0 worse, 3 open (1 high), 1 exception expiring, 1 measure overdue")]
    [InlineData("de", "0 schlechter, 3 offen (1 hoch), 1 Ausnahme läuft ab, 1 Maßnahme überfällig")]
    [InlineData("es", "0 empeorados, 3 abiertos (1 alto), 1 excepción a punto de caducar, 1 medida vencida")]
    [InlineData("fr", "0 dégradé, 3 ouverts (1 élevé), 1 exception bientôt expirée, 1 mesure en retard")]
    [InlineData("zh-Hans", "0 项恶化, 3 项未解决（1 项高）, 1 项例外即将过期, 1 项措施逾期")]
    public void Text_ist_uebersetzt_Leistungsdaten_und_Statuswort_bleiben_invariant(string language, string text)
    {
        using var _ = new UiCulture(language);
        var (run, previous) = O11Runs();
        var line = BriefLine.Format(Assessor.Assess(run, previous, null, null, AlertPolicy.For(AlertMode.Change), true, true, "x"));
        Assert.StartsWith("RIEGEL WARNING - HMI-01: ", line, StringComparison.Ordinal);
        Assert.Equal($"RIEGEL WARNING - HMI-01: {text} | score=72%;;;0;100 open=3 high=1 regressions=0 expiring=1 overdue=1 notcheckable=4", line);
    }

    [Fact]
    public void Mehrzahl_und_zusaetzliche_Teile_nur_wenn_sie_zaehlen()
    {
        using var _ = new UiCulture("en");
        var run = TestRuns.Make("PLC-GW", [TestRuns.Result("CRED-01", ResultStatus.Fail, Severity.High)],
            s => s with { ExceptionsExpiring = 2, MeasuresOverdue = 3, ExceptionsExpired = 2, ExceptionsPending = 1 },
            RunFlags.Incomplete | RunFlags.StaleSnapshot | RunFlags.ClockSuspect);
        var line = BriefLine.Format(Assessor.Assess(run, null, null, null, AlertPolicy.For(AlertMode.Change), true, true, "x"));
        Assert.Equal("RIEGEL CRITICAL - PLC-GW: 0 worse, 1 open (1 high), 2 exceptions expiring, 3 measures overdue, 1 newly open high, " +
                     "2 exceptions expired, 1 approval pending, incomplete check, snapshot stale, clock suspect" +
                     " | score=80%;;;0;100 open=1 high=1 regressions=0 expiring=2 overdue=3 notcheckable=0", line);
    }

    [Fact]
    public void Ohne_Wertung_steht_U_in_den_Leistungsdaten()
    {
        using var _ = new UiCulture("en");
        var run = TestRuns.Make("DC-01", [TestRuns.Result("SYS-01", ResultStatus.Pass)], flags: RunFlags.DomainController, score: null);
        var line = BriefLine.Format(Assessor.Assess(run, null, null, null, AlertPolicy.For(AlertMode.State), false, false, "x"));
        Assert.Equal("RIEGEL OK - DC-01: 0 worse, 0 open (0 high), 0 exceptions expiring, 0 measures overdue" +
                     " | score=U;;;0;100 open=0 high=0 regressions=0 expiring=0 overdue=0 notcheckable=0", line);
    }

    [Fact]
    public void Verschlechterung_zaehlt_in_worse_und_regressions()
    {
        using var _ = new UiCulture("en");
        var previous = TestRuns.Make("HMI-02", [TestRuns.Result("NET-01", ResultStatus.Pass, Severity.High)], collected: TestRuns.Collected.AddDays(-1));
        var run = TestRuns.Make("HMI-02", [TestRuns.Result("NET-01", ResultStatus.Fail, Severity.High)], previousRunId: previous.Id);
        var comparison = new RunComparison("HMI-02", previous.Id, run.Id, ComparisonBasis.Previous,
            [new StatusChange("riegel", "NET-01", StatusChangeKind.Worse, ResultStatus.Pass, ResultStatus.Fail, ResultFlags.None, ResultFlags.None, null)], [], false);
        var line = BriefLine.Format(Assessor.Assess(run, previous, comparison, null, AlertPolicy.For(AlertMode.Change), true, true, "x"));
        Assert.StartsWith("RIEGEL CRITICAL - HMI-02: 1 worse, 1 open (1 high), ", line, StringComparison.Ordinal);
        Assert.Contains("regressions=1 ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Fehler_ergibt_eine_UNKNOWN_Zeile_und_Exitcode_3()
    {
        using var _ = new UiCulture("en");
        using var temp = new TempFolder();
        var missing = Path.Combine(temp.Path, "HMI-09_20261018T080000Z.snapshot.json");
        var result = Cli.Run("check", "--snapshot", missing, "--brief");
        Assert.Equal(3, result.Exit);
        Assert.StartsWith("RIEGEL UNKNOWN - HMI-09_20261018T080000Z.snapshot.json: RGL-0002: ", result.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("|", result.Out, StringComparison.Ordinal);
        Assert.Contains("RGL-0002", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Mehrere_Rechner_eine_Zeile_je_Rechner_der_schlechteste_zuerst()
    {
        using var _ = new UiCulture("en");
        using var temp = new TempFolder();
        var a = SnapshotBuilder.Windows11Workgroup("A-01").WriteTo(temp.Sub("a"));
        var b = SnapshotBuilder.Windows11Workgroup("B-01").WriteTo(temp.Sub("b"));
        var services = Cli.With(runs: new FakeRunService(
            TestRuns.Make("A-01", [TestRuns.Result("SYS-01", ResultStatus.Pass)]),
            TestRuns.Make("B-01", [TestRuns.Result("CRED-01", ResultStatus.Fail, Severity.High)])));
        var result = Cli.Run(services, "check", "--snapshot", a, b, "--brief");
        var lines = result.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, result.Exit);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("RIEGEL CRITICAL - B-01: ", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("RIEGEL OK - A-01: ", lines[1], StringComparison.Ordinal);
    }
}
