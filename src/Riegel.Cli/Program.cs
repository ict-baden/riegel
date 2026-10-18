// SPDX-License-Identifier: MIT
using Riegel.Commands;
using Riegel.Core.Localization;
using Riegel.Core.Services;
using Riegel.Core.Settings;

namespace Riegel.Cli;

/// <summary>
/// Konsolenprogramm (spec 4.2): Riegel.Cli.exe unter Windows, riegel-linux-x64/-arm64 unter Linux. Gibt den Exitcode
/// von <see cref="CommandApp.Run(string[], IPlatform, TextWriter, TextWriter)"/> zurück – in PowerShell steht er
/// verlässlich in $LASTEXITCODE, in cmd in %ERRORLEVEL%.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        UiLanguage.Apply(SettingsStore.Load().Language, SettingsStore.LoadMachineDefaults().Language);
#if WINDOWS
        IPlatform platform = Riegel.Platform.Windows.PlatformFactory.Create();
#else
        IPlatform platform = new NullPlatform();
#endif
        return CommandApp.Run(args, platform, Console.Out, Console.Error);
    }
}
