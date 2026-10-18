// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using Riegel.Core.About;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Resources;
using Riegel.Core.Services;

namespace Riegel.Core.Reports.Pdf;

/// <summary>
/// Factory der PDF-Dokumente (gehört ab dem Gerüst dem Paket pdf; Signatur fest). Der Platzhalter schreibt unter den
/// echten Dateinamen ein einseitiges PDF mit dem Hinweis, dass das Dokument noch entsteht.
/// </summary>
public static class PdfFactory
{
    public static IPdfWriter Create(RiegelServices services) => new StubPdfWriter();
}

/// <summary>Platzhalter (Gerüst).</summary>
internal sealed class StubPdfWriter : IPdfWriter
{
    public string WriteReport(ReportData d, PdfOptions o)
    {
        var path = Placeholder(o.OutDir, ReportFileNames.Report(d.Header.PlantName, d.Header.EvaluationDate), d.Header.PlantName);
        if (o.PerMachine)
            foreach (var m in d.Machines)
                Placeholder(o.OutDir, ReportFileNames.MachineReport(d.Header.PlantName, d.Header.EvaluationDate, m.HostName), m.HostName);
        return path;
    }

    public string WriteSummary(SummaryData d, PdfOptions o) => Placeholder(o.OutDir, ReportFileNames.Summary(d.Date), "");

    public string WriteTargetConfig(TargetConfigData d, PdfOptions o) =>
        Placeholder(o.OutDir, ReportFileNames.TargetConfigPdf(d.PlantName, d.Date), d.PlantName);

    public string WriteApprovalSheet(ApprovalSheetData d, PdfOptions o) =>
        Placeholder(o.OutDir, ReportFileNames.ApprovalSheet(d.PlantName, d.Date), d.PlantName);

    public string WriteCollectionRecord(CollectionRecordData d, PdfOptions o) =>
        Placeholder(o.OutDir, ReportFileNames.CollectionRecord(d.PlantName, d.Import.Id), d.PlantName);

    private static string Placeholder(string outDir, string fileName, string subject)
    {
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, fileName);
        var text = CommonStrings.ResourceManager.GetString(nameof(CommonStrings.Placeholder_Document), CultureInfo.InvariantCulture) ?? "";
        MinimalPdf.Write(path, [$"Riegel {AppInfo.Version}", fileName, subject, text]);
        return path;
    }
}

/// <summary>
/// Ein einseitiges, gültiges PDF 1.4 mit Helvetica (Standardschrift, nicht eingebettet, WinAnsi). Nur für Platzhalter
/// und Tests; Zeichen außerhalb von Latin-1 werden zu „?“.
/// </summary>
internal static class MinimalPdf
{
    public static void Write(string path, IReadOnlyList<string> lines)
    {
        var content = new StringBuilder("BT /F1 11 Tf 56 780 Td 14 TL\n");
        foreach (var line in lines) content.Append('(').Append(Escape(line)).Append(") Tj T*\n");
        content.Append("ET\n");
        var stream = Latin1(content.ToString());
        var head = Latin1("<< /Length " + stream.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n");
        byte[] contents = [.. head, .. stream, .. Latin1("endstream")];

        var objects = new List<byte[]>
        {
            Latin1("<< /Type /Catalog /Pages 2 0 R >>"),
            Latin1("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Latin1("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>"),
            Latin1("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
            contents,
        };
        using var output = new MemoryStream();
        void Put(byte[] bytes) => output.Write(bytes);
        Put(Latin1("%PDF-1.4\n"));
        var offsets = new List<long>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            Put(Latin1($"{i + 1} 0 obj\n"));
            Put(objects[i]);
            Put(Latin1("\nendobj\n"));
        }
        long xref = output.Position;
        var table = new StringBuilder($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) table.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        table.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
        Put(Latin1(table.ToString()));
        File.WriteAllBytes(path, output.ToArray());
    }

    private static string Escape(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (c is '(' or ')' or '\\') sb.Append('\\').Append(c);
            else if (c < 32) sb.Append(' ');
            else sb.Append(c <= 255 ? c : '?');
        }
        return sb.ToString();
    }

    private static byte[] Latin1(string text) => Encoding.Latin1.GetBytes(text);
}
