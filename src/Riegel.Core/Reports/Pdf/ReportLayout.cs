// SPDX-License-Identifier: MIT
using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;

namespace Riegel.Core.Reports.Pdf;

/// <summary>Was der Anlagen- bzw. Rechnerbericht zusätzlich zu den Daten braucht.</summary>
/// <param name="MachineHost">Rechnerbericht (O5) für diesen Rechner; null = Anlagenbericht (O4).</param>
/// <param name="FallbackFrom">Angefragte Sprache, wenn das Dokument mangels Schrift auf Englisch entsteht (RGL-0510).</param>
internal sealed record ReportLayoutOptions(bool IncludeAppendix, string? MachineHost, CoverLogo.Checked? Logo, string? FallbackFrom);

/// <summary>
/// Anlagenbericht „zum Abzeichnen“ (O4) und Rechnerbericht (O5) nach spec 5: Deckblatt mit Bannern, Prüfsummen,
/// Vertraulichkeitsvermerk und Einordnung; Inhalt und Lesehilfe; eine Seite Zusammenfassung für die Leitung;
/// „Verschlechtert“ zuerst; offene Befunde je Regel; Register, Zuschnitt, Maßnahmenplan; Rechnerübersicht; Methode und
/// Quellen; Prüfung und Freigabe mit dem Bindungssatz; Anhang A je Rechner.
/// </summary>
internal sealed class ReportLayout(ReportData data, ReportLayoutOptions options, PdfTexts t, PdfText text, string family,
    PageFrame frame, PdfLimits limits) : DocumentLayout(t, text, family, frame, limits)
{
    private readonly ReportHeader _h = data.Header;
    private readonly Dictionary<string, string> _groupNames = data.Machines
        .Where(m => m.GroupId is not null && m.GroupName is not null)
        .GroupBy(m => m.GroupId!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().GroupName!, StringComparer.Ordinal);

    private bool IsMachineReport => options.MachineHost is not null;
    private bool AsDelivered => _h.Preset == ReportPreset.AsDelivered;

    /// <summary>Titel nach Voreinstellung (spec 2.17).</summary>
    public static string Title(PdfTexts t, ReportPreset preset) =>
        t.T(preset == ReportPreset.AsDelivered ? "Report_Title_AsDelivered" : "Report_Title_Audit");

    // Abschnittstitel – die Tests suchen genau diese Texte
    private string SummaryTitle => T.T("Summary_Heading");
    private string ChangesTitle => T.T("Changes_Heading");
    private string FindingsTitle => T.T("Findings_Heading");
    private string VendorTitle => T.T("Vendor_Heading");
    private string ExceptionsTitle => T.T("Exceptions_Heading");
    private string TailoringTitle => T.T("Tailoring_Heading");
    private string MeasuresTitle => T.T("Measures_Heading");
    private string MachinesTitle => T.T("Machines_Heading");
    private string MethodTitle => T.T("Method_Heading");
    private string SignOffTitle => T.T("SignOff_Heading");
    private string AppendixTitle => T.T("Appendix_Heading");

    private bool ShowAppendix => options.IncludeAppendix || IsMachineReport;

    protected override void Compose()
    {
        Plan(SummaryTitle);
        Plan(ChangesTitle);
        Plan(FindingsTitle);
        if (AsDelivered) Plan(VendorTitle);
        Plan(ExceptionsTitle);
        Plan(TailoringTitle);
        Plan(MeasuresTitle);
        Plan(MachinesTitle);
        Plan(MethodTitle);
        Plan(SignOffTitle);
        if (ShowAppendix && data.Machines.Count > 0) Plan(AppendixTitle);

        Cover();
        Section.AddPageBreak();
        TableOfContents();
        Legend();
        ManagementSummary();
        Changes();
        Findings();
        if (AsDelivered) VendorDeviations();
        ExceptionRegister();
        TailoringRegister();
        MeasurePlan();
        MachinesOverview();
        Method();
        SignOff();
        if (ShowAppendix && data.Machines.Count > 0) Appendix();
    }

    // ---- 1. Deckblatt --------------------------------------------------------------------------------------

    private void Cover()
    {
        var label = Section.AddParagraph(T.T("Product_Label"));
        label.Format.SpaceBefore = Unit.FromCentimeter(0.6);
        label.Format.Font.Size = 9;
        label.Format.Font.Bold = true;
        label.Format.Font.Color = RedColor;

        var title = Section.AddParagraph();
        title.Format.Font.Size = 24;
        title.Format.Font.Bold = true;
        title.Format.SpaceAfter = 2;
        Text.Append(title, Title(T, _h.Preset), ContentWidth, 24, bold: true);

        var subtitle = Section.AddParagraph();
        subtitle.Format.Font.Size = 13;
        subtitle.Format.SpaceAfter = 1;
        Text.Append(subtitle, IsMachineReport ? T.F("Report_MachineSubtitle", options.MachineHost, _h.PlantName) : _h.PlantName,
            ContentWidth, 13, bold: true);
        var tagline = Section.AddParagraph();
        tagline.Format.Font.Size = 9;
        tagline.Format.Font.Color = MutedColor;
        tagline.Format.SpaceAfter = 8;
        Text.Append(tagline, T.T("Tagline"), ContentWidth, 9);
        AccentBar();
        Spacer(6);

        FontFallbackNote(options.FallbackFrom);
        Banners(data.Banners);
        Spacer(2);

        var info = KeyValueTable(4.0);
        KeyValue(info, T.T("Cover_Plant"), $"{_h.PlantName} ({_h.PlantId})", bold: true);
        if (!string.IsNullOrWhiteSpace(_h.PlantDescription)) KeyValue(info, T.T("Cover_Description"), _h.PlantDescription);
        KeyValue(info, T.T("Cover_Scope"), T.F("Cover_ScopeValue", T.Number(_h.Hosts.Count), T.Hosts(_h.Hosts, max: 8)));
        KeyValue(info, T.T("Cover_EvaluationDate"), PdfTexts.Date(_h.EvaluationDate), bold: true);
        KeyValue(info, T.T("Cover_Report"), T.F("Cover_ReportValue", _h.ReportId, PdfTexts.DateTime(_h.GeneratedAtUtc)));
        PerformedBy(info);
        KeyValue(info, T.T("Cover_Tool"), ToolLine(_h.Tool));
        CellNote(info, info.Rows[info.Rows.Count - 1], 1, T.F("Cover_ExeSha", T.OrDash(_h.Tool.ExeSha256)));
        KeyValue(info, T.T("Cover_Catalogue"), T.F("Cover_CatalogueValue", _h.Catalogue.Id, _h.Catalogue.Version, PdfTexts.Date(_h.Catalogue.ReleasedOn)));
        CellNote(info, info.Rows[info.Rows.Count - 1], 1, "SHA-256 " + _h.Catalogue.Sha256);
        if (_h.Baselines.Count == 0) KeyValue(info, T.T("Cover_Baselines"), T.T("None"));
        else
        {
            KeyValue(info, T.T("Cover_Baselines"), string.Join("\n", _h.Baselines.Select(b => $"{b.Name} ({b.Id})")));
            foreach (var b in _h.Baselines) CellNote(info, info.Rows[info.Rows.Count - 1], 1, $"{b.Id}: SHA-256 {b.SourceSha256}");
        }
        KeyValue(info, T.T("Cover_Registers"), T.T("Cover_RegistersValue"));
        var registers = info.Rows[info.Rows.Count - 1];
        CellNote(info, registers, 1, $"{T.T("Register_Exceptions")}: {_h.Registers.Exceptions}");
        CellNote(info, registers, 1, $"{T.T("Register_Tailoring")}: {_h.Registers.Tailoring}");
        CellNote(info, registers, 1, $"{T.T("Register_Measures")}: {_h.Registers.Measures}");

        if (AsDelivered) IdentityBlock();

        var confidential = Section.AddParagraph();
        confidential.Format.SpaceBefore = 8;
        confidential.Format.Font.Bold = true;
        confidential.Format.Font.Color = RedColor;
        Text.Append(confidential, _h.Confidentiality, ContentWidth, NormalSize, bold: true);

        Classification();
    }

    /// <summary>„Durchgeführt von“ mit optionalem Logo (geprüft, als Bytes eingebettet).</summary>
    private void PerformedBy(Table info)
    {
        var row = info.AddRow();
        var k = row.Cells[0].AddParagraph();
        k.Format.Font.Color = MutedColor;
        Text.Append(k, T.T("Cover_PerformedBy"), CellWidth(info, 0), TableSize);
        if (options.Logo is { } logo)
        {
            var p = row.Cells[1].AddParagraph();
            p.Format.SpaceAfter = 2;
            var image = p.AddImage(logo.Source);
            image.LockAspectRatio = true;
            // Höchstens 1,6 cm hoch und 6 cm breit
            double heightCm = 1.6, widthCm = heightCm * logo.Width / logo.Height;
            if (widthCm > 6.0)
            {
                image.Width = Unit.FromCentimeter(6.0);
            }
            else image.Height = Unit.FromCentimeter(heightCm);
        }
        var v = row.Cells[1].AddParagraph();
        Text.Append(v, string.IsNullOrWhiteSpace(_h.PerformedBy) ? T.Dash : _h.PerformedBy, CellWidth(info, 1), TableSize, bold: true);
    }

    /// <summary>Maschinenidentität für den Auslieferungszustand (spec 2.17).</summary>
    private void IdentityBlock()
    {
        var machines = data.Machines.Where(m => m.Identity is not null).ToList();
        if (machines.Count == 0) return;
        var head = Section.AddParagraph();
        head.Format.SpaceBefore = 8;
        head.Format.KeepWithNext = true;
        Text.Append(head, T.T("Identity_Heading"), ContentWidth, NormalSize, bold: true, color: RedColor);
        foreach (var m in machines)
        {
            var table = KeyValueTable(4.0);
            KeyValue(table, T.T("Machine_Host"), m.HostName, bold: true);
            IdentityRows(table, m.Identity!);
            KeepStart(table, whole: true);
        }
    }

    private void IdentityRows(Table table, MachineIdentity id)
    {
        KeyValue(table, T.T("Identity_MachineType"), id.MachineType);
        KeyValue(table, T.T("Identity_SerialNumber"), id.SerialNumber);
        KeyValue(table, T.T("Identity_OrderNumber"), id.OrderNumber);
        KeyValue(table, T.T("Identity_DeliveryDate"), T.Date(id.DeliveryDate));
        KeyValue(table, T.T("Identity_PlantSoftware"), id.PlantSoftware);
        KeyValue(table, T.T("Identity_Builder"), id.Builder);
        KeyValue(table, T.T("Identity_Location"), id.Location);
    }

    /// <summary>„Einordnung“ mit Haftungsausschluss (spec 2.16): Nachweise unterstützt, keine Konformitätsaussage.</summary>
    private void Classification()
    {
        var head = Section.AddParagraph();
        head.Format.SpaceBefore = 8;
        head.Format.KeepWithNext = true;
        Text.Append(head, T.T("Classification_Heading"), ContentWidth, NormalSize, bold: true, color: RedColor);
        var intro = Section.AddParagraph();
        intro.Style = SmallStyle;
        intro.Format.KeepWithNext = true;
        Text.Append(intro, T.T("Classification_Intro") + " " + string.Join(" · ", data.Method.Frameworks), ContentWidth, SmallSize);
        var disclaimer = Section.AddParagraph();
        disclaimer.Style = SmallStyle;
        Text.Append(disclaimer, T.T("Classification_Disclaimer"), ContentWidth, SmallSize, bold: true);
    }

    // ---- Inhalt und Lesehilfe ----------------------------------------------------------------------------

    private void Legend()
    {
        var head = Section.AddParagraph();
        head.Format.SpaceBefore = 14;
        head.Format.SpaceAfter = 4;
        head.Format.KeepWithNext = true;
        Text.Append(head, T.T("Legend_Heading"), ContentWidth, 11, bold: true, color: RedColor);
        var table = NewTable(3.4, 10.8, 2.8);
        HeaderRow(table, T.T("Legend_Status"), T.T("Legend_Meaning"), T.T("Legend_Open"));
        foreach (var status in Enum.GetValues<ResultStatus>())
        {
            var row = table.AddRow();
            CellText(table, row, 0, T.Status(status), bold: true);
            CellText(table, row, 1, T.T("Legend_" + status));
            CellText(table, row, 2, status == ResultStatus.Fail ? T.Yes : T.No);
        }
        KeepStart(table, whole: true);
        Intro(T.T("Legend_Flags"), keepWithNext: false);
    }

    // ---- 2. Zusammenfassung für die Leitung (eine Seite) ------------------------------------------------------

    private void ManagementSummary()
    {
        var c = data.Counts;
        Heading1(SummaryTitle, newPage: true);
        Intro(T.F("Summary_Intro", T.Number(c.Machines), T.Number(c.MachinesScored), PdfTexts.Date(_h.EvaluationDate)), keepWithNext: true);

        // Zählungen zuerst (spec 2.6)
        Chips(
        [
            (T.Number(c.OpenHigh), T.T("Count_OpenHigh"), PdfColors.For(Severity.High).Fill, PdfColors.For(Severity.High).Font),
            (T.Number(c.OpenMedium), T.T("Count_OpenMedium"), PdfColors.For(Severity.Medium).Fill, PdfColors.For(Severity.Medium).Font),
            (T.Number(c.OpenLow), T.T("Count_OpenLow"), PdfColors.For(Severity.Low).Fill, PdfColors.For(Severity.Low).Font),
            (T.Number(c.Regressions), T.T("Count_Regressions"), c.Regressions > 0 ? PdfColors.BannerFill : null, c.Regressions > 0 ? PdfColors.Warning : null),
            (T.Number(c.NewlyOpen), T.T("Count_NewlyOpen"), null, null),
        ]);
        Chips(
        [
            (T.Number(c.ExceptionsValid), T.T("Count_ExceptionsValid"), null, null),
            (T.Number(c.ExceptionsExpiring), T.T("Count_ExceptionsExpiring"), c.ExceptionsExpiring > 0 ? PdfColors.BannerFill : null, c.ExceptionsExpiring > 0 ? PdfColors.BannerText : null),
            (T.Number(c.ExceptionsExpired), T.T("Count_ExceptionsExpired"), c.ExceptionsExpired > 0 ? PdfColors.BannerFill : null, c.ExceptionsExpired > 0 ? PdfColors.Warning : null),
            (T.Number(c.ExceptionsPending), T.T("Count_ExceptionsPending"), null, null),
            (T.Number(c.MeasuresOverdue), T.T("Count_MeasuresOverdue"), c.MeasuresOverdue > 0 ? PdfColors.BannerFill : null, c.MeasuresOverdue > 0 ? PdfColors.Warning : null),
            (T.Number(c.NotCheckable), T.T("Count_NotCheckable"), null, null),
        ]);

        ScoreBlock(data.Score);
        var notes = new List<string>();
        if (c.DomainControllers > 0) notes.Add(T.F("Summary_DomainControllers", T.Number(c.DomainControllers)));
        notes.Add(T.F("Summary_UnsupportedStale", T.Number(c.UnsupportedOs), T.Number(c.StaleSnapshots)));
        notes.Add(T.F("Summary_Registers", T.Number(c.TailoringEntries), T.Number(c.MeasuresPlanned)));
        foreach (var note in notes) Paragraph(note, small: true);

        // Werte je Kategorie (unskaliert); der Audit-Deckel gilt nur für die Gesamtwerte
        var categories = data.Categories.Where(x => x.Pass + x.Fail + x.Exception > 0).ToList();
        if (categories.Count > 0)
        {
            Heading2(T.T("Summary_Categories"));
            var table = NewTable(6.6, 2.5, 2.5, 2.5, 2.9);
            HeaderRow(table, T.T("Col_Category"), T.Status(ResultStatus.Pass), T.Status(ResultStatus.Fail), T.Status(ResultStatus.Exception), T.T("Col_Score"));
            foreach (var cat in categories)
            {
                var row = table.AddRow();
                CellText(table, row, 0, T.Category(cat.Category) + (cat.Capped || (cat.Category == RuleCategories.Audit && data.Score.AuditCapped) ? " ¹" : ""));
                CellText(table, row, 1, T.Number(cat.Pass)).Format.Alignment = ParagraphAlignment.Right;
                CellText(table, row, 2, T.Number(cat.Fail)).Format.Alignment = ParagraphAlignment.Right;
                CellText(table, row, 3, T.Number(cat.Exception)).Format.Alignment = ParagraphAlignment.Right;
                CellText(table, row, 4, T.Percent(cat.Score), bold: true, color: data.Score.Greyed ? GreyedColor : null).Format.Alignment = ParagraphAlignment.Right;
            }
            KeepStart(table, whole: true);
            if (data.Score.AuditCapped || categories.Any(x => x.Capped)) Paragraph("¹ " + T.T("Score_AuditCapFootnote"), small: true);
        }

        // Die zehn häufigsten offenen Befunde über alle Rechner
        Heading2(T.T("Summary_TopFindings"));
        if (data.Findings.Count == 0) Paragraph(T.T("Findings_None"));
        else
        {
            var top = NewTable(1.9, 6.2, 1.7, 2.4, 4.8);
            HeaderRow(top, T.T("Col_Rule"), T.T("Col_Title"), T.T("Col_Severity"), T.T("Col_OpenOn"), T.T("Col_Measure"));
            foreach (var f in data.Findings.Take(10))
            {
                var row = top.AddRow();
                CellText(top, row, 0, f.RuleId, bold: true);
                CellText(top, row, 1, f.Title);
                SeverityCell(row.Cells[2], f.Severity);
                CellText(top, row, 3, T.F("OpenOf", T.Number(f.OpenCount), T.Number(f.ApplicableCount)));
                CellText(top, row, 4, MeasureText(f.Measure), color: f.Measure?.Overdue == true ? WarningColor : null);
            }
            KeepStart(top, whole: true);
            if (data.Findings.Count > 10) Paragraph(T.F("Summary_MoreFindings", T.Number(data.Findings.Count - 10)), small: true);
        }
    }

    /// <summary>Härtungsgrad, Dokumentiert, Prüfumfang; grau mit „nur X % prüfbar“, wenn unvollständig (spec 2.4 Punkt 9).</summary>
    private void ScoreBlock(ScoreView score)
    {
        var table = NewTable(framed: false, 5.6, 0.1, 5.6, 0.1, 5.6);
        table.Borders.Visible = false;
        table.TopPadding = 3;
        table.BottomPadding = 3;
        var row = table.AddRow();
        var color = score.Greyed ? GreyedColor : TextColor;
        void Cell(int i, string value, string label, Color valueColor)
        {
            var cell = row.Cells[i];
            cell.Shading.Color = PdfColors.Rgb(PdfColors.HeaderLight);
            var v = cell.AddParagraph();
            v.Format.Font.Size = 17;
            v.Format.Font.Bold = true;
            v.Format.Font.Color = valueColor;
            v.Format.Alignment = ParagraphAlignment.Center;
            v.AddText(PdfText.Clean(value));
            var l = cell.AddParagraph();
            l.Format.Font.Size = SmallSize;
            l.Format.Alignment = ParagraphAlignment.Center;
            Text.Append(l, label, CellWidth(table, i), SmallSize);
        }
        Cell(0, score.Score is null ? T.T("NotRated") : T.Percent(score.Score), T.T("Score_Hardening"), color);
        Cell(2, score.Documented is null ? T.T("NotRated") : T.Percent(score.Documented), T.T("Score_Documented"), color);
        Cell(4, T.Percent(score.CheckedShare), T.T("Score_CheckedShare"), score.Greyed ? WarningColor : TextColor);
        if (score.Greyed) Paragraph(T.F("Score_Greyed", T.Percent(score.CheckedShare)), small: true, color: WarningColor, bold: true);
        Paragraph(T.T("Score_Explained"), small: true);
    }

    private string MeasureText(MeasureSummary? m) => m is null ? T.Dash
        : m.Overdue ? T.F("Measure_Overdue", m.MeasureId, PdfTexts.Date(m.DueDate), m.OwnerRole)
        : T.F("Measure_Planned", m.MeasureId, PdfTexts.Date(m.DueDate), m.OwnerRole);

    // ---- 3. Verschlechtert --------------------------------------------------------------------------------

    private void Changes()
    {
        Heading1(ChangesTitle, newPage: true);
        Intro(T.T("Changes_Intro"));
        var worse = data.Changes.Where(c => c.Change.Kind == StatusChangeKind.Worse).ToList();
        if (worse.Count == 0) Paragraph(T.T("Changes_None"));
        else ChangeTable(worse);

        Heading2(T.T("Expired_Heading"));
        if (data.ExpiredExceptions.Count == 0) Paragraph(T.T("Expired_None"));
        else
        {
            var table = NewTable(1.8, 6.0, 1.7, 2.4, 5.1);
            HeaderRow(table, T.T("Col_Entry"), T.T("Col_Rule"), T.T("Col_Severity"), T.T("Col_ExpiredOn"), T.T("Col_Hosts"));
            foreach (var e in data.ExpiredExceptions)
            {
                var row = table.AddRow();
                CellText(table, row, 0, e.Entry.Id, bold: true);
                CellText(table, row, 1, $"{e.Entry.RuleId} {e.RuleTitle}");
                SeverityCell(row.Cells[2], e.Severity);
                CellText(table, row, 3, T.Date(e.Entry.Approval.ReviewBy), color: WarningColor);
                CellText(table, row, 4, T.Hosts(e.AffectedHosts));
            }
            KeepStart(table);
        }

        var other = data.Changes.Where(c => c.Change.Kind is not StatusChangeKind.Worse and not StatusChangeKind.ExceptionExpired).ToList();
        if (other.Count > 0)
        {
            Heading2(T.T("Changes_Other"));
            ChangeTable(other, kind: true);
        }
    }

    private void ChangeTable(IReadOnlyList<HostChange> changes, bool kind = false)
    {
        var table = kind ? NewTable(2.5, 5.2, 1.6, 2.4, 2.6, 2.7) : NewTable(2.5, 6.4, 1.6, 3.6, 2.9);
        if (kind) HeaderRow(table, T.T("Col_Host"), T.T("Col_Rule"), T.T("Col_Severity"), T.T("Col_Change"), T.T("Col_FromTo"), T.T("Col_Basis"));
        else HeaderRow(table, T.T("Col_Host"), T.T("Col_Rule"), T.T("Col_Severity"), T.T("Col_FromTo"), T.T("Col_Basis"));
        foreach (var c in changes.Take(Limits.Changes))
        {
            var row = table.AddRow();
            int i = 0;
            CellText(table, row, i++, c.HostName, bold: true);
            CellText(table, row, i++, $"{c.Change.RuleId} {c.Title}");
            SeverityCell(row.Cells[i++], c.Severity);
            if (kind) CellText(table, row, i++, T.Change(c.Change.Kind));
            CellText(table, row, i++, FromTo(c.Change), color: c.Change.Kind == StatusChangeKind.Worse ? WarningColor : null);
            CellText(table, row, i, T.Basis(c.Basis));
            CellNote(table, row, i, c.FromRunId);
        }
        KeepStart(table);
        Truncated(changes.Count, Limits.Changes);
    }

    private string FromTo(StatusChange c) =>
        $"{(c.From is { } f ? T.Status(f) : T.T("Status_None"))} → {(c.To is { } to ? T.Status(to) : T.T("Status_None"))}";

    // ---- 4. Offene Befunde je Regel ----------------------------------------------------------------------

    private void Findings()
    {
        Heading1(FindingsTitle);
        Intro(T.T(IsMachineReport ? "Findings_IntroMachine" : "Findings_Intro"));
        if (data.Findings.Count == 0)
        {
            Paragraph(T.T("Findings_None"));
            return;
        }
        var table = NewTable(5.0, 1.6, 1.8, 4.0, 4.6);
        HeaderRow(table, T.T("Col_Rule"), T.T("Col_Severity"), T.T("Col_OpenOn"), T.T("Col_Hosts"), T.T("Col_MeasureNotes"));
        foreach (var f in data.Findings.Take(Limits.Findings))
        {
            var row = table.AddRow();
            CellText(table, row, 0, f.RuleId, bold: true);
            CellNote(table, row, 0, f.Title, TextColor);
            if (f.RuleSetId != "riegel") CellNote(table, row, 0, T.F("Finding_RuleSet", f.RuleSetId));
            SeverityCell(row.Cells[1], f.Severity);
            CellText(table, row, 2, T.F("OpenOf", T.Number(f.OpenCount), T.Number(f.ApplicableCount)));
            CellText(table, row, 3, T.Hosts(f.Hosts, f.MoreHosts));
            CellText(table, row, 4, MeasureText(f.Measure), color: f.Measure?.Overdue == true ? WarningColor : null);
            var flags = T.Flags(f.Flags);
            CellNote(table, row, 4, flags.Length == 0 ? null : flags);
        }
        KeepStart(table);
        Truncated(data.Findings.Count, Limits.Findings);
    }

    // ---- Abweichungen der Maschinensoftware (asDelivered) -----------------------------------------------------

    /// <summary>Rollen, unter denen ein Hersteller Ausnahmen anlegt (spec 2.11 „Hersteller“), in allen fünf Sprachen.</summary>
    internal static bool IsBuilderRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        foreach (var lang in Localization.UiLanguage.Supported)
            if (string.Equals(role.Trim(), Resources.PdfStrings.ResourceManager.GetString("Role_Builder", CultureInfo.GetCultureInfo(lang)), StringComparison.OrdinalIgnoreCase))
                return true;
        return string.Equals(role.Trim(), "Hersteller", StringComparison.OrdinalIgnoreCase);
    }

    private void VendorDeviations()
    {
        Heading1(VendorTitle);
        Intro(T.T("Vendor_Intro"));
        var entries = data.Exceptions.Concat(data.ExpiredExceptions).Where(e => IsBuilderRole(e.Entry.CreatedByRole))
            .OrderBy(e => e.Entry.Id, StringComparer.Ordinal).ToList();
        if (entries.Count == 0)
        {
            Paragraph(T.T("Vendor_None"));
            return;
        }
        var table = NewTable(1.8, 4.6, 6.4, 4.2);
        HeaderRow(table, T.T("Col_Entry"), T.T("Col_Rule"), T.T("Col_Justification"), T.T("Col_State"));
        foreach (var e in entries)
        {
            var row = table.AddRow();
            CellText(table, row, 0, e.Entry.Id, bold: true);
            CellText(table, row, 1, $"{e.Entry.RuleId} {e.RuleTitle}");
            CellText(table, row, 2, e.Entry.Justification);
            CellNote(table, row, 2, e.Entry.Reference is null ? null : T.F("Exc_Reference", e.Entry.Reference));
            CellText(table, row, 3, T.State(e.State), bold: true, color: StateColor(e.State));
        }
        KeepStart(table);
    }

    // ---- 5. Ausnahmeregister ------------------------------------------------------------------------------

    private Color? StateColor(RegisterEntryState state) => state switch
    {
        RegisterEntryState.Valid => GoodColor,
        RegisterEntryState.Expired or RegisterEntryState.Invalid => WarningColor,
        _ => PdfColors.Rgb(PdfColors.BannerBorder),
    };

    private string StateSummary<TView>(IReadOnlyList<TView> views, Func<TView, RegisterEntryState> state) =>
        string.Join(" · ", Enum.GetValues<RegisterEntryState>()
            .Select(s => (s, n: views.Count(v => state(v) == s)))
            .Where(x => x.n > 0)
            .Select(x => $"{T.State(x.s)}: {T.Number(x.n)}"));

    private void ExceptionRegister()
    {
        Heading1(ExceptionsTitle);
        var all = data.Exceptions.Concat(data.ExpiredExceptions)
            .GroupBy(e => e.Entry.Id, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(e => e.Entry.Id, StringComparer.Ordinal).ToList();
        Intro(T.F("Exceptions_Intro", PdfTexts.Sha8(_h.Registers.Exceptions), T.Number(all.Count),
            all.Count == 0 ? T.Dash : StateSummary(all, v => v.State)));
        if (all.Count == 0)
        {
            Paragraph(T.T("Exceptions_None"));
            return;
        }
        var table = NewTable(4.1, 2.5, 4.6, 5.8);
        HeaderRow(table, T.T("Col_Entry"), T.T("Col_State"), T.T("Col_ScopeApproval"), T.T("Col_Justification"));
        foreach (var e in all.Take(Limits.RegisterRows))
        {
            var x = e.Entry;
            var row = table.AddRow();
            CellText(table, row, 0, x.Id, bold: true);
            CellNote(table, row, 0, $"{x.RuleId} {e.RuleTitle}", TextColor);
            CellNote(table, row, 0, T.F("Exc_Severity", T.Severity(e.Severity)));
            CellText(table, row, 1, T.State(e.State), bold: true, color: StateColor(e.State));
            var flags = T.RegisterFlags(e.Flags);
            CellNote(table, row, 1, flags.Length == 0 ? null : flags, e.Flags == RegisterFlags.None ? null : WarningColor);
            CellText(table, row, 2, T.Scope(x.Scope, _groupNames));
            CellNote(table, row, 2, T.F("Exc_Category", T.Reason(x.Category)));
            CellNote(table, row, 2, ApprovalText(x.Approval));
            CellText(table, row, 3, x.Justification);
            if (x.CompensatingMeasure is { Length: > 0 } cm) CellNote(table, row, 3, T.F("Exc_Compensating", cm));
            if (x.Reference is { Length: > 0 } reference) CellNote(table, row, 3, T.F("Exc_Reference", reference));
            if (x.AcceptedValue is { Length: > 0 } accepted) CellNote(table, row, 3, T.F("Exc_AcceptedValue", accepted));
            CellNote(table, row, 3, T.F("Exc_Hosts", T.Hosts(e.AffectedHosts)));
            CellNote(table, row, 3, T.F("Exc_ContentHash", PdfTexts.Sha8(e.ContentSha256)));
        }
        KeepStart(table);
        Truncated(all.Count, Limits.RegisterRows);
    }

    private string ApprovalText(Approval a)
    {
        if (a.ApprovedOn is null && a.ApprovedByRole is null && a.ApprovalRecord is null) return T.T("Approval_None");
        var who = string.IsNullOrWhiteSpace(a.ApprovedByName) ? T.OrDash(a.ApprovedByRole) : $"{T.OrDash(a.ApprovedByRole)} ({a.ApprovedByName})";
        return T.F("Approval_Text", who, T.Date(a.ApprovedOn), T.OrDash(a.ApprovalRecord), T.Date(a.ReviewBy));
    }

    // ---- 6. Zuschnitt -------------------------------------------------------------------------------------

    private void TailoringRegister()
    {
        Heading1(TailoringTitle);
        var all = data.Tailoring.OrderBy(e => e.Entry.Id, StringComparer.Ordinal).ToList();
        Intro(T.F("Tailoring_Intro", PdfTexts.Sha8(_h.Registers.Tailoring), T.Number(all.Count),
            all.Count == 0 ? T.Dash : StateSummary(all, v => v.State)));
        if (all.Count == 0)
        {
            Paragraph(T.T("Tailoring_None"));
            return;
        }
        var table = NewTable(4.1, 2.5, 4.6, 5.8);
        HeaderRow(table, T.T("Col_Entry"), T.T("Col_State"), T.T("Col_ScopeApproval"), T.T("Col_Reason"));
        foreach (var e in all.Take(Limits.RegisterRows))
        {
            var x = e.Entry;
            var row = table.AddRow();
            CellText(table, row, 0, x.Id, bold: true);
            CellNote(table, row, 0, $"{x.RuleId} {e.RuleTitle}", TextColor);
            CellNote(table, row, 0, T.F("Exc_Severity", T.Severity(e.Severity)));
            CellText(table, row, 1, T.State(e.State), bold: true, color: StateColor(e.State));
            var flags = T.RegisterFlags(e.Flags);
            CellNote(table, row, 1, flags.Length == 0 ? null : flags, WarningColor);
            CellText(table, row, 2, T.Scope(x.Scope, _groupNames));
            CellNote(table, row, 2, ApprovalText(x.Approval));
            CellText(table, row, 3, x.Reason);
            if (x.Comment is { Length: > 0 } comment) CellNote(table, row, 3, comment);
            CellNote(table, row, 3, T.F("Exc_Hosts", T.Hosts(e.AffectedHosts)));
            CellNote(table, row, 3, T.F("Exc_ContentHash", PdfTexts.Sha8(e.ContentSha256)));
        }
        KeepStart(table);
        Truncated(all.Count, Limits.RegisterRows);
    }

    // ---- 7. Maßnahmenplan ---------------------------------------------------------------------------------

    private void MeasurePlan()
    {
        Heading1(MeasuresTitle);
        var all = data.Measures.OrderBy(m => m.Entry.Id, StringComparer.Ordinal).ToList();
        Intro(T.F("Measures_Intro", PdfTexts.Sha8(_h.Registers.Measures), T.Number(all.Count), T.Number(all.Count(m => m.Overdue))));
        if (all.Count == 0)
        {
            Paragraph(T.T("Measures_None"));
            return;
        }
        var table = NewTable(4.3, 4.4, 3.6, 2.0, 2.7);
        HeaderRow(table, T.T("Col_Entry"), T.T("Col_Action"), T.T("Col_Owner"), T.T("Col_Due"), T.T("Col_State"));
        foreach (var m in all.Take(Limits.RegisterRows))
        {
            var x = m.Entry;
            var row = table.AddRow();
            CellText(table, row, 0, x.Id, bold: true);
            CellNote(table, row, 0, $"{x.RuleId} {m.RuleTitle}", TextColor);
            CellNote(table, row, 0, T.F("Exc_Severity", T.Severity(m.Severity)));
            CellText(table, row, 1, x.Action);
            if (x.Ticket is { Length: > 0 } ticket) CellNote(table, row, 1, T.F("Measure_Ticket", ticket));
            CellNote(table, row, 1, T.F("Exc_Hosts", T.Hosts(m.AffectedHosts)));
            CellText(table, row, 2, x.OwnerRole);
            CellText(table, row, 3, PdfTexts.Date(x.DueDate), bold: m.Overdue, color: m.Overdue ? WarningColor : null);
            if (m.Overdue) CellNote(table, row, 3, T.T("Measure_OverdueLabel"), WarningColor);
            CellText(table, row, 4, T.T("MeasureState_" + x.State), bold: true);
            var flags = T.RegisterFlags(m.Flags);
            CellNote(table, row, 4, flags.Length == 0 ? null : flags, WarningColor);
            CellNote(table, row, 4, T.F("Measure_OpenOn", T.Number(m.OpenCount)));
            if (x.Comment is { Length: > 0 } comment) CellNote(table, row, 4, comment);
        }
        KeepStart(table);
        Truncated(all.Count, Limits.RegisterRows);
    }

    // ---- 8. Rechnerübersicht ------------------------------------------------------------------------------

    private void MachinesOverview()
    {
        Heading1(MachinesTitle);
        Intro(T.T("Machines_Intro"));
        var table = NewTable(3.5, 3.5, 2.7, 2.2, 2.7, 2.4);
        HeaderRow(table, T.T("Col_Host"), T.T("Col_Os"), T.T("Col_Collected"), T.T("Col_OpenHml"), T.T("Col_Scores"), T.T("Col_Notes"));
        foreach (var m in data.Machines.Take(Limits.Machines))
        {
            var row = table.AddRow();
            CellText(table, row, 0, m.HostName, bold: true);
            CellNote(table, row, 0, m.Label, TextColor);
            if (m.GroupName is not null || m.Role is not null)
                CellNote(table, row, 0, string.Join(" · ", new[] { m.GroupName, m.Role is { } r ? T.Role(r) : null }.Where(s => s is not null)));
            CellText(table, row, 1, m.Lifecycle?.ProductName ?? m.Machine.EditionId);
            CellNote(table, row, 1, Build(m.Machine));
            if (m.Lifecycle is { } life) CellNote(table, row, 1, SupportText(life), life.Supported == false ? WarningColor : null);
            CellText(table, row, 2, PdfTexts.Date(DateOnly.FromDateTime(m.Collection.CollectedAtUtc.UtcDateTime)));
            CellNote(table, row, 2, T.F("Age_Days", T.Number(m.Collection.AgeDays)), (m.Flags & RunFlags.StaleSnapshot) != 0 ? WarningColor : null);
            CellNote(table, row, 2, T.F("Admin_Value", T.YesNo(m.Collection.IsAdmin)), m.Collection.IsAdmin ? null : WarningColor);
            CellText(table, row, 3, $"{T.Number(m.Summary.OpenHigh)} / {T.Number(m.Summary.OpenMedium)} / {T.Number(m.Summary.OpenLow)}", bold: m.Summary.OpenHigh > 0);
            if (m.IsDomainController)
            {
                CellText(table, row, 4, T.T("NotRated"), bold: true);
                CellNote(table, row, 4, T.Common("RunFlag_DomainController"));
            }
            else
            {
                bool greyed = (m.Flags & RunFlags.Incomplete) != 0 || m.Summary.CheckedShare < 90 || !m.Collection.IsAdmin;
                CellText(table, row, 4, T.F("Scores_Value", T.Percent(m.Summary.Score), T.Percent(m.Summary.Documented)), bold: true,
                    color: greyed ? GreyedColor : null);
                CellNote(table, row, 4, T.F("Scores_Share", T.Percent(m.Summary.CheckedShare)), greyed ? WarningColor : null);
            }
            var flags = T.RunFlags(m.Flags & ~RunFlags.DomainController);
            CellText(table, row, 5, flags.Length == 0 ? T.Dash : flags, color: flags.Length == 0 ? null : WarningColor);
        }
        KeepStart(table);
        Truncated(data.Machines.Count, Limits.Machines);
        Paragraph(T.T("Machines_Legend"), small: true);

        if (data.Baselines.Count > 0)
        {
            Heading2(T.T("Baselines_Heading"));
            Intro(T.T("Baselines_Intro"));
            var b = NewTable(4.6, 1.6, 1.8, 2.3, 1.9, 1.6, 3.2);
            HeaderRow(b, light: false, 7.0, T.T("Col_Baseline"), T.Status(ResultStatus.Pass), T.Status(ResultStatus.Fail), T.Status(ResultStatus.Exception),
                T.Status(ResultStatus.NotCheckable), T.T("Col_Score"), T.T("Col_Content"));
            foreach (var s in data.Baselines)
            {
                var row = b.AddRow();
                CellText(b, row, 0, s.Baseline.Name, bold: true);
                CellNote(b, row, 0, $"{s.Baseline.Id} · {T.F("Baseline_Rules", T.Number(s.Baseline.RuleCount))}");
                CellNote(b, row, 0, "SHA-256 " + PdfTexts.Sha8(s.Baseline.SourceSha256) + "…");
                CellText(b, row, 1, T.Number(s.Pass));
                CellText(b, row, 2, T.Number(s.Fail));
                CellText(b, row, 3, T.Number(s.Exception));
                CellText(b, row, 4, T.Number(s.NotCheckable));
                CellText(b, row, 5, T.Percent(s.Score), bold: true);
                CellText(b, row, 6, s.ContentIncluded ? T.T("Baseline_ContentIncluded") : T.T("Baseline_MetadataOnly"));
            }
            KeepStart(b, whole: true);
        }
    }

    private string Build(MachineInfo m)
    {
        var parts = new List<string>();
        if (m.Build is { } build) parts.Add(T.F("Os_Build", m.Ubr is { } ubr ? $"{build}.{ubr}" : build.ToString(CultureInfo.InvariantCulture)));
        if (m.DisplayVersion is { Length: > 0 } dv) parts.Add(dv);
        if (m.EditionId is { Length: > 0 } ed) parts.Add(ed);
        if (m.Architecture is { Length: > 0 } arch) parts.Add(arch);
        return string.Join(" · ", parts);
    }

    private string SupportText(LifecycleView life) => life.Supported switch
    {
        false => T.F("Support_Ended", T.Date(life.EndOfSupport)) + Esu(life),
        true => T.F("Support_Until", T.Date(life.EndOfSupport)) + Esu(life),
        null => T.T("Support_Unknown") + Esu(life),
    };

    private string Esu(LifecycleView life) => !life.EsuCovered ? ""
        : life.EsuEnd is { } end ? " · " + T.F("Support_Esu", PdfTexts.Date(end)) : " · " + T.T("Support_EsuCovered");

    // ---- 9. Methode und Quellen ---------------------------------------------------------------------------

    private void Method()
    {
        var m = data.Method;
        Heading1(MethodTitle);
        Heading2(T.T("Method_Collection_Heading"));
        Paragraph(T.T("Method_Collection"));
        Heading2(T.T("Method_Evidence_Heading"));
        foreach (var key in new[] { "Method_Evidence_1", "Method_Evidence_2", "Method_Evidence_3", "Method_Evidence_4", "Method_Evidence_5", "Method_Evidence_6" })
            Bullet(T.T(key));
        Heading2(T.T("Method_Score_Heading"));
        Paragraph(T.T("Method_Score"));
        Heading2(T.T("Method_Catalogue_Heading"));
        Paragraph(T.F("Method_Catalogue", _h.Catalogue.Id, _h.Catalogue.Version, PdfTexts.Date(_h.Catalogue.ReleasedOn),
            T.Number(m.RulesTotal), T.Number(m.CheckRules), T.Number(m.InventoryRules)));
        Paragraph("SHA-256 " + _h.Catalogue.Sha256, small: true);
        var p = m.Provenance;
        Paragraph(T.F("Method_Provenance", T.Number(p.Total), T.Number(p.Acsc), T.Number(p.MsDocs), T.Number(p.MsLearnFacts),
            T.Number(p.Disa), T.Number(p.AlsoInMsBaseline)));

        Heading2(T.T("Method_Sources_Heading"));
        if (m.Sources.Count > 0)
        {
            var table = NewTable(5.2, 3.0, 8.8);
            HeaderRow(table, T.T("Col_Source"), T.T("Col_Licence"), T.T("Col_Attribution"));
            foreach (var s in m.Sources)
            {
                var row = table.AddRow();
                CellText(table, row, 0, s.Name, bold: true);
                CellNote(table, row, 0, s.Src);
                CellText(table, row, 1, s.Licence);
                CellText(table, row, 2, s.Attribution);
                CellNote(table, row, 2, s.Url);
            }
            KeepStart(table);
        }
        Paragraph(T.T("Method_NoCisBsi"), bold: true).Format.SpaceBefore = 6;

        Heading2(T.T("Method_Integrity_Heading"));
        Paragraph(T.Common("Integrity_Statement"));

        Heading2(T.T("Method_Limits_Heading"));
        foreach (var key in new[] { "Method_Limit_1", "Method_Limit_2", "Method_Limit_3", "Method_Limit_4" })
            Bullet(T.T(key));
        if (m.ExperimentalGpoAttribution) Bullet(T.T("Method_Limit_Gpo"));
        if (data.Counts.DomainControllers > 0) Bullet(T.T("Method_Limit_Dc"));
    }

    // ---- 10. Prüfung und Freigabe -------------------------------------------------------------------------

    /// <summary>Rollen je Voreinstellung (spec 5 O4 Abschnitt 10, 2.17).</summary>
    internal static IReadOnlyList<SignOffRole> Roles(PdfTexts t, ReportPreset preset, IReadOnlyList<SignOffRole> configured)
    {
        if (preset == ReportPreset.AsDelivered)
        {
            // Hersteller erstellt / Hersteller geprüft / Betreiber übernommen – eigene Rollen nur, wenn sie „accepted“ enthalten
            if (configured.Any(r => r.Step == SignOffSteps.Accepted)) return configured;
            return
            [
                new(SignOffSteps.Created, t.T("Role_Builder")),
                new(SignOffSteps.Reviewed, t.T("Role_Builder")),
                new(SignOffSteps.Accepted, t.T("Role_Operator")),
            ];
        }
        if (configured.Count > 0) return configured;
        return
        [
            new(SignOffSteps.Created, t.Common("Role_PlantOwner")),
            new(SignOffSteps.Reviewed, t.Common("Role_SecurityOfficer")),
            new(SignOffSteps.Approved, t.Common("Role_PlantManagement")),
        ];
    }

    private void SignOff()
    {
        var s = data.SignOff;
        Heading1(SignOffTitle, newPage: true);
        Paragraph(T.T(AsDelivered ? "SignOff_IntroAsDelivered" : "SignOff_Intro")).Format.KeepWithNext = true;

        // Bindungssatz (spec 2.11): die Freigabe gilt genau diesen Registerständen
        var binding = Section.AddParagraph();
        binding.Format.SpaceBefore = 4;
        binding.Format.KeepWithNext = true;
        Text.Append(binding, T.F("SignOff_Binding", PdfTexts.Sha8(s.ExceptionsSha256), T.Number(s.ExceptionCount),
            T.Number(s.ExceptionValidCount), PdfTexts.Sha8(s.TailoringSha256), T.Number(s.TailoringCount)), ContentWidth, NormalSize, bold: true);
        var measures = Section.AddParagraph();
        measures.Format.KeepWithNext = true;
        Text.Append(measures, T.F("SignOff_Measures", PdfTexts.Sha8(s.MeasuresSha256), T.Number(s.MeasureCount)), ContentWidth, NormalSize);

        // Bezug: das unterschriebene Blatt bleibt zuordenbar, auch wenn es getrennt abgelegt wird
        var reference = Section.AddParagraph();
        reference.Style = IntroStyle;
        reference.Format.SpaceAfter = 6;
        Text.Append(reference, T.F("SignOff_Reference", s.ReportId, string.Join(", ", s.RunIds)) + " ", ContentWidth, SmallSize);
        reference.AddText(PdfText.Clean(T.T("SignOff_PageCount") + " "));
        reference.AddNumPagesField();

        var roles = Roles(T, _h.Preset, s.Roles);
        SignatureBlocks([.. roles.Select(r => (T.Step(r.Step), (string?)r.Role))]);
    }

    // ---- Anhang A ----------------------------------------------------------------------------------------

    private void Appendix()
    {
        Heading1(AppendixTitle, newPage: true);
        Intro(T.T("Appendix_Intro"), keepWithNext: true);
        for (int i = 0; i < data.Machines.Count; i++)
        {
            if (i > 0) Section.AddPageBreak();
            Machine(data.Machines[i], i + 1);
        }
    }

    private Paragraph SubHeading(string title)
    {
        var p = Section.AddParagraph();
        p.Format.SpaceBefore = 8;
        p.Format.SpaceAfter = 2;
        p.Format.KeepWithNext = true;
        Text.Append(p, title, ContentWidth, NormalSize, bold: true, color: RedColor);
        return p;
    }

    private void Machine(MachineReport m, int number)
    {
        Heading2(m.Label is { Length: > 0 } label ? T.F("Appendix_MachineLabel", number, m.HostName, label) : T.F("Appendix_Machine", number, m.HostName));
        var info = KeyValueTable(4.0);
        KeyValue(info, T.T("Machine_Host"), m.HostName, bold: true);
        if (m.Label is not null) KeyValue(info, T.T("Machine_Label"), m.Label);
        if (m.GroupName is not null || m.Role is not null)
            KeyValue(info, T.T("Machine_Group"), string.Join(" · ", new[] { m.GroupName, m.Role is { } r ? T.Role(r) : null }.Where(x => x is not null)));
        if (m.Identity is { } id) IdentityRows(info, id);
        KeyValue(info, T.T("Machine_Os"), m.Lifecycle?.ProductName ?? T.OrDash(m.Machine.EditionId));
        CellNote(info, info.Rows[info.Rows.Count - 1], 1, Build(m.Machine));
        if (m.Lifecycle is { } life) KeyValue(info, T.T("Machine_Support"), SupportText(life), color: life.Supported == false ? WarningColor : null);
        KeyValue(info, T.T("Machine_Domain"), m.Machine.DnsDomain is { Length: > 0 } dns ? $"{T.DomainRole(m.Machine.DomainRole)} · {dns}" : T.DomainRole(m.Machine.DomainRole));
        var c = m.Collection;
        // Veraltet (spec 2.8): „Stand der Erfassung: … (vor N Tagen)“ – N liegt dann über staleDays, also nie 1
        bool stale = (m.Flags & RunFlags.StaleSnapshot) != 0;
        KeyValue(info, T.T("Machine_Collected"), T.F(stale ? "Machine_CollectedStale" : "Machine_CollectedValue", PdfTexts.DateTime(c.CollectedAtUtc),
            T.Number(c.AgeDays)), bold: true, color: stale ? WarningColor : null);
        KeyValue(info, T.T("Machine_Imported"), T.DateTime(c.ImportedAtUtc));
        KeyValue(info, T.T("Machine_Admin"), T.YesNo(c.IsAdmin), color: c.IsAdmin ? null : WarningColor);
        KeyValue(info, T.T("Machine_Options"), T.Options(c.Options));
        KeyValue(info, T.T("Machine_Origin"), T.Origin(c.Origin));
        KeyValue(info, T.T("Machine_Collector"), $"{c.CollectorName} {c.CollectorVersion}");
        if (c.CollectorExeSha256 is { Length: > 0 } exe) CellNote(info, info.Rows[info.Rows.Count - 1], 1, "SHA-256 " + exe);
        KeyValue(info, T.T("Machine_SnapshotSha"), c.SnapshotSha256, size: SmallSize);
        if (c.ImportId is not null) KeyValue(info, T.T("Machine_ImportId"), c.ImportId);
        KeyValue(info, T.T("Machine_RunId"), m.RunId);
        if (m.IsDomainController) KeyValue(info, T.T("Col_Scores"), T.T("NotRatedDc"), bold: true);
        else
        {
            bool greyed = (m.Flags & RunFlags.Incomplete) != 0 || m.Summary.CheckedShare < 90 || !c.IsAdmin;
            KeyValue(info, T.T("Col_Scores"), T.F("Machine_ScoresValue", T.Percent(m.Summary.Score), T.Percent(m.Summary.Documented),
                T.Percent(m.Summary.CheckedShare)), bold: true, color: greyed ? GreyedColor : null);
        }
        KeyValue(info, T.T("Machine_Counts"), T.F("Machine_CountsValue", T.Number(m.Summary.OpenHigh), T.Number(m.Summary.OpenMedium),
            T.Number(m.Summary.OpenLow), T.Number(m.Summary.Exception), T.Number(m.Summary.NotCheckable)));
        var flags = T.RunFlags(m.Flags);
        if (flags.Length > 0) KeyValue(info, T.T("Col_Notes"), flags, color: WarningColor);

        var results = m.Results.Take(Limits.ResultsPerMachine).ToList();

        // „Verschlechtert“ zuerst
        SubHeading(T.T("Machine_Worse"));
        if (m.Worse.Count == 0) Paragraph(T.T("Changes_None"), small: true);
        else ChangeTable(m.Worse);

        var open = results.Where(r => r.Result.Status == ResultStatus.Fail)
            .OrderByDescending(r => r.Result.Severity).ThenBy(r => r.Result.RuleId, StringComparer.Ordinal).ToList();
        SubHeading(T.F("Machine_Open", T.Number(open.Count)));
        if (open.Count == 0) Paragraph(T.T("Findings_None"), small: true);
        else
        {
            var table = NewTable(4.2, 1.5, 2.6, 2.6, 2.4, 3.7);
            HeaderRow(table, T.T("Col_Rule"), T.T("Col_Severity"), T.T("Col_Expected"), T.T("Col_Actual"), T.T("Col_Source"), T.T("Col_Notes"));
            foreach (var r in open)
            {
                var row = table.AddRow();
                ResultRuleCell(table, row, r);
                SeverityCell(row.Cells[1], r.Result.Severity);
                CellText(table, row, 2, r.Result.Expected);
                CellText(table, row, 3, r.Result.Actual ?? T.Common("Value_NotSet"));
                CellText(table, row, 4, T.Source(r.Result.Source));
                CellNote(table, row, 4, r.Result.SourceDetail);
                if (r.Result.Source == ValueSource.DomainGpo) CellNote(table, row, 4, T.T("Source_DomainGpoExperimental"), WarningColor);
                ResultNotes(table, row, 5, r);
            }
            KeepStart(table);
        }

        var exceptions = results.Where(r => r.Result.Status == ResultStatus.Exception).ToList();
        if (exceptions.Count > 0)
        {
            SubHeading(T.F("Machine_Exceptions", T.Number(exceptions.Count)));
            var table = NewTable(5.0, 1.5, 2.4, 2.4, 5.7);
            HeaderRow(table, T.T("Col_Rule"), T.T("Col_Severity"), T.T("Col_Expected"), T.T("Col_Actual"), T.T("Col_Notes"));
            foreach (var r in exceptions)
            {
                var row = table.AddRow();
                ResultRuleCell(table, row, r);
                SeverityCell(row.Cells[1], r.Result.Severity);
                CellText(table, row, 2, r.Result.Expected);
                CellText(table, row, 3, r.Result.Actual ?? T.Common("Value_NotSet"));
                ResultNotes(table, row, 4, r);
            }
            KeepStart(table);
        }

        var tailored = results.Where(r => r.Result.Status == ResultStatus.OutOfScope).ToList();
        if (tailored.Count > 0)
        {
            SubHeading(T.F("Machine_Tailored", T.Number(tailored.Count)));
            var table = NewTable(7.0, 1.5, 8.5);
            HeaderRow(table, T.T("Col_Rule"), T.T("Col_Severity"), T.T("Col_Notes"));
            foreach (var r in tailored)
            {
                var row = table.AddRow();
                ResultRuleCell(table, row, r);
                SeverityCell(row.Cells[1], r.Result.Severity);
                ResultNotes(table, row, 2, r);
            }
            KeepStart(table);
        }

        var notCheckable = results.Where(r => r.Result.Status == ResultStatus.NotCheckable)
            .GroupBy(r => r.Result.ReasonCode ?? "", StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        if (notCheckable.Count > 0)
        {
            SubHeading(T.F("Machine_NotCheckable", T.Number(notCheckable.Sum(g => g.Count()))));
            var table = NewTable(6.0, 11.0);
            HeaderRow(table, T.T("Col_Reason"), T.T("Col_Rules"));
            foreach (var g in notCheckable)
            {
                var row = table.AddRow();
                CellText(table, row, 0, g.Key.Length == 0 ? T.T("Reason_Unknown") : g.Key, bold: true);
                CellNote(table, row, 0, T.CodeText(g.Key));
                CellText(table, row, 1, string.Join("\n", g.Select(r => RuleWithFlags(r))));
            }
            KeepStart(table);
        }

        var notApplicable = results.Where(r => r.Result.Status == ResultStatus.NotApplicable).ToList();
        if (notApplicable.Count > 0)
        {
            SubHeading(T.F("Machine_NotApplicable", T.Number(notApplicable.Count)));
            Paragraph(string.Join(", ", notApplicable.Select(r => r.Result.ReasonCode is { } code ? $"{r.Result.RuleId} ({code})" : r.Result.RuleId)), small: true);
        }

        var passed = results.Where(r => r.Result.Status == ResultStatus.Pass).ToList();
        if (passed.Count > 0)
        {
            SubHeading(T.F("Machine_Passed", T.Number(passed.Count)));
            Paragraph(string.Join(", ", passed.Select(r => (r.Result.Flags & ResultFlags.FromDefault) != 0
                ? $"{r.Result.RuleId} ({T.Common("Flag_FromDefault")})" : r.Result.RuleId)), small: true);
        }
        if (m.Results.Count > results.Count) Truncated(m.Results.Count, results.Count);

        SubHeading(T.F("Machine_Drift", T.Number(m.Drift.Total)));
        if (m.Drift.Total == 0 || m.Drift.Top.Count == 0) Paragraph(T.T("Drift_None"), small: true);
        else
        {
            var table = NewTable(6.3, 2.3, 4.2, 4.2);
            HeaderRow(table, T.T("Col_Item"), T.T("Col_Change"), T.T("Col_Before"), T.T("Col_After"));
            foreach (var d in m.Drift.Top.Take(20))
            {
                var row = table.AddRow();
                CellText(table, row, 0, d.Key);
                CellNote(table, row, 0, d.Path);
                CellText(table, row, 1, T.Drift(d.Kind));
                CellText(table, row, 2, d.From);
                CellText(table, row, 3, d.To);
            }
            KeepStart(table);
            if (m.Drift.Total > m.Drift.Top.Count) Paragraph(T.F("Drift_More", T.Number(m.Drift.Total - m.Drift.Top.Count)), small: true);
        }

        if (m.Facts.Count > 0 || m.NetworkFacts.Count > 0)
        {
            SubHeading(T.T("Machine_Facts"));
            var table = NewTable(5.0, 3.5, 5.0, 3.5);
            HeaderRow(table, light: true, T.T("Col_Fact"), T.T("Col_Value"), T.T("Col_NetworkFact"), T.T("Col_Value"));
            int rows = Math.Max(m.Facts.Count, m.NetworkFacts.Count);
            for (int i = 0; i < rows; i++)
            {
                var row = table.AddRow();
                if (i < m.Facts.Count)
                {
                    CellText(table, row, 0, m.Facts[i].Name);
                    CellText(table, row, 1, m.Facts[i].Value ?? T.T("Unknown"));
                }
                if (i < m.NetworkFacts.Count)
                {
                    CellText(table, row, 2, m.NetworkFacts[i].Name);
                    CellText(table, row, 3, m.NetworkFacts[i].Value ?? T.T("Unknown"));
                }
            }
            KeepStart(table);
        }

        if (m.Inventory.Count > 0)
        {
            SubHeading(T.T("Machine_Inventory"));
            var table = NewTable(5.0, 12.0);
            HeaderRow(table, light: true, T.T("Col_Rule"), T.T("Col_Items"));
            foreach (var inv in m.Inventory)
            {
                var row = table.AddRow();
                CellText(table, row, 0, inv.RuleId, bold: true);
                CellNote(table, row, 0, inv.Title, TextColor);
                CellText(table, row, 1, inv.Items.Count == 0 ? T.T("None") : string.Join("\n", inv.Items.Take(40)));
                if (inv.Items.Count > 40) CellNote(table, row, 1, T.F("Truncated", T.Number(40), T.Number(inv.Items.Count)), WarningColor);
            }
            KeepStart(table);
        }
    }

    private void ResultRuleCell(Table table, Row row, RuleResultView r)
    {
        CellText(table, row, 0, r.Result.RuleId, bold: true);
        CellNote(table, row, 0, r.Title, TextColor);
    }

    private string RuleWithFlags(RuleResultView r)
    {
        var flags = T.Flags(r.Result.Flags);
        return flags.Length == 0 ? $"{r.Result.RuleId} {r.Title}" : $"{r.Result.RuleId} {r.Title} ({flags})";
    }

    /// <summary>Hinweise zu einem Ergebnis: Flags (z. B. „Standardwert angenommen“), Registerbezug, Einzelheiten, Grundcode.</summary>
    private void ResultNotes(Table table, Row row, int column, RuleResultView r)
    {
        var x = r.Result;
        var lines = new List<string>();
        var flags = T.Flags(x.Flags);
        if (flags.Length > 0) lines.Add(flags);
        if (x.ExceptionId is not null) lines.Add(T.F("Note_Exception", x.ExceptionId));
        if (x.TailoringId is not null) lines.Add(T.F("Note_Tailoring", x.TailoringId));
        if (x.MeasureId is not null) lines.Add(T.F("Note_Measure", x.MeasureId));
        if (x.Details is { Count: > 0 } details) lines.AddRange(details.Take(5));
        if (x.ReasonCode is not null) lines.Add(x.ReasonCode);
        if (r.OtNote is { Length: > 0 } ot) lines.Add(T.F("Note_Ot", ot));
        CellText(table, row, column, lines.Count == 0 ? T.Dash : string.Join("\n", lines));
    }
}
