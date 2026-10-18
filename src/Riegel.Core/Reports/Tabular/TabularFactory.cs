// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Services;

namespace Riegel.Core.Reports.Tabular;

/// <summary>
/// Factory von Excel und CSV (Paket data-outputs; Signatur fest): Excel-Mappe der Anlage (O7), Soll-Konfiguration als
/// Mappe (O13) und results.csv (O3).
/// </summary>
public static class TabularFactory
{
    public static ITabularWriter Create(RiegelServices services) => new TabularWriter(() => services.Catalogue.LoadEmbedded());
}

/// <summary>Schreibt die Tabellenausgaben unter den Dateinamen aus spec 5.</summary>
/// <param name="catalogue">Quelle der englischen Regeltitel für <c>title_en</c> (eingebetteter Katalog).</param>
internal sealed class TabularWriter(Func<ICatalogue> catalogue) : ITabularWriter
{
    private ICatalogue? _catalogue;
    private bool _catalogueTried;

    /// <summary>Spalten von results.csv (spec 5 O3), invariant.</summary>
    public static IReadOnlyList<string> CsvColumns => ResultsCsv.Columns;

    public string WriteWorkbook(ReportData d, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, ReportFileNames.Workbook(d.Header.PlantName, d.Header.EvaluationDate));
        OverviewWorkbook.Write(d, path);
        return path;
    }

    public string WriteTargetConfigWorkbook(TargetConfigData d, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, ReportFileNames.TargetConfigWorkbook(d.PlantName, d.Date));
        TargetConfigWorkbook.Write(d, path);
        return path;
    }

    public string WriteCsv(ReportData d, string outDir)
    {
        Directory.CreateDirectory(outDir);
        return ResultsCsv.Write(d, Path.Combine(outDir, ReportFileNames.ResultsCsv), EnglishTitle);
    }

    /// <summary>
    /// Englischer Titel für <c>title_en</c>: aus dem Katalog, wenn die Regel dort steht (eigener Regelsatz); sonst null,
    /// dann gilt der Titel aus den Berichtsdaten (importierte Baselines haben nur den Titel ihres Autors).
    /// </summary>
    private string? EnglishTitle(string ruleSetId, string ruleId)
    {
        if (!_catalogueTried)
        {
            _catalogueTried = true;
            try { _catalogue = catalogue(); }
            catch (RiegelException) { _catalogue = null; }
        }
        var rule = _catalogue?.Find(ruleId);
        return rule is not null && string.Equals(rule.RuleSetId, ruleSetId, StringComparison.Ordinal) ? rule.Title.Get("en") : null;
    }
}
