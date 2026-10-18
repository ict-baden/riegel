// SPDX-License-Identifier: MIT
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Infrastructure;

/// <summary>
/// Gemeinsame Regeln für jede Regeldatei unter rules/ (spec 2.1–2.4, 2.2 Herkunft der Sollwerte). Die Pakete
/// rules-a/b/c verlassen sich darauf; der Integrationsschritt setzt <see cref="CatalogueComplete"/> auf true und prüft
/// dann alle 20 Präfixdateien mit 153 Regeln.
/// </summary>
public sealed partial class CatalogueInvariantTests
{
    /// <summary>Erst der Integrationsschritt setzt das auf true (alle Regelpakete zusammengeführt).</summary>
    public const bool CatalogueComplete = false;

    /// <summary>Regel-IDs von Katalog 2026.10 je Präfix (spec 2.1).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Catalogue202610 = new Dictionary<string, string[]>
    {
        ["LIFE"] = Ids("LIFE", 1, 2, 3, 4, 5),
        ["NET"] = Ids("NET", 1, 2, 3, 4, 5, 8, 9, 10, 11, 17, 18, 19, 23, 25, 29),
        ["NETX"] = Ids("NETX", 1),
        ["CRED"] = Ids("CRED", 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 13, 15, 17, 18, 20, 21),
        ["LOGON"] = Ids("LOGON", 9, 10),
        ["ACCT"] = Ids("ACCT", 1, 5, 6, 10, 11, 12, 14, 15, 16, 17),
        ["UAC"] = Ids("UAC", 1, 2, 4),
        ["URA"] = Ids("URA", 2, 3, 5),
        ["DEF"] = Ids("DEF", 1, 2, 3, 9, 11, 12, 14, 17, 18),
        ["ASR"] = Ids("ASR", 1, 2, 5, 8, 9, 13, 15),
        ["APP"] = Ids("APP", 1, 2, 6),
        ["ENC"] = Ids("ENC", 1, 2, 3, 4, 5, 6, 7),
        ["FW"] = Ids("FW", 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11),
        ["SVC"] = Ids("SVC", 1, 2, 4),
        ["SYS"] = Ids("SYS", 7, 8),
        ["AUD"] = Ids("AUD", [.. Enumerable.Range(1, 23), 26, 27, 28, 30]),
        ["PS"] = Ids("PS", 1, 2),
        ["RDP"] = Ids("RDP", 1, 2, 3, 4, 5, 6, 7, 8, 9),
        ["MED"] = Ids("MED", 1, 2, 3, 4, 5, 6),
        ["TLS"] = Ids("TLS", 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11),
    };

    /// <summary>Gruppenpräfixe von Grundschutz++ (BSI Stand-der-Technik-Bibliothek, Anwenderkatalog 2026-09-24; nur Kennungen).</summary>
    public static readonly string[] GsppPrefixes =
        ["ARCH", "ASST", "BER", "BES", "DET", "DEV", "DLS", "GC", "GEB", "KONF", "NOT", "PERF", "PERS", "REA", "RISK", "SENS", "STM", "TEST", "UMS", "VRB"];

    private static string[] Ids(string prefix, params int[] numbers) => [.. numbers.Select(n => $"{prefix}-{n:D2}")];

    private static IEnumerable<(string File, string Prefix, RuleFile Rules, string Text)> Files() =>
        RepoPaths.RuleFiles().Select(f => (f, Path.GetFileNameWithoutExtension(f), RiegelJson.Deserialize<RuleFile>(File.ReadAllBytes(f)), File.ReadAllText(f)));

    [Fact]
    public void Kein_Platzhalter_fuer_den_vollstaendigen_Katalog_vergessen()
    {
        Assert.Equal(153, Catalogue202610.Values.Sum(v => v.Length));
        Assert.Equal(20, Catalogue202610.Count);
        Assert.Equal(56, new[] { "LIFE", "NET", "NETX", "CRED", "LOGON", "ACCT", "UAC", "URA" }.Sum(p => Catalogue202610[p].Length));
        Assert.Equal(42, new[] { "DEF", "ASR", "APP", "ENC", "FW", "SVC", "SYS" }.Sum(p => Catalogue202610[p].Length));
        Assert.Equal(55, new[] { "AUD", "PS", "RDP", "MED", "TLS" }.Sum(p => Catalogue202610[p].Length));
    }

    [Fact]
    public void Katalog_ist_vollstaendig_wenn_die_Integration_es_sagt()
    {
#pragma warning disable CS0162 // bis zur Integration absichtlich unerreichbar
        if (!CatalogueComplete) return;
        var present = Files().ToDictionary(f => f.Prefix, f => f.Rules.Rules.Where(r => r.State == RuleState.Active).Select(r => r.Id).ToList());
        Assert.Equal(Catalogue202610.Keys.Order(StringComparer.Ordinal), present.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(153, present.Values.Sum(v => v.Count));
#pragma warning restore CS0162
    }

    [Fact]
    public void Jede_Regeldatei_ist_gueltig_und_passt_zu_ihrem_Praefix()
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, prefix, rules, text) in Files())
        {
            problems.AddRange(SchemaValidator.Errors(text).Select(e => $"{prefix}: {e}"));
            if (!Catalogue202610.ContainsKey(prefix)) problems.Add($"{prefix}.json: unbekanntes Präfix");
            if (rules.Prefix != prefix) problems.Add($"{prefix}.json: \"prefix\" ist {rules.Prefix}");
            foreach (var rule in rules.Rules)
            {
                if (!seen.Add(rule.Id)) problems.Add($"{rule.Id}: doppelt");
                if (!Regex.IsMatch(rule.Id, $"^{prefix}-\\d{{2}}$")) problems.Add($"{rule.Id}: passt nicht zur Datei {prefix}.json");
                if (!RuleCategories.IsKnown(rule.Category)) problems.Add($"{rule.Id}: Kategorie {rule.Category}");
                if (rule.State == RuleState.Active && Catalogue202610.TryGetValue(prefix, out var ids) && !ids.Contains(rule.Id))
                    problems.Add($"{rule.Id}: nicht in Katalog 2026.10 (spec 2.1)");
                if (rule.RuleSetId != "riegel") problems.Add($"{rule.Id}: ruleSetId {rule.RuleSetId}");
            }
            // Je vorhandener Datei die Zahl aus spec 2.1 (aktive Regeln)
            if (Catalogue202610.TryGetValue(prefix, out var expected))
            {
                var active = rules.Rules.Where(r => r.State == RuleState.Active).Select(r => r.Id).Order(StringComparer.Ordinal).ToList();
                if (!active.SequenceEqual(expected.Order(StringComparer.Ordinal)))
                    problems.Add($"{prefix}.json: aktive Regeln {string.Join(",", active)} statt {string.Join(",", expected)}");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Texte_Belege_und_Standardwerte_sind_vollstaendig()
    {
        var problems = new List<string>();
        var allowList = WhenMissingPassAllowList();
        foreach (var (_, _, rules, _) in Files())
        {
            foreach (var rule in rules.Rules)
            {
                foreach (var (field, text) in new[] { ("title", rule.Title), ("rationale", rule.Rationale) })
                    foreach (var code in new[] { "en", "de" })
                        if (!text.Has(code)) problems.Add($"{rule.Id}: {field}.{code} leer");
                if (rule.Kind == RuleKind.Check && rule.State == RuleState.Active)
                {
                    if (!rule.Verified.Any(v => RuleSources.ValueBacking.Contains(v.Src)))
                        problems.Add($"{rule.Id}: kein verified-Eintrag aus {string.Join("/", RuleSources.ValueBacking)} (spec 2.2)");
                }
                foreach (var check in Flatten(rule.Check))
                {
                    var (osDefaults, whenMissing, missingPass) = check switch
                    {
                        RegistryCheck r => (r.OsDefaults, r.WhenMissing, r.MissingPass),
                        RegistryEffectiveCheck e => (e.OsDefaults, e.WhenMissing, e.MissingPass),
                        SeceditCheck s => ((IReadOnlyList<OsDefault>?)null, s.WhenMissing, (MissingPass?)null),
                        _ => (null, WhenMissing.NotCheckable, null),
                    };
                    foreach (var d in osDefaults ?? [])
                        if (!d.Source.StartsWith("https://", StringComparison.Ordinal) || d.Seen == default)
                            problems.Add($"{rule.Id}: osDefault ohne Quelle (https) oder Datum");
                    if (whenMissing == WhenMissing.OsDefault && (osDefaults is null || osDefaults.Count == 0))
                        problems.Add($"{rule.Id}: whenMissing osDefault ohne osDefaults");
                    if (whenMissing == WhenMissing.Pass)
                    {
                        if (missingPass is null) problems.Add($"{rule.Id}: whenMissing pass ohne missingPass");
                        if (!allowList.Contains(rule.Id)) problems.Add($"{rule.Id}: whenMissing pass, aber nicht in Rules/{{A,B,C}}/when-missing-pass.txt");
                    }
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Verweise_halten_die_Lizenzgrenzen_ein()
    {
        var problems = new List<string>();
        var gspp = new Regex($@"^({string.Join("|", GsppPrefixes)})\.\d+(\.\d+)*$");
        foreach (var (file, prefix, rules, text) in Files())
        {
            // MSB-Verweise nennen nie einen Wert (auch im Rohtext geprüft, nicht nur im Schema)
            var root = JsonNode.Parse(text)!;
            foreach (var rule in root["rules"]!.AsArray())
                foreach (var reference in rule!["references"]?.AsArray() ?? [])
                    if (reference!["src"]?.GetValue<string>() == "MSB" && reference.AsObject().Any(p => p.Key is "value" or "expected" or "data"))
                        problems.Add($"{rule["id"]}: MSB-Verweis mit Wert");
            foreach (var rule in rules.Rules)
                foreach (var reference in rule.References.Where(r => r.Src == "GSPP"))
                    foreach (var id in (reference.Ids ?? []).Append(reference.Id).OfType<string>())
                        if (!gspp.IsMatch(id)) problems.Add($"{rule.Id}: GS++-Kennung {id} hat kein bekanntes Präfix");
            if (Cis().IsMatch(text)) problems.Add($"{prefix}.json: erwähnt CIS (spec 2.2: nichts von CIS)");
            if (text.Contains("SiSyPHuS", StringComparison.OrdinalIgnoreCase)) problems.Add($"{prefix}.json: erwähnt SiSyPHuS");
            foreach (Match m in GrundschutzModule().Matches(text))
                problems.Add($"{prefix}.json: IT-Grundschutz-Baustein {m.Value} (eigene IDs nutzen den Bindestrich)");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Jede_aktive_Pruefregel_hat_die_Pflicht_Testvektoren()
    {
        var problems = new List<string>();
        foreach (var (_, _, rules, _) in Files())
            foreach (var rule in rules.Rules.Where(r => r.Kind == RuleKind.Check && r.State == RuleState.Active))
            {
                var tests = rule.Tests;
                if (!tests.Any(t => t.Expect == ResultStatus.Pass)) problems.Add($"{rule.Id}: kein Vektor mit pass");
                if (!tests.Any(t => t.Expect == ResultStatus.Fail)) problems.Add($"{rule.Id}: kein Vektor mit fail");
                if (!tests.Any(t => t.Items.Values.Any(i => i.Status == ItemStatus.NotCollected)))
                    problems.Add($"{rule.Id}: kein Vektor mit einem NotCollected-Eintrag");
                if (!tests.Any(t => t.Items.Values.Any(i => i.Status is ItemStatus.KeyMissing or ItemStatus.ValueMissing)))
                    problems.Add($"{rule.Id}: kein Vektor mit fehlendem Schlüssel oder Wert (KeyMissing/ValueMissing)");
            }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Faehigkeiten_sind_ueber_alle_Dateien_eindeutig_und_belegt()
    {
        var folder = Path.Combine(RepoPaths.Rules, "capabilities");
        if (!Directory.Exists(folder)) return;
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            foreach (var entry in RiegelJson.Deserialize<CapabilityFile>(File.ReadAllBytes(file)).Capabilities)
            {
                Assert.True(seen.TryAdd(entry.Capability, Path.GetFileName(file)), $"Fähigkeit {entry.Capability} doppelt ({seen.GetValueOrDefault(entry.Capability)}, {Path.GetFileName(file)})");
                Assert.StartsWith("https://", entry.Source, StringComparison.Ordinal);
            }
    }

    [Fact]
    public void Katalogkopf_hat_Version_und_Datum()
    {
        var header = RiegelJson.ReadFile<CatalogueDocument>(Path.Combine(RepoPaths.Rules, "catalogue.json"), 1024 * 1024);
        Assert.Equal(SchemaIds.Catalogue, header.Schema);
        Assert.Equal("riegel", header.Id);
        Assert.Equal("2026.10", header.Version);
        Assert.True(header.ReleasedOn >= new DateOnly(2026, 10, 18), "releasedOn ist frühestens der Tag des Gerüsts");
        Assert.Null(header.Rules);
    }

    /// <summary>Regel-IDs aus tests/Riegel.Tests/Rules/{A,B,C}/when-missing-pass.txt (je Zeile eine ID, # = Kommentar).</summary>
    internal static HashSet<string> WhenMissingPassAllowList()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in new[] { "A", "B", "C" })
        {
            var file = Path.Combine(RepoPaths.TestsProject, "Rules", package, "when-missing-pass.txt");
            if (!File.Exists(file)) continue;
            foreach (var line in File.ReadAllLines(file))
            {
                var id = line.Split('#')[0].Trim();
                if (id.Length > 0) ids.Add(id);
            }
        }
        return ids;
    }

    /// <summary>Alle Prüfungen einer Regel, auch die verschachtelten.</summary>
    internal static IEnumerable<CheckSpec> Flatten(CheckSpec check)
    {
        yield return check;
        IEnumerable<CheckSpec> children = check switch
        {
            FirstOfCheck f => f.Checks,
            AllOfCheck a => a.Checks,
            AnyOfCheck a => a.Checks,
            NoneOfCheck n => n.Checks,
            NotCheck n => [n.Check],
            _ => [],
        };
        foreach (var child in children)
            foreach (var nested in Flatten(child)) yield return nested;
    }

    [GeneratedRegex(@"\bCIS\b")]
    private static partial Regex Cis();

    [GeneratedRegex(@"\b(ISMS|ORP|CON|OPS|DER|APP|SYS|IND|NET|INF)\.\d+(\.\d+)*(\.A\d+)?\b")]
    private static partial Regex GrundschutzModule();
}
