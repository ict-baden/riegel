// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Plants;

namespace Riegel.Core.Registers;

/// <summary>
/// Freigaben nur für gültige Einträge und dieselbe Sicht auf „ungültig“ in Liste und Prüfung (Prüfung vom 2026-10-20:
/// <c>approve</c> gab eine Ausnahme ohne akzeptierten Wert frei, <c>list</c> zeigte sie danach als gültig, während
/// <c>validate</c> sie ungültig nannte und die Auswertung sie nicht anwandte).
/// </summary>
public static class RegisterApproval
{
    /// <summary>
    /// Wirft RGL-0448 mit dem ersten Fehler, den einer der Einträge <paramref name="ids"/> nach der Freigabe noch hat
    /// (<paramref name="issuesAfterApproval"/> = Prüfung der Register mit eingetragener Freigabe).
    /// </summary>
    public static void ThrowIfInvalid(IEnumerable<ValidationIssue> issuesAfterApproval, RegisterKind kind, IEnumerable<string> ids)
    {
        var wanted = new HashSet<string>(ids.Select(i => i.Trim()), StringComparer.OrdinalIgnoreCase);
        var first = issuesAfterApproval.FirstOrDefault(i => i.Register == kind && i.Severity == IssueSeverity.Error && i.EntryId is { } id && wanted.Contains(id));
        if (first is not null) throw RglCodesWorkflow.Error(RglCodesWorkflow.InvalidNotApproved, first.EntryId, first.Code, first.Message);
    }

    /// <summary>IDs der Einträge eines Registers mit mindestens einem Fehler der Prüfung (Zustand „ungültig“).</summary>
    public static IReadOnlySet<string> InvalidIds(IEnumerable<ValidationIssue> issues, RegisterKind kind) =>
        issues.Where(i => i.Register == kind && i.Severity == IssueSeverity.Error && i.EntryId is not null)
            .Select(i => i.EntryId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
