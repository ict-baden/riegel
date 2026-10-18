// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Services;

namespace Riegel.Core.Evaluation;

/// <summary>
/// Factory der Auswertung (gehört ab dem Gerüst dem Paket engine; Signatur fest). Der Platzhalter bewertet nichts:
/// jede Regel ist „nicht prüfbar“ mit RGL-0003. So kann nie ein falsches „erfüllt“ entstehen, solange die Engine fehlt.
/// </summary>
public static class EvaluationFactory
{
    public static IEvaluator Create(RiegelServices services) => new StubEvaluator();
}

/// <summary>Platzhalter (Gerüst): NotCheckable für jede Regel.</summary>
internal sealed class StubEvaluator : IEvaluator
{
    public IReadOnlyList<RuleResult> Evaluate(Snapshot s, ICatalogue c, EvaluationContext ctx) =>
        c.Rules
            .OrderBy(r => r.RuleSetId, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new RuleResult(r.RuleSetId, r.Id, ResultStatus.NotCheckable, r.Severity, null, null, ValueSource.Unknown,
                null, [], null, null, null, ResultFlags.None, RglCodesCommon.NotImplemented, null))
            .ToList();
}
