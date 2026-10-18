// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Resources;
using Riegel.UI;

namespace Riegel.ViewModels;

/// <summary>
/// Anzeigetexte für Modellwerte in der Sprache der Oberfläche: Status, Schwere, Kategorien, Flags in Worten,
/// Herkunft, Register-Zustände, Datum nach Gebietsschema (Richtlinie 13 Punkt 5). Gemeinsame Begriffe kommen aus
/// CommonStrings (Riegel.Core), damit Oberfläche und Berichte gleich sprechen.
/// </summary>
internal static class Texts
{
    public static string Status(ResultStatus s) => Loc.C("Status_" + s);

    public static string Severity(Severity s) => Loc.C("Severity_" + s);

    public static string Category(string code) => CommonStrings.ResourceManager.GetString("Category_" + code, CultureInfo.CurrentUICulture) ?? code;

    public static string Reason(ReasonCategory r) => Loc.C("Reason_" + r);

    public static string Role(MachineRole? r) => r is null ? "" : Loc.T("MachineRole_" + r);

    public static string Source(ValueSource s) => Loc.T("Source_" + s);

    /// <summary>Flags in Worten („Standardwert angenommen“ …), in der Reihenfolge des Enums.</summary>
    public static IReadOnlyList<string> Flags(ResultFlags flags) =>
        [.. Enum.GetValues<ResultFlags>().Where(f => f != ResultFlags.None && (flags & f) == f).Select(f => Loc.C("Flag_" + f))];

    public static string FlagsLine(ResultFlags flags) => string.Join(" · ", Flags(flags));

    public static IReadOnlyList<string> RunFlags(RunFlags flags) =>
        [.. Enum.GetValues<RunFlags>().Where(f => f != Core.Model.RunFlags.None && (flags & f) == f).Select(f => Loc.C("RunFlag_" + f))];

    public static IReadOnlyList<string> RegisterFlagTexts(RegisterFlags flags) =>
        [.. Enum.GetValues<RegisterFlags>().Where(f => f != RegisterFlags.None && (flags & f) == f).Select(f => Loc.C("RegisterFlag_" + f))];

    public static string EntryState(RegisterEntryState s) => Loc.T("EntryState_" + s);

    /// <summary>Farbton eines Register-Zustands für <see cref="TonePill"/>.</summary>
    public static string EntryTone(RegisterEntryState s) => s switch
    {
        RegisterEntryState.Valid => "Good",
        RegisterEntryState.Expiring or RegisterEntryState.Pending or RegisterEntryState.ChangedAfterApproval => "Caution",
        RegisterEntryState.Expired or RegisterEntryState.Invalid => "Critical",
        _ => "",
    };

    public static string MeasureState(MeasureState s) => Loc.T("MeasureState_" + s);

    public static string ChangeKind(StatusChangeKind k) => Loc.T("Change_" + k);

    public static string DriftKind(DriftKind k) => Loc.T("Drift_" + k);

    /// <summary>Kanonischer Wert für die Anzeige; null = „(nicht gesetzt)“.</summary>
    public static string Value(string? canonical) => canonical ?? Loc.C("Value_NotSet");

    public static string Date(DateOnly? d) => d is { } x ? x.ToString("d", CultureInfo.CurrentCulture) : "–";

    public static string Date(DateTimeOffset? d) => d is { } x ? x.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) : "–";

    public static string DateTime(DateTimeOffset? d) => d is { } x ? x.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "–";

    public static string Number(int n) => n.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>
    /// Pfad mit %TEMP%, %LocalAppData% oder %UserProfile% statt des Profilordners – zeigt den Benutzernamen nicht
    /// (Demo und Bilder der Doku). Environment.ExpandEnvironmentVariables macht daraus wieder den echten Pfad.
    /// </summary>
    public static string ShortPath(string path)
    {
        foreach (var (variable, folder) in new[]
        {
            ("TEMP", Path.GetTempPath().TrimEnd('\\', '/')),
            ("LocalAppData", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
            ("UserProfile", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        })
        {
            if (folder.Length > 0 && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                && (path.Length == folder.Length || path[folder.Length] is '\\' or '/'))
                return "%" + variable + "%" + path[folder.Length..];
        }
        return path;
    }

    public static string Percent(int? n) => n is { } x ? Loc.F("Format_Percent", x) : "–";

    /// <summary>Geltungsbereich lesbar: Rechner, Gruppen (mit Namen) oder ganze Anlage.</summary>
    public static string Scope(RegisterScope scope, Plant plant)
    {
        if (scope.AllMachines) return Loc.T("Scope_All");
        var parts = new List<string>();
        parts.AddRange(scope.Machines);
        parts.AddRange(scope.Groups.Select(g => plant.Groups.FirstOrDefault(x => x.Id == g)?.Name is { } name ? Loc.F("Scope_GroupNamed", name) : g));
        return parts.Count == 0 ? Loc.T("Scope_None") : string.Join(", ", parts);
    }

    /// <summary>Betriebssystem kurz: Edition und Build.</summary>
    public static string Os(MachineInfo? m, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? facts = null)
    {
        if (m is null) return "–";
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(m.EditionId)) parts.Add(m.EditionId);
        if (m.Build is { } b) parts.Add(m.Ubr is { } u ? $"{b}.{u}" : b.ToString(CultureInfo.InvariantCulture));
        return parts.Count == 0 ? "–" : string.Join(" · ", parts);
    }

    public static string Origin(SnapshotOrigin o) => Loc.T("Origin_" + o);

    /// <summary>Text zu einem RGL-Grundcode aus den Sprachdateien von Riegel.Core (Engine, Parsing …), sonst nur der Code.</summary>
    public static string ReasonCode(string? code)
    {
        if (string.IsNullOrEmpty(code)) return "";
        var key = code.Replace("-", "", StringComparison.Ordinal);
        foreach (var manager in new[] { EngineStrings.ResourceManager, ParsingStrings.ResourceManager, WorkflowStrings.ResourceManager, CommonStrings.ResourceManager })
        {
            var text = manager.GetString("Reason_" + key, CultureInfo.CurrentUICulture) ?? manager.GetString("Error_" + key, CultureInfo.CurrentUICulture);
            if (!string.IsNullOrEmpty(text)) return $"{code}: {text}";
        }
        return code;
    }
}
