// SPDX-License-Identifier: MIT
using System.Text.Json.Serialization;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Model;

// ---- Tabellen des Katalogs (Formate der Dateien unter rules/, beschrieben in docs/file-formats.md) ----

/// <summary>
/// Supportende je Windows-Produkt (<c>rules/lifecycle/windows.json</c>, <c>riegel.lifecycle/1</c>). Jeder Eintrag nennt
/// seine Quelle (Microsoft-Lifecycle-Seite) und das Datum, an dem sie gesehen wurde.
/// </summary>
public sealed record LifecycleTable(string Schema, IReadOnlyList<LifecycleEntry> Products)
{
    public IReadOnlyList<LifecycleEntry> Products { get; init; } = Products ?? [];

    public static LifecycleTable Empty { get; } = new(SchemaIds.Lifecycle, []);
}

/// <summary>
/// Ein Produkt, abgegrenzt über Build, Editionen und Installationsart. Der erste passende Eintrag gilt.
/// </summary>
/// <param name="Id">Stabile Kennung, z. B. „win11-24h2-pro“.</param>
/// <param name="Os">„client“ oder „server“.</param>
/// <param name="Editions">EditionID-Werte; leer oder fehlend = alle.</param>
/// <param name="Channel">gac, ltsc, ltsb, server, ltsc-iot …</param>
/// <param name="EndOfSupport">Letzter Tag mit Sicherheitsupdates ohne ESU (bei Windows 10/11 GAC: Ende der Wartung der Edition).</param>
/// <param name="EsuEnd">Ende der Extended Security Updates, wo Microsoft sie nennt.</param>
/// <param name="CrossCheck">Gegenprobe, z. B. endoflife.date.</param>
public sealed record LifecycleEntry(string Id, string Name, string Os, int Build, IReadOnlyList<string>? Editions,
    IReadOnlyList<string>? InstallationTypes, string? Channel, DateOnly? MainstreamEnd, DateOnly EndOfSupport,
    DateOnly? EsuEnd, string Source, DateOnly Seen, string? CrossCheck, string? Note);

/// <summary>
/// Build → UBR → Veröffentlichungsdatum (+ KB) (<c>rules/lifecycle/builds.json</c>, <c>riegel.builds/1</c>).
/// </summary>
public sealed record BuildTable(string Schema, IReadOnlyList<BuildEntry> Builds)
{
    public IReadOnlyList<BuildEntry> Builds { get; init; } = Builds ?? [];

    public static BuildTable Empty { get; } = new(SchemaIds.Builds, []);
}

/// <summary>Ein Build mit seinen kumulativen Updates, aufsteigend nach UBR.</summary>
public sealed record BuildEntry(int Build, string Name, IReadOnlyList<BuildRelease> Releases, string Source, DateOnly Seen)
{
    public IReadOnlyList<BuildRelease> Releases { get; init; } = Releases ?? [];
}

/// <param name="Kind">B (Patchday), D (Vorschau), OOB (außerplanmäßig).</param>
public sealed record BuildRelease(int Ubr, DateOnly Date, string? Kb, string? Kind);

/// <summary>
/// Fähigkeiten je Betriebssystem (alle <c>rules/capabilities/*.json</c> zusammen). Doppelte Namen über Dateien
/// hinweg sind ein Ladefehler.
/// </summary>
public sealed record CapabilityTable(IReadOnlyList<CapabilityEntry> Capabilities)
{
    public IReadOnlyList<CapabilityEntry> Capabilities { get; init; } = Capabilities ?? [];

    public static CapabilityTable Empty { get; } = new([]);
}

/// <summary>Eine Datei <c>rules/capabilities/&lt;paket&gt;.json</c> (<c>riegel.capabilities/1</c>).</summary>
public sealed record CapabilityFile(string Schema, IReadOnlyList<CapabilityEntry> Capabilities)
{
    public IReadOnlyList<CapabilityEntry> Capabilities { get; init; } = Capabilities ?? [];
}

/// <summary>
/// Eine Fähigkeit: wo sie existiert (ODER über die Einträge), mit Microsoft-Quelle. Beispiel aus spec 2.5:
/// <c>{ "capability": "asr", "exists": [ { "os": "client", "minBuild": 16299, … } ], "source": "https://…", "seen": "2026-10-18" }</c>.
/// </summary>
public sealed record CapabilityEntry(string Capability, IReadOnlyList<CapabilityCondition> Exists, string Source,
    DateOnly Seen, string? Note)
{
    public IReadOnlyList<CapabilityCondition> Exists { get; init; } = Exists ?? [];
}

/// <summary>Alle angegebenen Bedingungen müssen gelten; fehlende Angaben gelten als „egal“.</summary>
public sealed record CapabilityCondition(string? Os, int? MinBuild, int? MaxBuild, IReadOnlyList<string>? Editions,
    IReadOnlyList<string>? InstallationTypes, string? Note);

/// <summary>Daten der Regeln (alle <c>rules/data/*.json</c> zusammen).</summary>
public sealed record CatalogueData(IReadOnlyList<RemoteAccessTool> RemoteAccessTools, OfficeDetection? OfficeDetection)
{
    public IReadOnlyList<RemoteAccessTool> RemoteAccessTools { get; init; } = RemoteAccessTools ?? [];

    public static CatalogueData Empty { get; } = new([], null);
}

/// <summary>
/// Eine Datei <c>rules/data/&lt;name&gt;.json</c> (<c>riegel.data/1</c>): je nach <paramref name="Kind"/> ist genau ein
/// Teil gefüllt.
/// </summary>
public sealed record DataFile(string Schema, DataKind Kind, IReadOnlyList<RemoteAccessTool>? RemoteAccessTools,
    OfficeDetection? OfficeDetection);

[JsonConverter(typeof(CamelCaseEnumConverter<DataKind>))]
public enum DataKind { RemoteAccessTools, OfficeDetection }

/// <summary>
/// Erkennung eines Fernwartungswerkzeugs. Vergleich immer ohne Groß/klein, als genaue Dienstnamen bzw. als
/// Teilzeichenkette des Anzeigenamens – nie als regulärer Ausdruck (spec 7.12).
/// </summary>
public sealed record RemoteAccessTool(string Product, IReadOnlyList<string> ServiceNames, IReadOnlyList<string> DisplayNameContains,
    string Source, DateOnly Seen, string? Note)
{
    public IReadOnlyList<string> ServiceNames { get; init; } = ServiceNames ?? [];
    public IReadOnlyList<string> DisplayNameContains { get; init; } = DisplayNameContains ?? [];
}

/// <summary>Office-Erkennung für den Fakt <c>office.installed</c> (spec 2.5).</summary>
/// <param name="ClickToRunKeys">Registry-Schlüssel der Click-to-Run-Konfiguration.</param>
/// <param name="InstallRootKeys">Schlüssel <c>Office\&lt;ver&gt;\Common\InstallRoot</c> (beide Ansichten).</param>
/// <param name="UninstallDisplayNamePrefixes">Anfänge von Uninstall-DisplayName.</param>
public sealed record OfficeDetection(IReadOnlyList<DetectionKey> ClickToRunKeys, IReadOnlyList<DetectionKey> InstallRootKeys,
    IReadOnlyList<DetectionPattern> UninstallDisplayNamePrefixes)
{
    public IReadOnlyList<DetectionKey> ClickToRunKeys { get; init; } = ClickToRunKeys ?? [];
    public IReadOnlyList<DetectionKey> InstallRootKeys { get; init; } = InstallRootKeys ?? [];
    public IReadOnlyList<DetectionPattern> UninstallDisplayNamePrefixes { get; init; } = UninstallDisplayNamePrefixes ?? [];
}

/// <summary>Ein Registry-Schlüssel (optional mit Wert), dessen Vorhandensein etwas belegt.</summary>
public sealed record DetectionKey(string Key, string? Value, string? Source, DateOnly? Seen, string? Note);

/// <summary>Ein Textmuster (Anfang oder Teilzeichenkette, ohne Groß/klein).</summary>
public sealed record DetectionPattern(string Pattern, string? Source, DateOnly? Seen, string? Note);
