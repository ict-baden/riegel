// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Plants;
using Riegel.Core.Registers;
using Riegel.Tests.Plants;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Plants.WorkflowKit;

namespace Riegel.Tests.Registers;

/// <summary>Jede Gültigkeitsregel der Register (spec 2.11–2.13) mit festem „heute“ (2026-10-18).</summary>
public sealed class RegisterValidationTests
{
    private static readonly TestCatalogue Catalogue = new(
    [
        Check("NET-08"), Check("NET-01", severity: Severity.High), Check("PS-02", "powershell", Severity.Low),
        WorkflowKit.Inventory("ACCT-15"), RuleBuilder.Check("NET-30", "network").Retired().Build(),
    ]);

    private static IReadOnlyList<ValidationIssue> Validate(RegisterSet r, Plant? plant = null) =>
        Services().Registers.Validate(r, Catalogue, plant ?? WorkflowKit.Plant(), Today);

    private static IReadOnlyList<ValidationIssue> ForException(ExceptionEntry e, Plant? plant = null) => Validate(Set([e]), plant);

    private static void AssertIssue(IReadOnlyList<ValidationIssue> issues, string code, IssueSeverity severity, string? entry = null)
    {
        var hit = issues.FirstOrDefault(i => i.Code == code && (entry is null || i.EntryId == entry));
        Assert.True(hit is not null, $"{code} fehlt: " + string.Join("; ", issues.Select(i => $"{i.EntryId} {i.Code} {i.Message}")));
        Assert.Equal(severity, hit!.Severity);
        Assert.False(string.IsNullOrWhiteSpace(hit.Message));
    }

    [Fact]
    public void Gueltige_freigegebene_Ausnahme_hat_keine_Befunde()
    {
        var issues = ForException(Exception("EX-0001", "NET-08", Machines("HMI-TEST")).Approve());
        Assert.Empty(issues);
        Assert.Equal(0, RegisterValidator.ExitCode(issues));
    }

    [Fact]
    public void Begruendung_braucht_20_Zeichen()
    {
        AssertIssue(ForException(Exception("EX-0001", "NET-08", Machines("HMI-TEST"), justification: "zu kurz").Approve()),
            RglCodesWorkflow.TextTooShort, IssueSeverity.Error);
        Assert.DoesNotContain(ForException(Exception("EX-0001", "NET-08", Machines("HMI-TEST"), justification: new string('x', 20)).Approve()),
            i => i.Code == RglCodesWorkflow.TextTooShort);
        AssertIssue(Validate(Set(tailoring: [Tailoring("TL-0001", "NET-08", All) with { Reason = "kurz" }])), RglCodesWorkflow.TextTooShort,
            IssueSeverity.Error);
    }

    [Fact]
    public void Herstellervorgabe_braucht_Referenz_und_Kompensation_braucht_Massnahme()
    {
        var vendor = Exception("EX-0001", "NET-08", All, category: ReasonCategory.VendorRequirement) with { Reference = null };
        AssertIssue(ForException(vendor.Approve()), RglCodesWorkflow.ReferenceRequired, IssueSeverity.Error);
        var compensating = Exception("EX-0001", "NET-08", All, category: ReasonCategory.CompensatingControl) with { CompensatingMeasure = " " };
        AssertIssue(ForException(compensating.Approve()), RglCodesWorkflow.CompensatingMeasureRequired, IssueSeverity.Error);
    }

    [Fact]
    public void AcceptedValue_ist_bei_check_Regeln_Pflicht_und_Stern_warnt()
    {
        AssertIssue(ForException(Exception("EX-0001", "NET-08", All, accepted: null).Approve()), RglCodesWorkflow.AcceptedValueRequired, IssueSeverity.Error);
        AssertIssue(ForException(Exception("EX-0001", "NET-08", All, accepted: "*").Approve()), RglCodesWorkflow.AcceptedValueAny, IssueSeverity.Warning);
        // Bestandsregeln brauchen keinen Wert, die Ausnahme wirkt dort aber nie
        var inventory = ForException(Exception("EX-0001", "ACCT-15", All, accepted: null).Approve());
        Assert.DoesNotContain(inventory, i => i.Code == RglCodesWorkflow.AcceptedValueRequired);
        AssertIssue(inventory, RglCodesWorkflow.InventoryRule, IssueSeverity.Warning);
    }

    [Fact]
    public void Ohne_Freigabe_beantragt_und_ohne_Hash_nicht_gebunden()
    {
        AssertIssue(ForException(Exception("EX-0001", "NET-08", All)), RglCodesWorkflow.ApprovalPending, IssueSeverity.Warning);
        var partial = Exception("EX-0001", "NET-08", All) with { Approval = new Approval("ISB", null, Today, null, null, null) };
        var issues = ForException(partial);
        AssertIssue(issues, RglCodesWorkflow.ApprovalPending, IssueSeverity.Warning);
        Assert.Contains("reviewBy", issues.First(i => i.Code == RglCodesWorkflow.ApprovalPending).Message, StringComparison.Ordinal);
        var approved = Exception("EX-0001", "NET-08", All).Approve();
        var unbound = approved with { Approval = approved.Approval with { ApprovedContentSha256 = null } };
        AssertIssue(ForException(unbound), RglCodesWorkflow.ApprovalNotBound, IssueSeverity.Warning);
        Assert.Equal(1, RegisterValidator.ExitCode(ForException(unbound)));
    }

    [Fact]
    public void Aenderung_nach_Freigabe_wird_erkannt()
    {
        var approved = Exception("EX-0001", "NET-08", All).Approve();
        var edited = approved with { Justification = approved.Justification + " Nachtrag.", ModifiedOn = Today };
        AssertIssue(ForException(edited), RglCodesWorkflow.ChangedAfterApproval, IssueSeverity.Warning);
        Assert.Empty(ForException(approved));
    }

    [Fact]
    public void Pruefdatum_hoechstens_36_Monate_Warnung_ab_12_nicht_vor_der_Freigabe()
    {
        var on = Today.AddMonths(-1);
        AssertIssue(ForException(Exception("EX-0001", "NET-08", All).Approve(on, on.AddMonths(36).AddDays(1))), RglCodesWorkflow.ReviewByTooLate, IssueSeverity.Error);
        Assert.DoesNotContain(ForException(Exception("EX-0001", "NET-08", All).Approve(on, on.AddMonths(36))), i => i.Code == RglCodesWorkflow.ReviewByTooLate);
        AssertIssue(ForException(Exception("EX-0001", "NET-08", All).Approve(on, on.AddMonths(13))), RglCodesWorkflow.ReviewByLong, IssueSeverity.Warning);
        Assert.Empty(ForException(Exception("EX-0001", "NET-08", All).Approve(on, on.AddMonths(12))));
        AssertIssue(ForException(Exception("EX-0001", "NET-08", All).Approve(on, on.AddDays(-1))), RglCodesWorkflow.ReviewByBeforeApproval, IssueSeverity.Error);
        // Zuschnitt: 36 Monate, aber keine 12-Monats-Warnung
        var tailoring = Validate(Set(tailoring: [Tailoring("TL-0001", "NET-08", All).Approve(on, on.AddMonths(30))]));
        Assert.Empty(tailoring);
        AssertIssue(Validate(Set(tailoring: [Tailoring("TL-0001", "NET-08", All).Approve(on, on.AddMonths(37))])), RglCodesWorkflow.ReviewByTooLate,
            IssueSeverity.Error);
    }

    [Fact]
    public void Abgelaufen_und_laeuft_bald_ab()
    {
        var expired = Exception("EX-0001", "NET-08", All).Approve(Today.AddMonths(-13), Today.AddDays(-1));
        AssertIssue(ForException(expired), RglCodesWorkflow.Expired, IssueSeverity.Warning);
        var lastDay = Exception("EX-0001", "NET-08", All).Approve(Today.AddMonths(-11), Today);
        AssertIssue(ForException(lastDay), RglCodesWorkflow.Expiring, IssueSeverity.Warning);
        Assert.DoesNotContain(ForException(lastDay), i => i.Code == RglCodesWorkflow.Expired);
        var warn = Exception("EX-0001", "NET-08", All).Approve(Today.AddMonths(-11), Today.AddDays(30));
        AssertIssue(ForException(warn), RglCodesWorkflow.Expiring, IssueSeverity.Warning);
        var fine = Exception("EX-0001", "NET-08", All).Approve(Today.AddMonths(-10), Today.AddDays(31));
        Assert.Empty(ForException(fine));
    }

    [Fact]
    public void Risikouebernahme_hoher_Regeln_nur_durch_berechtigte_Rollen()
    {
        var plant = WorkflowKit.Plant(PlantSettings.Default with { RiskAcceptanceRoles = ["Geschäftsführung"] });
        var risk = Exception("EX-0001", "NET-01", All, category: ReasonCategory.RiskAccepted);
        AssertIssue(ForException(risk.Approve(role: "Anlagenverantwortlicher"), plant), RglCodesWorkflow.RiskAcceptanceRole, IssueSeverity.Error);
        Assert.Empty(ForException(risk.Approve(role: "geschäftsführung"), plant));
        // mittlere Regel oder keine Rollenliste: keine Einschränkung
        Assert.Empty(ForException(Exception("EX-0001", "NET-08", All, category: ReasonCategory.RiskAccepted).Approve(role: "Anlagenverantwortlicher"), plant));
        Assert.Empty(ForException(risk.Approve(role: "Anlagenverantwortlicher")));
    }

    [Fact]
    public void Ueberschneidungen_doppelte_IDs_und_Form_der_IDs()
    {
        var a = Exception("EX-0001", "NET-08", Machines("HMI-TEST")).Approve();
        var b = Exception("EX-0002", "NET-08", Groups("g-hmi")).Approve();
        var c = Exception("EX-0003", "PS-02", All).Approve();
        var issues = Validate(Set([a, b, c]));
        AssertIssue(issues, RglCodesWorkflow.Overlap, IssueSeverity.Warning, "EX-0001");
        AssertIssue(issues, RglCodesWorkflow.Overlap, IssueSeverity.Warning, "EX-0002");
        Assert.DoesNotContain(issues, i => i.EntryId == "EX-0003");
        // Ausnahme und Zuschnitt derselben Regel überschneiden sich ebenfalls
        AssertIssue(Validate(Set([c], [Tailoring("TL-0001", "PS-02", Groups("g-hmi")).Approve()])), RglCodesWorkflow.Overlap, IssueSeverity.Warning, "EX-0003");

        var duplicate = Validate(Set([a, a with { RuleId = "PS-02", Scope = All }]));
        AssertIssue(duplicate, RglCodesWorkflow.DuplicateId, IssueSeverity.Error);
        AssertIssue(Validate(Set([a with { Id = "A-1" }])), RglCodesWorkflow.IdFormat, IssueSeverity.Error);
        AssertIssue(Validate(Set(measures: [Measure("MS-1", "NET-08", All, Today.AddDays(5))])), RglCodesWorkflow.IdFormat, IssueSeverity.Error);
    }

    [Fact]
    public void Regeln_Gruppen_und_Rechner_muessen_bekannt_sein()
    {
        AssertIssue(ForException(Exception("EX-0001", "GIBT-ES-NICHT", All).Approve()), RglCodesWorkflow.UnknownRule, IssueSeverity.Error);
        AssertIssue(ForException(Exception("EX-0001", "NET-30", All).Approve()), RglCodesWorkflow.RuleRetired, IssueSeverity.Warning);
        AssertIssue(ForException(Exception("EX-0001", "NET-08", Groups("g-gibt-es-nicht")).Approve()), RglCodesWorkflow.UnknownGroup, IssueSeverity.Error);
        AssertIssue(ForException(Exception("EX-0001", "NET-08", Machines("FREMD-01")).Approve()), RglCodesWorkflow.UnknownMachine, IssueSeverity.Warning);
        AssertIssue(ForException(Exception("EX-0001", "NET-08", new RegisterScope([" "], [], false)).Approve()), RglCodesWorkflow.EmptyScope, IssueSeverity.Error);
        AssertIssue(ForException(Exception("EX-0001", "", All).Approve()), RglCodesWorkflow.FieldRequired, IssueSeverity.Error);
    }

    [Fact]
    public void Rechner_mit_Laeufen_sind_bekannt()
    {
        using var plant = new TempPlant();
        Directory.CreateDirectory(Path.Combine(plant.Folder, "runs", "FREMD-01"));
        var issues = Services().Registers.Validate(Set([Exception("EX-0001", "NET-08", Machines("FREMD-01")).Approve()]), Catalogue, plant.Plant, Today);
        Assert.DoesNotContain(issues, i => i.Code == RglCodesWorkflow.UnknownMachine);
    }

    [Fact]
    public void Zuschnitt_hoher_Regel_warnt()
    {
        AssertIssue(Validate(Set(tailoring: [Tailoring("TL-0001", "NET-01", All).Approve()])), RglCodesWorkflow.TailoringHighSeverity, IssueSeverity.Warning);
        Assert.Empty(Validate(Set(tailoring: [Tailoring("TL-0001", "NET-08", All).Approve()])));
    }

    [Fact]
    public void Massnahmen_Pflichtfelder_und_ueberfaellig()
    {
        AssertIssue(Validate(Set(measures: [Measure("M-0001", "NET-08", All, Today.AddDays(-1))])), RglCodesWorkflow.MeasureOverdue, IssueSeverity.Warning);
        Assert.Empty(Validate(Set(measures: [Measure("M-0001", "NET-08", All, Today)])));
        Assert.Empty(Validate(Set(measures: [Measure("M-0001", "NET-08", All, Today.AddDays(-1), MeasureState.Done)])));
        var empty = Measure("M-0001", "NET-08", All, Today.AddDays(9)) with { Action = "", OwnerRole = " " };
        var issues = Validate(Set(measures: [empty]));
        Assert.Equal(2, issues.Count(i => i.Code == RglCodesWorkflow.FieldRequired));
        Assert.Equal(2, RegisterValidator.ExitCode(issues));
    }

    [Fact]
    public void Freigabe_in_der_Zukunft_und_fremde_Anlage()
    {
        AssertIssue(ForException(Exception("EX-0001", "NET-08", All).Approve(Today.AddDays(1), Today.AddMonths(6))), RglCodesWorkflow.ApprovedInFuture,
            IssueSeverity.Warning);
        AssertIssue(Validate(Set([Exception("EX-0001", "NET-08", All).Approve()], plantId: "andere-anlage")), RglCodesWorkflow.PlantIdMismatch,
            IssueSeverity.Warning);
    }

    [Fact]
    public void Befunde_sind_sortiert_und_uebersetzt()
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("de");
            var issues = Validate(Set([Exception("EX-0002", "NET-08", All, justification: "kurz"), Exception("EX-0001", "NET-08", All)]));
            Assert.Equal(issues.OrderBy(i => i.EntryId, StringComparer.Ordinal).ThenBy(i => i.Code, StringComparer.Ordinal).Select(i => i.EntryId + i.Code),
                issues.Select(i => i.EntryId + i.Code));
            Assert.Contains(issues, i => i.Message.Contains("Zeichen", StringComparison.Ordinal));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }
}
