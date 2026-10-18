// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.RegularExpressions;
using Riegel.Core.Localization;
using Riegel.Core.Plants;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Infrastructure;

/// <summary>
/// Mehrzahl (Review docs-ui-2, Richtlinie 13 Punkt 3): Zahlen-Texte kommen als <c>_one</c>/<c>_other</c> und werden
/// über <see cref="Plural"/> gewählt – „1 findings“, „1 exports added“, „1 errors“ und „in 1 days“ gibt es nicht mehr.
/// </summary>
public sealed partial class PluralTests
{
    [Theory]
    [InlineData("en", 1, "one")]
    [InlineData("en", 0, "other")]
    [InlineData("de", 2, "other")]
    [InlineData("es", 1, "one")]
    [InlineData("fr", 0, "one")]
    [InlineData("fr", 1, "one")]
    [InlineData("fr", 2, "other")]
    [InlineData("zh-Hans", 1, "other")]
    public void Kategorie_nach_CLDR(string language, long n, string expected) => Assert.Equal(expected, Plural.Category(language, n));

    /// <summary>Jede Form „_one“ hat ihr Gegenstück (_other oder den Grundschlüssel) in jeder Sprache.</summary>
    [Theory]
    [MemberData(nameof(LocalizationTests.Owners), MemberType = typeof(LocalizationTests))]
    public void Jede_Einzahl_hat_eine_Mehrzahl(string folder, string name)
    {
        foreach (var code in UiLanguage.Supported)
        {
            var file = Path.Combine(RepoPaths.Root, folder, code == UiLanguage.Fallback ? name + ".resx" : $"{name}.{code}.resx");
            var texts = LocalizationTests.Load(file);
            foreach (var key in texts.Keys.Where(k => k.EndsWith("_one", StringComparison.Ordinal)))
            {
                var stem = key[..^"_one".Length];
                Assert.True(texts.ContainsKey(stem + "_other") || texts.ContainsKey(stem), $"{Path.GetFileName(file)}: {key} ohne {stem}_other");
            }
        }
    }

    /// <summary>Mit n = 1 formatiert, steht in keiner englischen Einzahl ein Plural-Substantiv hinter der 1.</summary>
    [Theory]
    [MemberData(nameof(LocalizationTests.Owners), MemberType = typeof(LocalizationTests))]
    public void Einzahl_mit_eins_formatiert_ist_richtiges_Englisch(string folder, string name)
    {
        var texts = LocalizationTests.Load(Path.Combine(RepoPaths.Root, folder, name + ".resx"));
        foreach (var (key, text) in texts.Where(t => t.Key.EndsWith("_one", StringComparison.Ordinal)))
        {
            var formatted = string.Format(CultureInfo.InvariantCulture, text.Replace("{{", "(", StringComparison.Ordinal).Replace("}}", ")", StringComparison.Ordinal),
                "1", "1", "1", "1", "1", "1");
            Assert.DoesNotMatch(PluralNoun(), formatted);
        }
    }

    /// <summary>Die Texte, die das Review nannte, haben Formen für 1.</summary>
    [Theory]
    [InlineData("src/Riegel/Resources/UiStrings", "Machines_SummaryWorse")]
    [InlineData("src/Riegel/Resources/UiStrings", "Format_DaysAgo")]
    [InlineData("src/Riegel/Resources/UiStrings", "Start_Machines")]
    [InlineData("src/Riegel/Resources/UiStrings", "Main_PlantDetail")]
    [InlineData("src/Riegel/Resources/UiStrings", "Notice_Approved")]
    [InlineData("src/Riegel.Commands/Resources/CliStrings", "Cli_ImportDone")]
    [InlineData("src/Riegel.Commands/Resources/CliStrings", "Cli_TemplateImported")]
    [InlineData("src/Riegel.Commands/Resources/CliStrings", "Cli_CountErrors")]
    [InlineData("src/Riegel.Core/Resources/WorkflowStrings", "Error_RGL0439")]
    public void Genannte_Texte_haben_eine_Einzahl(string file, string key)
    {
        var texts = LocalizationTests.Load(Path.Combine(RepoPaths.Root, file + ".resx"));
        Assert.True(texts.ContainsKey(key + "_one"), key);
    }

    [Fact]
    public void Ablauf_in_einem_Tag_steht_in_der_Einzahl()
    {
        var previous = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal("Expires on 2026-11-05 (in 1 day).", RglCodesWorkflow.Message(RglCodesWorkflow.Expiring, new LocalDate(new DateOnly(2026, 11, 5)), 1));
            Assert.Equal("Expires on 2026-11-05 (in 18 days).", RglCodesWorkflow.Message(RglCodesWorkflow.Expiring, new LocalDate(new DateOnly(2026, 11, 5)), 18));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("Läuft am 2026-11-05 ab (in 1 Tag).", RglCodesWorkflow.Message(RglCodesWorkflow.Expiring, new LocalDate(new DateOnly(2026, 11, 5)), 1));
        }
        finally
        {
            (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = previous;
        }
    }

    [GeneratedRegex(@"\b1 (days|machines|exports|entries|errors|warnings|findings|files|rules|results|steps|exceptions)\b")]
    private static partial Regex PluralNoun();
}
