// SPDX-License-Identifier: MIT
using System.Text;
using ClosedXML.Excel;
using Riegel.Core.Reports.Tabular;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Reports.Tabular;

/// <summary>
/// Formel-Einschleusung (spec 7.12): Werte aus Rechner- und Registerdaten, die mit <c>= + - @</c>, TAB oder CR
/// beginnen, bekommen in der CSV ein <c>'</c> und sind in Excel ausdrückliche Textzellen – nie Formeln.
/// </summary>
public sealed class FormulaInjectionTests
{
    [Theory]
    [InlineData("=1+1", true)]
    [InlineData("+1", true)]
    [InlineData("-1", true)]
    [InlineData("@A1", true)]
    [InlineData("\tx", true)]
    [InlineData("\rx", true)]
    [InlineData("a=1", false)]
    [InlineData(" =1", false)]
    [InlineData("", false)]
    public void Erkennt_formelartige_Werte(string text, bool formula)
    {
        Assert.Equal(formula, TextGuard.IsFormulaLike(text));
        Assert.Equal(formula ? "'" + text : text, TextGuard.ForCsv(text));
    }

    [Fact]
    public void CSV_setzt_ein_Hochkomma_vor_jeden_formelartigen_Wert()
    {
        using var temp = new TempFolder();
        var path = TabularTestSupport.Writer().WriteCsv(TabularTestSupport.Hostile(), temp.Path);
        var rows = TabularTestSupport.ParseCsv(Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        foreach (var row in rows.Skip(1))
            foreach (var field in row)
                Assert.False(TextGuard.IsFormulaLike(field), $"ungeschützt: {field}");
        var hostile = rows.Skip(1).Where(r => r[1] == "'=HOST()").ToList();
        Assert.NotEmpty(hostile);
        Assert.All(hostile, r => Assert.Equal("'=ID()", r[0]));
        Assert.Contains(hostile, r => r[11] == "'=HYPERLINK(\"http://example.invalid\",\"x\")");
        Assert.Contains(hostile, r => r[11] == "'\tTAB=1");
        Assert.Contains(hostile, r => r[11] == "'\rCR=1");
        Assert.Contains(hostile, r => r[11] == "'-2+3");
    }

    [Fact]
    public void Excel_schreibt_formelartige_Werte_als_Textzellen_und_nie_als_Formel()
    {
        using var temp = new TempFolder();
        var path = TabularTestSupport.Writer().WriteWorkbook(TabularTestSupport.Hostile(), temp.Path);

        // Rohdaten: kein Formelelement in irgendeinem Blatt
        foreach (var xml in TabularTestSupport.SheetXml(path))
        {
            Assert.DoesNotContain("<f>", xml, StringComparison.Ordinal);
            Assert.DoesNotContain("<f ", xml, StringComparison.Ordinal);
        }

        using var wb = new XLWorkbook(path);
        int checkedCells = 0;
        foreach (var cell in wb.Worksheets.SelectMany(ws => ws.CellsUsed()))
        {
            Assert.False(cell.HasFormula, $"{cell.Worksheet.Name}!{cell.Address}: Formel");
            if (cell.DataType != XLDataType.Text) continue;
            var text = cell.GetString();
            if (!TextGuard.IsFormulaLike(text)) continue;
            Assert.True(cell.Style.IncludeQuotePrefix, $"{cell.Worksheet.Name}!{cell.Address}: kein Text-Präfix für „{text}“");
            Assert.Equal("@", cell.Style.NumberFormat.Format);
            checkedCells++;
        }
        Assert.True(checkedCells >= 10, $"nur {checkedCells} formelartige Zellen gefunden");
        // Der Originaltext bleibt erhalten (kein „'“ im Wert), das NUL-Zeichen ist ersetzt
        Assert.Contains(wb.Worksheets.SelectMany(ws => ws.CellsUsed()), c => c.GetString() == "=HOST()");
        Assert.Contains(wb.Worksheets.SelectMany(ws => ws.CellsUsed()), c => c.GetString().Contains("NUL�im Text", StringComparison.Ordinal));
    }

    [Fact]
    public void Sehr_lange_Texte_werden_an_der_Zellgrenze_gekuerzt()
    {
        var text = new string('x', 40_000);
        var cut = TextGuard.ForExcel(text);
        Assert.Equal(TextGuard.ExcelCellMax, cut.Length);
        Assert.EndsWith("…", cut, StringComparison.Ordinal);
    }
}
