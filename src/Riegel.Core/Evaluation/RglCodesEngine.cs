// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.Model;
using Riegel.Core.Resources;

namespace Riegel.Core.Evaluation;

/// <summary>
/// Fehler- und Grundcodes der Engine: RGL-0200 bis RGL-0249 (spec 7.13). Die Texte stehen in EngineStrings unter
/// <c>Error_RGL&lt;nnnn&gt;</c>. 0200–0219: Katalog laden und prüfen (Ausnahme beim Laden); 0220–0239: Gründe für
/// „nicht prüfbar“ (in <see cref="RuleResult.ReasonCode"/>); 0240–0249: Gründe für „nicht anwendbar“.
/// </summary>
public static class RglCodesEngine
{
    public const int First = 200;
    public const int Last = 249;

    // ---------------------------------------------------------------- Katalog (Ladefehler)

    /// <summary>Katalog ungültig (allgemein).</summary>
    public const string CatalogueInvalid = "RGL-0200";

    /// <summary>Unbekannte Prüfart (nicht im geschlossenen Vokabular, spec 2.3).</summary>
    public const string UnknownCheckType = "RGL-0201";

    /// <summary>Unbekanntes Feld in einer Regel, Prüfung oder Tabelle.</summary>
    public const string UnknownField = "RGL-0202";

    /// <summary>Operator passt nicht zum Werttyp, oder Sollwert fehlt bzw. hat den falschen Typ.</summary>
    public const string OperatorMismatch = "RGL-0203";

    /// <summary>Standardwert (osDefaults, missingPass) ohne Quelle (https) oder ohne Datum.</summary>
    public const string DefaultWithoutSource = "RGL-0204";

    /// <summary>whenMissing osDefault ohne osDefaults, pass ohne missingPass, oder für diese Prüfart nicht erlaubt.</summary>
    public const string MissingRuleIncomplete = "RGL-0205";

    /// <summary>Unbekannte Fähigkeit in appliesTo.capability.</summary>
    public const string UnknownCapability = "RGL-0206";

    /// <summary>Unbekannter Fakt (oder falscher Werttyp) in appliesTo.when.</summary>
    public const string UnknownFact = "RGL-0207";

    /// <summary>Fähigkeit in mehreren Dateien von rules/capabilities/ definiert.</summary>
    public const string DuplicateCapability = "RGL-0208";

    /// <summary>Regel-ID doppelt.</summary>
    public const string DuplicateRuleId = "RGL-0209";

    /// <summary>Regel ungültig (ID, Kategorie, Anwendbarkeit, leere Verknüpfung, Bestandsprüfung in einer Prüfregel …).</summary>
    public const string InvalidRule = "RGL-0210";

    /// <summary>Tabelle oder Datei des Katalogs ungültig (Lebenszyklus, Builds, Fähigkeiten, Daten, unbekannte Datei).</summary>
    public const string InvalidTable = "RGL-0211";

    /// <summary>Kunden-Baseline passt nicht (RuleSetId, IMP-Präfix, Baseline doppelt).</summary>
    public const string BaselineConflict = "RGL-0212";

    // ---------------------------------------------------------------- Gründe für „nicht prüfbar“ (je Eintragsstatus und mehr)

    /// <summary>Eintrag: Zugriff verweigert.</summary>
    public const string ItemDenied = "RGL-0220";

    /// <summary>Eintrag: Fehler bei der Erfassung.</summary>
    public const string ItemError = "RGL-0221";

    /// <summary>Eintrag: nicht erfasst.</summary>
    public const string ItemNotCollected = "RGL-0222";

    /// <summary>Eintrag: WMI-Namespace oder -Klasse fehlt.</summary>
    public const string ItemNamespaceMissing = "RGL-0223";

    /// <summary>Eintrag abgeschnitten, oder der Erfassungsbereich ist unvollständig.</summary>
    public const string ItemTruncated = "RGL-0224";

    /// <summary>Eintrag vom Sammler als nicht anwendbar markiert.</summary>
    public const string ItemNotApplicable = "RGL-0225";

    /// <summary>Fehlen nicht beweisbar: Schlüssel außerhalb der Abdeckung, Wert außerhalb des Wertfilters.</summary>
    public const string AbsenceNotProvable = "RGL-0226";

    /// <summary>Wert nicht gesetzt; die Regel sagt whenMissing notCheckable.</summary>
    public const string NotSetNoDefault = "RGL-0227";

    /// <summary>Wert nicht gesetzt; kein belegter Standardwert passt zu diesem Rechner.</summary>
    public const string DefaultUnknown = "RGL-0228";

    /// <summary>Anwendbarkeit nicht bestimmbar (Fakt oder Kopf-Feld unbekannt).</summary>
    public const string ApplicabilityUnknown = "RGL-0229";

    /// <summary>Nur konfiguriert, Wirkung nicht nachgewiesen.</summary>
    public const string ConfiguredOnly = "RGL-0230";

    /// <summary>Nur heuristische Belege (SecurityCenter2).</summary>
    public const string HeuristicOnly = "RGL-0231";

    /// <summary>Uhr des Zielrechners unplausibel – Altersangaben nicht prüfbar.</summary>
    public const string ClockSuspect = "RGL-0232";

    /// <summary>Unerwarteter Wert (Form oder JSON-Typ) in den Rohdaten.</summary>
    public const string UnexpectedValue = "RGL-0233";

    /// <summary>Geschwärzter Wert lässt sich nicht vergleichen.</summary>
    public const string RedactedValue = "RGL-0234";

    /// <summary>Daten des Katalogs fehlen oder sind zu alt (Lebenszyklus, Builds, Muster).</summary>
    public const string CatalogueDataMissing = "RGL-0235";

    /// <summary>Zustand von Microsoft Defender nicht bestimmbar.</summary>
    public const string DefenderStateUnknown = "RGL-0236";

    /// <summary>Kein Teil der Prüfung lieferte einen entscheidenden Beleg.</summary>
    public const string NoDecisiveEvidence = "RGL-0237";

    /// <summary>Kontonamen in den Benutzerrechten nicht in SIDs aufgelöst.</summary>
    public const string UnresolvedNames = "RGL-0238";

    /// <summary>Auswertung der Regel fehlgeschlagen (Programmfehler, nie ein Absturz).</summary>
    public const string EvaluationFailed = "RGL-0239";

    // ---------------------------------------------------------------- Gründe für „nicht anwendbar“

    /// <summary>Betriebssystemart (Client/Server) passt nicht.</summary>
    public const string NotApplicableOs = "RGL-0240";

    /// <summary>Build außerhalb des Bereichs der Regel.</summary>
    public const string NotApplicableBuild = "RGL-0241";

    /// <summary>Domänenmitgliedschaft passt nicht.</summary>
    public const string NotApplicableDomain = "RGL-0242";

    /// <summary>Domänencontroller, die Regel schließt sie aus.</summary>
    public const string NotApplicableDomainController = "RGL-0243";

    /// <summary>Faktbedingung (appliesTo.when) trifft nicht zu.</summary>
    public const string NotApplicableCondition = "RGL-0244";

    /// <summary>Funktion auf diesem Betriebssystem nicht vorhanden.</summary>
    public const string NotApplicableCapability = "RGL-0245";

    /// <summary>Virenschutz eines Drittanbieters aktiv.</summary>
    public const string NotApplicableThirdPartyAv = "RGL-0246";

    /// <summary>Dienst, Feature oder Zeilen fehlen, und die Regel sagt dafür notApplicable.</summary>
    public const string NotApplicableAbsent = "RGL-0247";

    /// <summary>Regel zurückgezogen (state retired).</summary>
    public const string Retired = "RGL-0248";

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        CatalogueInvalid, UnknownCheckType, UnknownField, OperatorMismatch, DefaultWithoutSource, MissingRuleIncomplete,
        UnknownCapability, UnknownFact, DuplicateCapability, DuplicateRuleId, InvalidRule, InvalidTable, BaselineConflict,
        ItemDenied, ItemError, ItemNotCollected, ItemNamespaceMissing, ItemTruncated, ItemNotApplicable, AbsenceNotProvable,
        NotSetNoDefault, DefaultUnknown, ApplicabilityUnknown, ConfiguredOnly, HeuristicOnly, ClockSuspect, UnexpectedValue,
        RedactedValue, CatalogueDataMissing, DefenderStateUnknown, NoDecisiveEvidence, UnresolvedNames, EvaluationFailed,
        NotApplicableOs, NotApplicableBuild, NotApplicableDomain, NotApplicableDomainController, NotApplicableCondition,
        NotApplicableCapability, NotApplicableThirdPartyAv, NotApplicableAbsent, Retired,
    ];

    /// <summary>Text zu einem Code der Engine in der aktuellen Sprache, mit Platzhaltern.</summary>
    public static string Message(string code, params object?[] args) => Message(CultureInfo.CurrentUICulture, code, args);

    /// <summary>Text zu einem Code der Engine in einer bestimmten Sprache.</summary>
    public static string Message(CultureInfo culture, string code, params object?[] args)
    {
        var key = "Error_" + code.Replace("-", "", StringComparison.Ordinal);
        var text = EngineStrings.ResourceManager.GetString(key, culture) ?? code;
        return args.Length == 0 ? text : string.Format(culture, text, args);
    }

    /// <summary>Ausnahme mit einem Code der Engine und dessen Text.</summary>
    public static RiegelException Exception(string code, params object?[] args) => new(code, Message(code, args));
}
