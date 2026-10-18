// SPDX-License-Identifier: MIT
using System.Collections;
using System.Globalization;
using System.Text.Json;
using Riegel.Collector.Collection;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Platform.Windows;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Windows.Collector;

/// <summary>Leser des eingebetteten Plans und der kleine JSON-Schreiber/-Leser des Sammlers (net35-tauglich).</summary>
public sealed class PlanAndJsonTests
{
    [Fact]
    public void Planleser_liest_den_Vertrag_wie_Core()
    {
        var plan = CollectorTestFiles.ContractPlan();
        var contract = RiegelJson.ReadFile<CollectionPlan>(RepoPaths.CollectionPlan, 10 * 1024 * 1024);

        Assert.Equal(contract.Limits.MaxValuesTotal, plan.MaxValuesTotal);
        Assert.Equal(contract.Limits.RecursiveMaxDepth, plan.RecursiveMaxDepth);
        Assert.Equal(contract.Limits.MaxBinaryBytes, plan.MaxBinaryBytes);
        Assert.Equal(contract.Limits.ToolTimeoutSeconds, plan.ToolTimeoutSeconds);
        Assert.Equal(contract.Limits.MaxRawTextBytes, plan.MaxRawTextBytes);
        Assert.Equal(contract.Limits.WindowsUpdateHistoryEntries, plan.WindowsUpdateHistoryEntries);
        Assert.Equal(contract.Limits.TempPathMaxChars, plan.TempPathMaxChars);
        Assert.Equal(contract.Limits.MaxStringChars, plan.MaxStringChars);
        Assert.Equal(contract.Registry.Roots.Concat(contract.Registry.Keys).Select(r => r.Path), plan.Registry.Select(r => r.Path));
        Assert.Equal(contract.Registry.Roots.Concat(contract.Registry.Keys).Select(r => r.Values?.ToArray()), plan.Registry.Select(r => r.Values));
        Assert.Equal(contract.Wmi.Select(w => w.Key), plan.Wmi.Select(w => w.Key));
        Assert.Equal(contract.Wmi.Select(w => w.Where), plan.Wmi.Select(w => w.Where));
        Assert.Equal(contract.Wmi.SelectMany(w => w.Properties), plan.Wmi.SelectMany(w => w.Properties));
        Assert.Equal(contract.Wmi.SelectMany(w => w.Methods.Select(m => m.Name)), plan.Wmi.SelectMany(w => w.Methods.Select(m => m.Name)));
        Assert.Equal(contract.Api.Select(a => a.Key), plan.Api.Select(a => a.Key));
        Assert.Equal(contract.Api.Select(a => a.Mode.ToString()), plan.Api.Select(a => a.Mode), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(contract.Api.Single(a => a.Api == "lsa-rights").Rights, plan.Api.Single(a => a.Api == "lsa-rights").Rights);
        Assert.Equal(contract.Events.Select(e => e.Xpath), plan.Events.Select(e => e.Xpath));
        Assert.Equal(contract.Raw.Select(r => r.Key), plan.Raw.Select(r => r.Key));
        Assert.Equal(contract.Raw.SelectMany(r => r.Arguments ?? []), plan.Raw.SelectMany(r => r.Arguments));
        Assert.Equal(contract.AllowedWmiMethods, plan.AllowedWmiMethods);
    }

    [Fact]
    public void Windows_Anbindung_bettet_den_Plan_des_Vertrags_ein()
    {
        Assert.Equal(CollectorTestFiles.ContractPlan().Wmi.Count, WindowsPlatform.LoadPlan().Wmi.Count);
        Assert.Equal(CollectorTestFiles.ContractPlan().Registry.Count, WindowsPlatform.LoadPlan().Registry.Count);
    }

    [Theory]
    [InlineData("{\"schema\":\"riegel.other/1\"}")]
    [InlineData("{\"schema\":\"riegel.collection-plan/1\"}")]
    [InlineData("[1,2]")]
    [InlineData("{\"schema\":\"riegel.collection-plan/1\",")]
    public void Ungueltiger_Plan_wird_abgelehnt(string json) => Assert.ThrowsAny<FormatException>(() => Plan.Parse(json));

    [Fact]
    public void Plan_mit_fremder_Registry_Wurzel_wird_abgelehnt()
    {
        string json = File.ReadAllText(RepoPaths.CollectionPlan).Replace(@"""HKLM\\SOFTWARE\\Policies""", @"""HKCU\\Software""");
        Assert.Throws<FormatException>(() => Plan.Parse(json));
    }

    [Fact]
    public void Schreiber_maskiert_richtig_und_jeder_JSON_Leser_nimmt_es_an()
    {
        string tricky = "\"\\\n\r\t\b\f\u0001\u007f/äöü€ 中文 😀 \ud800 x";
        var o = new JObject().Add("s", tricky).Add("n", null).Add("b", true).Add("i", -5).Add("u", 4294967295u)
            .Add("l", long.MinValue).Add("d", 0.5).Add("nan", double.NaN).Add("a", new List<object> { 1, "x" }).Add("leer", new JObject())
            .Add("liste", new List<object>());
        string json = JsonWriter.Write(o);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(tricky.Replace("\ud800 ", "� "), root.GetProperty("s").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("n").ValueKind);
        Assert.Equal(4294967295L, root.GetProperty("u").GetInt64());
        Assert.Equal(long.MinValue, root.GetProperty("l").GetInt64());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("nan").ValueKind);
        Assert.Contains("\"leer\": {}", json, StringComparison.Ordinal);
        Assert.Contains("\"liste\": []", json, StringComparison.Ordinal);
        Assert.Contains("äöü€ 中文", json, StringComparison.Ordinal); // lesbar, nicht als \u-Folge
        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Zahlen_sind_kulturunabhaengig()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            string json = JsonWriter.Write(new JObject().Add("d", 1.25).Add("g", 1234567));
            Assert.Contains("1.25", json, StringComparison.Ordinal);
            Assert.Contains("1234567", json, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Leser_liest_was_der_Schreiber_schreibt()
    {
        var o = new JObject().Add("a", "x\"y\\zä").Add("b", new List<object?> { 1L, 2.5, false, null }).Add("c", new JObject().Add("d", "e"));
        var back = (JObject)JsonReader.Parse(JsonWriter.Write(o));
        Assert.Equal("x\"y\\zä", back.Get("a"));
        Assert.Equal(new object?[] { 1L, 2.5, false, null }, ((IList)back.Get("b")).Cast<object?>());
        Assert.Equal("e", ((JObject)back.Get("c")).Get("d"));
    }

    [Fact]
    public void Leser_begrenzt_die_Tiefe()
    {
        string deep = new string('[', 70) + new string(']', 70);
        Assert.Throws<FormatException>(() => JsonReader.Parse(deep));
    }

    [Fact]
    public void Export_ist_ein_gueltiger_Snapshot_des_Vertrags()
    {
        var data = new SnapshotData
        {
            CollectorName = "Riegel.Collector", CollectorVersion = "0.1.0", Clr = "4.0.30319.42000", Is64BitProcess = true,
            CollectedAtUtc = new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc), IsAdmin = true, Origin = "collector",
        };
        data.Machine.HostName = "PC-01";
        data.Machine.Build = 26100;
        data.Machine.LastBootUtc = new DateTime(2026, 10, 18, 8, 0, 0, 500, DateTimeKind.Utc);
        data.Coverage.Add(new CoverageInfo { Root = @"HKLM\SOFTWARE\Policies", Recursive = true, MaxDepth = 8 });
        data.Items.Add(CollectedItem.Ok(@"reg:HKLM\SOFTWARE\Policies", new JObject().Add("values", new JObject()).Add("subkeys", new List<object>())));
        data.Items.Add(CollectedItem.Fail(ItemKeys.LsassProtection, Statuses.NotCollected, null, "only with --deep"));
        data.Items.Add(new CollectedItem(@"wmi:root\cimv2:Win32_Tpm", Statuses.Denied) { ErrorCode = -2147217405, Truncated = true });
        string json = JsonWriter.Write(data.ToJson());
        SnapshotSchema.AssertValid(json, "Export");
        var snapshot = RiegelJson.Deserialize<Snapshot>(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Equal(SnapshotOrigin.Collector, snapshot.Origin);
        Assert.Equal(ItemStatus.NotCollected, snapshot.Items[1].Status);
        Assert.True(snapshot.Items[2].Truncated);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 8, 0, 0, 500, TimeSpan.Zero), snapshot.Machine.LastBootUtc);
        Assert.Contains("\"lastBootUtc\": \"2026-10-18T08:00:00.5Z\"", json, StringComparison.Ordinal);
        Assert.True(data.Incomplete);
    }

    [Theory]
    [InlineData("PC-01", "PC-01")]
    [InlineData("Prüfstand Ä", "Pruefstand-Ae")]
    [InlineData("con", "_con")]
    [InlineData("---", "unknown")]
    [InlineData("a b..c", "a-b..c")]
    public void Dateiname_des_Exports_wie_Core(string host, string expected)
    {
        Assert.Equal(expected, ExportNames.SafeHost(host));
        Assert.Equal(FileNames.Host(host), ExportNames.SafeHost(host));
        var at = new DateTimeOffset(2026, 10, 18, 9, 1, 2, TimeSpan.Zero);
        Assert.Equal(SnapshotFile.FileName(host, at), ExportNames.FileName(host, at.UtcDateTime, false));
        Assert.Equal(SnapshotFile.FileName(host, at, gzip: true), ExportNames.FileName(host, at.UtcDateTime, true));
    }
}
