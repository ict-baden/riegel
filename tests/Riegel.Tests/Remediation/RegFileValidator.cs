// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Riegel.Tests.Remediation;

/// <summary>Ein Wert aus einer geprüften .reg-Datei.</summary>
internal sealed record RegEntry(string Key, string Name, string Kind, object? Data)
{
    public override string ToString() => $"[{Key}] {Name} = {Kind}:{Data}";
}

/// <summary>
/// Strenger Prüfer für .reg-Dateien (Version 5.00), unabhängig vom Schreiber gebaut nach KB 310516 und einem
/// aufgezeichneten <c>reg export</c>: UTF-16 LE mit BOM, nur CRLF, erste Zeile genau die Versionszeile, danach nur
/// Leerzeilen, Kommentare (<c>;</c>), Schlüsselzeilen <c>[HKEY_LOCAL_MACHINE\…]</c> und Wertzeilen. Zeichenketten nur
/// mit <c>\\</c> und <c>\"</c> als Escapes, ohne Steuerzeichen; Hex-Daten mit <c>,\</c>-Fortsetzung und zwei Leerzeichen.
/// Jede Abweichung lässt den Test scheitern. Gibt die gelesenen Werte zurück (Hex-Typen dekodiert).
/// </summary>
internal static partial class RegFileValidator
{
    [GeneratedRegex(@"^\[(-?)(HKEY_LOCAL_MACHINE(\\[\x20\x21\x23-\x5A\x5E-\x7E]{1,255})+)\]$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyLine();

    [GeneratedRegex(@"^dword:([0-9a-f]{8})$", RegexOptions.CultureInvariant)]
    private static partial Regex Dword();

    [GeneratedRegex(@"^(hex|hex\((?:2|7|b)\)):((?:[0-9a-f]{2},)*[0-9a-f]{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex Hex();

    public static IReadOnlyList<RegEntry> Validate(byte[] bytes, string expectedFirstComment)
    {
        Assert.True(bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE, "UTF-16 LE mit BOM");
        Assert.True(bytes.Length % 2 == 0, "UTF-16 mit gerader Bytezahl");
        var text = new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true).GetString(bytes, 2, bytes.Length - 2);
        // Nur CRLF als Zeilenende
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r') Assert.True(i + 1 < text.Length && text[i + 1] == '\n', $"CR ohne LF bei {i}");
            if (text[i] == '\n') Assert.True(i > 0 && text[i - 1] == '\r', $"LF ohne CR bei {i}");
        }
        Assert.EndsWith("\r\n\r\n", text, StringComparison.Ordinal);
        var lines = text[..^2].Split("\r\n");
        Assert.Equal("Windows Registry Editor Version 5.00", lines[0]);
        Assert.Equal("; " + expectedFirstComment, lines[1]);
        Assert.Equal("", lines[^1]);

        var entries = new List<RegEntry>();
        string? key = null;
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            Assert.True(line.All(c => !char.IsControl(c) && (int)c != 0x2028 && (int)c != 0x2029), $"Zeile {i + 1}: Steuerzeichen");
            if (line.Length == 0) continue;
            if (line.StartsWith(';')) continue;
            if (line.StartsWith('['))
            {
                var m = KeyLine().Match(line);
                Assert.True(m.Success, $"Zeile {i + 1}: ungültige Schlüsselzeile {line}");
                Assert.True(m.Groups[1].Value.Length == 0, $"Zeile {i + 1}: Riegel löscht keine ganzen Schlüssel");
                key = m.Groups[2].Value;
                continue;
            }
            Assert.True(key is not null, $"Zeile {i + 1}: Wert vor dem ersten Schlüssel");
            // Name
            int pos;
            string name;
            if (line.StartsWith("@=", StringComparison.Ordinal)) { name = ""; pos = 2; }
            else
            {
                Assert.True(line.StartsWith('"'), $"Zeile {i + 1}: Wertname muss in Anführungszeichen stehen: {line}");
                name = ReadQuoted(line, 0, out pos, i + 1);
                Assert.True(pos < line.Length && line[pos] == '=', $"Zeile {i + 1}: „=“ nach dem Namen erwartet");
                pos++;
            }
            var data = line[pos..];
            // Fortsetzungszeilen der Hex-Daten einsammeln
            while (data.EndsWith(",\\", StringComparison.Ordinal))
            {
                Assert.True(i + 1 < lines.Length, "Fortsetzung am Dateiende");
                var next = lines[++i];
                Assert.True(next.StartsWith("  ", StringComparison.Ordinal) && !next.StartsWith("   ", StringComparison.Ordinal),
                    $"Zeile {i + 1}: Fortsetzung mit genau zwei Leerzeichen");
                data = data[..^1] + next[2..];
            }
            entries.Add(new RegEntry(key!, name, Kind(data, i + 1, out var value), value));
        }
        return entries;
    }

    private static string Kind(string data, int line, out object? value)
    {
        value = null;
        if (data == "-") return "delete";
        if (data.StartsWith('"'))
        {
            value = ReadQuoted(data, 0, out int end, line);
            Assert.True(end == data.Length, $"Zeile {line}: Zeichen nach der Zeichenkette");
            return "sz";
        }
        var dword = Dword().Match(data);
        if (dword.Success)
        {
            value = uint.Parse(dword.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return "dword";
        }
        var hex = Hex().Match(data);
        Assert.True(hex.Success, $"Zeile {line}: ungültige Daten {data}");
        var bytes = hex.Groups[2].Value.Length == 0 ? [] : hex.Groups[2].Value.Split(',').Select(b => Convert.ToByte(b, 16)).ToArray();
        switch (hex.Groups[1].Value)
        {
            case "hex":
                value = bytes;
                return "binary";
            case "hex(b)":
                Assert.Equal(8, bytes.Length);
                value = BitConverter.ToUInt64(bytes);
                return "qword";
            case "hex(2)":
                value = Utf16(bytes, line, terminators: 1).Single();
                return "expand_sz";
            default:
                value = Utf16(bytes, line, terminators: 2);
                return "multi_sz";
        }
    }

    /// <summary>UTF-16 LE mit abschließendem NUL (expand_sz) bzw. NUL je Eintrag und einem weiteren am Ende (multi_sz).</summary>
    private static List<string> Utf16(byte[] bytes, int line, int terminators)
    {
        Assert.True(bytes.Length % 2 == 0, $"Zeile {line}: ungerade Bytezahl");
        var text = new UnicodeEncoding(false, false, true).GetString(bytes);
        if (terminators == 1)
        {
            Assert.EndsWith("\0", text, StringComparison.Ordinal);
            var body = text[..^1];
            Assert.DoesNotContain('\0', body);
            return [body];
        }
        Assert.EndsWith("\0\0", text, StringComparison.Ordinal);
        var parts = text[..^2].Split('\0');
        Assert.All(parts, p => Assert.NotEmpty(p));
        return [.. parts];
    }

    private static string ReadQuoted(string line, int start, out int end, int lineNo)
    {
        Assert.Equal('"', line[start]);
        var sb = new StringBuilder();
        int i = start + 1;
        while (true)
        {
            Assert.True(i < line.Length, $"Zeile {lineNo}: Zeichenkette nicht geschlossen");
            char c = line[i];
            if (c == '"') { end = i + 1; return sb.ToString(); }
            if (c == '\\')
            {
                Assert.True(i + 1 < line.Length && line[i + 1] is '\\' or '"', $"Zeile {lineNo}: unzulässiges Escape bei {i}");
                sb.Append(line[i + 1]);
                i += 2;
                continue;
            }
            sb.Append(c);
            i++;
        }
    }
}
