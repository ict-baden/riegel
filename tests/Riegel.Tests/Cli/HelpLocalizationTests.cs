// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Commands;
using Riegel.Commands.Commands;
using Riegel.Commands.Resources;
using Riegel.Core.Localization;
using Riegel.Tests.Infrastructure;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Cli;

/// <summary>
/// Hilfe in fünf Sprachen (Paket cli, Abnahme; spec 7.10): Übersicht und Hilfe je Befehl in jeder Sprache vollständig,
/// übersetzt (nicht der englische Text) und mit allen Befehlen; Statuswörter der Icinga-Zeile invariant.
/// </summary>
public sealed class HelpLocalizationTests
{
    public static TheoryData<string> Languages() => [.. UiLanguage.Supported];

    [Theory]
    [MemberData(nameof(Languages))]
    public void Uebersicht_nennt_jeden_Befehl_und_die_globalen_Optionen(string language)
    {
        using var _ = new UiCulture(language);
        foreach (var word in new[] { "--help", "--hilfe" })
        {
            var result = Cli.Run(word);
            Assert.Equal(0, result.Exit);
            foreach (var command in CommandApp.CommandNames)
                Assert.Contains(command, result.Out, StringComparison.Ordinal);
            foreach (var option in new[] { "--version", "--info", "--licenses", "--lizenzen", "--nur-lesen", "--read-only" })
                Assert.Contains(option, result.Out, StringComparison.Ordinal);
            Assert.Contains(CommandContext.ProgramName, result.Out, StringComparison.Ordinal);
            foreach (var state in new[] { "OK", "WARNING", "CRITICAL", "UNKNOWN" })
                Assert.Contains(state, result.Out, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Jeder_Befehl_hat_eine_Hilfe_in_jeder_Sprache(string language)
    {
        using var _ = new UiCulture(language);
        foreach (var command in CommandApp.CommandNames)
        {
            var result = Cli.Run(command, "--help");
            Assert.True(result.Exit == 0, $"{command}: {result}");
            Assert.Contains(CommandContext.ProgramName + " " + command, result.Out, StringComparison.Ordinal);
            Assert.DoesNotContain("{0}", result.Out, StringComparison.Ordinal);
            var hilfe = Cli.Run(command, "--hilfe");
            Assert.Equal(result.Out, hilfe.Out);
        }
    }

    [Fact]
    public void Jeder_Befehl_hat_einen_Hilfetext()
    {
        Assert.Equal(CommandApp.CommandNames.Order(StringComparer.Ordinal), InfoCommands.CommandHelpKeys.Keys.Order(StringComparer.Ordinal));
        foreach (var key in InfoCommands.CommandHelpKeys.Values)
            Assert.False(string.IsNullOrWhiteSpace(CliStrings.ResourceManager.GetString(key, CultureInfo.InvariantCulture)), key);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("zh-Hans")]
    public void Hilfetexte_sind_uebersetzt_und_nicht_englisch(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var keys = InfoCommands.CommandHelpKeys.Values.Concat(["Help_Overview", "Help_ExitCodes", "Help_ScriptsNote", "Cli_Description"]);
        foreach (var key in keys)
        {
            var english = CliStrings.ResourceManager.GetString(key, CultureInfo.InvariantCulture);
            var translated = CliStrings.ResourceManager.GetString(key, culture);
            Assert.False(string.IsNullOrWhiteSpace(translated), $"{language}: {key}");
            Assert.NotEqual(english, translated);
        }
    }

    [Fact]
    public void Jede_Sprachdatei_hat_dieselben_Schluessel_ohne_Rueckfall_auf_Englisch()
    {
        var folder = Path.Combine(RepoPaths.Root, "src", "Riegel.Commands", "Resources");
        var english = LocalizationTests.Load(Path.Combine(folder, "CliStrings.resx"));
        foreach (var language in UiLanguage.Supported.Where(l => l != UiLanguage.Fallback))
        {
            var culture = CultureInfo.GetCultureInfo(language);
            foreach (var key in english.Keys)
            {
                // Der Text kommt aus der Sprachdatei selbst, nicht aus der englischen Rückfalldatei
                var own = CliStrings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)?.GetString(key);
                Assert.False(string.IsNullOrWhiteSpace(own), $"{language}: {key}");
            }
        }
    }

    [Fact]
    public void Deutsche_Hilfe_spricht_Deutsch()
    {
        using var _ = new UiCulture("de");
        var result = Cli.Run("--hilfe");
        Assert.Contains("Aufruf:", result.Out, StringComparison.Ordinal);
        Assert.Contains("Prüfen und überwachen", result.Out, StringComparison.Ordinal);
        Assert.Contains("Anlagenordner", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Fehlermeldungen_sind_uebersetzt_der_Code_bleibt()
    {
        string en, de;
        using (new UiCulture("en")) en = Cli.Run("gibtsnicht").Err;
        using (new UiCulture("de")) de = Cli.Run("gibtsnicht").Err;
        Assert.StartsWith("RGL-0450: Unknown command", en, StringComparison.Ordinal);
        Assert.StartsWith("RGL-0450: Unbekannter Befehl", de, StringComparison.Ordinal);
    }

    [Fact]
    public void Jeder_Fehlercode_der_Befehlszeile_hat_einen_Text_im_Bereich_450_bis_499()
    {
        foreach (var code in RglCodesCli.All)
        {
            int n = int.Parse(code[4..], CultureInfo.InvariantCulture);
            Assert.InRange(n, RglCodesCli.First, RglCodesCli.Last);
            foreach (var language in UiLanguage.Supported)
                Assert.False(string.IsNullOrWhiteSpace(CliStrings.ResourceManager.GetString("Error_" + code.Replace("-", ""), CultureInfo.GetCultureInfo(language))), code);
        }
        Assert.Equal(RglCodesCli.All.Count, RglCodesCli.All.Distinct().Count());
    }
}
