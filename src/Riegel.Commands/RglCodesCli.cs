// SPDX-License-Identifier: MIT
namespace Riegel.Commands;

/// <summary>
/// Fehlercodes der Befehlszeile: RGL-0450 bis RGL-0499 (spec 7.13). Gerüst: der Bereich und der Code für
/// Aufruffehler (Exitcode 3); die übrigen legt das Paket cli an, Texte in CliStrings.
/// </summary>
public static class RglCodesCli
{
    public const int First = 450;
    public const int Last = 499;

    /// <summary>Unbekannter Befehl, unbekannte Option oder fehlender Wert.</summary>
    public const string UsageError = "RGL-0450";

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } = [UsageError];
}
