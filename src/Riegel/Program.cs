// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Cli;
using Riegel.Commands;
using Riegel.Core.Localization;
using Riegel.Core.Settings;
using Riegel.Demo;
using Riegel.Interop;
using Riegel.Platform.Windows;
using Riegel.Session;

namespace Riegel;

/// <summary>
/// Einstieg (Muster aus Zutritt, spec 7.7). Ohne Argumente startet die Oberfläche; <c>--demo</c> mit der Demo-Anlage;
/// <c>--screenshots &lt;Ordner&gt; [--theme light|dark|both]</c> rendert jede Seite hell und dunkel; das versteckte
/// <c>--demo-report &lt;Ordner&gt;</c> schreibt jedes Dokument aus spec 5 von der Demo-Anlage. Alles andere – auch das
/// versteckte <c>collect --handoff &lt;token&gt;</c> der Rechteerhöhung – geht an die Befehlszeile (<see cref="CommandApp.Run(string[], Core.Services.IPlatform, TextWriter, TextWriter)"/>):
/// Riegel.exe hängt sich dafür an die Konsole des Aufrufers. In Skripten Riegel.Cli.exe nehmen (spec 4.2).
/// </summary>
internal static class Program
{
    /// <summary>AppUserModelID für die Taskleiste (spec 6).</summary>
    public const string AppUserModelId = "ICT-Baden.Riegel";

    [STAThread]
    public static int Main(string[] args)
    {
        UiLanguage.Apply(SettingsStore.Load().Language, SettingsStore.LoadMachineDefaults().Language);
        var options = StartOptions.Parse(args);
        if (options.Mode == StartMode.Screenshots)
        {
            // Datum und Zahlen in der Sprache der Bilder – hier und nicht erst im async-Ablauf: CurrentCulture ist
            // async-lokal und spränge nach dem ersten await für Ereignisse aus der Nachrichtenschleife zurück.
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentUICulture;
        }
        switch (options.Mode)
        {
            case StartMode.CommandLine:
                return RunCommandLine(args);
            case StartMode.DemoReport:
                ConsoleSession.Attach();
                return DemoReport.Run(options.Folder, PlatformFactory.Create(), Console.Out, Console.Error);
            case StartMode.Usage:
                ConsoleSession.Attach();
                Console.Error.WriteLine(options.Problem);
                return CommandApp.Unknown;
        }

        _ = Native.SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        SingleInstance? instance = null;
        if (options.Mode is StartMode.Gui or StartMode.Demo)
        {
            instance = SingleInstance.TryAcquire(out bool denied);
            if (instance is null)
            {
                // Riegel läuft schon in dieser Sitzung: das Fenster nach vorn holen und beenden. Läuft es mit
                // Administratorrechten, erreicht dieser Prozess es nicht – dann ein Hinweis statt eines stillen Endes
                if (!SingleInstance.SignalFirst() && denied)
                    System.Windows.MessageBox.Show(Riegel.UI.Loc.T("App_AlreadyRunningElevated"), "Riegel",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return CommandApp.Ok;
            }
        }
        var app = new App(new StartOptionsForApp(options), instance);
        app.InitializeComponent();
        return app.Run();
    }

    private static int RunCommandLine(string[] args)
    {
        ConsoleSession.Attach();
        int code = CommandApp.Run(args, PlatformFactory.Create(), Console.Out, Console.Error);
        // In Skripten wartet die Konsole nicht auf Riegel.exe (Fensterprogramm): Hinweis auf Riegel.Cli.exe in der Hilfe
        if (args.Length > 0 && args[0].Trim().ToLowerInvariant() is "--help" or "--hilfe" or "-h" or "/?")
        {
            Console.Out.WriteLine();
            Console.Out.WriteLine(Riegel.UI.Loc.T("App_UseCliInScripts"));
        }
        return code;
    }
}

/// <summary>Wie Riegel.exe startet.</summary>
internal enum StartMode { Gui, Demo, Screenshots, DemoReport, CommandLine, Usage }

/// <summary>Auswertung der Argumente (ohne Seiteneffekte, testbar).</summary>
internal sealed record StartOptions(StartMode Mode, string Folder = "", string Theme = "both", string? Problem = null)
{
    public static StartOptions Parse(string[] args)
    {
        if (args.Length == 0) return new(StartMode.Gui);
        var first = args[0].Trim();
        if (Is(first, "--demo")) return args.Length == 1 ? new(StartMode.Demo) : Bad(args[1]);
        if (Is(first, "--demo-report"))
        {
            if (args.Length != 2 || args[1].StartsWith("--", StringComparison.Ordinal)) return Bad(args.Length > 1 ? args[1] : first);
            return new(StartMode.DemoReport, Path.GetFullPath(args[1]));
        }
        if (Is(first, "--screenshots"))
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal)) return Bad(first);
            var theme = "both";
            if (args.Length == 4 && Is(args[2], "--theme")) theme = args[3].Trim().ToLowerInvariant();
            else if (args.Length != 2) return Bad(args[2]);
            if (theme is not ("light" or "dark" or "both")) return Bad(theme);
            return new(StartMode.Screenshots, Path.GetFullPath(args[1]), theme);
        }
        // Alles andere (Befehle, --help, --version, collect --handoff …) gehört der Befehlszeile
        return new(StartMode.CommandLine);
    }

    private static StartOptions Bad(string argument) =>
        new(StartMode.Usage, Problem: $"{RglCodesCli()}: {string.Format(CultureInfo.CurrentCulture, Riegel.UI.Loc.T("App_UnknownArgument"), argument)}");

    private static string RglCodesCli() => Riegel.Commands.RglCodesCli.UsageError;

    private static bool Is(string arg, string name) => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Startangaben für die WPF-Anwendung.</summary>
internal sealed record StartOptionsForApp(AppMode Mode, string ScreenshotFolder, string Theme)
{
    public StartOptionsForApp(StartOptions o) : this(o.Mode switch
    {
        StartMode.Demo => AppMode.Demo,
        StartMode.Screenshots => AppMode.Screenshots,
        _ => AppMode.Normal,
    }, o.Folder, o.Theme)
    {
    }
}
