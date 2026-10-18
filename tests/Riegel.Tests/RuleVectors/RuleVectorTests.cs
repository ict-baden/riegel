// SPDX-License-Identifier: MIT
using Riegel.Core.Catalogue;
using Riegel.Core.Model;
using Riegel.Core.Services;
using Riegel.Tests.TestSupport;
using Xunit.Abstractions;

namespace Riegel.Tests.RuleVectors;

/// <summary>
/// Jeder Testvektor jeder Regel muss mit der echten Engine stimmen (spec 8.2): die Regeln des eingebetteten Katalogs
/// (rules/**, im Branch der Engine noch leer – Integrationsgate) und die eigenen Testregeln der Engine, die jede
/// Prüfart mit pass, fail, fehlend, nicht erfasst, verweigert, abgeschnitten, falschem Typ und n/a abdecken.
/// </summary>
public sealed class RuleVectorTests(ITestOutputHelper output)
{
    [Fact]
    public void Jeder_Vektor_des_eingebetteten_Katalogs_stimmt()
    {
        var catalogue = RiegelServices.Create(new NullPlatform(), new FixedClock()).Catalogue.LoadEmbedded();
        var failures = VectorRunner.Failures(catalogue, out var count);
        output.WriteLine($"{catalogue.Rules.Count} Regeln, {count} Vektoren");
        Assert.True(failures.Count == 0, $"{failures.Count} von {count} Vektoren weichen ab:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Jeder_Vektor_der_Testregeln_stimmt()
    {
        var catalogue = VectorRunner.TestCatalogue();
        var failures = VectorRunner.Failures(catalogue, out var count);
        output.WriteLine($"{catalogue.Rules.Count} Testregeln, {count} Vektoren");
        foreach (var rule in catalogue.Rules.OrderBy(r => r.Id, StringComparer.Ordinal))
            foreach (var vector in rule.Tests)
            {
                var (result, _) = VectorRunner.Run(catalogue, rule, vector);
                output.WriteLine($"{rule.Id} „{vector.Name}“ → {result.Status} {result.ReasonCode} [{string.Join(",", VectorRunner.FlagNames(result.Flags))}] {string.Join(" | ", result.Details ?? [])}");
            }
        Assert.True(count > 400, "zu wenige Vektoren");
        Assert.True(failures.Count == 0, $"{failures.Count} von {count} Vektoren weichen ab:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Die_Testregeln_sind_schemagueltig()
    {
        var root = Path.Combine(RepoPaths.TestsProject, "RuleVectors", "TestRules", "rules");
        var files = Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        Assert.True(files.Count >= 15);
        foreach (var file in files) SchemaValidator.AssertValid(File.ReadAllText(file), Path.GetRelativePath(root, file));
    }

    /// <summary>
    /// Abnahme: jede Prüfart mit pass, fail, fehlend, nicht erfasst, verweigert, abgeschnitten, falschem Typ und n/a.
    /// Ausnahmen: lifecycle liest keinen Eintrag; Bestandsregeln kennen kein fail; Verknüpfungen erben die Fälle ihrer Teile.
    /// </summary>
    [Fact]
    public void Jede_Pruefart_hat_alle_acht_Faelle()
    {
        var catalogue = VectorRunner.TestCatalogue();
        string[] all = ["pass", "fail", "missing", "not collected", "denied", "truncated", "wrong type", "n/a"];
        var exempt = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [CheckTypes.Lifecycle] = ["missing", "not collected", "denied", "truncated", "wrong type", "n/a"],
            [CheckTypes.Inventory] = ["pass", "fail", "missing", "truncated", "wrong type", "n/a"],
            [CheckTypes.FirstOf] = ["denied", "truncated", "wrong type", "n/a"],
            [CheckTypes.AllOf] = ["denied", "truncated", "wrong type", "n/a"],
            [CheckTypes.AnyOf] = ["denied", "truncated", "wrong type", "n/a"],
            [CheckTypes.NoneOf] = ["denied", "truncated", "wrong type", "n/a"],
            [CheckTypes.Not] = ["denied", "truncated", "wrong type", "n/a"],
        };
        var gaps = new List<string>();
        foreach (var type in CheckTypes.All)
        {
            var vectors = catalogue.Rules.Where(r => r.Check.Type == type).SelectMany(r => r.Tests).ToList();
            foreach (var scenario in all.Except(exempt.GetValueOrDefault(type, [])))
            {
                bool covered = scenario switch
                {
                    "pass" => vectors.Any(v => v.Expect is ResultStatus.Pass or ResultStatus.Info),
                    "fail" => vectors.Any(v => v.Expect == ResultStatus.Fail),
                    "n/a" => vectors.Any(v => v.Expect == ResultStatus.NotApplicable),
                    _ => vectors.Any(v => v.Name?.Contains(scenario, StringComparison.OrdinalIgnoreCase) == true),
                };
                if (!covered) gaps.Add($"{type}: {scenario}");
            }
        }
        Assert.True(gaps.Count == 0, "Fehlende Fälle:\n" + string.Join("\n", gaps));
    }

    [Fact]
    public void Die_Testregeln_decken_jede_Pruefart_ab()
    {
        var catalogue = VectorRunner.TestCatalogue();
        var used = catalogue.Rules.SelectMany(r => CatalogueValidator.Flatten(r.Check)).Select(c => c.Type).ToHashSet(StringComparer.Ordinal);
        var missing = CheckTypes.All.Where(t => !used.Contains(t)).ToList();
        Assert.True(missing.Count == 0, "Ohne Testregel: " + string.Join(", ", missing));
        // Je Prüfart der obersten Ebene: pass/info, fail (außer Bestand), nicht prüfbar, und ein Vektor „not collected“ und „missing“
        foreach (var group in catalogue.Rules.GroupBy(r => r.Check.Type))
        {
            var vectors = group.SelectMany(r => r.Tests).ToList();
            var expects = vectors.Select(v => v.Expect).ToHashSet();
            Assert.True(expects.Contains(ResultStatus.Pass) || expects.Contains(ResultStatus.Info), $"{group.Key}: kein pass");
            if (group.Key != CheckTypes.Inventory) Assert.True(expects.Contains(ResultStatus.Fail) || group.All(r => r.Kind == RuleKind.Inventory), $"{group.Key}: kein fail");
            Assert.True(expects.Contains(ResultStatus.NotCheckable), $"{group.Key}: kein nicht prüfbar");
            // lifecycle liest keinen Eintrag, nur Kopf und Tabelle (unbekannter Kopf: EvaluationEdgeTests)
            if (group.Key != CheckTypes.Lifecycle)
                Assert.True(vectors.Any(v => v.Items.Values.Any(i => i.Status == ItemStatus.NotCollected)), $"{group.Key}: kein not collected");
        }
    }
}
