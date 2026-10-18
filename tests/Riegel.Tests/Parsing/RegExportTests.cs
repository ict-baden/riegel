// SPDX-License-Identifier: MIT
using System.Text;
using Riegel.Core.Model;
using Riegel.Core.Parsing;
using static Riegel.Tests.Parsing.ParsingSamples;

namespace Riegel.Tests.Parsing;

/// <summary>reg export (5.00 und REGEDIT4) und die Ausgabe von reg query.</summary>
public sealed class RegExportTests
{
    private const string Export5 = """
        Windows Registry Editor Version 5.00

        [HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Beispiel]
        "Zahl"=dword:0000002a
        "Text"="C:\\Pfad mit \"Anfuehrung\""
        @="Standard"
        "Leer"=""
        "Erweitert"=hex(2):25,00,53,00,79,00,73,00,74,00,65,00,6d,00,52,00,6f,00,6f,00,\
          74,00,25,00,00,00
        "Liste"=hex(7):61,00,00,00,62,00,00,00,00,00
        "Gross"=hex(b):01,00,00,00,00,00,00,00
        "Roh"=hex:de,ad,be,ef
        "Nichts"=hex(0):
        "Gleich=Name"="x"

        [HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Beispiel\Unter]
        "A"=dword:00000001
        """;

    [Fact]
    public void Liest_alle_Werttypen_und_Fortsetzungszeilen()
    {
        var file = RegFileParser.Parse(Utf16Bom(Export5), "01-beispiel.reg", 1000, 1024);
        Assert.True(file.Unicode);
        Assert.Equal([@"HKLM\SOFTWARE\Policies\Beispiel", @"HKLM\SOFTWARE\Policies\Beispiel\Unter"], file.Keys.Select(k => k.Path));
        var root = file.Keys[0];
        RegValue V(string name) => root.Values.Single(v => v.Key == name).Value;
        Assert.Equal(("dword", (object?)42u), (V("Zahl").Type, V("Zahl").Data));
        Assert.Equal("C:\\Pfad mit \"Anfuehrung\"", V("Text").Data);
        Assert.Equal("Standard", V("").Data);
        Assert.Equal("", V("Leer").Data);
        Assert.Equal(("expand_sz", (object?)"%SystemRoot%"), (V("Erweitert").Type, V("Erweitert").Data));
        Assert.Equal(new[] { "a", "b" }, (string[])V("Liste").Data!);
        Assert.Equal(("qword", (object?)1ul), (V("Gross").Type, V("Gross").Data));
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, (byte[])V("Roh").Data!);
        Assert.Equal("none", V("Nichts").Type);
        Assert.Equal("x", V("Gleich=Name").Data);
        Assert.Equal(new[] { "Unter" }, root.Subkeys!);
    }

    [Fact]
    public void REGEDIT4_ist_ANSI()
    {
        var text = "REGEDIT4\r\n\r\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\A]\r\n\"Pfad\"=hex(2):25,41,25,00\r\n\"Liste\"=hex(7):61,00,62,00,00\r\n";
        var file = RegFileParser.Parse(Encoding.Latin1.GetBytes(text), "alt.reg", 100, 1024);
        Assert.False(file.Unicode);
        Assert.Equal("%A%", file.Keys[0].Values[0].Value.Data);
        Assert.Equal(new[] { "a", "b" }, (string[])file.Keys[0].Values[1].Value.Data!);
    }

    [Theory]
    [InlineData("Windows Registry Editor Version 5.00\n\n[-HKEY_LOCAL_MACHINE\\SOFTWARE\\A]\n")]
    [InlineData("Windows Registry Editor Version 5.00\n\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\A]\n\"B\"=-\n")]
    [InlineData("Windows Registry Editor Version 5.00\n\n[HKEY_CURRENT_USER\\Software\\A]\n")]
    [InlineData("Windows Registry Editor Version 5.00\n\n\"B\"=dword:00000001\n")]
    [InlineData("Windows Registry Editor Version 5.00\n\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\A]\n\"B\"=dword:1000000000\n")]
    [InlineData("Windows Registry Editor Version 5.00\n\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\A]\n\"B\"=hex:zz\n")]
    [InlineData("Windows Registry Editor Version 5.00\n\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\A]\n\"B\"=hex(b):01,02\n")]
    [InlineData("Windows Registry Editor Version 5.00\n\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\A]\n\"B\"=hex(7):61,00,00\n")]
    [InlineData("Windows Registry Editor Version 5.00\n\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\A]\n\"B\"=hex:01,\\\n")]
    [InlineData("Windows Registry Editor Version 5.00\n\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\A]\nirgendwas\n")]
    [InlineData("Windows Registry Editor Version 5.00\n\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\A]\n\"B\"=\"offen\n")]
    [InlineData("Kein Kopf\n")]
    public void Kaputte_Exporte_ergeben_RGL_0263(string text)
    {
        var e = Assert.Throws<RiegelException>(() => RegFileParser.Parse(Utf16Bom(text), "x.reg", 100, 1024));
        Assert.True(e.Code == RglCodesParsing.RegFileInvalid, e.ToString());
    }

    [Fact]
    public void Version_5_ohne_UTF16_und_ungueltiges_UTF16_sind_RGL_0262()
    {
        var ansi = Encoding.ASCII.GetBytes("Windows Registry Editor Version 5.00\r\n");
        Assert.Equal(RglCodesParsing.InvalidEncoding, Assert.Throws<RiegelException>(() => RegFileParser.Parse(ansi, "x.reg", 10, 100)).Code);
        byte[] lone = [.. Utf16Bom("Windows Registry Editor Version 5.00\n"), 0x00, 0xDC];
        Assert.Equal(RglCodesParsing.InvalidEncoding, Assert.Throws<RiegelException>(() => RegFileParser.Parse(lone, "x.reg", 10, 100)).Code);
    }

    [Fact]
    public void Zu_viele_Werte_sind_begrenzt()
    {
        var text = "Windows Registry Editor Version 5.00\n\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\A]\n"
                   + string.Concat(Enumerable.Range(0, 30).Select(i => $"\"V{i}\"=dword:00000001\n"));
        Assert.Equal(RglCodesParsing.TooManyItems, Assert.Throws<RiegelException>(() => RegFileParser.Parse(Utf16Bom(text), "x.reg", 10, 100)).Code);
    }

    [Fact]
    public void Lange_Texte_werden_im_Export_abgeschnitten_und_markiert()
    {
        var key = new RegKeyData(@"HKLM\SOFTWARE\A");
        key.Set("Lang", RegValue.String(new string('x', 50)));
        key.Set("Bytes", RegValue.Bytes("binary", new byte[5000]));
        var json = key.ToItemValue(10).GetProperty("values");
        Assert.Equal(10, json.GetProperty("Lang").GetProperty("v").GetString()!.Length);
        Assert.True(json.GetProperty("Lang").GetProperty("truncated").GetBoolean());
        Assert.Equal(5000, json.GetProperty("Bytes").GetProperty("len").GetInt32());
        Assert.Equal(4096, Convert.FromBase64String(json.GetProperty("Bytes").GetProperty("v").GetString()!).Length);
    }

    // ---------------------------------------------------------------- reg query

    [Fact]
    public void Reg_query_Vista_und_XP()
    {
        var vista = "\r\nHKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\Spooler\r\n    Start    REG_DWORD    0x2\r\n\r\n"
                    + "HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon\r\n    DefaultUserName    REG_SZ    Bediener Linie 3\r\n"
                    + "    SearchList    REG_SZ    \r\n    Liste    REG_MULTI_SZ    a\\0b\\0\r\n\r\nSuche beendet: 3 Übereinstimmung(en) gefunden.\r\n";
        var keys = RegQueryParser.Parse(vista, "x.txt", 100);
        Assert.Equal(2, keys.Count);
        Assert.Equal(2u, RegQueryParser.ToRegValue(keys[0].Values[0], "x")!.Data);
        Assert.Equal("Bediener Linie 3", RegQueryParser.ToRegValue(keys[1].Values[0], "x")!.Data);
        Assert.Equal("", RegQueryParser.ToRegValue(keys[1].Values[1], "x")!.Data);
        Assert.Equal(new[] { "a", "b" }, (string[])RegQueryParser.ToRegValue(keys[1].Values[2], "x")!.Data!);
        var xp = "\r\n! REG.EXE VERSION 3.0\r\n\r\nHKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\USBSTOR\r\n    Start\tREG_DWORD\t0x4\r\n";
        var xpKeys = RegQueryParser.Parse(xp, "x.txt", 100);
        Assert.Single(xpKeys);
        Assert.Equal(4u, RegQueryParser.ToRegValue(xpKeys[0].Values[0], "x")!.Data);
    }

    [Theory]
    [InlineData("    Start    REG_DWORD    0x2\r\n")]
    [InlineData("HKEY_LOCAL_MACHINE\\SYSTEM\\A\r\n    kaputt ohne Typ\r\n")]
    public void Kaputte_Ausgabe_ergibt_RGL_0266(string text)
    {
        Assert.Equal(RglCodesParsing.RegQueryInvalid, Assert.Throws<RiegelException>(() => RegQueryParser.Parse(text, "x.txt", 100)).Code);
    }

    [Fact]
    public void Kaputte_Zahl_ergibt_RGL_0266()
    {
        var keys = RegQueryParser.Parse("HKEY_LOCAL_MACHINE\\SYSTEM\\A\r\n    Start    REG_DWORD    zwei\r\n", "x.txt", 100);
        Assert.Equal(RglCodesParsing.RegQueryInvalid, Assert.Throws<RiegelException>(() => RegQueryParser.ToRegValue(keys[0].Values[0], "x.txt")).Code);
    }
}
