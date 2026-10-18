// SPDX-License-Identifier: MIT
using ClosedXML.Excel;
using Riegel.Core.Evidence;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Resources;

namespace Riegel.Core.Reports.Tabular;

/// <summary>
/// Soll-Konfiguration als Excel-Mappe (spec 5 O13): Übersicht mit Katalog, Herkunft der Sollwerte und Zuschnitt-Hash,
/// Regeln im Prüfumfang je Gruppe/Rolle mit Sollwerten, Zuschnittentscheidungen, importierte Baselines und ein
/// Freigabeblock.
/// </summary>
internal static class TargetConfigWorkbook
{
    /// <summary>Schlüssel der Blattnamen in der Reihenfolge der Mappe.</summary>
    public static IReadOnlyList<string> SheetKeys { get; } =
    [
        nameof(DataOutputStrings.Sheet_TargetOverview), nameof(DataOutputStrings.Sheet_TargetRules),
        nameof(DataOutputStrings.Sheet_Tailoring), nameof(DataOutputStrings.Sheet_TargetBaselines),
        nameof(DataOutputStrings.Sheet_TargetSignOff),
    ];

    public static void Write(TargetConfigData d, string path)
    {
        var t = OutputText.For(d.Language);
        using var workbook = new XLWorkbook();
        workbook.Properties.Author = About.AppInfo.Name;
        workbook.Properties.Title = d.PlantName;
        workbook.Properties.Created = DateTime.SpecifyKind(d.GeneratedAtUtc.UtcDateTime, DateTimeKind.Unspecified);
        workbook.Properties.Modified = workbook.Properties.Created;

        Overview(workbook, d, t);
        Rules(workbook, d, t);
        Tailoring(workbook, d, t);
        Baselines(workbook, d, t);
        SignOff(workbook, d, t);
        OverviewWorkbook.Save(workbook, path);
    }

    private static void Overview(XLWorkbook wb, TargetConfigData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_TargetOverview)), OverviewWorkbook.Headers(t,
            nameof(DataOutputStrings.Col_Section), nameof(DataOutputStrings.Col_Item), nameof(DataOutputStrings.Col_Value)));
        string plant = t.Get(nameof(DataOutputStrings.Section_Plant));
        s.Add(plant, t.Get(nameof(DataOutputStrings.Item_PlantName)), d.PlantName);
        s.Add(plant, t.Get(nameof(DataOutputStrings.Item_PlantId)), d.PlantId);
        s.Add(plant, t.Get(nameof(DataOutputStrings.Item_Date)), d.Date);
        s.Add(plant, t.Get(nameof(DataOutputStrings.Item_GeneratedAt)), d.GeneratedAtUtc);
        s.Add(plant, t.Get(nameof(DataOutputStrings.Item_Confidentiality)), d.Confidentiality);
        s.Add(plant, t.Get(nameof(DataOutputStrings.Item_Language)), d.Language);
        if (d.PreTranslated) s.Add(plant, t.Get(nameof(DataOutputStrings.Item_Language)), t.Common("PreTranslation_Note"));
        string tool = t.Get(nameof(DataOutputStrings.Evidence_Tool));
        s.Add(tool, d.Tool.Name, d.Tool.Version);
        s.Add(tool, t.Get(nameof(DataOutputStrings.Evidence_Commit)), d.Tool.Commit);
        string catalogue = t.Get(nameof(DataOutputStrings.Evidence_Catalogue));
        s.Add(catalogue, d.Catalogue.Id + " " + d.Catalogue.Version, d.Catalogue.Sha256);
        s.Add(catalogue, t.Get(nameof(DataOutputStrings.Evidence_ReleasedOn)), d.Catalogue.ReleasedOn);
        s.Add(t.Get(nameof(DataOutputStrings.Sheet_Tailoring)), "tailoring.json", d.TailoringSha256);

        string provenance = t.Get(nameof(DataOutputStrings.Section_Provenance));
        var p = d.Provenance;
        s.Add(provenance, t.Get(nameof(DataOutputStrings.Provenance_Total)), p.Total);
        s.Add(provenance, t.Get(nameof(DataOutputStrings.Provenance_Acsc)), p.Acsc);
        s.Add(provenance, t.Get(nameof(DataOutputStrings.Provenance_MsDocs)), p.MsDocs);
        s.Add(provenance, t.Get(nameof(DataOutputStrings.Provenance_MsLearnFacts)), p.MsLearnFacts);
        s.Add(provenance, t.Get(nameof(DataOutputStrings.Provenance_Disa)), p.Disa);
        s.Add(provenance, t.Get(nameof(DataOutputStrings.Provenance_AlsoInMsBaseline)), p.AlsoInMsBaseline);

        string groups = t.Get(nameof(DataOutputStrings.Section_Groups));
        foreach (var g in d.Groups)
            s.Add(groups, g.Name, t.Get(nameof(DataOutputStrings.Value_GroupSummary), g.Rules.Count, g.Tailoring.Count, Cells.Join(g.Hosts, ", ")));
        s.Finish();
    }

    private static void Rules(XLWorkbook wb, TargetConfigData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_TargetRules)), OverviewWorkbook.Headers(t,
            nameof(DataOutputStrings.Col_Group), nameof(DataOutputStrings.Col_Role), nameof(DataOutputStrings.Col_Hosts),
            nameof(DataOutputStrings.Col_RuleSet), nameof(DataOutputStrings.Col_RuleId), nameof(DataOutputStrings.Col_Title),
            nameof(DataOutputStrings.Col_Category), nameof(DataOutputStrings.Col_Severity), nameof(DataOutputStrings.Col_Kind),
            nameof(DataOutputStrings.Col_Expectation), nameof(DataOutputStrings.Col_AppliesTo), nameof(DataOutputStrings.Col_Sources),
            nameof(DataOutputStrings.Col_AlsoInMsBaseline)));
        foreach (var g in d.Groups)
        {
            var hosts = Cells.Join(g.Hosts, ", ");
            var role = g.Role is { } r ? t.Get("MachineRole_" + r) : null;
            foreach (var rule in g.Rules)
                s.Add(g.Name, role, hosts, rule.RuleSetId, rule.RuleId, rule.Title, t.Category(rule.Category), t.Severity(rule.Severity),
                    t.Get("Kind_" + rule.Kind), rule.Expectation, rule.AppliesTo ?? t.Get(nameof(DataOutputStrings.Value_AppliesEverywhere)),
                    Cells.Join(rule.Sources, ", "), rule.AlsoInMsBaseline);
        }
        s.Finish();
    }

    private static void Tailoring(XLWorkbook wb, TargetConfigData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_Tailoring)), OverviewWorkbook.Headers(t,
            nameof(DataOutputStrings.Col_Group), nameof(DataOutputStrings.Col_Id), nameof(DataOutputStrings.Col_RuleId),
            nameof(DataOutputStrings.Col_Title), nameof(DataOutputStrings.Col_Severity), nameof(DataOutputStrings.Col_State),
            nameof(DataOutputStrings.Col_Scope), nameof(DataOutputStrings.Col_TailoringReason), nameof(DataOutputStrings.Col_ApprovedByRole),
            nameof(DataOutputStrings.Col_ApprovedOn), nameof(DataOutputStrings.Col_ReviewBy), nameof(DataOutputStrings.Col_ApprovalRecord),
            nameof(DataOutputStrings.Col_ContentSha256)));
        foreach (var g in d.Groups)
            foreach (var v in g.Tailoring)
            {
                var e = v.Entry;
                s.Add(g.Name, e.Id, e.RuleId, v.RuleTitle, t.Severity(v.Severity), OverviewWorkbook.State(v.State, t),
                    OverviewWorkbook.Scope(e.Scope, t), e.Reason, e.Approval.ApprovedByRole, e.Approval.ApprovedOn, e.Approval.ReviewBy,
                    e.Approval.ApprovalRecord, v.ContentSha256);
            }
        s.Finish();
    }

    private static void Baselines(XLWorkbook wb, TargetConfigData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_TargetBaselines)), OverviewWorkbook.Headers(t,
            nameof(DataOutputStrings.Col_Id), nameof(DataOutputStrings.Col_Name), nameof(DataOutputStrings.Col_SourceKind),
            nameof(DataOutputStrings.Col_SourceFile), nameof(DataOutputStrings.Col_SourceSha256), nameof(DataOutputStrings.Col_ImportedAt),
            nameof(DataOutputStrings.Col_RuleCount)));
        foreach (var b in d.Baselines)
            s.Add(b.Id, b.Name, t.Get("SourceKind_" + b.Kind), b.SourceFileName, b.SourceSha256, b.ImportedAtUtc, b.RuleCount);
        s.Finish();
    }

    private static void SignOff(XLWorkbook wb, TargetConfigData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_TargetSignOff)), OverviewWorkbook.Headers(t,
            nameof(DataOutputStrings.Col_Step), nameof(DataOutputStrings.Col_Role), nameof(DataOutputStrings.Col_Name),
            nameof(DataOutputStrings.Col_Date), nameof(DataOutputStrings.Col_Signature), nameof(DataOutputStrings.Col_Remarks)));
        foreach (var role in d.SignOff) s.Add(OverviewWorkbook.StepText(role.Step, t), role.Role);
        s.Finish([t.Get(nameof(DataOutputStrings.TargetConfig_SignOffNote), Hashes.Short(d.Catalogue.Sha256), Hashes.Short(d.TailoringSha256))]);
    }
}
