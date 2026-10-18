// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.Json.Serialization;
using Riegel.Core.Model.Reports;
using Riegel.Core.Settings;

namespace Riegel.Core.Model.Json;

/// <summary>
/// Quellgenerierte JSON-Beschreibung aller Vertragstypen (spec 7.2): camelCase, Aufzählungen als camelCase-Text,
/// <c>"type"</c> unterscheidet die Prüfungen, feste Reihenfolge der Eigenschaften (Deklaration), invariant,
/// eingerückt mit LF. Unbekannte Eigenschaften sind ein Fehler (geschlossenes Vokabular).
/// Nicht direkt <c>Default</c> benutzen, sondern <see cref="RiegelJson"/>: nur dort gilt die lesbare Kodierung von
/// Umlauten, und Prüfsummen hängen davon ab.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.Unspecified,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    IndentSize = 2,
    NewLine = "\n",
    AllowOutOfOrderMetadataProperties = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    AllowTrailingCommas = false,
    NumberHandling = JsonNumberHandling.Strict,
    MaxDepth = 64,
    Converters = [typeof(UtcDateTimeOffsetConverter)])]
// Dateien
[JsonSerializable(typeof(Snapshot))]
[JsonSerializable(typeof(RuleFile))]
[JsonSerializable(typeof(CatalogueDocument))]
[JsonSerializable(typeof(LifecycleTable))]
[JsonSerializable(typeof(BuildTable))]
[JsonSerializable(typeof(CapabilityFile))]
[JsonSerializable(typeof(CapabilityTable))]
[JsonSerializable(typeof(DataFile))]
[JsonSerializable(typeof(CatalogueData))]
[JsonSerializable(typeof(ExceptionFile))]
[JsonSerializable(typeof(TailoringFile))]
[JsonSerializable(typeof(MeasureFile))]
[JsonSerializable(typeof(Plant))]
[JsonSerializable(typeof(Run))]
[JsonSerializable(typeof(ApprovedState))]
[JsonSerializable(typeof(ImportRecord))]
[JsonSerializable(typeof(BaselineFile))]
[JsonSerializable(typeof(EvidenceManifest))]
[JsonSerializable(typeof(CollectionPlan))]
[JsonSerializable(typeof(RiegelSettings))]
// Einzelne Teile (Inhalts-Hashes, Ausgaben mit --format json)
[JsonSerializable(typeof(Rule))]
[JsonSerializable(typeof(CheckSpec))]
[JsonSerializable(typeof(SnapshotItem))]
[JsonSerializable(typeof(RuleResult))]
[JsonSerializable(typeof(List<RuleResult>))]
[JsonSerializable(typeof(ExceptionEntry))]
[JsonSerializable(typeof(TailoringEntry))]
[JsonSerializable(typeof(MeasureEntry))]
[JsonSerializable(typeof(RunSummary))]
[JsonSerializable(typeof(RunInfo))]
[JsonSerializable(typeof(List<RunInfo>))]
[JsonSerializable(typeof(RunComparison))]
[JsonSerializable(typeof(SampleComparison))]
[JsonSerializable(typeof(VerifyResult))]
[JsonSerializable(typeof(SampleResult))]
[JsonSerializable(typeof(RemediationOutput))]
[JsonSerializable(typeof(ValidationIssue))]
[JsonSerializable(typeof(List<ValidationIssue>))]
[JsonSerializable(typeof(AttributionMap))]
[JsonSerializable(typeof(EvaluationContext))]
[JsonSerializable(typeof(ReadLimits))]
[JsonSerializable(typeof(HashCheck))]
[JsonSerializable(typeof(ImportedBaseline))]
[JsonSerializable(typeof(CollectOptions))]
[JsonSerializable(typeof(HandoffResult))]
[JsonSerializable(typeof(PdfOptions))]
// Berichtsdaten (SampleData-Rundreise, --format json)
[JsonSerializable(typeof(ReportData))]
[JsonSerializable(typeof(SummaryData))]
[JsonSerializable(typeof(TargetConfigData))]
[JsonSerializable(typeof(ApprovalSheetData))]
[JsonSerializable(typeof(CollectionRecordData))]
// Freie Werte
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(SortedDictionary<string, JsonElement>))]
[JsonSerializable(typeof(List<string>))]
public sealed partial class RiegelJsonContext : JsonSerializerContext;
