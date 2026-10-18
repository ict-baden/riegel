// SPDX-License-Identifier: MIT
using Riegel.Core.Diagnostics;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Settings;

/// <summary>
/// Einstellungen pro Benutzer (<c>settings.json</c> im Datenordner) und Vorgaben für alle Benutzer
/// (<c>defaults.json</c>, nur gelesen; verteilt per GPO/Intune). Nicht gesetzte Werte sind null.
/// </summary>
/// <param name="Language">"" oder null = Sprache des Systems; sonst en, de, es, fr, zh-Hans.</param>
/// <param name="Theme">light, dark oder null (dem Windows-Design folgen).</param>
/// <param name="ReportLanguage">Standardsprache der Berichte (null = Sprache der Oberfläche).</param>
/// <param name="RecentPlants">Zuletzt geöffnete Anlagenordner (nur in settings.json).</param>
public sealed record RiegelSettings(string? Language = null, string? Theme = null, string? DefaultPlantFolder = null,
    int? WarnDays = null, int? StaleDays = null, int? UpdateMaxDays = null, int? UnusedAccountDays = null,
    string? ReportLanguage = null, IReadOnlyList<string>? RecentPlants = null)
{
    public static RiegelSettings Empty { get; } = new();
}

/// <summary>Wirksame Einstellungen: Benutzer vor Vorgabe vor eingebautem Standard (spec 7.10, 7.8 Einstellungen).</summary>
/// <param name="Preset">Namen der Einstellungen (camelCase), die aus defaults.json kommen („vorgegeben“).</param>
public sealed record EffectiveSettings(string Language, string? Theme, string? DefaultPlantFolder, int WarnDays, int StaleDays,
    int UpdateMaxDays, int UnusedAccountDays, string? ReportLanguage, IReadOnlyList<string> RecentPlants,
    IReadOnlySet<string> Preset)
{
    public bool IsPreset(string name) => Preset.Contains(name);
}

/// <summary>Wo Riegel pro Benutzer und für alle Benutzer speichert (Richtlinie 12, spec 6).</summary>
public static class AppPaths
{
    /// <summary>Übersteuert den Datenordner (Skripte, Tests): absoluter Pfad.</summary>
    public const string DataDirVariable = "RIEGEL_DATA_DIR";

    /// <summary>Datenordner: RIEGEL_DATA_DIR, sonst %LocalAppData%\Riegel (Windows) bzw. ~/.local/share/riegel (Linux).</summary>
    public static string DataFolder => ResolveDataFolder(Environment.GetEnvironmentVariable, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());

    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");

    public static string LogsFolder => Path.Combine(DataFolder, "logs");

    /// <summary>Übergabeordner der Rechteerhöhung (spec 7.6).</summary>
    public static string HandoffFolder => Path.Combine(DataFolder, "handoff");

    /// <summary>
    /// Vorgaben für alle Benutzer: %ProgramData%\ICT-Baden\Riegel\defaults.json (Windows),
    /// /etc/ictbaden/riegel/defaults.json (Linux, Richtlinie 12).
    /// </summary>
    public static string MachineDefaultsFile => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.DoNotVerify), "ICT-Baden", "Riegel", "defaults.json")
        : Path.Combine("/etc", "ictbaden", "riegel", "defaults.json");

    internal static string ResolveDataFolder(Func<string, string?> env, bool windows, bool macOS)
    {
        if (env(DataDirVariable) is { Length: > 0 } custom && Path.IsPathRooted(custom)) return custom;
        if (windows)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "Riegel");
        var home = env("HOME") is { Length: > 0 } h ? h
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        if (macOS) return Path.Combine(home, "Library", "Application Support", "Riegel");
        var xdg = env("XDG_DATA_HOME");
        var baseDir = !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg) ? xdg : Path.Combine(home, ".local", "share");
        return Path.Combine(baseDir, "riegel");
    }
}

/// <summary>Lesen und Schreiben der Einstellungen. Fehler beim Lesen führen zu Standardwerten, nie zum Absturz.</summary>
public static class SettingsStore
{
    /// <summary>Höchstgröße von settings.json und defaults.json.</summary>
    public const long MaxBytes = 1024 * 1024;

    /// <summary>settings.json lesen; fehlt sie oder ist sie kaputt: leere Einstellungen (RGL-0010 im Protokoll).</summary>
    public static RiegelSettings Load(string? path = null) => Read(path ?? AppPaths.SettingsFile, RglCodesCommon.SettingsUnreadable);

    /// <summary>defaults.json lesen (nur lesen, nie schreiben); kaputt: ignoriert (RGL-0011 im Protokoll).</summary>
    public static RiegelSettings LoadMachineDefaults(string? path = null) => Read(path ?? AppPaths.MachineDefaultsFile, RglCodesCommon.DefaultsUnreadable);

    /// <summary>settings.json atomar schreiben.</summary>
    public static void Save(RiegelSettings settings, string? path = null) => RiegelJson.WriteFile(path ?? AppPaths.SettingsFile, settings);

    /// <summary>Benutzer vor Vorgabe vor Standard (30/30/35/90 Tage wie <see cref="PlantSettings.Default"/>).</summary>
    public static EffectiveSettings Resolve(RiegelSettings user, RiegelSettings machine)
    {
        var preset = new SortedSet<string>(StringComparer.Ordinal);
        T Pick<T>(string name, T? userValue, T? machineValue, T fallback) where T : struct
        {
            if (userValue is { } u) return u;
            if (machineValue is { } m) { preset.Add(name); return m; }
            return fallback;
        }
        string? PickText(string name, string? userValue, string? machineValue)
        {
            if (!string.IsNullOrWhiteSpace(userValue)) return userValue;
            if (!string.IsNullOrWhiteSpace(machineValue)) { preset.Add(name); return machineValue; }
            return null;
        }
        return new EffectiveSettings(
            PickText("language", user.Language, machine.Language) ?? "",
            PickText("theme", user.Theme, machine.Theme),
            PickText("defaultPlantFolder", user.DefaultPlantFolder, machine.DefaultPlantFolder),
            Pick("warnDays", user.WarnDays, machine.WarnDays, 30),
            Pick("staleDays", user.StaleDays, machine.StaleDays, 30),
            Pick("updateMaxDays", user.UpdateMaxDays, machine.UpdateMaxDays, 35),
            Pick("unusedAccountDays", user.UnusedAccountDays, machine.UnusedAccountDays, 90),
            PickText("reportLanguage", user.ReportLanguage, machine.ReportLanguage),
            user.RecentPlants ?? [],
            preset);
    }

    private static RiegelSettings Read(string path, string errorCode)
    {
        try
        {
            if (!File.Exists(path)) return RiegelSettings.Empty;
            return RiegelJson.ReadFile<RiegelSettings>(path, MaxBytes);
        }
        catch (Exception e) when (e is RiegelException or IOException or UnauthorizedAccessException)
        {
            // Nur Code und Ausnahmetyp ins Protokoll, nie Inhalte (spec 7.6)
            Log.Warning($"{errorCode}: {e.GetType().Name}");
            return RiegelSettings.Empty;
        }
    }
}
