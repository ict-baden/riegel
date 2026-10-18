// SPDX-License-Identifier: MIT
namespace Riegel.Core.Evaluation;

/// <summary>
/// Fehler- und Grundcodes der Engine: RGL-0200 bis RGL-0249 (spec 7.13). Gerüst: nur der Bereich; die Codes legt das
/// Paket engine an (z. B. je Eintragsstatus ein Grund für „nicht prüfbar“), Texte in EngineStrings.
/// </summary>
public static class RglCodesEngine
{
    public const int First = 200;
    public const int Last = 249;

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } = [];
}
