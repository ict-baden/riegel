// SPDX-License-Identifier: MIT
using Riegel.Core.Model;

namespace Riegel.Commands.Alerting;

/// <summary>Eine Verschlechterung: Regel, Schwere und Vergleichsbasis (null = Kennzeichen „Regression“ am Ergebnis).</summary>
internal sealed record Regression(string RuleSetId, string RuleId, Severity Severity, ComparisonBasis? Basis);

/// <summary>
/// Ein ausgewerteter Lauf mit allem, was check und import ausgeben: Vergleiche, Tokens, Exitcode.
/// </summary>
/// <param name="Previous">Voriger Lauf (anderer Export, spec 2.8), null ohne Anlage oder beim ersten Lauf.</param>
/// <param name="VsPrevious">Vergleich mit dem vorigen Lauf.</param>
/// <param name="VsApproved">Vergleich mit dem freigegebenen Stand.</param>
/// <param name="Stored">Der Lauf wurde im Anlagenordner gespeichert.</param>
/// <param name="InPlant">Mit Anlage ausgewertet (Register, Vergleiche).</param>
/// <param name="Source">Export, aus dem der Lauf entstand (Datei, Ordner oder „dieser PC“).</param>
internal sealed record RunAssessment(Run Run, Run? Previous, RunComparison? VsPrevious, RunComparison? VsApproved,
    TokenCounts Counts, IReadOnlyList<Regression> Regressions, IReadOnlyList<RuleResult> NewHigh, bool Stored, bool InPlant,
    string Source, AlertOutcome Outcome)
{
    /// <summary>Offene Befunde (Status fail).</summary>
    public int Open => Math.Max(Run.Results.Count(r => r.Status == ResultStatus.Fail), Run.Summary.Fail);
}

/// <summary>
/// Leitet die Tokens eines Laufs ab (spec 7.7, 2.7). Quelle sind die Ergebnisse mit ihren Kennzeichen, die
/// Zusammenfassung des Laufs und die Vergleiche mit vorigem Lauf und freigegebenem Stand. Wo Ergebnisse und
/// Zusammenfassung verschieden zählen, gilt die größere Zahl – lieber einmal zu oft warnen als einen Befund verschweigen.
/// </summary>
internal static class Assessor
{
    /// <summary>Status, deren Wechsel nach fail eine Verschlechterung ist (spec 2.7).</summary>
    private static readonly ResultStatus[] GoodBefore = [ResultStatus.Pass, ResultStatus.Exception, ResultStatus.NotApplicable, ResultStatus.OutOfScope];

    public static RunAssessment Assess(Run run, Run? previous, RunComparison? vsPrevious, RunComparison? vsApproved,
        AlertPolicy policy, bool stored, bool inPlant, string source)
    {
        var (counts, regressions, newHigh) = Count(run, previous, vsPrevious, vsApproved);
        return new RunAssessment(run, previous, vsPrevious, vsApproved, counts, regressions, newHigh, stored, inPlant, source,
            policy.Evaluate(counts));
    }

    public static (TokenCounts Counts, IReadOnlyList<Regression> Regressions, IReadOnlyList<RuleResult> NewHigh) Count(
        Run run, Run? previous, RunComparison? vsPrevious, RunComparison? vsApproved)
    {
        var results = run.Results;
        var counts = new TokenCounts();

        // Verschlechterungen: Kennzeichen am Ergebnis und echte Verschlechterungen aus den Vergleichen; je Regel einmal
        var regressions = new Dictionary<(string, string), Regression>();
        foreach (var r in results.Where(r => r.Status == ResultStatus.Fail && (r.Flags & ResultFlags.Regression) != 0))
            regressions.TryAdd((r.RuleSetId, r.RuleId), new Regression(r.RuleSetId, r.RuleId, r.Severity, null));
        foreach (var comparison in new[] { vsPrevious, vsApproved })
        {
            if (comparison is null) continue;
            foreach (var change in comparison.StatusChanges)
            {
                if (!IsRegression(change)) continue;
                var severity = results.FirstOrDefault(r => r.RuleSetId == change.RuleSetId && r.RuleId == change.RuleId)?.Severity ?? Severity.Medium;
                regressions.TryAdd((change.RuleSetId, change.RuleId), new Regression(change.RuleSetId, change.RuleId, severity, comparison.Basis));
            }
        }
        var regressionList = regressions.Values
            .OrderByDescending(r => r.Severity).ThenBy(r => r.RuleSetId, StringComparer.Ordinal).ThenBy(r => r.RuleId, StringComparer.Ordinal)
            .ToList();
        counts[AlertToken.Regression] = Math.Max(regressionList.Count, run.Summary.Regressions);

        // Neu offen und hoch: im vorigen Lauf nicht offen (auch neue Regel oder vorher nicht prüfbar); ohne vorigen Lauf alle
        // je Regel der erste Eintrag – doppelte Ergebnisse (Programmfehler) dürfen die Ausgabe nicht verhindern
        var before = previous?.Results.GroupBy(r => (r.RuleSetId, r.RuleId)).ToDictionary(g => g.Key, g => g.First().Status) ?? [];
        var newHigh = results
            .Where(r => r.Status == ResultStatus.Fail && r.Severity == Severity.High)
            .Where(r => !before.TryGetValue((r.RuleSetId, r.RuleId), out var old) || old != ResultStatus.Fail)
            .OrderBy(r => r.RuleSetId, StringComparer.Ordinal).ThenBy(r => r.RuleId, StringComparer.Ordinal)
            .ToList();
        counts[AlertToken.NewHigh] = newHigh.Count;

        var s = run.Summary;
        counts[AlertToken.High] = Math.Max(Open(results, Severity.High), s.OpenHigh);
        counts[AlertToken.Medium] = Math.Max(Open(results, Severity.Medium), s.OpenMedium);
        counts[AlertToken.Low] = Math.Max(Open(results, Severity.Low), s.OpenLow);
        counts[AlertToken.Expired] = Math.Max(Flagged(results, ResultFlags.ExceptionExpired), s.ExceptionsExpired);
        counts[AlertToken.Expiring] = Math.Max(Flagged(results, ResultFlags.ExceptionExpiring), s.ExceptionsExpiring);
        counts[AlertToken.Pending] = Math.Max(
            Flagged(results, ResultFlags.ExceptionPending | ResultFlags.ExceptionChangedAfterApproval | ResultFlags.TailoringPending),
            s.ExceptionsPending);
        counts[AlertToken.Overdue] = Math.Max(Flagged(results, ResultFlags.MeasureOverdue), s.MeasuresOverdue);
        counts[AlertToken.Incomplete] = (run.Flags & RunFlags.Incomplete) != 0 ? 1 : 0;
        counts[AlertToken.Stale] = (run.Flags & RunFlags.StaleSnapshot) != 0 ? 1 : 0;
        counts[AlertToken.Clock] = (run.Flags & RunFlags.ClockSuspect) != 0 ? 1 : 0;
        return (counts, regressionList, newHigh);
    }

    /// <summary>
    /// Echte Verschlechterung (spec 2.7): vorher erfüllt, bewusst offen, nicht anwendbar oder nicht im Prüfumfang, jetzt
    /// nicht erfüllt. Keine Verschlechterung: nur abgelaufene Ausnahme, neue Regel, vorher nicht prüfbar („neu offen“).
    /// </summary>
    public static bool IsRegression(StatusChange change) =>
        change.Kind == StatusChangeKind.Worse &&
        change.To == ResultStatus.Fail &&
        change.From is { } from && GoodBefore.Contains(from) &&
        (change.ToFlags & ResultFlags.ExceptionExpired) == 0 &&
        (change.ToFlags & ResultFlags.NewRule) == 0;

    private static int Open(IReadOnlyList<RuleResult> results, Severity severity) =>
        results.Count(r => r.Status == ResultStatus.Fail && r.Severity == severity);

    private static int Flagged(IReadOnlyList<RuleResult> results, ResultFlags any) =>
        results.Count(r => (r.Flags & any) != 0);
}
