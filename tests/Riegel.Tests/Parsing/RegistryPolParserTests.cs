// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Parsing;
using static Riegel.Tests.Parsing.ParsingSamples;

namespace Riegel.Tests.Parsing;

/// <summary>Registry.pol nach Microsofts „Registry Policy File Format“ – gültige, Sonder- und feindliche Fälle.</summary>
public sealed class RegistryPolParserTests
{
    [Fact]
    public void Liest_Werte_aller_Typen_und_ordnet_sie_HKLM_zu()
    {
        var bytes = Pol(
            (@"Software\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast", 4, Dword(0)),
            (@"Software\Policies\Beispiel", "Text", 1, Sz("abc")),
            (@"Software\Policies\Beispiel", "Pfad", 2, Sz(@"%SystemRoot%\x")),
            (@"Software\Policies\Beispiel", "Liste", 7, MultiSz("a", "b")),
            (@"Software\Policies\Beispiel", "Gross", 11, [1, 0, 0, 0, 0, 0, 0, 0]),
            (@"Software\Policies\Beispiel", "Roh", 3, [0xDE, 0xAD]));
        var entries = RegistryPolParser.Parse(bytes, "Registry.pol", 1000);
        Assert.Equal(6, entries.Count);
        Assert.All(entries, e => Assert.Equal(PolEntryKind.Value, e.Kind));
        Assert.Equal(@"HKLM\Software\Policies\Microsoft\Windows NT\DNSClient", entries[0].MachineKey);
        var parsed = RegistryPolParser.ToParsedItem(entries, 1024 * 1024);
        var json = parsed.GetRawText();
        Assert.Contains("\"value\":\"EnableMulticast\",\"t\":\"dword\",\"v\":0", json, StringComparison.Ordinal);
        Assert.Contains("\"t\":\"sz\",\"v\":\"abc\"", json, StringComparison.Ordinal);
        Assert.Contains("\"t\":\"expand_sz\"", json, StringComparison.Ordinal);
        Assert.Contains("\"t\":\"multi_sz\",\"v\":[\"a\",\"b\"]", json, StringComparison.Ordinal);
        Assert.Contains("\"t\":\"qword\",\"v\":\"1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"t\":\"binary\",\"v\":\"3q0=\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Sondernamen_sind_keine_Werte()
    {
        var bytes = Pol(
            (@"Software\Policies\A", "**del.Alt", 1, Sz(" ")),
            (@"Software\Policies\A", "**DeleteValues", 1, Sz("x;y")),
            (@"Software\Policies\A", "**DelVals.", 1, Sz(" ")),
            (@"Software\Policies\A", "**DeleteKeys", 1, Sz("K1;K2")),
            (@"Software\Policies\A", "**SecureKey", 4, Dword(1)),
            (@"Software\Policies\A", "**soft.Weich", 4, Dword(1)),
            (@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "", 0, []));
        var entries = RegistryPolParser.Parse(bytes, "Registry.pol", 1000);
        Assert.Equal([PolEntryKind.DeleteValue, PolEntryKind.DeleteValues, PolEntryKind.DeleteAllValues, PolEntryKind.DeleteKeys,
            PolEntryKind.SecureKey, PolEntryKind.OtherSpecial, PolEntryKind.CreateKey], entries.Select(e => e.Kind));
        Assert.Equal(["Alt"], entries[0].Targets);
        Assert.Equal(["x", "y"], entries[1].Targets);
        Assert.Equal(["K1", "K2"], entries[3].Targets);
        Assert.Equal("[]", RegistryPolParser.ToParsedItem(entries, 100).GetRawText());
    }

    [Fact]
    public void Nur_Kopf_ist_eine_leere_Richtlinie()
    {
        // Wie in den synthetischen Fixtures: „PReg“ + Version 1, keine Einträge
        Assert.Empty(RegistryPolParser.Parse(Convert.FromBase64String("UFJlZwEAAAA="), "Registry.pol", 10));
    }

    [Theory]
    [InlineData("kurz")]
    [InlineData("signatur")]
    [InlineData("version")]
    [InlineData("abgeschnitten")]
    [InlineData("groesse")]
    [InlineData("klammer")]
    [InlineData("utf16")]
    [InlineData("ohne-nul")]
    public void Feindliche_Dateien_ergeben_RGL_0270(string fall)
    {
        var good = Pol((@"Software\Policies\A", "B", 4, Dword(1)));
        byte[] bytes = fall switch
        {
            "kurz" => [0x50, 0x52],
            "signatur" => [.. "XReg"u8.ToArray(), 1, 0, 0, 0],
            "version" => [.. good[..4], 2, 0, 0, 0, .. good[8..]],
            "abgeschnitten" => good[..^3],
            "groesse" => Grow(good),
            "klammer" => [.. good[..8], (byte)'(', 0, .. good[10..]],
            "utf16" => [.. good[..10], 0x00, 0xD8, .. good[12..]],
            "ohne-nul" => [.. good[..8], (byte)'[', 0, .. Enumerable.Repeat((byte)'A', 20).SelectMany(b => new[] { b, (byte)0 })],
            _ => throw new ArgumentOutOfRangeException(nameof(fall)),
        };
        var e = Assert.Throws<RiegelException>(() => RegistryPolParser.Parse(bytes, "Registry.pol", 100));
        Assert.True(e.Code == RglCodesParsing.RegistryPolInvalid, e.ToString());

        static byte[] Grow(byte[] pol)
        {
            // Größenfeld auf 0x7FFFFFFF setzen: die Daten reichen nicht
            var copy = pol.ToArray();
            int sizeAt = copy.Length - 2 - 4 - 2 - 4;
            copy[sizeAt] = 0xFF; copy[sizeAt + 1] = 0xFF; copy[sizeAt + 2] = 0xFF; copy[sizeAt + 3] = 0x7F;
            return copy;
        }
    }

    [Fact]
    public void Zu_viele_Eintraege_sind_begrenzt()
    {
        var entries = Enumerable.Range(0, 20).Select(i => (@"Software\Policies\A", "V" + i, 4u, Dword(1))).ToArray();
        var e = Assert.Throws<RiegelException>(() => RegistryPolParser.Parse(Pol(entries), "Registry.pol", 10));
        Assert.Equal(RglCodesParsing.TooManyItems, e.Code);
    }

    [Fact]
    public void Geheime_Texte_werden_geschwaerzt_Zahlen_nicht()
    {
        var bytes = Pol(
            (@"Software\Policies\Beispiel\Autologon", "DefaultPassword", 1, Sz("geheim")),
            (@"Software\Policies\Microsoft\Windows NT\Terminal Services", "DisablePasswordSaving", 4, Dword(1)));
        var entries = RegistryPolParser.Parse(bytes, "Registry.pol", 10);
        var json = RegistryPolParser.ToParsedItem(entries, 100).GetRawText();
        Assert.DoesNotContain("geheim", json, StringComparison.Ordinal);
        Assert.Contains("\"redacted\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"value\":\"DisablePasswordSaving\",\"t\":\"dword\",\"v\":1", json, StringComparison.Ordinal);
        var clean = RegistryPolParser.Redact(bytes, entries);
        Assert.NotNull(clean);
        var again = RegistryPolParser.Parse(clean, "Registry.pol", 10);
        Assert.Empty(again[0].Data);
        Assert.Equal(entries[1].Data, again[1].Data);
        Assert.Null(RegistryPolParser.Redact(bytes[..^0], again));
    }

    [Fact]
    public void Ausgabe_passt_zur_Kodierung_der_Registry_Eintraege()
    {
        var parsed = RegistryPolParser.ToParsedItem(RegistryPolParser.Parse(Pol((@"Software\Policies\A", "Zahl", 4, Dword(4294967295))), "x", 10), 100);
        var entry = parsed.EnumerateArray().Single();
        Assert.Equal(4294967295u, entry.GetProperty("v").GetUInt32());
        Assert.Equal(["key", "value", "t", "v"], entry.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Number, entry.GetProperty("v").ValueKind);
    }
}
