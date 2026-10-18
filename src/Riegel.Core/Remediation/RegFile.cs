// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;

namespace Riegel.Core.Remediation;

/// <summary>
/// Ein Wert, wie er in eine .reg-Datei geschrieben wird (Typ und Daten), oder das Löschen eines Werts.
/// </summary>
internal abstract record RegData
{
    public sealed record Dword(uint Value) : RegData;
    public sealed record Qword(ulong Value) : RegData;
    public sealed record Sz(string Value) : RegData;
    public sealed record ExpandSz(string Value) : RegData;
    public sealed record MultiSz(IReadOnlyList<string> Values) : RegData;
    public sealed record Binary(byte[] Value) : RegData;
    public sealed record Delete : RegData;

    /// <summary>Kurzform für das README, z. B. <c>1 (dword)</c>.</summary>
    public string Describe() => this switch
    {
        Dword d => d.Value.ToString(CultureInfo.InvariantCulture) + " (dword)",
        Qword q => q.Value.ToString(CultureInfo.InvariantCulture) + " (qword)",
        Sz s => RegFile.Short(s.Value) + " (sz)",
        ExpandSz s => RegFile.Short(s.Value) + " (expand_sz)",
        MultiSz m => "[" + string.Join(", ", m.Values.Select(RegFile.Short)) + "] (multi_sz)",
        Binary b => Model.CanonicalValue.Binary(b.Value) + " (binary)",
        _ => "–",
    };
}

/// <summary>
/// Schreibweise von .reg-Dateien (Registrierungs-Editor Version 5.00). Belegt durch Microsoft KB 310516 „How to add,
/// modify, or delete registry subkeys and values by using a .reg file“ (Syntax, dword:, hex:, hex(2):, hex(7):,
/// Löschen mit „-“; Archivfassung vom 2022-12-23, gesehen 2026-10-18) und durch einen am 2026-10-18 lesend
/// aufgezeichneten <c>reg export</c> (UTF-16 LE mit BOM, CRLF, „\\“ und „\"“ in Zeichenketten, <c>@=</c> für den
/// Standardwert, <c>hex(b):</c> für QWORD, Fortsetzung mit „,\“ und zwei Leerzeichen, Leerzeile am Ende).
/// </summary>
internal static class RegFile
{
    public const string VersionLine = "Windows Registry Editor Version 5.00";

    /// <summary>Höchstlänge einer Zeile vor dem Umbruch der Hex-Daten (wie regedit, etwa 80 Zeichen).</summary>
    private const int LineWidth = 79;

    /// <summary>Zeichenkette in Anführungszeichen; „\“ → „\\“, „"“ → „\"“.</summary>
    public static string Quote(string text) =>
        "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>Name eines Werts: <c>@</c> für den Standardwert, sonst in Anführungszeichen.</summary>
    public static string Name(string valueName) => valueName.Length == 0 ? "@" : Quote(valueName);

    /// <summary>Kopfzeile eines Schlüssels; <paramref name="key"/> ist schon geprüft und beginnt mit HKEY_LOCAL_MACHINE.</summary>
    public static string KeyLine(string key) => "[" + key + "]";

    /// <summary>Eine Wertzeile (bei Hex-Daten mit Fortsetzungszeilen).</summary>
    public static IEnumerable<string> ValueLines(string valueName, RegData data)
    {
        var prefix = Name(valueName) + "=";
        return data switch
        {
            RegData.Dword d => [prefix + "dword:" + d.Value.ToString("x8", CultureInfo.InvariantCulture)],
            RegData.Qword q => Hex(prefix + "hex(b):", BitConverter.IsLittleEndian ? BitConverter.GetBytes(q.Value) : [.. BitConverter.GetBytes(q.Value).Reverse()]),
            RegData.Sz s => [prefix + Quote(s.Value)],
            RegData.ExpandSz s => Hex(prefix + "hex(2):", Encoding.Unicode.GetBytes(s.Value + "\0")),
            RegData.MultiSz m => Hex(prefix + "hex(7):", Encoding.Unicode.GetBytes(string.Concat(m.Values.Select(v => v + "\0")) + "\0")),
            RegData.Binary b => Hex(prefix + "hex:", b.Value),
            RegData.Delete => [prefix + "-"],
            _ => throw new ArgumentOutOfRangeException(nameof(data)),
        };
    }

    /// <summary>Hex-Bytes mit Komma, Umbruch mit „,\“ und zwei Leerzeichen Einzug (wie regedit).</summary>
    private static List<string> Hex(string prefix, byte[] bytes)
    {
        var lines = new List<string>();
        var line = new StringBuilder(prefix);
        for (int i = 0; i < bytes.Length; i++)
        {
            var token = bytes[i].ToString("x2", CultureInfo.InvariantCulture) + (i < bytes.Length - 1 ? "," : "");
            if (line.Length + token.Length + 1 > LineWidth && line.Length > 2 && i > 0)
            {
                lines.Add(line.Append('\\').ToString());
                line.Clear().Append("  ");
            }
            line.Append(token);
        }
        lines.Add(line.ToString());
        return lines;
    }

    /// <summary>Datei: Versionszeile, Hinweis als Kommentar, Abschnitte, Leerzeile am Ende; UTF-16 LE mit BOM, CRLF.</summary>
    public static byte[] Encode(IEnumerable<string> lines)
    {
        var text = string.Join("\r\n", lines) + "\r\n";
        var preamble = Encoding.Unicode.GetPreamble();
        var body = Encoding.Unicode.GetBytes(text);
        return [.. preamble, .. body];
    }

    /// <summary>Kommentarzeile (Strichpunkt am Zeilenanfang); Zeilenumbrüche und Steuerzeichen im Text werden ersetzt.</summary>
    public static string Comment(string text) => "; " + Values.Printable(text, 2000);

    /// <summary>Gekürzte, druckbare Darstellung für das README.</summary>
    internal static string Short(string text) => "\"" + Values.Printable(text, 120) + "\"";
}
