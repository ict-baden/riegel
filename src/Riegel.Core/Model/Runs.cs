// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.Json.Serialization;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Model;

// ---- Runs, approvals, imports, comparison (O2, spec 2.8, 2.14, 7.6) ----

/// <summary>SHA-256 der drei Register (klein, hex); "" für ein Register, das es nicht gibt.</summary>
public sealed record RegisterHashes(string Exceptions, string Tailoring, string Measures)
{
    public static RegisterHashes Empty { get; } = new("", "", "");
}

/// <summary>
/// Ein Lauf (<c>runs/&lt;host&gt;/&lt;runId&gt;.results.json</c>, <c>riegel.run/1</c>): ein Export, ausgewertet mit
/// Katalog, Registern und Baselines. Byte-gleich bei gleichen Eingaben und gleicher Auswertungsuhr (spec 7.6).
/// </summary>
/// <param name="Id">&lt;yyyyMMddTHHmmssZ&gt;-&lt;6 hex&gt; (spec 7.6).</param>
/// <param name="EvaluationDate">Kalendertag der Auswertung (Supportende, Gültigkeit, Fälligkeiten).</param>
/// <param name="CollectedAtUtc">Uhr des Zielrechners (Altersangaben).</param>
/// <param name="ImportedAtUtc">Zeit des Imports in den Anlagenordner.</param>
/// <param name="Snapshot">Pfad (relativ zum Anlagenordner) und SHA-256 des Exports.</param>
/// <param name="Facts">Abgeleitete Fakten (spec 2.5), Name → Wert oder null.</param>
/// <param name="PreviousRunId">Neuester Lauf eines anderen Exports desselben Rechners (nach CollectedAtUtc).</param>
/// <param name="ReEvaluationOf">Gleicher Export erneut ausgewertet: ID des ersten Laufs.</param>
public sealed record Run(string Schema, string Id, string HostName, DateTimeOffset EvaluatedAtUtc, DateOnly EvaluationDate,
    DateTimeOffset CollectedAtUtc, DateTimeOffset? ImportedAtUtc, FileRef Snapshot, ToolInfo Tool, CatalogueRef Catalogue,
    IReadOnlyList<BaselineRef> Baselines, RegisterHashes Registers, bool IsAdmin, CollectionOptions Options,
    IReadOnlyList<RuleResult> Results, RunSummary Summary, IReadOnlyList<CategoryScore> Categories,
    IReadOnlyDictionary<string, JsonElement> Facts, string? PreviousRunId, string? ApprovedRunId,
    string? ReEvaluationOf, RunFlags Flags)
{
    public IReadOnlyList<BaselineRef> Baselines { get; init; } = Baselines ?? [];
    public IReadOnlyList<RuleResult> Results { get; init; } = Results ?? [];
    public IReadOnlyList<CategoryScore> Categories { get; init; } = Categories ?? [];
    public IReadOnlyDictionary<string, JsonElement> Facts { get; init; } = Facts ?? new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
}

/// <summary>Kennzeichen eines Laufs. JSON: Liste von camelCase-Namen.</summary>
[Flags]
[JsonConverter(typeof(RunFlagsConverter))]
public enum RunFlags { None = 0, StaleSnapshot = 1, ClockSuspect = 2, MachineIdentityChanged = 4, Incomplete = 8, DomainController = 16 }

/// <summary>Zählungen und Werte eines Laufs (spec 2.6). Score/Documented null bei Domänencontrollern.</summary>
/// <param name="CheckedShare">Prüfumfang in Prozent.</param>
public sealed record RunSummary(int Pass, int Fail, int Exception, int OutOfScope, int NotApplicable, int NotCheckable,
    int Info, int OpenHigh, int OpenMedium, int OpenLow, int Regressions, int NewlyOpen, int ExceptionsExpiring,
    int ExceptionsExpired, int ExceptionsPending, int MeasuresOverdue, int? Score, int? Documented, int CheckedShare);

/// <summary>Wert je Berichtskategorie (ungedeckelt innerhalb der Kategorie).</summary>
/// <param name="Weight">Gewicht der Kategorie im Gesamtwert (bei audit gedeckelt).</param>
/// <param name="Capped">true, wenn die Deckelung (12 Punkte) gegriffen hat.</param>
public sealed record CategoryScore(string Category, int Pass, int Fail, int Exception, int? Score, double Weight, bool Capped);

/// <summary>Freigegebener Stand eines Rechners (<c>approved/&lt;host&gt;.json</c>, <c>riegel.approved/1</c>).</summary>
public sealed record ApprovedState(string Schema, string HostName, string RunId, string SnapshotSha256,
    string ResultsSha256, string CatalogueVersion, RegisterHashes Registers, Approval Approval);

/// <summary>Import- und Erhebungsnachweis (<c>imports/&lt;ts&gt;-&lt;6hex&gt;.import.json</c>, <c>riegel.import/1</c>).</summary>
public sealed record ImportRecord(string Schema, string Id, DateTimeOffset ImportedAtUtc, string? CollectedByRole,
    string? WitnessRole, string? OperatorRole, IReadOnlyList<ImportedFile> Files)
{
    public IReadOnlyList<ImportedFile> Files { get; init; } = Files ?? [];
}

/// <summary>Ein importierter Export.</summary>
/// <param name="SidecarOk">Die .sha256-Datei passte.</param>
/// <param name="RunId">Lauf, der beim Import entstand.</param>
public sealed record ImportedFile(string FileName, string Sha256, bool SidecarOk, string HostName,
    DateTimeOffset CollectedAtUtc, string CollectorName, string CollectorVersion, string? CollectorExeSha256,
    bool IsAdmin, CollectionOptions Options, SnapshotOrigin Origin, string? RunId);

/// <summary>Vergleich zweier Läufe oder eines Laufs mit dem freigegebenen Stand bzw. einer Stichprobe (spec 2.14).</summary>
public sealed record RunComparison(string HostName, string? FromRunId, string ToRunId, ComparisonBasis Basis,
    IReadOnlyList<StatusChange> StatusChanges, IReadOnlyList<ValueDrift> Drift, bool MachineIdentityChanged)
{
    public IReadOnlyList<StatusChange> StatusChanges { get; init; } = StatusChanges ?? [];
    public IReadOnlyList<ValueDrift> Drift { get; init; } = Drift ?? [];
}

[JsonConverter(typeof(CamelCaseEnumConverter<ComparisonBasis>))]
public enum ComparisonBasis { Previous, Approved, Sample }

/// <summary>Statusänderung einer Regel zwischen zwei Ständen.</summary>
/// <param name="From">Status vorher; null bei einer neuen Regel.</param>
/// <param name="Detail">Z. B. ID der abgelaufenen Ausnahme.</param>
public sealed record StatusChange(string RuleSetId, string RuleId, StatusChangeKind Kind, ResultStatus? From, ResultStatus? To,
    ResultFlags FromFlags, ResultFlags ToFlags, string? Detail);

/// <summary>Art der Änderung (spec 2.14). <see cref="ScopeChanged"/> = „Prüfumfang geändert“ (Admin, --minimal).</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<StatusChangeKind>))]
public enum StatusChangeKind
{
    Worse, Better, ExceptionAdded, ExceptionRemoved, ExceptionExpired, TailoringAdded, TailoringRemoved, NewRule,
    ScopeChanged, Other,
}

/// <summary>Abweichung eines Rohwerts (normalisierter Vergleich der Einträge ohne die flüchtige Liste, spec 2.14).</summary>
/// <param name="Key">Snapshot-Eintrag.</param>
/// <param name="Path">Teil darin, z. B. Wertname oder Dienstname/Eigenschaft.</param>
/// <param name="From">Vorher, kanonisch; null = fehlte.</param>
/// <param name="To">Nachher, kanonisch; null = fehlt.</param>
public sealed record ValueDrift(string Key, string? Path, string? From, string? To, DriftKind Kind);

[JsonConverter(typeof(CamelCaseEnumConverter<DriftKind>))]
public enum DriftKind { Added, Removed, Changed }

/// <summary>Stichprobe des Prüfers: frischer Export gegen den gepackten desselben Rechners (spec 2.14).</summary>
public sealed record SampleComparison(string HostName, bool Consistent, IReadOnlyList<ValueDrift> Differences,
    string PackedSnapshotSha256, string FreshSnapshotSha256, DateTimeOffset PackedCollectedAtUtc,
    DateTimeOffset FreshCollectedAtUtc)
{
    public IReadOnlyList<ValueDrift> Differences { get; init; } = Differences ?? [];
}

/// <summary>Kurzinfo eines gespeicherten Laufs (Liste im Anlagenspeicher).</summary>
public sealed record RunInfo(string Id, string HostName, DateTimeOffset EvaluatedAtUtc, DateOnly EvaluationDate,
    DateTimeOffset CollectedAtUtc, string SnapshotSha256, string CatalogueVersion, string? ReEvaluationOf, RunFlags Flags,
    int? Score, int OpenHigh);

/// <summary>Datei mit Prüfsumme; <paramref name="Path"/> relativ zum Anlagenordner bzw. Paket, mit „/“.</summary>
public sealed record FileRef(string Path, string Sha256);

/// <summary>Werkzeug, das ausgewertet hat: Name, Version, Commit, SHA-256 der laufenden Exe.</summary>
public sealed record ToolInfo(string Name, string Version, string? Commit, string? ExeSha256);

/// <summary>Verwendeter Katalog.</summary>
public sealed record CatalogueRef(string Id, string Version, DateOnly ReleasedOn, string Sha256);

/// <summary>Verwendete Kunden-Baseline.</summary>
public sealed record BaselineRef(string Id, string Name, BaselineSourceKind Kind, string SourceSha256,
    string? SourceFileName, DateTimeOffset ImportedAtUtc, int RuleCount);

/// <summary>Woraus eine Baseline importiert wurde (spec 2.15).</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<BaselineSourceKind>))]
public enum BaselineSourceKind { GpoBackup, RiegelJson, Csv }
