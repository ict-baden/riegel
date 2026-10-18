// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Windows.Markup;
using Riegel.Core.Resources;

namespace Riegel.UI;

/// <summary>
/// Text aus UiStrings in XAML: <c>{l:T Key}</c> (Richtlinie 13: kein Text fest im Code). Die Sprache steht beim Start
/// fest; ein Sprachwechsel baut das Fenster neu auf. Ein fehlender Schlüssel erscheint als „[Key]“ (und fällt im Test
/// <c>XamlKeysTests</c> auf).
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}

/// <summary>Texte der Oberfläche (UiStrings) und gemeinsame Texte (CommonStrings) für ViewModels.</summary>
public static class Loc
{
    /// <summary>
    /// Eigener ResourceManager statt der erzeugten Klasse <c>UiStrings</c>: Der zweite XAML-Übersetzungslauf (wpftmp)
    /// kennt die erzeugte Klasse nicht.
    /// </summary>
    public static readonly System.Resources.ResourceManager Strings = new("Riegel.Resources.UiStrings", typeof(Loc).Assembly);

    /// <summary>Text der Oberfläche; fehlt er, „[Key]“.</summary>
    public static string T(string key) => Strings.GetString(key, CultureInfo.CurrentUICulture) ?? $"[{key}]";

    /// <summary>Text mit Platzhaltern in der Kultur der Oberfläche.</summary>
    public static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    /// <summary>Gemeinsamer Text aus Riegel.Core (Status, Schwere, Kategorien, Flags …).</summary>
    public static string C(string key) => CommonStrings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
