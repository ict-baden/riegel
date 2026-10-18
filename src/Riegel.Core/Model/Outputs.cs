// SPDX-License-Identifier: MIT
using System.Text.Json.Serialization;
using Riegel.Core.Model.Json;
using Riegel.Core.Model.Reports;

namespace Riegel.Core.Model;

// ---- Optionen und Ergebnisse der Ausgaben (spec 5, 7.2) ----

/// <summary>Optionen der PDF-Ausgabe.</summary>
/// <param name="OutDir">Zielordner (wird angelegt).</param>
/// <param name="PdfA">PDF/A nur, wenn das pdf-Paket es nachweislich kann (spec 5, V6).</param>
/// <param name="PerMachine">Zusätzlich je Rechner ein PDF (O5).</param>
/// <param name="IncludeAppendix">Anhang A je Rechner im Anlagenbericht.</param>
public sealed record PdfOptions(string OutDir, bool PdfA = false, bool PerMachine = false, bool IncludeAppendix = true);

/// <summary>Auftrag für das Nachweispaket (O9).</summary>
/// <param name="Documents">Bereits erzeugte Berichtsdateien (PDF, Excel, CSV), die ins Paket kommen.</param>
/// <param name="Hosts">null = alle Rechner der Anlage.</param>
/// <param name="IncludeBaselineContent">Inhalt importierter Baselines mitpacken (Lizenzwarnung, spec 2.15).</param>
public sealed record EvidenceRequest(Plant Plant, IReadOnlyList<string>? Hosts, DateOnly EvaluationDate,
    IReadOnlyList<string> Documents, bool IncludeBaselineContent, string? OperatorRole, string Language, string OutDir)
{
    public IReadOnlyList<string> Documents { get; init; } = Documents ?? [];
}

/// <summary>Ergebnis von <c>verify</c> (Exitcodes 0 reproduziert, 2 abweichend, 3 Fehler).</summary>
/// <param name="EngineVersion">Version, mit der jetzt ausgewertet wurde.</param>
/// <param name="PackedEngineVersion">Version laut Paket.</param>
/// <param name="NotVerifiable">Ergebnisse ohne Baseline-Inhalt („nicht nachprüfbar“).</param>
public sealed record VerifyResult(VerifyStatus Status, string EngineVersion, string? PackedEngineVersion,
    IReadOnlyList<VerifyFinding> Findings, int SnapshotsChecked, int ResultsCompared, IReadOnlyList<string> NotVerifiable)
{
    public IReadOnlyList<VerifyFinding> Findings { get; init; } = Findings ?? [];
    public IReadOnlyList<string> NotVerifiable { get; init; } = NotVerifiable ?? [];

    /// <summary>0 reproduziert, 2 Abweichung, 3 Fehler (spec 7.7).</summary>
    [JsonIgnore]
    public int ExitCode => Status switch { VerifyStatus.Reproduced => 0, VerifyStatus.Mismatch => 2, _ => 3 };
}

[JsonConverter(typeof(CamelCaseEnumConverter<VerifyStatus>))]
public enum VerifyStatus { Reproduced, Mismatch, Error }

/// <summary>Ein Befund von verify (falsche Prüfsumme, fehlende Datei, abweichendes Ergebnis …).</summary>
public sealed record VerifyFinding(string Code, string? File, string? HostName, string? RuleId, string Message);

/// <summary>Ergebnis von <c>compare-snapshot</c> (Exitcodes 0 konsistent, 1 abweichend, 3 Fehler).</summary>
public sealed record SampleResult(SampleStatus Status, SampleComparison? Comparison, string? ErrorCode, string? Message)
{
    [JsonIgnore]
    public int ExitCode => Status switch { SampleStatus.Consistent => 0, SampleStatus.Different => 1, _ => 3 };
}

[JsonConverter(typeof(CamelCaseEnumConverter<SampleStatus>))]
public enum SampleStatus { Consistent, Different, Error }

/// <summary>Geschriebene Abhilfe-Dateien (O10, nie ausgeführt).</summary>
/// <param name="Directory">remediation/&lt;host&gt;/&lt;runId&gt;/</param>
/// <param name="Files">Geschriebene Dateien (vollständige Pfade).</param>
/// <param name="IncludedRuleIds">Regeln mit Abhilfe in den Dateien.</param>
/// <param name="Excluded">Weggelassene Regeln mit Grund.</param>
/// <param name="ManualRuleIds">Regeln, die nur als Handlungsschritt im README stehen.</param>
public sealed record RemediationOutput(string Directory, IReadOnlyList<string> Files, IReadOnlyList<string> IncludedRuleIds,
    IReadOnlyList<RemediationExclusion> Excluded, IReadOnlyList<string> ManualRuleIds);

/// <summary>Warum eine Regel nicht in der Abhilfe steht.</summary>
public sealed record RemediationExclusion(string RuleId, RemediationExclusionReason Reason, string? EntryId);

[JsonConverter(typeof(CamelCaseEnumConverter<RemediationExclusionReason>))]
public enum RemediationExclusionReason { ValidException, PendingException, Tailored, NoRemediation, NotFailing }

/// <summary>Optionen der Erfassung dieses Rechners (spec 7.4 / <c>collect</c>).</summary>
public sealed record CollectOptions(bool Minimal = false, bool Deep = false, bool Pseudonymize = false);

/// <summary>Ergebnis der Erfassung mit Rechteerhöhung über den Übergabeordner (spec 7.6).</summary>
/// <param name="SnapshotPath">Export im Übergabeordner, wenn erfolgreich.</param>
/// <param name="DifferentUserProfile">Erhöhung mit anderem Konto („über die Schulter“): Profile unterscheiden sich.</param>
public sealed record HandoffResult(bool Success, string? SnapshotPath, int ExitCode, string? ErrorCode, string? Message,
    bool DifferentUserProfile);
