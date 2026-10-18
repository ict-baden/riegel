// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Resources;

namespace Riegel.Core.Localization;

/// <summary>
/// Mehrzahl nach den CLDR-Regeln unserer fünf Sprachen (Richtlinie 13 Punkt 3: nicht über „if (n == 1)“ im Text) für
/// alle Sprachdateien (Review docs-ui-2; vorher nur in der Icinga-Zeile). .resx kennt keine ICU-Nachrichten, deshalb je
/// Text zwei Schlüssel <c>&lt;Name&gt;_one</c> und <c>&lt;Name&gt;_other</c>; welche gilt, entscheidet die Regel der
/// Sprache: en, de, es genau 1; fr 0 und 1; zh-Hans immer „other“.
/// </summary>
public static class Plural
{
    /// <summary>CLDR-Kategorie „one“ oder „other“ von <paramref name="n"/> in <paramref name="language"/>.</summary>
    public static string Category(string language, long n) => UiLanguage.Normalize(language) switch
    {
        "fr" => n is 0 or 1 ? "one" : "other",
        "zh-Hans" => "other",
        _ => n == 1 ? "one" : "other",
    };

    /// <summary>
    /// Text <paramref name="key"/>_one bzw. _other aus <paramref name="resources"/> in der Oberflächensprache, mit den
    /// Platzhaltern aus <paramref name="args"/> in der Formatkultur. <paramref name="n"/> wählt nur die Form; die Zahl
    /// selbst steht als Platzhalter in <paramref name="args"/>. Fehlen die Formen, gilt <paramref name="key"/> selbst.
    /// </summary>
    public static string Format(ResourceManager resources, string key, long n, params object?[] args)
    {
        var ui = CultureInfo.CurrentUICulture;
        var text = resources.GetString(key + "_" + Category(ui.Name, n), ui)
                   ?? resources.GetString(key + "_other", ui)
                   ?? resources.GetString(key, ui)
                   ?? key;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    /// <summary>Hat die Sprachdatei Mehrzahlformen für <paramref name="key"/>?</summary>
    public static bool Has(ResourceManager resources, string key) =>
        resources.GetString(key + "_other", CultureInfo.InvariantCulture) is not null;
}
