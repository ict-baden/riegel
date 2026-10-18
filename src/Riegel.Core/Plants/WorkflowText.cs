// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.Localization;
using Riegel.Core.Resources;

namespace Riegel.Core.Plants;

/// <summary>
/// Texte des Pakets workflow in einer bestimmten Sprache (Berichtsdaten folgen der Berichtssprache, nicht der
/// Oberfläche). Fehlt ein Text, liefert .NET den englischen aus der neutralen .resx.
/// </summary>
internal static class WorkflowText
{
    /// <summary>Kultur zu einem Sprachcode (en, de, es, fr, zh-Hans …); unbekannt → Englisch.</summary>
    public static CultureInfo Culture(string? language)
    {
        var code = UiLanguage.Normalize(language);
        if (code.Length == 0) code = UiLanguage.Fallback;
        return CultureInfo.GetCultureInfo(code);
    }

    /// <summary>Sprachcode der aktuellen Oberfläche (für Detailtexte ohne eigene Sprachangabe).</summary>
    public static string CurrentLanguage()
    {
        var code = UiLanguage.Normalize(CultureInfo.CurrentUICulture.Name);
        return code.Length == 0 ? UiLanguage.Fallback : code;
    }

    /// <summary>Text aus WorkflowStrings in <paramref name="language"/>.</summary>
    public static string Get(string key, string? language) =>
        WorkflowStrings.ResourceManager.GetString(key, Culture(language)) ?? key;

    /// <summary>Text aus WorkflowStrings mit Platzhaltern (invariant formatiert).</summary>
    public static string Format(string key, string? language, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, Get(key, language), args);

    /// <summary>Text aus CommonStrings in <paramref name="language"/>.</summary>
    public static string Common(string key, string? language) =>
        CommonStrings.ResourceManager.GetString(key, Culture(language)) ?? key;
}
