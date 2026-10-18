// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Riegel.Core.Model.Json;

/// <summary>
/// Aufzählungen als camelCase-Text (<c>"keyMissing"</c>), nie als Zahl (spec 7.2). Abweichende Namen (z. B.
/// <c>"expand_sz"</c>) stehen per <see cref="JsonStringEnumMemberNameAttribute"/> am Wert.
/// </summary>
public sealed class CamelCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    where TEnum : struct, Enum;

/// <summary>
/// Zeitpunkte immer als UTC in ISO 8601 mit „Z“ (<c>2026-10-18T09:30:00Z</c>, Sekundenbruchteile nur wenn vorhanden).
/// Gelesen wird jede ISO-8601-Angabe mit Zeitzone; ohne Zeitzone ist sie ungültig, weil sonst unklar wäre, welche Uhr gilt.
/// </summary>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Zeitpunkt muss eine Zeichenkette sein.");
        var text = reader.GetString()!;
        if (!TryParse(text, out var value)) throw new JsonException("Zeitpunkt ist kein ISO-8601-Wert mit Zeitzone.");
        return value;
    }

    private static readonly string[] Formats = ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];

    /// <summary>ISO 8601 mit Zeitzone („Z“ oder ±hh:mm), Ergebnis in UTC.</summary>
    public static bool TryParse(string text, out DateTimeOffset value)
    {
        value = default;
        if (!HasZone(text)) return false;
        if (!DateTimeOffset.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
        value = parsed.ToUniversalTime();
        return true;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Format(value));

    /// <summary>Kanonische Textform, auch für Dateinamen-unabhängige Ausgaben (CSV, Berichte).</summary>
    public static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);

    private static bool HasZone(string text)
    {
        if (text.EndsWith('Z') || text.EndsWith('z')) return true;
        int t = text.IndexOf('T', StringComparison.OrdinalIgnoreCase);
        if (t < 0) return false;
        var time = text.AsSpan(t + 1);
        return time.LastIndexOfAny('+', '-') > 0;
    }
}

/// <summary>
/// Bitflags als Liste von camelCase-Namen in aufsteigender Bitfolge (<c>["regression","fromDefault"]</c>); keine
/// Flags = leere Liste. So bleibt die Ausgabe stabil und lässt sich per Schema prüfen.
/// </summary>
public abstract class FlagsArrayConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    private static readonly (long Bit, string Name)[] Members = BuildMembers();

    private static (long, string)[] BuildMembers()
    {
        var list = new List<(long, string)>();
        foreach (var value in Enum.GetValues<TEnum>())
        {
            long bit = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (bit == 0 || (bit & (bit - 1)) != 0) continue;
            list.Add((bit, JsonNamingPolicy.CamelCase.ConvertName(value.ToString())));
        }
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return [.. list];
    }

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Flags müssen eine Liste sein.");
        long result = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("Flag muss eine Zeichenkette sein.");
            var name = reader.GetString();
            bool found = false;
            foreach (var (bit, memberName) in Members)
            {
                if (!string.Equals(memberName, name, StringComparison.Ordinal)) continue;
                result |= bit;
                found = true;
                break;
            }
            if (!found) throw new JsonException($"Unbekanntes Flag: {name}");
        }
        return (TEnum)Enum.ToObject(typeof(TEnum), result);
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        long bits = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        writer.WriteStartArray();
        foreach (var (bit, name) in Members)
            if ((bits & bit) != 0) writer.WriteStringValue(name);
        writer.WriteEndArray();
    }

    /// <summary>Namen der gesetzten Flags (camelCase, aufsteigend), z. B. für CSV und Textausgabe.</summary>
    public static IReadOnlyList<string> Names(TEnum value)
    {
        long bits = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        var names = new List<string>();
        foreach (var (bit, name) in Members)
            if ((bits & bit) != 0) names.Add(name);
        return names;
    }
}

/// <summary>JSON-Form von <see cref="ResultFlags"/>.</summary>
public sealed class ResultFlagsConverter : FlagsArrayConverter<ResultFlags>;

/// <summary>JSON-Form von <see cref="RunFlags"/>.</summary>
public sealed class RunFlagsConverter : FlagsArrayConverter<RunFlags>;

/// <summary>JSON-Form von <see cref="Reports.RegisterFlags"/>.</summary>
public sealed class RegisterFlagsConverter : FlagsArrayConverter<Reports.RegisterFlags>;

/// <summary>
/// <see cref="LocalizedText"/> als einfaches Objekt Sprache → Text (<c>{ "en": "…", "de": "…" }</c>), Schlüssel sortiert.
/// </summary>
public sealed class LocalizedTextConverter : JsonConverter<LocalizedText>
{
    public override LocalizedText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Text muss ein Objekt Sprache → Text sein.");
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Sprachcode erwartet.");
            var language = reader.GetString()!;
            if (!reader.Read() || reader.TokenType != JsonTokenType.String) throw new JsonException($"Text für {language} muss eine Zeichenkette sein.");
            if (!values.TryAdd(language, reader.GetString()!)) throw new JsonException($"Sprache {language} doppelt.");
        }
        return new LocalizedText(values);
    }

    public override void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (language, text) in value.Values.OrderBy(p => p.Key, StringComparer.Ordinal))
            writer.WriteString(language, text);
        writer.WriteEndObject();
    }
}
