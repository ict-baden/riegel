// SPDX-License-Identifier: MIT
namespace Riegel.Tools.RuleTool;

/// <summary>
/// Codes für Katalog-Inhalt und RuleTool: RGL-0300 bis RGL-0399 (spec 7.13; Pakete rules-a/b/c). Gerüst: nur der Bereich.
/// </summary>
public static class RglCodesCatalogue
{
    public const int First = 300;
    public const int Last = 399;

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } = [];
}
