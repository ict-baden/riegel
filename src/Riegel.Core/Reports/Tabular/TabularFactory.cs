// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Resources;
using Riegel.Core.Services;

namespace Riegel.Core.Reports.Tabular;

/// <summary>
/// Factory von Excel und CSV (gehört ab dem Gerüst dem Paket data-outputs; Signatur fest). Der Platzhalter schreibt
/// unter den echten Dateinamen eine Mappe mit einem Hinweisblatt und eine CSV mit der Kopfzeile von O3.
/// </summary>
public static class TabularFactory
{
    public static ITabularWriter Create(RiegelServices services) => new StubTabularWriter();
}

/// <summary>Platzhalter (Gerüst).</summary>
internal sealed class StubTabularWriter : ITabularWriter
{
    /// <summary>Spalten von results.csv (spec 5 O3), invariant.</summary>
    public static IReadOnlyList<string> CsvColumns { get; } =
    [
        "plant", "host", "runId", "evaluatedAtUtc", "ruleSet", "ruleId", "category", "severity", "status", "flags", "expected",
        "actual", "valueSource", "exceptionId", "exceptionReviewBy", "tailoringId", "measureId", "measureDue", "title_en",
    ];

    public string WriteWorkbook(ReportData d, string outDir) =>
        Placeholder(outDir, ReportFileNames.Workbook(d.Header.PlantName, d.Header.EvaluationDate));

    public string WriteTargetConfigWorkbook(TargetConfigData d, string outDir) =>
        Placeholder(outDir, ReportFileNames.TargetConfigWorkbook(d.PlantName, d.Date));

    public string WriteCsv(ReportData d, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, ReportFileNames.ResultsCsv);
        File.WriteAllText(path, string.Join(",", CsvColumns) + "\r\n", new UTF8Encoding(false));
        return path;
    }

    private static string Placeholder(string outDir, string fileName)
    {
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, fileName);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Riegel");
        sheet.Cell(1, 1).SetValue(CommonStrings.ResourceManager.GetString(nameof(CommonStrings.Placeholder_Document), CultureInfo.InvariantCulture) ?? "");
        workbook.SaveAs(path);
        return path;
    }
}
