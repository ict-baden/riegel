// SPDX-License-Identifier: MIT
namespace Riegel.Core.Reports.Pdf;

/// <summary>
/// Fehlercodes der PDF-Dokumente: RGL-0500 bis RGL-0549 (spec 7.13; RGL-0510 = keine Schrift für die Sprache,
/// spec 7.10). Gerüst: nur der Bereich; die Codes legt das Paket pdf an, Texte in PdfStrings.
/// </summary>
public static class RglCodesPdf
{
    public const int First = 500;
    public const int Last = 549;

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } = [];
}
