// SPDX-License-Identifier: MIT
using System.Text.Json.Serialization;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Model.Reports;

// ---- Report data (spec 5, 7.2): erzeugt vom Paket workflow, gezeichnet von pdf und data-outputs ----
// Alle Texte, die aus Daten kommen (Regeltitel, Hinweise), sind bereits in der Berichtssprache aufgelöst. Feste
// Texte (Überschriften, Banner, Statusnamen) setzen die Schreiber aus ihren eigenen Sprachdateien.

/// <summary>Auftrag für einen Anlagenbericht (CLI <c>report</c>, Berichte-Seite).</summary>
/// <param name="Hosts">null = alle Rechner der Anlage.</param>
/// <param name="Formats">Standard: PDF und Excel (spec 5).</param>
/// <param name="Evidence">Zusätzlich das Nachweispaket (O9).</param>
public sealed record ReportRequest(Plant Plant, IReadOnlyList<string>? Hosts, DateOnly EvaluationDate, string Language,
    ReportPreset Preset, bool PerMachine, bool IncludeAppendix, IReadOnlyList<ReportFormat> Formats, bool Evidence,
    bool IncludeBaselineContent, bool PdfA, string OutDir)
{
    public IReadOnlyList<ReportFormat> Formats { get; init; } = Formats ?? [];
}

[JsonConverter(typeof(CamelCaseEnumConverter<ReportFormat>))]
public enum ReportFormat { Pdf, Xlsx, Csv, Json }

/// <summary>Alles, was der Anlagenbericht (O4/O5), die Excel-Mappe (O7) und die CSV (O3) zeigen.</summary>
/// <param name="Changes">„Verschlechtert seit dem letzten Lauf / seit der Freigabe“ über die Anlage, zuerst gezeigt.</param>
/// <param name="ExpiredExceptions">Abgelaufene Ausnahmen (eigener Block, keine Verschlechterung).</param>
/// <param name="Findings">Offene Befunde je Regel über alle Rechner.</param>
public sealed record ReportData(ReportHeader Header, IReadOnlyList<Banner> Banners, ReportCounts Counts, ScoreView Score,
    IReadOnlyList<CategoryScore> Categories, IReadOnlyList<AggregatedFinding> Findings, IReadOnlyList<HostChange> Changes,
    IReadOnlyList<ExceptionView> ExpiredExceptions, IReadOnlyList<ExceptionView> Exceptions,
    IReadOnlyList<TailoringView> Tailoring, IReadOnlyList<MeasureView> Measures, IReadOnlyList<MachineReport> Machines,
    IReadOnlyList<InsurerFactsRow> InsurerFacts, IReadOnlyList<BaselineSection> Baselines, SignOffData SignOff,
    MethodInfo Method)
{
    public IReadOnlyList<Banner> Banners { get; init; } = Banners ?? [];
    public IReadOnlyList<CategoryScore> Categories { get; init; } = Categories ?? [];
    public IReadOnlyList<AggregatedFinding> Findings { get; init; } = Findings ?? [];
    public IReadOnlyList<HostChange> Changes { get; init; } = Changes ?? [];
    public IReadOnlyList<ExceptionView> ExpiredExceptions { get; init; } = ExpiredExceptions ?? [];
    public IReadOnlyList<ExceptionView> Exceptions { get; init; } = Exceptions ?? [];
    public IReadOnlyList<TailoringView> Tailoring { get; init; } = Tailoring ?? [];
    public IReadOnlyList<MeasureView> Measures { get; init; } = Measures ?? [];
    public IReadOnlyList<MachineReport> Machines { get; init; } = Machines ?? [];
    public IReadOnlyList<InsurerFactsRow> InsurerFacts { get; init; } = InsurerFacts ?? [];
    public IReadOnlyList<BaselineSection> Baselines { get; init; } = Baselines ?? [];
}

/// <summary>Kopf: Anlage, Bericht, Werkzeug, Katalog, Register, Deckblattangaben.</summary>
/// <param name="ReportId">Kennung des Berichts (in der Fußzeile jeder Seite).</param>
/// <param name="PerformedBy">„Durchgeführt von“ (Partner), null = keine Angabe.</param>
/// <param name="LogoPath">Geprüftes Logo im Anlagenordner, null = keines.</param>
/// <param name="Confidentiality">Vertraulichkeitsvermerk (Standard aus den Texten).</param>
/// <param name="PreTranslated">Sprache noch nicht geprüft (es/fr/zh-Hans): Vermerk auf Deckblatt und Fußzeilen.</param>
public sealed record ReportHeader(string PlantId, string PlantName, string? PlantDescription, string ReportId,
    ReportPreset Preset, string Language, DateOnly EvaluationDate, DateTimeOffset GeneratedAtUtc, ToolInfo Tool,
    CatalogueRef Catalogue, IReadOnlyList<BaselineRef> Baselines, RegisterHashes Registers, string? PerformedBy,
    string? LogoPath, string Confidentiality, IReadOnlyList<string> Hosts, bool PreTranslated)
{
    public IReadOnlyList<BaselineRef> Baselines { get; init; } = Baselines ?? [];
    public IReadOnlyList<string> Hosts { get; init; } = Hosts ?? [];
}

/// <summary>Hinweisbanner auf Deckblatt und Zusammenfassung (spec 2.4 Punkt 9, 2.8, 5).</summary>
/// <param name="CheckedShare">Prüfumfang in Prozent (IncompleteCheck).</param>
/// <param name="Hosts">Betroffene Rechner.</param>
/// <param name="AgeDays">Alter in Tagen (StaleSnapshot, StaleCatalogue).</param>
/// <param name="Areas">Fehlende Bereiche (Kategorie-Codes) bei unvollständiger Prüfung.</param>
public sealed record Banner(BannerKind Kind, int? CheckedShare, IReadOnlyList<string> Hosts, int? AgeDays,
    IReadOnlyList<string> Areas)
{
    public IReadOnlyList<string> Hosts { get; init; } = Hosts ?? [];
    public IReadOnlyList<string> Areas { get; init; } = Areas ?? [];
}

[JsonConverter(typeof(CamelCaseEnumConverter<BannerKind>))]
public enum BannerKind
{
    /// <summary>„Unvollständige Prüfung“: unter 90 % prüfbar oder nicht als Administrator erfasst.</summary>
    IncompleteCheck,
    /// <summary>„Stand der Erfassung: … (vor N Tagen)“.</summary>
    StaleSnapshot,
    /// <summary>„Regelwerk älter als 6 Monate …“.</summary>
    StaleCatalogue,
    /// <summary>„Maschinelle Vorübersetzung – nicht geprüft“.</summary>
    PreTranslation,
    /// <summary>„Quelle: Domänen-GPO (nicht in echter Domäne getestet)“.</summary>
    ExperimentalGpoAttribution,
    /// <summary>Uhr eines Rechners unplausibel.</summary>
    ClockSuspect,
    /// <summary>Domänencontroller: Katalog 2026.10 nicht für DCs ausgelegt, kein Härtungsgrad.</summary>
    DomainController,
}

/// <summary>Zählungen zuerst (spec 2.6): offene Befunde je Schwere, Verschlechterungen, Register.</summary>
public sealed record ReportCounts(int Machines, int MachinesScored, int OpenHigh, int OpenMedium, int OpenLow,
    int Regressions, int NewlyOpen, int NotCheckable, int ExceptionsValid, int ExceptionsExpiring, int ExceptionsExpired,
    int ExceptionsPending, int TailoringEntries, int MeasuresPlanned, int MeasuresOverdue, int UnsupportedOs,
    int StaleSnapshots, int DomainControllers);

/// <summary>Werte der Anlage; <paramref name="Greyed"/> = grau mit „nur X % prüfbar“.</summary>
public sealed record ScoreView(int? Score, int? Documented, int CheckedShare, bool Greyed, bool AuditCapped);

/// <summary>Offener Befund je Regel über alle Rechner („NET-01 offen auf 37 von 50 Rechnern“).</summary>
/// <param name="Hosts">Höchstens 10 Rechner; <paramref name="MoreHosts"/> = es gibt weitere („…“).</param>
public sealed record AggregatedFinding(string RuleSetId, string RuleId, string Title, string Category, Severity Severity,
    int OpenCount, int ApplicableCount, IReadOnlyList<string> Hosts, bool MoreHosts, MeasureSummary? Measure,
    ResultFlags Flags)
{
    public IReadOnlyList<string> Hosts { get; init; } = Hosts ?? [];
}

/// <summary>Stand einer geplanten Maßnahme zu einem Befund.</summary>
public sealed record MeasureSummary(string MeasureId, string OwnerRole, DateOnly DueDate, bool Overdue);

/// <summary>Eine Statusänderung eines Rechners gegenüber dem vorigen Lauf oder dem freigegebenen Stand.</summary>
public sealed record HostChange(string HostName, ComparisonBasis Basis, string? FromRunId, string ToRunId, string Title,
    Severity Severity, StatusChange Change);

/// <summary>Zustand eines Registereintrags in Berichten.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<RegisterEntryState>))]
public enum RegisterEntryState { Valid, Pending, ChangedAfterApproval, Expiring, Expired, Invalid }

/// <summary>Kennzeichen an Registereinträgen (spec 2.7). JSON: Liste von camelCase-Namen.</summary>
[Flags]
[JsonConverter(typeof(RegisterFlagsConverter))]
public enum RegisterFlags
{
    None = 0,
    /// <summary>Die Regel besteht jetzt: „Ausnahme nicht mehr nötig?“</summary>
    Unused = 1,
    Overlap = 2,
    RuleRetired = 4,
    MeasureDoneButFailing = 8,
    /// <summary>Istwert weicht vom akzeptierten Wert ab.</summary>
    ValueMismatch = 16,
}

/// <summary>Ausnahme im Bericht.</summary>
/// <param name="AffectedHosts">Rechner, auf die sie wirkt.</param>
/// <param name="ContentSha256">SHA-256 des kanonischen Eintrags ohne Freigabefelder.</param>
public sealed record ExceptionView(ExceptionEntry Entry, RegisterEntryState State, RegisterFlags Flags, string RuleTitle,
    Severity? Severity, IReadOnlyList<string> AffectedHosts, string ContentSha256)
{
    public IReadOnlyList<string> AffectedHosts { get; init; } = AffectedHosts ?? [];
}

/// <summary>Zuschnitt im Bericht.</summary>
public sealed record TailoringView(TailoringEntry Entry, RegisterEntryState State, RegisterFlags Flags, string RuleTitle,
    Severity? Severity, IReadOnlyList<string> AffectedHosts, string ContentSha256)
{
    public IReadOnlyList<string> AffectedHosts { get; init; } = AffectedHosts ?? [];
}

/// <summary>Maßnahme im Bericht; <paramref name="OpenCount"/> = auf wie vielen Rechnern der Befund noch offen ist.</summary>
public sealed record MeasureView(MeasureEntry Entry, bool Overdue, RegisterFlags Flags, string RuleTitle, Severity? Severity,
    IReadOnlyList<string> AffectedHosts, int OpenCount)
{
    public IReadOnlyList<string> AffectedHosts { get; init; } = AffectedHosts ?? [];
}

/// <summary>Ein Rechner im Bericht (Übersicht und Anhang A).</summary>
/// <param name="Worse">„Verschlechtert“ dieses Rechners, zuerst im Anhang.</param>
/// <param name="Lifecycle">Supportstand des Betriebssystems.</param>
public sealed record MachineReport(string HostName, string? Label, string? GroupId, string? GroupName, MachineRole? Role,
    MachineIdentity? Identity, MachineInfo Machine, CollectionInfo Collection, string RunId, RunSummary Summary,
    IReadOnlyList<CategoryScore> Categories, IReadOnlyList<RuleResultView> Results, IReadOnlyList<HostChange> Worse,
    DriftSummary Drift, IReadOnlyList<FactView> Facts, IReadOnlyList<FactView> NetworkFacts,
    IReadOnlyList<InventoryView> Inventory, LifecycleView? Lifecycle, RunFlags Flags)
{
    public IReadOnlyList<CategoryScore> Categories { get; init; } = Categories ?? [];
    public IReadOnlyList<RuleResultView> Results { get; init; } = Results ?? [];
    public IReadOnlyList<HostChange> Worse { get; init; } = Worse ?? [];
    public IReadOnlyList<FactView> Facts { get; init; } = Facts ?? [];
    public IReadOnlyList<FactView> NetworkFacts { get; init; } = NetworkFacts ?? [];
    public IReadOnlyList<InventoryView> Inventory { get; init; } = Inventory ?? [];

    /// <summary>Domänencontroller: „nicht bewertet“.</summary>
    [JsonIgnore]
    public bool IsDomainController => (Flags & RunFlags.DomainController) != 0;
}

/// <summary>Erhebungsangaben eines Rechners.</summary>
/// <param name="AgeDays">Tage zwischen Erfassung und Auswertungstag.</param>
public sealed record CollectionInfo(DateTimeOffset CollectedAtUtc, int AgeDays, DateTimeOffset? ImportedAtUtc, bool IsAdmin,
    CollectionOptions Options, SnapshotOrigin Origin, string CollectorName, string CollectorVersion,
    string? CollectorExeSha256, string SnapshotSha256, string? ImportId);

/// <summary>Ein Ergebnis mit den Texten der Regel in der Berichtssprache.</summary>
public sealed record RuleResultView(RuleResult Result, string Title, string Category, RuleKind Kind, string? Rationale,
    string? OtNote, string? Remedy, IReadOnlyList<string> References)
{
    public IReadOnlyList<string> References { get; init; } = References ?? [];
}

/// <summary>Abweichungen der Rohwerte: Gesamtzahl und die ersten 20.</summary>
public sealed record DriftSummary(int Total, IReadOnlyList<ValueDrift> Top)
{
    public IReadOnlyList<ValueDrift> Top { get; init; } = Top ?? [];

    public static DriftSummary None { get; } = new(0, []);
}

/// <summary>Ein Fakt für den Bericht (Name = Fakt-Code aus spec 2.5, Wert kanonisch, null = unbekannt).</summary>
public sealed record FactView(string Name, string? Value);

/// <summary>Bestand einer inventory-Regel als Zeilen.</summary>
public sealed record InventoryView(string RuleId, string Title, InventoryRenderer Renderer, IReadOnlyList<string> Items)
{
    public IReadOnlyList<string> Items { get; init; } = Items ?? [];
}

/// <summary>Supportstand des Betriebssystems.</summary>
/// <param name="Supported">null = nicht bestimmbar.</param>
public sealed record LifecycleView(string? ProductName, DateOnly? EndOfSupport, DateOnly? EsuEnd, bool EsuCovered, bool? Supported);

/// <summary>Eine Zeile „Fakten für Versicherer-Fragebögen“ (Excel O7).</summary>
public sealed record InsurerFactsRow(string HostName, string? OsName, int? Build, DateOnly? SupportEnd, bool? Supported,
    string? AvProduct, string? AvState, int? SignatureAgeDays, int? DaysSinceUpdate, string? PatchMonth,
    int? LocalAdminCount, bool? RdpEnabled, bool? RdpNla, bool? Smb1, bool? BitLocker, bool? FirewallOn,
    NetworkFacts Network);

/// <summary>„Netzanbindung“ eines Rechners (spec 2.5 net.*).</summary>
public sealed record NetworkFacts(bool? GatewayConfigured, int? ActiveAdapters, bool? Wireless, bool? IpForwarding, bool? ProxyConfigured);

/// <summary>Wertung je importierter Baseline (eigener Abschnitt, spec 2.15).</summary>
/// <param name="ContentIncluded">false = im Paket nur Metadaten, „nicht nachprüfbar“.</param>
public sealed record BaselineSection(BaselineRef Baseline, int Pass, int Fail, int Exception, int NotCheckable, int? Score,
    bool ContentIncluded);

/// <summary>
/// Freigabeblock (spec 2.11): „Mit der Freigabe werden die Ausnahmen im Register &lt;sha8&gt; (n Einträge, davon m
/// gültig) und der Zuschnitt &lt;sha8&gt; bestätigt.“
/// </summary>
public sealed record SignOffData(IReadOnlyList<SignOffRole> Roles, string ExceptionsSha256, int ExceptionCount,
    int ExceptionValidCount, string TailoringSha256, int TailoringCount, string MeasuresSha256, int MeasureCount,
    IReadOnlyList<string> RunIds, string ReportId)
{
    public IReadOnlyList<SignOffRole> Roles { get; init; } = Roles ?? [];
    public IReadOnlyList<string> RunIds { get; init; } = RunIds ?? [];
}

/// <summary>Methode und Quellen (Abschnitt 9 des Berichts).</summary>
/// <param name="Sources">Regelquellen mit Lizenz und Namensnennung.</param>
/// <param name="Frameworks">Einordnung: Kennungen, für die Riegel Nachweise unterstützt (spec 2.16), ohne Konformitätsaussage.</param>
/// <param name="ExperimentalGpoAttribution">Mindestens ein Wert wurde einer Domänen-GPO zugeordnet.</param>
public sealed record MethodInfo(IReadOnlyList<SourceAttribution> Sources, int RulesTotal, int CheckRules, int InventoryRules,
    ProvenanceCounts Provenance, IReadOnlyList<string> Frameworks, bool ExperimentalGpoAttribution)
{
    public IReadOnlyList<SourceAttribution> Sources { get; init; } = Sources ?? [];
    public IReadOnlyList<string> Frameworks { get; init; } = Frameworks ?? [];
}

/// <summary>Eine Quelle mit Lizenz und Namensnennung (aus THIRD-PARTY-NOTICES.txt).</summary>
public sealed record SourceAttribution(string Src, string Name, string Licence, string Attribution, string? Url);

/// <summary>„Herkunft der Sollwerte“: wie viele Regeln durch welche Quelle belegt sind (spec 5 O13).</summary>
public sealed record ProvenanceCounts(int Total, int Acsc, int MsDocs, int MsLearnFacts, int Disa, int AlsoInMsBaseline);
