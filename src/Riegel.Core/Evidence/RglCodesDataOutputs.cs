// SPDX-License-Identifier: MIT
namespace Riegel.Core.Evidence;

/// <summary>
/// Fehlercodes von Excel, CSV, Nachweispaket, verify, Stichprobe und Abhilfe: RGL-0550 bis RGL-0599 (spec 7.13).
/// Gerüst: nur der Bereich; die Codes legt das Paket data-outputs an, Texte in DataOutputStrings.
/// </summary>
public static class RglCodesDataOutputs
{
    public const int First = 550;
    public const int Last = 599;

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } = [];
}
