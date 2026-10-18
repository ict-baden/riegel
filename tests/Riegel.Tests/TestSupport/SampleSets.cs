// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Samples;

namespace Riegel.Tests.TestSupport;

/// <summary>
/// Zugriff auf die Beispieldaten (<see cref="SampleData"/>) für Tests der Pakete pdf, data-outputs und app, auch als
/// Theorie-Daten (Name → Objekt) und als JSON-Text.
/// </summary>
public static class SampleSets
{
    /// <summary>Alle Berichtsdokumente als (Name, Objekt).</summary>
    public static TheoryData<string> DocumentNames { get; } = new() { "report", "summary", "targetConfig", "approvalSheet", "collectionRecord" };

    /// <summary>Ein Berichtsdokument nach Name.</summary>
    public static object Document(string name) => name switch
    {
        "report" => SampleData.Report(),
        "summary" => SampleData.Summary(),
        "targetConfig" => SampleData.TargetConfig(),
        "approvalSheet" => SampleData.ApprovalSheet(),
        "collectionRecord" => SampleData.CollectionRecord(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    /// <summary>JSON eines Berichtsdokuments (wie <c>--format json</c>).</summary>
    public static string DocumentJson(string name) => Document(name) switch
    {
        Core.Model.Reports.ReportData d => RiegelJson.Serialize(d),
        Core.Model.Reports.SummaryData d => RiegelJson.Serialize(d),
        Core.Model.Reports.TargetConfigData d => RiegelJson.Serialize(d),
        Core.Model.Reports.ApprovalSheetData d => RiegelJson.Serialize(d),
        Core.Model.Reports.CollectionRecordData d => RiegelJson.Serialize(d),
        _ => throw new InvalidOperationException(),
    };

    /// <summary>Alle Beispielläufe als Dateien im Vertragsformat (Name → JSON).</summary>
    public static IEnumerable<(string Name, string Json)> RunFiles() =>
        SampleData.Runs().Select(r => (r.Id, RiegelJson.Serialize(r)));

    /// <summary>Die Beispielregister als Dateien (Schema → JSON).</summary>
    public static IEnumerable<(string Schema, string Json)> RegisterFiles()
    {
        var r = SampleData.Registers();
        yield return (SchemaIds.Exceptions, RiegelJson.Serialize(r.Exceptions));
        yield return (SchemaIds.Tailoring, RiegelJson.Serialize(r.Tailoring));
        yield return (SchemaIds.Measures, RiegelJson.Serialize(r.Measures));
    }
}
