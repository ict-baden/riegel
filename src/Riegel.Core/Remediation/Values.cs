// SPDX-License-Identifier: MIT
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Riegel.Core.Model;

namespace Riegel.Core.Remediation;

/// <summary>Warum ein alter Wert nicht in die Rücknahme kommt (Schlüssel des README-Texts).</summary>
internal enum OmitReason { NotCollected, ControlCharacters, Redacted, Truncated, WrongType, UnsupportedType, TooLong, EmptyListEntry }

/// <summary>Alter Wert eines Registry-Werts laut Export.</summary>
internal abstract record OldValue
{
    /// <summary>Wert war nachweislich nicht gesetzt: Rücknahme löscht ihn wieder.</summary>
    public sealed record NotSet : OldValue;

    /// <summary>Wert vorhanden und typgeprüft.</summary>
    public sealed record Present(RegData Data) : OldValue;

    /// <summary>Wert unbekannt oder nicht sicher schreibbar: nicht in die Rücknahme, Hinweis im README.</summary>
    public sealed record Omitted(OmitReason Reason) : OldValue;
}

/// <summary>
/// Prüfung von Schlüsseln, Wertnamen und Daten für die Abhilfe-Dateien (spec 7.12 „generated files are attack
/// surface“): Schlüssel und Namen nur aus dem Katalog und nur in einer engen Schreibweise; alte Werte aus dem Export
/// streng nach Typ (DWORD/QWORD nur numerisch, Zeichenketten ohne Steuerzeichen).
/// </summary>
internal static partial class Values
{
    /// <summary>Riegel-eigene Grenze für Zeichenketten in den Dateien (längere Werte prüft man von Hand).</summary>
    public const int MaxString = 16_384;

    /// <summary>Riegel-eigene Grenze für Schlüsselteile und Wertnamen.</summary>
    public const int MaxName = 255;

    /// <summary>Binärdaten höchstens so lang (der Export hält 4096 Bytes, spec 7.3).</summary>
    public const int MaxBinary = 4096;

    /// <summary>Ein Schlüsselteil: druckbares ASCII ohne „[“, „]“, „"“ und „\“.</summary>
    [GeneratedRegex(@"^[\x20-\x21\x23-\x5A\x5E-\x7E]{1,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeySegment();

    /// <summary>
    /// Registry-Pfad aus dem Katalog als <c>HKEY_LOCAL_MACHINE\…</c>; null, wenn er nicht HKLM ist oder Zeichen enthält,
    /// die eine .reg-Datei verfälschen könnten.
    /// </summary>
    public static string? MachineKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        string rest;
        if (key.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase)) rest = key[5..];
        else if (key.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase)) rest = key[19..];
        else return null;
        var parts = rest.Split('\\');
        if (parts.Length == 0 || parts.Any(p => !KeySegment().IsMatch(p) || p.Trim().Length != p.Length)) return null;
        return @"HKEY_LOCAL_MACHINE\" + string.Join('\\', parts);
    }

    /// <summary>Gleicher Schlüssel (ohne Groß/klein, HKLM = HKEY_LOCAL_MACHINE)?</summary>
    public static string Normal(string key) =>
        (key.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase) ? @"HKLM\" + key[19..] : key).TrimEnd('\\').ToLowerInvariant();

    /// <summary>Wertname aus dem Katalog: "" (Standardwert) oder ohne Steuerzeichen und höchstens 255 Zeichen.</summary>
    public static bool IsValueName(string? name) => name is not null && name.Length <= MaxName && IsClean(name);

    /// <summary>
    /// Keine Steuerzeichen (C0, DEL, C1 – also auch CR, LF, NUL), keine Zeilen-/Absatztrenner U+2028/U+2029 und keine
    /// einzelnen Surrogate.
    /// </summary>
    public static bool IsClean(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsControl(c) || (int)c is 0x2028 or 0x2029 || char.IsLowSurrogate(c)) return false;
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
                i++;
            }
        }
        return true;
    }

    /// <summary>Neuer Wert aus dem Katalog; null, wenn Typ und Daten nicht zusammenpassen oder unsicher sind.</summary>
    public static RegData? FromCatalogue(RegValueType? type, JsonElement? data, out OmitReason? why)
    {
        why = null;
        if (type is null || data is not { } d) { why = OmitReason.WrongType; return null; }
        switch (type.Value)
        {
            case RegValueType.Dword:
                if (d.ValueKind == JsonValueKind.Number && d.TryGetUInt32(out var u)) return new RegData.Dword(u);
                if (d.ValueKind == JsonValueKind.String && CanonicalValue.TryParseInteger(d.GetString() ?? "", out var du) && du <= uint.MaxValue)
                    return new RegData.Dword((uint)du);
                break;
            case RegValueType.Qword:
                if (d.ValueKind == JsonValueKind.Number && d.TryGetUInt64(out var q)) return new RegData.Qword(q);
                if (d.ValueKind == JsonValueKind.String && CanonicalValue.TryParseInteger(d.GetString() ?? "", out var qu)) return new RegData.Qword(qu);
                break;
            case RegValueType.Sz:
            case RegValueType.ExpandSz:
                if (d.ValueKind == JsonValueKind.String && Text(d.GetString(), out var s, ref why))
                    return type == RegValueType.Sz ? new RegData.Sz(s!) : new RegData.ExpandSz(s!);
                return null;
            case RegValueType.MultiSz:
                if (d.ValueKind == JsonValueKind.Array && List(d, out var list, ref why)) return new RegData.MultiSz(list!);
                return null;
            case RegValueType.Binary:
                if (d.ValueKind == JsonValueKind.String && Base64(d.GetString(), out var bytes)) return new RegData.Binary(bytes!);
                break;
        }
        why ??= OmitReason.WrongType;
        return null;
    }

    /// <summary>
    /// Alter Wert aus dem Export (<c>reg:</c>-Eintrag, Kodierung spec 7.3). Nur Ok/ValueMissing/KeyMissing in einem
    /// vollständig erfassten Bereich gelten als Beweis; alles andere ist „nicht erfasst“.
    /// </summary>
    public static OldValue FromSnapshot(Snapshot s, string catalogueKey, string valueName)
    {
        var itemKey = ItemKeys.Registry(@"HKLM\" + Normal(catalogueKey)[5..]);
        var item = s.Items.FirstOrDefault(i => ItemKeys.Same(i.Key, itemKey));
        if (item is null || item.Truncated || !Covered(s, catalogueKey)) return new OldValue.Omitted(OmitReason.NotCollected);
        switch (item.Status)
        {
            case ItemStatus.KeyMissing:
                return new OldValue.NotSet();
            case ItemStatus.Ok:
            case ItemStatus.ValueMissing:
                break;
            default:
                return new OldValue.Omitted(OmitReason.NotCollected);
        }
        if (item.Value is not { ValueKind: JsonValueKind.Object } value) return item.Status == ItemStatus.ValueMissing
            ? new OldValue.NotSet() : new OldValue.Omitted(OmitReason.NotCollected);
        // Nur bestimmte Werte gelesen (Filter des Plans): ein anderer Name ist unbekannt
        if (value.TryGetProperty("valueFilter", out var filter) && filter.ValueKind == JsonValueKind.Array
            && !filter.EnumerateArray().Any(f => f.ValueKind == JsonValueKind.String && string.Equals(f.GetString(), valueName, StringComparison.OrdinalIgnoreCase)))
            return new OldValue.Omitted(OmitReason.NotCollected);
        if (!value.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object)
            return new OldValue.Omitted(OmitReason.NotCollected);
        JsonElement? found = null;
        foreach (var p in values.EnumerateObject())
            if (string.Equals(p.Name, valueName, StringComparison.OrdinalIgnoreCase)) { found = p.Value; break; }
        if (found is not { } v) return new OldValue.NotSet();
        return Typed(v);
    }

    private static OldValue Typed(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object) return new OldValue.Omitted(OmitReason.WrongType);
        if (v.TryGetProperty("redacted", out var red) && red.ValueKind == JsonValueKind.True) return new OldValue.Omitted(OmitReason.Redacted);
        if (v.TryGetProperty("truncated", out var tr) && tr.ValueKind == JsonValueKind.True) return new OldValue.Omitted(OmitReason.Truncated);
        var type = v.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        if (!v.TryGetProperty("v", out var data)) return new OldValue.Omitted(OmitReason.WrongType);
        OmitReason? why = null;
        switch (type)
        {
            case "dword":
                return data.ValueKind == JsonValueKind.Number && data.TryGetUInt32(out var u)
                    ? new OldValue.Present(new RegData.Dword(u)) : new OldValue.Omitted(OmitReason.WrongType);
            case "qword":
                if (data.ValueKind == JsonValueKind.String && ulong.TryParse(data.GetString(), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var q))
                    return new OldValue.Present(new RegData.Qword(q));
                return data.ValueKind == JsonValueKind.Number && data.TryGetUInt64(out var qn)
                    ? new OldValue.Present(new RegData.Qword(qn)) : new OldValue.Omitted(OmitReason.WrongType);
            case "sz":
            case "expand_sz":
                if (data.ValueKind != JsonValueKind.String) return new OldValue.Omitted(OmitReason.WrongType);
                return Text(data.GetString(), out var s, ref why)
                    ? new OldValue.Present(type == "sz" ? new RegData.Sz(s!) : new RegData.ExpandSz(s!))
                    : new OldValue.Omitted(why!.Value);
            case "multi_sz":
                if (data.ValueKind != JsonValueKind.Array) return new OldValue.Omitted(OmitReason.WrongType);
                return List(data, out var list, ref why) ? new OldValue.Present(new RegData.MultiSz(list!)) : new OldValue.Omitted(why!.Value);
            case "binary":
                return data.ValueKind == JsonValueKind.String && Base64(data.GetString(), out var bytes)
                    ? new OldValue.Present(new RegData.Binary(bytes!)) : new OldValue.Omitted(OmitReason.WrongType);
            default:
                return new OldValue.Omitted(OmitReason.UnsupportedType);
        }
    }

    /// <summary>Liegt der Schlüssel in einem vollständig erfassten Bereich ohne verweigerte Unterschlüssel?</summary>
    private static bool Covered(Snapshot s, string catalogueKey)
    {
        var key = Normal(catalogueKey);
        foreach (var c in s.Coverage)
        {
            var root = Normal(c.Root);
            bool inside = key == root || (c.Recursive && key.StartsWith(root + "\\", StringComparison.Ordinal));
            if (!inside) continue;
            if (!c.Complete) return false;
            if (c.DeniedSubkeys.Any(d => key == Normal(d) || key.StartsWith(Normal(d) + "\\", StringComparison.Ordinal))) return false;
        }
        return true;
    }

    private static bool Text(string? text, out string? value, ref OmitReason? why)
    {
        value = text;
        if (text is null) { why = OmitReason.WrongType; return false; }
        if (!IsClean(text)) { why = OmitReason.ControlCharacters; return false; }
        if (text.Length > MaxString) { why = OmitReason.TooLong; return false; }
        return true;
    }

    private static bool List(JsonElement array, out List<string>? values, ref OmitReason? why)
    {
        values = [];
        int total = 0;
        foreach (var e in array.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.String) { why = OmitReason.WrongType; values = null; return false; }
            var s = e.GetString() ?? "";
            // Ein leerer Eintrag würde die Liste in REG_MULTI_SZ beenden
            if (s.Length == 0) { why = OmitReason.EmptyListEntry; values = null; return false; }
            if (!Text(s, out _, ref why)) { values = null; return false; }
            total += s.Length + 1;
            if (total > MaxString) { why = OmitReason.TooLong; values = null; return false; }
            values.Add(s);
        }
        return true;
    }

    private static bool Base64(string? text, out byte[]? bytes)
    {
        bytes = null;
        if (text is null) return false;
        try { bytes = Convert.FromBase64String(text); }
        catch (FormatException) { return false; }
        return bytes.Length <= MaxBinary;
    }

    /// <summary>Druckbare, gekürzte Form für README und Kommentare: Steuerzeichen werden zu U+FFFD.</summary>
    public static string Printable(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(Math.Min(text.Length, max + 1));
        for (int i = 0; i < text.Length && sb.Length < max; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb.Append(c).Append(text[++i]);
                continue;
            }
            sb.Append(char.IsControl(c) || char.IsSurrogate(c) || (int)c is 0x2028 or 0x2029 ? (char)0xFFFD : c);
        }
        if (text.Length > sb.Length) sb.Append('…');
        return sb.ToString();
    }
}
