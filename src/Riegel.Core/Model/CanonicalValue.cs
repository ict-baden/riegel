// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Riegel.Core.Resources;

namespace Riegel.Core.Model;

/// <summary>
/// Kanonische Darstellung von Werten (spec 7.3), gemeinsam für Expected, Actual und acceptedValue:
/// DWORD/QWORD dezimal, Wahrheitswerte <c>true</c>/<c>false</c>, Texte unverändert, multi_sz als <c>["a","b"]</c>,
/// Binärdaten als <c>hex:&lt;erste 32 Bytes&gt;…</c>, fehlend als null (Anzeige „(nicht gesetzt)“ / „(not set)“).
/// <c>0</c> und <c>0x0</c> sind derselbe Wert. Geschwärzte Werte heißen <c>redacted</c> bzw. <c>redacted:empty</c>.
/// </summary>
public static class CanonicalValue
{
    /// <summary>Kanonische Form eines geschwärzten, nicht leeren Werts.</summary>
    public const string Redacted = "redacted";

    /// <summary>Kanonische Form eines geschwärzten, leeren Werts.</summary>
    public const string RedactedEmpty = "redacted:empty";

    /// <summary>Höchstens so viele Bytes zeigt die Binärdarstellung.</summary>
    public const int BinaryPrefixBytes = 32;

    private static readonly JsonWriterOptions CompactWriter = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Ein JSON-Wert (aus einer Regel, einem WMI-Eintrag oder einer API) in kanonischer Form; null/fehlend → null.
    /// Objekte werden kompakt mit sortierten Schlüsseln geschrieben.
    /// </summary>
    public static string? Render(JsonElement? value)
    {
        if (value is not { } v) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => null,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => Number(v),
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Array or JsonValueKind.Object => Compact(v),
            _ => null,
        };
    }

    /// <summary>
    /// Ein Registry-Wert in der Snapshot-Kodierung <c>{ "t": …, "v": …, "redacted"?, "empty"?, "truncated"? }</c>.
    /// </summary>
    public static string? RenderRegistry(JsonElement registryValue)
    {
        if (registryValue.ValueKind != JsonValueKind.Object) return Render(registryValue);
        if (registryValue.TryGetProperty("redacted", out var redacted) && redacted.ValueKind == JsonValueKind.True)
            return registryValue.TryGetProperty("empty", out var empty) && empty.ValueKind == JsonValueKind.True ? RedactedEmpty : Redacted;
        var type = registryValue.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        if (!registryValue.TryGetProperty("v", out var v)) return null;
        bool truncated = registryValue.TryGetProperty("truncated", out var tr) && tr.ValueKind == JsonValueKind.True;
        return RenderRegistry(type, v, truncated);
    }

    /// <summary>Registry-Wert nach Typ: dword (Zahl), qword (Dezimaltext), sz/expand_sz, multi_sz (Liste), binary (base64).</summary>
    public static string? RenderRegistry(string? type, JsonElement v, bool truncated = false)
    {
        switch (type)
        {
            case "dword":
            case "qword":
                return v.ValueKind switch
                {
                    JsonValueKind.Number => Number(v),
                    JsonValueKind.String => TryParseInteger(v.GetString() ?? "", out var n) ? n.ToString(CultureInfo.InvariantCulture) : v.GetString(),
                    _ => Render(v),
                };
            case "binary":
                if (v.ValueKind != JsonValueKind.String) return Render(v);
                try
                {
                    var bytes = Convert.FromBase64String(v.GetString() ?? "");
                    return Binary(bytes, truncated);
                }
                catch (FormatException)
                {
                    return v.GetString();
                }
            default:
                return Render(v);
        }
    }

    /// <summary><c>hex:&lt;erste 32 Bytes&gt;</c> plus „…“, wenn es mehr Bytes gibt oder der Wert abgeschnitten war.</summary>
    public static string Binary(ReadOnlySpan<byte> bytes, bool truncated = false)
    {
        var shown = bytes.Length > BinaryPrefixBytes ? bytes[..BinaryPrefixBytes] : bytes;
        var hex = Convert.ToHexString(shown).ToLowerInvariant();
        return "hex:" + hex + (bytes.Length > BinaryPrefixBytes || truncated ? "…" : "");
    }

    /// <summary>
    /// Gleicher Wert? Ganzzahlen werden als Zahl verglichen (dezimal oder 0x-hex, „0“ = „0x0“), Texte ordinal.
    /// null ist nur gleich null.
    /// </summary>
    public static bool AreEqual(string? a, string? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (TryParseInteger(a, out var x) && TryParseInteger(b, out var y)) return x == y;
        return string.Equals(a, b, StringComparison.Ordinal);
    }

    /// <summary>Ganzzahl dezimal („42“) oder hexadezimal („0x2A“), ohne Vorzeichen, invariant.</summary>
    public static bool TryParseInteger(string text, out ulong value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Für die Anzeige: null → „(not set)“ in der aktuellen Sprache, sonst der kanonische Text.</summary>
    public static string Display(string? canonical) => canonical ?? CommonStrings.Value_NotSet;

    private static string Number(JsonElement v)
    {
        if (v.TryGetUInt64(out var u)) return u.ToString(CultureInfo.InvariantCulture);
        if (v.TryGetInt64(out var l)) return l.ToString(CultureInfo.InvariantCulture);
        if (v.TryGetDecimal(out var d)) return d.ToString(CultureInfo.InvariantCulture);
        return v.GetRawText();
    }

    private static string Compact(JsonElement v)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CompactWriter)) WriteSorted(writer, v);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSorted(Utf8JsonWriter writer, JsonElement v)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var p in v.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(p.Name);
                    WriteSorted(writer, p.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var e in v.EnumerateArray()) WriteSorted(writer, e);
                writer.WriteEndArray();
                break;
            default:
                v.WriteTo(writer);
                break;
        }
    }
}
