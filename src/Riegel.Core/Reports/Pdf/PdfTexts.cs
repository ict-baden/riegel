// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Resources;

namespace Riegel.Core.Reports.Pdf;

/// <summary>
/// Texte eines PDF-Dokuments in seiner Sprache: feste Texte aus PdfStrings, Status-, Schwere-, Kategorie- und
/// Flag-Namen aus CommonStrings (Richtlinie 13: kein Text im Code). Datum und Uhrzeit stehen in allen Sprachen als
/// ISO 8601 (spec 5: UTC, eindeutig für Prüfer in jedem Land), Zahlen nach der Sprache.
/// </summary>
internal sealed class PdfTexts
{
    public PdfTexts(string language)
    {
        Language = language;
        Culture = CultureInfo.GetCultureInfo(language);
    }

    /// <summary>Sprache des Dokuments (en, de, es, fr, zh-Hans).</summary>
    public string Language { get; }

    public CultureInfo Culture { get; }

    /// <summary>Fester Text aus PdfStrings; fehlt er, der Schlüssel in Klammern (fällt im Test auf).</summary>
    public string T(string key) => PdfStrings.ResourceManager.GetString(key, Culture) ?? $"[{key}]";

    /// <summary>Text mit Platzhaltern.</summary>
    public string F(string key, params object?[] args) => string.Format(Culture, T(key), args);

    /// <summary>Text aus CommonStrings (Status, Schwere, Kategorien, Flags, Rollen …), sonst <paramref name="fallback"/>.</summary>
    public string Common(string key, string? fallback = null) =>
        CommonStrings.ResourceManager.GetString(key, Culture) ?? fallback ?? key;

    public string Status(ResultStatus status) => Common("Status_" + status);

    public string Severity(Severity severity) => Common("Severity_" + severity);

    public string Severity(Severity? severity) => severity is { } s ? Severity(s) : Dash;

    public string Category(string category) => Common("Category_" + category, category);

    public string Reason(ReasonCategory reason) => Common("Reason_" + reason);

    public string Reason(ReasonCategory? reason) => reason is { } r ? Reason(r) : Dash;

    /// <summary>Flags eines Ergebnisses als lesbare Liste („Standardwert angenommen, Neue Regel“), leer = keine.</summary>
    public string Flags(ResultFlags flags)
    {
        var names = Enum.GetValues<ResultFlags>().Where(f => f != ResultFlags.None && (flags & f) == f)
            .Select(f => Common("Flag_" + f));
        return string.Join(", ", names);
    }

    public string RunFlags(RunFlags flags)
    {
        var names = Enum.GetValues<RunFlags>().Where(f => f != Model.RunFlags.None && (flags & f) == f)
            .Select(f => Common("RunFlag_" + f));
        return string.Join(", ", names);
    }

    public string RegisterFlags(RegisterFlags flags)
    {
        var names = Enum.GetValues<RegisterFlags>().Where(f => f != Model.Reports.RegisterFlags.None && (flags & f) == f)
            .Select(f => Common("RegisterFlag_" + f));
        return string.Join(", ", names);
    }

    public string State(RegisterEntryState state) => T("State_" + state);

    public string Source(ValueSource source) => T("Source_" + source);

    public string Origin(SnapshotOrigin origin) => T("Origin_" + origin);

    public string Role(MachineRole role) => T("Role_" + role);

    public string Change(StatusChangeKind kind) => T("Change_" + kind);

    public string Basis(ComparisonBasis basis) => T("Basis_" + basis);

    public string Drift(DriftKind kind) => T("Drift_" + kind);

    /// <summary>Freigabeschritt (Erstellt, Geprüft, Freigegeben, Übernommen); unbekannte Schritte unverändert.</summary>
    public string Step(string step) => step switch
    {
        SignOffSteps.Created => T("Step_Created"),
        SignOffSteps.Reviewed => T("Step_Reviewed"),
        SignOffSteps.Approved => T("Step_Approved"),
        SignOffSteps.Accepted => T("Step_Accepted"),
        _ => step,
    };

    /// <summary>Text zu einem Grundcode („RGL-0230“) aus den Sprachdateien der Pakete, sonst null.</summary>
    public string? CodeText(string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        var key = "Error_" + code.Replace("-", "", StringComparison.Ordinal);
        foreach (var manager in new[] { EngineStrings.ResourceManager, ParsingStrings.ResourceManager, CommonStrings.ResourceManager, WorkflowStrings.ResourceManager })
        {
            try
            {
                if (manager.GetString(key, Culture) is { Length: > 0 } text) return text.Replace("{0}", "…", StringComparison.Ordinal);
            }
            catch (System.Resources.MissingManifestResourceException)
            {
                // Sprachdatei eines Pakets fehlt noch – dann nur der Code
            }
        }
        return null;
    }

    public string Dash => "–";

    public string Yes => T("Yes");

    public string No => T("No");

    public string YesNo(bool value) => value ? Yes : No;

    public string YesNo(bool? value) => value is { } v ? YesNo(v) : T("Unknown");

    public string Number(int value) => value.ToString("N0", Culture);

    public string Number(int? value) => value is { } v ? Number(v) : Dash;

    /// <summary>Prozentwert in der Schreibweise der Sprache („87 %“, „87%“).</summary>
    public string Percent(int? value) => value is { } v ? F("Percent", Number(v)) : Dash;

    public static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public string Date(DateOnly? value) => value is { } v ? Date(v) : Dash;

    /// <summary>Zeitpunkt in UTC als ISO 8601 („2026-10-18 09:00 UTC“).</summary>
    public static string DateTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

    public string DateTime(DateTimeOffset? value) => value is { } v ? DateTime(v) : Dash;

    /// <summary>Die ersten 8 Zeichen einer Prüfsumme (Freigabesatz, spec 2.11).</summary>
    public static string Sha8(string? sha) => string.IsNullOrEmpty(sha) ? "–" : sha.Length <= 8 ? sha : sha[..8];

    public string OrDash(string? value) => string.IsNullOrWhiteSpace(value) ? Dash : value;

    /// <summary>Geltungsbereich eines Registereintrags („alle Rechner“, „Gruppe g-hmi“, „HMI-L3-01“).</summary>
    public string Scope(RegisterScope scope, IReadOnlyDictionary<string, string>? groupNames = null)
    {
        if (scope.AllMachines) return T("Scope_All");
        var parts = new List<string>();
        foreach (var g in scope.Groups)
            parts.Add(F("Scope_Group", groupNames is not null && groupNames.TryGetValue(g, out var name) ? name : g));
        parts.AddRange(scope.Machines);
        return parts.Count == 0 ? Dash : string.Join(", ", parts);
    }

    /// <summary>Optionen der Erfassung („minimal, pseudonymisiert“ oder „Standard“).</summary>
    public string Options(CollectionOptions options)
    {
        var parts = new List<string>();
        if (options.Minimal) parts.Add(T("Option_Minimal"));
        if (options.Deep) parts.Add(T("Option_Deep"));
        if (options.Pseudonymized) parts.Add(T("Option_Pseudonymized"));
        return parts.Count == 0 ? T("Option_Standard") : string.Join(", ", parts);
    }

    /// <summary>Domänenrolle aus Win32_ComputerSystem.DomainRole (0–5).</summary>
    public string DomainRole(int? role) => role is >= 0 and <= 5 ? T("DomainRole_" + role.Value.ToString(CultureInfo.InvariantCulture)) : T("Unknown");

    /// <summary>Liste von Rechnern, höchstens <paramref name="max"/>, dann „…“.</summary>
    public string Hosts(IReadOnlyList<string> hosts, bool more = false, int max = 10)
    {
        if (hosts.Count == 0) return Dash;
        var shown = string.Join(", ", hosts.Take(max));
        return hosts.Count > max || more ? shown + ", …" : shown;
    }
}
