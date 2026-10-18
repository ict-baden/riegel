// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.Diagnostics;
using Riegel.Core.Localization;
using Riegel.Core.Model;
using Riegel.Core.Services;
using Riegel.Core.Settings;
using Riegel.Demo;
using Riegel.UI;

namespace Riegel.Session;

/// <summary>Wie die Oberfläche läuft (spec 7.7: normal, <c>--demo</c>, <c>--screenshots</c>).</summary>
internal enum AppMode { Normal, Demo, Screenshots }

/// <summary>Art einer Meldung in der Fußleiste.</summary>
internal enum NoticeKind { Info, Success, Warning, Error }

/// <summary>
/// Zustand der Oberfläche über alle Seiten: Modus, Einstellungen, geöffnete Anlage, Meldungen, „läuft gerade“. Alles,
/// was schreibt, geht über die Methoden dieser Klasse (<c>AppSession.Actions.cs</c>): im Prüfermodus und in der Demo
/// wird dort geprüft, was erlaubt ist.
/// </summary>
internal sealed partial class AppSession : ObservableObject, IDisposable
{
    private PlantContext? _plant;
    private bool _busy;
    private string _busyText = "";
    private string? _demoFolder;
    private RiegelSettings _user;
    private readonly bool _saveSettings;

    public AppSession(AppMode mode, IPlatform platform, RiegelSettings user, RiegelSettings machine, bool saveSettings = true)
    {
        Mode = mode;
        Platform = platform;
        _user = user;
        MachineDefaults = machine;
        _saveSettings = saveSettings && mode == AppMode.Normal;
        RealServices = AppServices.From(RiegelServices.CreateDefault(platform));
        Settings = SettingsStore.Resolve(user, machine);
        try { IsElevated = mode == AppMode.Normal && platform.IsElevated; }
        catch (Exception e) when (e is not OutOfMemoryException) { IsElevated = false; }
    }

    public AppMode Mode { get; }

    public IPlatform Platform { get; }

    /// <summary>Die echten Dienste (für Anlagen der Benutzer immer diese).</summary>
    public AppServices RealServices { get; }

    /// <summary>Dienste der geöffneten Anlage (bei der Demo ggf. nachgebildet), sonst die echten.</summary>
    public AppServices Services => _plant?.Services ?? RealServices;

    public RiegelSettings UserSettings => _user;

    public RiegelSettings MachineDefaults { get; }

    public EffectiveSettings Settings { get; private set; }

    public bool IsElevated { get; }

    public PlantContext? Plant => _plant;

    public bool HasPlant => _plant is not null;

    public bool IsDemoPlant => _plant?.IsDemo == true;

    /// <summary>In den Anlagenordner darf geschrieben werden (Anlage offen, kein Prüfermodus).</summary>
    public bool CanWrite => _plant is { ReadOnly: false };

    /// <summary>„Diesen PC prüfen“ ist erlaubt: nicht in der Demo, nicht beim Erzeugen der Screenshots (spec 7.8).</summary>
    public bool CollectionAllowed => Mode == AppMode.Normal && !IsDemoPlant;

    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }

    public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }

    /// <summary>Sprachcode der Oberfläche (en, de, es, fr, zh-Hans).</summary>
    public static string Language => UiLanguage.Normalize(CultureInfo.CurrentUICulture.Name) is { Length: > 0 } code ? code : UiLanguage.Fallback;

    /// <summary>Sprache der Berichte: Anlage, sonst Einstellung, sonst Oberfläche.</summary>
    public string ReportLanguage => UiLanguage.Normalize(_plant?.Plant.Settings.ReportLanguage) is { Length: > 0 } p ? p
        : UiLanguage.Normalize(Settings.ReportLanguage) is { Length: > 0 } s ? s : Language;

    /// <summary>Die Anlage wurde geöffnet, geschlossen oder neu geladen.</summary>
    public event Action? PlantChanged;

    /// <summary>Meldung für die Fußleiste.</summary>
    public event Action<NoticeKind, string>? Notice;

    /// <summary>Eine Seite soll angezeigt werden (Kürzel, z. B. „results“), optional mit Rechner.</summary>
    public event Action<string, string?>? PageRequested;

    public void Report(NoticeKind kind, string text) => Notice?.Invoke(kind, text);

    public void ShowPage(string slug, string? host = null) => PageRequested?.Invoke(slug, host);

    /// <summary>Lesbarer Text mit Fehlercode zu einer Ausnahme (Richtlinie 13 Punkt 9).</summary>
    public static string Describe(Exception e) => e switch
    {
        RiegelException r => r.ToString(),
        IOException or UnauthorizedAccessException => $"{RglCodesCommon.IoError}: {RglCodesCommon.Message(RglCodesCommon.IoError, e.Message)}",
        _ => RglCodesApp.Message(RglCodesApp.Unexpected) + " (" + e.GetType().Name + ")",
    };

    /// <summary>Fehler aus einem Befehl melden und protokollieren (ohne Inhalte, nur Code und Typ).</summary>
    public void Fail(Exception e)
    {
        Log.Error("Fehler in der Oberfläche", e is RiegelException r ? new InvalidOperationException(r.Code) : e, withStack: e is not (RiegelException or IOException));
        Report(NoticeKind.Error, Describe(e));
    }

    /// <summary>Arbeit im Hintergrund mit „läuft …“-Anzeige.</summary>
    public async Task<T> Work<T>(string busyText, Func<T> work)
    {
        IsBusy = true;
        BusyText = busyText;
        try { return await Task.Run(work); }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }
    }

    public Task Work(string busyText, Action work) => Work(busyText, () => { work(); return true; });

    // ---------------------------------------------------------------- Anlage öffnen und schließen

    /// <summary>Anlagenordner öffnen; <paramref name="readOnly"/> = Prüfermodus (spec 7.6).</summary>
    public async Task OpenAsync(string folder, bool readOnly)
    {
        var services = RealServices;
        var context = await Work(Loc.T("Busy_Opening"), () => PlantContext.Load(services, services.Plants.Open(folder, readOnly), isDemo: false));
        SetPlant(context);
        Remember(context.Folder);
        Log.Info(readOnly ? "Anlage im Prüfermodus geöffnet" : "Anlage geöffnet");
    }

    /// <summary>Neue Anlage anlegen (Standardwerte aus den Einstellungen) und öffnen.</summary>
    public async Task CreateAsync(string folder, string name)
    {
        var services = RealServices;
        var settings = Settings;
        var context = await Work(Loc.T("Busy_Creating"), () =>
        {
            var plant = services.Plants.Create(folder, name);
            var configured = plant with
            {
                Settings = plant.Settings with
                {
                    WarnDays = settings.WarnDays,
                    StaleDays = settings.StaleDays,
                    UpdateMaxDays = settings.UpdateMaxDays,
                    UnusedAccountDays = settings.UnusedAccountDays,
                    ReportLanguage = UiLanguage.Normalize(settings.ReportLanguage) is { Length: > 0 } l ? l : null,
                },
            };
            services.Plants.SavePlant(configured, plant.FileSha256 ?? "");
            return PlantContext.Load(services, services.Plants.Open(folder, false), isDemo: false);
        });
        SetPlant(context);
        Remember(context.Folder);
        Log.Info("Anlage angelegt");
    }

    /// <summary>Demo-Anlage „Musterwerk – Linie 3“ aufbauen und öffnen (spec 7.9).</summary>
    /// <param name="fakeServices">Nur für Tests: Entwicklungsdienste erzwingen oder abschalten; sonst RIEGEL_FAKE_SERVICES.</param>
    public async Task OpenDemoAsync(bool? fakeServices = null)
    {
        bool fake = fakeServices ?? AppServices.FakeRequested();
        var platform = Platform;
        var (folder, context) = await Work(Loc.T("Busy_Demo"), () =>
        {
            DemoPlant.CleanupOld();
            var dir = DemoPlant.NewFolder();
            try
            {
                var plant = DemoPlant.Build(dir, platform, fake);
                var services = DemoPlant.Services(platform, DemoData.Now, fake);
                return (dir, PlantContext.Load(services, services.Plants.Open(plant.Folder!, false), isDemo: true));
            }
            catch
            {
                DemoPlant.Delete(dir);
                throw;
            }
        });
        var old = _demoFolder;
        _demoFolder = folder;
        SetPlant(context);
        if (old is not null) DemoPlant.Delete(old);
    }

    /// <summary>Geöffnete Anlage neu laden (nach einer Änderung).</summary>
    public async Task ReloadAsync()
    {
        if (_plant is not { } current) return;
        var context = await Work(Loc.T("Busy_Loading"), () =>
            PlantContext.Load(current.Services, current.Services.Plants.Open(current.Folder, current.ReadOnly), current.IsDemo));
        SetPlant(context);
    }

    public void ClosePlant()
    {
        SetPlant(null);
        if (_demoFolder is not null)
        {
            DemoPlant.Delete(_demoFolder);
            _demoFolder = null;
        }
    }

    internal void SetPlant(PlantContext? context)
    {
        _plant = context;
        RaiseAll();
        PlantChanged?.Invoke();
    }

    // ---------------------------------------------------------------- Einstellungen

    /// <summary>Zuletzt geöffnete Anlagen (höchstens acht, ohne Demo).</summary>
    public IReadOnlyList<string> RecentPlants => _user.RecentPlants ?? [];

    private void Remember(string folder)
    {
        var list = new List<string> { folder };
        list.AddRange(RecentPlants.Where(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)));
        UpdateSettings(_user with { RecentPlants = [.. list.Take(8)] });
    }

    public void ForgetRecent(string folder) =>
        UpdateSettings(_user with { RecentPlants = [.. RecentPlants.Where(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase))] });

    /// <summary>Einstellungen des Benutzers ändern und (im normalen Modus) in settings.json speichern.</summary>
    public void UpdateSettings(RiegelSettings user)
    {
        _user = user;
        Settings = SettingsStore.Resolve(user, MachineDefaults);
        if (_saveSettings)
        {
            try { SettingsStore.Save(user); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or RiegelException) { Fail(e); }
        }
        Raise(nameof(Settings));
        Raise(nameof(RecentPlants));
    }

    public void Dispose()
    {
        if (_demoFolder is not null) DemoPlant.Delete(_demoFolder);
        _demoFolder = null;
    }
}
