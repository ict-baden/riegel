// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using Riegel.Core.Model;

namespace Riegel.Core.Parsing;

/// <summary>Eine gelesene .reg-Datei: Schlüssel in Dateireihenfolge.</summary>
internal sealed record RegFile(bool Unicode, IReadOnlyList<RegKeyData> Keys);

/// <summary>
/// Leser für Exporte von <c>reg export</c> / regedit: „Windows Registry Editor Version 5.00“ (UTF-16 LE mit BOM) und
/// „REGEDIT4“ (ANSI). Schlüsselzeilen <c>[HKEY_LOCAL_MACHINE\…]</c>, Wertzeilen <c>"Name"=…</c> bzw. <c>@=…</c> mit
/// <c>"Text"</c>, <c>dword:xxxxxxxx</c>, <c>hex:…</c> (REG_BINARY) und <c>hex(n):…</c> (n = Typzahl hexadezimal, z. B.
/// hex(2) REG_EXPAND_SZ, hex(7) REG_MULTI_SZ, hex(b) REG_QWORD), Fortsetzungszeilen mit „\“ am Ende. Löschzeilen
/// (<c>[-…]</c>, <c>=-</c>), andere Hives als HKLM und jede unbekannte Zeile sind Fehler (RGL-0263): ein Export, den
/// Riegel nicht vollständig versteht, darf keine Abdeckung „vollständig“ begründen.
/// </summary>
internal static class RegFileParser
{
    private const string Header5 = "Windows Registry Editor Version 5.00";
    private const string Header4 = "REGEDIT4";
    private const string Hklm = "HKEY_LOCAL_MACHINE";

    /// <summary>Datei lesen (Bytes inkl. BOM).</summary>
    internal static RegFile Parse(byte[] bytes, string source, int maxValues, int maxStringChars)
    {
        string text;
        bool unicode;
        switch (TextFiles.DetectBom(bytes))
        {
            case TextFiles.Bom.Utf16Le:
                text = TextFiles.DecodeUtf16(bytes.AsSpan(2), source);
                unicode = true;
                break;
            case TextFiles.Bom.Utf8:
                text = TextFiles.DecodeUtf8(bytes.AsSpan(3), source);
                unicode = true;
                break;
            case TextFiles.Bom.Utf16Be:
                throw RglCodesParsing.Error(RglCodesParsing.InvalidEncoding, source, "UTF-16 BE");
            default:
                // REGEDIT4 ist ANSI; Zeichen außerhalb ASCII kommen nur in Namen und Texten vor
                text = Encoding.Latin1.GetString(bytes);
                unicode = false;
                break;
        }
        return ParseText(text, unicode, source, maxValues, maxStringChars);
    }

    internal static RegFile ParseText(string text, bool unicode, string source, int maxValues, int maxStringChars)
    {
        var lines = TextFiles.Lines(text);
        int index = 0;
        while (index < lines.Count && lines[index].Trim().Length == 0) index++;
        if (index >= lines.Count) throw Invalid(source, 1);
        var header = lines[index].Trim();
        if (header == Header5)
        {
            if (!unicode) throw RglCodesParsing.Error(RglCodesParsing.InvalidEncoding, source, "UTF-16 LE (BOM FF FE)");
        }
        else if (header == Header4)
        {
            if (unicode) throw Invalid(source, index + 1);
        }
        else
        {
            throw Invalid(source, index + 1);
        }
        index++;
        var keys = new List<RegKeyData>();
        var byPath = new Dictionary<string, RegKeyData>(StringComparer.OrdinalIgnoreCase);
        RegKeyData? current = null;
        int values = 0;
        for (; index < lines.Count; index++)
        {
            int lineNumber = index + 1;
            var line = lines[index];
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == ';') continue;
            if (trimmed[0] == '[')
            {
                if (trimmed[^1] != ']' || trimmed.Length < 3 || trimmed[1] == '-') throw Invalid(source, lineNumber);
                var path = NormalizeKey(trimmed[1..^1], source, lineNumber);
                if (!byPath.TryGetValue(path, out current))
                {
                    current = new RegKeyData(path);
                    byPath[path] = current;
                    keys.Add(current);
                }
                continue;
            }
            if (current is null) throw Invalid(source, lineNumber);
            // Fortsetzungszeilen (hex-Daten, „\“ am Ende) zusammensetzen
            var logical = new StringBuilder(trimmed);
            while (logical.Length > 0 && logical[^1] == '\\' && IsHexContinuation(logical))
            {
                logical.Length--;
                if (++index >= lines.Count) throw Invalid(source, lineNumber);
                logical.Append(lines[index].Trim());
                if (logical.Length > 64L * maxStringChars) throw Invalid(source, lineNumber);
            }
            var (name, value) = ParseValueLine(logical.ToString(), unicode, source, lineNumber, maxStringChars);
            if (++values > maxValues) throw RglCodesParsing.Error(RglCodesParsing.TooManyItems, source, maxValues);
            current.Set(name, value);
        }
        // Unterschlüssel aus den Pfaden
        foreach (var key in keys)
        {
            int cut = key.Path.LastIndexOf('\\');
            if (cut > 0 && byPath.TryGetValue(key.Path[..cut], out var parent)) parent.Subkeys!.Add(key.Path[(cut + 1)..]);
        }
        return new RegFile(unicode, keys);
    }

    /// <summary>Endet die Zeile mit „\“ als Fortsetzung von hex-Daten (nicht als Teil eines Textes)?</summary>
    private static bool IsHexContinuation(StringBuilder line)
    {
        var s = line.ToString();
        int eq = FindAssignment(s);
        if (eq < 0) return false;
        var data = s[(eq + 1)..].TrimStart();
        return data.StartsWith("hex", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>„HKEY_LOCAL_MACHINE\…“ → „HKLM\…“; andere Hives sind nicht erlaubt.</summary>
    private static string NormalizeKey(string key, string source, int lineNumber)
    {
        key = key.Trim();
        if (key.Equals(Hklm, StringComparison.OrdinalIgnoreCase)) return "HKLM";
        if (!key.StartsWith(Hklm + "\\", StringComparison.OrdinalIgnoreCase)) throw Invalid(source, lineNumber);
        var rest = key[(Hklm.Length + 1)..].TrimEnd('\\');
        if (rest.Length == 0 || rest.Contains("\\\\", StringComparison.Ordinal) || TextFiles.HasControlCharacters(rest)) throw Invalid(source, lineNumber);
        return @"HKLM\" + rest;
    }

    /// <summary>Position des „=“ nach dem Namen (Name in Anführungszeichen mit \" und \\ oder „@“); -1 wenn keiner.</summary>
    private static int FindAssignment(string line)
    {
        if (line.StartsWith('@')) return line.Length > 1 && line[1] == '=' ? 1 : -1;
        if (!line.StartsWith('"')) return -1;
        for (int i = 1; i < line.Length; i++)
        {
            if (line[i] == '\\') { i++; continue; }
            if (line[i] == '"') return i + 1 < line.Length && line[i + 1] == '=' ? i + 1 : -1;
        }
        return -1;
    }

    private static (string Name, RegValue Value) ParseValueLine(string line, bool unicode, string source, int lineNumber, int maxStringChars)
    {
        int eq = FindAssignment(line);
        if (eq < 0) throw Invalid(source, lineNumber);
        string name = line.StartsWith('@') ? "" : Unescape(line[1..(eq - 1)], source, lineNumber);
        var data = line[(eq + 1)..].Trim();
        if (data == "-") throw Invalid(source, lineNumber);
        try
        {
            if (data.StartsWith('"'))
            {
                if (data.Length < 2 || data[^1] != '"') throw Invalid(source, lineNumber);
                return (name, RegValue.String(Unescape(data[1..^1], source, lineNumber)));
            }
            if (data.StartsWith("dword:", StringComparison.OrdinalIgnoreCase))
            {
                var hex = data[6..];
                if (hex.Length is 0 or > 8 || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var d))
                    throw Invalid(source, lineNumber);
                return (name, RegValue.Dword(d));
            }
            if (data.StartsWith("hex", StringComparison.OrdinalIgnoreCase))
            {
                uint type = RegTypes.Binary;
                int colon = data.IndexOf(':');
                if (colon < 0) throw Invalid(source, lineNumber);
                var prefix = data[..colon];
                if (prefix.Length > 3)
                {
                    if (prefix[3] != '(' || prefix[^1] != ')' ||
                        !uint.TryParse(prefix[4..^1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out type))
                        throw Invalid(source, lineNumber);
                }
                else if (!prefix.Equals("hex", StringComparison.OrdinalIgnoreCase))
                {
                    throw Invalid(source, lineNumber);
                }
                var bytes = ParseHexBytes(data[(colon + 1)..], source, lineNumber);
                if (type == RegTypes.DwordBigEndian)
                {
                    if (bytes.Length != 4) throw Invalid(source, lineNumber);
                    return (name, RegValue.Bytes("unknown", bytes));
                }
                return (name, RegValue.FromRaw(type, bytes, unicode));
            }
        }
        catch (FormatException)
        {
            throw Invalid(source, lineNumber);
        }
        throw Invalid(source, lineNumber);
    }

    private static byte[] ParseHexBytes(string text, string source, int lineNumber)
    {
        text = text.Trim();
        if (text.Length == 0) return [];
        var parts = text.Split(',');
        var bytes = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i].Trim();
            if (p.Length is 0 or > 2 || !byte.TryParse(p, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[i]))
                throw Invalid(source, lineNumber);
        }
        return bytes;
    }

    /// <summary>\\ → \ und \" → " (wie regedit schreibt); jedes andere \x bleibt unverändert.</summary>
    private static string Unescape(string text, string source, int lineNumber)
    {
        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] is '\\' or '"')
            {
                builder.Append(text[++i]);
                continue;
            }
            if (c == '"') throw Invalid(source, lineNumber);
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static RiegelException Invalid(string source, int lineNumber) =>
        RglCodesParsing.Error(RglCodesParsing.RegFileInvalid, source, lineNumber);
}
