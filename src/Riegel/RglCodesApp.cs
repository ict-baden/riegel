// SPDX-License-Identifier: MIT
using System.Globalization;

namespace Riegel;

/// <summary>
/// Fehlercodes der Oberfläche: RGL-0600 bis RGL-0699 (spec 7.13). Texte in UiStrings unter <c>Error_RGL&lt;nnnn&gt;</c>
/// (fünf Sprachen); die Meldung zeigt immer Code und Text (Richtlinie 13 Punkt 9).
/// </summary>
public static class RglCodesApp
{
    public const int First = 600;
    public const int Last = 699;

    /// <summary>Unerwarteter Fehler in der Oberfläche.</summary>
    public const string Unexpected = "RGL-0600";

    /// <summary>Die Demo-Anlage ließ sich nicht aufbauen.</summary>
    public const string DemoFailed = "RGL-0601";

    /// <summary>Screenshots: WPF hat ein leeres Bild gezeichnet (getrennte Sitzung) oder die Fenstergröße stimmt nicht.</summary>
    public const string ScreenshotFailed = "RGL-0602";

    /// <summary>Keine Anlage geöffnet.</summary>
    public const string NoPlant = "RGL-0603";

    /// <summary>In der Demo und beim Erzeugen der Screenshots nicht verfügbar (z. B. „Diesen PC prüfen“).</summary>
    public const string NotInDemo = "RGL-0604";

    /// <summary>Prüfung mit Rechteerhöhung fehlgeschlagen oder abgebrochen.</summary>
    public const string ElevationFailed = "RGL-0605";

    /// <summary>Rechteerhöhung mit einem anderen Konto: der Export liegt in dessen Profil.</summary>
    public const string ElevationOtherProfile = "RGL-0606";

    /// <summary>Ein Export ließ sich nicht importieren.</summary>
    public const string ImportFailed = "RGL-0607";

    /// <summary>Ein Dokument ließ sich nicht erzeugen.</summary>
    public const string DocumentFailed = "RGL-0608";

    /// <summary>Nichts zum Importieren (keine Exporte oder Bordmittel-Pakete).</summary>
    public const string NothingToImport = "RGL-0609";

    /// <summary>Begründung kürzer als 20 Zeichen.</summary>
    public const string JustificationTooShort = "RGL-0610";

    /// <summary>Herstellervorgabe ohne Verweis auf Dokument oder Ticket.</summary>
    public const string ReferenceRequired = "RGL-0611";

    /// <summary>Kompensierende Maßnahme fehlt.</summary>
    public const string CompensatingRequired = "RGL-0612";

    /// <summary>Akzeptierter Wert fehlt (Pflicht bei check-Regeln).</summary>
    public const string AcceptedValueRequired = "RGL-0613";

    /// <summary>Akzeptierter Wert „*“ (jeder Wert) – Warnung.</summary>
    public const string AcceptedValueAny = "RGL-0614";

    /// <summary>Freigabe unvollständig (Rolle, Datum, Prüftermin, Nachweis).</summary>
    public const string ApprovalIncomplete = "RGL-0615";

    /// <summary>Prüftermin mehr als 36 Monate nach der Freigabe.</summary>
    public const string ReviewTooFar = "RGL-0616";

    /// <summary>Prüftermin mehr als 12 Monate nach der Freigabe – Warnung.</summary>
    public const string ReviewOverYear = "RGL-0617";

    /// <summary>Prüftermin vor dem Freigabedatum.</summary>
    public const string ReviewBeforeApproval = "RGL-0618";

    /// <summary>Regel nicht im Regelwerk – Warnung.</summary>
    public const string UnknownRule = "RGL-0619";

    /// <summary>Grund des Zuschnitts kürzer als 20 Zeichen.</summary>
    public const string ReasonTooShort = "RGL-0620";

    /// <summary>Zuschnitt einer Regel mit hoher Schwere – Warnung.</summary>
    public const string TailoringHighSeverity = "RGL-0621";

    /// <summary>Maßnahme ohne Beschreibung.</summary>
    public const string ActionRequired = "RGL-0622";

    /// <summary>Verantwortliche Rolle fehlt.</summary>
    public const string OwnerRoleRequired = "RGL-0623";

    /// <summary>Geltungsbereich leer (kein Rechner, keine Gruppe, nicht die ganze Anlage).</summary>
    public const string ScopeEmpty = "RGL-0624";

    /// <summary>„Risiko akzeptiert“ bei hoher Schwere nur durch die Rollen der Anlageneinstellung.</summary>
    public const string RiskRoleNotAllowed = "RGL-0625";

    /// <summary>Rolle der Freigabe fehlt.</summary>
    public const string ApprovalRoleRequired = "RGL-0626";

    /// <summary>Freigabe-Nachweis (Change-, QMS- oder Blattnummer) fehlt.</summary>
    public const string ApprovalRecordRequired = "RGL-0627";

    /// <summary>Überschneidung mit einem anderen Eintrag – Warnung.</summary>
    public const string Overlap = "RGL-0628";

    /// <summary>Die Baseline ließ sich nicht speichern oder entfernen.</summary>
    public const string BaselineFailed = "RGL-0630";

    /// <summary>Kein Ausgabeordner gewählt (im Prüfermodus Pflicht).</summary>
    public const string OutputFolderRequired = "RGL-0631";

    /// <summary>Ordner oder Link ließ sich nicht öffnen.</summary>
    public const string OpenFailed = "RGL-0632";

    /// <summary>Hostname ungültig oder schon vorhanden.</summary>
    public const string InvalidHost = "RGL-0633";

    /// <summary>Ungültige Zahl in den Einstellungen.</summary>
    public const string InvalidNumber = "RGL-0634";

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Unexpected, DemoFailed, ScreenshotFailed, NoPlant, NotInDemo, ElevationFailed, ElevationOtherProfile, ImportFailed, DocumentFailed,
        NothingToImport, JustificationTooShort, ReferenceRequired, CompensatingRequired, AcceptedValueRequired, AcceptedValueAny,
        ApprovalIncomplete, ReviewTooFar, ReviewOverYear, ReviewBeforeApproval, UnknownRule, ReasonTooShort, TailoringHighSeverity,
        ActionRequired, OwnerRoleRequired, ScopeEmpty, RiskRoleNotAllowed, ApprovalRoleRequired, ApprovalRecordRequired, Overlap,
        BaselineFailed, OutputFolderRequired, OpenFailed, InvalidHost, InvalidNumber,
    ];

    /// <summary>„RGL-0610: Text …“ in der Sprache der Oberfläche.</summary>
    public static string Message(string code, params object?[] args)
    {
        var key = "Error_" + code.Replace("-", "", StringComparison.Ordinal);
        var text = Riegel.UI.Loc.Strings.GetString(key, CultureInfo.CurrentUICulture) ?? code;
        return $"{code}: {(args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args))}";
    }

    /// <summary>Nur der übersetzte Text ohne Code.</summary>
    public static string Text(string code, params object?[] args)
    {
        var key = "Error_" + code.Replace("-", "", StringComparison.Ordinal);
        var text = Riegel.UI.Loc.Strings.GetString(key, CultureInfo.CurrentUICulture) ?? code;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }
}
