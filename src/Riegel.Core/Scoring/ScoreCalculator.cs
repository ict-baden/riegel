// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Services;

namespace Riegel.Core.Scoring;

/// <summary>
/// Wertung nach spec 2.6. Gewichte hoch 3, mittel 2, niedrig 1 über die prüfbaren Ergebnisse (pass, fail, exception):
/// <list type="bullet">
/// <item>Härtungsgrad = round(100 × Σw(pass) / Σw(pass + fail + exception)); Ausnahmen heben ihn nie.</item>
/// <item>Dokumentiert = round(100 × Σw(pass + exception) / Σw(pass + fail + exception)).</item>
/// <item>Deckel: die Kategorie audit trägt höchstens 12 Gewichtspunkte bei, f = min(1, 12 / Σw(audit)).</item>
/// <item>Kategoriewerte ungedeckelt innerhalb der Kategorie; Prüfumfang = prüfbar / (anwendbar − outOfScope − info).</item>
/// <item>Dezimalrechnung, am Ende MidpointRounding.AwayFromZero; Domänencontroller: kein Härtungsgrad.</item>
/// </list>
/// Wert, Kategorien und Prüfumfang gelten für den eigenen Katalog (RuleSetId „riegel“); Kunden-Baselines haben ihren
/// eigenen Abschnitt (spec 2.15). Die Zählungen umfassen alle Ergebnisse.
/// </summary>
internal sealed class ScoreCalculator : IScoring
{
    public const string OwnRuleSet = "riegel";
    public const decimal AuditCap = 12m;

    public static decimal Weight(Severity s) => s switch { Severity.High => 3m, Severity.Medium => 2m, _ => 1m };

    public (RunSummary, IReadOnlyList<CategoryScore>) Score(IReadOnlyList<RuleResult> r, ICatalogue c, Run? previous, bool isDc)
    {
        var totals = Totals.Of(r, c);
        int Count(ResultStatus s) => r.Count(x => x.Status == s);
        int Open(Severity s) => r.Count(x => x.Status == ResultStatus.Fail && x.Severity == s);
        int Flagged(ResultFlags f) => r.Count(x => (x.Flags & f) != 0);

        int newlyOpen = 0;
        if (previous is not null)
        {
            var before = previous.Results.GroupBy(x => (x.RuleSetId, x.RuleId)).ToDictionary(g => g.Key, g => g.First().Status);
            newlyOpen = r.Count(x => x.Status == ResultStatus.Fail &&
                (!before.TryGetValue((x.RuleSetId, x.RuleId), out var old) || old == ResultStatus.NotCheckable));
        }
        var summary = new RunSummary(Count(ResultStatus.Pass), Count(ResultStatus.Fail), Count(ResultStatus.Exception),
            Count(ResultStatus.OutOfScope), Count(ResultStatus.NotApplicable), Count(ResultStatus.NotCheckable), Count(ResultStatus.Info),
            Open(Severity.High), Open(Severity.Medium), Open(Severity.Low), Flagged(ResultFlags.Regression), newlyOpen,
            r.Count(x => x.Status == ResultStatus.Exception && (x.Flags & ResultFlags.ExceptionExpiring) != 0), Flagged(ResultFlags.ExceptionExpired),
            Flagged(ResultFlags.ExceptionPending), Flagged(ResultFlags.MeasureOverdue),
            isDc ? null : totals.Score, isDc ? null : totals.Documented, totals.CheckedShare);
        return (summary, totals.Categories(isDc));
    }

    /// <summary>Kategorie einer Regel laut Katalog; unbekannt → „system“.</summary>
    public static string CategoryOf(RuleResult x, ICatalogue c)
    {
        var category = c.Find(x.RuleId)?.Category;
        return category is not null && RuleCategories.IsKnown(category) ? category : "system";
    }

    /// <summary>Gewichtete Summen eines Laufs; addierbar über Rechner (Werte der Anlage, spec 5).</summary>
    internal sealed class Totals
    {
        private readonly Dictionary<string, CategorySums> _categories = RuleCategories.All.ToDictionary(c => c, _ => new CategorySums(), StringComparer.Ordinal);

        /// <summary>Gedeckelte Summen über alle Kategorien (Zähler/Nenner des Härtungsgrads).</summary>
        public decimal PassScaled { get; private set; }
        public decimal ExceptionScaled { get; private set; }
        public decimal ConsideredScaled { get; private set; }
        public int Checkable { get; private set; }
        public int Denominator { get; private set; }
        public bool Capped { get; private set; }

        public static Totals Of(IReadOnlyList<RuleResult> results, ICatalogue c)
        {
            var t = new Totals();
            foreach (var x in results.Where(x => x.RuleSetId == OwnRuleSet))
            {
                if (x.Status is ResultStatus.NotApplicable or ResultStatus.OutOfScope or ResultStatus.Info) continue;
                t.Denominator++;
                if (x.Status is not (ResultStatus.Pass or ResultStatus.Fail or ResultStatus.Exception)) continue;
                t.Checkable++;
                var sums = t._categories[CategoryOf(x, c)];
                var w = Weight(x.Severity);
                sums.Considered += w;
                switch (x.Status)
                {
                    case ResultStatus.Pass: sums.Pass += w; sums.PassCount++; break;
                    case ResultStatus.Fail: sums.FailCount++; break;
                    default: sums.Exception += w; sums.ExceptionCount++; break;
                }
            }
            t.Scale();
            return t;
        }

        private void Scale()
        {
            PassScaled = ExceptionScaled = ConsideredScaled = 0;
            foreach (var (category, sums) in _categories)
            {
                // erst multiplizieren, dann teilen: 54 · 12 / 54 ergibt genau 12
                bool capped = category == RuleCategories.Audit && sums.Considered > AuditCap;
                decimal Scaled(decimal w) => capped ? w * AuditCap / sums.Considered : w;
                sums.Factor = capped ? AuditCap / sums.Considered : 1m;
                sums.ScaledWeight = Scaled(sums.Considered);
                PassScaled += Scaled(sums.Pass);
                ExceptionScaled += Scaled(sums.Exception);
                ConsideredScaled += sums.ScaledWeight;
            }
            Capped = _categories[RuleCategories.Audit].Factor < 1m;
        }

        /// <summary>Summen mehrerer Läufe (Deckel je Rechner, dann addiert).</summary>
        public static Totals Sum(IEnumerable<Totals> runs)
        {
            var t = new Totals();
            foreach (var run in runs)
            {
                foreach (var (category, sums) in run._categories)
                {
                    var target = t._categories[category];
                    target.Pass += sums.Pass;
                    target.Exception += sums.Exception;
                    target.Considered += sums.Considered;
                    target.ScaledWeight += sums.ScaledWeight;
                    target.PassCount += sums.PassCount;
                    target.FailCount += sums.FailCount;
                    target.ExceptionCount += sums.ExceptionCount;
                    target.AnyCapped |= sums.Factor < 1m || sums.AnyCapped;
                }
                t.PassScaled += run.PassScaled;
                t.ExceptionScaled += run.ExceptionScaled;
                t.ConsideredScaled += run.ConsideredScaled;
                t.Checkable += run.Checkable;
                t.Denominator += run.Denominator;
                t.Capped |= run.Capped;
            }
            t._summed = true;
            return t;
        }

        private bool _summed;

        public int? Score => ConsideredScaled > 0 ? Percent(PassScaled, ConsideredScaled) : null;
        public int? Documented => ConsideredScaled > 0 ? Percent(PassScaled + ExceptionScaled, ConsideredScaled) : null;
        public int CheckedShare => Denominator > 0 ? Percent(Checkable, Denominator) : 0;

        /// <summary>Kategoriewerte in Berichtsreihenfolge; Gewicht = Beitrag zum Gesamtwert (audit gedeckelt).</summary>
        public IReadOnlyList<CategoryScore> Categories(bool noScore) =>
        [
            .. RuleCategories.All.Select(category =>
            {
                var s = _categories[category];
                int? value = noScore || s.Considered == 0 ? null : Percent(s.Pass, s.Considered);
                var weight = s.ScaledWeight;
                bool capped = _summed ? s.AnyCapped : s.Factor < 1m;
                return new CategoryScore(category, s.PassCount, s.FailCount, s.ExceptionCount, value, (double)weight, capped);
            }),
        ];

        public static int Percent(decimal part, decimal whole) => (int)Math.Round(100m * part / whole, MidpointRounding.AwayFromZero);

        private sealed class CategorySums
        {
            public decimal Pass;
            public decimal Exception;
            public decimal Considered;
            public decimal Factor = 1m;
            public decimal ScaledWeight;
            public int PassCount;
            public int FailCount;
            public int ExceptionCount;
            public bool AnyCapped;
        }
    }
}
