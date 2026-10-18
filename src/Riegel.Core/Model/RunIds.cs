// SPDX-License-Identifier: MIT
namespace Riegel.Core.Model;

/// <summary>
/// Lauf-IDs (spec 7.6): <c>&lt;yyyyMMddTHHmmssZ&gt;-&lt;6 hex&gt;</c>. Der Zeitstempel ist die Auswertungszeit, die sechs
/// Hex-Zeichen kommen aus dem SHA-256 über Export, Katalog, Register und Auswertungszeit – so kollidieren weder
/// Stapelimporte noch erneute Auswertungen, und gleiche Eingaben ergeben dieselbe ID.
/// </summary>
public static class RunIds
{
    public static string Create(DateTimeOffset evaluatedAtUtc, string snapshotSha256, string catalogueSha256, RegisterHashes registers)
    {
        var stamp = FileNames.Timestamp(evaluatedAtUtc);
        var material = string.Join("\n", snapshotSha256, catalogueSha256, registers.Exceptions, registers.Tailoring,
            registers.Measures, Json.UtcDateTimeOffsetConverter.Format(evaluatedAtUtc));
        return stamp + "-" + Hashes.Sha256Hex(material)[..6];
    }

    /// <summary>Hat <paramref name="id"/> die Form einer Lauf-ID?</summary>
    public static bool IsValid(string? id) =>
        id is { Length: 23 } && id[8] == 'T' && id[15] == 'Z' && id[16] == '-' &&
        id[..8].All(char.IsAsciiDigit) && id[9..15].All(char.IsAsciiDigit) && id[17..].All(char.IsAsciiHexDigitLower);
}
