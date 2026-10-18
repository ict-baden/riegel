// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.Json.Serialization;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Model;

// ---- Snapshot (O1, spec 7.3) ----
// Rohdaten eines Rechners, wie Sammler, App oder Bordmittel-Paket sie liefern. Keine Bewertung, nur Fakten mit Status.

/// <summary>Ein Export (<c>riegel.snapshot/1</c>): Kopf, Erfassungsumfang und alle Einträge.</summary>
/// <param name="Schema">Immer <see cref="SchemaIds.Snapshot"/>.</param>
/// <param name="CollectedAtUtc">Uhr des Zielrechners beim Erfassen (Zeitbasis für Altersangaben, spec 2.8).</param>
/// <param name="Coverage">Ein Eintrag je erfasstem Registry-Bereich; entscheidet zwischen KeyMissing und NotCollected.</param>
public sealed record Snapshot(string Schema, CollectorInfo Collector, DateTimeOffset CollectedAtUtc, bool IsAdmin,
    SnapshotOrigin Origin, CollectionOptions Options, MachineInfo Machine, IReadOnlyList<CoverageEntry> Coverage,
    IReadOnlyList<SnapshotItem> Items)
{
    public IReadOnlyList<CoverageEntry> Coverage { get; init; } = Coverage ?? [];
    public IReadOnlyList<SnapshotItem> Items { get; init; } = Items ?? [];
}

/// <summary>Wer erfasst hat: Name und Version des Sammlers, SHA-256 seiner Exe, CLR-Version, 64-Bit-Prozess.</summary>
public sealed record CollectorInfo(string Name, string Version, string? ExeSha256, string Clr, bool Is64BitProcess);

/// <summary>Schalter der Erfassung (spec 7.4): <c>--minimal</c>, <c>--deep</c>, <c>--pseudonymize</c>.</summary>
public sealed record CollectionOptions(bool Minimal, bool Deep, bool Pseudonymized);

/// <summary>Herkunft eines Exports. <c>parsed:*</c>-Einträge sind nur bei <see cref="Synthetic"/> erlaubt (spec 7.3).</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<SnapshotOrigin>))]
public enum SnapshotOrigin { Collector, App, Builtin, Recorded, Synthetic }

/// <summary>
/// Kopf des Rechners. Felder, die eine Quelle nicht liefert (Bordmittel-Pakete kennen z. B. keine DomainRole), sind
/// null; Regeln, die davon abhängen, werden dann „nicht prüfbar“ (spec 2.3).
/// </summary>
/// <param name="DomainRole">Win32_ComputerSystem.DomainRole (0–5; 4/5 = Domänencontroller).</param>
/// <param name="ProductType">1 = Client, 2 = DC, 3 = Server.</param>
/// <param name="Build">CurrentBuild, z. B. 26100. Nie ProductName verwenden (spec research 3.3).</param>
/// <param name="Ubr">Update Build Revision (Patchstand).</param>
public sealed record MachineInfo(string HostName, string? DnsDomain, int? DomainRole, int? ProductType, int? Build,
    int? Ubr, string? EditionId, string? InstallationType, string? DisplayVersion, string? Architecture,
    string? MachineGuid, string? OsLanguage, DateTimeOffset? LastBootUtc);

/// <summary>
/// Erfasster Registry-Bereich (spec 7.3). Innerhalb eines vollständigen Bereichs heißt ein fehlender Schlüssel
/// KeyMissing (Beweis „nicht gesetzt“), sonst NotCollected.
/// </summary>
/// <param name="Root">Pfad ohne Präfix, z. B. <c>HKLM\SOFTWARE\Policies</c>.</param>
/// <param name="Recursive">true = mit Unterschlüsseln bis <paramref name="MaxDepth"/>; false = genau dieser Schlüssel.</param>
/// <param name="Complete">false, wenn die Grenze der Werte erreicht wurde oder der Bereich nicht lesbar war.</param>
/// <param name="DeniedSubkeys">Unterschlüssel ohne Leserecht (vollständige Pfade).</param>
/// <param name="TruncatedAt">Erster nicht mehr gelesener Schlüssel, wenn abgebrochen wurde.</param>
public sealed record CoverageEntry(string Root, bool Recursive, int MaxDepth, bool Complete,
    IReadOnlyList<string> DeniedSubkeys, string? TruncatedAt)
{
    public IReadOnlyList<string> DeniedSubkeys { get; init; } = DeniedSubkeys ?? [];
}

/// <summary>Ein erfasster Eintrag (Schlüssel nach den Mustern in spec 7.3, z. B. <c>reg:HKLM\…</c>, <c>wmi:…</c>, <c>api:…</c>).</summary>
/// <param name="Value">Wert in der Kodierung von spec 7.3 bzw. docs/file-formats.md; fehlt bei Fehlerstatus.</param>
/// <param name="ErrorCode">Win32-/HRESULT-Code bei Denied oder Error.</param>
/// <param name="Error">Kurzer Fehlertext (ohne Namen oder Werte des Rechners).</param>
/// <param name="Truncated">Wert wurde an einer Grenze abgeschnitten (gilt als Unavailable, spec 2.4).</param>
public sealed record SnapshotItem(string Key, ItemStatus Status, JsonElement? Value, int? ErrorCode, string? Error,
    int DurationMs, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Truncated = false);

/// <summary>Status eines Eintrags. Nur Ok, ValueMissing und KeyMissing (in vollständigem Bereich) sind Beweise (spec 2.4).</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ItemStatus>))]
public enum ItemStatus { Ok, ValueMissing, KeyMissing, Denied, NotApplicable, NamespaceMissing, NotCollected, Error }
