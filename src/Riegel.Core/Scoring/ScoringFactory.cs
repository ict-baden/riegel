// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Services;

namespace Riegel.Core.Scoring;

/// <summary>
/// Factory der Wertung (gehört ab dem Gerüst dem Paket workflow; Signatur fest). Der Platzhalter zählt und rechnet
/// Härtungsgrad, Dokumentiert, Kategoriewerte und Prüfumfang nach spec 2.6 in einfacher Form (Regressionen,
/// Register-Zählungen und Grenzfälle kommen mit dem Paket workflow).
/// </summary>
public static class ScoringFactory
{
    public static IScoring Create(RiegelServices services) => new StubScoring();
}

/// <summary>Platzhalter (Gerüst).</summary>
internal sealed class StubScoring : IScoring
{
    private const decimal AuditCap = 12m;

    public (RunSummary, IReadOnlyList<CategoryScore>) Score(IReadOnlyList<RuleResult> r, ICatalogue c, Run? previous, bool isDc)
    {
        int Count(ResultStatus s) => r.Count(x => x.Status == s);
        int Open(Severity s) => r.Count(x => x.Status == ResultStatus.Fail && x.Severity == s);
        static decimal Weight(Severity s) => s switch { Severity.High => 3, Severity.Medium => 2, _ => 1 };
        bool Considered(RuleResult x) => x.Status is ResultStatus.Pass or ResultStatus.Fail or ResultStatus.Exception;
        string CategoryOf(RuleResult x) => c.Find(x.RuleId)?.Category ?? "system";

        var auditWeight = r.Where(x => Considered(x) && CategoryOf(x) == RuleCategories.Audit).Sum(x => Weight(x.Severity));
        var factor = auditWeight > AuditCap ? AuditCap / auditWeight : 1m;
        decimal W(RuleResult x) => Weight(x.Severity) * (CategoryOf(x) == RuleCategories.Audit ? factor : 1m);

        var considered = r.Where(Considered).ToList();
        var total = considered.Sum(W);
        int? score = null, documented = null;
        if (!isDc && total > 0)
        {
            score = (int)Math.Round(100m * considered.Where(x => x.Status == ResultStatus.Pass).Sum(W) / total, MidpointRounding.AwayFromZero);
            documented = (int)Math.Round(100m * considered.Where(x => x.Status != ResultStatus.Fail).Sum(W) / total, MidpointRounding.AwayFromZero);
        }
        int applicable = r.Count - Count(ResultStatus.NotApplicable) - Count(ResultStatus.OutOfScope) - Count(ResultStatus.Info);
        int checkedShare = applicable > 0 ? (int)Math.Round(100m * considered.Count / applicable, MidpointRounding.AwayFromZero) : 0;

        var categories = RuleCategories.All.Select(category =>
        {
            var inCategory = considered.Where(x => CategoryOf(x) == category).ToList();
            var weight = inCategory.Sum(x => Weight(x.Severity));
            int? value = weight > 0 ? (int)Math.Round(100m * inCategory.Where(x => x.Status == ResultStatus.Pass).Sum(x => Weight(x.Severity)) / weight, MidpointRounding.AwayFromZero) : null;
            bool capped = category == RuleCategories.Audit && factor < 1m;
            return new CategoryScore(category, inCategory.Count(x => x.Status == ResultStatus.Pass), inCategory.Count(x => x.Status == ResultStatus.Fail),
                inCategory.Count(x => x.Status == ResultStatus.Exception), value, (double)(capped ? AuditCap : weight), capped);
        }).ToList();

        var summary = new RunSummary(Count(ResultStatus.Pass), Count(ResultStatus.Fail), Count(ResultStatus.Exception),
            Count(ResultStatus.OutOfScope), Count(ResultStatus.NotApplicable), Count(ResultStatus.NotCheckable), Count(ResultStatus.Info),
            Open(Severity.High), Open(Severity.Medium), Open(Severity.Low), 0, 0, 0, 0, 0, 0, score, documented, checkedShare);
        return (summary, categories);
    }
}
