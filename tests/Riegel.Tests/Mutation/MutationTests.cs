// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using Riegel.Core.Evaluation;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;
using Riegel.Tests.Infrastructure;
using Riegel.Tests.RuleVectors;
using Riegel.Tests.TestSupport;
using Xunit.Abstractions;

namespace Riegel.Tests.Mutation;

/// <summary>
/// Mutationstest (spec 8.3): für jede Regel und jeden Beleg, den sie liest, wird der Eintrag auf Denied,
/// NotCollected, Truncated oder Error gesetzt – das Ergebnis darf dann nie ein „erfüllt“ sein, das auf diesem Beleg
/// beruht (ein anderer, unveränderter Beleg darf weiter tragen, z. B. die Konfiguration in firstOf). Danach alle
/// Belege zugleich. Auf KeyMissing gesetzt darf ein „erfüllt“ nur bleiben mit belegtem Standardwert (FromDefault)
/// oder bei Regeln in der Erlaubnisliste (whenMissing pass, notExists, ifAbsent pass). Ausgangslagen: jeder
/// Testvektor der Regel und jede synthetische Fixture.
/// </summary>
public sealed class MutationTests(ITestOutputHelper output)
{
    private static readonly ItemStatus[] Unavailable = [ItemStatus.Denied, ItemStatus.NotCollected, ItemStatus.Error];

    /// <summary>
    /// Eigene Testregeln, die ein „erfüllt“ aus nachweislichem Fehlen ableiten dürfen (gilt für die Engine-Tests; die
    /// Regelpakete führen ihre Liste in tests/Riegel.Tests/Rules/{A,B,C}/when-missing-pass.txt).
    /// </summary>
    private static readonly HashSet<string> TestAllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        "TREG-03", // whenMissing pass mit missingPass
        "TREG-10", // notExists
        "TSYS-01", // ifAbsent pass: Dienstschlüssel nachweislich nicht vorhanden (Bordmittel-Abdeckung)
        "TSYS-03", // ifAbsent pass
    };

    /// <summary>Negativkontrolle: not/noneOf über whenMissing fail – der Test muss das als „erfüllt aus Fehlen“ melden.</summary>
    private static readonly HashSet<string> NegativeControls = new(StringComparer.OrdinalIgnoreCase) { "TCMP-05", "TCMP-06" };

    private static readonly Lazy<IReadOnlyList<Snapshot>> Fixtures = new(() =>
        [.. Directory.EnumerateFiles(RepoPaths.SyntheticFixtures, "*.snapshot.json").Order(StringComparer.Ordinal)
            .Select(f => RiegelJson.ReadFile<Snapshot>(f, 64L * 1024 * 1024))]);

    [Fact]
    public void Kein_Erfuellt_aus_fehlenden_Belegen_im_eingebetteten_Katalog()
    {
        var catalogue = RiegelServices.Create(new NullPlatform(), new FixedClock()).Catalogue.LoadEmbedded();
        var violations = Violations(catalogue, catalogue.Rules, CatalogueInvariantTests.WhenMissingPassAllowList(), out var runs);
        output.WriteLine($"{catalogue.Rules.Count} Regeln, {runs} Auswertungen");
        // Seit der Integration enthält der eingebettete Katalog alle 153 Regeln von 2026.10
        Assert.Equal(153, catalogue.Rules.Count);
        Assert.True(runs > 10_000, $"nur {runs} Auswertungen");
        Assert.True(violations.Count == 0, $"{violations.Count} Verstöße:\n" + string.Join("\n", violations.Take(60)));
    }

    [Fact]
    public void Kein_Erfuellt_aus_fehlenden_Belegen_in_den_Testregeln()
    {
        var catalogue = VectorRunner.TestCatalogue();
        var rules = catalogue.Rules.Where(r => !NegativeControls.Contains(r.Id)).ToList();
        var violations = Violations(catalogue, rules, TestAllowList, out var runs);
        output.WriteLine($"{rules.Count} Testregeln, {runs} Auswertungen");
        Assert.True(runs > 10_000, $"nur {runs} Auswertungen");
        Assert.True(violations.Count == 0, $"{violations.Count} Verstöße:\n" + string.Join("\n", violations.Take(60)));
    }

    [Fact]
    public void Negativkontrolle_der_Test_erkennt_ein_Erfuellt_aus_Fehlen()
    {
        var catalogue = VectorRunner.TestCatalogue();
        foreach (var id in NegativeControls)
        {
            var violations = Violations(catalogue, [catalogue.Find(id)!], new HashSet<string>(), out _);
            Assert.Contains(violations, v => v.Contains("KeyMissing", StringComparison.Ordinal));
        }
    }

    /// <summary>Alle Verstöße gegen spec 8.3 für die genannten Regeln.</summary>
    internal static List<string> Violations(ICatalogue catalogue, IReadOnlyList<Rule> rules, IReadOnlySet<string> allowList, out int runs)
    {
        var violations = new ConcurrentBag<string>();
        int count = 0;
        Parallel.ForEach(rules.Where(r => r.State == RuleState.Active), rule =>
        {
            var evaluator = new Evaluator(null);
            var bases = rule.Tests.Select(v => (Name: "vector " + v.Name, Snapshot: VectorRunner.ToSnapshot(v), Facts: v.Facts))
                .Concat(Fixtures.Value.Select(f => (Name: "fixture " + f.Machine.HostName, Snapshot: f, Facts: (IReadOnlyDictionary<string, System.Text.Json.JsonElement>?)null)));
            foreach (var (name, snapshot, facts) in bases)
            {
                (RuleResult Result, IReadOnlySet<string> Deciding) Eval(Snapshot s)
                {
                    Interlocked.Increment(ref count);
                    var (result, deciding) = evaluator.EvaluateDetailed(s, catalogue, VectorRunner.Context, facts, [rule]).Single();
                    return (result, deciding);
                }
                var (baseResult, baseDeciding) = Eval(snapshot);
                var keys = new SortedSet<string>(CheckEvidence.KeysOf(rule.Check), StringComparer.OrdinalIgnoreCase);
                keys.UnionWith(baseResult.EvidenceKeys);
                keys.UnionWith(baseDeciding);
                foreach (var key in keys)
                {
                    foreach (var status in Unavailable)
                    {
                        var (r, d) = Eval(Mutate(snapshot, [key], status, truncate: false));
                        if (r.Status == ResultStatus.Pass && d.Contains(key)) violations.Add($"{rule.Id} ({name}): {key} → {status} ergibt pass");
                    }
                    var (rt, dt) = Eval(Mutate(snapshot, [key], null, truncate: true));
                    if (rt.Status == ResultStatus.Pass && dt.Contains(key)) violations.Add($"{rule.Id} ({name}): {key} → Truncated ergibt pass");
                    var (rm, dm) = Eval(Mutate(snapshot, [key], ItemStatus.KeyMissing, truncate: false));
                    if (rm.Status == ResultStatus.Pass && dm.Contains(key) && !rm.Flags.HasFlag(ResultFlags.FromDefault) && !allowList.Contains(rule.Id))
                        violations.Add($"{rule.Id} ({name}): {key} → KeyMissing ergibt pass ohne belegten Standardwert");
                }
                // Alle Belege zugleich nicht verfügbar
                foreach (var status in Unavailable)
                {
                    var (r, d) = Eval(Mutate(snapshot, keys, status, truncate: false));
                    if (r.Status == ResultStatus.Pass && d.Overlaps(keys)) violations.Add($"{rule.Id} ({name}): alle Belege → {status} ergibt pass");
                }
            }
        });
        runs = count;
        return [.. violations.Order(StringComparer.Ordinal)];
    }

    /// <summary>Einträge ersetzen (Status ohne Wert) bzw. als abgeschnitten markieren; Abdeckung bleibt.</summary>
    internal static Snapshot Mutate(Snapshot s, IEnumerable<string> keys, ItemStatus? status, bool truncate)
    {
        var items = new Dictionary<string, SnapshotItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in s.Items) items.TryAdd(item.Key, item);
        foreach (var key in keys)
        {
            items.TryGetValue(key, out var existing);
            items[key] = truncate
                ? (existing ?? new SnapshotItem(key, ItemStatus.Ok, null, null, null, 0)) with { Truncated = true }
                : new SnapshotItem(existing?.Key ?? key, status!.Value, null, status == ItemStatus.KeyMissing ? null : 1, null, 0);
        }
        return s with { Items = [.. items.Values] };
    }
}
