// SPDX-License-Identifier: MIT
using System.IO.Compression;
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

/// <summary>
/// Export-Leser (spec 7.3, 7.12, 8.8): Fixtures, gzip, Begleitdatei, Grenzen, feindliche Eingaben, Schwärzen beim
/// Import und das Verwerfen und Neuberechnen von parsed:*.
/// </summary>
public sealed class SnapshotReaderTests
{
    private static ISnapshotReader Reader() => RiegelServices.Create(new NullPlatform(), new FixedClock()).Snapshots;

    public static TheoryData<string> Fixtures => new()
    {
        "win11-24h2-workgroup-admin", "win11-24h2-workgroup-nonadmin", "server2022-domain-member", "win10-ltsc2019-hmi",
        "server2016-historian", "win7-sp1", "server2012r2", "win10-german-locale", "server2022-dc", "win11-defender-passive-thirdparty-av",
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Synthetische_Fixtures_werden_gelesen_mit_Abdeckung_aus_dem_Kopf(string name)
    {
        var path = RepoPaths.Synthetic(name);
        var original = RiegelJson.Deserialize<Snapshot>(File.ReadAllBytes(path));
        var snapshot = Reader().Read(path, ReadLimits.Default);
        Assert.Equal(SnapshotOrigin.Synthetic, snapshot.Origin);
        Assert.Equal(original.Coverage.Select(c => RiegelJson.Serialize(c)), snapshot.Coverage.Select(c => RiegelJson.Serialize(c)));
        Assert.Equal(original.Machine, snapshot.Machine);
        // Mitgelieferte parsed:* bleiben bei synthetic erhalten, fehlende werden aus Rohdaten ergänzt
        foreach (var item in original.Items.Where(i => i.Key.StartsWith("parsed:", StringComparison.Ordinal)))
            Assert.Equal(item.Value!.Value.GetRawText(), snapshot.Items.Single(i => i.Key == item.Key).Value!.Value.GetRawText());
        var pol = snapshot.Items.Single(i => i.Key == ItemKeys.ParsedRegistryPol);
        Assert.Equal(ItemStatus.Ok, pol.Status);
        Assert.Equal("[]", pol.Value!.Value.GetRawText());
        Assert.Equal(snapshot.Items.Count, snapshot.Items.Select(i => i.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        LocalSchema.AssertValid(RiegelJson.Serialize(snapshot), "snapshot");
    }

    [Fact]
    public void Firewall_Regeln_der_Fixture_werden_aus_der_Registry_berechnet()
    {
        var snapshot = Reader().Read(RepoPaths.Synthetic("win11-24h2-workgroup-admin"), ReadLimits.Default);
        var rules = snapshot.Items.Single(i => i.Key == ItemKeys.ParsedFirewallRulesRegistry);
        Assert.Equal(ItemStatus.Ok, rules.Status);
        var list = rules.Value!.Value.EnumerateArray().ToList();
        Assert.NotEmpty(list);
        Assert.All(list, r => Assert.Equal("local", r.GetProperty("store").GetString()));
        var rdp = list.Single(r => r.GetProperty("id").GetString() == "RemoteDesktop-UserMode-In-TCP");
        Assert.False(rdp.GetProperty("active").GetBoolean());
        Assert.Equal(["3389"], rdp.GetProperty("lports").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public void Gzip_und_Begleitdatei()
    {
        using var temp = new TempFolder();
        var snapshot = Collected().RegistryValue(@"HKLM\SOFTWARE\Policies\A", "B", RegValueType.Dword, 1).Build();
        var plain = SnapshotFile.Write(snapshot, temp.Sub("plain"));
        var gz = SnapshotFile.Write(snapshot, temp.Sub("gz"), gzip: true);
        var reader = Reader();
        var a = reader.Read(plain, ReadLimits.Default);
        var b = reader.Read(gz, ReadLimits.Default);
        Assert.Equal(RiegelJson.Serialize(a), RiegelJson.Serialize(b));
        Assert.Equal(HashCheckStatus.Ok, reader.VerifySidecar(plain).Status);
        Assert.Equal(HashCheckStatus.Ok, reader.VerifySidecar(gz).Status);
        File.AppendAllText(plain, " ");
        Assert.Equal(HashCheckStatus.Mismatch, reader.VerifySidecar(plain).Status);
        File.WriteAllText(SnapshotFile.SidecarPath(gz), "kaputt");
        Assert.Equal(HashCheckStatus.Malformed, reader.VerifySidecar(gz).Status);
        File.WriteAllBytes(SnapshotFile.SidecarPath(gz), new byte[10_000]);
        Assert.Equal(HashCheckStatus.Malformed, reader.VerifySidecar(gz).Status);
        File.Delete(SnapshotFile.SidecarPath(gz));
        Assert.Equal(HashCheckStatus.Missing, reader.VerifySidecar(gz).Status);
        Assert.Equal(RglCodesCommon.NotFound, Assert.Throws<RiegelException>(() => reader.VerifySidecar(Path.Combine(temp.Path, "fehlt.json"))).Code);
    }

    // ---------------------------------------------------------------- feindliche Eingaben

    [Fact]
    public void Abgeschnittenes_JSON_ergibt_RGL_0250()
    {
        using var temp = new TempFolder();
        var json = SnapshotBuilder.Windows11Workgroup().ToJson();
        var path = Write(temp, "abgeschnitten.snapshot.json", json[..(json.Length / 2)]);
        Assert.Equal(RglCodesParsing.SnapshotNotJson, Assert.Throws<RiegelException>(() => Reader().Read(path, ReadLimits.Default)).Code);
        var empty = Write(temp, "leer.snapshot.json", "");
        Assert.Equal(RglCodesParsing.SnapshotNotJson, Assert.Throws<RiegelException>(() => Reader().Read(empty, ReadLimits.Default)).Code);
    }

    [Fact]
    public void Zu_tiefes_JSON_ergibt_RGL_0255()
    {
        using var temp = new TempFolder();
        var path = Write(temp, "tief.snapshot.json", new string('[', 200) + new string(']', 200));
        Assert.Equal(RglCodesParsing.JsonTooDeep, Assert.Throws<RiegelException>(() => Reader().Read(path, ReadLimits.Default)).Code);
        // Auch innerhalb eines sonst gültigen Exports
        var deepValue = "{\"a\":" + string.Concat(Enumerable.Repeat("[", 70)) + string.Concat(Enumerable.Repeat("]", 70)) + "}";
        var json = SnapshotBuilder.Windows11Workgroup().ToJson().Replace("\"items\": [", "\"items\": [{\"key\":\"api:x\",\"status\":\"ok\",\"value\":" + deepValue + ",\"durationMs\":0},", StringComparison.Ordinal);
        var nested = Write(temp, "tief2.snapshot.json", json);
        Assert.Equal(RglCodesParsing.JsonTooDeep, Assert.Throws<RiegelException>(() => Reader().Read(nested, ReadLimits.Default)).Code);
    }

    [Fact]
    public void Zu_lange_Zeichenketten_ergeben_RGL_0254()
    {
        using var temp = new TempFolder();
        var path = SnapshotFile.Write(Collected().RegistryValue(@"HKLM\SOFTWARE\Policies\A", "Lang", RegValueType.Sz, new string('x', 5000)).Build(), temp.Path);
        Assert.Equal(RglCodesParsing.StringTooLong,
            Assert.Throws<RiegelException>(() => Reader().Read(path, ReadLimits.Default with { MaxStringChars = 1000 })).Code);
        // Rohtexte (raw:*) dürfen so groß sein wie eine Textdatei (gpresult bis 4 MB laut Plan)
        var raw = SnapshotFile.Write(Collected("RAW").Api(ItemKeys.RawGpresultXml, new { text = GpresultAndFirewallTests.Sample() + new string(' ', 3000) }).Build(), temp.Sub("raw"));
        var read = Reader().Read(raw, ReadLimits.Default with { MaxStringChars = 1000 });
        Assert.Equal(ItemStatus.Ok, read.Items.Single(i => i.Key == ItemKeys.ParsedGpresult).Status);
    }

    [Fact]
    public void Gzip_Bombe_wird_beim_Entpacken_gestoppt()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "bombe.snapshot.json.gz");
        using (var file = File.Create(path))
        using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
        {
            var zeros = new byte[1 << 20];
            for (int i = 0; i < 80; i++) gzip.Write(zeros);
        }
        Assert.True(new FileInfo(path).Length < 1 << 20, "Die Bombe ist klein gepackt.");
        Assert.Equal(RglCodesParsing.SnapshotTooLarge, Assert.Throws<RiegelException>(() => Reader().Read(path, ReadLimits.Default)).Code);
        Assert.Equal(RglCodesParsing.SnapshotTooLarge,
            Assert.Throws<RiegelException>(() => Reader().Read(path, ReadLimits.Default with { MaxSnapshotBytes = 10 })).Code);
        var broken = Write(temp, "kaputt.snapshot.json.gz", "\u001f\u008bnicht gzip");
        File.WriteAllBytes(broken, [0x1F, 0x8B, 0x08, 0x00, 0x01, 0x02]);
        Assert.Equal(RglCodesParsing.GzipInvalid, Assert.Throws<RiegelException>(() => Reader().Read(broken, ReadLimits.Default)).Code);
        // Abgeschnittene gzip-Datei: der Inhalt ist kein vollständiges JSON mehr
        var valid = SnapshotFile.Write(SnapshotBuilder.Windows11Workgroup().Build(), temp.Sub("gut"), gzip: true);
        var cut = Path.Combine(temp.Path, "halb.snapshot.json.gz");
        File.WriteAllBytes(cut, File.ReadAllBytes(valid)[..400]);
        Assert.Contains(Assert.Throws<RiegelException>(() => Reader().Read(cut, ReadLimits.Default)).Code, new[] { RglCodesParsing.GzipInvalid, RglCodesParsing.SnapshotNotJson });
        var notGzip = Write(temp, "falsch.snapshot.json.gz", "{}");
        Assert.Equal(RglCodesParsing.GzipInvalid, Assert.Throws<RiegelException>(() => Reader().Read(notGzip, ReadLimits.Default)).Code);
    }

    [Fact]
    public void Zu_viele_Eintraege_ergeben_RGL_0253()
    {
        using var temp = new TempFolder();
        var builder = Collected();
        for (int i = 0; i < 30; i++) builder.Api("api:x" + i, new { n = i });
        var path = SnapshotFile.Write(builder.Build(), temp.Path);
        Assert.Equal(RglCodesParsing.TooManyItems, Assert.Throws<RiegelException>(() => Reader().Read(path, ReadLimits.Default with { MaxItems = 10 })).Code);
    }

    [Theory]
    [InlineData("\"schema\": \"riegel.snapshot/1\"", "\"schema\": \"riegel.snapshot/2\"", RglCodesParsing.SnapshotVersion)]
    [InlineData("\"schema\": \"riegel.snapshot/1\"", "\"schema\": \"riegel.run/1\"", RglCodesCommon.UnsupportedSchema)]
    [InlineData("\"origin\": \"collector\"", "\"origin\": \"irgendwo\"", RglCodesCommon.InvalidJson)]
    [InlineData("\"key\": \"api:netusermodals\"", "\"key\": \"foo:bar\"", RglCodesParsing.SnapshotInvalid)]
    [InlineData("\"key\": \"api:netusermodals\"", "\"key\": \"API:LSA-RIGHTS\"", RglCodesParsing.SnapshotInvalid)]
    [InlineData("\"key\": \"reg:HKLM\\\\SOFTWARE\\\\Policies\\\\A\"", "\"key\": \"reg:HKCU\\\\Software\\\\A\"", RglCodesParsing.SnapshotInvalid)]
    [InlineData("\"t\": \"dword\"", "\"t\": \"word\"", RglCodesParsing.SnapshotInvalid)]
    [InlineData("\"hostName\": \"W11-PARSE\"", "\"hostName\": \"\"", RglCodesParsing.SnapshotInvalid)]
    [InlineData("\"root\": \"HKLM\\\\SOFTWARE\\\\Policies\"", "\"root\": \"HKCU\\\\SOFTWARE\\\\Policies\"", RglCodesParsing.SnapshotInvalid)]
    // Prüfung vom 2026-10-20: fehlende Pflichtfelder, leere Einträge und einzelne Ersatzzeichen ergaben RGL-0001
    [InlineData("\"schema\": \"riegel.snapshot/1\",", "", RglCodesParsing.SnapshotInvalid)]
    [InlineData("\"hostName\": \"W11-PARSE\"", "\"hostName\": \"W11-\\ud800PARSE\"", RglCodesCommon.InvalidJson)]
    [InlineData("\"minPwLen\": 12", "\"minPwLen\": \"\\udc00\"", RglCodesParsing.SnapshotInvalid)]
    [InlineData("\"items\": [", "\"items\": [ null,", RglCodesParsing.SnapshotInvalid)]
    public void Ungueltige_Exporte_ergeben_RGL_Codes(string find, string replace, string code)
    {
        using var temp = new TempFolder();
        var json = Collected()
            .RegistryValue(@"HKLM\SOFTWARE\Policies\A", "B", RegValueType.Dword, 1)
            .Api(ItemKeys.NetUserModals, new { minPwLen = 12 })
            .Api(ItemKeys.LsaRights, new { SeDebugPrivilege = new[] { "S-1-5-32-544" } })
            .ToJson();
        Assert.Contains(find, json, StringComparison.Ordinal);
        var path = Write(temp, "x.snapshot.json", json.Replace(find, replace, StringComparison.Ordinal));
        var e = Assert.Throws<RiegelException>(() => Reader().Read(path, ReadLimits.Default));
        Assert.True(e.Code == code, e.ToString());
    }

    [Fact]
    public void Fehlende_Datei_ergibt_RGL_0002()
    {
        Assert.Equal(RglCodesCommon.NotFound, Assert.Throws<RiegelException>(() => Reader().Read(Path.Combine(Path.GetTempPath(), "gibt-es-nicht.json"), ReadLimits.Default)).Code);
    }

    // ---------------------------------------------------------------- parsed:* und Schwärzen

    private const string SeceditText = "[Unicode]\r\nUnicode=yes\r\n[System Access]\r\nMinimumPasswordLength = 0\r\n[Privilege Rights]\r\nSeDebugPrivilege = *S-1-5-32-544\r\n";

    [Fact]
    public void Eingeschmuggelte_parsed_Eintraege_werden_verworfen_und_neu_berechnet()
    {
        using var temp = new TempFolder();
        // Synthetisch bauen (nur so lässt der Builder parsed:* zu), dann als Sammler-Export ausgeben
        var smuggled = SnapshotBuilder.Windows11Workgroup("SCHMUGGEL")
            .Api(ItemKeys.RawSeceditInf, new { text = SeceditText })
            .Parsed(ItemKeys.ParsedSecedit, new { sections = new Dictionary<string, object> { ["System Access"] = new { MinimumPasswordLength = "14" } } })
            .Parsed(ItemKeys.ParsedGpresult, new { gpos = Array.Empty<object>(), registry = Array.Empty<object>(), security = Array.Empty<object>() })
            .Parsed("parsed:erfunden", new { x = 1 })
            .Build() with { Origin = SnapshotOrigin.Collector };
        var path = SnapshotFile.Write(smuggled, temp.Path);
        var snapshot = Reader().Read(path, ReadLimits.Default);
        var secedit = snapshot.Items.Single(i => i.Key == ItemKeys.ParsedSecedit);
        Assert.Equal(ItemStatus.Ok, secedit.Status);
        Assert.Equal("0", secedit.Value!.Value.GetProperty("sections").GetProperty("System Access").GetProperty("MinimumPasswordLength").GetString());
        // Ohne Rohdaten kein gpresult, sondern „nicht erfasst“ mit Grund
        var gp = snapshot.Items.Single(i => i.Key == ItemKeys.ParsedGpresult);
        Assert.Equal(ItemStatus.NotCollected, gp.Status);
        Assert.StartsWith(RglCodesParsing.RawSourceUnavailable, gp.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(snapshot.Items, i => i.Key == "parsed:erfunden");
    }

    [Fact]
    public void Status_der_Rohdaten_geht_auf_den_abgeleiteten_Eintrag_ueber()
    {
        using var temp = new TempFolder();
        var path = SnapshotFile.Write(Collected().NonAdmin().Denied(ItemKeys.RawSeceditInf, 740).Item(ItemKeys.RawGpresultXml, ItemStatus.NotCollected, null, null, "minimal").Build(), temp.Path);
        var snapshot = Reader().Read(path, ReadLimits.Default);
        var secedit = snapshot.Items.Single(i => i.Key == ItemKeys.ParsedSecedit);
        Assert.Equal((ItemStatus.Denied, 740), (secedit.Status, secedit.ErrorCode!.Value));
        Assert.Equal(ItemStatus.NotCollected, snapshot.Items.Single(i => i.Key == ItemKeys.ParsedGpresult).Status);
    }

    [Fact]
    public void Kaputte_Rohdaten_ergeben_einen_Fehler_am_abgeleiteten_Eintrag()
    {
        using var temp = new TempFolder();
        var polBytes = Pol((@"Software\Policies\A", "B", 4, Dword(1)));
        var path = SnapshotFile.Write(Collected()
            .Api(ItemKeys.RawSeceditInf, new { text = "kein ini" })
            .Api(ItemKeys.RawRegistryPol, new { base64 = Convert.ToBase64String(polBytes), sha256 = new string('0', 64) })
            .Api(ItemKeys.RawGpresultXml, new { text = "<Rsop>" })
            .Build(), temp.Path);
        var snapshot = Reader().Read(path, ReadLimits.Default);
        Assert.StartsWith(RglCodesParsing.InfInvalid, snapshot.Items.Single(i => i.Key == ItemKeys.ParsedSecedit).Error, StringComparison.Ordinal);
        Assert.StartsWith(RglCodesParsing.RawHashMismatch, snapshot.Items.Single(i => i.Key == ItemKeys.ParsedRegistryPol).Error, StringComparison.Ordinal);
        // Unlesbares gpresult-XML: Rohdaten verworfen (Geheimnisse ließen sich darin nicht finden)
        var raw = snapshot.Items.Single(i => i.Key == ItemKeys.RawGpresultXml);
        Assert.Equal((ItemStatus.Error, (JsonElement?)null), (raw.Status, raw.Value));
        Assert.Equal(ItemStatus.Error, snapshot.Items.Single(i => i.Key == ItemKeys.ParsedGpresult).Status);
    }

    [Fact]
    public void Registry_pol_und_gpresult_werden_berechnet()
    {
        using var temp = new TempFolder();
        var polBytes = Pol((@"Software\Policies\Beispiel", "LokalerWert", 4, Dword(1)));
        var path = SnapshotFile.Write(Collected()
            .Api(ItemKeys.RawRegistryPol, new { base64 = Convert.ToBase64String(polBytes), sha256 = Hashes.Sha256Hex(polBytes) })
            .Api(ItemKeys.RawGpresultXml, new { text = GpresultAndFirewallTests.Sample() })
            .Build(), temp.Path);
        var snapshot = Reader().Read(path, ReadLimits.Default);
        var pol = snapshot.Items.Single(i => i.Key == ItemKeys.ParsedRegistryPol).Value!.Value.EnumerateArray().Single();
        Assert.Equal(@"HKLM\Software\Policies\Beispiel", pol.GetProperty("key").GetString());
        var gp = snapshot.Items.Single(i => i.Key == ItemKeys.ParsedGpresult).Value!.Value;
        Assert.Equal(2, gp.GetProperty("gpos").GetArrayLength());
    }

    [Fact]
    public void Geheimnisse_werden_beim_Import_erneut_geschwaerzt()
    {
        using var temp = new TempFolder();
        var polBytes = Pol((@"Software\Policies\Beispiel\Autologon", "DefaultPassword", 1, Sz("geheim-pol")));
        var snapshot = Collected("GEHEIM")
            .RegistryValue(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "DefaultPassword", RegValueType.Sz, "geheim-reg")
            .RegistryValue(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "AutoAdminLogon", RegValueType.Sz, "1")
            .RegistryValue(@"HKLM\SOFTWARE\Policies\Beispiel", "ApiToken", RegValueType.Sz, "")
            .RegistryValue(@"HKLM\SYSTEM\CurrentControlSet\Control\Lsa", "LimitBlankPasswordUse", RegValueType.Dword, 1)
            .RegistryValue(@"HKLM\SYSTEM\CurrentControlSet\Services\SNMP\Parameters\ValidCommunities", "oeffentlich-geheim", RegValueType.Dword, 4)
            .Api(ItemKeys.RawSeceditInf, new { text = "[Registry Values]\r\nMACHINE\\Software\\Beispiel\\Passphrase=1,\"geheim-inf\"\r\n" })
            .Api(ItemKeys.RawRegistryPol, new { base64 = Convert.ToBase64String(polBytes), sha256 = Hashes.Sha256Hex(polBytes) })
            .Api(ItemKeys.RawGpresultXml, new { text = GpresultAndFirewallTests.Sample() })
            .Build();
        var path = SnapshotFile.Write(snapshot, temp.Path);
        var read = Reader().Read(path, ReadLimits.Default);
        var json = RiegelJson.Serialize(read);
        foreach (var secret in new[] { "geheim-reg", "geheim-pol", "geheim-inf", "oeffentlich-geheim", ">geheim<" })
            Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        var winlogon = read.Items.Single(i => i.Key.EndsWith(@"\Winlogon", StringComparison.Ordinal)).Value!.Value.GetProperty("values");
        Assert.True(winlogon.GetProperty("DefaultPassword").GetProperty("redacted").GetBoolean());
        Assert.False(winlogon.GetProperty("DefaultPassword").GetProperty("empty").GetBoolean());
        Assert.Equal("1", winlogon.GetProperty("AutoAdminLogon").GetProperty("v").GetString());
        var policies = read.Items.Single(i => i.Key == @"reg:HKLM\SOFTWARE\Policies\Beispiel").Value!.Value.GetProperty("values");
        Assert.True(policies.GetProperty("ApiToken").GetProperty("empty").GetBoolean());
        var lsa = read.Items.Single(i => i.Key == @"reg:HKLM\SYSTEM\CurrentControlSet\Control\Lsa").Value!.Value.GetProperty("values");
        Assert.Equal(1, lsa.GetProperty("LimitBlankPasswordUse").GetProperty("v").GetInt32());
        var snmp = read.Items.Single(i => i.Key.EndsWith("ValidCommunities", StringComparison.Ordinal)).Value!.Value.GetProperty("values");
        Assert.Equal(["redacted-1"], snmp.EnumerateObject().Select(p => p.Name));
        var pol = read.Items.Single(i => i.Key == ItemKeys.RawRegistryPol).Value!.Value;
        Assert.True(pol.GetProperty("redacted").GetBoolean());
        Assert.Equal(Hashes.Sha256Hex(polBytes), pol.GetProperty("sha256").GetString());
        Assert.Equal(ItemStatus.Ok, read.Items.Single(i => i.Key == ItemKeys.ParsedRegistryPol).Status);
        // Ein zweites Mal lesen ändert nichts mehr
        var again = SnapshotFile.Write(read with { Machine = read.Machine with { HostName = "GEHEIM2" } }, temp.Sub("2"));
        Assert.Equal(json.Replace("GEHEIM", "GEHEIM2", StringComparison.Ordinal), RiegelJson.Serialize(Reader().Read(again, ReadLimits.Default)));
    }

    [Fact]
    public void Ein_Ordner_wird_als_Bordmittel_Paket_gelesen()
    {
        var snapshot = Reader().Read(Path.Combine(RepoPaths.SyntheticFixtures, "PG-ALT-01_20261010.riegel-builtin"), ReadLimits.Default);
        Assert.Equal(SnapshotOrigin.Builtin, snapshot.Origin);
        var check = Reader().VerifySidecar(Path.Combine(RepoPaths.SyntheticFixtures, "PG-ALT-01_20261010.riegel-builtin"));
        Assert.Equal(HashCheckStatus.Missing, check.Status);
        Assert.True(Hashes.IsSha256(check.Actual));
    }

    private static string Write(TempFolder temp, string name, string text)
    {
        var path = Path.Combine(temp.Path, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }
}
