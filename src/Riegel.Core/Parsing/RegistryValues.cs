// SPDX-License-Identifier: MIT
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Riegel.Core.Parsing;

/// <summary>
/// Registry-Werttypen nach ihren Zahlen aus WinNT.h (REG_NONE 0, REG_SZ 1, REG_EXPAND_SZ 2, REG_BINARY 3, REG_DWORD 4,
/// REG_DWORD_BIG_ENDIAN 5, REG_LINK 6, REG_MULTI_SZ 7, REG_QWORD 11) und ihre Namen in der Snapshot-Kodierung
/// (spec 7.3: dword, qword, sz, expand_sz, multi_sz, binary, none, unknown). secedit schreibt dieselben Zahlen
/// (research-technik §3.1: „4,“ = DWORD, „1,“ = SZ, „7,“ = MULTI_SZ, „3,“ = BINARY).
/// </summary>
internal static class RegTypes
{
    internal const uint None = 0, Sz = 1, ExpandSz = 2, Binary = 3, Dword = 4, DwordBigEndian = 5, Link = 6, MultiSz = 7, Qword = 11;

    /// <summary>Name in der Snapshot-Kodierung; alles ohne eigene Darstellung heißt „unknown“.</summary>
    internal static string Name(uint type) => type switch
    {
        None => "none",
        Sz => "sz",
        ExpandSz => "expand_sz",
        Binary => "binary",
        Dword => "dword",
        MultiSz => "multi_sz",
        Qword => "qword",
        _ => "unknown",
    };
}

/// <summary>
/// Ein Registry-Wert, wie ihn die Leser von .reg-Dateien, reg query und Registry.pol liefern. Wird in die
/// Snapshot-Kodierung geschrieben: DWORD als Zahl, QWORD als Dezimaltext, multi_sz als Liste, Binärdaten als base64
/// (höchstens 4096 Bytes, sonst die ersten 4096 mit <c>truncated</c> und <c>len</c>), Texte höchstens
/// <c>maxStringChars</c> Zeichen (sonst abgeschnitten mit <c>truncated</c>).
/// </summary>
internal sealed class RegValue
{
    /// <summary>Höchstens so viele Binärbytes stehen im Export (collection-plan limits.maxBinaryBytes).</summary>
    internal const int MaxBinaryBytes = 4096;

    private RegValue(string type, object? data)
    {
        Type = type;
        Data = data;
    }

    /// <summary>Typname der Snapshot-Kodierung.</summary>
    internal string Type { get; }

    /// <summary>uint (dword), ulong (qword), string (sz, expand_sz), string[] (multi_sz), byte[] (binary, none, unknown).</summary>
    internal object? Data { get; }

    /// <summary>Wert ist geschwärzt (Kennwort o. ä.); <see cref="Data"/> ist dann null.</summary>
    internal bool Redacted { get; private init; }

    /// <summary>Bei geschwärzten Werten: war der Wert leer? null = unbekannt (z. B. „DefaultPassword=present“).</summary>
    internal bool? RedactedEmpty { get; private init; }

    internal static RegValue Dword(uint v) => new("dword", v);
    internal static RegValue Qword(ulong v) => new("qword", v);
    internal static RegValue String(string v, bool expand = false) => new(expand ? "expand_sz" : "sz", v);
    internal static RegValue MultiString(IReadOnlyList<string> v) => new("multi_sz", v.ToArray());
    internal static RegValue Bytes(string type, byte[] v) => new(type, v);

    /// <summary>Geschwärzter Wert (spec 7.3).</summary>
    internal static RegValue RedactedValue(string type, bool? empty) => new(type, null) { Redacted = true, RedactedEmpty = empty };

    /// <summary>
    /// Wert aus Typzahl und Rohbytes (Registry.pol, hex(n) in .reg-Dateien). Texte sind UTF-16 LE
    /// (<paramref name="unicode"/>) oder – in REGEDIT4-Dateien – einzelne Bytes (Latin-1).
    /// </summary>
    /// <exception cref="FormatException">Länge oder Kodierung passt nicht zum Typ.</exception>
    internal static RegValue FromRaw(uint type, ReadOnlySpan<byte> data, bool unicode = true)
    {
        switch (type)
        {
            case RegTypes.Dword:
                if (data.Length != 4) throw new FormatException("REG_DWORD braucht 4 Bytes.");
                return Dword(BinaryPrimitives.ReadUInt32LittleEndian(data));
            case RegTypes.Qword:
                if (data.Length != 8) throw new FormatException("REG_QWORD braucht 8 Bytes.");
                return Qword(BinaryPrimitives.ReadUInt64LittleEndian(data));
            case RegTypes.Sz:
            case RegTypes.ExpandSz:
                return String(DecodeString(data, unicode).TrimEnd('\0'), type == RegTypes.ExpandSz);
            case RegTypes.MultiSz:
                return MultiString(SplitMulti(DecodeString(data, unicode)));
            default:
                return Bytes(RegTypes.Name(type), data.ToArray());
        }
    }

    private static string DecodeString(ReadOnlySpan<byte> data, bool unicode)
    {
        if (!unicode) return Encoding.Latin1.GetString(data);
        if (data.Length % 2 != 0) throw new FormatException("UTF-16-Text mit ungerader Länge.");
        try
        {
            return TextFiles.StrictUtf16.GetString(data);
        }
        catch (DecoderFallbackException e)
        {
            throw new FormatException("Ungültiges UTF-16.", e);
        }
    }

    /// <summary>REG_MULTI_SZ: durch NUL getrennt, abgeschlossen mit zwei NUL; leere Einträge am Ende fallen weg.</summary>
    private static List<string> SplitMulti(string text)
    {
        var parts = text.Split('\0').ToList();
        while (parts.Count > 0 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
        return parts;
    }

    /// <summary>Ist der Wert leer (für <c>empty</c> beim Schwärzen)?</summary>
    internal bool IsEmpty => Data switch
    {
        null => true,
        string s => s.Length == 0,
        string[] a => a.Length == 0,
        byte[] b => b.Length == 0,
        _ => false,
    };

    /// <summary>Kanonischer Vergleich mit einem anderen Wert (Typ und Daten).</summary>
    internal bool SameAs(RegValue other)
    {
        if (Redacted || other.Redacted) return false;
        if (!string.Equals(Type, other.Type, StringComparison.Ordinal)) return false;
        return (Data, other.Data) switch
        {
            (uint a, uint b) => a == b,
            (ulong a, ulong b) => a == b,
            (string a, string b) => string.Equals(a, b, StringComparison.Ordinal),
            (string[] a, string[] b) => a.SequenceEqual(b, StringComparer.Ordinal),
            (byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b),
            _ => false,
        };
    }

    /// <summary>Schreibt den Wert als <c>{ "t": …, "v": … }</c> (Snapshot-Kodierung).</summary>
    internal void WriteTo(Utf8JsonWriter w, int maxStringChars)
    {
        w.WriteStartObject();
        w.WriteString("t", Type);
        if (Redacted)
        {
            w.WriteBoolean("redacted", true);
            if (RedactedEmpty is { } empty) w.WriteBoolean("empty", empty);
            w.WriteEndObject();
            return;
        }
        bool truncated = false;
        long? length = null;
        switch (Data)
        {
            case uint d:
                w.WriteNumber("v", d);
                break;
            case ulong q:
                w.WriteString("v", q.ToString(CultureInfo.InvariantCulture));
                break;
            case string s:
                if (s.Length > maxStringChars) { s = s[..maxStringChars]; truncated = true; }
                w.WriteString("v", s);
                break;
            case string[] list:
                w.WritePropertyName("v");
                w.WriteStartArray();
                foreach (var entry in list)
                {
                    var e = entry;
                    if (e.Length > maxStringChars) { e = e[..maxStringChars]; truncated = true; }
                    w.WriteStringValue(e);
                }
                w.WriteEndArray();
                break;
            case byte[] bytes:
                if (bytes.Length > MaxBinaryBytes)
                {
                    truncated = true;
                    length = bytes.Length;
                    w.WriteString("v", Convert.ToBase64String(bytes, 0, MaxBinaryBytes));
                }
                else
                {
                    w.WriteString("v", Convert.ToBase64String(bytes));
                }
                break;
        }
        if (truncated) w.WriteBoolean("truncated", true);
        if (length is { } len) w.WriteNumber("len", len);
        w.WriteEndObject();
    }

    /// <summary>Als JSON-Wert (für Regeln aus Baselines: dword als Zahl, qword als Dezimaltext, multi_sz als Liste).</summary>
    internal JsonElement? ExpectedJson() => Data switch
    {
        uint d => Model.JsonValues.Number(d),
        ulong q => Model.JsonValues.Text(q.ToString(CultureInfo.InvariantCulture)),
        string s => Model.JsonValues.Text(s),
        string[] list => Model.JsonValues.Texts(list),
        byte[] bytes => Model.JsonValues.Text(Convert.ToBase64String(bytes)),
        _ => null,
    };
}

/// <summary>Ein Registry-Schlüssel mit seinen Werten, wie ihn ein Leser zusammensetzt (Reihenfolge wie gelesen).</summary>
internal sealed class RegKeyData(string path)
{
    /// <summary>Vollständiger Pfad <c>HKLM\…</c> in der Schreibweise der Quelle.</summary>
    internal string Path { get; } = path;

    /// <summary>Werte nach Name (Groß/klein egal), in Lesereihenfolge.</summary>
    internal List<KeyValuePair<string, RegValue>> Values { get; } = [];

    /// <summary>Unterschlüssel (Namen); null = unbekannt (z. B. aus reg query /v, das keine Unterschlüssel nennt).</summary>
    internal SortedSet<string>? Subkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nur diese Werte wurden gelesen (spec 7.3 valueFilter); null = alle.</summary>
    internal List<string>? ValueFilter { get; set; }

    /// <summary>Wert setzen (ein gleichnamiger Wert, Groß/klein egal, wird ersetzt).</summary>
    internal void Set(string name, RegValue value)
    {
        int index = Values.FindIndex(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) Values[index] = new(Values[index].Key, value);
        else Values.Add(new(name, value));
    }

    /// <summary>Als Wert eines <c>reg:</c>-Eintrags (spec 7.3), mit Schwärzung der Geheimnisse.</summary>
    internal JsonElement ToItemValue(int maxStringChars) => Model.JsonValues.Build(w =>
    {
        w.WriteStartObject();
        w.WritePropertyName("values");
        w.WriteStartObject();
        bool snmp = Secrets.IsSnmpCommunitiesKey(Path);
        int n = 0;
        foreach (var (name, value) in Values)
        {
            var effectiveName = snmp ? "redacted-" + (++n).ToString(CultureInfo.InvariantCulture) : name;
            w.WritePropertyName(effectiveName);
            var effective = !value.Redacted && Secrets.ShouldRedact(name, value.Type) ? RegValue.RedactedValue(value.Type, value.IsEmpty) : value;
            effective.WriteTo(w, maxStringChars);
        }
        w.WriteEndObject();
        if (Subkeys is { } subkeys)
        {
            w.WritePropertyName("subkeys");
            w.WriteStartArray();
            foreach (var s in subkeys) w.WriteStringValue(s);
            w.WriteEndArray();
        }
        if (ValueFilter is { } filter)
        {
            w.WritePropertyName("valueFilter");
            w.WriteStartArray();
            foreach (var f in filter) w.WriteStringValue(f);
            w.WriteEndArray();
        }
        w.WriteEndObject();
    });
}
