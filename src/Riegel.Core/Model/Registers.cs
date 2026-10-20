// SPDX-License-Identifier: MIT
using System.Text.Json.Serialization;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Model;

// ---- Registers (O8, spec 2.11–2.13) ----
// Dokumente des Kunden unter seiner Änderungskontrolle: UTF-8, eingerückt, feste Reihenfolge, nach id sortiert.

/// <summary>Geltungsbereich eines Registereintrags; der genaueste gewinnt (Rechner vor Gruppe vor Anlage).</summary>
public sealed record RegisterScope(IReadOnlyList<string> Machines, IReadOnlyList<string> Groups, bool AllMachines)
{
    public IReadOnlyList<string> Machines { get; init; } = Machines ?? [];
    public IReadOnlyList<string> Groups { get; init; } = Groups ?? [];

    /// <summary>Leer: trifft keinen Rechner (die Prüfung meldet RGL-0424).</summary>
    public static RegisterScope Empty { get; } = new([], [], false);
}

/// <summary>
/// Freigabe. Gültig nur mit Rolle, Datum, Prüftermin und Freigabe-Nachweis; <paramref name="ApprovedContentSha256"/>
/// bindet sie an den Inhalt (SHA-256 des kanonischen Eintrags ohne die Freigabefelder).
/// </summary>
/// <param name="ApprovalRecord">Change-, QMS- oder Genehmigungsblatt-Nummer des Kunden, z. B. „CHG-2026-0042“.</param>
/// <param name="ApprovedByName">Optional, Kundendaten.</param>
public sealed record Approval(string? ApprovedByRole, string? ApprovedByName, DateOnly? ApprovedOn, DateOnly? ReviewBy,
    string? ApprovalRecord, string? ApprovedContentSha256)
{
    /// <summary>Noch nicht freigegeben.</summary>
    public static Approval None { get; } = new(null, null, null, null, null, null);
}

/// <summary>Ausnahme („bewusst offen“, spec 2.11).</summary>
/// <param name="Id">EX-0001 …</param>
/// <param name="Justification">Pflicht, mindestens 20 Zeichen.</param>
/// <param name="CompensatingMeasure">Pflicht bei compensatingControl.</param>
/// <param name="Reference">Pflicht bei vendorRequirement (Herstellerdokument oder Ticket).</param>
/// <param name="AcceptedValue">Pflicht bei check-Regeln: kanonischer Istwert, "*" = jeder Wert (Warnung).</param>
public sealed record ExceptionEntry(string Id, string RuleId, RegisterScope Scope, ReasonCategory Category,
    string Justification, string? CompensatingMeasure, string? Reference, string? AcceptedValue, Approval Approval,
    DateOnly CreatedOn, string? CreatedByRole, DateOnly? ModifiedOn, string? CatalogueVersion, string? Comment);

/// <summary>Begründungsart einer Ausnahme.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ReasonCategory>))]
public enum ReasonCategory { VendorRequirement, TechnicalNecessity, CompensatingControl, RiskAccepted, NotRelevantHere }

/// <summary><c>exceptions.json</c> (<c>riegel.exceptions/1</c>); Vorlagen <c>*.exceptions-template.json</c> haben dieselbe Form ohne Freigaben.</summary>
public sealed record ExceptionFile(string Schema, string PlantId, IReadOnlyList<ExceptionEntry> Exceptions) : IAfterRead<ExceptionFile>
{
    public IReadOnlyList<ExceptionEntry> Exceptions { get; init; } = Exceptions ?? [];

    ExceptionFile IAfterRead<ExceptionFile>.AfterRead() => this with
    {
        Exceptions = [.. Exceptions.Select(e => e with
        {
            Id = e.Id ?? "", RuleId = e.RuleId ?? "", Scope = e.Scope ?? RegisterScope.Empty, Justification = e.Justification ?? "",
            Approval = e.Approval ?? Approval.None,
        })],
    };
}

/// <summary>Zuschnitt („nicht im Prüfumfang“, spec 2.12).</summary>
/// <param name="Id">TL-0001 …</param>
/// <param name="Reason">Pflicht, mindestens 20 Zeichen.</param>
public sealed record TailoringEntry(string Id, string RuleId, RegisterScope Scope, string Reason, Approval Approval,
    DateOnly CreatedOn, string? CreatedByRole, DateOnly? ModifiedOn, string? Comment);

/// <summary><c>tailoring.json</c> (<c>riegel.tailoring/1</c>).</summary>
public sealed record TailoringFile(string Schema, string PlantId, IReadOnlyList<TailoringEntry> Entries) : IAfterRead<TailoringFile>
{
    public IReadOnlyList<TailoringEntry> Entries { get; init; } = Entries ?? [];

    TailoringFile IAfterRead<TailoringFile>.AfterRead() => this with
    {
        Entries = [.. Entries.Select(e => e with
        {
            Id = e.Id ?? "", RuleId = e.RuleId ?? "", Scope = e.Scope ?? RegisterScope.Empty, Reason = e.Reason ?? "", Approval = e.Approval ?? Approval.None,
        })],
    };
}

/// <summary>Maßnahme (Maßnahmenplan, spec 2.13). Braucht keine Freigabe.</summary>
/// <param name="Id">M-0001 …</param>
public sealed record MeasureEntry(string Id, string RuleId, RegisterScope Scope, string Action, string OwnerRole,
    DateOnly DueDate, string? Ticket, MeasureState State, DateOnly CreatedOn, string? CreatedByRole, DateOnly? ModifiedOn,
    string? Comment);

[JsonConverter(typeof(CamelCaseEnumConverter<MeasureState>))]
public enum MeasureState { Planned, Done, Dropped }

/// <summary><c>measures.json</c> (<c>riegel.measures/1</c>).</summary>
public sealed record MeasureFile(string Schema, string PlantId, IReadOnlyList<MeasureEntry> Measures) : IAfterRead<MeasureFile>
{
    public IReadOnlyList<MeasureEntry> Measures { get; init; } = Measures ?? [];

    MeasureFile IAfterRead<MeasureFile>.AfterRead() => this with
    {
        Measures = [.. Measures.Select(e => e with
        {
            Id = e.Id ?? "", RuleId = e.RuleId ?? "", Scope = e.Scope ?? RegisterScope.Empty, Action = e.Action ?? "", OwnerRole = e.OwnerRole ?? "",
        })],
    };
}

/// <summary>Die drei Register einer Anlage mit ihren SHA-256 beim Laden (für optimistische Nebenläufigkeit).</summary>
public sealed record RegisterSet(ExceptionFile Exceptions, TailoringFile Tailoring, MeasureFile Measures, RegisterHashes Hashes);

[JsonConverter(typeof(CamelCaseEnumConverter<RegisterKind>))]
public enum RegisterKind { Exceptions, Tailoring, Measures }

/// <summary>Befund der Registerprüfung (spec 2.11–2.13).</summary>
/// <param name="EntryId">Betroffener Eintrag; null bei Befunden über die ganze Datei.</param>
/// <param name="Code">RGL-Code (Bereich workflow).</param>
/// <param name="Field">Betroffenes Feld (camelCase), wenn eines.</param>
/// <param name="Message">Text in der gewählten Sprache.</param>
public sealed record ValidationIssue(RegisterKind Register, string? EntryId, IssueSeverity Severity, string Code,
    string? Field, string Message);

[JsonConverter(typeof(CamelCaseEnumConverter<IssueSeverity>))]
public enum IssueSeverity { Warning, Error }
