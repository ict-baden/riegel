// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.Model;

namespace Riegel.Core.Parsing;

/// <summary>
/// Leser für die Ausgabe von <c>reg query … /v &lt;Wert&gt;</c> (auch mit <c>/s</c>), wie sie <c>collect-builtin.cmd</c>
/// schreibt (spec 7.5): Schlüsselzeilen ab Spalte 1 (<c>HKEY_LOCAL_MACHINE\…</c>), Wertzeilen eingerückt mit Name,
/// Typkennung und Daten, getrennt durch vier Leerzeichen (ab Vista) oder einen Tabulator (Windows XP, Form
/// unverified). Die Typkennungen (<c>REG_DWORD</c> …) sind nicht übersetzt; jede andere Zeile ab Spalte 1 (die
/// übersetzte Fußzeile „End of search …“, die Kopfzeile „! REG.EXE VERSION 3.0“, Fehlermeldungen) wird übergangen.
/// </summary>
internal static class RegQueryParser
{
    private const string Hklm = "HKEY_LOCAL_MACHINE";

    private static readonly string[] TypeTokens =
    [
        "REG_SZ", "REG_EXPAND_SZ", "REG_MULTI_SZ", "REG_DWORD", "REG_QWORD", "REG_BINARY", "REG_NONE", "REG_DWORD_BIG_ENDIAN",
        "REG_LINK", "REG_RESOURCE_LIST", "REG_FULL_RESOURCE_DESCRIPTOR", "REG_RESOURCE_REQUIREMENTS_LIST",
    ];

    /// <summary>Ein Wert aus der Ausgabe.</summary>
    internal sealed record QueryValue(string Name, string Type, string Data);

    /// <summary>Ein Schlüssel mit den ausgegebenen Werten.</summary>
    internal sealed record QueryKey(string Path, List<QueryValue> Values);

    /// <summary>Liest die Ausgabe. Eine eingerückte Zeile ohne erkennbare Typkennung → RGL-0266.</summary>
    internal static List<QueryKey> Parse(string text, string source, int maxValues)
    {
        var keys = new List<QueryKey>();
        QueryKey? current = null;
        int lineNumber = 0, values = 0;
        foreach (var line in TextFiles.Lines(text))
        {
            lineNumber++;
            if (line.Trim().Length == 0) continue;
            if (line[0] != ' ' && line[0] != '\t')
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith(Hklm + "\\", StringComparison.OrdinalIgnoreCase) || trimmed.Equals(Hklm, StringComparison.OrdinalIgnoreCase))
                {
                    var rest = trimmed.Length > Hklm.Length ? trimmed[(Hklm.Length + 1)..].TrimEnd('\\') : "";
                    if (TextFiles.HasControlCharacters(rest)) throw Invalid(source, lineNumber);
                    current = new QueryKey(rest.Length == 0 ? "HKLM" : @"HKLM\" + rest, []);
                    keys.Add(current);
                }
                else
                {
                    // Fußzeile, Kopfzeile oder Meldung (übersetzt) – kein Inhalt
                    current = null;
                }
                continue;
            }
            if (current is null) throw Invalid(source, lineNumber);
            var value = ParseValue(line, source, lineNumber);
            if (++values > maxValues) throw RglCodesParsing.Error(RglCodesParsing.TooManyItems, source, maxValues);
            current.Values.Add(value);
        }
        return keys;
    }

    private static QueryValue ParseValue(string line, string source, int lineNumber)
    {
        var body = line.TrimStart(' ', '\t');
        foreach (var separator in new[] { "    ", "\t" })
        {
            int search = 0;
            while (true)
            {
                int at = body.IndexOf(separator + "REG_", search, StringComparison.Ordinal);
                if (at < 0) break;
                int tokenStart = at + separator.Length;
                int tokenEnd = tokenStart;
                while (tokenEnd < body.Length && (char.IsAsciiLetterUpper(body[tokenEnd]) || body[tokenEnd] == '_')) tokenEnd++;
                var token = body[tokenStart..tokenEnd];
                bool endOk = tokenEnd == body.Length || body.AsSpan(tokenEnd).StartsWith(separator, StringComparison.Ordinal);
                if (endOk && TypeTokens.Contains(token, StringComparer.Ordinal))
                {
                    var name = body[..at];
                    var data = tokenEnd == body.Length ? "" : body[(tokenEnd + separator.Length)..];
                    return new QueryValue(name, token, data);
                }
                search = at + 1;
            }
        }
        throw Invalid(source, lineNumber);
    }

    /// <summary>Daten einer Wertzeile als Registry-Wert; unbekannte Typen → null (werden übergangen).</summary>
    internal static RegValue? ToRegValue(QueryValue value, string source)
    {
        var data = value.Data.Trim();
        switch (value.Type)
        {
            case "REG_DWORD":
                return RegValue.Dword(ParseHex32(data, source));
            case "REG_QWORD":
                return RegValue.Qword(ParseHex64(data, source));
            case "REG_SZ":
                return RegValue.String(value.Data);
            case "REG_EXPAND_SZ":
                return RegValue.String(value.Data, expand: true);
            case "REG_MULTI_SZ":
                var parts = value.Data.Split(@"\0").ToList();
                while (parts.Count > 0 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
                return RegValue.MultiString(parts);
            case "REG_BINARY":
                if (data.Length % 2 != 0) throw RglCodesParsing.Error(RglCodesParsing.RegQueryInvalid, source, value.Name);
                try
                {
                    return RegValue.Bytes("binary", Convert.FromHexString(data));
                }
                catch (FormatException)
                {
                    throw RglCodesParsing.Error(RglCodesParsing.RegQueryInvalid, source, value.Name);
                }
            default:
                return null;
        }
    }

    private static uint ParseHex32(string text, string source)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v)) return v;
        throw RglCodesParsing.Error(RglCodesParsing.RegQueryInvalid, source, text);
    }

    private static ulong ParseHex64(string text, string source)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ulong.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v)) return v;
        throw RglCodesParsing.Error(RglCodesParsing.RegQueryInvalid, source, text);
    }

    private static RiegelException Invalid(string source, int lineNumber) =>
        RglCodesParsing.Error(RglCodesParsing.RegQueryInvalid, source, lineNumber);
}
