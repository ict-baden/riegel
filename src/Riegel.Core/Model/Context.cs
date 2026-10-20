// SPDX-License-Identifier: MIT
using System.Text.Json.Serialization;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Model;

// ---- Kontext und Optionen der Dienste (spec 7.2) ----

/// <summary>Was die Engine außer Export und Katalog braucht (spec 2.8 Zeitbasis).</summary>
/// <param name="EvaluationDate">Kalendertag für Supportende, Gültigkeit, Fälligkeiten.</param>
/// <param name="EvaluatedAtUtc">Uhr der Auswertung (ClockSuspect).</param>
/// <param name="ImportedAtUtc">Importzeit (ClockSuspect), null ohne Anlagenordner.</param>
/// <param name="UpdateMaxDays">Plant-Einstellung (35).</param>
/// <param name="UnusedAccountDays">Plant-Einstellung (90).</param>
/// <param name="EsuCovered">Rechner hat ESU (LIFE-01).</param>
/// <param name="Language">Sprache für Detailtexte.</param>
public sealed record EvaluationContext(DateOnly EvaluationDate, DateTimeOffset EvaluatedAtUtc, DateTimeOffset? ImportedAtUtc,
    int UpdateMaxDays, int UnusedAccountDays, bool EsuCovered, string Language);

/// <summary>
/// Grenzen für feindliche Eingaben (spec 7.12). Jede Überschreitung ist ein RGL-Code, nie ein Absturz.
/// </summary>
public sealed record ReadLimits(
    long MaxSnapshotBytes = 64L * 1024 * 1024,
    int MaxItems = 500_000,
    int MaxStringChars = 1024 * 1024,
    int MaxJsonDepth = 64,
    long MaxRegisterBytes = 10L * 1024 * 1024,
    int MaxRegisterEntries = 10_000,
    long MaxRegistryPolBytes = 16L * 1024 * 1024,
    long MaxTextFileBytes = 16L * 1024 * 1024,
    long MaxBaselineCsvBytes = 10L * 1024 * 1024,
    int MaxBaselineCsvRows = 50_000,
    long MaxEvidenceZipBytes = 512L * 1024 * 1024,
    long MaxEvidenceEntryBytes = 64L * 1024 * 1024,
    long MaxLogoBytes = 1024 * 1024,
    long MaxPlantFileBytes = 10L * 1024 * 1024,
    long MaxCatalogueBytes = 64L * 1024 * 1024,
    int ToolTimeoutSeconds = 60)
{
    /// <summary>Die Grenzen aus spec 7.12.</summary>
    public static ReadLimits Default { get; } = new();
}

/// <summary>Prüfung der .sha256-Begleitdatei (sha256sum-Format).</summary>
public sealed record HashCheck(HashCheckStatus Status, string? Expected, string Actual, string? SidecarPath);

[JsonConverter(typeof(CamelCaseEnumConverter<HashCheckStatus>))]
public enum HashCheckStatus { Ok, Mismatch, Missing, Malformed }

/// <summary>
/// Herkunft der Registry-Werte (spec 2.10), Schlüssel aus <see cref="KeyOf"/>. <see cref="Determinable"/> false =
/// ohne Admin/gpresult nicht bestimmbar (dann Unknown).
/// </summary>
public sealed record AttributionMap(IReadOnlyDictionary<string, ValueAttribution> Entries, bool Determinable)
{
    public IReadOnlyDictionary<string, ValueAttribution> Entries { get; init; } = Entries ?? new Dictionary<string, ValueAttribution>();

    /// <summary>Keine Zuordnung (Stub, Linux ohne Rohdaten).</summary>
    public static AttributionMap Empty { get; } = new(new Dictionary<string, ValueAttribution>(), false);

    /// <summary>Normalisierter Schlüssel „hklm\pfad|wert“ (klein, invariant, ohne „reg:“ und ohne Schrägstrich am Ende).</summary>
    public static string KeyOf(string registryKey, string valueName)
    {
        var key = registryKey.StartsWith("reg:", StringComparison.OrdinalIgnoreCase) ? registryKey[4..] : registryKey;
        return key.TrimEnd('\\').ToLowerInvariant() + "|" + valueName.ToLowerInvariant();
    }

    /// <summary>Herkunft eines Werts, null wenn nicht zugeordnet.</summary>
    public ValueAttribution? Find(string registryKey, string valueName) =>
        Entries.TryGetValue(KeyOf(registryKey, valueName), out var found) ? found : null;
}

/// <summary>Herkunft eines Werts; <paramref name="Detail"/> z. B. der GPO-Name.</summary>
/// <param name="Experimental">Domänen-GPO-Zuordnung ist in v1 experimentell (spec 2.10).</param>
public sealed record ValueAttribution(ValueSource Source, string? Detail, bool Experimental);

/// <summary>Optionen des Imports (spec 7.7 <c>import</c>).</summary>
/// <param name="Evaluate">Nach dem Import auswerten und Läufe speichern.</param>
/// <param name="Limits">null = <see cref="ReadLimits.Default"/>.</param>
/// <param name="SkipKnown">
/// Exporte, die schon in der Anlage liegen und einen Lauf haben (gleicher SHA-256), überspringen: kein neuer Lauf, kein
/// neuer Eintrag im Importnachweis (wiederkehrender Import eines Freigabeordners). Sind alle bekannt, entsteht kein
/// Importnachweis; der zurückgegebene hat dann keine Dateien.
/// </param>
public sealed record ImportOptions(string? CollectedByRole, string? WitnessRole, string? OperatorRole, bool Evaluate = true,
    ReadLimits? Limits = null, bool SkipKnown = false);

/// <summary>Optionen einer Auswertung.</summary>
/// <param name="EvaluationDate">null = heute (UTC) laut Uhr der Dienste.</param>
/// <param name="Catalogue">null = eingebetteter Katalog (verify übergibt den gepackten).</param>
/// <param name="Registers">null = Register der Anlage (verify übergibt die gepackten).</param>
/// <param name="Store">Lauf im Anlagenordner speichern (check --no-store: false).</param>
public sealed record RunOptions(DateOnly? EvaluationDate = null, DateTimeOffset? ImportedAtUtc = null, string Language = "en",
    bool Store = true, ReadLimits? Limits = null, ICatalogue? Catalogue = null, RegisterSet? Registers = null,
    string? ImportId = null, Plant? Plant = null);
