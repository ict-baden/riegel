// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Evaluation;

/// <summary>
/// Lesehilfen für die Rohwerte der Exporte (spec 7.3). Namen von Eigenschaften werden ohne Groß/klein verglichen
/// (WMI- und Registry-Namen sind es auch). Nichts hier wirft bei fremden Formen: unbekannt ist dann null.
/// </summary>
internal static class JsonRead
{
    /// <summary>Eigenschaft eines Objekts, zuerst genau, dann ohne Groß/klein.</summary>
    public static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) return false;
        if (obj.TryGetProperty(name, out value)) return true;
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        return false;
    }

    /// <summary>
    /// Eigenschaft über einen Pfad mit „.“, z. B. die Ausgabe einer WMI-Methode je Zeile
    /// (<c>GetKeyProtectors.KeyProtectorTypes</c>); WMI-Eigenschaften enthalten nie einen Punkt.
    /// </summary>
    public static bool TryGetPath(JsonElement obj, string path, out JsonElement value)
    {
        value = obj;
        foreach (var part in path.Split('.'))
            if (!TryGet(value, part, out value)) return false;
        return true;
    }

    /// <summary>Eigenschaft oder null (fehlend oder JSON null).</summary>
    public static JsonElement? Get(JsonElement obj, string name) =>
        TryGet(obj, name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? v : null;

    public static bool IsNull(JsonElement? value) => value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

    public static string? String(JsonElement obj, string name) =>
        Get(obj, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    public static bool? Bool(JsonElement obj, string name) => Get(obj, name) switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        _ => null,
    };

    /// <summary>Ganzzahl aus JSON-Zahl oder Dezimaltext (uint64 kommen als Text, spec 7.3).</summary>
    public static long? Integer(JsonElement obj, string name) => Get(obj, name) is { } v ? Integer(v) : null;

    public static long? Integer(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)) return l;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }

    /// <summary>Zahl als decimal (auch uint64), sonst null.</summary>
    public static decimal? Number(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String)
        {
            var text = v.GetString()?.Trim() ?? "";
            if (text.Length > 0 && decimal.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var s)) return s;
        }
        return null;
    }

    /// <summary>Zeitpunkt (ISO 8601 mit Zone) oder Datum (yyyy-MM-dd, als UTC-Mitternacht).</summary>
    public static DateTimeOffset? Time(JsonElement? v)
    {
        if (v is not { ValueKind: JsonValueKind.String } s) return null;
        var text = s.GetString() ?? "";
        if (UtcDateTimeOffsetConverter.TryParse(text, out var t)) return t;
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return null;
    }

    /// <summary>Elemente einer Liste; kein Array → leer.</summary>
    public static IEnumerable<JsonElement> Array(JsonElement? v) =>
        v is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : [];

    /// <summary>Zeilen eines WMI-Eintrags: <c>rows</c>, bei Methoden <c>out</c> als eine Zeile; null = falsche Form.</summary>
    public static IReadOnlyList<JsonElement>? Rows(JsonElement value)
    {
        if (TryGet(value, "rows", out var rows))
            return rows.ValueKind == JsonValueKind.Array ? [.. rows.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object)] : null;
        if (TryGet(value, "out", out var output))
            return output.ValueKind == JsonValueKind.Object ? [output] : null;
        return null;
    }
}
