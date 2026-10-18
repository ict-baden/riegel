// SPDX-License-Identifier: MIT
using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using Riegel.Core.Localization;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Services;

namespace Riegel.Core.Reports.Pdf;

/// <summary>Ein gebautes, noch nicht gerendertes Dokument mit seinen Metadaten.</summary>
/// <param name="Language">Sprache, in der es tatsächlich entsteht.</param>
/// <param name="FallbackFrom">Angefragte Sprache, wenn mangels Schrift Englisch genommen wurde (RGL-0510).</param>
internal sealed record BuiltDocument(Document Document, string Family, string Language, string? FallbackFrom, string Title,
    string Subject, string Keywords, DateTimeOffset CreatedAtUtc);

/// <summary>
/// Schreibt die PDF-Dokumente aus den Berichtsdaten (spec 5: O4, O5, O6, O12, O13, O14). Liest nur das Logo und die
/// Schriften, schreibt nur in den Zielordner (erst eine temporäre Datei, dann ersetzen) und startet nichts.
/// </summary>
internal sealed class PdfWriter : IPdfWriter
{
    private readonly Func<string?> _cjkFamily;
    private readonly PdfLimits _limits;

    /// <param name="cjkFamily">Liefert die CJK-Familie oder null (Tests: Rückfall ohne CJK-Schrift prüfen).</param>
    internal PdfWriter(Func<string?>? cjkFamily = null, PdfLimits? limits = null)
    {
        _cjkFamily = cjkFamily ?? (() => PdfFonts.CjkFamily);
        _limits = limits ?? PdfLimits.Default;
    }

    /// <summary>
    /// Nur ein MigraDoc-Satz zur Zeit im Prozess: MigraDoc 6.2.4 merkt sich die zuletzt benutzte Schrift in statischen
    /// Feldern ohne Sperre (in Zutritt nachgewiesen). Gilt auch für die Umbruchprüfung in den Tests.
    /// </summary>
    internal static readonly Lock RenderGate = new();

    // ---- IPdfWriter ----------------------------------------------------------------------------------------

    public string WriteReport(ReportData d, PdfOptions o)
    {
        ArgumentNullException.ThrowIfNull(d);
        Check(o);
        var logo = CoverLogo.Load(d.Header.LogoPath);
        var path = Target(o, ReportFileNames.Report(SafeName(d.Header.PlantName), d.Header.EvaluationDate));
        Save(BuildReport(d, o.IncludeAppendix, null, logo), path);
        if (o.PerMachine)
            foreach (var m in d.Machines)
                Save(BuildReport(d, true, m.HostName, logo), Target(o, ReportFileNames.MachineReport(SafeName(d.Header.PlantName), d.Header.EvaluationDate, SafeName(m.HostName))));
        return path;
    }

    public string WriteSummary(SummaryData d, PdfOptions o)
    {
        ArgumentNullException.ThrowIfNull(d);
        Check(o);
        var path = Target(o, ReportFileNames.Summary(d.Date));
        Save(BuildSummary(d), path);
        return path;
    }

    public string WriteTargetConfig(TargetConfigData d, PdfOptions o)
    {
        ArgumentNullException.ThrowIfNull(d);
        Check(o);
        var path = Target(o, ReportFileNames.TargetConfigPdf(SafeName(d.PlantName), d.Date));
        Save(BuildTargetConfig(d), path);
        return path;
    }

    public string WriteApprovalSheet(ApprovalSheetData d, PdfOptions o)
    {
        ArgumentNullException.ThrowIfNull(d);
        Check(o);
        var path = Target(o, ReportFileNames.ApprovalSheet(SafeName(d.PlantName), d.Date));
        Save(BuildApprovalSheet(d), path);
        return path;
    }

    public string WriteCollectionRecord(CollectionRecordData d, PdfOptions o)
    {
        ArgumentNullException.ThrowIfNull(d);
        Check(o);
        var path = Target(o, ReportFileNames.CollectionRecord(SafeName(d.PlantName), SafeName(d.Import.Id)));
        Save(BuildCollectionRecord(d), path);
        return path;
    }

    // ---- Dokumente bauen (auch für Tests und Vorschau) -----------------------------------------------------------

    /// <param name="machineHost">Rechnerbericht (O5) für diesen Rechner; null = Anlagenbericht.</param>
    internal BuiltDocument BuildReport(ReportData d, bool includeAppendix = true, string? machineHost = null, CoverLogo.Checked? logo = null)
    {
        var data = d;
        if (machineHost is not null)
        {
            var machine = d.Machines.FirstOrDefault(m => string.Equals(m.HostName, machineHost, StringComparison.OrdinalIgnoreCase))
                ?? throw RiegelException.Common(RglCodesCommon.NotFound, machineHost);
            data = MachineView.For(d, machine);
        }
        var h = data.Header;
        return Build(h.Language, h.PreTranslated, h.GeneratedAtUtc, h.Tool, t =>
        {
            var title = ReportLayout.Title(t, h.Preset);
            var document = machineHost is null ? t.F("Footer_Report", h.ReportId) : t.F("Footer_MachineReport", h.ReportId, machineHost);
            return (title, machineHost is null ? h.PlantName : $"{h.PlantName} · {machineHost}", h.ReportId, document, h.Confidentiality,
                machineHost is null ? h.PlantName : $"{h.PlantName} · {machineHost}");
        }, (t, text, family, frame, fallback) =>
            new ReportLayout(data, new ReportLayoutOptions(includeAppendix, machineHost, logo, fallback), t, text, family, frame, _limits));
    }

    internal BuiltDocument BuildSummary(SummaryData d) =>
        Build(d.Language, d.PreTranslated, d.GeneratedAtUtc, d.Tool, t =>
        {
            var title = t.T("SummaryDoc_Title");
            return (title, PdfTexts.Date(d.Date), "summary-" + PdfTexts.Date(d.Date), t.F("Footer_Summary", PdfTexts.Date(d.Date)),
                d.Confidentiality, PdfTexts.Date(d.Date));
        }, (t, text, family, frame, fallback) => new SummaryLayout(d, fallback, t, text, family, frame, _limits));

    internal BuiltDocument BuildTargetConfig(TargetConfigData d) =>
        Build(d.Language, d.PreTranslated, d.GeneratedAtUtc, d.Tool, t =>
        {
            var title = t.T("Target_Title");
            return (title, d.PlantName, "target-config-" + PdfTexts.Date(d.Date), t.F("Footer_Target", PdfTexts.Date(d.Date)), d.Confidentiality, d.PlantName);
        }, (t, text, family, frame, fallback) => new TargetConfigLayout(d, fallback, t, text, family, frame, _limits));

    internal BuiltDocument BuildApprovalSheet(ApprovalSheetData d) =>
        Build(d.Language, d.PreTranslated, d.GeneratedAtUtc, d.Tool, t =>
        {
            var title = t.T("ApprovalDoc_Title");
            return (title, d.PlantName, d.SheetId, t.F("Footer_Approval", d.SheetId), d.Confidentiality, d.PlantName);
        }, (t, text, family, frame, fallback) => new ApprovalSheetLayout(d, fallback, t, text, family, frame, _limits));

    internal BuiltDocument BuildCollectionRecord(CollectionRecordData d) =>
        Build(d.Language, d.PreTranslated, d.GeneratedAtUtc, d.Tool, t =>
        {
            var title = t.T("RecordDoc_Title");
            return (title, d.PlantName, d.Import.Id, t.F("Footer_Record", d.Import.Id), d.Confidentiality, d.PlantName);
        }, (t, text, family, frame, fallback) => new CollectionRecordLayout(d, fallback, t, text, family, frame, _limits));

    private delegate DocumentLayout LayoutFactory(PdfTexts t, PdfText text, string family, PageFrame frame, string? fallbackFrom);

    private BuiltDocument Build(string requestedLanguage, bool preTranslated, DateTimeOffset createdAt, ToolInfo tool,
        Func<PdfTexts, (string Title, string Subject, string Keywords, string Document, string Confidentiality, string HeaderRight)> describe,
        LayoutFactory create)
    {
        var (family, language, fallback) = Fonts(requestedLanguage);
        var t = new PdfTexts(language);
        var (title, subject, keywords, document, confidentiality, headerRight) = describe(t);
        if (string.IsNullOrWhiteSpace(confidentiality)) confidentiality = t.Common("Confidentiality_Default");
        // Vorübersetzung: nach der Sprache, in der das Dokument entsteht (ein Rückfall auf Englisch ist geprüft)
        bool marked = fallback is null && (preTranslated || !UiLanguage.IsReviewedReportLanguage(language));
        var frame = new PageFrame(string.IsNullOrWhiteSpace(tool.Version) ? About.AppInfo.Version : tool.Version, document, confidentiality, title, headerRight, marked);
        using var text = new PdfText(family, t.T("Shortened"));
        var doc = create(t, text, family, frame, fallback).Build(title, subject);
        return new BuiltDocument(doc, family, language, fallback, title, subject, keywords, createdAt);
    }

    /// <summary>
    /// Schrift und Sprache: Chinesisch nur mit einer CJK-Schrift mit TrueType-Konturen, sonst Englisch und RGL-0510
    /// (spec 7.10). Unbekannte Sprachen werden Englisch.
    /// </summary>
    private (string Family, string Language, string? FallbackFrom) Fonts(string requested)
    {
        var language = UiLanguage.Normalize(requested);
        if (language.Length == 0) language = UiLanguage.Fallback;
        var latin = PdfFonts.LatinFamily;
        if (language != "zh-Hans") return (latin, language, null);
        var cjk = _cjkFamily();
        return cjk is null ? (latin, UiLanguage.Fallback, language) : (cjk, language, null);
    }

    // ---- Rendern und speichern -----------------------------------------------------------------------------

    private static void Check(PdfOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        // PDF/A: PDFsharp-MigraDoc 6.2.4 kann aus MigraDoc-Dokumenten kein gültiges PDF/A schreiben (docs/reports.md)
        if (o.PdfA) throw new RiegelException(RglCodesPdf.PdfANotSupported, RglCodesPdf.Message(RglCodesPdf.PdfANotSupported));
    }

    /// <summary>
    /// Namensteil ohne ungültiges UTF-16: FileNames.Token normalisiert (FormC) und wirft bei einzelnen Surrogat-Hälften,
    /// die ein Anlagenname aus fremdem JSON enthalten kann. Gültige Namen bleiben unverändert.
    /// </summary>
    private static string SafeName(string? name) => PdfText.Clean(name);

    private static string Target(PdfOptions o, string fileName)
    {
        try
        {
            Directory.CreateDirectory(o.OutDir);
            return Path.Combine(Path.GetFullPath(o.OutDir), fileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new RiegelException(RglCodesPdf.WriteFailed, RglCodesPdf.Message(RglCodesPdf.WriteFailed, o.OutDir, e.Message), e);
        }
    }

    /// <summary>Rendert das Dokument; erst vollständig, dann schreiben – scheitert das Rendern, bleibt die Zieldatei unberührt.</summary>
    internal static PdfSharp.Pdf.PdfDocument Render(BuiltDocument built)
    {
        lock (RenderGate)
        {
            var renderer = new PdfDocumentRenderer { Document = built.Document };
            renderer.RenderDocument();
            var pdf = renderer.PdfDocument;
            pdf.Info.Title = PdfText.Clean(built.Title);
            pdf.Info.Author = "Riegel";
            pdf.Info.Subject = PdfText.Clean(built.Subject);
            pdf.Info.Keywords = PdfText.Clean(built.Keywords);
            pdf.Info.Creator = $"Riegel {About.AppInfo.Version}";
            pdf.Info.CreationDate = built.CreatedAtUtc.UtcDateTime;
            pdf.Info.ModificationDate = built.CreatedAtUtc.UtcDateTime;
            pdf.Language = built.Language;
            return pdf;
        }
    }

    internal static void Save(BuiltDocument built, string path)
    {
        var pdf = Render(built);
        var dir = Path.GetDirectoryName(path)!;
        var temp = Path.Combine(dir, $"~{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                pdf.Save(stream, false);
                if (stream.CanWrite) stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            throw new RiegelException(RglCodesPdf.WriteFailed, RglCodesPdf.Message(RglCodesPdf.WriteFailed, path, e.Message), e);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
        finally
        {
            pdf.Dispose();
        }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Aufräumen ist best effort */ }
    }
}
