// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Runtime.InteropServices;
using Riegel.Core.About;
using Riegel.Core.Diagnostics;
using Riegel.Session;
using Riegel.ViewModels;

namespace Riegel;

/// <summary>
/// Protokoll und Diagnosepaket der App (Richtlinie 10, Muster aus Zutritt): Start mit Version und System, unbehandelte
/// Fehler, Angaben für das Diagnosepaket. Inhalte von Anlagen (Rechnernamen, Werte, Register) kommen nie hinein.
/// </summary>
internal static class AppDiagnostics
{
    private static bool _hooked;

    public static void Start(string mode)
    {
        Log.Start(Log.DefaultFolder);
        Log.Info($"Start {AppInfo.Name} {AppInfo.VersionWithBuild} – {mode} – {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}");
        if (_hooked) return;
        _hooked = true;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error(e.IsTerminating ? "Absturz" : "Unbehandelter Fehler", e.ExceptionObject as Exception, withStack: true);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unbeobachteter Fehler einer Hintergrundaufgabe", e.Exception, withStack: true);
            e.SetObserved();
        };
    }

    /// <summary>Angaben für das Diagnosepaket: Version, Build, Sammler, Katalog, Modus, Zahl der Rechner – keine Namen.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Info(AppSession? session)
    {
        var info = new List<KeyValuePair<string, string>>
        {
            new("Version", AppInfo.Version),
            new("Build", AppInfo.Commit ?? "unknown"),
            new("Publisher", AppInfo.Publisher),
            new("Collector", AppInfo.CollectorVersion),
            new("Executable", Environment.ProcessPath is { } exe ? SettingsViewModel.Shorten(exe) : "unknown"),
        };
        if (session is not null)
        {
            info.Add(new("Mode", session.Mode.ToString()));
            info.Add(new("Elevated", session.IsElevated ? "yes" : "no"));
            info.Add(new("Plant open", session.HasPlant ? "yes" : "no"));
            if (session.Plant is { } plant)
            {
                info.Add(new("Read-only mode", plant.ReadOnly ? "yes" : "no"));
                info.Add(new("Demo", plant.IsDemo ? "yes" : "no"));
                info.Add(new("Machines", plant.Machines.Count.ToString(CultureInfo.InvariantCulture)));
                info.Add(new("Catalogue", $"{plant.Catalogue.Id} {plant.Catalogue.Version} ({plant.Catalogue.Rules.Count} rules)"));
            }
        }
        return info;
    }
}
