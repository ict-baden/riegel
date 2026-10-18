// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Parsing;
using static Riegel.Tests.Parsing.ParsingSamples;

namespace Riegel.Tests.Parsing;

/// <summary>gpresult-XML (RSoP) und Firewall-Regeln nach der Grammatik aus [MS-GPFAS].</summary>
public sealed class GpresultAndFirewallTests
{
    internal const string DomainGpoId = "{00000000-0000-4000-8000-00000000a001}";
    internal const string FilteredGpoId = "{00000000-0000-4000-8000-00000000a002}";

    /// <summary>Handgeschriebenes Beispiel: lokale Gruppenrichtlinie und eine (erfundene) Domänen-GPO „Linie-3-Basis“.</summary>
    internal static string Sample(bool withAdmxPolicy = false) => Gpresult(
        [("Local Group Policy", "LocalGPO"), ("Linie-3-Basis", DomainGpoId)],
        Extension("RegistrySettings", RegistryNs,
            RegistrySetting("LocalGPO", @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", null),
            RegistrySetting("LocalGPO", @"Software\Policies\Beispiel", "LokalerWert"),
            RegistrySetting(DomainGpoId, @"Software\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast"),
            RegistrySetting(DomainGpoId, @"Software\Policies\Microsoft\Windows NT\Terminal Services", "fDisableCdm", precedence: 2),
            RegistrySetting(DomainGpoId, @"Software\Policies\Beispiel\Autologon", "DefaultPassword", "geheim"),
            withAdmxPolicy ? Policy(DomainGpoId, "Turn off multicast name resolution") : ""),
        Extension("SecuritySettings", SecurityNs,
            Account(DomainGpoId, "MinimumPasswordLength", 14),
            SecurityOption(DomainGpoId, @"MACHINE\System\CurrentControlSet\Control\Lsa\NoLMHash", 1)));

    [Fact]
    public void Liest_GPOs_gewinnende_Registry_und_Sicherheitseinstellungen()
    {
        var data = GpresultParser.Parse(Sample(withAdmxPolicy: true), "gpresult.xml", 1 << 20);
        Assert.Equal([("Local Group Policy", "LocalGPO"), ("Linie-3-Basis", DomainGpoId)], data.Gpos.Select(g => (g.Name, g.Guid)));
        // Nur-Schlüssel-Eintrag und Rang 2 fallen weg
        Assert.Equal(
            [(@"HKLM\Software\Policies\Beispiel", "LokalerWert", "Local Group Policy"),
             (@"HKLM\Software\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast", "Linie-3-Basis"),
             (@"HKLM\Software\Policies\Beispiel\Autologon", "DefaultPassword", "Linie-3-Basis")],
            data.Registry.Select(r => (r.Key, r.Value, r.Gpo)));
        Assert.Equal(["MinimumPasswordLength", @"MACHINE\System\CurrentControlSet\Control\Lsa\NoLMHash"], data.Security.Select(s => s.Name));
        Assert.Equal(["Turn off multicast name resolution"], data.Policies.Select(p => p.Name));
        Assert.True(data.IsLocal("Local Group Policy"));
        Assert.False(data.IsLocal("Linie-3-Basis"));
        var item = GpresultParser.ToParsedItem(data);
        Assert.Equal(["gpos", "registry", "security", "policies"], item.EnumerateObject().Select(p => p.Name));
        var back = GpresultParser.FromParsedItem(item);
        Assert.Equal(data.Registry, back.Registry);
        Assert.Equal(data.Policies, back.Policies);
    }

    [Fact]
    public void Gefilterte_oder_verweigerte_GPOs_zaehlen_nicht()
    {
        var filtered = Gpresult([("Local Group Policy", "LocalGPO"), ("Gefiltert", FilteredGpoId)])
            .Replace("<FilterAllowed>true</FilterAllowed>\n      <AccessDenied>false</AccessDenied>\n    </GPO>\n  </ComputerResults>",
                "<FilterAllowed>false</FilterAllowed>\n      <AccessDenied>false</AccessDenied>\n    </GPO>\n  </ComputerResults>", StringComparison.Ordinal);
        var data = GpresultParser.Parse(filtered, "gpresult.xml", 1 << 20);
        Assert.Equal(["Local Group Policy"], data.Gpos.Select(g => g.Name));
    }

    [Fact]
    public void Daten_geheimer_Werte_werden_im_XML_geschwaerzt()
    {
        var clean = GpresultParser.Redact(Sample(), "gpresult.xml", 1 << 20);
        Assert.NotNull(clean);
        Assert.DoesNotContain("geheim", clean, StringComparison.Ordinal);
        Assert.Contains("DefaultPassword", clean, StringComparison.Ordinal);
        // Danach ist nichts mehr zu tun, und das Ergebnis bleibt lesbar
        Assert.Null(GpresultParser.Redact(clean, "gpresult.xml", 1 << 20));
        Assert.Equal(3, GpresultParser.Parse(clean, "gpresult.xml", 1 << 20).Registry.Count);
    }

    [Theory]
    [InlineData("<Rsop><ComputerResults>")]
    [InlineData("<!DOCTYPE r [<!ENTITY x \"y\">]><Rsop><ComputerResults/></Rsop>")]
    [InlineData("<Rsop><Andere/></Rsop>")]
    [InlineData("kein xml")]
    public void Kaputtes_oder_feindliches_XML_ergibt_RGL_0272(string xml)
    {
        Assert.Equal(RglCodesParsing.GpresultInvalid, Assert.Throws<RiegelException>(() => GpresultParser.Parse(xml, "gpresult.xml", 1 << 20)).Code);
    }

    [Fact]
    public void Tiefe_und_Groesse_sind_begrenzt()
    {
        var deep = "<Rsop>" + string.Concat(Enumerable.Repeat("<a>", 100)) + string.Concat(Enumerable.Repeat("</a>", 100)) + "</Rsop>";
        Assert.Equal(RglCodesParsing.GpresultInvalid, Assert.Throws<RiegelException>(() => GpresultParser.Parse(deep, "x", 1 << 20)).Code);
        Assert.Equal(RglCodesParsing.GpresultInvalid, Assert.Throws<RiegelException>(() => GpresultParser.Parse(Sample(), "x", 100)).Code);
    }

    // ---------------------------------------------------------------- Firewall

    [Fact]
    public void Firewall_Regel_mit_allen_ausgewerteten_Feldern()
    {
        var rule = FirewallRuleParser.Parse("local", "Beispiel-In",
            "v2.31|Action=Allow|Active=TRUE|Dir=In|Protocol=6|Profile=Private|Profile=Public|LPort=4840|LPort=5000-5010|RA4=LocalSubnet|RA6=fe80::/64|App=%ProgramFiles%\\Beispiel\\server.exe|Svc=*|Name=Beispiel Leitsystem|Desc=frei|EmbedCtxt=Gruppe|");
        Assert.False(rule.Invalid);
        Assert.Equal(("allow", true, "in", 6), (rule.Action, rule.Active, rule.Direction, rule.Protocol!.Value));
        Assert.Equal(["4840", "5000-5010"], rule.LocalPorts);
        Assert.Equal(["LocalSubnet", "fe80::/64"], rule.RemoteAddresses);
        Assert.Equal(["private", "public"], rule.Profiles);
        Assert.Equal(("Beispiel Leitsystem", "*"), (rule.Name, rule.Service));
    }

    [Fact]
    public void Standardwerte_laut_Grammatik()
    {
        // Ohne Profile= alle Profile, ohne Active= false, ohne Protocol= jedes Protokoll; Groß/klein egal (ABNF)
        var rule = FirewallRuleParser.Parse("gpo", "R1", "v2.10|action=block|dir=out|NeuesFeld2_99=irgendwas|");
        Assert.False(rule.Invalid);
        Assert.Equal(["domain", "private", "public"], rule.Profiles);
        Assert.False(rule.Active);
        Assert.Null(rule.Protocol);
        Assert.Equal("block", rule.Action);
        Assert.Empty(rule.RemoteAddresses);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2.31|Action=Allow|")]
    [InlineData("v2|Action=Allow|")]
    [InlineData("v2.31|Action=Allow")]
    [InlineData("v2.31|Action=Erlauben|")]
    [InlineData("v2.31|Action=Allow|Action=Block|")]
    [InlineData("v2.31|Dir=Hinein|")]
    [InlineData("v2.31|Protocol=256|")]
    [InlineData("v2.31|Protocol=6|LPort=70000|")]
    [InlineData("v2.31|Protocol=6|LPort=abc|")]
    [InlineData("v2.31|Profile=Firma|")]
    [InlineData("v2.31|Active=ja|")]
    [InlineData("v2.31|ohneGleich|")]
    [InlineData("v999.1|Action=Allow|")]
    public void Ungueltige_Regeln_sind_unbekannt_nie_abwesend(string text)
    {
        var rule = FirewallRuleParser.Parse("local", "X", text);
        Assert.True(rule.Invalid);
        var json = FirewallRuleParser.ToParsedItem([rule]).EnumerateArray().Single();
        Assert.True(json.GetProperty("invalid").GetBoolean());
        Assert.Equal("X", json.GetProperty("id").GetString());
    }

    [Fact]
    public void Ausgabe_hat_die_Felder_aus_spec_7_3()
    {
        var json = FirewallRuleParser.ToParsedItem([FirewallRuleParser.Parse("local", "R", "v2.31|Action=Allow|Dir=In|")]).EnumerateArray().Single();
        Assert.Equal(["store", "id", "name", "action", "active", "dir", "protocol", "lports", "ra", "profiles", "app", "svc"],
            json.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("protocol").ValueKind);
    }
}
