// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.Localization;
using Riegel.Core.Settings;
using Riegel.Session;
using Riegel.UI;
using Riegel.UI.Theme;

namespace Riegel.ViewModels;

/// <summary>
/// Einstellungen (spec 7.8 Seite 10): Sprache, Design, Standardordner, Fristen (Standard für neue Anlagen) und
/// Berichtssprache; Werte aus defaults.json (GPO/Intune) erscheinen als „vorgegeben“. Darunter die Einstellungen der
/// geöffneten Anlage (riegel-plant.json, nur auf ausdrücklichen Wunsch gespeichert).
/// </summary>
internal sealed class SettingsViewModel : PageViewModel
{
    private Choice<string> _language;
    private Choice<string> _theme;
    private Choice<string> _reportLanguage;
    private string _folder = "";
    private string _warn = "", _stale = "", _update = "", _unused = "";
    private string _plantWarn = "", _plantStale = "", _plantUpdate = "", _plantUnused = "", _operator = "";
    private Choice<string> _plantLanguage;
    private bool _loading;

    public SettingsViewModel(AppSession session, IDialogs dialogs) : base(session, dialogs, "settings", "")
    {
        Languages = [new Choice<string>("", Loc.T("Settings_LanguageSystem")), .. UiLanguage.Supported.Select(c => new Choice<string>(c, UiLanguage.NativeName(c)))];
        ReportLanguages = [new Choice<string>("", Loc.T("Settings_ReportLanguageUi")), .. UiLanguage.Supported.Select(c => new Choice<string>(c, UiLanguage.NativeName(c)))];
        Themes = [new Choice<string>("", Loc.T("Settings_ThemeSystem")), new Choice<string>("light", Loc.T("Settings_ThemeLight")), new Choice<string>("dark", Loc.T("Settings_ThemeDark"))];
        _language = Languages[0];
        _theme = Themes[0];
        _reportLanguage = ReportLanguages[0];
        _plantLanguage = ReportLanguages[0];
        BrowseCommand = new RelayCommand(() => { if (Dialogs.PickFolder(Loc.T("Settings_DefaultFolder"), Folder) is { } f) { Folder = f; Save(); } });
        SavePlantCommand = new AsyncCommand(SavePlantAsync, () => Session.CanWrite && PlantErrors.Count == 0);
        OpenDataFolderCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(AppPaths.DataFolder);
            if (Shell.ShowInFolder(Session.Platform, AppPaths.DataFolder) is { } p) Session.Report(NoticeKind.Warning, p);
        }, () => Session.Mode == AppMode.Normal);
        Load();
    }

    public override bool NeedsPlant => false;

    /// <summary>Sprache gewechselt: die App baut das Fenster in der neuen Sprache neu auf.</summary>
    public event Action<string>? LanguageChanged;

    public IReadOnlyList<Choice<string>> Languages { get; }
    public IReadOnlyList<Choice<string>> ReportLanguages { get; }
    public IReadOnlyList<Choice<string>> Themes { get; }

    public RelayCommand BrowseCommand { get; }
    public AsyncCommand SavePlantCommand { get; }
    public RelayCommand OpenDataFolderCommand { get; }

    public Choice<string> Language
    {
        get => _language;
        set
        {
            if (!Set(ref _language, value) || _loading) return;
            Save();
            LanguageChanged?.Invoke(value.Value);
        }
    }

    public Choice<string> Theme
    {
        get => _theme;
        set
        {
            if (!Set(ref _theme, value) || _loading) return;
            Save();
            if (Session.Mode != AppMode.Screenshots) ThemeManager.Initialize(ThemeManager.ForceDarkFor(value.Value.Length == 0 ? null : value.Value));
        }
    }

    public Choice<string> ReportLanguage { get => _reportLanguage; set { if (Set(ref _reportLanguage, value) && !_loading) Save(); } }

    public string Folder { get => _folder; set { if (Set(ref _folder, value) && !_loading) Save(); } }

    public string WarnDays { get => _warn; set { if (Set(ref _warn, value) && !_loading) Save(); } }
    public string StaleDays { get => _stale; set { if (Set(ref _stale, value) && !_loading) Save(); } }
    public string UpdateMaxDays { get => _update; set { if (Set(ref _update, value) && !_loading) Save(); } }
    public string UnusedAccountDays { get => _unused; set { if (Set(ref _unused, value) && !_loading) Save(); } }

    /// <summary>„vorgegeben“ neben Werten aus defaults.json (spec 7.8), sonst null.</summary>
    private string? PresetBadge(string name) => Session.Settings.IsPreset(name) ? Loc.T("Settings_Preset") : null;

    public string? LanguagePreset => PresetBadge("language");
    public string? ThemePreset => PresetBadge("theme");
    public string? FolderPreset => PresetBadge("defaultPlantFolder");
    public string? WarnPreset => PresetBadge("warnDays");
    public string? StalePreset => PresetBadge("staleDays");
    public string? UpdatePreset => PresetBadge("updateMaxDays");
    public string? UnusedPreset => PresetBadge("unusedAccountDays");
    public string? ReportLanguagePreset => PresetBadge("reportLanguage");

    public IReadOnlyList<string> Errors { get; private set; } = [];
    public bool HasErrors => Errors.Count > 0;

    public string SettingsPath => Shorten(AppPaths.SettingsFile);
    public string DefaultsPath => AppPaths.MachineDefaultsFile;

    // ---------------------------------------------------------------- Anlage

    public bool HasPlantSettings => Plant is not null;
    public bool CanWritePlant => Session.CanWrite;
    public string PlantTitle => Plant is null ? "" : Loc.F("Settings_PlantTitle", Plant.Plant.Name);
    public string PlantWarnDays { get => _plantWarn; set { if (Set(ref _plantWarn, value)) RaisePlantErrors(); } }
    public string PlantStaleDays { get => _plantStale; set { if (Set(ref _plantStale, value)) RaisePlantErrors(); } }
    public string PlantUpdateMaxDays { get => _plantUpdate; set { if (Set(ref _plantUpdate, value)) RaisePlantErrors(); } }
    public string PlantUnusedAccountDays { get => _plantUnused; set { if (Set(ref _plantUnused, value)) RaisePlantErrors(); } }
    public string OperatorRole { get => _operator; set => Set(ref _operator, value); }
    public Choice<string> PlantLanguage { get => _plantLanguage; set => Set(ref _plantLanguage, value); }

    public IReadOnlyList<string> PlantErrors =>
        [.. new[] { PlantWarnDays, PlantStaleDays, PlantUpdateMaxDays, PlantUnusedAccountDays }.Where(t => Days(t) is null).Select(t => RglCodesApp.Message(RglCodesApp.InvalidNumber, t))];

    private void RaisePlantErrors() => Raise(nameof(PlantErrors));

    public override void OnPlantChanged()
    {
        base.OnPlantChanged();
        Load();
    }

    private void Load()
    {
        _loading = true;
        var user = Session.UserSettings;
        var effective = Session.Settings;
        _language = Languages.FirstOrDefault(l => l.Value == UiLanguage.Normalize(user.Language)) ?? Languages[0];
        _theme = Themes.FirstOrDefault(t => t.Value == (effective.Theme ?? "")) ?? Themes[0];
        _reportLanguage = ReportLanguages.FirstOrDefault(l => l.Value == UiLanguage.Normalize(effective.ReportLanguage)) ?? ReportLanguages[0];
        _folder = effective.DefaultPlantFolder ?? "";
        _warn = Num(effective.WarnDays);
        _stale = Num(effective.StaleDays);
        _update = Num(effective.UpdateMaxDays);
        _unused = Num(effective.UnusedAccountDays);
        if (Plant is { } plant)
        {
            var s = plant.Plant.Settings;
            _plantWarn = Num(s.WarnDays);
            _plantStale = Num(s.StaleDays);
            _plantUpdate = Num(s.UpdateMaxDays);
            _plantUnused = Num(s.UnusedAccountDays);
            _operator = s.OperatorRole ?? "";
            _plantLanguage = ReportLanguages.FirstOrDefault(l => l.Value == UiLanguage.Normalize(s.ReportLanguage)) ?? ReportLanguages[0];
        }
        _loading = false;
        RaiseAll();
    }

    private static string Num(int n) => n.ToString(CultureInfo.CurrentCulture);

    private static int? Days(string text) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var n) && n is >= 1 and <= 3650 ? n : null;

    /// <summary>Einstellungen des Benutzers speichern (settings.json); vorgegebene Werte bleiben, solange der Benutzer nichts ändert.</summary>
    private void Save()
    {
        var errors = new List<string>();
        int? Read(string text, int? current, string name)
        {
            var value = Days(text);
            if (value is null) { errors.Add(RglCodesApp.Message(RglCodesApp.InvalidNumber, text)); return current; }
            // Unveränderter vorgegebener oder eingebauter Wert: nicht in settings.json übernehmen – sonst hebelte er
            // spätere Vorgaben aus defaults.json aus
            if (current is null && value == Effective(name)) return null;
            return value;
        }
        var user = Session.UserSettings;
        var updated = user with
        {
            Language = Language.Value.Length == 0 ? null : Language.Value,
            Theme = Theme.Value.Length == 0 ? null : Theme.Value,
            ReportLanguage = ReportLanguage.Value.Length == 0 ? null : ReportLanguage.Value,
            DefaultPlantFolder = string.IsNullOrWhiteSpace(Folder) ? null : Folder.Trim(),
            WarnDays = Read(WarnDays, user.WarnDays, "warnDays"),
            StaleDays = Read(StaleDays, user.StaleDays, "staleDays"),
            UpdateMaxDays = Read(UpdateMaxDays, user.UpdateMaxDays, "updateMaxDays"),
            UnusedAccountDays = Read(UnusedAccountDays, user.UnusedAccountDays, "unusedAccountDays"),
        };
        if (Session.Settings.IsPreset("defaultPlantFolder") && updated.DefaultPlantFolder == Session.Settings.DefaultPlantFolder && user.DefaultPlantFolder is null)
            updated = updated with { DefaultPlantFolder = null };
        Errors = errors;
        Raise(nameof(Errors));
        Raise(nameof(HasErrors));
        Session.UpdateSettings(updated);
        foreach (var n in new[] { nameof(LanguagePreset), nameof(ThemePreset), nameof(FolderPreset), nameof(WarnPreset), nameof(StalePreset), nameof(UpdatePreset),
                     nameof(UnusedPreset), nameof(ReportLanguagePreset) })
            Raise(n);
    }

    private int Effective(string name) => name switch
    {
        "warnDays" => Session.Settings.WarnDays,
        "staleDays" => Session.Settings.StaleDays,
        "updateMaxDays" => Session.Settings.UpdateMaxDays,
        _ => Session.Settings.UnusedAccountDays,
    };

    private async Task SavePlantAsync()
    {
        if (Plant is null || PlantErrors.Count > 0) return;
        int warn = Days(PlantWarnDays)!.Value, stale = Days(PlantStaleDays)!.Value, update = Days(PlantUpdateMaxDays)!.Value, unused = Days(PlantUnusedAccountDays)!.Value;
        var role = string.IsNullOrWhiteSpace(OperatorRole) ? null : OperatorRole.Trim();
        var language = PlantLanguage.Value.Length == 0 ? null : PlantLanguage.Value;
        await Session.SavePlantAsync(p => p with
        {
            Settings = p.Settings with { WarnDays = warn, StaleDays = stale, UpdateMaxDays = update, UnusedAccountDays = unused, OperatorRole = role, ReportLanguage = language },
        }, Loc.T("Notice_PlantSettingsSaved"));
    }

    /// <summary>Pfade im eigenen Profil mit Umgebungsvariable: kein Benutzername auf der Seite (und auf Screenshots).</summary>
    internal static string Shorten(string path) => Texts.ShortPath(path);
}
