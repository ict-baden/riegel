// SPDX-License-Identifier: MIT
using System.Globalization;

namespace Riegel.Core.Localization;

/// <summary>
/// Sprache der Oberfläche, der Befehlszeile und der Berichte (Richtlinie 13, spec 7.10; Muster aus Lupe, auf fünf
/// Sprachen erweitert). Reihenfolge: RIEGEL_LANGUAGE → Einstellung des Benutzers → defaults.json → Betriebssystem →
/// Englisch. Fehlt ein Text in einer Sprache, zeigt .NET den englischen aus der neutralen .resx.
/// </summary>
public static class UiLanguage
{
    /// <summary>Umgebungsvariable, die alles übersteuert (Skripte, Tests).</summary>
    public const string EnvironmentVariable = "RIEGEL_LANGUAGE";

    /// <summary>Rückfallsprache, steht in den neutralen *Strings.resx.</summary>
    public const string Fallback = "en";

    /// <summary>Sprachen mit eigener Sprachdatei, in der Reihenfolge der Auswahlliste.</summary>
    public static IReadOnlyList<string> Supported { get; } = ["en", "de", "es", "fr", "zh-Hans"];

    /// <summary>
    /// Sprachen, deren Berichte geprüft sind. Für alle anderen tragen Deckblatt und Fußzeilen „Maschinelle
    /// Vorübersetzung – nicht geprüft“ (spec 7.10).
    /// </summary>
    public static IReadOnlyList<string> ReviewedReportLanguages { get; } = ["en", "de"];

    /// <summary>Sprache des Systems beim Start, bevor Riegel sie überschreibt.</summary>
    public static CultureInfo System { get; } = CultureInfo.CurrentUICulture;

    /// <summary>"" (keine Wahl) oder ein Code aus <see cref="Supported"/> in dessen Schreibweise.</summary>
    public static string Normalize(string? setting)
    {
        var code = setting?.Trim() ?? "";
        if (code.Length == 0) return "";
        var exact = Supported.FirstOrDefault(s => s.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        // „zh“, „zh-CN“, „de-AT“ … auf eine unterstützte Sprache abbilden
        try { return FromCulture(CultureInfo.GetCultureInfo(code)) ?? ""; }
        catch (CultureNotFoundException) { return ""; }
    }

    /// <summary>Die Sprache, die gilt: erst die Wahlen in Reihenfolge, dann das System, sonst Englisch.</summary>
    public static string Resolve(CultureInfo system, params string?[] choices)
    {
        foreach (var choice in choices)
        {
            var chosen = Normalize(choice);
            if (chosen.Length > 0) return chosen;
        }
        return FromCulture(system) ?? Fallback;
    }

    /// <summary>
    /// Stellt die Sprache für alle Threads ein. Vorrang: <see cref="EnvironmentVariable"/>, dann
    /// <paramref name="userSetting"/>, dann <paramref name="machineDefault"/>, dann das System.
    /// </summary>
    public static CultureInfo Apply(string? userSetting, string? machineDefault = null)
    {
        var code = Resolve(System, Environment.GetEnvironmentVariable(EnvironmentVariable), userSetting, machineDefault);
        var culture = CultureInfo.GetCultureInfo(code);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return culture;
    }

    /// <summary>Ist die Berichtssprache geprüft (kein Vermerk „Vorübersetzung“)?</summary>
    public static bool IsReviewedReportLanguage(string language) =>
        ReviewedReportLanguages.Contains(Normalize(language), StringComparer.Ordinal);

    /// <summary>Name einer Sprache in ihr selbst („Deutsch“, „English“, „中文(简体)“), für die Auswahlliste.</summary>
    public static string NativeName(string code)
    {
        var culture = CultureInfo.GetCultureInfo(code);
        var name = culture.NativeName;
        return name.Length == 0 ? code : char.ToUpper(name[0], culture) + name[1..];
    }

    private static string? FromCulture(CultureInfo culture)
    {
        for (var c = culture; !c.Equals(CultureInfo.InvariantCulture); c = c.Parent)
        {
            var match = Supported.FirstOrDefault(s => s.Equals(c.Name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
            // zh-CN → zh-Hans (Parent), zh → zh-Hans (vereinfacht ist unsere chinesische Sprache)
            if (c.Name.Equals("zh", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
        }
        return null;
    }
}
