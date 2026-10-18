// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;

namespace Riegel.Core.Reports.Pdf;

/// <summary>
/// Erhebungsprotokoll (O14): je importiertem Export Rechner, Erfassungszeit, Sammler mit Version und Exe-SHA-256,
/// Export-SHA-256, Ergebnis der Prüfsummendatei, Administrator, Optionen und Herkunft; erhebende Rolle, Zeuge, die
/// Integritätsaussage und Unterschriftszeilen „Erhoben von“ und „Zeuge (Betreiber)“.
/// </summary>
internal sealed class CollectionRecordLayout(CollectionRecordData data, string? fallbackFrom, PdfTexts t, PdfText text, string family,
    PageFrame frame, PdfLimits limits) : DocumentLayout(t, text, family, frame, limits)
{
    protected override void Compose()
    {
        var import = data.Import;
        TitleBlock(T.T("RecordDoc_Title"), T.F("RecordDoc_Subtitle", data.PlantName, import.Id));
        FontFallbackNote(fallbackFrom);
        Banners([]);

        var info = KeyValueTable(4.2);
        KeyValue(info, T.T("Cover_Plant"), $"{data.PlantName} ({data.PlantId})", bold: true);
        KeyValue(info, T.T("RecordDoc_Import"), import.Id, bold: true);
        KeyValue(info, T.T("RecordDoc_ImportedAt"), PdfTexts.DateTime(import.ImportedAtUtc));
        KeyValue(info, T.T("RecordDoc_CollectedBy"), T.OrDash(import.CollectedByRole));
        KeyValue(info, T.T("RecordDoc_Witness"), T.OrDash(import.WitnessRole));
        KeyValue(info, T.T("RecordDoc_Operator"), T.OrDash(import.OperatorRole));
        KeyValue(info, T.T("RecordDoc_Files"), T.Number(import.Files.Count));
        KeyValue(info, T.T("Cover_Tool"), ToolLine(data.Tool));
        KeyValue(info, T.T("Cover_Generated"), PdfTexts.DateTime(data.GeneratedAtUtc));
        Paragraph(T.T("RecordDoc_Intro"), small: true).Format.SpaceBefore = 4;

        Heading1(T.T("RecordDoc_FilesHeading"));
        if (import.Files.Count == 0) Paragraph(T.T("RecordDoc_NoFiles"));
        foreach (var f in import.Files.Take(Limits.RegisterRows))
        {
            var table = NewTable(4.2, 12.8);
            var head = table.AddRow();
            head.Shading.Color = PdfColors.Rgb(PdfColors.HeaderLight);
            head.Cells[0].MergeRight = 1;
            CellText(table, head, 0, f.HostName, bold: true);
            void Line(string key, string? value, bool bold = false, MigraDoc.DocumentObjectModel.Color? color = null, double? size = null)
            {
                var row = table.AddRow();
                CellText(table, row, 0, key, color: MutedColor);
                CellText(table, row, 1, value, bold: bold, color: color, size: size);
            }
            Line(T.T("RecordDoc_File"), f.FileName);
            Line(T.T("RecordDoc_CollectedAt"), PdfTexts.DateTime(f.CollectedAtUtc), bold: true);
            Line(T.T("Machine_Collector"), $"{f.CollectorName} {f.CollectorVersion}");
            Line(T.T("RecordDoc_CollectorSha"), f.CollectorExeSha256 ?? T.T("RecordDoc_NoExe"), size: SmallSize);
            Line(T.T("Machine_SnapshotSha"), f.Sha256, size: SmallSize);
            Line(T.T("RecordDoc_Sidecar"), T.T(f.SidecarOk ? "RecordDoc_SidecarOk" : "RecordDoc_SidecarBad"), bold: true,
                color: f.SidecarOk ? GoodColor : WarningColor);
            Line(T.T("Machine_Admin"), T.YesNo(f.IsAdmin), color: f.IsAdmin ? null : WarningColor);
            Line(T.T("Machine_Options"), T.Options(f.Options));
            Line(T.T("Machine_Origin"), T.Origin(f.Origin));
            Line(T.T("Machine_RunId"), f.RunId);
            KeepStart(table, whole: true);
            Spacer(4);
        }
        Truncated(import.Files.Count, Limits.RegisterRows);

        Heading2(T.T("Method_Integrity_Heading"));
        Paragraph(T.Common("Integrity_Statement"));

        Heading1(T.T("RecordDoc_SignHeading"), newPage: import.Files.Count > 2);
        Paragraph(T.T("RecordDoc_SignIntro")).Format.KeepWithNext = true;
        ReferenceLine(T.F("RecordDoc_Reference", import.Id, data.PlantName));
        SignatureBlocks(
        [
            (T.T("RecordDoc_SignCollected"), import.CollectedByRole),
            (T.T("RecordDoc_SignWitness"), import.WitnessRole),
        ], remarks: true);
    }
}
