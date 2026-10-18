// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Commands.Resources;
using Riegel.Core.Localization;

namespace Riegel.Commands.Output;

/// <summary>
/// Mehrzahl nach den CLDR-Regeln unserer fünf Sprachen (Richtlinie 13 Punkt 3: nicht über „if (n == 1)“ im Text).
/// .resx kennt keine ICU-Nachrichten, deshalb je Text zwei Schlüssel <c>&lt;Name&gt;_one</c> und <c>&lt;Name&gt;_other</c>;
/// welche gilt, entscheidet die Regel der Sprache (en/de/es: genau 1; fr: 0 und 1; zh-Hans: immer „other“).
/// </summary>
internal static class Plural
{
    public static string Category(string language, long n) => UiLanguage.Normalize(language) switch
    {
        "fr" => n is 0 or 1 ? "one" : "other",
        "zh-Hans" => "other",
        _ => n == 1 ? "one" : "other",
    };

    /// <summary>
    /// Text <paramref name="key"/>_one/_other in der Oberflächensprache; {0} ist die Zahl (mit
    /// <paramref name="numberCulture"/> formatiert), weitere Platzhalter aus <paramref name="args"/>.
    /// </summary>
    public static string Format(string key, long n, CultureInfo numberCulture, params object?[] args)
    {
        var ui = CultureInfo.CurrentUICulture;
        var category = Category(ui.Name, n);
        var text = CliStrings.ResourceManager.GetString(key + "_" + category, ui)
                   ?? CliStrings.ResourceManager.GetString(key + "_other", ui)
                   ?? key;
        object?[] all = [n.ToString(numberCulture), .. args];
        return string.Format(numberCulture, text, all);
    }
}
