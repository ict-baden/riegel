// SPDX-License-Identifier: MIT
using System.Text;
using System.Text.Json.Nodes;
using Riegel.Core.Catalogue;
using Riegel.Core.Evaluation;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;
using Riegel.Tests.RuleVectors;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Catalogue;

/// <summary>
/// Katalog laden und prüfen (spec 2.3, 7.2): eingebettet und aus einem Strom, mit kanonischem SHA-256, mit
/// Kunden-Baselines, und jede inhaltliche Prüfung mit ihrem RGL-Code (0200–0212).
/// </summary>
public sealed class CatalogueProviderTests
{
    private const string Header = """{ "schema": "riegel.catalogue/1", "id": "riegel", "version": "2026.10", "releasedOn": "2026-10-18" }""";

    /// <summary>Eine gültige Regel als JSON-Knoten, die einzelne Tests verändern.</summary>
    private static JsonObject ValidRule(string id = "TX-01") => JsonNode.Parse($$"""
        {
          "id": "{{id}}", "since": "2026.10", "state": "active", "category": "system", "severity": "medium", "kind": "check",
          "title": { "en": "Test", "de": "Test" }, "rationale": { "en": "Test", "de": "Test" },
          "appliesTo": { "os": ["client", "server"], "domain": "any", "excludeDc": false, "when": [] },
          "needsAdmin": false,
          "check": { "type": "registry", "key": "HKLM\\SOFTWARE\\Policies\\Riegel-Test\\Values", "value": "Enabled", "valueType": "dword",
                     "op": "eq", "expected": 1, "whenMissing": "fail" },
          "references": [], "textSources": [], "verified": [], "tests": []
        }
        """)!.AsObject();

    private static Dictionary<string, byte[]> Files(params (string Name, string Json)[] files)
    {
        var d = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["rules/catalogue.json"] = Encoding.UTF8.GetBytes(Header) };
        foreach (var (name, json) in files) d["rules/" + name] = Encoding.UTF8.GetBytes(json);
        return d;
    }

    private static string RuleFile(string prefix, params JsonObject[] rules) =>
        new JsonObject { ["schema"] = "riegel.rules/1", ["prefix"] = prefix, ["rules"] = new JsonArray([.. rules]) }.ToJsonString();

    private static string Code(Action load)
    {
        var e = Assert.Throws<RiegelException>(load);
        return e.Code;
    }

    private static string LoadRule(JsonObject rule) => Code(() => CatalogueProvider.FromFiles(Files(("TX.json", RuleFile("TX", rule)))));

    // ---------------------------------------------------------------- Laden

    [Fact]
    public void Eingebetteter_Katalog_laedt_und_wird_zwischengespeichert()
    {
        var provider = RiegelServices.Create(new NullPlatform(), new FixedClock()).Catalogue;
        var a = provider.LoadEmbedded();
        Assert.Same(a, provider.LoadEmbedded());
        Assert.Equal("riegel", a.Id);
        Assert.Equal("2026.10", a.Version);
        Assert.True(Hashes.IsSha256(a.Sha256));
    }

    [Fact]
    public void Gueltige_Regel_laedt_und_ist_auffindbar()
    {
        var catalogue = CatalogueProvider.FromFiles(Files(("TX.json", RuleFile("TX", ValidRule()))));
        Assert.Single(catalogue.Rules);
        Assert.NotNull(catalogue.Find("tx-01"));
        Assert.Null(catalogue.Find("TX-02"));
    }

    [Fact]
    public void Gleicher_Inhalt_gibt_denselben_SHA256_auch_ueber_den_Strom()
    {
        var catalogue = VectorRunner.TestCatalogue();
        var document = new CatalogueDocument(SchemaIds.Catalogue, catalogue.Id, catalogue.Version, catalogue.ReleasedOn, catalogue.Rules,
            catalogue.Lifecycle, catalogue.Builds, catalogue.Capabilities, catalogue.Data);
        using var stream = new MemoryStream(RiegelJson.SerializeToUtf8Bytes(document));
        var loaded = new CatalogueProvider().Load(stream, ReadLimits.Default);
        Assert.Equal(catalogue.Sha256, loaded.Sha256);
        Assert.Equal(catalogue.Rules.Count, loaded.Rules.Count);
        // Reihenfolge der Regeln ändert den Hash nicht (kanonische Form)
        var reversed = document with { Rules = [.. catalogue.Rules.Reverse()] };
        using var stream2 = new MemoryStream(RiegelJson.SerializeToUtf8Bytes(reversed));
        Assert.Equal(catalogue.Sha256, new CatalogueProvider().Load(stream2, ReadLimits.Default).Sha256);
        // Eine geänderte Regel ändert ihn
        var changed = document with { Rules = [.. catalogue.Rules.Skip(1)] };
        using var stream3 = new MemoryStream(RiegelJson.SerializeToUtf8Bytes(changed));
        Assert.NotEqual(catalogue.Sha256, new CatalogueProvider().Load(stream3, ReadLimits.Default).Sha256);
    }

    [Fact]
    public void Strom_mit_falschem_Schema_zu_gross_oder_kaputt_wird_abgelehnt()
    {
        var provider = new CatalogueProvider();
        Assert.Equal(RglCodesCommon.UnsupportedSchema, Code(() => provider.Load(new MemoryStream("""{ "schema": "riegel.rules/1" }"""u8.ToArray()), ReadLimits.Default)));
        Assert.Equal(RglCodesCommon.LimitExceeded, Code(() => provider.Load(new MemoryStream(new byte[2048]), new ReadLimits(MaxCatalogueBytes: 1024))));
        Assert.Equal(RglCodesCommon.InvalidJson, Code(() => provider.Load(new MemoryStream("{ nicht json"u8.ToArray()), ReadLimits.Default)));
        var deep = string.Concat(Enumerable.Repeat("[", 80)) + string.Concat(Enumerable.Repeat("]", 80));
        Assert.Equal(RglCodesCommon.InvalidJson, Code(() => provider.Load(new MemoryStream(Encoding.UTF8.GetBytes(deep)), ReadLimits.Default)));
    }

    // ---------------------------------------------------------------- geschlossenes Vokabular

    [Fact]
    public void Unbekannte_Pruefart_RGL0201()
    {
        var rule = ValidRule();
        rule["check"] = JsonNode.Parse("""{ "type": "powershell", "script": "Get-Item" }""");
        Assert.Equal(RglCodesEngine.UnknownCheckType, LoadRule(rule));
    }

    [Fact]
    public void Unbekanntes_Feld_in_Pruefung_und_Regel_RGL0202()
    {
        var rule = ValidRule();
        rule["check"]!["command"] = "reg add";
        Assert.Equal(RglCodesEngine.UnknownField, LoadRule(rule));
        var nested = ValidRule();
        nested["check"] = JsonNode.Parse("""{ "type": "firstOf", "checks": [ { "type": "lsaProtection", "deep": true } ] }""");
        Assert.Equal(RglCodesEngine.UnknownField, LoadRule(nested));
        var top = ValidRule();
        top["script"] = "x";
        Assert.Equal(RglCodesEngine.UnknownField, LoadRule(top));
    }

    [Fact]
    public void Die_Felder_jeder_Pruefart_entsprechen_spec_2_3()
    {
        Assert.Equal(["acceptTypes", "expected", "key", "missingPass", "multiSz", "op", "osDefaults", "value", "valueType", "whenMissing"],
            ClosedVocabulary.FieldsOf(CheckTypes.Registry));
        Assert.Equal(["accept", "guid"], ClosedVocabulary.FieldsOf(CheckTypes.AsrRule));
        Assert.Equal(["ports", "profiles"], ClosedVocabulary.FieldsOf(CheckTypes.FirewallExposure));
        Assert.Empty(ClosedVocabulary.FieldsOf(CheckTypes.LsaProtection));
        Assert.Equal(CheckTypes.All.Order(StringComparer.Ordinal), ClosedVocabulary.CheckTypes.Keys.Order(StringComparer.Ordinal));
    }

    // ---------------------------------------------------------------- inhaltliche Prüfung

    [Theory]
    [InlineData("""{ "type": "registry", "key": "HKLM\\X", "value": "v", "valueType": "dword", "op": "containsAll", "expected": [1], "whenMissing": "fail" }""")]
    [InlineData("""{ "type": "registry", "key": "HKLM\\X", "value": "v", "valueType": "sz", "op": "lt", "expected": "a", "whenMissing": "fail" }""")]
    [InlineData("""{ "type": "registry", "key": "HKLM\\X", "value": "v", "valueType": "dword", "op": "eq", "whenMissing": "fail" }""")]
    [InlineData("""{ "type": "registry", "key": "HKLM\\X", "value": "v", "valueType": "dword", "op": "eq", "expected": "eins", "whenMissing": "fail" }""")]
    [InlineData("""{ "type": "registry", "key": "HKLM\\X", "value": "v", "valueType": "dword", "op": "in", "expected": 1, "whenMissing": "fail" }""")]
    [InlineData("""{ "type": "registry", "key": "HKLM\\X", "value": "v", "valueType": "binary", "op": "eq", "expected": "zz", "whenMissing": "fail" }""")]
    [InlineData("""{ "type": "registry", "key": "HKLM\\X", "value": "v", "valueType": "dword", "acceptTypes": ["binary"], "op": "eq", "expected": 1, "whenMissing": "fail" }""")]
    [InlineData("""{ "type": "registry", "key": "HKLM\\X", "value": "v", "valueType": "dword", "op": "eq", "expected": 1, "whenMissing": "fail", "multiSz": "list" }""")]
    [InlineData("""{ "type": "secedit", "section": "System Access", "key": "K", "op": "containsAll", "expected": ["a"], "valueKind": "number", "whenMissing": "fail" }""")]
    [InlineData("""{ "type": "accountPolicy", "field": "minPwLen", "op": "ge", "expected": "vierzehn" }""")]
    [InlineData("""{ "type": "wmiProperty", "item": "wmi:root\\cimv2:Win32_Service", "property": "State", "rows": "any", "op": "lt", "expected": "Running", "ifNoRows": "fail" }""")]
    [InlineData("""{ "type": "defenderSetting", "source": "status", "property": "RealTimeProtectionEnabled", "op": "eq" }""")]
    public void Operator_passt_nicht_zum_Werttyp_RGL0203(string check)
    {
        var rule = ValidRule();
        rule["check"] = JsonNode.Parse(check);
        Assert.Equal(RglCodesEngine.OperatorMismatch, LoadRule(rule));
    }

    [Fact]
    public void Standardwert_ohne_Quelle_oder_Datum_RGL0204()
    {
        var rule = ValidRule();
        rule["check"] = JsonNode.Parse("""{ "type": "registry", "key": "HKLM\\X", "value": "v", "valueType": "dword", "op": "eq", "expected": 1, "whenMissing": "osDefault", "osDefaults": [ { "minBuild": 1, "value": 1, "source": "http://example.org/x", "seen": "2026-10-18" } ] }""");
        Assert.Equal(RglCodesEngine.DefaultWithoutSource, LoadRule(rule));
        rule = ValidRule();
        rule["check"] = JsonNode.Parse("""{ "type": "registry", "key": "HKLM\\X", "value": "v", "valueType": "dword", "op": "eq", "expected": 1, "whenMissing": "pass", "missingPass": { "source": "https://example.org/x", "seen": "2026-10-18", "note": "" } }""");
        Assert.Equal(RglCodesEngine.DefaultWithoutSource, LoadRule(rule));
    }

    [Theory]
    [InlineData("""{ "type": "registry", "key": "HKLM\\X", "value": "v", "valueType": "dword", "op": "eq", "expected": 1, "whenMissing": "osDefault" }""")]
    [InlineData("""{ "type": "registry", "key": "HKLM\\X", "value": "v", "valueType": "dword", "op": "eq", "expected": 1, "whenMissing": "pass" }""")]
    [InlineData("""{ "type": "secedit", "section": "System Access", "key": "K", "op": "eq", "expected": 1, "valueKind": "number", "whenMissing": "osDefault" }""")]
    public void Fehlende_Werte_ohne_Beleg_RGL0205(string check)
    {
        var rule = ValidRule();
        rule["check"] = JsonNode.Parse(check);
        Assert.Equal(RglCodesEngine.MissingRuleIncomplete, LoadRule(rule));
    }

    [Fact]
    public void Unbekannte_Faehigkeit_RGL0206_und_unbekannter_Fakt_RGL0207()
    {
        var rule = ValidRule();
        rule["appliesTo"]!["capability"] = "warpAntrieb";
        rule["appliesTo"]!["ifCapabilityAbsent"] = "fail";
        Assert.Equal(RglCodesEngine.UnknownCapability, LoadRule(rule));
        var fact = ValidRule();
        fact["appliesTo"]!["when"] = JsonNode.Parse("""[ { "fact": "machine.color", "op": "eq", "value": "rot" } ]""");
        Assert.Equal(RglCodesEngine.UnknownFact, LoadRule(fact));
        var wrongType = ValidRule();
        wrongType["appliesTo"]!["when"] = JsonNode.Parse("""[ { "fact": "office.installed", "op": "eq", "value": "ja" } ]""");
        Assert.Equal(RglCodesEngine.UnknownFact, LoadRule(wrongType));
        var badMode = ValidRule();
        badMode["appliesTo"]!["when"] = JsonNode.Parse("""[ { "fact": "defender.mode", "op": "in", "value": ["active", "turbo"] } ]""");
        Assert.Equal(RglCodesEngine.UnknownFact, LoadRule(badMode));
        var ok = ValidRule();
        ok["appliesTo"]!["when"] = JsonNode.Parse("""[ { "fact": "defender.mode", "op": "in", "value": ["active", "passive"] }, { "fact": "os.build", "op": "ne", "value": 1 } ]""");
        Assert.Single(CatalogueProvider.FromFiles(Files(("TX.json", RuleFile("TX", ok)))).Rules);
    }

    [Fact]
    public void Doppelte_Faehigkeit_RGL0208_und_doppelte_ID_RGL0209()
    {
        const string Caps = """{ "schema": "riegel.capabilities/1", "capabilities": [ { "capability": "c1", "exists": [ { "os": "client" } ], "source": "https://example.org/c", "seen": "2026-10-18" } ] }""";
        Assert.Equal(RglCodesEngine.DuplicateCapability, Code(() => CatalogueProvider.FromFiles(Files(("capabilities/a.json", Caps), ("capabilities/b.json", Caps)))));
        Assert.Equal(RglCodesEngine.DuplicateRuleId, LoadRuleFile(RuleFile("TX", ValidRule(), ValidRule())));
    }

    private static string LoadRuleFile(string json) => Code(() => CatalogueProvider.FromFiles(Files(("TX.json", json))));

    [Fact]
    public void Ungueltige_Regeln_RGL0210()
    {
        var category = ValidRule();
        category["category"] = "sonstiges";
        Assert.Equal(RglCodesEngine.InvalidRule, LoadRule(category));
        var hkcu = ValidRule();
        hkcu["check"]!["key"] = "HKCU\\Software\\X";
        Assert.Equal(RglCodesEngine.InvalidRule, LoadRule(hkcu));
        var inventoryInCheck = ValidRule();
        inventoryInCheck["check"] = JsonNode.Parse("""{ "type": "inventory", "renderer": "shares", "items": ["wmi:root\\cimv2:Win32_Share"] }""");
        Assert.Equal(RglCodesEngine.InvalidRule, LoadRule(inventoryInCheck));
        var emptyComposite = ValidRule();
        emptyComposite["check"] = JsonNode.Parse("""{ "type": "anyOf", "checks": [] }""");
        Assert.Equal(RglCodesEngine.InvalidRule, LoadRule(emptyComposite));
        var badSid = ValidRule();
        badSid["check"] = JsonNode.Parse("""{ "type": "userRight", "right": "SeDebugPrivilege", "mode": "onlyFrom", "sids": ["Administratoren"] }""");
        Assert.Equal(RglCodesEngine.InvalidRule, LoadRule(badSid));
        var noLimit = ValidRule();
        noLimit["check"] = JsonNode.Parse("""{ "type": "localGroup", "sid": "S-1-5-32-544" }""");
        Assert.Equal(RglCodesEngine.InvalidRule, LoadRule(noLimit));
        // Präfix der Datei und ID passen nicht
        Assert.Equal(RglCodesEngine.InvalidRule, LoadRuleFile(RuleFile("TX", ValidRule("TY-01"))));
    }

    [Fact]
    public void Ungueltige_Tabellen_und_unbekannte_Dateien_RGL0211()
    {
        const string Builds = """{ "schema": "riegel.builds/1", "builds": [ { "build": 1, "name": "x", "releases": [ { "ubr": 2, "date": "2026-01-01" }, { "ubr": 1, "date": "2026-02-01" } ], "source": "https://example.org/b", "seen": "2026-10-18" } ] }""";
        Assert.Equal(RglCodesEngine.InvalidTable, Code(() => CatalogueProvider.FromFiles(Files(("lifecycle/builds.json", Builds)))));
        Assert.Equal(RglCodesEngine.InvalidTable, Code(() => CatalogueProvider.FromFiles(Files(("extra/notes.json", "{}")))));
        const string Data = """{ "schema": "riegel.data/1", "kind": "officeDetection", "remoteAccessTools": [] }""";
        Assert.Equal(RglCodesEngine.InvalidTable, Code(() => CatalogueProvider.FromFiles(Files(("data/x.json", Data)))));
    }

    // ---------------------------------------------------------------- Kunden-Baselines

    private static Rule ImportedRule(string baseline, string id) => RuleBuilder.Check($"IMP-{baseline}-{id}", "network")
        .Registry(@"HKLM\SOFTWARE\Policies\Kunde", "Port", RegValueType.Dword, CheckOperator.Eq, 4840, WhenMissing.Fail).RuleSet(baseline).Build();

    private static ImportedBaseline Baseline(string id, params Rule[] rules) =>
        new(new BaselineRef(id, "Kundenvorgaben (Test)", BaselineSourceKind.RiegelJson, new string('a', 64), null, FixedClock.Default, rules.Length), rules);

    [Fact]
    public void Baselines_werden_mit_eigener_RuleSetId_verbunden()
    {
        var provider = new CatalogueProvider();
        var core = VectorRunner.TestCatalogue();
        var combined = provider.Combine(core, [Baseline("kunde", ImportedRule("kunde", "R1"), ImportedRule("kunde", "R2"))]);
        Assert.Equal(core.Rules.Count + 2, combined.Rules.Count);
        Assert.Equal("kunde", combined.Find("IMP-kunde-R1")!.RuleSetId);
        Assert.NotEqual(core.Sha256, combined.Sha256);
        Assert.Equal(core.Sha256, provider.Combine(core, []).Sha256);
        // Ergebnisse tragen die RuleSetId der Baseline
        var results = new Evaluator(null).Evaluate(SnapshotBuilder.Windows11Workgroup().Build(), combined, VectorRunner.Context);
        Assert.Equal("kunde", results.Single(r => r.RuleId == "IMP-kunde-R1").RuleSetId);
    }

    [Fact]
    public void Baseline_mit_fremder_RuleSetId_doppelter_ID_oder_doppelt_RGL0212_RGL0209()
    {
        var provider = new CatalogueProvider();
        var core = VectorRunner.TestCatalogue();
        Assert.Equal(RglCodesEngine.BaselineConflict, Code(() => provider.Combine(core, [Baseline("kunde", ImportedRule("andere", "R1"))])));
        Assert.Equal(RglCodesEngine.BaselineConflict, Code(() => provider.Combine(core, [Baseline("kunde", ImportedRule("kunde", "R1")), Baseline("kunde", ImportedRule("kunde", "R2"))])));
        Assert.Equal(RglCodesEngine.DuplicateRuleId, Code(() => provider.Combine(core, [Baseline("kunde", ImportedRule("kunde", "R1"), ImportedRule("kunde", "R1"))])));
        var badId = RuleBuilder.Check("TREG-99", "network").Registry(@"HKLM\SOFTWARE\Policies\Kunde", "Port", RegValueType.Dword, CheckOperator.Eq, 1, WhenMissing.Fail).RuleSet("kunde").Build();
        Assert.Equal(RglCodesEngine.BaselineConflict, Code(() => provider.Combine(core, [Baseline("kunde", badId)])));
    }

    [Fact]
    public void Jeder_Code_hat_Texte_in_fuenf_Sprachen()
    {
        foreach (var code in RglCodesEngine.All)
            foreach (var language in new[] { "en", "de", "es", "fr", "zh-Hans" })
            {
                var text = RglCodesEngine.Message(System.Globalization.CultureInfo.GetCultureInfo(language), code, "x");
                Assert.False(string.IsNullOrWhiteSpace(text), $"{code} {language}");
                Assert.NotEqual(code, text);
            }
        Assert.Equal(RglCodesEngine.All.Count, RglCodesEngine.All.Distinct().Count());
        Assert.All(RglCodesEngine.All, c => Assert.InRange(int.Parse(c[4..], System.Globalization.CultureInfo.InvariantCulture), RglCodesEngine.First, RglCodesEngine.Last));
    }
}
