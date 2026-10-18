// SPDX-License-Identifier: MIT
using System.Text;
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Parsing;
using Riegel.Core.Services;
using Riegel.Core.Snapshots;
using Riegel.Tests.Parsing;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Parsing.ParsingSamples;

namespace Riegel.Tests.Snapshots;

/// <summary>Bordmittel-Pakete (spec 7.5): die Fixtures W7 und XP, Abdeckung genau für das Exportierte, Grenzen, Fehler.</summary>
public sealed class BuiltinBundleTests
{
    private const string Services = @"HKLM\SYSTEM\CurrentControlSet\Services";

    private static ISnapshotReader Reader() => RiegelServices.Create(new NullPlatform(), new FixedClock()).Snapshots;

    private static string Fixture(string name) => Path.Combine(RepoPaths.SyntheticFixtures, name);

    private static SnapshotItem Item(Snapshot s, string key) => s.Items.Single(i => ItemKeys.Same(i.Key, key));

    [Fact]
    public void Windows_7_Paket()
    {
        var s = Reader().FromBuiltinBundle(Fixture("PG-ALT-01_20261010.riegel-builtin"), ReadLimits.Default);
        Assert.Equal((SnapshotOrigin.Builtin, "PG-ALT-01", 7601, (int?)null, 1), (s.Origin, s.Machine.HostName, s.Machine.Build!.Value, s.Machine.Ubr, s.Machine.ProductType!.Value));
        // Bordmittel kennen keine DomainRole: null, nicht geraten
        Assert.Null(s.Machine.DomainRole);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero), s.CollectedAtUtc);
        Assert.True(s.IsAdmin);
        LocalSchema.AssertValid(RiegelJson.Serialize(s), "PG-ALT-01");

        // Abdeckung genau für die exportierten Schlüssel
        var roots = s.Coverage.Select(c => c.Root).ToList();
        Assert.Contains(@"HKLM\SOFTWARE\Policies", roots);
        Assert.Contains(@"HKLM\SYSTEM\CurrentControlSet\Control\Lsa", roots);
        Assert.Contains(@"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile", roots);
        Assert.DoesNotContain(@"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy", roots);
        Assert.DoesNotContain(@"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard", roots);
        Assert.Contains(s.Coverage, c => c.Root == Services && c.Recursive && c.MaxDepth == 1);

        var evidence = new RegistryEvidence(s);
        Assert.Equal(KeyEvidence.Present, evidence.Classify(@"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", out _));
        // Im exportierten Teilbaum: nachweislich nicht vorhanden
        Assert.Equal(KeyEvidence.NotSet, evidence.Classify(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\PowerShell", out _));
        // Außerhalb: nicht erfasst
        Assert.Equal(KeyEvidence.Unavailable, evidence.Classify(@"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard", out var dg));
        Assert.Equal(ItemStatus.NotCollected, dg!.Status);
        Assert.Equal(KeyEvidence.Unavailable, evidence.Classify(@"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\PublicProfile", out _));
        // Zwischenschlüssel, den die Datei auslässt, gibt es – seine Werte kennt das Paket nicht
        var windowsNt = Item(s, @"reg:HKLM\SOFTWARE\Policies\Microsoft\Windows NT").Value!.Value;
        Assert.Equal(0, windowsNt.GetProperty("valueFilter").GetArrayLength());
        Assert.Equal(["DNSClient"], windowsNt.GetProperty("subkeys").EnumerateArray().Select(e => e.GetString()));

        // Dienste: nur Start; ein nicht genannter Dienst ist nicht vorhanden
        var spooler = Item(s, @"reg:" + Services + @"\Spooler").Value!.Value;
        Assert.Equal(2, spooler.GetProperty("values").GetProperty("Start").GetProperty("v").GetInt32());
        Assert.Equal(["Start"], spooler.GetProperty("valueFilter").EnumerateArray().Select(e => e.GetString()));
        Assert.False(spooler.TryGetProperty("subkeys", out _));
        Assert.Equal(KeyEvidence.NotSet, evidence.Classify(Services + @"\TlntSvr", out _));
        // mrxsmb10 kommt vollständig aus der .reg-Datei
        Assert.False(Item(s, @"reg:" + Services + @"\mrxsmb10").Value!.Value.TryGetProperty("valueFilter", out _));
        // LanmanWorkstation steht nur über seinen exportierten Unterschlüssel im Paket: vorhanden, Werte nicht erfasst
        Assert.Equal(KeyEvidence.Present, evidence.Classify(Services + @"\LanmanWorkstation", out _));

        // Winlogon: nur die gefilterten Werte, kein Kennwort
        var winlogon = Item(s, @"reg:HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon").Value!.Value;
        Assert.Equal("0", winlogon.GetProperty("values").GetProperty("AutoAdminLogon").GetProperty("v").GetString());
        Assert.False(winlogon.GetProperty("values").TryGetProperty("DefaultPassword", out _));
        Assert.Contains("DefaultPassword", winlogon.GetProperty("valueFilter").EnumerateArray().Select(e => e.GetString()));

        // Sicherheitsvorlage, Überwachung, parsed:*
        Assert.Equal(ItemStatus.Ok, Item(s, ItemKeys.RawSeceditInf).Status);
        var secedit = Item(s, ItemKeys.ParsedSecedit);
        Assert.Equal("42", secedit.Value!.Value.GetProperty("sections").GetProperty("System Access").GetProperty("MaximumPasswordAge").GetString());
        var audit = Item(s, ItemKeys.AuditPolicy).Value!.Value;
        Assert.Equal(3, audit.GetProperty("{0CCE9215-69AE-11D9-BED3-505054503030}").GetInt32());
        Assert.Equal(7, audit.EnumerateObject().Count());

        // Alles andere aus dem Plan: nicht erfasst, mit Grund
        foreach (var key in new[] { ItemKeys.Firewall, ItemKeys.NetUserModals, ItemKeys.LsaRights, ItemKeys.RawRegistryPol, ItemKeys.RawGpresultXml,
                     @"wmi:root\cimv2:Win32_Service", @"wmi:root\Microsoft\Windows\Defender:MSFT_MpComputerStatus" })
        {
            var item = Item(s, key);
            Assert.Equal(ItemStatus.NotCollected, item.Status);
            Assert.StartsWith(RglCodesParsing.BundlePartMissing, item.Error, StringComparison.Ordinal);
        }
        Assert.Equal(ItemStatus.NotCollected, Item(s, ItemKeys.ParsedRegistryPol).Status);
        Assert.Equal(ItemStatus.NotCollected, Item(s, ItemKeys.ParsedFirewallRulesRegistry).Status);
    }

    [Fact]
    public void Windows_XP_Paket()
    {
        var s = Reader().FromBuiltinBundle(Fixture("XP-HMI-01_20261010.riegel-builtin"), ReadLimits.Default);
        Assert.Equal(("XP-HMI-01", 2600), (s.Machine.HostName, s.Machine.Build!.Value));
        LocalSchema.AssertValid(RiegelJson.Serialize(s), "XP-HMI-01");
        // XP hat kein auditpol /backup
        var audit = Item(s, ItemKeys.AuditPolicy);
        Assert.Equal(ItemStatus.NotCollected, audit.Status);
        Assert.Contains("auditpol.csv", audit.Error, StringComparison.Ordinal);
        // Tab-getrennte reg-query-Ausgabe (Form unverified) wird gelesen
        Assert.Equal(2, Item(s, @"reg:" + Services + @"\RemoteRegistry").Value!.Value.GetProperty("values").GetProperty("Start").GetProperty("v").GetInt32());
        // Automatische Anmeldung mit Kennwort: vorhanden, aber nur geschwärzt
        var values = Item(s, @"reg:HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon").Value!.Value.GetProperty("values");
        Assert.Equal("1", values.GetProperty("AutoAdminLogon").GetProperty("v").GetString());
        Assert.True(values.GetProperty("DefaultPassword").GetProperty("redacted").GetBoolean());
        Assert.False(values.GetProperty("DefaultPassword").TryGetProperty("v", out _));
    }

    [Fact]
    public void Gleiche_Eingabe_gleiches_Ergebnis()
    {
        var a = RiegelJson.Serialize(Reader().FromBuiltinBundle(Fixture("PG-ALT-01_20261010.riegel-builtin"), ReadLimits.Default));
        var b = RiegelJson.Serialize(Reader().FromBuiltinBundle(Fixture("PG-ALT-01_20261010.riegel-builtin"), ReadLimits.Default));
        Assert.Equal(a, b);
    }

    // ---------------------------------------------------------------- Fehler und feindliche Dateien

    private static TempFolder CopyBundle(out string folder)
    {
        var temp = new TempFolder();
        folder = Path.Combine(temp.Path, "HOST-T_20261012.riegel-builtin");
        CopyDirectory(Fixture("PG-ALT-01_20261010.riegel-builtin"), folder);
        return temp;
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(from)) CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    [Fact]
    public void Fehlgeschlagene_Schritte_werden_nicht_verwendet()
    {
        using var temp = CopyBundle(out var folder);
        File.WriteAllText(Path.Combine(folder, "exitcodes.txt"), "secedit=740\r\nauditpol=1\r\nreg-03=1\r\nservices=0\r\n");
        var s = Reader().FromBuiltinBundle(folder, ReadLimits.Default);
        var secedit = Item(s, ItemKeys.RawSeceditInf);
        Assert.Equal((ItemStatus.Error, 740), (secedit.Status, secedit.ErrorCode!.Value));
        Assert.StartsWith(RglCodesParsing.BundleStepFailed, secedit.Error, StringComparison.Ordinal);
        Assert.Equal(ItemStatus.Error, Item(s, ItemKeys.ParsedSecedit).Status);
        Assert.Equal(ItemStatus.Error, Item(s, ItemKeys.AuditPolicy).Status);
        Assert.False(s.IsAdmin);
        Assert.DoesNotContain(s.Coverage, c => c.Root.EndsWith(@"\Lsa", StringComparison.Ordinal));
        Assert.Equal(ItemStatus.NotCollected, Item(s, @"reg:HKLM\SYSTEM\CurrentControlSet\Control\Lsa").Status);
        // hostname.txt hat Vorrang vor dem Ordnernamen; das Datum kommt aus dem Ordnernamen
        Assert.Equal("PG-ALT-01", s.Machine.HostName);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero), s.CollectedAtUtc);
    }

    [Fact]
    public void Ohne_hostname_txt_kommt_der_Name_aus_dem_Ordner()
    {
        using var temp = CopyBundle(out var folder);
        File.Delete(Path.Combine(folder, "hostname.txt"));
        Assert.Equal("HOST-T", Reader().FromBuiltinBundle(folder, ReadLimits.Default).Machine.HostName);
    }

    [Theory]
    [InlineData("secedit.inf", RglCodesParsing.InvalidEncoding)]
    [InlineData("reg/03-lsa.reg", RglCodesParsing.InvalidEncoding)]
    [InlineData("auditpol.csv", RglCodesParsing.AuditCsvInvalid)]
    [InlineData("services-start.txt", RglCodesParsing.RegQueryInvalid)]
    public void Kaputte_Dateien_ergeben_RGL_Codes(string file, string code)
    {
        using var temp = CopyBundle(out var folder);
        var path = Path.Combine(folder, file.Replace('/', Path.DirectorySeparatorChar));
        byte[] hostile = file switch
        {
            "secedit.inf" or "reg/03-lsa.reg" => [0xFF, 0xFE, 0x5B, 0x00, 0x00, 0xD8, 0x41, 0x00],
            "auditpol.csv" => Crlf("Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value\nX,System,Logon,{0CCE9215-69AE-11D9-BED3-505054503030},Success,,99\n"),
            _ => Crlf("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\X\n    kaputt\n"),
        };
        File.WriteAllBytes(path, hostile);
        var e = Assert.Throws<RiegelException>(() => Reader().FromBuiltinBundle(folder, ReadLimits.Default));
        Assert.True(e.Code == code, e.ToString());
    }

    [Fact]
    public void Kaputte_reg_Datei_ergibt_RGL_0263()
    {
        using var temp = CopyBundle(out var folder);
        File.WriteAllBytes(Path.Combine(folder, "reg", "05-lanmanserver.reg"),
            Utf16Bom("Windows Registry Editor Version 5.00\n\n[HKEY_LOCAL_MACHINE\\SYSTEM\\A]\n\"B\"=dword:zz\n"));
        Assert.Equal(RglCodesParsing.RegFileInvalid, Assert.Throws<RiegelException>(() => Reader().FromBuiltinBundle(folder, ReadLimits.Default)).Code);
    }

    [Fact]
    public void Jede_Datei_hat_eine_Groessengrenze()
    {
        using var temp = CopyBundle(out var folder);
        File.WriteAllBytes(Path.Combine(folder, "services-start.txt"), new byte[3000]);
        var e = Assert.Throws<RiegelException>(() => Reader().FromBuiltinBundle(folder, ReadLimits.Default with { MaxTextFileBytes = 2048 }));
        Assert.Equal(RglCodesParsing.BundleFileTooLarge, e.Code);
    }

    [Fact]
    public void Geheimnisse_aus_reg_Exporten_werden_geschwaerzt()
    {
        using var temp = CopyBundle(out var folder);
        File.WriteAllBytes(Path.Combine(folder, "reg", "10-currentversion.reg"), Utf16Bom("""
            Windows Registry Editor Version 5.00

            [HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion]
            "CurrentBuild"="7601"

            [HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon]
            "AutoAdminLogon"="1"
            "DefaultPassword"="geheim-im-export"
            """));
        var s = Reader().FromBuiltinBundle(folder, ReadLimits.Default);
        Assert.DoesNotContain("geheim-im-export", RiegelJson.Serialize(s), StringComparison.Ordinal);
        // Der vollständige Export hat Vorrang vor winlogon.txt
        var winlogon = Item(s, @"reg:HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon").Value!.Value;
        Assert.False(winlogon.TryGetProperty("valueFilter", out _));
        Assert.True(winlogon.GetProperty("values").GetProperty("DefaultPassword").GetProperty("redacted").GetBoolean());
    }

    [Fact]
    public void Kein_Ordner_oder_kein_Hostname()
    {
        Assert.Equal(RglCodesCommon.NotFound, Assert.Throws<RiegelException>(() => Reader().FromBuiltinBundle(Path.Combine(Path.GetTempPath(), "fehlt-" + Guid.NewGuid()), ReadLimits.Default)).Code);
        using var temp = new TempFolder();
        var empty = temp.Sub("leer");
        Assert.Equal(RglCodesParsing.BundleInvalid, Assert.Throws<RiegelException>(() => Reader().FromBuiltinBundle(empty, ReadLimits.Default)).Code);
        File.WriteAllText(Path.Combine(empty, "hostname.txt"), "HOST\u0001\r\n", Encoding.ASCII);
        Assert.Equal(RglCodesParsing.BundleInvalid, Assert.Throws<RiegelException>(() => Reader().FromBuiltinBundle(empty, ReadLimits.Default)).Code);
    }

    [Fact]
    public void Ein_leeres_Paket_hat_keine_Abdeckung_und_alles_ist_nicht_erfasst()
    {
        using var temp = new TempFolder();
        var folder = temp.Sub("NUR-NAME_20261001.riegel-builtin");
        File.WriteAllText(Path.Combine(folder, "hostname.txt"), "NUR-NAME\r\n");
        var s = Reader().FromBuiltinBundle(folder, ReadLimits.Default);
        Assert.Empty(s.Coverage);
        Assert.False(s.IsAdmin);
        Assert.All(s.Items, i => Assert.Equal(ItemStatus.NotCollected, i.Status));
        Assert.All(s.Items.Where(i => i.Key.StartsWith("reg:", StringComparison.Ordinal)), i => Assert.Equal(JsonValueKind.Undefined, i.Value?.ValueKind ?? JsonValueKind.Undefined));
    }
}
