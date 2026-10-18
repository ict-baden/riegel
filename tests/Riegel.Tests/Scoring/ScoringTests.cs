// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Scoring;
using Riegel.Tests.Plants;
using static Riegel.Tests.Plants.WorkflowKit;

namespace Riegel.Tests.Scoring;

/// <summary>Wertung nach spec 2.6: Gewichte, Deckel der Kategorie audit, Kategoriewerte, Prüfumfang, Rundung, Domänencontroller, Zählungen.</summary>
public sealed class ScoringTests
{
    private static (RunSummary Summary, IReadOnlyList<CategoryScore> Categories) Score(IReadOnlyList<RuleResult> results, ICatalogue catalogue,
        Run? previous = null, bool isDc = false) => Services().Scoring.Score(results, catalogue, previous, isDc);

    [Fact]
    public void Haertungsgrad_und_Dokumentiert_mit_Gewichten_3_2_1()
    {
        var catalogue = new TestCatalogue([Check("A-01", severity: Severity.High), Check("A-02"), Check("A-03", severity: Severity.Low), Check("A-04")]);
        var (summary, _) = Score(
        [
            Result("A-01", ResultStatus.Pass, Severity.High),       // 3
            Result("A-02", ResultStatus.Exception, Severity.Medium), // 2
            Result("A-03", ResultStatus.Fail, Severity.Low),         // 1
            Result("A-04", ResultStatus.Fail, Severity.Medium),      // 2
        ], catalogue);
        Assert.Equal(38, summary.Score);       // 3 / 8 = 37,5 → 38 (AwayFromZero)
        Assert.Equal(63, summary.Documented);  // 5 / 8 = 62,5 → 63
        Assert.Equal((1, 2, 1), (summary.Pass, summary.Fail, summary.Exception));
        Assert.Equal((0, 1, 1), (summary.OpenHigh, summary.OpenMedium, summary.OpenLow));
    }

    [Fact]
    public void Ausnahmen_heben_nur_Dokumentiert_Zuschnitt_entfernt_aus_beiden()
    {
        var catalogue = new TestCatalogue([Check("A-01"), Check("A-02")]);
        var open = Score([Result("A-01", ResultStatus.Pass), Result("A-02", ResultStatus.Fail)], catalogue).Summary;
        var excepted = Score([Result("A-01", ResultStatus.Pass), Result("A-02", ResultStatus.Exception)], catalogue).Summary;
        var tailored = Score([Result("A-01", ResultStatus.Pass), Result("A-02", ResultStatus.OutOfScope)], catalogue).Summary;
        Assert.Equal((50, 50), (open.Score, open.Documented));
        Assert.Equal((50, 100), (excepted.Score, excepted.Documented));
        Assert.Equal((100, 100), (tailored.Score, tailored.Documented));
        Assert.Equal(100, tailored.CheckedShare);
    }

    [Fact]
    public void Audit_traegt_hoechstens_12_Gewichtspunkte_bei()
    {
        // 27 Überwachungsregeln mittlerer Schwere scheitern zusammen (54 Punkte), dazu 5 hohe Regeln anderswo bestanden (15 Punkte)
        var rules = Enumerable.Range(1, 27).Select(i => Check($"AUD-{i:00}", "audit")).Concat(Enumerable.Range(1, 5).Select(i => Check($"NET-{i:00}", severity: Severity.High))).ToList();
        var catalogue = new TestCatalogue(rules);
        var results = rules.Select(r => r.Category == "audit" ? Result(r.Id, ResultStatus.Fail) : Result(r.Id, ResultStatus.Pass, Severity.High)).ToList();
        var (summary, categories) = Score(results, catalogue);
        // f = 12/54 → audit trägt 12 bei: 15 / (15 + 12) = 55,6 % → 56 (ohne Deckel wären es 15/69 = 22 %)
        Assert.Equal(56, summary.Score);
        var audit = categories.Single(c => c.Category == "audit");
        Assert.True(audit.Capped);
        Assert.Equal(12d, audit.Weight);
        Assert.Equal(0, audit.Score);
        Assert.Equal(27, audit.Fail);
        var network = categories.Single(c => c.Category == "network");
        Assert.False(network.Capped);
        Assert.Equal(15d, network.Weight);
        Assert.Equal(100, network.Score);

        // unter dem Deckel bleibt alles ungedeckelt
        var few = Score([Result("AUD-01", ResultStatus.Fail), Result("AUD-02", ResultStatus.Pass), Result("NET-01", ResultStatus.Pass, Severity.High)], catalogue);
        Assert.False(few.Categories.Single(c => c.Category == "audit").Capped);
        Assert.Equal(71, few.Summary.Score); // (2 + 3) / 7
    }

    [Fact]
    public void Kategorien_ungedeckelt_in_Berichtsreihenfolge()
    {
        var catalogue = new TestCatalogue([Check("NET-01", severity: Severity.High), Check("NET-02"), Check("TLS-01", "tls", Severity.Low)]);
        var (_, categories) = Score([Result("NET-01", ResultStatus.Pass, Severity.High), Result("NET-02", ResultStatus.Fail), Result("TLS-01", ResultStatus.Exception, Severity.Low)], catalogue);
        Assert.Equal(RuleCategories.All, categories.Select(c => c.Category));
        var network = categories.Single(c => c.Category == "network");
        Assert.Equal((1, 1, 0, 60, 5d), (network.Pass, network.Fail, network.Exception, network.Score, network.Weight));
        Assert.Equal(0, categories.Single(c => c.Category == "tls").Score);
        Assert.Null(categories.Single(c => c.Category == "rdp").Score);
    }

    [Fact]
    public void Pruefumfang_ohne_nicht_anwendbar_Zuschnitt_und_Bestand()
    {
        var rules = Enumerable.Range(1, 10).Select(i => Check($"X-{i:00}")).ToList();
        var catalogue = new TestCatalogue(rules);
        var results = new List<RuleResult>
        {
            Result("X-01", ResultStatus.Pass), Result("X-02", ResultStatus.Fail), Result("X-03", ResultStatus.Exception),
            Result("X-04", ResultStatus.NotCheckable), Result("X-05", ResultStatus.NotCheckable), Result("X-06", ResultStatus.NotCheckable),
            Result("X-07", ResultStatus.NotApplicable), Result("X-08", ResultStatus.OutOfScope), Result("X-09", ResultStatus.Info),
            Result("X-10", ResultStatus.Pass),
        };
        var summary = Score(results, catalogue).Summary;
        Assert.Equal(57, summary.CheckedShare); // 4 prüfbar von 7 (10 − n/a − Zuschnitt − Bestand)
        Assert.Equal((3, 1, 1, 1), (summary.NotCheckable, summary.NotApplicable, summary.OutOfScope, summary.Info));
        Assert.Equal(0, Score([], catalogue).Summary.CheckedShare);
        Assert.Null(Score([], catalogue).Summary.Score);
    }

    [Theory]
    [InlineData(111, 13, 89)] // 111 von 124 = 89,5 %: vorher als 90 % ohne Hinweis „Unvollständige Prüfung“ (Review evidence-11)
    [InlineData(112, 12, 90)] // 112 von 124 = 90,3 %
    [InlineData(179, 1, 99)]  // 179 von 180 = 99,4 %: nie 100 %, solange etwas nicht prüfbar ist
    public void Pruefumfang_wird_abgeschnitten_damit_die_90_Prozent_Schwelle_stimmt(int checkable, int notCheckable, int expected)
    {
        var rules = Enumerable.Range(1, checkable + notCheckable).Select(i => Check($"X-{i:000}")).ToList();
        var results = rules.Select((r, i) => Result(r.Id, i < checkable ? ResultStatus.Pass : ResultStatus.NotCheckable)).ToList();
        var summary = Score(results, new TestCatalogue(rules)).Summary;
        Assert.Equal(expected, summary.CheckedShare);
        Assert.Equal(100m * checkable / (checkable + notCheckable) < 90m, summary.CheckedShare < 90);
    }

    [Fact]
    public void Domaenencontroller_ohne_Haertungsgrad()
    {
        var catalogue = new TestCatalogue([Check("A-01")]);
        var (summary, categories) = Score([Result("A-01", ResultStatus.Pass)], catalogue, isDc: true);
        Assert.Null(summary.Score);
        Assert.Null(summary.Documented);
        Assert.All(categories, c => Assert.Null(c.Score));
        Assert.Equal(100, summary.CheckedShare);
        Assert.Equal(1, summary.Pass);
    }

    [Fact]
    public void Zaehlungen_aus_Flags_und_neu_offen_gegen_den_vorigen_Lauf()
    {
        var catalogue = new TestCatalogue([Check("A-01"), Check("A-02"), Check("A-03"), Check("A-04"), Check("A-05")]);
        var previous = new RunServiceProbe().Run([Result("A-01", ResultStatus.Pass), Result("A-02", ResultStatus.NotCheckable), Result("A-05", ResultStatus.Fail)]);
        var (summary, _) = Score(
        [
            Result("A-01", ResultStatus.Fail, flags: ResultFlags.Regression),
            Result("A-02", ResultStatus.Fail, flags: ResultFlags.ExceptionPending | ResultFlags.MeasureOverdue),
            Result("A-03", ResultStatus.Fail, flags: ResultFlags.ExceptionExpired | ResultFlags.NewRule),
            Result("A-04", ResultStatus.Exception, flags: ResultFlags.ExceptionExpiring),
            Result("A-05", ResultStatus.Fail),
        ], catalogue, previous);
        Assert.Equal(1, summary.Regressions);
        Assert.Equal(2, summary.NewlyOpen);
        Assert.Equal((1, 1, 1, 1), (summary.ExceptionsExpiring, summary.ExceptionsExpired, summary.ExceptionsPending, summary.MeasuresOverdue));
    }

    [Fact]
    public void Baseline_Regeln_zaehlen_mit_aber_nicht_im_Haertungsgrad()
    {
        var catalogue = new TestCatalogue([Check("A-01")]);
        var (summary, _) = Score([Result("A-01", ResultStatus.Pass), Result("IMP-kunde-R1", ResultStatus.Fail, ruleSet: "kunde")], catalogue);
        Assert.Equal(100, summary.Score);
        Assert.Equal(1, summary.Fail);
        Assert.Equal(1, summary.OpenMedium);
        Assert.Equal(100, summary.CheckedShare);
    }

    [Fact]
    public void Anlagenwert_deckelt_je_Rechner_und_addiert_dann()
    {
        var rules = Enumerable.Range(1, 10).Select(i => Check($"AUD-{i:00}", "audit")).Append(Check("NET-01", severity: Severity.High)).ToList();
        var catalogue = new TestCatalogue(rules);
        var machine = rules.Select(r => r.Category == "audit" ? Result(r.Id, ResultStatus.Fail) : Result(r.Id, ResultStatus.Pass, Severity.High)).ToList();
        var one = ScoreCalculator.Totals.Of(machine, catalogue);
        var plant = ScoreCalculator.Totals.Sum([one, ScoreCalculator.Totals.Of(machine, catalogue)]);
        Assert.Equal(20, one.Score);   // 3 / (3 + 12)
        Assert.Equal(20, plant.Score); // je Rechner gedeckelt: 6 / 30, nicht 6 / (6 + 12)
        Assert.True(plant.Capped);
        Assert.Equal(24d, plant.Categories(false).Single(c => c.Category == "audit").Weight);
    }

    /// <summary>Ein Lauf als Vergleichsbasis (nur die Ergebnisse zählen).</summary>
    private sealed class RunServiceProbe
    {
        public Run Run(IReadOnlyList<RuleResult> results) =>
            new Riegel.Core.Runs.RunService(Services(), new ScriptedEvaluator())
                .EvaluateOnly(Riegel.Tests.TestSupport.SnapshotBuilder.Windows11Workgroup().Build(), new RunOptions(Catalogue: new TestCatalogue([]))) with { Results = results };
    }
}
