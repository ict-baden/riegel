// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;

namespace Riegel.Core.Services;

// ---- Dienst-Schnittstellen (spec 7.2). Jede Umsetzung entsteht in der Factory ihres Ordners (<Bereich>Factory.cs). ----

/// <summary>Katalog laden, prüfen und mit Baselines verbinden (Paket engine).</summary>
public interface ICatalogueProvider
{
    /// <summary>Der eingebettete Katalog (rules/** in Riegel.Core).</summary>
    ICatalogue LoadEmbedded();

    /// <summary>Ein Katalog als <see cref="CatalogueDocument"/> mit Regeln und Tabellen (z. B. aus dem Nachweispaket).</summary>
    ICatalogue Load(Stream catalogueJson, ReadLimits l);

    /// <summary>Eigener Katalog plus importierte Baselines (je mit eigener RuleSetId).</summary>
    ICatalogue Combine(ICatalogue core, IEnumerable<ImportedBaseline> b);
}

/// <summary>Fakten aus einem Export ableiten (spec 2.5; Paket engine). Unbekannt = JSON null.</summary>
public interface IFactsDeriver
{
    IReadOnlyDictionary<string, JsonElement> Derive(Snapshot s, ICatalogue c);
}

/// <summary>Regeln auswerten (Paket engine): rohe Status und Flags, ohne Register, Regressionen und Wertung.</summary>
public interface IEvaluator
{
    IReadOnlyList<RuleResult> Evaluate(Snapshot s, ICatalogue c, EvaluationContext ctx);
}

/// <summary>Exporte lesen (Paket parsing): JSON, .json.gz und Bordmittel-Pakete.</summary>
public interface ISnapshotReader
{
    Snapshot Read(string path, ReadLimits l);

    HashCheck VerifySidecar(string path);

    Snapshot FromBuiltinBundle(string folder, ReadLimits l);
}

/// <summary>Herkunft der Werte zuordnen (spec 2.10; Paket parsing).</summary>
public interface IValueSourceAttributor
{
    AttributionMap Build(Snapshot s);
}

/// <summary>Kunden-Baselines importieren (spec 2.15; Paket parsing).</summary>
public interface IBaselineImporter
{
    ImportedBaseline Import(string path, string name, ReadLimits l);
}

/// <summary>Anlagenordner (spec 7.6; Paket workflow).</summary>
public interface IPlantStore
{
    Plant Create(string dir, string name);

    Plant Open(string dir, bool readOnly);

    void SavePlant(Plant p, string expectedSha256);

    ImportRecord Import(Plant p, IReadOnlyList<string> paths, ImportOptions o);

    void SaveRun(Plant p, Run r);

    IReadOnlyList<RunInfo> ListRuns(Plant p, string host);

    Run LoadRun(Plant p, string runId);

    ApprovedState? LoadApproved(Plant p, string host);

    void SaveApproved(Plant p, ApprovedState a);
}

/// <summary>Register: Ausnahmen, Zuschnitt, Maßnahmen (spec 2.11–2.13; Paket workflow).</summary>
public interface IRegisterService
{
    RegisterSet Load(Plant p);

    void Save(Plant p, RegisterSet r, RegisterHashes expected);

    IReadOnlyList<RuleResult> Apply(IReadOnlyList<RuleResult> raw, RegisterSet r, Plant p, string host, DateOnly today);

    IReadOnlyList<ValidationIssue> Validate(RegisterSet r, ICatalogue c, Plant p, DateOnly today);

    RegisterSet Approve(RegisterSet r, RegisterKind kind, IReadOnlyList<string> ids, Approval a);

    RegisterSet ImportTemplate(RegisterSet r, string templatePath, ReadLimits l);
}

/// <summary>Vergleich und Abweichungen (spec 2.14; Paket workflow).</summary>
public interface IRunComparer
{
    RunComparison Compare(Run? from, Snapshot? fromSnapshot, Run to, Snapshot toSnapshot, ComparisonBasis b);

    SampleComparison CompareSnapshots(Snapshot packed, Snapshot fresh);
}

/// <summary>Wertung (spec 2.6; Paket workflow).</summary>
public interface IScoring
{
    (RunSummary, IReadOnlyList<CategoryScore>) Score(IReadOnlyList<RuleResult> r, ICatalogue c, Run? previous, bool isDc);
}

/// <summary>Auswerten und speichern (Paket workflow).</summary>
public interface IRunService
{
    Run EvaluateAndStore(Plant p, string snapshotPath, RunOptions o);

    Run EvaluateOnly(Snapshot s, RunOptions o);
}

/// <summary>Berichtsdaten bauen (Paket workflow).</summary>
public interface IReportDataBuilder
{
    ReportData Build(ReportRequest r);

    SummaryData BuildSummary(IReadOnlyList<Plant> plants, DateOnly d, string lang);

    TargetConfigData BuildTargetConfig(Plant p, string? groupId, string lang);

    ApprovalSheetData BuildApprovalSheet(Plant p, IReadOnlyList<string>? ids, string lang);

    CollectionRecordData BuildCollectionRecord(Plant p, string importId, string lang);
}

/// <summary>PDF-Dokumente (Paket pdf). Jede Methode gibt den Pfad der geschriebenen Datei zurück.</summary>
public interface IPdfWriter
{
    string WriteReport(ReportData d, PdfOptions o);

    string WriteSummary(SummaryData d, PdfOptions o);

    string WriteTargetConfig(TargetConfigData d, PdfOptions o);

    string WriteApprovalSheet(ApprovalSheetData d, PdfOptions o);

    string WriteCollectionRecord(CollectionRecordData d, PdfOptions o);
}

/// <summary>Excel und CSV (Paket data-outputs).</summary>
public interface ITabularWriter
{
    string WriteWorkbook(ReportData d, string outDir);

    string WriteTargetConfigWorkbook(TargetConfigData d, string outDir);

    string WriteCsv(ReportData d, string outDir);
}

/// <summary>Nachweispaket, verify und Stichprobe (Paket data-outputs).</summary>
public interface IEvidencePack
{
    string Write(EvidenceRequest r);

    VerifyResult Verify(string zipPath, ReadLimits l);

    SampleResult CompareSample(string freshSnapshotPath, string zipPath, string? host, ReadLimits l);
}

/// <summary>Abhilfe nur als Dateien (O10; Paket data-outputs).</summary>
public interface IRemediationWriter
{
    RemediationOutput Write(Run r, Snapshot s, ICatalogue c, RegisterSet reg, string outDir, string lang);
}

/// <summary>
/// Alles Windows-Eigene hinter einer Schnittstelle (Paket collector; unter Linux und in Tests <see cref="NullPlatform"/>).
/// </summary>
public interface IPlatform
{
    /// <summary>Kann dieser Rechner erfasst werden?</summary>
    bool CanCollect { get; }

    /// <summary>Läuft der Prozess mit erhöhten Rechten?</summary>
    bool IsElevated { get; }

    /// <summary>Diesen Rechner erfassen (Herkunft app).</summary>
    Snapshot CollectThisMachine(CollectOptions o, CancellationToken ct);

    /// <summary>Erhöhte Seite: Übergabeordner selbst anlegen; lehnt vorhandene Ordner und Reparse-Punkte ab.</summary>
    string CreateHandoffDirectory(string token);

    /// <summary>Nicht erhöhte Seite: Riegel mit UAC starten und auf den Export warten.</summary>
    HandoffResult RunElevatedCollect(string token, CancellationToken ct);

    /// <summary>Datei, die Riegel erzeugt hat, oder http/https-Link mit der Shell öffnen.</summary>
    void OpenWithShell(string path);

    /// <summary>Sammler-Paket (collector\ neben der App) nach <paramref name="targetDir"/> kopieren (O15).</summary>
    string ExportCollectorBundle(string targetDir);
}
