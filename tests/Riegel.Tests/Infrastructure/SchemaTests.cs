// SPDX-License-Identifier: MIT
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Model.Reports;
using Riegel.Core.Samples;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Infrastructure;

/// <summary>
/// Schemas (spec 6, 7.3): jede Datei unter schemas/ ist ein JSON Schema 2020-12; Fixtures, Regeldateien, Tabellen,
/// Vorlagen, Erfassungsplan, Katalogkopf und die Beispieldaten sind dagegen gültig und lassen sich mit den
/// Vertragstypen lesen; Beispieldaten überstehen die Rundreise byte-gleich.
/// </summary>
public sealed class SchemaTests
{
    public static TheoryData<string> SchemaFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(RepoPaths.Schemas, "*.schema.json").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(file)[..^".schema.json".Length]);
        return data;
    }

    [Theory]
    [MemberData(nameof(SchemaFiles))]
    public void Schema_ist_JSON_Schema_2020_12(string name)
    {
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Schemas, name + ".schema.json")))!;
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", node["$schema"]!.GetValue<string>());
        Assert.Equal("urn:ict-baden:riegel:schema:" + name, node["$id"]!.GetValue<string>());
        Assert.NotNull(SchemaValidator.Schema(name));
    }

    [Fact]
    public void Alle_Formate_aus_spec_6_haben_ein_Schema()
    {
        string[] ids =
        [
            SchemaIds.Snapshot, SchemaIds.Run, SchemaIds.Exceptions, SchemaIds.Tailoring, SchemaIds.Measures, SchemaIds.Plant,
            SchemaIds.Approved, SchemaIds.Import, SchemaIds.Baseline, SchemaIds.Evidence, SchemaIds.Catalogue, SchemaIds.CollectionPlan,
            SchemaIds.Rules, SchemaIds.Lifecycle, SchemaIds.Builds, SchemaIds.Capabilities, SchemaIds.Data, SchemaIds.ExceptionsTemplate,
            SchemaIds.TailoringTemplate,
        ];
        foreach (var id in ids)
            Assert.True(File.Exists(Path.Combine(RepoPaths.Schemas, SchemaValidator.FileFor(id) + ".schema.json")), id);
    }

    public static TheoryData<string> SyntheticSnapshots()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(RepoPaths.SyntheticFixtures, "*.snapshot.json").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(file));
        return data;
    }

    [Theory]
    [MemberData(nameof(SyntheticSnapshots))]
    public void Synthetische_Fixtures_sind_gueltig_und_lesbar(string fileName)
    {
        var path = Path.Combine(RepoPaths.SyntheticFixtures, fileName);
        var json = File.ReadAllText(path);
        SchemaValidator.AssertValid(json, fileName);
        var snapshot = RiegelJson.Deserialize<Snapshot>(File.ReadAllBytes(path));
        Assert.Equal(SnapshotOrigin.Synthetic, snapshot.Origin);
        Assert.Equal(snapshot.Items.Count, snapshot.Items.Select(i => i.Key.ToLowerInvariant()).Distinct().Count());
        // Die Datei entspricht der kanonischen Schreibweise (Rundreise ohne Änderung)
        Assert.Equal(json.Replace("\r\n", "\n"), RiegelJson.Serialize(snapshot));
    }

    [Fact]
    public void Die_Liste_der_synthetischen_Fixtures_ist_vollstaendig()
    {
        string[] expected =
        [
            "win11-24h2-workgroup-admin", "win11-24h2-workgroup-nonadmin", "server2022-domain-member", "win10-ltsc2019-hmi",
            "server2016-historian", "win7-sp1", "server2012r2", "win10-german-locale", "server2022-dc", "win11-defender-passive-thirdparty-av",
        ];
        foreach (var name in expected) Assert.True(File.Exists(RepoPaths.Synthetic(name)), name);
        foreach (var bundle in new[] { "PG-ALT-01_20261010.riegel-builtin", "XP-HMI-01_20261010.riegel-builtin" })
        {
            var folder = Path.Combine(RepoPaths.SyntheticFixtures, bundle);
            foreach (var file in new[] { "secedit.inf", "services-start.txt", "winlogon.txt", "hostname.txt", "ver.txt", "exitcodes.txt" })
                Assert.True(File.Exists(Path.Combine(folder, file)), $"{bundle}/{file}");
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(folder, "reg"), "*.reg"));
            // Windows schreibt .reg (Version 5.00) und secedit.inf als UTF-16 LE mit BOM
            foreach (var utf16 in Directory.GetFiles(Path.Combine(folder, "reg"), "*.reg").Append(Path.Combine(folder, "secedit.inf")))
            {
                var bytes = File.ReadAllBytes(utf16);
                Assert.True(bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xFE, $"{utf16}: UTF-16 LE mit BOM erwartet");
            }
            var reg = Encoding.Unicode.GetString(File.ReadAllBytes(Directory.GetFiles(Path.Combine(folder, "reg"), "*.reg")[0])[2..]);
            Assert.StartsWith("Windows Registry Editor Version 5.00\r\n", reg, StringComparison.Ordinal);
        }
        Assert.True(File.Exists(Path.Combine(RepoPaths.SyntheticFixtures, "PG-ALT-01_20261010.riegel-builtin", "auditpol.csv")));
        Assert.False(File.Exists(Path.Combine(RepoPaths.SyntheticFixtures, "XP-HMI-01_20261010.riegel-builtin", "auditpol.csv")), "XP hat kein auditpol /backup");
    }

    [Fact]
    public void Aufgezeichnete_Fixtures_sind_gueltig_und_ohne_parsed()
    {
        if (!Directory.Exists(RepoPaths.RecordedFixtures)) return; // kommen mit dem Paket collector
        foreach (var file in Directory.EnumerateFiles(RepoPaths.RecordedFixtures, "*.snapshot.json", SearchOption.AllDirectories))
        {
            SchemaValidator.AssertValid(File.ReadAllText(file), Path.GetFileName(file));
            var snapshot = RiegelJson.Deserialize<Snapshot>(File.ReadAllBytes(file));
            Assert.NotEqual(SnapshotOrigin.Synthetic, snapshot.Origin);
            Assert.DoesNotContain(snapshot.Items, i => i.Key.StartsWith("parsed:", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Regeldateien_Tabellen_und_Vorlagen_sind_gueltig()
    {
        var files = new List<string> { Path.Combine(RepoPaths.Rules, "catalogue.json"), RepoPaths.CollectionPlan };
        files.AddRange(RepoPaths.RuleFiles());
        foreach (var sub in new[] { "lifecycle", "capabilities", "data" })
            if (Directory.Exists(Path.Combine(RepoPaths.Rules, sub)))
                files.AddRange(Directory.EnumerateFiles(Path.Combine(RepoPaths.Rules, sub), "*.json"));
        if (Directory.Exists(RepoPaths.Templates))
            files.AddRange(Directory.EnumerateFiles(RepoPaths.Templates, "*.json", SearchOption.AllDirectories));
        foreach (var file in files)
            SchemaValidator.AssertValid(File.ReadAllText(file), Path.GetRelativePath(RepoPaths.Root, file));
    }

    [Fact]
    public void Katalogkopf_und_Erfassungsplan_lassen_sich_lesen()
    {
        var header = RiegelJson.ReadFile<CatalogueDocument>(Path.Combine(RepoPaths.Rules, "catalogue.json"), 1024 * 1024);
        Assert.Equal(("riegel", "2026.10.1"), (header.Id, header.Version));
        var plan = RiegelJson.ReadFile<CollectionPlan>(RepoPaths.CollectionPlan, 10 * 1024 * 1024);
        Assert.Equal(SchemaIds.CollectionPlan, plan.Schema);
        Assert.Equal(50_000, plan.Limits.MaxValuesTotal);
        Assert.Contains(plan.Api, a => a.Key == ItemKeys.LsaRights && a.Rights is { Count: 45 });
        Assert.Contains(plan.Api, a => a.Key == ItemKeys.LocalAdministrators);
        Assert.Contains(plan.Api, a => a.Key == ItemKeys.LocalRemoteDesktopUsers);
        Assert.Contains(plan.Api, a => a.Key == ItemKeys.LsassProtection && a.Mode == PlanMode.DeepOnly);
        Assert.Contains(plan.Raw, r => r.Key == ItemKeys.RawSeceditInf && r.Mode == PlanMode.SkipInMinimal);
        Assert.Contains(plan.Events, e => e.Key == ItemKeys.WininitEvent12);
    }

    [Fact]
    public void Beispieldaten_sind_gueltig()
    {
        SchemaValidator.AssertValid(RiegelJson.Serialize(SampleData.Plant()), "SampleData.Plant");
        foreach (var (schema, json) in SampleSets.RegisterFiles()) SchemaValidator.AssertValid(json, "SampleData " + schema);
        foreach (var (name, json) in SampleSets.RunFiles()) SchemaValidator.AssertValid(json, "SampleData Run " + name);
        foreach (var host in SampleData.Hosts) SchemaValidator.AssertValid(RiegelJson.Serialize(SampleData.Snapshot(host)), "SampleData.Snapshot " + host);
        SchemaValidator.AssertValid(RiegelJson.Serialize(SampleData.CollectionRecord().Import), "SampleData.CollectionRecord.Import");
    }

    [Theory]
    [MemberData(nameof(SampleSets.DocumentNames), MemberType = typeof(SampleSets))]
    public void Beispieldokumente_ueberstehen_die_Rundreise(string name)
    {
        var json = SampleSets.DocumentJson(name);
        object back = name switch
        {
            "report" => RiegelJson.Deserialize<ReportData>(json),
            "summary" => RiegelJson.Deserialize<SummaryData>(json),
            "targetConfig" => RiegelJson.Deserialize<TargetConfigData>(json),
            "approvalSheet" => RiegelJson.Deserialize<ApprovalSheetData>(json),
            _ => RiegelJson.Deserialize<CollectionRecordData>(json),
        };
        var again = back switch
        {
            ReportData d => RiegelJson.Serialize(d),
            SummaryData d => RiegelJson.Serialize(d),
            TargetConfigData d => RiegelJson.Serialize(d),
            ApprovalSheetData d => RiegelJson.Serialize(d),
            CollectionRecordData d => RiegelJson.Serialize(d),
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal(json, again);
        Assert.Equal(json, SampleSets.DocumentJson(name)); // deterministisch
    }

    [Fact]
    public void Beispieldaten_enthalten_jeden_Status_jedes_Flag_und_jede_Registerart()
    {
        var results = SampleData.Runs().SelectMany(r => r.Results).ToList();
        foreach (var status in Enum.GetValues<ResultStatus>()) Assert.Contains(results, r => r.Status == status);
        foreach (var flag in Enum.GetValues<ResultFlags>().Where(f => f != ResultFlags.None)) Assert.Contains(results, r => (r.Flags & flag) != 0);
        foreach (var source in Enum.GetValues<ValueSource>()) Assert.Contains(results, r => r.Source == source);
        var runs = SampleData.Runs();
        foreach (var flag in Enum.GetValues<RunFlags>().Where(f => f != RunFlags.None)) Assert.Contains(runs, r => (r.Flags & flag) != 0);
        var report = SampleData.Report();
        foreach (var kind in Enum.GetValues<BannerKind>()) Assert.Contains(report.Banners, b => b.Kind == kind);
        foreach (var state in Enum.GetValues<RegisterEntryState>()) Assert.Contains(report.Exceptions.Concat(report.ExpiredExceptions), e => e.State == state);
        foreach (var flag in Enum.GetValues<RegisterFlags>().Where(f => f != RegisterFlags.None))
            Assert.True(report.Exceptions.Any(e => (e.Flags & flag) != 0) || report.Measures.Any(m => (m.Flags & flag) != 0), flag.ToString());
        foreach (var state in Enum.GetValues<MeasureState>()) Assert.Contains(report.Measures, m => m.Entry.State == state);
        Assert.Contains(report.Measures, m => m.Overdue);
        Assert.Contains(report.Tailoring, t => t.State == RegisterEntryState.Valid);
        Assert.Contains(report.Tailoring, t => t.State == RegisterEntryState.Pending);
        Assert.Contains(report.Machines, m => m.IsDomainController);
        Assert.Contains(report.Machines, m => (m.Flags & RunFlags.StaleSnapshot) != 0);
        Assert.Contains(report.Machines, m => m.Identity is not null);
        Assert.True(report.Machines.Count(m => !m.IsDomainController && (m.Flags & RunFlags.StaleSnapshot) == 0) >= 3, "drei Rechner plus DC und veralteter");
        foreach (var status in Enum.GetValues<ItemStatus>()) Assert.Contains(SampleData.Snapshot(SampleData.Hmi).Items, i => i.Status == status);
    }

    [Fact]
    public void Json_folgt_den_Vertragsregeln()
    {
        var json = RiegelJson.Serialize(SampleData.Run(SampleData.Hmi));
        Assert.DoesNotContain("\r", json);
        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("riegel.run/1", root.GetProperty("schema").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("flags").ValueKind);
        var first = root.GetProperty("results")[0];
        Assert.Equal("fail", first.GetProperty("status").GetString());
        Assert.Equal("regression", first.GetProperty("flags")[0].GetString());
        Assert.Equal("2026-10-18T09:00:00Z", root.GetProperty("evaluatedAtUtc").GetString());
        Assert.Equal("2026-10-18", root.GetProperty("evaluationDate").GetString());
        // Umlaute lesbar, nicht als \u-Folge
        Assert.Contains("Qualitätssicherung", RiegelJson.Serialize(SampleData.Plant()), StringComparison.Ordinal);
    }

    [Fact]
    public void Pruefungen_werden_ueber_type_unterschieden_auch_wenn_type_nicht_vorne_steht()
    {
        const string json = """{ "op": "eq", "key": "HKLM\\SOFTWARE\\X", "value": "Y", "valueType": "dword", "expected": 1, "whenMissing": "fail", "type": "registry" }""";
        var check = RiegelJson.Deserialize<CheckSpec>(json);
        var registry = Assert.IsType<RegistryCheck>(check);
        Assert.Equal(RegValueType.Dword, registry.ValueType);
        Assert.StartsWith("{\n  \"type\": \"registry\"", RiegelJson.Serialize(check), StringComparison.Ordinal);
        Assert.Throws<RiegelException>(() => RiegelJson.Deserialize<CheckSpec>("""{ "type": "registry", "unknownField": 1 }"""));
        Assert.Throws<RiegelException>(() => RiegelJson.Deserialize<CheckSpec>("""{ "type": "shellCommand" }"""));
    }
}
