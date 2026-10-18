// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.Resources;

namespace Riegel.Core.Reports.Pdf;

/// <summary>
/// Fehlercodes der PDF-Dokumente: RGL-0500 bis RGL-0549 (spec 7.13). Die Texte stehen in PdfStrings unter
/// <c>Error_RGL&lt;nnnn&gt;</c>, in fünf Sprachen.
/// </summary>
public static class RglCodesPdf
{
    public const int First = 500;
    public const int Last = 549;

    /// <summary>Die PDF-Datei ließ sich nicht schreiben (Ordner, Rechte, Datei in einem Betrachter geöffnet).</summary>
    public const string WriteFailed = "RGL-0500";

    /// <summary>Keine Schrift für die PDF-Dokumente gefunden (Linux: Paket fonts-dejavu-core o. ä. fehlt).</summary>
    public const string NoFont = "RGL-0501";

    /// <summary>Das Deckblatt-Logo ist größer als 1 MB.</summary>
    public const string LogoTooLarge = "RGL-0502";

    /// <summary>Das Deckblatt-Logo ist kein lesbares PNG oder JPEG (oder unplausibel groß in Pixeln).</summary>
    public const string LogoInvalid = "RGL-0503";

    /// <summary>Das Deckblatt-Logo fehlt oder ist nicht lesbar.</summary>
    public const string LogoUnreadable = "RGL-0504";

    /// <summary>
    /// Keine Schrift für die Sprache (zh-Hans) gefunden: das Dokument entsteht auf Englisch, mit Hinweis auf dem
    /// Deckblatt (spec 7.10). Kein Abbruch.
    /// </summary>
    public const string NoFontForLanguage = "RGL-0510";

    /// <summary>
    /// PDF/A angefordert: PDFsharp-MigraDoc 6.2.4 kann aus MigraDoc-Dokumenten kein gültiges PDF/A schreiben
    /// (docs/reports.md). Abbruch vor dem Schreiben.
    /// </summary>
    public const string PdfANotSupported = "RGL-0520";

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } =
        [WriteFailed, NoFont, LogoTooLarge, LogoInvalid, LogoUnreadable, NoFontForLanguage, PdfANotSupported];

    /// <summary>Text zu einem Code in der aktuellen Sprache der Oberfläche, mit Platzhaltern.</summary>
    public static string Message(string code, params object?[] args) => Message(CultureInfo.CurrentUICulture, code, args);

    /// <summary>Text zu einem Code in einer bestimmten Sprache (z. B. der des Dokuments).</summary>
    public static string Message(CultureInfo culture, string code, params object?[] args)
    {
        var key = "Error_" + code.Replace("-", "", StringComparison.Ordinal);
        var text = PdfStrings.ResourceManager.GetString(key, culture) ?? code;
        return args.Length == 0 ? text : string.Format(culture, text, args);
    }
}
