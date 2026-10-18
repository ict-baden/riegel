// SPDX-License-Identifier: MIT
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;

namespace Riegel.Core.Reports.Pdf;

/// <summary>
/// „Ausnahmen zur Genehmigung“ (O12, spec 2.11): neue und geänderte (beantragte) Ausnahmen und Zuschnitt-Einträge mit
/// Inhalts-Hash, Regel, Geltungsbereich, Kategorie, Begründung, Maßnahme und Referenz; die Prüfsummen der Register zum
/// Zeitpunkt des Blatts und Unterschriftszeilen je Genehmigerrolle. Nach der Unterschrift trägt man die Freigabefelder
/// ein und nennt dieses Blatt als <c>approvalRecord</c>.
/// </summary>
internal sealed class ApprovalSheetLayout(ApprovalSheetData data, string? fallbackFrom, PdfTexts t, PdfText text, string family,
    PageFrame frame, PdfLimits limits) : DocumentLayout(t, text, family, frame, limits)
{
    protected override void Compose()
    {
        TitleBlock(T.T("ApprovalDoc_Title"), T.F("ApprovalDoc_Subtitle", data.PlantName, data.SheetId));
        FontFallbackNote(fallbackFrom);
        Banners([]);

        var info = KeyValueTable(4.2);
        KeyValue(info, T.T("Cover_Plant"), $"{data.PlantName} ({data.PlantId})", bold: true);
        KeyValue(info, T.T("ApprovalDoc_Sheet"), data.SheetId, bold: true);
        KeyValue(info, T.T("Target_Date"), PdfTexts.Date(data.Date));
        KeyValue(info, T.T("Cover_Generated"), PdfTexts.DateTime(data.GeneratedAtUtc));
        KeyValue(info, T.T("Cover_Tool"), ToolLine(data.Tool));
        KeyValue(info, T.T("Register_Exceptions"), "SHA-256 " + data.ExceptionsSha256, size: SmallSize);
        KeyValue(info, T.T("Register_Tailoring"), "SHA-256 " + data.TailoringSha256, size: SmallSize);
        KeyValue(info, T.T("ApprovalDoc_Entries"), T.F("ApprovalDoc_EntriesValue",
            T.Number(data.Entries.Count(e => e.Kind == RegisterKind.Exceptions)), T.Number(data.Entries.Count(e => e.Kind == RegisterKind.Tailoring))));
        Paragraph(T.F("ApprovalDoc_Intro", data.SheetId, PdfTexts.Date(data.Date)), small: true).Format.SpaceBefore = 4;

        Heading1(T.T("ApprovalDoc_EntriesHeading"));
        if (data.Entries.Count == 0) Paragraph(T.T("ApprovalDoc_NoEntries"));
        foreach (var e in data.Entries.Take(Limits.RegisterRows)) Entry(e);
        Truncated(data.Entries.Count, Limits.RegisterRows);

        Heading1(T.T("ApprovalDoc_SignHeading"), newPage: data.Entries.Count > 2);
        Paragraph(T.T("ApprovalDoc_SignIntro")).Format.KeepWithNext = true;
        var binding = Section.AddParagraph();
        binding.Format.SpaceBefore = 4;
        binding.Format.KeepWithNext = true;
        Text.Append(binding, T.F("ApprovalDoc_Binding", data.SheetId, PdfTexts.Sha8(data.ExceptionsSha256), PdfTexts.Sha8(data.TailoringSha256),
            T.Number(data.Entries.Count)), ContentWidth, NormalSize, bold: true);
        ReferenceLine(T.F("ApprovalDoc_Reference", data.SheetId, data.PlantName, PdfTexts.Date(data.Date)));
        var approvers = data.Approvers.Count > 0 ? data.Approvers : ReportLayout.Roles(T, ReportPreset.Audit, []).Where(r => r.Step == SignOffSteps.Approved).ToList();
        SignatureBlocks([.. approvers.Select(r => (T.Step(r.Step), (string?)r.Role))]);
    }

    private void Entry(ApprovalSheetEntry e)
    {
        var table = NewTable(4.2, 12.8);
        var head = table.AddRow();
        head.HeadingFormat = false;
        head.Shading.Color = PdfColors.Rgb(PdfColors.HeaderLight);
        var cell = head.Cells[0];
        cell.MergeRight = 1;
        var kind = T.T(e.Kind == RegisterKind.Tailoring ? "ApprovalDoc_KindTailoring" : "ApprovalDoc_KindException");
        CellText(table, head, 0, $"{e.Id} · {kind} · {e.RuleId} {e.RuleTitle}", bold: true);
        void Line(string key, string? value, bool bold = false)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var row = table.AddRow();
            CellText(table, row, 0, key, color: MutedColor);
            CellText(table, row, 1, value, bold: bold);
        }
        Line(T.T("Col_State"), T.State(e.State), bold: true);
        Line(T.T("Col_Severity"), T.Severity(e.Severity));
        Line(T.T("ApprovalDoc_Scope"), T.Scope(e.Scope));
        if (e.Kind == RegisterKind.Exceptions) Line(T.T("ApprovalDoc_Category"), T.Reason(e.Category));
        Line(T.T(e.Kind == RegisterKind.Tailoring ? "Col_Reason" : "Col_Justification"), e.Text);
        Line(T.T("ApprovalDoc_Compensating"), e.CompensatingMeasure);
        Line(T.T("ApprovalDoc_RefLabel"), e.Reference);
        Line(T.T("ApprovalDoc_AcceptedValue"), e.AcceptedValue);
        Line(T.T("ApprovalDoc_Measure"), e.Measure);
        Line(T.T("ApprovalDoc_ContentHash"), e.ContentSha256);
        Line(T.T("ApprovalDoc_Decision"), T.T("ApprovalDoc_DecisionValue"));
        // Platz zum Ankreuzen und für das Prüfdatum
        var decision = table.Rows[table.Rows.Count - 1];
        decision.Height = Unit.FromCentimeter(0.9);
        decision.HeightRule = RowHeightRule.AtLeast;
        decision.VerticalAlignment = VerticalAlignment.Center;
        KeepStart(table, whole: true);
        Spacer(4);
    }
}
