// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.Model;
using Riegel.Core.Resources;

namespace Riegel.Core.Evidence;

/// <summary>
/// Fehlercodes von Excel, CSV, Nachweispaket, verify, Stichprobe und Abhilfe: RGL-0550 bis RGL-0599 (spec 7.13).
/// Die Texte stehen in DataOutputStrings unter <c>Error_RGL&lt;nnnn&gt;</c> (fünf Sprachen).
/// </summary>
public static class RglCodesDataOutputs
{
    public const int First = 550;
    public const int Last = 599;

    /// <summary>Nachweispaket ist keine lesbare ZIP-Datei.</summary>
    public const string ZipUnreadable = "RGL-0550";

    /// <summary>Nachweispaket größer als die Grenze (spec 7.12: 512 MB).</summary>
    public const string ZipTooLarge = "RGL-0551";

    /// <summary>Ein Eintrag oder die Summe der Einträge überschreitet die Grenze (ZIP-Bombe).</summary>
    public const string EntryTooLarge = "RGL-0552";

    /// <summary>Unsicherer Eintragsname („..“, absoluter Pfad, Backslash, Steuerzeichen, doppelt).</summary>
    public const string UnsafeEntryName = "RGL-0553";

    /// <summary>SHA256SUMS.txt fehlt oder ist nicht im sha256sum-Format.</summary>
    public const string ChecksumFileInvalid = "RGL-0554";

    /// <summary>manifest.json fehlt, ist kein gültiges JSON oder hat ein anderes Schema als riegel.evidence/1.</summary>
    public const string ManifestInvalid = "RGL-0555";

    /// <summary>Prüfsumme einer Datei stimmt nicht (verändert).</summary>
    public const string FileHashMismatch = "RGL-0556";

    /// <summary>Datei steht in SHA256SUMS.txt oder manifest.json, fehlt aber im Paket.</summary>
    public const string FileMissing = "RGL-0557";

    /// <summary>Datei im Paket steht weder in SHA256SUMS.txt noch in manifest.json.</summary>
    public const string FileNotListed = "RGL-0558";

    /// <summary>Gepackter Katalog passt nicht zur Angabe im Manifest.</summary>
    public const string CatalogueMismatch = "RGL-0559";

    /// <summary>Ein Ergebnis weicht bei der erneuten Auswertung ab.</summary>
    public const string ResultMismatch = "RGL-0560";

    /// <summary>Zählungen eines Laufs weichen bei der erneuten Auswertung ab.</summary>
    public const string SummaryMismatch = "RGL-0561";

    /// <summary>Baseline-Inhalt nicht enthalten – Ergebnisse dieser Baseline nicht nachprüfbar (kein Fehler).</summary>
    public const string BaselineNotVerifiable = "RGL-0562";

    /// <summary>Lauf nicht nachprüfbar: anderer Katalog oder Register nicht im Paket.</summary>
    public const string RunNotVerifiable = "RGL-0563";

    /// <summary>Rechner nicht im Nachweispaket (compare-snapshot).</summary>
    public const string HostNotInPack = "RGL-0564";

    /// <summary>Export oder Lauf im Paket nicht lesbar.</summary>
    public const string PackedFileUnreadable = "RGL-0565";

    /// <summary>Zu viele Einträge im Paket.</summary>
    public const string TooManyEntries = "RGL-0566";

    /// <summary>Lauf oder Export der Anlage lässt sich nicht packen (außerhalb des Anlagenordners, Prüfsumme, Bordmittel-Rohdaten).</summary>
    public const string RunNotPackable = "RGL-0567";

    /// <summary>Berichtsdatei für das Paket fehlt, ist zu groß oder hat einen unzulässigen Namen.</summary>
    public const string DocumentNotPackable = "RGL-0568";

    /// <summary>Abhilfe-Eintrag im Katalog unzulässig – übersprungen (Hinweis im README).</summary>
    public const string RemediationActionInvalid = "RGL-0569";

    /// <summary>Alter Wert nicht in die Rücknahme übernommen (Hinweis im README).</summary>
    public const string RollbackValueOmitted = "RGL-0570";

    /// <summary>Lauf im Paket passt nicht zum Manifest (ID oder Rechner).</summary>
    public const string RunManifestMismatch = "RGL-0571";

    /// <summary>
    /// Gepackter Katalog hat ID und Version des eingebauten, aber einen anderen Inhalt (Regeln verändert) – abweichend.
    /// </summary>
    public const string CatalogueNotBuiltIn = "RGL-0572";

    /// <summary>
    /// Gepackter Katalog ist nicht der eingebaute (andere Version): seine Regeln ließen sich nicht mit einer Referenz
    /// vergleichen. Kein Fehler, aber Exitcode 1 (mit der Riegel-Version prüfen, die das Paket schrieb).
    /// </summary>
    public const string CatalogueNotReference = "RGL-0573";

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        ZipUnreadable, ZipTooLarge, EntryTooLarge, UnsafeEntryName, ChecksumFileInvalid, ManifestInvalid, FileHashMismatch,
        FileMissing, FileNotListed, CatalogueMismatch, ResultMismatch, SummaryMismatch, BaselineNotVerifiable,
        RunNotVerifiable, HostNotInPack, PackedFileUnreadable, TooManyEntries, RunNotPackable, DocumentNotPackable,
        RemediationActionInvalid, RollbackValueOmitted, RunManifestMismatch, CatalogueNotBuiltIn, CatalogueNotReference,
    ];

    /// <summary>Text zu einem Code in der aktuellen Sprache, mit Platzhaltern.</summary>
    public static string Message(string code, params object?[] args) => Message(CultureInfo.CurrentUICulture, code, args);

    /// <summary>Text zu einem Code in <paramref name="culture"/>, mit Platzhaltern.</summary>
    public static string Message(CultureInfo culture, string code, params object?[] args)
    {
        var key = "Error_" + code.Replace("-", "", StringComparison.Ordinal);
        var text = DataOutputStrings.ResourceManager.GetString(key, culture) ?? code;
        return args.Length == 0 ? text : string.Format(culture, text, args);
    }

    /// <summary>Ausnahme mit Code und übersetztem Text.</summary>
    internal static RiegelException Error(string code, params object?[] args) => new(code, Message(code, args));
}
