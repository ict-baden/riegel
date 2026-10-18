// SPDX-License-Identifier: MIT
using System.Collections;
using Riegel.Collector.Collection;
using static Riegel.Tests.Windows.Collector.CollectorTestFiles;
using static Riegel.Tests.Windows.Collector.FakeRegistry;

namespace Riegel.Tests.Windows.Collector;

/// <summary>
/// Registry nach dem Plan mit einer Attrappe: Abdeckung (vollständig, verweigert, abgeschnitten), KeyMissing für
/// fehlende Planschlüssel, Grenze der Werte, Filter, Tiefe, Kodierung der Werttypen, Schwärzen und SNMP-Regel.
/// </summary>
public sealed class RegistryCollectorTests
{
    private static SnapshotData Collect(FakeRegistry registry, int maxValues, params PlanRegistry[] entries)
    {
        var data = new SnapshotData();
        new RegistryCollector(registry, RegistryPlan(maxValues, entries), null, data).CollectAll();
        return data;
    }

    private static JObject Values(SnapshotData data, string key) =>
        (JObject)((JObject)data.Find(key)!.Value).Get("values");

    [Fact]
    public void Vollstaendiger_Bereich_mit_Unterschluesseln_und_Abdeckung()
    {
        var registry = new FakeRegistry()
            .Key(@"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", Dword("EnableMulticast", 0))
            .Key(@"SOFTWARE\Policies\Microsoft\Windows\System", Dword("EnableSmartScreen", 1));
        var data = Collect(registry, 50_000, Entry(@"HKLM\SOFTWARE\Policies"));

        var coverage = Assert.Single(data.Coverage);
        Assert.True(coverage.Complete);
        Assert.True(coverage.Recursive);
        Assert.Equal(8, coverage.MaxDepth);
        Assert.Empty(coverage.DeniedSubkeys);
        Assert.Null(coverage.TruncatedAt);
        Assert.Equal(0u, ((JObject)Values(data, @"reg:HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient").Get("EnableMulticast")).Get("v"));
        var root = (JObject)data.Find(@"reg:HKLM\SOFTWARE\Policies")!.Value;
        Assert.Equal(["Microsoft"], ((IList)root.Get("subkeys")).Cast<string>());
        Assert.All(data.Items, i => Assert.Equal(Statuses.Ok, i.Status));
    }

    [Fact]
    public void Fehlender_Bereich_ist_KeyMissing_mit_vollstaendiger_Abdeckung()
    {
        var data = Collect(new FakeRegistry(), 50_000, Entry(@"HKLM\SOFTWARE\Microsoft\Policies\LAPS"),
            Entry(@"HKLM\SYSTEM\CurrentControlSet\Services\mrxsmb10", "key", 0));
        Assert.All(data.Items, i => Assert.Equal(Statuses.KeyMissing, i.Status));
        Assert.Equal(2, data.Items.Count);
        Assert.All(data.Coverage, c => Assert.True(c.Complete));
        Assert.False(data.Coverage[1].Recursive);
        Assert.Equal(0, data.Coverage[1].MaxDepth);
    }

    [Fact]
    public void Verweigerter_Unterschluessel_steht_nur_in_der_Abdeckung()
    {
        var registry = new FakeRegistry()
            .Key(@"SYSTEM\CurrentControlSet\Control\Lsa", Dword("RunAsPPL", 2))
            .Deny(@"SYSTEM\CurrentControlSet\Control\Lsa\Audit\AuditPolicy");
        var data = Collect(registry, 50_000, Entry(@"HKLM\SYSTEM\CurrentControlSet\Control\Lsa"));
        var coverage = Assert.Single(data.Coverage);
        Assert.True(coverage.Complete);
        Assert.Equal([@"HKLM\SYSTEM\CurrentControlSet\Control\Lsa\Audit\AuditPolicy"], coverage.DeniedSubkeys);
        Assert.Null(data.Find(@"reg:HKLM\SYSTEM\CurrentControlSet\Control\Lsa\Audit\AuditPolicy"));
        Assert.DoesNotContain(data.Items, i => i.Status == Statuses.Denied);
    }

    [Fact]
    public void Verweigerter_Planschluessel_ist_Denied_und_unvollstaendig()
    {
        var registry = new FakeRegistry().Deny(@"SYSTEM\CurrentControlSet\Services\EventLog\Security");
        var data = Collect(registry, 50_000, Entry(@"HKLM\SYSTEM\CurrentControlSet\Services\EventLog\Security", "key", 0));
        var item = Assert.Single(data.Items);
        Assert.Equal(Statuses.Denied, item.Status);
        Assert.Equal(5, item.ErrorCode);
        Assert.False(data.Coverage[0].Complete);
        Assert.Equal([@"HKLM\SYSTEM\CurrentControlSet\Services\EventLog\Security"], data.Coverage[0].DeniedSubkeys);
    }

    [Fact]
    public void Grenze_der_Werte_schneidet_ab_und_markiert_die_restlichen_Bereiche()
    {
        var registry = new FakeRegistry()
            .Key(@"SOFTWARE\Policies\A", Dword("v1", 1), Dword("v2", 2))
            .Key(@"SOFTWARE\Policies\B", Dword("v3", 3), Dword("v4", 4))
            .Key(@"SYSTEM\CurrentControlSet\Control\Lsa", Dword("RunAsPPL", 2));
        var data = Collect(registry, 3, Entry(@"HKLM\SOFTWARE\Policies"), Entry(@"HKLM\SYSTEM\CurrentControlSet\Control\Lsa"));

        Assert.False(data.Coverage[0].Complete);
        Assert.Equal(@"HKLM\SOFTWARE\Policies\B", data.Coverage[0].TruncatedAt);
        var truncated = data.Find(@"reg:HKLM\SOFTWARE\Policies\B")!;
        Assert.True(truncated.Truncated);
        Assert.Equal(1, Values(data, @"reg:HKLM\SOFTWARE\Policies\B").Count);

        Assert.False(data.Coverage[1].Complete);
        Assert.Equal(@"HKLM\SYSTEM\CurrentControlSet\Control\Lsa", data.Coverage[1].TruncatedAt);
        Assert.Equal(Statuses.NotCollected, data.Find(@"reg:HKLM\SYSTEM\CurrentControlSet\Control\Lsa")!.Status);
    }

    [Fact]
    public void Wertfilter_und_eine_Ebene_children()
    {
        var registry = new FakeRegistry()
            .Key(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\App1", Sz("DisplayName", "Beispiel"), Sz("InstallLocation", @"C:\Beispiel"))
            .Key(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\App1\Tief", Sz("DisplayName", "zu tief"));
        string[] filter = ["DisplayName", "DisplayVersion", "Publisher"];
        var data = Collect(registry, 50_000, Entry(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "children", 1, filter));

        var app = (JObject)data.Find(@"reg:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\App1")!.Value;
        var values = (JObject)app.Get("values");
        Assert.True(values.Has("DisplayName"));
        Assert.False(values.Has("InstallLocation"));
        Assert.Equal(filter, ((IList)app.Get("valueFilter")).Cast<string>());
        Assert.Null(data.Find(@"reg:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\App1\Tief"));
        Assert.Equal(1, data.Coverage[0].MaxDepth);
        Assert.True(data.Coverage[0].Recursive);
    }

    [Fact]
    public void Tiefe_des_Plans_begrenzt_die_Rekursion()
    {
        var registry = new FakeRegistry().Key(@"SOFTWARE\Policies\1\2\3", Dword("x", 1));
        var data = Collect(registry, 50_000, Entry(@"HKLM\SOFTWARE\Policies", "recursive", 2));
        Assert.NotNull(data.Find(@"reg:HKLM\SOFTWARE\Policies\1\2"));
        Assert.Null(data.Find(@"reg:HKLM\SOFTWARE\Policies\1\2\3"));
    }

    [Fact]
    public void Werttypen_nach_dem_Vertrag()
    {
        var big = Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray();
        var registry = new FakeRegistry().Key(@"SOFTWARE\Policies\T",
            Dword("d", 4294967295), Qword("q", ulong.MaxValue), Sz("s", "text"), Sz("e", @"%SystemRoot%\x", RegTypes.ExpandSz),
            Multi("m", "a", "b"), Binary("b", [1, 2, 3]), Binary("big", big), new RegValue("odd", RegTypes.Dword, [1, 2]),
            new RegValue("leer", RegTypes.None, []));
        var data = Collect(registry, 50_000, Entry(@"HKLM\SOFTWARE\Policies"));
        var v = Values(data, @"reg:HKLM\SOFTWARE\Policies\T");
        JObject V(string n) => (JObject)v.Get(n);

        Assert.Equal("dword", V("d").Get("t"));
        Assert.Equal(4294967295u, V("d").Get("v"));
        Assert.Equal("qword", V("q").Get("t"));
        Assert.Equal("18446744073709551615", V("q").Get("v"));
        Assert.Equal("text", V("s").Get("v"));
        Assert.Equal(@"%SystemRoot%\x", V("e").Get("v")); // nie expandiert
        Assert.Equal("expand_sz", V("e").Get("t"));
        Assert.Equal(["a", "b"], ((IList)V("m").Get("v")).Cast<string>());
        Assert.Equal(Convert.ToBase64String([1, 2, 3]), V("b").Get("v"));
        Assert.Equal(true, V("big").Get("truncated"));
        Assert.Equal(5000, V("big").Get("len"));
        Assert.Equal(4096, Convert.FromBase64String((string)V("big").Get("v")).Length);
        Assert.Equal("unknown", V("odd").Get("t"));
        Assert.Equal("none", V("leer").Get("t"));
        Assert.False(V("leer").Has("v"));
    }

    [Theory]
    [InlineData("DefaultPassword", true)]
    [InlineData("AltDefaultPassword", true)]
    [InlineData("ProxyPwd", true)]
    [InlineData("ClientSecret", true)]
    [InlineData("AccessToken", true)]
    [InlineData("StoredCredential", true)]
    [InlineData("Passphrase", true)]
    [InlineData("CommunityName", true)]
    [InlineData("PASSWD", true)]
    [InlineData("EnableMulticast", false)]
    public void Geheimnis_Namen_werden_erkannt(string name, bool secret) => Assert.Equal(secret, Redaction.IsSecretName(name));

    [Fact]
    public void Zeichenketten_mit_Geheimnis_Namen_werden_geschwaerzt_Zahlen_Schalter_nicht()
    {
        var registry = new FakeRegistry().Key(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon",
            Sz("DefaultPassword", "geheim"), Sz("AltDefaultPassword", ""), Dword("DefaultPassword2", 7),
            Sz("ProxyPassword", "x"), Dword("LocalAccountTokenFilterPolicy", 1), Dword("DisablePasswordSaving", 1),
            new RegValue("DefaultPassword", RegTypes.Dword, BitConverter.GetBytes(1u)));
        var data = Collect(registry, 50_000, Entry(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "key", 0));
        var v = Values(data, @"reg:HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
        JObject V(string n) => (JObject)v.Get(n);

        // DefaultPassword steht zweimal in der Attrappe; der zweite (DWORD) ersetzt den ersten – auch er bleibt geschwärzt
        Assert.Equal(true, V("DefaultPassword").Get("redacted"));
        Assert.False(V("DefaultPassword").Has("v"));
        Assert.Equal(true, V("AltDefaultPassword").Get("empty"));
        Assert.Equal(true, V("ProxyPassword").Get("redacted"));
        Assert.Equal(false, V("ProxyPassword").Get("empty"));
        Assert.Equal(1u, V("LocalAccountTokenFilterPolicy").Get("v"));
        Assert.Equal(1u, V("DisablePasswordSaving").Get("v"));
        Assert.Equal(7u, V("DefaultPassword2").Get("v"));
        Assert.DoesNotContain("geheim", JsonWriter.Write(data.ToJson()), StringComparison.Ordinal);
    }

    [Fact]
    public void SNMP_Communities_und_Trap_Ziele_werden_umbenannt()
    {
        var registry = new FakeRegistry()
            .Key(@"SOFTWARE\Policies\SNMP\Parameters\ValidCommunities", Dword("geheim-1", 4), Dword("geheim-2", 8))
            .Key(@"SOFTWARE\Policies\SNMP\Parameters\TrapConfiguration\geheim-3", Sz("1", "manager.example.invalid"));
        var data = Collect(registry, 50_000, Entry(@"HKLM\SOFTWARE\Policies"));
        var communities = Values(data, @"reg:HKLM\SOFTWARE\Policies\SNMP\Parameters\ValidCommunities");
        Assert.Equal(["redacted-1", "redacted-2"], Enumerable.Range(0, communities.Count).Select(communities.NameAt));
        Assert.Equal(4u, ((JObject)communities.Get("redacted-1")).Get("v"));
        var traps = (JObject)data.Find(@"reg:HKLM\SOFTWARE\Policies\SNMP\Parameters\TrapConfiguration")!.Value;
        Assert.Equal(["redacted-1"], ((IList)traps.Get("subkeys")).Cast<string>());
        string json = JsonWriter.Write(data.ToJson());
        Assert.DoesNotContain("geheim", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Doppelte_Schluessel_aus_zwei_Planeintraegen_erscheinen_einmal()
    {
        var registry = new FakeRegistry().Key(@"SOFTWARE\Policies\X", Dword("a", 1));
        var data = Collect(registry, 50_000, Entry(@"HKLM\SOFTWARE\Policies"), Entry(@"HKLM\SOFTWARE\Policies\X", "key", 0));
        Assert.Single(data.Items, i => i.Key == @"reg:HKLM\SOFTWARE\Policies\X");
        Assert.Equal(2, data.Coverage.Count);
    }

    [Fact]
    public void Abdeckung_entspricht_dem_Vertragsplan()
    {
        var plan = ContractPlan();
        var data = new SnapshotData();
        new RegistryCollector(new FakeRegistry(), plan, null, data).CollectAll();
        Assert.Equal(plan.Registry.Count, data.Coverage.Count);
        Assert.Equal(plan.Registry.Select(r => r.Path), data.Coverage.Select(c => c.Root));
        Assert.All(data.Items, i => Assert.Equal(Statuses.KeyMissing, i.Status));
    }
}
