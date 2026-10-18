// SPDX-License-Identifier: MIT
using MigraDoc.DocumentObjectModel;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;

namespace Riegel.Core.Reports.Pdf;

/// <summary>
/// Übersicht für die Geschäftsführung (O6): genau eine A4-Seite über bis zu 12 Anlagen – je Anlage Rechner, Anteil
/// unterstützter Betriebssysteme, offene hohe Befunde mit dem Verlauf der letzten vier Monatsenden, Verschlechterungen,
/// bald ablaufende Ausnahmen, überfällige Maßnahmen und die Werte; dazu die fünf häufigsten hohen Befunde und die Banner.
/// </summary>
internal sealed class SummaryLayout(SummaryData data, string? fallbackFrom, PdfTexts t, PdfText text, string family, PageFrame frame,
    PdfLimits limits) : DocumentLayout(t, text, family, frame, limits)
{
    /// <summary>Mehr Anlagen passen nicht sicher auf eine Seite; weitere stehen dann auf einer Folgeseite.</summary>
    public const int PlantsPerPage = 12;

    protected override void Compose()
    {
        CompactBanners = true;
        TitleBlock(T.T("SummaryDoc_Title"), T.F("SummaryDoc_Subtitle", PdfTexts.Date(data.Date), PdfTexts.DateTime(data.GeneratedAtUtc)), 18);
        FontFallbackNote(fallbackFrom);
        Banners(data.Banners);

        var months = data.Plants.SelectMany(p => p.Trend).Select(p => p.MonthEnd).Distinct().Order().ToList();
        var trendHeader = months.Count == 0 ? T.T("Col_Trend")
            : T.F("Col_TrendMonths", Month(months[0]), Month(months[^1]));
        var table = NewTable(4.1, 1.35, 1.35, 1.2, 2.1, 1.25, 1.55, 1.6, 2.5);
        HeaderRow(table, light: false, 6.5, T.T("Col_Plant"), T.T("Col_Machines"), T.T("Col_SupportedOs"), T.T("Col_OpenHigh"), trendHeader,
            T.T("Col_Regressions"), T.T("Col_Expiring"), T.T("Col_Overdue"), T.T("Col_Scores"));
        foreach (var p in data.Plants)
        {
            var row = table.AddRow();
            // Unvollständig geprüft: Werte grau, Prüfumfang rot (Legende); kein eigener Vermerk, damit 12 Anlagen auf die Seite passen
            CellText(table, row, 0, p.PlantName, bold: true, size: 7.5);
            CellText(table, row, 1, T.Number(p.Machines)).Format.Alignment = ParagraphAlignment.Right;
            CellText(table, row, 2, T.Percent(p.SupportedOsPercent), color: p.SupportedOsPercent is < 100 ? WarningColor : null)
                .Format.Alignment = ParagraphAlignment.Right;
            CellText(table, row, 3, T.Number(p.OpenHigh), bold: true, color: p.OpenHigh > 0 ? WarningColor : null).Format.Alignment = ParagraphAlignment.Right;
            CellText(table, row, 4, Trend(p.Trend));
            CellText(table, row, 5, T.Number(p.Regressions), bold: p.Regressions > 0, color: p.Regressions > 0 ? WarningColor : null).Format.Alignment = ParagraphAlignment.Right;
            CellText(table, row, 6, T.Number(p.ExceptionsExpiring), bold: p.ExceptionsExpiring > 0).Format.Alignment = ParagraphAlignment.Right;
            CellText(table, row, 7, T.Number(p.MeasuresOverdue), bold: p.MeasuresOverdue > 0, color: p.MeasuresOverdue > 0 ? WarningColor : null).Format.Alignment = ParagraphAlignment.Right;
            CellText(table, row, 8, T.F("Scores_Value", T.Percent(p.Score), T.Percent(p.Documented)), bold: true, color: p.Incomplete ? GreyedColor : null);
            CellNote(table, row, 8, T.F("Scores_Share", T.Percent(p.CheckedShare)), p.Incomplete ? WarningColor : null);
        }
        if (data.Plants.Count > 1)
        {
            var total = table.AddRow();
            total.Shading.Color = PdfColors.Rgb(PdfColors.HeaderLight);
            CellText(table, total, 0, T.T("SummaryDoc_Total"), bold: true);
            CellText(table, total, 1, T.Number(data.Plants.Sum(p => p.Machines)), bold: true).Format.Alignment = ParagraphAlignment.Right;
            CellText(table, total, 2, "");
            CellText(table, total, 3, T.Number(data.Plants.Sum(p => p.OpenHigh)), bold: true).Format.Alignment = ParagraphAlignment.Right;
            CellText(table, total, 4, "");
            CellText(table, total, 5, T.Number(data.Plants.Sum(p => p.Regressions)), bold: true).Format.Alignment = ParagraphAlignment.Right;
            CellText(table, total, 6, T.Number(data.Plants.Sum(p => p.ExceptionsExpiring)), bold: true).Format.Alignment = ParagraphAlignment.Right;
            CellText(table, total, 7, T.Number(data.Plants.Sum(p => p.MeasuresOverdue)), bold: true).Format.Alignment = ParagraphAlignment.Right;
            CellText(table, total, 8, "");
        }
        if (data.Plants.Count == 0)
        {
            var row = table.AddRow();
            row.Cells[0].MergeRight = 8;
            CellText(table, row, 0, T.T("SummaryDoc_NoPlants"));
        }
        KeepStart(table, rows: 4);
        Paragraph(T.T("SummaryDoc_Legend"), small: true);

        var head = Section.AddParagraph();
        head.Format.SpaceBefore = 8;
        head.Format.SpaceAfter = 3;
        head.Format.KeepWithNext = true;
        Text.Append(head, T.T("SummaryDoc_TopFindings"), ContentWidth, 10.5, bold: true, color: RedColor);
        if (data.TopFindings.Count == 0) Paragraph(T.T("SummaryDoc_NoHighFindings"), small: true);
        else
        {
            var top = NewTable(1.9, 7.0, 2.4, 5.7);
            HeaderRow(top, T.T("Col_Rule"), T.T("Col_Title"), T.T("Col_OpenOn"), T.T("Col_Hosts"));
            foreach (var f in data.TopFindings.Take(5))
            {
                var row = top.AddRow();
                CellText(top, row, 0, f.RuleId, bold: true);
                CellText(top, row, 1, f.Title);
                CellText(top, row, 2, T.F("OpenOf", T.Number(f.OpenCount), T.Number(f.ApplicableCount)));
                CellText(top, row, 3, T.Hosts(f.Hosts, f.MoreHosts, max: 6));
            }
            KeepStart(top, whole: true);
        }
        Paragraph(T.T("SummaryDoc_Footnote"), small: true).Format.SpaceBefore = 6;
    }

    /// <summary>Offene hohe Befunde an den Monatsenden, älteste zuerst („9 · 8 · 7 · 5“; „–“ = noch keine Läufe).</summary>
    private string Trend(IReadOnlyList<TrendPoint> trend) =>
        trend.Count == 0 ? T.Dash : string.Join(" · ", trend.Select(p => T.Number(p.OpenHigh)));

    private static string Month(DateOnly date) => date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
}
