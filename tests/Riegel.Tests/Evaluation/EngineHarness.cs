// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Catalogue;
using Riegel.Core.Evaluation;
using Riegel.Core.Model;
using Riegel.Tests.RuleVectors;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Evaluation;

/// <summary>Kleine Helfer, um einzelne Regeln mit der echten Engine auszuwerten.</summary>
internal static class EngineHarness
{
    private static readonly Lazy<ICatalogue> Tables = new(VectorRunner.TestCatalogue);

    /// <summary>Auswertungskontext: 2026-10-18, ausgewertet 2026-10-18T09:00:00Z, 35/90 Tage.</summary>
    public static EvaluationContext Context(DateOnly? date = null, bool esu = false, string language = "en", DateTimeOffset? imported = null,
        DateTimeOffset? evaluated = null, int updateMaxDays = 35, int unusedDays = 90) =>
        new(date ?? new DateOnly(2026, 10, 18), evaluated ?? FixedClock.Default, imported, updateMaxDays, unusedDays, esu, language);

    /// <summary>Katalog aus Regeln, mit den Tabellen und Daten des Testkatalogs (erfundene Produkte, Fähigkeiten, Muster).</summary>
    public static ICatalogue Catalogue(params Rule[] rules)
    {
        var t = Tables.Value;
        return CatalogueProvider.FromDocument(new CatalogueDocument(SchemaIds.Catalogue, "riegel", "2026.10", new DateOnly(2026, 10, 18), rules,
            t.Lifecycle, t.Builds, t.Capabilities, t.Data));
    }

    /// <summary>Eine Regel auswerten.</summary>
    public static RuleResult Eval(Rule rule, Snapshot snapshot, EvaluationContext? context = null, IReadOnlyDictionary<string, JsonElement>? facts = null,
        Func<Snapshot, AttributionMap>? attribution = null) =>
        new Evaluator(null, attribution).Evaluate(snapshot, Catalogue(rule), context ?? Context(), facts, null).Single();

    /// <summary>Eine Regel des Testkatalogs nach ID.</summary>
    public static Rule TestRule(string id) => Tables.Value.Find(id) ?? throw new ArgumentException(id);

    /// <summary>Der Testkatalog selbst.</summary>
    public static ICatalogue TestCatalogue => Tables.Value;
}
