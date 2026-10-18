// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.Model;
using Riegel.Core.Resources;

namespace Riegel.Core.Parsing;

/// <summary>
/// Fehlercodes des Lesens und der Parser: RGL-0250 bis RGL-0299 (spec 7.13). Die Texte stehen in ParsingStrings unter
/// <c>Error_RGL&lt;nnnn&gt;</c>. Jeder Lesefehler feindlicher oder kaputter Eingaben ist einer dieser Codes, nie ein Absturz
/// (spec 7.12).
/// </summary>
public static class RglCodesParsing
{
    public const int First = 250;
    public const int Last = 299;

    // ---- Exporte (riegel.snapshot/1) ----

    /// <summary>Export ist kein gültiges JSON (z. B. abgeschnitten).</summary>
    public const string SnapshotNotJson = "RGL-0250";

    /// <summary>Export hat eine unbekannte Version von riegel.snapshot.</summary>
    public const string SnapshotVersion = "RGL-0251";

    /// <summary>Export (entpackt) größer als erlaubt.</summary>
    public const string SnapshotTooLarge = "RGL-0252";

    /// <summary>Zu viele Einträge.</summary>
    public const string TooManyItems = "RGL-0253";

    /// <summary>Zeichenkette länger als erlaubt.</summary>
    public const string StringTooLong = "RGL-0254";

    /// <summary>JSON zu tief verschachtelt.</summary>
    public const string JsonTooDeep = "RGL-0255";

    /// <summary>gzip-Datei beschädigt.</summary>
    public const string GzipInvalid = "RGL-0256";

    /// <summary>Export inhaltlich ungültig (doppelte Schlüssel, falsche Präfixe, falsche Kodierung).</summary>
    public const string SnapshotInvalid = "RGL-0257";

    /// <summary>parsed:*-Eintrag in einem nicht synthetischen Export verworfen und neu berechnet (Hinweis am Eintrag).</summary>
    public const string ParsedItemRecomputed = "RGL-0258";

    /// <summary>Rohdaten für einen abgeleiteten Eintrag fehlen oder waren nicht lesbar.</summary>
    public const string RawSourceUnavailable = "RGL-0259";

    // ---- Bordmittel-Pakete (spec 7.5) ----

    /// <summary>Ordner ist kein Bordmittel-Paket.</summary>
    public const string BundleInvalid = "RGL-0260";

    /// <summary>Datei im Paket größer als erlaubt.</summary>
    public const string BundleFileTooLarge = "RGL-0261";

    /// <summary>Datei ist kein gültiges UTF-16 (bzw. hat die falsche Kodierung).</summary>
    public const string InvalidEncoding = "RGL-0262";

    /// <summary>.reg-Datei fehlerhaft.</summary>
    public const string RegFileInvalid = "RGL-0263";

    /// <summary>Sicherheitsvorlage (INF) fehlerhaft.</summary>
    public const string InfInvalid = "RGL-0264";

    /// <summary>CSV der Überwachungsrichtlinie fehlerhaft.</summary>
    public const string AuditCsvInvalid = "RGL-0265";

    /// <summary>Ausgabe von reg query fehlerhaft.</summary>
    public const string RegQueryInvalid = "RGL-0266";

    /// <summary>Befehl im Paket endete mit Fehler (exitcodes.txt); seine Daten werden nicht verwendet.</summary>
    public const string BundleStepFailed = "RGL-0267";

    /// <summary>Bestandteil fehlt im Paket (z. B. auditpol.csv unter Windows XP).</summary>
    public const string BundlePartMissing = "RGL-0268";

    // ---- Rohdaten-Parser ----

    /// <summary>Registry.pol fehlerhaft.</summary>
    public const string RegistryPolInvalid = "RGL-0270";

    /// <summary>Prüfsumme von raw:registry-pol passt nicht zum Inhalt.</summary>
    public const string RawHashMismatch = "RGL-0271";

    /// <summary>gpresult-XML fehlerhaft.</summary>
    public const string GpresultInvalid = "RGL-0272";

    // ---- Kunden-Baselines (spec 2.15) ----

    /// <summary>Format der Baseline nicht erkannt.</summary>
    public const string BaselineUnknownFormat = "RGL-0280";

    /// <summary>Baseline ungültig.</summary>
    public const string BaselineInvalid = "RGL-0281";

    /// <summary>Kennung der Baseline oder einer Regel ungültig.</summary>
    public const string BaselineIdInvalid = "RGL-0282";

    /// <summary>Zwei Regeln einer Baseline haben dieselbe Kennung.</summary>
    public const string BaselineIdCollision = "RGL-0283";

    /// <summary>Zeile der CSV-Vorlage ungültig.</summary>
    public const string BaselineCsvInvalid = "RGL-0284";

    /// <summary>Zu viele Regeln oder Zeilen.</summary>
    public const string BaselineTooLarge = "RGL-0285";

    /// <summary>GPO-Sicherung ohne Computereinstellungen.</summary>
    public const string BaselineEmpty = "RGL-0286";

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        SnapshotNotJson, SnapshotVersion, SnapshotTooLarge, TooManyItems, StringTooLong, JsonTooDeep, GzipInvalid, SnapshotInvalid,
        ParsedItemRecomputed, RawSourceUnavailable, BundleInvalid, BundleFileTooLarge, InvalidEncoding, RegFileInvalid, InfInvalid,
        AuditCsvInvalid, RegQueryInvalid, BundleStepFailed, BundlePartMissing, RegistryPolInvalid, RawHashMismatch, GpresultInvalid,
        BaselineUnknownFormat, BaselineInvalid, BaselineIdInvalid, BaselineIdCollision, BaselineCsvInvalid,
        BaselineTooLarge, BaselineEmpty,
    ];

    /// <summary>Text zu einem Code in der aktuellen Sprache, mit Platzhaltern.</summary>
    public static string Message(string code, params object?[] args)
    {
        var key = "Error_" + code.Replace("-", "", StringComparison.Ordinal);
        var text = ParsingStrings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? code;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    /// <summary>Fehler mit einem Code dieses Bereichs und seinem übersetzten Text.</summary>
    internal static RiegelException Error(string code, params object?[] args) => new(code, Message(code, args));

    /// <summary>Kurzer Fehlertext für den <c>error</c> eines Eintrags: „RGL-nnnn: …“.</summary>
    internal static string ItemError(string code, params object?[] args) => code + ": " + Message(code, args);
}
