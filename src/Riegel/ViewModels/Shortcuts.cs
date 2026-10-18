// SPDX-License-Identifier: MIT
using System.Windows.Input;

namespace Riegel.ViewModels;

/// <summary>Was ein Tastenkürzel auslöst (spec 7.8).</summary>
internal enum ShortcutAction { None, OpenPlant, Import, CheckThisPc, Exception, Report, Help, Search, Page }

/// <summary>
/// Tastenkürzel der App: Strg+O Anlage öffnen, Strg+I Importieren, F5 Diesen PC prüfen, Strg+E Ausnahme, Strg+R Bericht,
/// F1 Hilfe, Strg+F Suche, Strg+1 … Strg+9 Seiten. Verlangt wird genau Strg (oder gar nichts für F-Tasten): AltGr meldet
/// Windows als Strg+Alt – AltGr+E („€“), AltGr+Q („@“) und AltGr+7…0 („{[]}“) auf deutschen Tastaturen dürfen nichts
/// auslösen, wenn jemand in einem Textfeld tippt (Richtlinie 02).
/// </summary>
internal static class Shortcuts
{
    public static (ShortcutAction Action, int Page) Resolve(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.None)
        {
            return key switch
            {
                Key.F5 => (ShortcutAction.CheckThisPc, 0),
                Key.F1 => (ShortcutAction.Help, 0),
                _ => (ShortcutAction.None, 0),
            };
        }
        if (modifiers != ModifierKeys.Control) return (ShortcutAction.None, 0);
        return key switch
        {
            Key.O => (ShortcutAction.OpenPlant, 0),
            Key.I => (ShortcutAction.Import, 0),
            Key.E => (ShortcutAction.Exception, 0),
            Key.R => (ShortcutAction.Report, 0),
            Key.F => (ShortcutAction.Search, 0),
            >= Key.D1 and <= Key.D9 => (ShortcutAction.Page, key - Key.D1),
            >= Key.NumPad1 and <= Key.NumPad9 => (ShortcutAction.Page, key - Key.NumPad1),
            _ => (ShortcutAction.None, 0),
        };
    }
}
