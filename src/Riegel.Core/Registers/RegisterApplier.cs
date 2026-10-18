// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Plants;

namespace Riegel.Core.Registers;

/// <summary>
/// Wendet die Register auf die Rohergebnisse eines Rechners an (spec 2.7, 2.11–2.13):
/// <list type="number">
/// <item>Zuschnitt: gültig → <c>outOfScope</c>; beantragt, geändert oder abgelaufen → normal bewertet mit
/// <c>TailoringPending</c>.</item>
/// <item>Ausnahme (nur bei <c>fail</c>): gültig und Wert passt → <c>exception</c> (bald ablaufend:
/// <c>ExceptionExpiring</c>); Wert weicht ab → <c>ExceptionValueMismatch</c>; beantragt oder ungültig →
/// <c>ExceptionPending</c>; nach Freigabe geändert → zusätzlich <c>ExceptionChangedAfterApproval</c>; abgelaufen →
/// <c>ExceptionExpired</c>.</item>
/// <item>Maßnahme: geplant und offen → <c>MeasurePlanned</c>, Fälligkeit vorbei → <c>MeasureOverdue</c>.</item>
/// </list>
/// Je Regel gewinnt der genaueste Geltungsbereich (Rechner vor Gruppe vor Anlage), bei Gleichstand die jüngste
/// Freigabe. Das Ergebnis ist sortiert (RuleSetId, RuleId) und hängt nur von den Eingaben ab.
/// </summary>
internal static class RegisterApplier
{
    /// <summary>Flags, die nur aus den Registern kommen (werden vor dem Anwenden entfernt).</summary>
    public const ResultFlags FromRegisters =
        ResultFlags.ExceptionExpired | ResultFlags.ExceptionExpiring | ResultFlags.ExceptionPending | ResultFlags.ExceptionValueMismatch |
        ResultFlags.ExceptionChangedAfterApproval | ResultFlags.TailoringPending | ResultFlags.MeasurePlanned | ResultFlags.MeasureOverdue;

    public static IReadOnlyList<RuleResult> Apply(IReadOnlyList<RuleResult> raw, RegisterSet r, Plant p, string host, DateOnly today)
    {
        var group = RegisterRules.GroupOf(p, host);
        var duplicateExceptions = Duplicates(r.Exceptions.Exceptions.Select(e => e.Id));
        var duplicateTailoring = Duplicates(r.Tailoring.Entries.Select(e => e.Id));
        var result = new List<RuleResult>(raw.Count);
        foreach (var input in raw)
        {
            var status = input.Status;
            var flags = input.Flags & ~FromRegisters;
            string? reason = input.ReasonCode;
            string? exceptionId = null, tailoringId = null, measureId = null;

            // Schon angewendete Ergebnisse (erneutes Anwenden): eine Ausnahme setzt ein „nicht erfüllt“ voraus
            if (status == ResultStatus.Exception) status = ResultStatus.Fail;

            // 1. Zuschnitt
            var tailoring = Best(r.Tailoring.Entries, t => t.RuleId, t => t.Scope, t => t.Approval, input.RuleId, host, group);
            bool tailored = false;
            if (tailoring is not null)
            {
                tailoringId = tailoring.Id;
                var state = duplicateTailoring.Contains(tailoring.Id) ? RegisterEntryState.Invalid
                    : RegisterRules.TailoringState(tailoring, input.Severity, p, today);
                if (RegisterRules.InForce(state))
                {
                    status = ResultStatus.OutOfScope;
                    tailored = true;
                }
                else
                {
                    flags |= ResultFlags.TailoringPending;
                }
            }
            if (!tailored && status == ResultStatus.OutOfScope)
            {
                // Rohstatus eines früher zugeschnittenen Ergebnisses ist nicht bekannt
                status = ResultStatus.NotCheckable;
                reason = RglCodesWorkflow.RawStatusUnknown;
            }

            if (!tailored)
            {
                // 2. Ausnahme
                if (status == ResultStatus.Fail)
                {
                    var exception = Best(r.Exceptions.Exceptions, e => e.RuleId, e => e.Scope, e => e.Approval, input.RuleId, host, group);
                    if (exception is not null)
                    {
                        exceptionId = exception.Id;
                        var state = duplicateExceptions.Contains(exception.Id) ? RegisterEntryState.Invalid
                            : RegisterRules.ExceptionState(exception, input.Severity, RuleKind.Check, p, today);
                        switch (state)
                        {
                            case RegisterEntryState.Valid:
                            case RegisterEntryState.Expiring:
                                if (RegisterRules.AcceptedValueMatches(exception.AcceptedValue, input.Actual))
                                {
                                    status = ResultStatus.Exception;
                                    if (state == RegisterEntryState.Expiring) flags |= ResultFlags.ExceptionExpiring;
                                }
                                else
                                {
                                    flags |= ResultFlags.ExceptionValueMismatch;
                                }
                                break;
                            case RegisterEntryState.Expired:
                                flags |= ResultFlags.ExceptionExpired;
                                break;
                            case RegisterEntryState.ChangedAfterApproval:
                                flags |= ResultFlags.ExceptionPending | ResultFlags.ExceptionChangedAfterApproval;
                                break;
                            default:
                                flags |= ResultFlags.ExceptionPending;
                                break;
                        }
                    }
                }

                // 3. Maßnahme
                var measure = BestMeasure(r.Measures.Measures, input.RuleId, host, group);
                if (measure is not null)
                {
                    measureId = measure.Id;
                    if (status == ResultStatus.Fail && measure.State == MeasureState.Planned)
                        flags |= RegisterRules.IsPastDue(measure, today) ? ResultFlags.MeasureOverdue : ResultFlags.MeasurePlanned;
                }
            }

            result.Add(input with
            {
                Status = status, Flags = flags, ExceptionId = exceptionId, TailoringId = tailoringId, MeasureId = measureId, ReasonCode = reason,
            });
        }
        return [.. result.OrderBy(x => x.RuleSetId, StringComparer.Ordinal).ThenBy(x => x.RuleId, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Der wirksame Eintrag einer Regel für einen Rechner: genauester Geltungsbereich, dann jüngstes approvedOn
    /// (beantragte Einträge zuletzt), dann ID – damit das Ergebnis eindeutig ist.
    /// </summary>
    internal static T? Best<T>(IEnumerable<T> entries, Func<T, string> rule, Func<T, RegisterScope> scope, Func<T, Approval> approval,
        string ruleId, string host, string? group) where T : class =>
        entries
            .Where(e => string.Equals(rule(e)?.Trim(), ruleId, StringComparison.OrdinalIgnoreCase))
            .Select(e => (Entry: e, Rank: RegisterRules.Specificity(scope(e), host, group)))
            .Where(x => x.Rank > 0)
            .OrderByDescending(x => x.Rank)
            .ThenByDescending(x => approval(x.Entry).ApprovedOn?.DayNumber ?? int.MinValue)
            .ThenBy(x => Id(x.Entry), StringComparer.Ordinal)
            .Select(x => x.Entry)
            .FirstOrDefault();

    /// <summary>Maßnahme einer Regel für einen Rechner: geplante vor erledigten (verworfene nie), genauester Bereich, früheste Fälligkeit.</summary>
    internal static MeasureEntry? BestMeasure(IEnumerable<MeasureEntry> measures, string ruleId, string host, string? group) =>
        measures
            .Where(m => m.State != MeasureState.Dropped && string.Equals(m.RuleId?.Trim(), ruleId, StringComparison.OrdinalIgnoreCase))
            .Select(m => (Entry: m, Rank: RegisterRules.Specificity(m.Scope, host, group)))
            .Where(x => x.Rank > 0)
            .OrderBy(x => x.Entry.State == MeasureState.Planned ? 0 : 1)
            .ThenByDescending(x => x.Rank)
            .ThenBy(x => x.Entry.DueDate)
            .ThenBy(x => x.Entry.Id, StringComparer.Ordinal)
            .Select(x => x.Entry)
            .FirstOrDefault();

    private static string Id<T>(T entry) => entry switch
    {
        ExceptionEntry e => e.Id,
        TailoringEntry t => t.Id,
        MeasureEntry m => m.Id,
        _ => "",
    };

    private static HashSet<string> Duplicates(IEnumerable<string> ids) =>
        new(ids.GroupBy(i => i, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.OrdinalIgnoreCase);
}
