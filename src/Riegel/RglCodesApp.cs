// SPDX-License-Identifier: MIT
namespace Riegel;

/// <summary>
/// Fehlercodes der Oberfläche: RGL-0600 bis RGL-0699 (spec 7.13). Gerüst: nur der Bereich; die Codes legt das Paket
/// app an, Texte in UiStrings.
/// </summary>
public static class RglCodesApp
{
    public const int First = 600;
    public const int Last = 699;

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } = [];
}
