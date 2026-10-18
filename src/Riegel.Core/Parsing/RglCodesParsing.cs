// SPDX-License-Identifier: MIT
namespace Riegel.Core.Parsing;

/// <summary>
/// Fehlercodes des Lesens und der Parser: RGL-0250 bis RGL-0299 (spec 7.13). Gerüst: nur der Bereich; die Codes legt
/// das Paket parsing an, Texte in ParsingStrings.
/// </summary>
public static class RglCodesParsing
{
    public const int First = 250;
    public const int Last = 299;

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } = [];
}
