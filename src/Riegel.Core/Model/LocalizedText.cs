// SPDX-License-Identifier: MIT
using System.Text.Json.Serialization;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Model;

/// <summary>
/// Text in mehreren Sprachen (Regeltexte im Katalog, spec 7.10). JSON: <c>{ "en": "…", "de": "…" }</c>.
/// </summary>
/// <param name="Values">Sprachcode (en, de, es, fr, zh-Hans) → Text.</param>
[JsonConverter(typeof(LocalizedTextConverter))]
public sealed record LocalizedText(IReadOnlyDictionary<string, string> Values)
{
    /// <summary>Rückfallsprache, wenn eine Sprache fehlt (Richtlinie 13).</summary>
    public const string FallbackLanguage = "en";

    public IReadOnlyDictionary<string, string> Values { get; init; } = Values ?? new Dictionary<string, string>();

    /// <summary>
    /// Text in <paramref name="language"/>: genau („zh-Hans“), dann die übergeordnete Sprache („de-AT“ → „de“), dann
    /// Englisch, sonst der erste vorhandene Text, sonst "".
    /// </summary>
    public string Get(string? language)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            for (var code = language.Trim(); code.Length > 0;)
            {
                foreach (var (key, text) in Values)
                    if (string.Equals(key, code, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(text)) return text;
                int dash = code.LastIndexOf('-');
                code = dash > 0 ? code[..dash] : "";
            }
        }
        if (Values.TryGetValue(FallbackLanguage, out var english) && !string.IsNullOrWhiteSpace(english)) return english;
        foreach (var key in Values.Keys.Order(StringComparer.Ordinal))
            if (!string.IsNullOrWhiteSpace(Values[key])) return Values[key];
        return "";
    }

    /// <summary>Gibt es einen nicht leeren Text genau in dieser Sprache (ohne Rückfall)?</summary>
    public bool Has(string language) =>
        Values.TryGetValue(language, out var text) && !string.IsNullOrWhiteSpace(text);

    /// <summary>Text aus Paaren Sprache, Text.</summary>
    public static LocalizedText Of(params (string Language, string Text)[] values)
    {
        var dict = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (language, text) in values) dict[language] = text;
        return new LocalizedText(dict);
    }

    /// <summary>Gleichheit über den Inhalt (Reihenfolge egal), nicht über die Referenz des Wörterbuchs.</summary>
    public bool Equals(LocalizedText? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Values.Count != other.Values.Count) return false;
        foreach (var (key, text) in Values)
            if (!other.Values.TryGetValue(key, out var otherText) || !string.Equals(text, otherText, StringComparison.Ordinal)) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var key in Values.Keys.Order(StringComparer.Ordinal))
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(Values[key], StringComparer.Ordinal);
        }
        return hash.ToHashCode();
    }
}
