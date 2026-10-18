// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Catalogue;
using Riegel.Core.Evaluation;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.RuleVectors;

/// <summary>
/// Führt die Testvektoren der Regeln aus (spec 2.3 „tests“). Ein Vektor wird zu einem synthetischen Export:
/// Rechnerkopf aus <c>machine</c>, Einträge aus <c>items</c>, ohne <c>coverage</c> volle Abdeckung des
/// Erfassungsplans – dann fehlt ein nicht genannter Planschlüssel nachweislich (KeyMissing), auch ein einzelner.
/// Zeitbasis: erfasst und ausgewertet zu <see cref="FixedClock.Default"/> (2026-10-18T09:00:00Z), Auswertungstag
/// 2026-10-18, updateMaxDays 35, unusedAccountDays 90, ohne ESU.
/// </summary>
internal static class VectorRunner
{
    public static DateTimeOffset Time => FixedClock.Default;

    public static EvaluationContext Context { get; } = new(new DateOnly(2026, 10, 18), FixedClock.Default, null, 35, 90, false, "en");

    private static readonly Lazy<List<CoverageEntry>> Plan = new(SnapshotBuilder.PlanCoverage);

    /// <summary>Der Export zu einem Vektor.</summary>
    public static Snapshot ToSnapshot(RuleTestVector v, string host = "VECTOR")
    {
        var m = v.Machine;
        var machine = new MachineInfo(host, null, m.DomainRole, m.ProductType, m.Build, m.Ubr, m.Edition, m.InstallationType, null, "AMD64",
            null, null, null);
        var items = v.Items.Select(kv => new SnapshotItem(kv.Key, kv.Value.Status, kv.Value.Value, null, null, 0, kv.Value.Truncated)).ToList();
        var coverage = v.Coverage?.ToList() ?? Plan.Value;
        if (v.Coverage is null)
        {
            // Vektor-Semantik: ein nicht genannter einzelner Planschlüssel fehlt nachweislich
            var present = new HashSet<string>(items.Select(i => i.Key), StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Plan.Value.Where(c => !c.Recursive))
            {
                var key = ItemKeys.Registry(entry.Root);
                if (!present.Contains(key)) items.Add(new SnapshotItem(key, ItemStatus.KeyMissing, null, null, null, 0));
            }
        }
        return new Snapshot(SchemaIds.Snapshot, new CollectorInfo("Riegel.Collector", "0.1.0", null, "4.0.30319.42000", true), Time, m.IsAdmin,
            SnapshotOrigin.Synthetic, new CollectionOptions(false, false, false), machine, coverage, items);
    }

    /// <summary>Der Testkatalog der Engine (tests/Riegel.Tests/RuleVectors/TestRules/rules) über den echten Lader.</summary>
    public static ICatalogue TestCatalogue()
    {
        var root = Path.Combine(RepoPaths.TestsProject, "RuleVectors", "TestRules", "rules");
        var files = Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .ToDictionary(f => "rules/" + Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);
        return CatalogueProvider.FromFiles(files);
    }

    /// <summary>Ein Vektor einer Regel: Ergebnis und entscheidende Belege.</summary>
    public static (RuleResult Result, IReadOnlySet<string> Deciding) Run(ICatalogue catalogue, Rule rule, RuleTestVector vector, Snapshot? snapshot = null)
    {
        var evaluator = new Evaluator(null);
        var (result, deciding) = evaluator.EvaluateDetailed(snapshot ?? ToSnapshot(vector), catalogue, Context, vector.Facts, [rule]).Single();
        return (result, deciding);
    }

    /// <summary>Alle Abweichungen der Vektoren eines Katalogs (leer = alle grün).</summary>
    public static List<string> Failures(ICatalogue catalogue, out int count)
    {
        var failures = new List<string>();
        count = 0;
        foreach (var rule in catalogue.Rules.OrderBy(r => r.Id, StringComparer.Ordinal))
            for (int i = 0; i < rule.Tests.Count; i++)
            {
                var vector = rule.Tests[i];
                count++;
                RuleResult result;
                try { result = Run(catalogue, rule, vector).Result; }
                catch (Exception e)
                {
                    failures.Add($"{rule.Id} #{i} „{vector.Name}“: {e.GetType().Name}: {e.Message}");
                    continue;
                }
                var flags = FlagNames(result.Flags);
                var missingFlags = (vector.ExpectFlags ?? []).Where(f => !flags.Contains(f)).ToList();
                if (result.Status != vector.Expect || missingFlags.Count > 0)
                    failures.Add($"{rule.Id} #{i} „{vector.Name}“: erwartet {vector.Expect}{FlagText(vector.ExpectFlags)}, ist {result.Status}" +
                                 $" [{string.Join(",", flags)}] {result.ReasonCode} {string.Join(" | ", result.Details ?? [])}");
            }
        return failures;
    }

    public static IReadOnlyList<string> FlagNames(ResultFlags flags) => ResultFlagsConverter.Names(flags);

    private static string FlagText(IReadOnlyList<string>? flags) => flags is { Count: > 0 } f ? " + " + string.Join(",", f) : "";

    /// <summary>Kurzform eines Werts für Meldungen.</summary>
    public static string Show(JsonElement? v) => v is { } e ? e.GetRawText() : "null";

    public static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
