// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Session;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Windows.App;

/// <summary>Pflichtfelder, Zustände und Grenzen der Register in der Oberfläche (spec 2.11–2.13, RGL-06xx).</summary>
public sealed class RegisterRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 18);
    private static readonly RegisterScope Host = new(["HMI-1"], [], false);

    private static Approval Approved(DateOnly on, DateOnly review, string? hash = null) => new("Qualitätssicherung", null, on, review, "CHG-1", hash);

    private static ExceptionEntry Exception(ReasonCategory category = ReasonCategory.TechnicalNecessity, string justification = "Begründung mit mehr als zwanzig Zeichen",
        string? reference = null, string? compensating = null, string? accepted = "1", Approval? approval = null, RegisterScope? scope = null) =>
        new("EX-0001", "NET-01", scope ?? Host, category, justification, compensating, reference, accepted, approval ?? Approval.None, Today, null, null, null, null);

    private static IReadOnlyList<string> Codes(IEnumerable<ValidationIssue> issues) => [.. issues.Select(i => i.Code)];

    [Fact]
    public void Zustand_aus_der_Freigabe()
    {
        Assert.Equal(RegisterEntryState.Pending, RegisterRules.StateOf(Approval.None, "a", Today, 30));
        Assert.Equal(RegisterEntryState.Valid, RegisterRules.StateOf(Approved(Today.AddMonths(-1), Today.AddMonths(6), "a"), "a", Today, 30));
        Assert.Equal(RegisterEntryState.Expiring, RegisterRules.StateOf(Approved(Today.AddMonths(-11), Today.AddDays(10), "a"), "a", Today, 30));
        Assert.Equal(RegisterEntryState.Expired, RegisterRules.StateOf(Approved(Today.AddMonths(-13), Today.AddDays(-1), "a"), "a", Today, 30));
        Assert.Equal(RegisterEntryState.ChangedAfterApproval, RegisterRules.StateOf(Approved(Today.AddMonths(-1), Today.AddMonths(6), "a"), "b", Today, 30));
        Assert.True(RegisterRules.Effective(RegisterEntryState.Expiring));
        Assert.False(RegisterRules.Effective(RegisterEntryState.Pending));
    }

    [Fact]
    public void Ausnahme_Pflichtfelder()
    {
        var rule = RuleBuilder.Check("NET-01", "network").Build();
        Assert.DoesNotContain(Codes(RegisterRules.IssuesFor(Exception(), rule, PlantSettings.Default)), c => c != RglCodesApp.UnknownRule);
        Assert.Contains(RglCodesApp.JustificationTooShort, Codes(RegisterRules.IssuesFor(Exception(justification: "zu kurz"), rule, PlantSettings.Default)));
        Assert.Contains(RglCodesApp.ReferenceRequired, Codes(RegisterRules.IssuesFor(Exception(ReasonCategory.VendorRequirement), rule, PlantSettings.Default)));
        Assert.DoesNotContain(RglCodesApp.ReferenceRequired, Codes(RegisterRules.IssuesFor(Exception(ReasonCategory.VendorRequirement, reference: "HD-4711"), rule, PlantSettings.Default)));
        Assert.Contains(RglCodesApp.CompensatingRequired, Codes(RegisterRules.IssuesFor(Exception(ReasonCategory.CompensatingControl), rule, PlantSettings.Default)));
        Assert.Contains(RglCodesApp.AcceptedValueRequired, Codes(RegisterRules.IssuesFor(Exception(accepted: null), rule, PlantSettings.Default)));
        Assert.Contains(RglCodesApp.AcceptedValueAny, Codes(RegisterRules.IssuesFor(Exception(accepted: "*"), rule, PlantSettings.Default)));
        Assert.Contains(RglCodesApp.ScopeEmpty, Codes(RegisterRules.IssuesFor(Exception(scope: new RegisterScope([], [], false)), rule, PlantSettings.Default)));
        Assert.Contains(RglCodesApp.UnknownRule, Codes(RegisterRules.IssuesFor(Exception(), null, PlantSettings.Default)));
    }

    [Fact]
    public void Prueftermin_Grenzen()
    {
        var on = new DateOnly(2026, 1, 10);
        Assert.Contains(RglCodesApp.ReviewBeforeApproval, Codes(RegisterRules.ApprovalIssues(RegisterKind.Exceptions, "EX-1", Approved(on, on.AddDays(-1)))));
        Assert.Contains(RglCodesApp.ReviewOverYear, Codes(RegisterRules.ApprovalIssues(RegisterKind.Exceptions, "EX-1", Approved(on, on.AddMonths(13)))));
        Assert.Contains(RglCodesApp.ReviewTooFar, Codes(RegisterRules.ApprovalIssues(RegisterKind.Exceptions, "EX-1", Approved(on, on.AddMonths(37)))));
        Assert.Empty(RegisterRules.ApprovalIssues(RegisterKind.Exceptions, "EX-1", Approved(on, on.AddMonths(12))));

        var errors = RegisterRules.ApprovalErrors("", null, null, "", out _);
        Assert.Contains(RglCodesApp.ApprovalRoleRequired, errors);
        Assert.Contains(RglCodesApp.ApprovalRecordRequired, errors);
        Assert.Contains(RglCodesApp.ApprovalIncomplete, errors);
        Assert.Empty(RegisterRules.ApprovalErrors("Qualitätssicherung", on, on.AddMonths(6), "CHG-1", out var warnings));
        Assert.Empty(warnings);
    }

    [Fact]
    public void Zuschnitt_und_Massnahme()
    {
        var high = RuleBuilder.Check("DEF-03", "defender").Severity(Severity.High).Build();
        var tailoring = new TailoringEntry("TL-0001", "DEF-03", Host, "kurz", Approval.None, Today, null, null, null);
        var codes = Codes(RegisterRules.IssuesFor(tailoring, high));
        Assert.Contains(RglCodesApp.ReasonTooShort, codes);
        Assert.Contains(RglCodesApp.TailoringHighSeverity, codes);

        var measure = new MeasureEntry("M-0001", "ENC-01", Host, " ", "", Today, null, MeasureState.Planned, Today, null, null, null);
        codes = Codes(RegisterRules.IssuesFor(measure, high));
        Assert.Contains(RglCodesApp.ActionRequired, codes);
        Assert.Contains(RglCodesApp.OwnerRoleRequired, codes);
    }

    [Fact]
    public void Geltungsbereich_und_Kennungen()
    {
        Assert.True(RegisterRules.InScope(new RegisterScope([], ["g-hmi"], false), "HMI-1", "g-hmi"));
        Assert.False(RegisterRules.InScope(new RegisterScope([], ["g-hmi"], false), "SRV-1", "g-srv"));
        Assert.True(RegisterRules.InScope(new RegisterScope([], [], true), "SRV-1", null));
        Assert.True(RegisterRules.Specificity(Host, "HMI-1", null) > RegisterRules.Specificity(new RegisterScope([], [], true), "HMI-1", null));
        Assert.Equal("EX-0003", RegisterRules.NextId("EX-", ["EX-0001", "EX-0002", "TL-0009"]));
        Assert.Equal("M-0001", RegisterRules.NextId("M-", []));
    }

    [Fact]
    public void Jeder_Fehlercode_hat_einen_Text_in_jeder_Sprache()
    {
        foreach (var code in RglCodesApp.All)
        {
            Assert.Matches(@"^RGL-06\d\d$", code);
            foreach (var language in new[] { "en", "de", "es", "fr", "zh-Hans" })
            {
                var text = UI.Loc.Strings.GetString("Error_" + code.Replace("-", ""), new System.Globalization.CultureInfo(language));
                Assert.False(string.IsNullOrWhiteSpace(text), $"{code} ({language})");
            }
        }
        Assert.StartsWith("RGL-0610: ", RglCodesApp.Message(RglCodesApp.JustificationTooShort));
    }
}
