// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Cli;
using Riegel.Commands;
using Riegel.Core.Localization;
using Riegel.Core.Settings;
using Riegel.Demo;
using Riegel.Platform.Windows;
using Riegel.Resources;

namespace Riegel;

/// <summary>
/// Einstieg (Muster aus Zutritt). Ohne Argumente startet die Oberfläche. Mit einem Befehl läuft Riegel.exe als
/// Befehlszeile ganz ohne WPF und hängt sich an die Konsole des Aufrufers; in Skripten Riegel.Cli.exe verwenden, auf das
/// die Konsole wartet (spec 4.2). Gerüst: <c>--demo</c> und <c>--screenshots</c> melden „nicht implementiert“,
/// <c>--demo-report &lt;Ordner&gt;</c> schreibt Platzhalter unter den echten Dateinamen.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        UiLanguage.Apply(SettingsStore.Load().Language, SettingsStore.LoadMachineDefaults().Language);
        if (args.Length == 0) return RunGui();

        ConsoleSession.Attach();
        var first = args[0].Trim();
        if (Is(first, "--demo") || Is(first, "--screenshots"))
        {
            Console.WriteLine(string.Format(CultureInfo.CurrentCulture, UiStrings.App_NotImplemented, first));
            return CommandApp.Ok;
        }
        if (Is(first, "--demo-report"))
        {
            if (args.Length != 2) return CommandApp.Run(["--demo-report"], PlatformFactory.Create(), Console.Out, Console.Error);
            var folder = Path.GetFullPath(args[1]);
            DemoReport.Write(folder);
            Console.WriteLine(string.Format(CultureInfo.CurrentCulture, UiStrings.App_DemoReportWritten, folder));
            return CommandApp.Ok;
        }
        return CommandApp.Run(args, PlatformFactory.Create(), Console.Out, Console.Error);
    }

    private static int RunGui()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run(new UI.MainWindow());
    }

    private static bool Is(string arg, string name) => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);
}
