// SPDX-License-Identifier: MIT
using System.Text.RegularExpressions;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Plants;

namespace Riegel.Core.Registers;

/// <summary>Ein Befund der Registerprüfung, noch ohne Text (der entsteht in der Sprache des Aufrufers).</summary>
internal sealed record Issue(string Code, IssueSeverity Severity, string? Field, params object?[] Args);

/// <summary>
/// Regeln der drei Register (spec 2.11–2.13), an einer Stelle für Prüfung (<c>Validate</c>), Anwendung
/// (<c>Apply</c>) und Registersichten der Berichte – so können Prüfung und Wirkung nie auseinanderlaufen.
/// </summary>
internal static partial class RegisterRules
{
    /// <summary>Mindestlänge von Begründung (Ausnahme) und Grund (Zuschnitt).</summary>
    public const int MinTextLength = 20;

    /// <summary>reviewBy höchstens 36 Monate nach approvedOn (sonst ungültig).</summary>
    public const int MaxReviewMonths = 36;

    /// <summary>Mehr als 12 Monate: Warnung (nur Ausnahmen).</summary>
    public const int WarnReviewMonths = 12;

    /// <summary>acceptedValue „jeder Wert“.</summary>
    public const string AnyValue = "*";

    /// <summary>acceptedValue für „nicht gesetzt“ (kanonisch ist das null; im Register steht die Anzeigeform).</summary>
    public static IReadOnlyList<string> NotSetTokens { get; } = ["(not set)", "(nicht gesetzt)"];

    [GeneratedRegex(@"^EX-\d{4,}$")]
    public static partial Regex ExceptionId();

    [GeneratedRegex(@"^TL-\d{4,}$")]
    public static partial Regex TailoringId();

    [GeneratedRegex(@"^M-\d{4,}$")]
    public static partial Regex MeasureId();

    // ---------------------------------------------------------------- Geltungsbereich

    /// <summary>Gruppe eines Rechners laut Anlage, null ohne Gruppe oder unbekannt.</summary>
    public static string? GroupOf(Plant plant, string host) =>
        plant.Machines.FirstOrDefault(m => SameHost(m.HostName, host))?.GroupId;

    public static bool SameHost(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Wie genau passt der Geltungsbereich auf den Rechner: 3 Rechner, 2 Gruppe, 1 ganze Anlage, 0 gar nicht
    /// (spec 2.11 „der genaueste gewinnt“).
    /// </summary>
    public static int Specificity(RegisterScope scope, string host, string? groupId)
    {
        if (scope.Machines.Any(m => SameHost(m, host))) return 3;
        if (groupId is not null && scope.Groups.Any(g => string.Equals(g?.Trim(), groupId, StringComparison.OrdinalIgnoreCase))) return 2;
        return scope.AllMachines ? 1 : 0;
    }

    /// <summary>Ist der Geltungsbereich leer (keine Rechner, keine Gruppen, nicht die ganze Anlage)?</summary>
    public static bool IsEmpty(RegisterScope scope) =>
        !scope.AllMachines && scope.Machines.All(string.IsNullOrWhiteSpace) && scope.Groups.All(string.IsNullOrWhiteSpace);

    /// <summary>Können beide Bereiche denselben Rechner treffen?</summary>
    public static bool Overlap(RegisterScope a, RegisterScope b, Plant plant)
    {
        if (IsEmpty(a) || IsEmpty(b)) return false;
        if (a.AllMachines || b.AllMachines) return true;
        if (a.Machines.Any(m => b.Machines.Any(n => SameHost(m, n)))) return true;
        if (a.Groups.Any(g => b.Groups.Any(h => string.Equals(g, h, StringComparison.OrdinalIgnoreCase)))) return true;
        bool MachineInGroups(RegisterScope machines, RegisterScope groups) =>
            machines.Machines.Any(m => GroupOf(plant, m) is { } g && groups.Groups.Any(h => string.Equals(g, h, StringComparison.OrdinalIgnoreCase)));
        return MachineInGroups(a, b) || MachineInGroups(b, a);
    }

    // ---------------------------------------------------------------- Zustand der Einträge

    /// <summary>
    /// Zustand einer Ausnahme am Tag <paramref name="today"/> (spec 2.11). <paramref name="severity"/> und
    /// <paramref name="kind"/> kommen aus dem Katalog bzw. aus dem Ergebnis; null = unbekannt (dann entfallen die davon
    /// abhängigen Prüfungen). Befunde landen in <paramref name="issues"/>.
    /// </summary>
    public static RegisterEntryState ExceptionState(ExceptionEntry e, Severity? severity, RuleKind? kind, Plant plant, DateOnly today,
        List<Issue>? issues = null)
    {
        issues ??= [];
        int errors = issues.Count(i => i.Severity == IssueSeverity.Error);
        CommonIssues(e.Id, ExceptionId(), "EX-0001", e.RuleId, e.Scope, issues);
        if (Length(e.Justification) < MinTextLength)
            issues.Add(new(RglCodesWorkflow.TextTooShort, IssueSeverity.Error, "justification", "justification", MinTextLength));
        if (e.Category == ReasonCategory.VendorRequirement && string.IsNullOrWhiteSpace(e.Reference))
            issues.Add(new(RglCodesWorkflow.ReferenceRequired, IssueSeverity.Error, "reference"));
        if (e.Category == ReasonCategory.CompensatingControl && string.IsNullOrWhiteSpace(e.CompensatingMeasure))
            issues.Add(new(RglCodesWorkflow.CompensatingMeasureRequired, IssueSeverity.Error, "compensatingMeasure"));
        if (kind == RuleKind.Check)
        {
            if (string.IsNullOrWhiteSpace(e.AcceptedValue)) issues.Add(new(RglCodesWorkflow.AcceptedValueRequired, IssueSeverity.Error, "acceptedValue"));
            else if (e.AcceptedValue.Trim() == AnyValue) issues.Add(new(RglCodesWorkflow.AcceptedValueAny, IssueSeverity.Warning, "acceptedValue"));
        }
        else if (kind == RuleKind.Inventory)
        {
            issues.Add(new(RglCodesWorkflow.InventoryRule, IssueSeverity.Warning, "ruleId", e.RuleId));
        }
        var roles = plant.Settings.RiskAcceptanceRoles.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        if (e.Category == ReasonCategory.RiskAccepted && severity == Severity.High && roles.Count > 0 &&
            !string.IsNullOrWhiteSpace(e.Approval.ApprovedByRole) &&
            !roles.Any(r => string.Equals(r.Trim(), e.Approval.ApprovedByRole.Trim(), StringComparison.OrdinalIgnoreCase)))
            issues.Add(new(RglCodesWorkflow.RiskAcceptanceRole, IssueSeverity.Error, "approval.approvedByRole",
                string.Join(", ", roles), e.Approval.ApprovedByRole));
        var approval = CheckApproval(e.Approval, ContentHashes.Of(e), today, plant.Settings.WarnDays, warnLong: true, issues);
        return Decide(issues.Count(i => i.Severity == IssueSeverity.Error) > errors, approval);
    }

    /// <summary>Zustand eines Zuschnitts (spec 2.12); Zuschnitt einer hohen Regel ergibt eine Warnung.</summary>
    public static RegisterEntryState TailoringState(TailoringEntry t, Severity? severity, Plant plant, DateOnly today, List<Issue>? issues = null)
    {
        issues ??= [];
        int errors = issues.Count(i => i.Severity == IssueSeverity.Error);
        CommonIssues(t.Id, TailoringId(), "TL-0001", t.RuleId, t.Scope, issues);
        if (Length(t.Reason) < MinTextLength)
            issues.Add(new(RglCodesWorkflow.TextTooShort, IssueSeverity.Error, "reason", "reason", MinTextLength));
        if (severity == Severity.High) issues.Add(new(RglCodesWorkflow.TailoringHighSeverity, IssueSeverity.Warning, "ruleId"));
        var approval = CheckApproval(t.Approval, ContentHashes.Of(t), today, plant.Settings.WarnDays, warnLong: false, issues);
        return Decide(issues.Count(i => i.Severity == IssueSeverity.Error) > errors, approval);
    }

    /// <summary>Befunde einer Maßnahme (spec 2.13): Pflichtfelder, überfällig.</summary>
    public static void MeasureIssues(MeasureEntry m, DateOnly today, List<Issue> issues)
    {
        CommonIssues(m.Id, MeasureId(), "M-0001", m.RuleId, m.Scope, issues);
        if (string.IsNullOrWhiteSpace(m.Action)) issues.Add(new(RglCodesWorkflow.FieldRequired, IssueSeverity.Error, "action", "action"));
        if (string.IsNullOrWhiteSpace(m.OwnerRole)) issues.Add(new(RglCodesWorkflow.FieldRequired, IssueSeverity.Error, "ownerRole", "ownerRole"));
        if (m.State == MeasureState.Planned && m.DueDate < today)
            issues.Add(new(RglCodesWorkflow.MeasureOverdue, IssueSeverity.Warning, "dueDate", Iso(m.DueDate)));
    }

    /// <summary>Ist die Maßnahme überfällig (geplant und Fälligkeit vorbei)? Ob der Befund noch offen ist, prüft der Aufrufer.</summary>
    public static bool IsPastDue(MeasureEntry m, DateOnly today) => m.State == MeasureState.Planned && m.DueDate < today;

    /// <summary>Gilt der Eintrag (gültig oder läuft bald ab)?</summary>
    public static bool InForce(RegisterEntryState state) => state is RegisterEntryState.Valid or RegisterEntryState.Expiring;

    /// <summary>
    /// Passt der Istwert zum akzeptierten Wert? „*“ = jeder Wert, „(not set)“ = nicht gesetzt; Zahlen numerisch
    /// („0“ = „0x0“), sonst ohne Groß/klein wie die Textvergleiche der Engine (spec 2.4).
    /// </summary>
    public static bool AcceptedValueMatches(string? accepted, string? actual)
    {
        if (string.IsNullOrWhiteSpace(accepted)) return false;
        var value = accepted.Trim();
        if (value == AnyValue) return true;
        if (NotSetTokens.Any(t => string.Equals(t, value, StringComparison.OrdinalIgnoreCase))) return actual is null;
        if (actual is null) return false;
        return CanonicalValue.AreEqual(value, actual) || CanonicalValue.AreEqual(accepted, actual) ||
               string.Equals(value, actual.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- intern

    private sealed record ApprovalResult(bool Pending, bool Changed, bool Expired, bool Expiring);

    private static RegisterEntryState Decide(bool invalid, ApprovalResult a) =>
        invalid ? RegisterEntryState.Invalid
        : a.Changed ? RegisterEntryState.ChangedAfterApproval
        : a.Pending ? RegisterEntryState.Pending
        : a.Expired ? RegisterEntryState.Expired
        : a.Expiring ? RegisterEntryState.Expiring
        : RegisterEntryState.Valid;

    private static void CommonIssues(string id, Regex idPattern, string example, string ruleId, RegisterScope scope, List<Issue> issues)
    {
        if (string.IsNullOrWhiteSpace(id) || !idPattern.IsMatch(id)) issues.Add(new(RglCodesWorkflow.IdFormat, IssueSeverity.Error, "id", id, example));
        if (string.IsNullOrWhiteSpace(ruleId)) issues.Add(new(RglCodesWorkflow.FieldRequired, IssueSeverity.Error, "ruleId", "ruleId"));
        if (IsEmpty(scope)) issues.Add(new(RglCodesWorkflow.EmptyScope, IssueSeverity.Error, "scope"));
    }

    /// <summary>
    /// Freigabe prüfen: vollständig (Rolle, Datum, Prüftermin, Nachweis), an den Inhalt gebunden, nicht in der Zukunft,
    /// Prüftermin in den Grenzen, nicht abgelaufen.
    /// </summary>
    private static ApprovalResult CheckApproval(Approval a, string contentSha, DateOnly today, int warnDays, bool warnLong, List<Issue> issues)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(a.ApprovedByRole)) missing.Add("approvedByRole");
        if (a.ApprovedOn is null) missing.Add("approvedOn");
        if (a.ReviewBy is null) missing.Add("reviewBy");
        if (string.IsNullOrWhiteSpace(a.ApprovalRecord)) missing.Add("approvalRecord");
        bool pending = false, changed = false, expired = false, expiring = false;
        if (missing.Count > 0)
        {
            issues.Add(new(RglCodesWorkflow.ApprovalPending, IssueSeverity.Warning, "approval", string.Join(", ", missing)));
            pending = true;
        }
        else if (string.IsNullOrWhiteSpace(a.ApprovedContentSha256))
        {
            issues.Add(new(RglCodesWorkflow.ApprovalNotBound, IssueSeverity.Warning, "approval.approvedContentSha256"));
            pending = true;
        }
        else if (!string.Equals(a.ApprovedContentSha256.Trim(), contentSha, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new(RglCodesWorkflow.ChangedAfterApproval, IssueSeverity.Warning, "approval.approvedContentSha256"));
            changed = true;
        }
        if (a.ApprovedOn is { } on && on > today)
        {
            issues.Add(new(RglCodesWorkflow.ApprovedInFuture, IssueSeverity.Warning, "approval.approvedOn", Iso(on)));
            pending = true;
        }
        if (a.ApprovedOn is { } approvedOn && a.ReviewBy is { } reviewBy)
        {
            if (reviewBy < approvedOn)
                issues.Add(new(RglCodesWorkflow.ReviewByBeforeApproval, IssueSeverity.Error, "approval.reviewBy", Iso(reviewBy), Iso(approvedOn)));
            else if (reviewBy > approvedOn.AddMonths(MaxReviewMonths))
                issues.Add(new(RglCodesWorkflow.ReviewByTooLate, IssueSeverity.Error, "approval.reviewBy", Iso(reviewBy), MaxReviewMonths, Iso(approvedOn)));
            else if (warnLong && reviewBy > approvedOn.AddMonths(WarnReviewMonths))
                issues.Add(new(RglCodesWorkflow.ReviewByLong, IssueSeverity.Warning, "approval.reviewBy", Iso(reviewBy), WarnReviewMonths, Iso(approvedOn)));
        }
        if (missing.Count == 0 && !pending && !changed && a.ReviewBy is { } until)
        {
            if (today > until)
            {
                issues.Add(new(RglCodesWorkflow.Expired, IssueSeverity.Warning, "approval.reviewBy", Iso(until)));
                expired = true;
            }
            else if (until.DayNumber - today.DayNumber <= warnDays)
            {
                issues.Add(new(RglCodesWorkflow.Expiring, IssueSeverity.Warning, "approval.reviewBy", Iso(until), until.DayNumber - today.DayNumber));
                expiring = true;
            }
        }
        return new ApprovalResult(pending, changed, expired, expiring);
    }

    private static int Length(string? text) => text?.Trim().Length ?? 0;

    internal static string Iso(DateOnly date) => FileNames.Date(date);
}
