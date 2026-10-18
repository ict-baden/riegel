// SPDX-License-Identifier: MIT
using System.Text;
using Riegel.Core.Model;
using Riegel.Core.Parsing;
using static Riegel.Tests.Parsing.ParsingSamples;

namespace Riegel.Tests.Parsing;

/// <summary>Sicherheitsvorlage (secedit /export, GptTmpl.inf) und CSV der Überwachungsrichtlinie (auditpol /backup, audit.csv).</summary>
public sealed class SeceditAndAuditTests
{
    private const string Inf = """
        [Unicode]
        Unicode=yes
        [System Access]
        MinimumPasswordLength = 14
        NewGuestName = "Gast"
        ; Kommentar
        [Event Audit]
        AuditLogonEvents = 3
        [Registry Values]
        MACHINE\System\CurrentControlSet\Control\Lsa\NoLMHash=4,1
        MACHINE\Software\Beispiel\Text=1,"a,b"
        MACHINE\Software\Beispiel\DefaultPassword=1,"geheim"
        MACHINE\Software\Beispiel\LimitBlankPasswordUse=4,1
        [Privilege Rights]
        SeDebugPrivilege = *S-1-5-32-544
        SeDenyNetworkLogonRight = Guest,*S-1-5-32-546
        SeTcbPrivilege =
        [Service General Setting]
        "Spooler",4,""
        [Version]
        signature="$CHICAGO$"
        Revision=1
        """;

    [Fact]
    public void Liest_die_Sektionen_wie_parsed_secedit()
    {
        var text = TextFiles.DecodeUtf16WithBom(Utf16Bom(Inf), "secedit.inf");
        var sections = SeceditParser.Parse(text, "secedit.inf", 1024);
        Assert.Equal(["Unicode", "System Access", "Event Audit", "Registry Values", "Privilege Rights", "Service General Setting", "Version"],
            sections.Select(s => s.Name));
        var json = SeceditParser.ToParsedItem(sections);
        var s = json.GetProperty("sections");
        Assert.Equal("14", s.GetProperty("System Access").GetProperty("MinimumPasswordLength").GetString());
        Assert.Equal("\"Gast\"", s.GetProperty("System Access").GetProperty("NewGuestName").GetString());
        var nolm = s.GetProperty("Registry Values").GetProperty(@"MACHINE\System\CurrentControlSet\Control\Lsa\NoLMHash");
        Assert.Equal(4, nolm.GetProperty("type").GetInt32());
        Assert.Equal("1", nolm.GetProperty("value").GetString());
        Assert.Equal("\"a,b\"", s.GetProperty("Registry Values").GetProperty(@"MACHINE\Software\Beispiel\Text").GetProperty("value").GetString());
        // Geheimnis geschwärzt, Zahlenschalter nicht
        var secret = s.GetProperty("Registry Values").GetProperty(@"MACHINE\Software\Beispiel\DefaultPassword");
        Assert.True(secret.GetProperty("redacted").GetBoolean());
        Assert.DoesNotContain("geheim", json.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("1", s.GetProperty("Registry Values").GetProperty(@"MACHINE\Software\Beispiel\LimitBlankPasswordUse").GetProperty("value").GetString());
        Assert.Equal(["*S-1-5-32-544"], s.GetProperty("Privilege Rights").GetProperty("SeDebugPrivilege").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["Guest", "*S-1-5-32-546"], s.GetProperty("Privilege Rights").GetProperty("SeDenyNetworkLogonRight").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(0, s.GetProperty("Privilege Rights").GetProperty("SeTcbPrivilege").GetArrayLength());
        // Zeilen ohne „=“ werden übergangen
        Assert.Empty(s.GetProperty("Service General Setting").EnumerateObject());
    }

    [Fact]
    public void Rohtext_wird_beim_Import_geschwaerzt()
    {
        var clean = SeceditParser.RedactText(Inf.Replace("\n", "\r\n"));
        Assert.NotNull(clean);
        Assert.DoesNotContain("geheim", clean, StringComparison.Ordinal);
        Assert.Contains(@"MACHINE\Software\Beispiel\DefaultPassword=1,", clean, StringComparison.Ordinal);
        Assert.Contains(@"LimitBlankPasswordUse=4,1", clean, StringComparison.Ordinal);
        Assert.Contains("\r\n", clean, StringComparison.Ordinal);
        Assert.Null(SeceditParser.RedactText("[System Access]\nMinimumPasswordLength = 1\n"));
    }

    [Theory]
    [InlineData("MinimumPasswordLength = 1\n[System Access]\n")]
    [InlineData("[System Access\nA=1\n")]
    [InlineData("")]
    public void Kaputte_Vorlagen_ergeben_RGL_0264(string text)
    {
        var e = Assert.Throws<RiegelException>(() => SeceditParser.Parse(text, "secedit.inf", 1024));
        Assert.Equal(RglCodesParsing.InfInvalid, e.Code);
    }

    [Fact]
    public void Ungueltiges_UTF16_und_fehlendes_BOM_sind_RGL_0262()
    {
        byte[] lone = [0xFF, 0xFE, 0x5B, 0x00, 0x00, 0xD8, 0x5D, 0x00];
        Assert.Equal(RglCodesParsing.InvalidEncoding, Assert.Throws<RiegelException>(() => TextFiles.DecodeUtf16WithBom(lone, "x.inf")).Code);
        Assert.Equal(RglCodesParsing.InvalidEncoding, Assert.Throws<RiegelException>(() => TextFiles.DecodeUtf16WithBom([0xFF, 0xFE, 0x41], "x.inf")).Code);
        Assert.Equal(RglCodesParsing.InvalidEncoding, Assert.Throws<RiegelException>(() => TextFiles.DecodeUtf16WithBom(Encoding.ASCII.GetBytes("[A]"), "x.inf")).Code);
    }

    [Fact]
    public void Lange_Zeilen_sind_begrenzt()
    {
        var text = "[System Access]\nA = " + new string('x', 200) + "\n";
        Assert.Equal(RglCodesParsing.InfInvalid, Assert.Throws<RiegelException>(() => SeceditParser.Parse(text, "x.inf", 100)).Code);
    }

    // ---------------------------------------------------------------- Überwachung

    private const string AuditBackup = """
        Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value
        TEST-01,System,Logon,{0CCE9215-69AE-11D9-BED3-505054503030},Success and Failure,,3
        TEST-01,System,"Anmelden, Abmelden",{0cce9216-69ae-11d9-bed3-505054503030},Erfolg,,1
        TEST-01,System,Process Creation,{0CCE922B-69AE-11D9-BED3-505054503030},No Auditing,,0
        TEST-01,,Option:CrashOnAuditFail,,Disabled,,0
        ,,FileGlobalSacl,,,,
        """;

    [Fact]
    public void Nur_GUID_und_Zahl_zaehlen_nie_der_uebersetzte_Text()
    {
        var rows = AuditCsvParser.Parse(AuditBackup.Replace("\n", "\r\n"), "auditpol.csv", 100, 1024);
        Assert.Equal(3, rows.Count);
        Assert.Equal("{0CCE9216-69AE-11D9-BED3-505054503030}", rows[1].Guid);
        Assert.Equal(1, rows[1].Setting);
        Assert.Equal("Anmelden, Abmelden", rows[1].Name);
        var item = AuditCsvParser.ToItem(rows);
        Assert.Equal(3, item.GetProperty("{0CCE9215-69AE-11D9-BED3-505054503030}").GetInt32());
        Assert.Equal(0, item.GetProperty("{0CCE922B-69AE-11D9-BED3-505054503030}").GetInt32());
    }

    [Theory]
    [InlineData("Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value\nX,System,Logon,{0CCE9215-69AE-11D9-BED3-505054503030},Success,,7\n")]
    [InlineData("Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value\nX,System,Logon,{0CCE9215-69AE-11D9-BED3-505054503030},Success,,eins\n")]
    [InlineData("Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value\nX,System,Logon,{0CCE9215-69AE-11D9-BED3-505054503030},Success,,1\nX,System,Logon,{0CCE9215-69AE-11D9-BED3-505054503030},Success,,3\n")]
    [InlineData("Rechner,Ziel,Name\nX,Y,Z\n")]
    [InlineData("Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value\nX,\"offen\n")]
    public void Kaputte_Zeilen_ergeben_RGL_0265(string csv)
    {
        Assert.Equal(RglCodesParsing.AuditCsvInvalid, Assert.Throws<RiegelException>(() => AuditCsvParser.Parse(csv, "auditpol.csv", 100, 1024)).Code);
    }

    [Fact]
    public void GPO_Sicherung_uebergeht_unbekannte_Werte()
    {
        var csv = "Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value\n"
                  + ",System,Audit Logon,{0cce9215-69ae-11d9-bed3-505054503030},Success and Failure,,3\n"
                  + ",System,Audit Other,{0cce9216-69ae-11d9-bed3-505054503030},?,,9\n";
        var rows = AuditCsvParser.Parse(csv, "audit.csv", 100, 1024, strict: false);
        Assert.Single(rows);
    }

    [Fact]
    public void Zu_viele_Zeilen_sind_begrenzt()
    {
        var csv = "Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value\n"
                  + string.Concat(Enumerable.Range(0, 20).Select(i => $"X,System,S{i},{{{Guid.NewGuid()}}},Success,,1\n"));
        Assert.Equal(RglCodesParsing.AuditCsvInvalid, Assert.Throws<RiegelException>(() => AuditCsvParser.Parse(csv, "auditpol.csv", 5, 1024)).Code);
    }
}
