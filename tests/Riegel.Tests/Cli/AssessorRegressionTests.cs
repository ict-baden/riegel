// SPDX-License-Identifier: MIT
using Riegel.Commands.Alerting;
using Riegel.Core.Model;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Cli;

/// <summary>
/// Verschlechterung nach spec 2.7 in den Vergleichen: dieselbe Regel wie die Kennzeichen am Ergebnis
/// (RunService.MarkChanges) – auch „Ausnahme entfernt“ und „Zuschnitt entfernt“ nach „nicht erfüllt“.
/// </summary>
public sealed class AssessorRegressionTests
{
    private static StatusChange Change(StatusChangeKind kind, ResultStatus from, ResultStatus to, ResultFlags toFlags = ResultFlags.None) =>
        new("riegel", "NET-01", kind, from, to, ResultFlags.None, toFlags, null);

    [Theory]
    [InlineData(StatusChangeKind.Worse, ResultStatus.Pass, ResultStatus.Fail, ResultFlags.None, true)]
    [InlineData(StatusChangeKind.ExceptionRemoved, ResultStatus.Exception, ResultStatus.Fail, ResultFlags.ExceptionPending, true)]
    [InlineData(StatusChangeKind.ExceptionRemoved, ResultStatus.Exception, ResultStatus.Fail, ResultFlags.ExceptionChangedAfterApproval, true)]
    [InlineData(StatusChangeKind.ExceptionRemoved, ResultStatus.Exception, ResultStatus.Fail, ResultFlags.None, true)]
    [InlineData(StatusChangeKind.TailoringRemoved, ResultStatus.OutOfScope, ResultStatus.Fail, ResultFlags.None, true)]
    [InlineData(StatusChangeKind.TailoringRemoved, ResultStatus.OutOfScope, ResultStatus.Pass, ResultFlags.None, false)]
    [InlineData(StatusChangeKind.TailoringRemoved, ResultStatus.OutOfScope, ResultStatus.NotApplicable, ResultFlags.None, false)]
    [InlineData(StatusChangeKind.ExceptionExpired, ResultStatus.Exception, ResultStatus.Fail, ResultFlags.ExceptionExpired, false)]
    [InlineData(StatusChangeKind.Worse, ResultStatus.Pass, ResultStatus.Fail, ResultFlags.NewRule, false)]
    [InlineData(StatusChangeKind.Other, ResultStatus.NotCheckable, ResultStatus.Fail, ResultFlags.None, false)]
    public void Verschlechterung_wie_die_Kennzeichen_am_Ergebnis(StatusChangeKind kind, ResultStatus from, ResultStatus to, ResultFlags toFlags, bool expected) =>
        Assert.Equal(expected, Assessor.IsRegression(Change(kind, from, to, toFlags)));

    [Fact]
    public void Gegen_den_freigegebenen_Stand_zaehlt_auch_bei_schon_offenem_Vorlauf()
    {
        // voriger Lauf schon fail (kein Kennzeichen Regression am Ergebnis), freigegeben war die Ausnahme bzw. der Zuschnitt
        var previous = TestRuns.Make("HMI-03", [TestRuns.Result("NET-01", ResultStatus.Fail, Severity.High), TestRuns.Result("NET-02", ResultStatus.Fail)],
            collected: TestRuns.Collected.AddDays(-1));
        var run = TestRuns.Make("HMI-03",
            [TestRuns.Result("NET-01", ResultStatus.Fail, Severity.High, ResultFlags.ExceptionPending), TestRuns.Result("NET-02", ResultStatus.Fail)],
            previousRunId: previous.Id);
        var approved = new RunComparison("HMI-03", "20261001T080000Z-aaaaaa", run.Id, ComparisonBasis.Approved,
        [
            new StatusChange("riegel", "NET-01", StatusChangeKind.ExceptionRemoved, ResultStatus.Exception, ResultStatus.Fail,
                ResultFlags.None, ResultFlags.ExceptionPending, "EX-0001"),
            new StatusChange("riegel", "NET-02", StatusChangeKind.TailoringRemoved, ResultStatus.OutOfScope, ResultStatus.Fail,
                ResultFlags.None, ResultFlags.None, "TL-0001"),
        ], [], false);
        var assessment = Assessor.Assess(run, previous, null, approved, AlertPolicy.For(AlertMode.Change), true, true, "x");
        Assert.Equal(["NET-01", "NET-02"], assessment.Regressions.Select(r => r.RuleId).Order(StringComparer.Ordinal));
        Assert.All(assessment.Regressions, r => Assert.Equal(ComparisonBasis.Approved, r.Basis));
    }
}
