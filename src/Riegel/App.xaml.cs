// SPDX-License-Identifier: MIT
using System.Windows;
using System.Windows.Threading;
using Riegel.Cli;
using Riegel.Core.Diagnostics;
using Riegel.Core.Localization;
using Riegel.Core.Model;
using Riegel.Core.Settings;
using Riegel.Platform.Windows;
using Riegel.Session;
using Riegel.UI;
using Riegel.UI.Theme;
using Riegel.ViewModels;

namespace Riegel;

/// <summary>
/// WPF-Anwendung: Oberfläche normal oder mit Demo-Anlage; <c>--screenshots</c> rendert außerhalb des Bildschirms
/// (Muster aus Zutritt). Unerwartete Fehler werden gemeldet statt abzustürzen.
/// </summary>
public partial class App : Application
{
    private readonly StartOptionsForApp _options;
    private readonly SingleInstance? _instance;
    private AppSession? _session;

    internal App(StartOptionsForApp options, SingleInstance? instance = null)
    {
        _options = options;
        _instance = instance;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        AppDiagnostics.Start(_options.Mode switch
        {
            AppMode.Demo => "Oberfläche mit Demo",
            AppMode.Screenshots => "Screenshots",
            _ => "Oberfläche",
        });

        if (_options.Mode == AppMode.Screenshots)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ConsoleSession.Attach();
            Dispatcher.InvokeAsync(RunScreenshotsAsync);
            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var user = SettingsStore.Load();
        var machine = SettingsStore.LoadMachineDefaults();
        _session = new AppSession(_options.Mode, PlatformFactory.Create(), user, machine);
        AsyncCommand.Failed = _session.Fail;
        ThemeManager.Initialize(ThemeManager.ForceDarkFor(_session.Settings.Theme));
        ShowMainWindow();
        _instance?.Listen(() => Dispatcher.BeginInvoke(BringToFront));
        if (_options.Mode == AppMode.Demo) Dispatcher.InvokeAsync(OpenDemoAsync);
        // liegengebliebene Übergabeordner von „Diesen PC prüfen“ (Absturz, abgebrochener Lauf) im Hintergrund aufräumen
        _ = Task.Run(AppSession.CleanupOldTransferFolders);
    }

    private async Task OpenDemoAsync()
    {
        try { await _session!.OpenDemoAsync(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Demo nicht aufgebaut", ex, withStack: true);
            _session!.Report(NoticeKind.Error, RglCodesApp.Message(RglCodesApp.DemoFailed) + " – " + AppSession.Describe(ex));
        }
    }

    private void ShowMainWindow()
    {
        var vm = new MainViewModel(_session!, new DialogService(_session!.Platform));
        vm.Settings.LanguageChanged += OnLanguageChanged;
        var window = new MainWindow(vm);
        var old = MainWindow;
        MainWindow = window;
        window.Show();
        if (old is not null && !ReferenceEquals(old, window)) old.Close();
    }

    /// <summary>Sprache gewechselt: Fenster in der neuen Sprache neu aufbauen; die Sitzung (geöffnete Anlage) bleibt.</summary>
    private void OnLanguageChanged(string language)
    {
        UiLanguage.Apply(language, _session!.MachineDefaults.Language);
        Dispatcher.BeginInvoke(() =>
        {
            var page = (MainWindow as MainWindow)?.ViewModel.SelectedPage.Slug;
            ShowMainWindow();
            if (page is not null && MainWindow is MainWindow w) w.ViewModel.Select(w.ViewModel.Page(page));
        });
    }

    private void BringToFront()
    {
        if (MainWindow is not { } w) return;
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        w.Topmost = true;
        w.Topmost = false;
        w.Focus();
    }

    private async Task RunScreenshotsAsync()
    {
        int code;
        try
        {
            code = await Screenshots.RunAsync(_options.ScreenshotFolder, _options.Theme);
        }
        catch (Exception ex)
        {
            // Bewusst nicht verschluckt: sichtbar melden und mit 3 enden
            Console.Error.WriteLine(ex is RiegelException r ? r.ToString() : $"{RglCodesApp.ScreenshotFailed}: {ex}");
            code = 3;
        }
        Shutdown(code);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Log.Error("Unerwarteter Fehler in der Oberfläche", e.Exception, withStack: true);
        if (_options.Mode == AppMode.Screenshots)
        {
            Console.Error.WriteLine($"{RglCodesApp.ScreenshotFailed}: {e.Exception}");
            Shutdown(3);
            return;
        }
        _session?.Report(NoticeKind.Error, RglCodesApp.Message(RglCodesApp.Unexpected) + " (" + e.Exception.GetType().Name + ")");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info($"Ende (Exitcode {e.ApplicationExitCode})");
        _session?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
