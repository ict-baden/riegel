// SPDX-License-Identifier: MIT
using System.Text.Json.Serialization;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Model;

// ---- Results (spec 2.7, 7.2) ----

/// <summary>Ergebnis einer Regel auf einem Rechner.</summary>
/// <param name="Expected">Sollwert in kanonischer Form (<see cref="CanonicalValue"/>).</param>
/// <param name="Actual">Istwert in kanonischer Form; null = nicht gesetzt.</param>
/// <param name="Source">Woher der Wert stammt (spec 2.10).</param>
/// <param name="SourceDetail">Z. B. der Name der GPO.</param>
/// <param name="EvidenceKeys">Gelesene Snapshot-Einträge.</param>
/// <param name="ReasonCode">RGL-Code, warum nicht prüfbar/anwendbar (z. B. RGL-02xx je Eintragsstatus).</param>
/// <param name="Details">Zusätzliche Zeilen, z. B. Treffer einer Bestandsregel oder „Kennwort in Registry“.</param>
public sealed record RuleResult(string RuleSetId, string RuleId, ResultStatus Status, Severity Severity,
    string? Expected, string? Actual, ValueSource Source, string? SourceDetail, IReadOnlyList<string> EvidenceKeys,
    string? ExceptionId, string? TailoringId, string? MeasureId, ResultFlags Flags, string? ReasonCode,
    IReadOnlyList<string>? Details)
{
    public IReadOnlyList<string> EvidenceKeys { get; init; } = EvidenceKeys ?? [];
}

/// <summary>Status (spec 2.7). Offen zählt nur <see cref="Fail"/>.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ResultStatus>))]
public enum ResultStatus { Pass, Fail, Exception, OutOfScope, NotApplicable, NotCheckable, Info }

/// <summary>Zusätzliche Kennzeichen eines Ergebnisses (spec 2.7). JSON: Liste von camelCase-Namen.</summary>
[Flags]
[JsonConverter(typeof(ResultFlagsConverter))]
public enum ResultFlags : long
{
    None = 0, Regression = 1, NewRule = 2, ExceptionExpired = 4, ExceptionExpiring = 8, ExceptionPending = 16,
    ExceptionValueMismatch = 32, ExceptionChangedAfterApproval = 64, TailoringPending = 128, MeasurePlanned = 256,
    MeasureOverdue = 512, FromDefault = 1024, ConfiguredOnly = 2048, Heuristic = 4096, Derived = 8192,
    StaleSnapshot = 16384, ClockSuspect = 32768,
}

/// <summary>Herkunft eines Werts (spec 2.10). DomainGpo ist in v1 experimentell.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ValueSource>))]
public enum ValueSource { Unknown, DomainGpo, LocalPolicy, Mdm, DirectlySet, OsDefault, Runtime, NotSet }
