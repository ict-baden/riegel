// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Evaluation;
using Riegel.Core.Facts;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;
using Riegel.Tests.RuleVectors;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Facts;

/// <summary>Fakten aus spec 2.5 aus den synthetischen Fixtures und gebauten Exporten; unbekannt ist immer JSON null.</summary>
public sealed class FactsDeriverTests
{
    private static readonly ICatalogue Catalogue = VectorRunner.TestCatalogue();

    private static Snapshot Fixture(string name) => RiegelJson.ReadFile<Snapshot>(RepoPaths.Synthetic(name), 64L * 1024 * 1024);

    private static FactSet Facts(Snapshot s) => FactsDeriver.DeriveDetailed(new EvidenceStore(s), Catalogue);

    private static string Show(FactSet f, string fact) => f.Values.TryGetValue(fact, out var v) ? v.GetRawText() : "(fehlt)";

    [Fact]
    public void Jeder_Fakt_aus_spec_2_5_ist_da_auch_wenn_unbekannt()
    {
        var facts = new FactsDeriver().Derive(SnapshotBuilder.Machine("LEER", null, null, null).NoCoverage().Build(), Catalogue);
        foreach (var name in FactNames.Fixed.Keys) Assert.True(facts.ContainsKey(name), name);
        foreach (var capability in Catalogue.Capabilities.Capabilities) Assert.True(facts.ContainsKey(FactNames.CapabilityPrefix + capability.Capability));
        Assert.Equal(facts.Keys.Order(StringComparer.Ordinal), facts.Keys);
        // Nichts geraten: ohne Kopf und ohne Einträge ist alles unbekannt, defender.mode „unknown“
        foreach (var (name, value) in facts)
            if (name == FactNames.DefenderMode) Assert.Equal("unknown", value.GetString());
            else Assert.True(value.ValueKind == JsonValueKind.Null, $"{name} = {value.GetRawText()}");
    }

    [Fact]
    public void Windows11_Arbeitsgruppe()
    {
        var f = Facts(Fixture("win11-24h2-workgroup-admin"));
        Assert.Equal("client", f.Kind);
        Assert.Equal(26100, f.Build);
        Assert.Equal(6899, f.Ubr);
        Assert.Equal("Professional", f.Edition);
        Assert.False(f.Bool(FactNames.OsServerCore));
        Assert.False(f.DomainJoined);
        Assert.False(f.IsDc);
        Assert.False(f.Bool(FactNames.MachineMobile));
        Assert.Equal("active", f.Text(FactNames.DefenderMode));
        Assert.DoesNotContain(FactNames.DefenderMode, f.Derived);
        Assert.True(f.Bool(FactNames.DefenderRtp));
        Assert.False(f.Bool(FactNames.AvThirdPartyActive));
        Assert.DoesNotContain(FactNames.AvThirdPartyActive, f.Heuristic);
        Assert.False(f.Bool(FactNames.OfficeInstalled));
        Assert.False(f.Bool(FactNames.RdpEnabled));
        Assert.True(f.Bool(FactNames.NetGatewayConfigured));
        Assert.Equal(1, f.Number(FactNames.NetActiveAdapters));
        Assert.False(f.Bool(FactNames.NetWireless));
        Assert.False(f.Bool(FactNames.NetIpForwarding));
        Assert.False(f.Bool(FactNames.NetProxyConfigured));
        Assert.True(f.Bool("capability.testFeature"));
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 6, 30, 0, TimeSpan.Zero), f.LastBootUtc);
        Assert.Contains(Known.MpStatus, f.SourcesOf(FactNames.DefenderMode));
    }

    [Fact]
    public void Defender_passiv_mit_Drittanbieter_ueber_WSC()
    {
        var f = Facts(Fixture("win11-defender-passive-thirdparty-av"));
        Assert.Equal("passive", f.Text(FactNames.DefenderMode));
        Assert.True(f.Bool(FactNames.AvThirdPartyActive));
        Assert.DoesNotContain(FactNames.AvThirdPartyActive, f.Heuristic);
        Assert.False(f.Bool(FactNames.DefenderRtp));
    }

    [Fact]
    public void Windows7_ohne_Defender_mit_heuristischem_Drittanbieter()
    {
        var f = Facts(Fixture("win7-sp1"));
        Assert.Equal("absent", f.Text(FactNames.DefenderMode));
        Assert.True(f.Bool(FactNames.AvThirdPartyActive));
        Assert.Contains(FactNames.AvThirdPartyActive, f.Heuristic);
        Assert.Null(f.Bool(FactNames.DefenderRtp));
        Assert.False(f.Bool("capability.testFeature"));
    }

    [Fact]
    public void Server2012R2_ohne_Sicherheitscenter_Drittanbieter_unbekannt()
    {
        var f = Facts(Fixture("server2012r2"));
        Assert.Equal("server", f.Kind);
        Assert.Equal("absent", f.Text(FactNames.DefenderMode));
        Assert.Null(f.Bool(FactNames.AvThirdPartyActive));
    }

    [Theory]
    [InlineData("server2022-domain-member", true, false)]
    [InlineData("server2022-dc", true, true)]
    [InlineData("server2016-historian", false, false)]
    [InlineData("win10-german-locale", false, false)]
    public void Domaene_und_Domaenencontroller(string fixture, bool joined, bool dc)
    {
        var f = Facts(Fixture(fixture));
        Assert.Equal(joined, f.DomainJoined);
        Assert.Equal(dc, f.IsDc);
    }

    [Fact]
    public void Hergeleiteter_Defender_Modus_ohne_AMRunningMode()
    {
        var active = SnapshotBuilder.Windows11Workgroup()
            .WmiRows(@"root\Microsoft\Windows\Defender", "MSFT_MpComputerStatus", new { AntivirusEnabled = true, RealTimeProtectionEnabled = true, AMServiceEnabled = true })
            .Api(ItemKeys.WscAntivirus, new[] { new { name = "Windows Defender", state = "on", signature = "upToDate" } }).Build();
        var f = Facts(active);
        Assert.Equal("active", f.Text(FactNames.DefenderMode));
        Assert.Contains(FactNames.DefenderMode, f.Derived);
        Assert.Contains(ItemKeys.WscAntivirus, f.SourcesOf(FactNames.DefenderMode));

        var off = SnapshotBuilder.Windows11Workgroup()
            .WmiRows(@"root\Microsoft\Windows\Defender", "MSFT_MpComputerStatus", new { AntivirusEnabled = false, RealTimeProtectionEnabled = false, AMServiceEnabled = true })
            .Build();
        Assert.Equal("off", Facts(off).Text(FactNames.DefenderMode));

        // Nicht eindeutig: Defender an, aber ein anderer Virenschutz auch → unknown
        var ambiguous = SnapshotBuilder.Windows11Workgroup()
            .WmiRows(@"root\Microsoft\Windows\Defender", "MSFT_MpComputerStatus", new { AntivirusEnabled = true, RealTimeProtectionEnabled = true })
            .Api(ItemKeys.WscAntivirus, new[] { new { name = "Beispiel-Virenschutz", state = "on", signature = "upToDate" } }).Build();
        Assert.Equal("unknown", Facts(ambiguous).Text(FactNames.DefenderMode) ?? Show(Facts(ambiguous), FactNames.DefenderMode).Trim('"'));
        Assert.True(Facts(ambiguous).IsUnknown(FactNames.DefenderMode));
    }

    [Theory]
    [InlineData("Normal", "active")]
    [InlineData("Passive", "passive")]
    [InlineData("Passive Mode", "passive")]
    [InlineData("EDR Block Mode", "edrBlock")]
    [InlineData("Irgendwas", "unknown")]
    public void AMRunningMode_wird_abgebildet(string amRunningMode, string expected)
    {
        // AntivirusEnabled fehlt, damit keine Herleitung greift
        var s = SnapshotBuilder.Windows11Workgroup()
            .WmiRows(@"root\Microsoft\Windows\Defender", "MSFT_MpComputerStatus", new { AMRunningMode = amRunningMode }).Build();
        var f = Facts(s);
        Assert.Equal(expected, f.Values[FactNames.DefenderMode].GetString());
    }

    [Fact]
    public void Kopf_fehlt_Registry_ergaenzt_wie_bei_Bordmittel_Paketen()
    {
        var s = SnapshotBuilder.Machine("ALT", null, null, null)
            .RegistryValue(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild", RegValueType.Sz, "7601")
            .RegistryValue(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", RegValueType.Sz, "Professional")
            .RegistryValue(@"HKLM\SYSTEM\CurrentControlSet\Control\ProductOptions", "ProductType", RegValueType.Sz, "WinNT")
            .Build();
        var f = Facts(s);
        Assert.Equal(7601, f.Build);
        Assert.Equal("client", f.Kind);
        Assert.Equal("Professional", f.Edition);
        Assert.Null(f.DomainJoined);
        Assert.Null(f.Ubr);
    }

    [Fact]
    public void Office_RDP_WLAN_Proxy_und_Weiterleitung()
    {
        var s = SnapshotBuilder.Windows11Workgroup()
            .RegistryKey(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "BspOffice")
            .RegistryValue(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\BspOffice", "DisplayName", RegValueType.Sz, "Beispiel-Office 2024")
            .RegistryValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services", "fDenyTSConnections", RegValueType.Dword, 0)
            .RegistryValue(@"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", RegValueType.Dword, 1)
            .RegistryValue(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "IPEnableRouter", RegValueType.Dword, 1)
            .WmiRows(@"root\wmi", "MSNdis_PhysicalMediumType", new { InstanceName = "WLAN", NdisPhysicalMediumType = 9, Active = true })
            .Api(ItemKeys.WinHttpProxy, new { accessType = 3, proxy = "proxy.corp.example:8080", bypass = "<local>" })
            .WmiRows(@"root\cimv2", "Win32_ComputerSystem", new { DomainRole = 0, PCSystemType = 2 })
            .Build();
        var f = Facts(s);
        Assert.True(f.Bool(FactNames.OfficeInstalled));
        Assert.True(f.Bool(FactNames.RdpEnabled)); // Richtlinie vor lokalem Wert
        Assert.True(f.Bool(FactNames.NetIpForwarding));
        Assert.True(f.Bool(FactNames.NetWireless));
        Assert.True(f.Bool(FactNames.NetProxyConfigured));
        Assert.True(f.Bool(FactNames.MachineMobile));
        // Ohne Daten zur Office-Erkennung im Katalog bleibt der Fakt unbekannt
        var bare = new FactsDeriver().Derive(s, RiegelServices.Create(new NullPlatform(), new FixedClock()).Catalogue.LoadEmbedded());
        Assert.Equal(JsonValueKind.Null, bare[FactNames.OfficeInstalled].ValueKind);
    }

    [Fact]
    public void Nicht_verfuegbare_Belege_machen_Fakten_unbekannt()
    {
        var s = SnapshotBuilder.Windows11Workgroup()
            .Denied(Known.ComputerSystem)
            .Denied(Known.MpStatus)
            .Denied(ItemKeys.WscAntivirus)
            .Denied(Known.SecurityCenterAv)
            .Denied(Known.AdapterConfiguration)
            .Item(ItemKeys.Registry(@"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server"), ItemStatus.Denied)
            .Build();
        var f = Facts(s);
        Assert.Null(f.Bool(FactNames.MachineMobile));
        Assert.True(f.IsUnknown(FactNames.DefenderMode));
        Assert.Null(f.Bool(FactNames.AvThirdPartyActive));
        Assert.Null(f.Bool(FactNames.NetGatewayConfigured));
        Assert.Null(f.Bool(FactNames.RdpEnabled));
        Assert.False(f.DomainJoined); // aus dem Kopf
    }
}
