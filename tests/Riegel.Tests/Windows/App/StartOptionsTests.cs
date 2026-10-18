// SPDX-License-Identifier: MIT
using System.Windows.Input;
using Riegel.Session;
using Riegel.ViewModels;

namespace Riegel.Tests.Windows.App;

/// <summary>Start von Riegel.exe (spec 7.7, 7.8): Modus aus den Argumenten, Tastenkürzel ohne AltGr.</summary>
public sealed class StartOptionsTests
{
    [Fact]
    public void Ohne_Argumente_startet_die_Oberflaeche()
    {
        Assert.Equal(StartMode.Gui, StartOptions.Parse([]).Mode);
    }

    [Theory]
    [InlineData("--demo")]
    [InlineData("--DEMO")]
    public void Demo(string arg)
    {
        Assert.Equal(StartMode.Demo, StartOptions.Parse([arg]).Mode);
        Assert.Equal(AppMode.Demo, new StartOptionsForApp(StartOptions.Parse([arg])).Mode);
    }

    [Fact]
    public void Screenshots_mit_und_ohne_Design()
    {
        var both = StartOptions.Parse(["--screenshots", "out"]);
        Assert.Equal(StartMode.Screenshots, both.Mode);
        Assert.Equal("both", both.Theme);
        Assert.True(Path.IsPathRooted(both.Folder));
        Assert.Equal("dark", StartOptions.Parse(["--screenshots", "out", "--theme", "Dark"]).Theme);
        Assert.Equal("light", StartOptions.Parse(["--screenshots", "out", "--theme", "light"]).Theme);
        Assert.Equal(AppMode.Screenshots, new StartOptionsForApp(both).Mode);
    }

    [Theory]
    [InlineData("--screenshots")]
    [InlineData("--screenshots", "--theme", "dark")]
    [InlineData("--screenshots", "out", "--theme", "blau")]
    [InlineData("--screenshots", "out", "--farbe", "dark")]
    [InlineData("--demo-report")]
    [InlineData("--demo-report", "a", "b")]
    [InlineData("--demo", "extra")]
    public void Falsche_Angaben_ergeben_einen_Hinweis(params string[] args)
    {
        var o = StartOptions.Parse(args);
        Assert.Equal(StartMode.Usage, o.Mode);
        Assert.StartsWith("RGL-", o.Problem);
    }

    [Fact]
    public void Demo_Bericht()
    {
        var o = StartOptions.Parse(["--demo-report", "out/demo"]);
        Assert.Equal(StartMode.DemoReport, o.Mode);
        Assert.True(Path.IsPathRooted(o.Folder));
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("--help")]
    [InlineData("check", "--plant", "x")]
    [InlineData("collect", "--handoff", "abc")]
    [InlineData("--info")]
    public void Alles_andere_geht_an_die_Befehlszeile(params string[] args)
    {
        Assert.Equal(StartMode.CommandLine, StartOptions.Parse(args).Mode);
    }

    [Fact]
    public void Kennungen_der_Einzelinstanz_und_der_Taskleiste()
    {
        Assert.Equal("ICT-Baden.Riegel", Program.AppUserModelId);
        Assert.Equal(@"Local\ICT-Baden.Riegel.SingleInstance", SingleInstance.MutexName);
    }

    [Theory]
    [InlineData(Key.O, "OpenPlant")]
    [InlineData(Key.I, "Import")]
    [InlineData(Key.E, "Exception")]
    [InlineData(Key.R, "Report")]
    [InlineData(Key.F, "Search")]
    public void Strg_Kuerzel(Key key, string expected)
    {
        Assert.Equal(Enum.Parse<ShortcutAction>(expected), Shortcuts.Resolve(key, ModifierKeys.Control).Action);
    }

    [Fact]
    public void F_Tasten_und_Seiten()
    {
        Assert.Equal(ShortcutAction.CheckThisPc, Shortcuts.Resolve(Key.F5, ModifierKeys.None).Action);
        Assert.Equal(ShortcutAction.Help, Shortcuts.Resolve(Key.F1, ModifierKeys.None).Action);
        Assert.Equal((ShortcutAction.Page, 0), Shortcuts.Resolve(Key.D1, ModifierKeys.Control));
        Assert.Equal((ShortcutAction.Page, 8), Shortcuts.Resolve(Key.D9, ModifierKeys.Control));
        Assert.Equal((ShortcutAction.Page, 2), Shortcuts.Resolve(Key.NumPad3, ModifierKeys.Control));
    }

    [Theory]
    [InlineData(Key.E)]  // AltGr+E = €
    [InlineData(Key.Q)]  // AltGr+Q = @
    [InlineData(Key.D7)] // AltGr+7 = {
    [InlineData(Key.D8)] // AltGr+8 = [
    [InlineData(Key.D9)] // AltGr+9 = ]
    [InlineData(Key.O)]
    [InlineData(Key.I)]
    [InlineData(Key.R)]
    public void AltGr_loest_nichts_aus(Key key)
    {
        // Windows meldet AltGr als Strg+Alt
        Assert.Equal(ShortcutAction.None, Shortcuts.Resolve(key, ModifierKeys.Control | ModifierKeys.Alt).Action);
    }

    [Fact]
    public void Andere_Umschalter_loesen_nichts_aus()
    {
        Assert.Equal(ShortcutAction.None, Shortcuts.Resolve(Key.O, ModifierKeys.Control | ModifierKeys.Shift).Action);
        Assert.Equal(ShortcutAction.None, Shortcuts.Resolve(Key.F5, ModifierKeys.Control).Action);
        Assert.Equal(ShortcutAction.None, Shortcuts.Resolve(Key.O, ModifierKeys.None).Action);
    }
}
