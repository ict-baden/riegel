// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;

namespace Riegel.Core.Reports.Pdf;

/// <summary>
/// Soll-Konfiguration (O13): je Gruppe/Rolle die Regeln im Prüfumfang mit dem Sollwert (aus der Prüfung erzeugt),
/// Anwendbarkeit und Herkunft, die Zuschnitt-Entscheidungen, die importierten Kundenbaselines, Katalogversion und
/// SHA-256, „Herkunft der Sollwerte“ und ein Freigabeblock.
/// </summary>
internal sealed class TargetConfigLayout(TargetConfigData data, string? fallbackFrom, PdfTexts t, PdfText text, string family,
    PageFrame frame, PdfLimits limits) : DocumentLayout(t, text, family, frame, limits)
{
    protected override void Compose()
    {
        TitleBlock(T.T("Target_Title"), data.PlantName);
        FontFallbackNote(fallbackFrom);
        Banners([]);

        var info = KeyValueTable(4.2);
        KeyValue(info, T.T("Cover_Plant"), $"{data.PlantName} ({data.PlantId})", bold: true);
        KeyValue(info, T.T("Target_Date"), PdfTexts.Date(data.Date), bold: true);
        KeyValue(info, T.T("Cover_Generated"), PdfTexts.DateTime(data.GeneratedAtUtc));
        KeyValue(info, T.T("Cover_Tool"), ToolLine(data.Tool));
        KeyValue(info, T.T("Cover_Catalogue"), T.F("Cover_CatalogueValue", data.Catalogue.Id, data.Catalogue.Version, PdfTexts.Date(data.Catalogue.ReleasedOn)));
        CellNote(info, info.Rows[info.Rows.Count - 1], 1, "SHA-256 " + data.Catalogue.Sha256);
        KeyValue(info, T.T("Register_Tailoring"), "SHA-256 " + data.TailoringSha256, size: SmallSize);
        if (data.Baselines.Count == 0) KeyValue(info, T.T("Cover_Baselines"), T.T("None"));
        else
        {
            KeyValue(info, T.T("Cover_Baselines"), string.Join("\n", data.Baselines.Select(b => $"{b.Name} ({b.Id}) · {T.F("Baseline_Rules", T.Number(b.RuleCount))}")));
            foreach (var b in data.Baselines) CellNote(info, info.Rows[info.Rows.Count - 1], 1, $"{b.Id}: SHA-256 {b.SourceSha256}");
        }
        KeyValue(info, T.T("Target_Groups"), T.Number(data.Groups.Count));
        Paragraph(T.T("Target_Intro"), small: true).Format.SpaceBefore = 4;

        // Herkunft der Sollwerte (spec 5 O13)
        Heading2(T.T("Target_Provenance_Heading"));
        var p = data.Provenance;
        var prov = NewTable(11.0, 6.0);
        HeaderRow(prov, light: true, T.T("Col_Source"), T.T("Target_RulesBacked"));
        void Row(string label, int value)
        {
            var row = prov.AddRow();
            CellText(prov, row, 0, label);
            CellText(prov, row, 1, T.Number(value), bold: true);
        }
        Row(T.T("Target_Prov_Total"), p.Total);
        Row(T.T("Target_Prov_Acsc"), p.Acsc);
        Row(T.T("Target_Prov_MsDocs"), p.MsDocs);
        Row(T.T("Target_Prov_MsLearn"), p.MsLearnFacts);
        Row(T.T("Target_Prov_Disa"), p.Disa);
        Row(T.T("Target_Prov_Msb"), p.AlsoInMsBaseline);
        KeepStart(prov, whole: true);
        Paragraph(T.T("Method_NoCisBsi"), small: true);

        foreach (var g in data.Groups) Group(g);

        // Freigabe
        Heading1(T.T("Target_SignOff_Heading"), newPage: true);
        Paragraph(T.T("Target_SignOff_Intro")).Format.KeepWithNext = true;
        var binding = Section.AddParagraph();
        binding.Format.SpaceBefore = 4;
        binding.Format.KeepWithNext = true;
        Text.Append(binding, T.F("Target_Binding", data.Catalogue.Version, PdfTexts.Sha8(data.Catalogue.Sha256), PdfTexts.Sha8(data.TailoringSha256),
            T.Number(data.Groups.Count)), ContentWidth, NormalSize, bold: true);
        ReferenceLine(T.F("Target_Reference", data.PlantName, PdfTexts.Date(data.Date)));
        var roles = ReportLayout.Roles(T, ReportPreset.Audit, data.SignOff);
        SignatureBlocks([.. roles.Select(r => (T.Step(r.Step), (string?)r.Role))]);
    }

    private void Group(TargetConfigGroup g)
    {
        var title = g.Role is { } role ? T.F("Target_GroupWithRole", g.Name, T.Role(role)) : T.F("Target_Group", g.Name);
        Heading1(title);
        Intro(T.F("Target_GroupIntro", T.Hosts(g.Hosts, max: 20), T.Number(g.Rules.Count)));
        if (g.Rules.Count == 0) Paragraph(T.T("Target_NoRules"));
        else
        {
            var table = NewTable(5.0, 1.5, 4.9, 2.7, 2.9);
            HeaderRow(table, T.T("Col_Rule"), T.T("Col_Severity"), T.T("Col_Expected"), T.T("Col_AppliesTo"), T.T("Col_Sources"));
            foreach (var r in g.Rules.Take(Limits.RegisterRows))
            {
                var row = table.AddRow();
                CellText(table, row, 0, r.RuleId, bold: true);
                CellNote(table, row, 0, r.Title, TextColor);
                CellNote(table, row, 0, T.Category(r.Category) + (r.RuleSetId != "riegel" ? " · " + T.F("Finding_RuleSet", r.RuleSetId) : ""));
                SeverityCell(row.Cells[1], r.Severity);
                CellText(table, row, 2, r.Expectation);
                if (r.Kind == RuleKind.Inventory) CellNote(table, row, 2, T.T("Target_Inventory"));
                CellText(table, row, 3, r.AppliesTo ?? T.T("Target_AppliesEverywhere"));
                CellText(table, row, 4, r.Sources.Count == 0 ? T.Dash : string.Join(", ", r.Sources));
                if (r.AlsoInMsBaseline) CellNote(table, row, 4, T.T("Target_AlsoMsb"));
            }
            KeepStart(table);
            Truncated(g.Rules.Count, Limits.RegisterRows);
        }

        Heading2(T.F("Target_Tailoring", g.Name));
        if (g.Tailoring.Count == 0) Paragraph(T.T("Tailoring_None"), small: true);
        else
        {
            var table = NewTable(1.8, 5.0, 2.6, 7.6);
            HeaderRow(table, T.T("Col_Entry"), T.T("Col_Rule"), T.T("Col_State"), T.T("Col_Reason"));
            foreach (var v in g.Tailoring)
            {
                var row = table.AddRow();
                CellText(table, row, 0, v.Entry.Id, bold: true);
                CellText(table, row, 1, $"{v.Entry.RuleId} {v.RuleTitle}");
                CellText(table, row, 2, T.State(v.State), bold: true);
                CellText(table, row, 3, v.Entry.Reason);
                CellNote(table, row, 3, T.F("Exc_ContentHash", PdfTexts.Sha8(v.ContentSha256)));
            }
            KeepStart(table);
        }
    }
}
