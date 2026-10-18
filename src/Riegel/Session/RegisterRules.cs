// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;

namespace Riegel.Session;

/// <summary>
/// Regeln für Registereinträge, wie die Oberfläche sie anzeigt und in den Dialogen prüft (spec 2.11–2.13):
/// Geltungsbereich, Zustand einer Freigabe, Pflichtfelder. Verbindlich bewertet der Arbeitsablauf
/// (<c>IRegisterService.Apply</c>/<c>Validate</c>); hier geht es um sofortige Rückmeldung beim Bearbeiten und um die
/// Filter der Register-Seiten.
/// </summary>
internal static class RegisterRules
{
    /// <summary>Mindestlänge von Begründung und Grund (spec 2.11, 2.12).</summary>
    public const int MinTextLength = 20;

    /// <summary>Prüftermin höchstens 36 Monate, Warnung ab 12 Monaten nach der Freigabe.</summary>
    public const int MaxReviewMonths = 36;
    public const int WarnReviewMonths = 12;

    /// <summary>Gilt der Geltungsbereich für diesen Rechner?</summary>
    public static bool InScope(RegisterScope scope, string host, string? groupId) =>
        scope.AllMachines
        || scope.Machines.Contains(host, StringComparer.OrdinalIgnoreCase)
        || (groupId is not null && scope.Groups.Contains(groupId, StringComparer.OrdinalIgnoreCase));

    /// <summary>Wie genau ein Bereich auf den Rechner passt: Rechner 3, Gruppe 2, Anlage 1, sonst 0.</summary>
    public static int Specificity(RegisterScope scope, string host, string? groupId) =>
        scope.Machines.Contains(host, StringComparer.OrdinalIgnoreCase) ? 3
        : groupId is not null && scope.Groups.Contains(groupId, StringComparer.OrdinalIgnoreCase) ? 2
        : scope.AllMachines ? 1 : 0;

    /// <summary>Rechner der Anlage, für die der Bereich gilt.</summary>
    public static IReadOnlyList<string> Hosts(RegisterScope scope, Plant plant, IEnumerable<string>? knownHosts = null)
    {
        var all = plant.Machines.Select(m => m.HostName).Concat(knownHosts ?? []).Distinct(StringComparer.OrdinalIgnoreCase);
        return [.. all.Where(h => InScope(scope, h, GroupOf(plant, h))).Order(StringComparer.OrdinalIgnoreCase)];
    }

    public static string? GroupOf(Plant plant, string host) =>
        plant.Machines.FirstOrDefault(m => string.Equals(m.HostName, host, StringComparison.OrdinalIgnoreCase))?.GroupId;

    /// <summary>Freigabe vollständig (Rolle, Datum, Prüftermin, Nachweis)?</summary>
    public static bool Complete(Approval a) =>
        !string.IsNullOrWhiteSpace(a.ApprovedByRole) && a.ApprovedOn is not null && a.ReviewBy is not null && !string.IsNullOrWhiteSpace(a.ApprovalRecord);

    /// <summary>
    /// Zustand eines Eintrags mit Freigabe: beantragt, nach Freigabe geändert, abgelaufen, läuft bald ab, gültig. Ungültig
    /// kommt aus der Prüfung (<see cref="IssuesFor(ExceptionEntry, Rule?, PlantSettings)"/>).
    /// </summary>
    public static RegisterEntryState StateOf(Approval a, string contentSha256, DateOnly today, int warnDays)
    {
        if (!Complete(a)) return RegisterEntryState.Pending;
        if (a.ApprovedContentSha256 is { Length: > 0 } hash && !string.Equals(hash, contentSha256, StringComparison.OrdinalIgnoreCase))
            return RegisterEntryState.ChangedAfterApproval;
        if (a.ReviewBy < today) return RegisterEntryState.Expired;
        if (a.ReviewBy <= today.AddDays(warnDays)) return RegisterEntryState.Expiring;
        return RegisterEntryState.Valid;
    }

    /// <summary>Wirksam für die Auswertung (gültig oder läuft bald ab)?</summary>
    public static bool Effective(RegisterEntryState state) => state is RegisterEntryState.Valid or RegisterEntryState.Expiring;

    private static ValidationIssue Issue(RegisterKind kind, string? id, IssueSeverity severity, string code, string? field) =>
        new(kind, id, severity, code, field, RglCodesApp.Text(code));

    /// <summary>Pflichtfelder und Grenzen einer Ausnahme (spec 2.11).</summary>
    public static IReadOnlyList<ValidationIssue> IssuesFor(ExceptionEntry e, Rule? rule, PlantSettings settings)
    {
        var issues = new List<ValidationIssue>();
        void Add(IssueSeverity s, string code, string? field) => issues.Add(Issue(RegisterKind.Exceptions, e.Id, s, code, field));
        if (rule is null) Add(IssueSeverity.Warning, RglCodesApp.UnknownRule, "ruleId");
        if (!e.Scope.AllMachines && e.Scope.Machines.Count == 0 && e.Scope.Groups.Count == 0) Add(IssueSeverity.Error, RglCodesApp.ScopeEmpty, "scope");
        if ((e.Justification ?? "").Trim().Length < MinTextLength) Add(IssueSeverity.Error, RglCodesApp.JustificationTooShort, "justification");
        if (e.Category == ReasonCategory.VendorRequirement && string.IsNullOrWhiteSpace(e.Reference)) Add(IssueSeverity.Error, RglCodesApp.ReferenceRequired, "reference");
        if (e.Category == ReasonCategory.CompensatingControl && string.IsNullOrWhiteSpace(e.CompensatingMeasure))
            Add(IssueSeverity.Error, RglCodesApp.CompensatingRequired, "compensatingMeasure");
        if (rule?.Kind != RuleKind.Inventory)
        {
            if (string.IsNullOrWhiteSpace(e.AcceptedValue)) Add(IssueSeverity.Error, RglCodesApp.AcceptedValueRequired, "acceptedValue");
            else if (e.AcceptedValue.Trim() == "*") Add(IssueSeverity.Warning, RglCodesApp.AcceptedValueAny, "acceptedValue");
        }
        if (e.Category == ReasonCategory.RiskAccepted && rule?.Severity == Severity.High && settings.RiskAcceptanceRoles.Count > 0
            && e.Approval.ApprovedByRole is { } role && !settings.RiskAcceptanceRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            Add(IssueSeverity.Error, RglCodesApp.RiskRoleNotAllowed, "approvedByRole");
        issues.AddRange(ApprovalIssues(RegisterKind.Exceptions, e.Id, e.Approval));
        return issues;
    }

    /// <summary>Pflichtfelder eines Zuschnitts (spec 2.12).</summary>
    public static IReadOnlyList<ValidationIssue> IssuesFor(TailoringEntry t, Rule? rule)
    {
        var issues = new List<ValidationIssue>();
        void Add(IssueSeverity s, string code, string? field) => issues.Add(Issue(RegisterKind.Tailoring, t.Id, s, code, field));
        if (rule is null) Add(IssueSeverity.Warning, RglCodesApp.UnknownRule, "ruleId");
        if (!t.Scope.AllMachines && t.Scope.Machines.Count == 0 && t.Scope.Groups.Count == 0) Add(IssueSeverity.Error, RglCodesApp.ScopeEmpty, "scope");
        if ((t.Reason ?? "").Trim().Length < MinTextLength) Add(IssueSeverity.Error, RglCodesApp.ReasonTooShort, "reason");
        if (rule?.Severity == Severity.High) Add(IssueSeverity.Warning, RglCodesApp.TailoringHighSeverity, "ruleId");
        issues.AddRange(ApprovalIssues(RegisterKind.Tailoring, t.Id, t.Approval));
        return issues;
    }

    /// <summary>Pflichtfelder einer Maßnahme (spec 2.13).</summary>
    public static IReadOnlyList<ValidationIssue> IssuesFor(MeasureEntry m, Rule? rule)
    {
        var issues = new List<ValidationIssue>();
        void Add(IssueSeverity s, string code, string? field) => issues.Add(Issue(RegisterKind.Measures, m.Id, s, code, field));
        if (rule is null) Add(IssueSeverity.Warning, RglCodesApp.UnknownRule, "ruleId");
        if (!m.Scope.AllMachines && m.Scope.Machines.Count == 0 && m.Scope.Groups.Count == 0) Add(IssueSeverity.Error, RglCodesApp.ScopeEmpty, "scope");
        if (string.IsNullOrWhiteSpace(m.Action)) Add(IssueSeverity.Error, RglCodesApp.ActionRequired, "action");
        if (string.IsNullOrWhiteSpace(m.OwnerRole)) Add(IssueSeverity.Error, RglCodesApp.OwnerRoleRequired, "ownerRole");
        return issues;
    }

    /// <summary>Grenzen einer eingetragenen Freigabe; eine fehlende Freigabe ist kein Fehler (der Eintrag ist dann beantragt).</summary>
    public static IEnumerable<ValidationIssue> ApprovalIssues(RegisterKind kind, string? id, Approval a)
    {
        if (a.ApprovedOn is { } on && a.ReviewBy is { } review)
        {
            if (review < on) yield return Issue(kind, id, IssueSeverity.Error, RglCodesApp.ReviewBeforeApproval, "reviewBy");
            else if (review > on.AddMonths(MaxReviewMonths)) yield return Issue(kind, id, IssueSeverity.Error, RglCodesApp.ReviewTooFar, "reviewBy");
            else if (review > on.AddMonths(WarnReviewMonths)) yield return Issue(kind, id, IssueSeverity.Warning, RglCodesApp.ReviewOverYear, "reviewBy");
        }
    }

    /// <summary>Prüfung einer Freigabe, wie sie der Dialog „Genehmigung eintragen“ verlangt.</summary>
    public static IReadOnlyList<string> ApprovalErrors(string? role, DateOnly? on, DateOnly? reviewBy, string? record, out IReadOnlyList<string> warnings)
    {
        var errors = new List<string>();
        var warn = new List<string>();
        if (string.IsNullOrWhiteSpace(role)) errors.Add(RglCodesApp.ApprovalRoleRequired);
        if (string.IsNullOrWhiteSpace(record)) errors.Add(RglCodesApp.ApprovalRecordRequired);
        if (on is null || reviewBy is null) errors.Add(RglCodesApp.ApprovalIncomplete);
        else
        {
            foreach (var issue in ApprovalIssues(RegisterKind.Exceptions, null, new Approval(role, null, on, reviewBy, record, null)))
                (issue.Severity == IssueSeverity.Error ? errors : warn).Add(issue.Code);
        }
        warnings = warn;
        return errors;
    }

    /// <summary>Nächste freie Kennung (EX-0001, TL-0001, M-0001).</summary>
    public static string NextId(string prefix, IEnumerable<string> existing)
    {
        int max = 0;
        foreach (var id in existing)
        {
            if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(id[prefix.Length..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n))
                max = Math.Max(max, n);
        }
        return prefix + (max + 1).ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
    }
}
