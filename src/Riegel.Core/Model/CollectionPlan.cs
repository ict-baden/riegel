// SPDX-License-Identifier: MIT
using System.Text.Json.Serialization;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Model;

// ---- Erfassungsplan contracts/collection-plan.json (riegel.collection-plan/1, spec 7.3) ----
// Der Sammler (net35) liest dieselbe Datei mit einem eigenen kleinen Leser; diese Typen sind für Core, RuleTool und
// Tests. Jede Änderung am Plan erhöht später die Sammler-Version.

/// <summary>Was ein Sammler liest. Nichts außerhalb dieses Plans wird erfasst.</summary>
/// <param name="AllowedWmiMethods">Die einzigen WMI-Methoden, die ein Sammler aufrufen darf (spec 7.4).</param>
/// <param name="Sources">Belege für Listen im Plan (z. B. die Rechte von api:lsa-rights).</param>
public sealed record CollectionPlan(string Schema, int Version, PlanLimits Limits, RegistryPlan Registry,
    IReadOnlyList<WmiPlanItem> Wmi, IReadOnlyList<ApiPlanItem> Api, IReadOnlyList<EventPlanItem> Events,
    IReadOnlyList<RawPlanItem> Raw, IReadOnlyList<string> AllowedWmiMethods, IReadOnlyList<PlanSource> Sources)
{
    public IReadOnlyList<WmiPlanItem> Wmi { get; init; } = Wmi ?? [];
    public IReadOnlyList<ApiPlanItem> Api { get; init; } = Api ?? [];
    public IReadOnlyList<EventPlanItem> Events { get; init; } = Events ?? [];
    public IReadOnlyList<RawPlanItem> Raw { get; init; } = Raw ?? [];
    public IReadOnlyList<string> AllowedWmiMethods { get; init; } = AllowedWmiMethods ?? [];
    public IReadOnlyList<PlanSource> Sources { get; init; } = Sources ?? [];
}

/// <summary>Grenzen der Erfassung.</summary>
/// <param name="MaxValuesTotal">Höchstzahl Registry-Werte insgesamt (50.000); danach complete: false.</param>
/// <param name="RecursiveMaxDepth">Tiefe der rekursiven Bereiche (8).</param>
/// <param name="MaxBinaryBytes">Binärwerte darüber werden abgeschnitten (4096).</param>
/// <param name="ToolTimeoutSeconds">Zeitlimit für secedit/gpresult (60).</param>
/// <param name="MaxRawTextBytes">Rohtexte (gpresult-XML) höchstens so groß (4 MB).</param>
/// <param name="WindowsUpdateHistoryEntries">Neueste n Einträge des Update-Verlaufs (100).</param>
/// <param name="TempPathMaxChars">Pfad des Arbeitsordners höchstens so lang (gpresult verlangt ≤ 127).</param>
/// <param name="MaxStringChars">Längere Zeichenketten werden abgeschnitten (truncated).</param>
public sealed record PlanLimits(int MaxValuesTotal, int RecursiveMaxDepth, int MaxBinaryBytes, int ToolTimeoutSeconds,
    long MaxRawTextBytes, int WindowsUpdateHistoryEntries, int TempPathMaxChars, int MaxStringChars);

/// <summary>Registry: rekursive Bereiche und einzelne Schlüssel (immer die 64-Bit-Ansicht; 32-Bit über WOW6432Node-Pfade).</summary>
public sealed record RegistryPlan(IReadOnlyList<RegistryPlanEntry> Roots, IReadOnlyList<RegistryPlanEntry> Keys)
{
    public IReadOnlyList<RegistryPlanEntry> Roots { get; init; } = Roots ?? [];
    public IReadOnlyList<RegistryPlanEntry> Keys { get; init; } = Keys ?? [];
}

/// <summary>Ein Eintrag des Registry-Plans; je Eintrag ein CoverageEntry im Export.</summary>
/// <param name="Path"><c>HKLM\…</c></param>
/// <param name="Kind">recursive (bis MaxDepth), key (genau dieser Schlüssel), children (Unterschlüssel eine Ebene tief).</param>
/// <param name="Values">Nur diese Werte lesen (Filter, z. B. MachineGuid); null = alle. Im Export als „valueFilter“ vermerkt.</param>
public sealed record RegistryPlanEntry(string Path, RegistryPlanKind Kind, int MaxDepth, IReadOnlyList<string>? Values,
    bool NeedsAdmin, PlanMode Mode, string? Note);

[JsonConverter(typeof(CamelCaseEnumConverter<RegistryPlanKind>))]
public enum RegistryPlanKind { Recursive, Key, Children }

/// <summary>Wann ein Eintrag erfasst wird: immer, nicht mit --minimal, nur mit --deep (spec 7.4).</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<PlanMode>))]
public enum PlanMode { Always, SkipInMinimal, DeepOnly }

/// <summary>WMI-Klasse oder statische Methode.</summary>
/// <param name="Key">Schlüssel im Export, z. B. <c>wmi:root\cimv2:Win32_Service</c>.</param>
/// <param name="Where">WQL-Bedingung (fest im Plan), z. B. <c>LocalAccount=TRUE</c>.</param>
/// <param name="Properties">Gelesene Eigenschaften (bei statischen Methoden: die Felder von <c>out</c>).</param>
/// <param name="AdminOnlyProperties">Eigenschaften, die nur mit Admin-Rechten Werte liefern (z. B. Defender-Ausnahmen).</param>
public sealed record WmiPlanItem(string Key, string Namespace, string Class, string? Where, IReadOnlyList<string> Properties,
    IReadOnlyList<string>? AdminOnlyProperties, IReadOnlyList<WmiPlanMethod> Methods, bool NeedsAdmin, PlanMode Mode, string? Note)
{
    public IReadOnlyList<string> Properties { get; init; } = Properties ?? [];
    public IReadOnlyList<WmiPlanMethod> Methods { get; init; } = Methods ?? [];
}

/// <summary>Erlaubte Methode eines WMI-Eintrags.</summary>
/// <param name="Kind">static (Ergebnis unter <c>out</c>) oder perRow (je Zeile unter dem Methodennamen).</param>
/// <param name="ViaClass">Klasse, auf deren Instanz die Methode läuft, wenn nicht die des Eintrags
/// (Win32_LogicalShareSecuritySetting für Freigaben).</param>
/// <param name="KeyProperty">Eigenschaft der Zeile, die die Instanz von <paramref name="ViaClass"/> bestimmt (Name).</param>
/// <param name="OutProperties">Gelesene Ausgabefelder.</param>
public sealed record WmiPlanMethod(string Name, WmiMethodKind Kind, string? ViaClass, string? KeyProperty,
    IReadOnlyList<string>? OutProperties);

[JsonConverter(typeof(CamelCaseEnumConverter<WmiMethodKind>))]
public enum WmiMethodKind { Static, PerRow }

/// <summary>Eintrag über eine dokumentierte Windows-API.</summary>
/// <param name="Api">Kurzname der Abfrage (netusermodals, netuserenum, lsa-rights, localgroup, audit-policy, …).</param>
/// <param name="Rights">Bei lsa-rights: die abgefragten Rechte.</param>
/// <param name="Sid">Bei localgroup: die Gruppe.</param>
/// <param name="Condition">Zusätzliche Bedingung, z. B. „wuauserv läuft bereits“.</param>
public sealed record ApiPlanItem(string Key, string Api, bool NeedsAdmin, PlanMode Mode, IReadOnlyList<string>? Rights,
    string? Sid, string? Condition, string? Note);

/// <summary>Ein Ereignis aus einem Protokoll, mit fester XPath-Abfrage.</summary>
public sealed record EventPlanItem(string Key, string Log, string Provider, int EventId, string Xpath, bool AfterLastBoot,
    bool NeedsAdmin, PlanMode Mode, string? Note);

/// <summary>Rohdaten: Ausgabe eines Windows-Werkzeugs (feste Argumente) oder eine Datei.</summary>
/// <param name="Arguments">Feste Argumente; <c>{out}</c> und <c>{log}</c> stehen für Dateien im Arbeitsordner.</param>
/// <param name="File">Bei kind file: Pfad mit <c>%SystemRoot%</c>.</param>
public sealed record RawPlanItem(string Key, RawKind Kind, string? Tool, IReadOnlyList<string>? Arguments, string? File,
    long MaxBytes, bool NeedsAdmin, PlanMode Mode, string? Note);

[JsonConverter(typeof(CamelCaseEnumConverter<RawKind>))]
public enum RawKind { Tool, File }

/// <summary>Beleg einer Liste im Plan.</summary>
public sealed record PlanSource(string Topic, string Url, DateOnly Seen);
