// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Commands.Resources;

namespace Riegel.Commands.Output;

/// <summary>
/// Mehrzahl in CliStrings (<see cref="Core.Localization.Plural"/>): je Text <c>&lt;Name&gt;_one</c> und
/// <c>&lt;Name&gt;_other</c>, die Regel der Oberflächensprache wählt.
/// </summary>
internal static class Plural
{
    public static string Category(string language, long n) => Core.Localization.Plural.Category(language, n);

    /// <summary>
    /// Text <paramref name="key"/>_one/_other in der Oberflächensprache; {0} ist die Zahl (mit
    /// <paramref name="numberCulture"/> formatiert), weitere Platzhalter aus <paramref name="args"/> (Icinga-Zeile).
    /// </summary>
    public static string Format(string key, long n, CultureInfo numberCulture, params object?[] args)
    {
        var ui = CultureInfo.CurrentUICulture;
        var text = CliStrings.ResourceManager.GetString(key + "_" + Category(ui.Name, n), ui)
                   ?? CliStrings.ResourceManager.GetString(key + "_other", ui)
                   ?? key;
        object?[] all = [n.ToString(numberCulture), .. args];
        return string.Format(numberCulture, text, all);
    }

    /// <summary>Bildschirmtext <paramref name="key"/>_one/_other nach <paramref name="n"/>, Platzhalter wie im Text.</summary>
    public static string Text(string key, long n, params object?[] args) =>
        Core.Localization.Plural.Format(CliStrings.ResourceManager, key, n, args);
}
