// SPDX-License-Identifier: MIT
using System.Text.Json.Serialization;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Model;

// ---- Plant (spec 7.6) ----

/// <summary>
/// Anlage (<c>riegel-plant.json</c>, <c>riegel.plant/1</c>). Die Angaben <see cref="Folder"/>, <see cref="ReadOnly"/>
/// und <see cref="FileSha256"/> stehen nicht in der Datei; der Anlagenspeicher setzt sie beim Öffnen.
/// </summary>
public sealed record Plant(string Schema, string Id, string Name, string? Description, IReadOnlyList<MachineGroup> Groups,
    IReadOnlyList<PlantMachine> Machines, PlantSettings Settings) : IAfterRead<Plant>
{
    /// <summary>Fehlen die Einstellungen in der Datei, gelten die Standardwerte (statt dass die Anlage unlesbar wird).</summary>
    Plant IAfterRead<Plant>.AfterRead() => Settings is null ? this with { Settings = PlantSettings.Default } : this;

    public IReadOnlyList<MachineGroup> Groups { get; init; } = Groups ?? [];
    public IReadOnlyList<PlantMachine> Machines { get; init; } = Machines ?? [];

    /// <summary>Anlagenordner (vollständiger Pfad), null bei einer Anlage, die nur im Speicher existiert.</summary>
    [JsonIgnore] public string? Folder { get; init; }

    /// <summary>Prüfermodus: es wird nichts in den Anlagenordner geschrieben (spec 7.6).</summary>
    [JsonIgnore] public bool ReadOnly { get; init; }

    /// <summary>SHA-256 von riegel-plant.json beim Laden (Erwartungswert für SavePlant).</summary>
    [JsonIgnore] public string? FileSha256 { get; init; }
}

/// <summary>Gruppe von Rechnern mit gleicher Rolle (Zuschnitt und Soll-Konfiguration je Rolle).</summary>
public sealed record MachineGroup(string Id, string Name, MachineRole Role);

[JsonConverter(typeof(CamelCaseEnumConverter<MachineRole>))]
public enum MachineRole { Other, Hmi, EngineeringStation, Historian, Server, Office, JumpHost }

/// <summary>Rechner der Anlage. Zuordnung über den Hostnamen (spec 2.8).</summary>
/// <param name="EsuCovered">Rechner hat Extended Security Updates (LIFE-01).</param>
public sealed record PlantMachine(string HostName, string? GroupId, string? Label, bool EsuCovered, string? Notes,
    MachineIdentity? Identity);

/// <summary>Maschinenidentität für den Auslieferungszustand (spec 2.17).</summary>
public sealed record MachineIdentity(string? MachineType, string? SerialNumber, string? OrderNumber,
    DateOnly? DeliveryDate, string? PlantSoftware, string? Builder, string? Location);

/// <summary>Einstellungen der Anlage; Standardwerte in <see cref="Default"/>.</summary>
/// <param name="WarnDays">Ausnahmen „läuft bald ab“ (30).</param>
/// <param name="StaleDays">Export „veraltet“ (30).</param>
/// <param name="UpdateMaxDays">Höchstalter des letzten kumulativen Updates (35).</param>
/// <param name="UnusedAccountDays">Konto „unbenutzt“ (90).</param>
/// <param name="Confidentiality">Vertraulichkeitsvermerk; null = Standard aus den Texten.</param>
/// <param name="ReportLanguage">Sprache der Berichte; null = Sprache der Oberfläche.</param>
/// <param name="PerformedBy">„Durchgeführt von“ auf dem Deckblatt.</param>
/// <param name="CoverLogoFile">PNG/JPEG im Anlagenordner, höchstens 1 MB, nie ausgeliefert.</param>
/// <param name="RiskAcceptanceRoles">Rollen, die riskAccepted für hohe Schwere freigeben dürfen; leer = alle.</param>
/// <param name="OperatorRole">Rolle des Betreibers im Nachweispaket.</param>
public sealed record PlantSettings(int WarnDays, int StaleDays, int UpdateMaxDays, int UnusedAccountDays,
    string? Confidentiality, IReadOnlyList<SignOffRole> SignOff, string? ReportLanguage, ReportPreset ReportPreset,
    string? PerformedBy, string? CoverLogoFile, IReadOnlyList<string> RiskAcceptanceRoles, string? OperatorRole)
{
    public IReadOnlyList<SignOffRole> SignOff { get; init; } = SignOff ?? [];
    public IReadOnlyList<string> RiskAcceptanceRoles { get; init; } = RiskAcceptanceRoles ?? [];

    /// <summary>
    /// Standard: 30/30/35/90 Tage, Freigabe erstellt/geprüft/freigegeben mit den Rollennamen der aktuellen Sprache
    /// (CommonStrings), Preset audit.
    /// </summary>
    public static PlantSettings Default => new(30, 30, 35, 90, null,
        [
            new(SignOffSteps.Created, Resources.CommonStrings.Role_PlantOwner),
            new(SignOffSteps.Reviewed, Resources.CommonStrings.Role_SecurityOfficer),
            new(SignOffSteps.Approved, Resources.CommonStrings.Role_PlantManagement),
        ],
        null, ReportPreset.Audit, null, null, [], null);
}

/// <summary>Eine Zeile der Freigabe (Rolle, nie ein Name).</summary>
/// <param name="Step">created, reviewed, approved oder accepted (<see cref="SignOffSteps"/>).</param>
public sealed record SignOffRole(string Step, string Role);

/// <summary>Schritte der Freigabe (spec 5 O4 Abschnitt 10, 2.17).</summary>
public static class SignOffSteps
{
    public const string Created = "created";
    public const string Reviewed = "reviewed";
    public const string Approved = "approved";
    public const string Accepted = "accepted";
}

[JsonConverter(typeof(CamelCaseEnumConverter<ReportPreset>))]
public enum ReportPreset { Audit, AsDelivered }
