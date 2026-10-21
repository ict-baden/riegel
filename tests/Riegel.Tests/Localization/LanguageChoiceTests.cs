// SPDX-License-Identifier: MIT
using Riegel.Core.Localization;

namespace Riegel.Tests.Localization;

/// <summary>Sprachauswahl: geprüfte Sprachen ohne Zusatz, maschinell übersetzte mit dem Zusatz in der Sprache selbst.</summary>
public sealed class LanguageChoiceTests
{
    [Theory]
    [InlineData("es", "traducción automática")]
    [InlineData("fr", "traduction automatique")]
    [InlineData("zh-Hans", "机器翻译")]
    public void Maschinell_uebersetzte_Sprachen_sind_in_der_Auswahl_gekennzeichnet(string code, string note)
    {
        var name = UiLanguage.ChoiceName(code);
        Assert.StartsWith(UiLanguage.NativeName(code), name, StringComparison.Ordinal);
        Assert.EndsWith("(" + note + ")", name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void Gepruefte_Sprachen_tragen_keinen_Zusatz(string code) =>
        Assert.Equal(UiLanguage.NativeName(code), UiLanguage.ChoiceName(code));
}
