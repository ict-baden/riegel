// SPDX-License-Identifier: MIT
using Riegel.Commands;
using Riegel.Core.Localization;
using Riegel.Core.Services;
using Riegel.Core.Settings;

namespace Riegel.Cli;

/// <summary>
/// Konsolenprogramm (spec 4.2): Riegel.Cli.exe unter Windows, riegel-linux-x64/-arm64 unter Linux. Gibt den Exitcode
/// von <see cref="CommandApp.Run(string[], IPlatform, TextWriter, TextWriter, CancellationToken)"/> zurück – in
/// PowerShell steht er verlässlich in $LASTEXITCODE, in cmd in %ERRORLEVEL% (Konsolen-Subsystem: die Shell wartet).
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        // Linux: ICU und OpenSSL prüfen, bevor irgendetwas Kulturdaten braucht (sonst Abbruch der Laufzeit mit 134)
        var native = NativePrerequisites.Prepare();
        ConsoleOutput.Configure();
        // Ohne ICU liest .NET die Systemsprache nicht – dann aus LANG, mit dem Rang der Systemsprache (zuletzt)
        var machineDefault = SettingsStore.LoadMachineDefaults().Language;
        if (native.IcuMissing && string.IsNullOrWhiteSpace(machineDefault)) machineDefault = NativePrerequisites.LanguageFromEnvironment();
        UiLanguage.Apply(SettingsStore.Load().Language, machineDefault);
        if (NativePrerequisites.Report(native, args, Console.Out, Console.Error) is { } stop) return stop;

        // Strg+C: erst sauber abbrechen (Exitcode 3, RGL-0469), ein zweites Strg+C beendet sofort
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            if (cancel.IsCancellationRequested) return;
            e.Cancel = true;
            cancel.Cancel();
        };

#if WINDOWS
        IPlatform platform = Riegel.Platform.Windows.PlatformFactory.Create();
#else
        IPlatform platform = new NullPlatform();
#endif
        int code = CommandApp.Run(args, platform, Console.Out, Console.Error, cancel.Token);
        Console.Out.Flush();
        Console.Error.Flush();
        return code;
    }
}
