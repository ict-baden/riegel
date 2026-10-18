// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.Model;
using Riegel.Core.Resources;

namespace Riegel.Core.Plants;

/// <summary>
/// Fehlercodes von Anlagenordner, Läufen, Registern, Vergleich und Berichtsdaten: RGL-0400 bis RGL-0449 (spec 7.13).
/// Texte in WorkflowStrings unter <c>Error_RGL&lt;nnnn&gt;</c>. Die Codes ab RGL-0420 sind Befunde der Registerprüfung
/// (<see cref="ValidationIssue.Code"/>).
/// </summary>
public static class RglCodesWorkflow
{
    public const int First = 400;
    public const int Last = 449;

    /// <summary>Anlagenordner im Prüfermodus geöffnet: es wird nichts hineingeschrieben (spec 7.6).</summary>
    public const string ReadOnlyPlant = "RGL-0401";

    /// <summary>Kein Anlagenordner (riegel-plant.json fehlt).</summary>
    public const string NotAPlant = "RGL-0402";

    /// <summary>„von jemand anderem geändert“: SHA-256 der Datei passt nicht zum Erwartungswert (spec 7.6).</summary>
    public const string ChangedByOthers = "RGL-0403";

    /// <summary>Sperrdatei .riegel.lock nicht binnen 10 s erhalten (spec 7.6).</summary>
    public const string LockTimeout = "RGL-0404";

    /// <summary>Ein anderer Export mit demselben Namen liegt schon im Anlagenordner.</summary>
    public const string SnapshotConflict = "RGL-0405";

    /// <summary>Die .sha256-Begleitdatei passt nicht zum Export; nichts wurde importiert.</summary>
    public const string SidecarMismatch = "RGL-0406";

    /// <summary>Keine Lauf-ID (Form &lt;yyyyMMddTHHmmssZ&gt;-&lt;6 hex&gt;).</summary>
    public const string InvalidRunId = "RGL-0407";

    /// <summary>Freigabe ohne Rolle oder ohne Freigabe-Nachweis (approvalRecord).</summary>
    public const string ApprovalIncomplete = "RGL-0408";

    /// <summary>Eintrag gibt es im Register nicht.</summary>
    public const string EntryNotFound = "RGL-0409";

    /// <summary>Freigegebener Stand passt nicht zum Lauf (Prüfsumme der Ergebnisse oder des Exports).</summary>
    public const string ApprovedStateMismatch = "RGL-0410";

    /// <summary>Keine Import-ID.</summary>
    public const string InvalidImportId = "RGL-0411";

    /// <summary>riegel-plant.json ist ungültig (z. B. doppelte Gruppen).</summary>
    public const string InvalidPlant = "RGL-0412";

    /// <summary>Maßnahmen brauchen keine Freigabe (spec 2.13).</summary>
    public const string MeasuresNeedNoApproval = "RGL-0413";

    /// <summary>Begründungsgrund: Rohstatus eines schon angewendeten Ergebnisses unbekannt (Zuschnitt nicht mehr gültig).</summary>
    public const string RawStatusUnknown = "RGL-0414";

    /// <summary>Der Export eines Laufs liegt nicht im Anlagenordner (absoluter Pfad, „..“, Laufwerk, Verknüpfung, Ordner).</summary>
    public const string RunSnapshotOutsidePlant = "RGL-0415";

    /// <summary>Der Export im Anlagenordner passt nicht zum Lauf (SHA-256 abweichend).</summary>
    public const string RunSnapshotMismatch = "RGL-0416";

    // ---- Registerprüfung (ValidationIssue.Code) ----

    /// <summary>ID doppelt.</summary>
    public const string DuplicateId = "RGL-0420";

    /// <summary>ID hat nicht die Form EX-0001 / TL-0001 / M-0001.</summary>
    public const string IdFormat = "RGL-0421";

    /// <summary>Regel gibt es im Katalog nicht.</summary>
    public const string UnknownRule = "RGL-0422";

    /// <summary>Regel ist zurückgezogen (Eintrag bleibt, markiert).</summary>
    public const string RuleRetired = "RGL-0423";

    /// <summary>Geltungsbereich leer.</summary>
    public const string EmptyScope = "RGL-0424";

    /// <summary>Gruppe gibt es in der Anlage nicht.</summary>
    public const string UnknownGroup = "RGL-0425";

    /// <summary>Rechner ist der Anlage nicht bekannt.</summary>
    public const string UnknownMachine = "RGL-0426";

    /// <summary>Begründung bzw. Grund kürzer als 20 Zeichen.</summary>
    public const string TextTooShort = "RGL-0427";

    /// <summary>vendorRequirement ohne reference.</summary>
    public const string ReferenceRequired = "RGL-0428";

    /// <summary>compensatingControl ohne compensatingMeasure.</summary>
    public const string CompensatingMeasureRequired = "RGL-0429";

    /// <summary>acceptedValue fehlt bei einer check-Regel.</summary>
    public const string AcceptedValueRequired = "RGL-0430";

    /// <summary>acceptedValue „*“ (jeder Wert).</summary>
    public const string AcceptedValueAny = "RGL-0431";

    /// <summary>Freigabe fehlt oder ist unvollständig (beantragt).</summary>
    public const string ApprovalPending = "RGL-0432";

    /// <summary>Freigabefelder ohne Inhalts-Hash (nicht über „approve“ eingetragen).</summary>
    public const string ApprovalNotBound = "RGL-0433";

    /// <summary>Inhalt nach der Freigabe geändert.</summary>
    public const string ChangedAfterApproval = "RGL-0434";

    /// <summary>reviewBy mehr als 36 Monate nach approvedOn.</summary>
    public const string ReviewByTooLate = "RGL-0435";

    /// <summary>reviewBy mehr als 12 Monate nach approvedOn.</summary>
    public const string ReviewByLong = "RGL-0436";

    /// <summary>reviewBy vor approvedOn.</summary>
    public const string ReviewByBeforeApproval = "RGL-0437";

    /// <summary>Abgelaufen (reviewBy überschritten).</summary>
    public const string Expired = "RGL-0438";

    /// <summary>Läuft binnen warnDays ab.</summary>
    public const string Expiring = "RGL-0439";

    /// <summary>riskAccepted für eine hohe Regel ohne berechtigte Rolle (riskAcceptanceRoles).</summary>
    public const string RiskAcceptanceRole = "RGL-0440";

    /// <summary>Überschneidung mit einem anderen Eintrag derselben Regel.</summary>
    public const string Overlap = "RGL-0441";

    /// <summary>Zuschnitt einer Regel hoher Schwere.</summary>
    public const string TailoringHighSeverity = "RGL-0442";

    /// <summary>Maßnahme überfällig.</summary>
    public const string MeasureOverdue = "RGL-0443";

    /// <summary>Pflichtfeld leer.</summary>
    public const string FieldRequired = "RGL-0444";

    /// <summary>plantId der Datei passt nicht zur Anlage.</summary>
    public const string PlantIdMismatch = "RGL-0445";

    /// <summary>Freigabedatum liegt in der Zukunft (noch nicht wirksam).</summary>
    public const string ApprovedInFuture = "RGL-0446";

    /// <summary>Ausnahme für eine Bestandsregel (die nie „nicht erfüllt“ ist).</summary>
    public const string InventoryRule = "RGL-0447";

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        ReadOnlyPlant, NotAPlant, ChangedByOthers, LockTimeout, SnapshotConflict, SidecarMismatch, InvalidRunId,
        ApprovalIncomplete, EntryNotFound, ApprovedStateMismatch, InvalidImportId, InvalidPlant, MeasuresNeedNoApproval,
        RawStatusUnknown, RunSnapshotOutsidePlant, RunSnapshotMismatch, DuplicateId, IdFormat, UnknownRule, RuleRetired, EmptyScope, UnknownGroup, UnknownMachine,
        TextTooShort, ReferenceRequired, CompensatingMeasureRequired, AcceptedValueRequired, AcceptedValueAny,
        ApprovalPending, ApprovalNotBound, ChangedAfterApproval, ReviewByTooLate, ReviewByLong, ReviewByBeforeApproval,
        Expired, Expiring, RiskAcceptanceRole, Overlap, TailoringHighSeverity, MeasureOverdue, FieldRequired,
        PlantIdMismatch, ApprovedInFuture, InventoryRule,
    ];

    /// <summary>Text zu einem Code dieses Bereichs in der aktuellen Sprache, mit Platzhaltern.</summary>
    public static string Message(string code, params object?[] args)
    {
        var key = "Error_" + code.Replace("-", "", StringComparison.Ordinal);
        var text = WorkflowStrings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? code;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    /// <summary>Fehler mit einem Code dieses Bereichs und dessen Text aus WorkflowStrings.</summary>
    internal static RiegelException Error(string code, params object?[] args) => new(code, Message(code, args));
}
