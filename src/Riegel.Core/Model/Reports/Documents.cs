// SPDX-License-Identifier: MIT
namespace Riegel.Core.Model.Reports;

// ---- Weitere Dokumente (spec 5: O6, O12, O13, O14) ----

/// <summary>Eine Seite für die Geschäftsführung über mehrere Anlagen (O6).</summary>
/// <param name="TopFindings">Die fünf häufigsten offenen hohen Befunde über alle Anlagen.</param>
public sealed record SummaryData(DateOnly Date, string Language, DateTimeOffset GeneratedAtUtc, ToolInfo Tool,
    IReadOnlyList<SummaryPlantRow> Plants, IReadOnlyList<AggregatedFinding> TopFindings, IReadOnlyList<Banner> Banners,
    string Confidentiality, bool PreTranslated)
{
    public IReadOnlyList<SummaryPlantRow> Plants { get; init; } = Plants ?? [];
    public IReadOnlyList<AggregatedFinding> TopFindings { get; init; } = TopFindings ?? [];
    public IReadOnlyList<Banner> Banners { get; init; } = Banners ?? [];
}

/// <summary>Eine Anlage auf der Übersichtsseite.</summary>
/// <param name="SupportedOsPercent">Anteil der Rechner mit unterstütztem Betriebssystem; null = nicht bestimmbar.</param>
/// <param name="Trend">Offene hohe Befunde an den letzten 4 Monatsenden (älteste zuerst).</param>
public sealed record SummaryPlantRow(string PlantId, string PlantName, int Machines, int? SupportedOsPercent, int OpenHigh,
    IReadOnlyList<TrendPoint> Trend, int Regressions, int ExceptionsExpiring, int MeasuresOverdue, int? Score,
    int? Documented, int CheckedShare, bool Incomplete)
{
    public IReadOnlyList<TrendPoint> Trend { get; init; } = Trend ?? [];
}

/// <summary>Stand an einem Monatsende; null = keine Läufe bis dahin.</summary>
public sealed record TrendPoint(DateOnly MonthEnd, int? OpenHigh);

/// <summary>Soll-Konfiguration einer Anlage (O13), je Gruppe/Rolle.</summary>
/// <param name="TailoringSha256">SHA-256 des Zuschnitts, der in die Soll-Konfiguration eingeht.</param>
public sealed record TargetConfigData(string PlantId, string PlantName, string Language, DateOnly Date,
    DateTimeOffset GeneratedAtUtc, ToolInfo Tool, CatalogueRef Catalogue, IReadOnlyList<TargetConfigGroup> Groups,
    IReadOnlyList<BaselineRef> Baselines, ProvenanceCounts Provenance, IReadOnlyList<SignOffRole> SignOff,
    string TailoringSha256, string Confidentiality, bool PreTranslated)
{
    public IReadOnlyList<TargetConfigGroup> Groups { get; init; } = Groups ?? [];
    public IReadOnlyList<BaselineRef> Baselines { get; init; } = Baselines ?? [];
    public IReadOnlyList<SignOffRole> SignOff { get; init; } = SignOff ?? [];
}

/// <summary>Eine Gruppe (null = Rechner ohne Gruppe) mit ihren Regeln im Prüfumfang.</summary>
public sealed record TargetConfigGroup(string? GroupId, string Name, MachineRole? Role, IReadOnlyList<string> Hosts,
    IReadOnlyList<TargetConfigRule> Rules, IReadOnlyList<TailoringView> Tailoring)
{
    public IReadOnlyList<string> Hosts { get; init; } = Hosts ?? [];
    public IReadOnlyList<TargetConfigRule> Rules { get; init; } = Rules ?? [];
    public IReadOnlyList<TailoringView> Tailoring { get; init; } = Tailoring ?? [];
}

/// <summary>Eine Regel der Soll-Konfiguration.</summary>
/// <param name="Expectation">Sollwert als lesbarer Text, aus der Prüfung erzeugt.</param>
/// <param name="Sources">src der verified-Einträge (ACSC, MSDOCS-CCBY, MSLEARN-FACT, DISA).</param>
/// <param name="AlsoInMsBaseline">Erscheint auch in einer Microsoft-Baseline (nur Bestätigung).</param>
/// <param name="AppliesTo">Anwendbarkeit als lesbarer Text (z. B. „nur Domänenmitglieder“), null = überall.</param>
public sealed record TargetConfigRule(string RuleSetId, string RuleId, string Title, string Category, Severity Severity,
    RuleKind Kind, string Expectation, IReadOnlyList<string> Sources, bool AlsoInMsBaseline, string? AppliesTo)
{
    public IReadOnlyList<string> Sources { get; init; } = Sources ?? [];
}

/// <summary>„Ausnahmen zur Genehmigung“ (O12): neue und geänderte Einträge mit Inhalts-Hash und Unterschriftszeilen.</summary>
/// <param name="SheetId">Kennung des Blatts, z. B. für approvalRecord („Freigabeblatt FB-12“).</param>
public sealed record ApprovalSheetData(string PlantId, string PlantName, string Language, DateOnly Date,
    DateTimeOffset GeneratedAtUtc, ToolInfo Tool, string SheetId, string ExceptionsSha256, string TailoringSha256,
    IReadOnlyList<ApprovalSheetEntry> Entries, IReadOnlyList<SignOffRole> Approvers, string Confidentiality,
    bool PreTranslated)
{
    public IReadOnlyList<ApprovalSheetEntry> Entries { get; init; } = Entries ?? [];
    public IReadOnlyList<SignOffRole> Approvers { get; init; } = Approvers ?? [];
}

/// <summary>Ein Eintrag auf dem Genehmigungsblatt (Ausnahme oder Zuschnitt).</summary>
/// <param name="Text">Begründung (Ausnahme) bzw. Grund (Zuschnitt).</param>
/// <param name="Measure">Geplante Maßnahme zur selben Regel, wenn vorhanden.</param>
public sealed record ApprovalSheetEntry(RegisterKind Kind, string Id, string RuleId, string RuleTitle, Severity? Severity,
    RegisterScope Scope, ReasonCategory? Category, string Text, string? CompensatingMeasure, string? Reference,
    string? AcceptedValue, string? Measure, string ContentSha256, RegisterEntryState State);

/// <summary>Erhebungsprotokoll (O14) zu einem Import, mit Zeugen und Integritätsaussage.</summary>
public sealed record CollectionRecordData(string PlantId, string PlantName, string Language, DateTimeOffset GeneratedAtUtc,
    ToolInfo Tool, ImportRecord Import, string Confidentiality, bool PreTranslated);
