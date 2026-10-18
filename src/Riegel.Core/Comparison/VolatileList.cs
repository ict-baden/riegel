// SPDX-License-Identifier: MIT
namespace Riegel.Core.Comparison;

/// <summary>
/// Flüchtige Werte, die der Rohwertvergleich nicht meldet (spec 2.14): Signaturalter und -versionen, Zeitstempel,
/// Daten des Update-Verlaufs, Laufzeit seit dem Start, Dauern. Sie ändern sich auf jedem gesunden Rechner täglich und
/// würden echte Abweichungen verdecken. Die Liste steht auch in docs/scoring-and-comparison.md.
/// </summary>
internal static class VolatileList
{
    /// <summary>Ganze Einträge: der Update-Verlauf ist eine Liste von Ereignissen, keine Konfiguration (Patchstand: UBR, QFE).</summary>
    public static IReadOnlyList<string> Items { get; } = ["api:windows-update-history"];

    /// <summary>Registry-Schlüssel (mit Unterschlüsseln), deren Werte Signatur- und Aktualisierungsstände sind.</summary>
    public static IReadOnlyList<string> RegistryKeyPrefixes { get; } = [@"reg:HKLM\SOFTWARE\Microsoft\Windows Defender\Signature Updates"];

    /// <summary>Namen von Eigenschaften oder Werten (letzter Teil des Pfads), ohne Groß/klein.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        // Defender: Signaturen und Versionen der Engine/Plattform (MSFT_MpComputerStatus)
        "AntivirusSignatureLastUpdated", "AntivirusSignatureAge", "AntivirusSignatureVersion",
        "AntispywareSignatureLastUpdated", "AntispywareSignatureAge", "AntispywareSignatureVersion",
        "NISSignatureLastUpdated", "NISSignatureAge", "NISSignatureVersion",
        "AMProductVersion", "AMEngineVersion", "AMServiceVersion", "NISEngineVersion",
        // Defender in der Registry
        "SignaturesLastUpdated", "ASSignatureVersion", "AVSignatureVersion", "EngineVersion", "SignatureUpdateLastAttempted",
        // Zeitstempel und Laufzeit
        "LastBootUpTime", "timestamp", "timeCreatedUtc", "bootTimeUtc",
        // lokale Konten: letzte Anmeldung, Alter des Kennworts
        "lastLogonDate", "passwordAgeDays",
        // Dauern
        "durationMs",
    ];

    private static readonly HashSet<string> NameSet = new(Names, StringComparer.OrdinalIgnoreCase);

    /// <summary>Gehört der ganze Eintrag zur flüchtigen Liste?</summary>
    public static bool IsVolatileItem(string key) =>
        Items.Any(i => string.Equals(i, key, StringComparison.OrdinalIgnoreCase)) ||
        RegistryKeyPrefixes.Any(p => key.Equals(p, StringComparison.OrdinalIgnoreCase) || key.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase));

    /// <summary>Ist dieser Pfad in einem Eintrag flüchtig (letzter Teil in <see cref="Names"/>)?</summary>
    public static bool IsVolatilePath(string path)
    {
        var last = path.Split('/')[^1];
        return NameSet.Contains(last);
    }

    /// <summary>Felder des Rechnerkopfs, die nicht verglichen werden (Laufzeit seit dem Start).</summary>
    public static IReadOnlyList<string> HeaderFields { get; } = ["lastBootUtc"];
}
