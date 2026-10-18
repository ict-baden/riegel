// SPDX-License-Identifier: MIT
namespace Riegel.Core.Plants;

/// <summary>
/// Fehlercodes von Anlagenordner, Läufen, Registern, Vergleich und Berichtsdaten: RGL-0400 bis RGL-0449 (spec 7.13).
/// Gerüst: nur der Bereich und die beiden Codes, die spec 7.6 schon festlegt; Texte in WorkflowStrings.
/// </summary>
public static class RglCodesWorkflow
{
    public const int First = 400;
    public const int Last = 449;

    /// <summary>„von jemand anderem geändert“: SHA-256 der Datei passt nicht zum Erwartungswert (spec 7.6).</summary>
    public const string ChangedByOthers = "RGL-0403";

    /// <summary>Sperrdatei .riegel.lock nicht binnen 10 s erhalten (spec 7.6).</summary>
    public const string LockTimeout = "RGL-0404";

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } = [ChangedByOthers, LockTimeout];
}
