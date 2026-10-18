// SPDX-License-Identifier: MIT
using ClosedXML.Excel;
using Riegel.Core.Evidence;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Resources;

namespace Riegel.Core.Reports.Tabular;

/// <summary>
/// Excel-Mappe der Anlage (spec 5 O7): Zusammenfassung, Rechner, Befunde, Ergebnisse, Ausnahmen, Zuschnitt,
/// Maßnahmen, Änderungen, Versicherer-Fakten, Bestand, Regeln und Nachweise – in der Berichtssprache.
/// </summary>
internal static class OverviewWorkbook
{
    /// <summary>Schlüssel der Blattnamen in der Reihenfolge der Mappe (spec 5 O7).</summary>
    public static IReadOnlyList<string> SheetKeys { get; } =
    [
        nameof(DataOutputStrings.Sheet_Summary), nameof(DataOutputStrings.Sheet_Machines), nameof(DataOutputStrings.Sheet_Findings),
        nameof(DataOutputStrings.Sheet_Results), nameof(DataOutputStrings.Sheet_Exceptions), nameof(DataOutputStrings.Sheet_Tailoring),
        nameof(DataOutputStrings.Sheet_Measures), nameof(DataOutputStrings.Sheet_Changes), nameof(DataOutputStrings.Sheet_InsurerFacts),
        nameof(DataOutputStrings.Sheet_Inventory), nameof(DataOutputStrings.Sheet_Rules), nameof(DataOutputStrings.Sheet_Evidence),
    ];

    public static void Write(ReportData d, string path)
    {
        var t = OutputText.For(d.Header.Language);
        using var workbook = new XLWorkbook();
        workbook.Properties.Author = About.AppInfo.Name;
        workbook.Properties.Title = d.Header.PlantName;
        workbook.Properties.Created = DateTime.SpecifyKind(d.Header.GeneratedAtUtc.UtcDateTime, DateTimeKind.Unspecified);
        workbook.Properties.Modified = workbook.Properties.Created;

        Summary(workbook, d, t);
        Machines(workbook, d, t);
        Findings(workbook, d, t);
        Results(workbook, d, t);
        Exceptions(workbook, d, t);
        Tailoring(workbook, d, t);
        Measures(workbook, d, t);
        Changes(workbook, d, t);
        Insurer(workbook, d, t);
        Inventory(workbook, d, t);
        Rules(workbook, d, t);
        EvidenceSheet(workbook, d, t);

        Save(workbook, path);
    }

    internal static void Save(XLWorkbook workbook, string path)
    {
        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        OutputFiles.WriteAtomic(path, buffer.ToArray());
    }

    internal static string[] Headers(OutputText t, params string[] keys) => [.. keys.Select(k => t.Get(k))];

    // ---------------------------------------------------------------- Zusammenfassung

    private static void Summary(XLWorkbook wb, ReportData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_Summary)),
            Headers(t, nameof(DataOutputStrings.Col_Section), nameof(DataOutputStrings.Col_Item), nameof(DataOutputStrings.Col_Value)));
        var h = d.Header;
        string plant = t.Get(nameof(DataOutputStrings.Section_Plant));
        s.Add(plant, t.Get(nameof(DataOutputStrings.Item_PlantName)), h.PlantName);
        s.Add(plant, t.Get(nameof(DataOutputStrings.Item_PlantId)), h.PlantId);
        if (h.PlantDescription is { Length: > 0 }) s.Add(plant, t.Get(nameof(DataOutputStrings.Item_Description)), h.PlantDescription);
        s.Add(plant, t.Get(nameof(DataOutputStrings.Item_ReportId)), h.ReportId);
        s.Add(plant, t.Get(nameof(DataOutputStrings.Item_Preset)), t.Get("Preset_" + h.Preset));
        s.Add(plant, t.Get(nameof(DataOutputStrings.Item_EvaluationDate)), h.EvaluationDate);
        s.Add(plant, t.Get(nameof(DataOutputStrings.Item_GeneratedAt)), h.GeneratedAtUtc);
        s.Add(plant, t.Get(nameof(DataOutputStrings.Item_Hosts)), Cells.Join(h.Hosts));
        if (h.PerformedBy is { Length: > 0 }) s.Add(plant, t.Get(nameof(DataOutputStrings.Item_PerformedBy)), h.PerformedBy);
        s.Add(plant, t.Get(nameof(DataOutputStrings.Item_Confidentiality)), h.Confidentiality);
        s.Add(plant, t.Get(nameof(DataOutputStrings.Item_Language)), h.Language);
        if (h.PreTranslated) s.Add(plant, t.Get(nameof(DataOutputStrings.Item_Language)), t.Common("PreTranslation_Note"));

        var c = d.Counts;
        string counts = t.Get(nameof(DataOutputStrings.Section_Counts));
        void Count(string key, int value) => s.Add(counts, t.Get(key), value);
        Count(nameof(DataOutputStrings.Item_OpenHigh), c.OpenHigh);
        Count(nameof(DataOutputStrings.Item_OpenMedium), c.OpenMedium);
        Count(nameof(DataOutputStrings.Item_OpenLow), c.OpenLow);
        Count(nameof(DataOutputStrings.Item_Regressions), c.Regressions);
        Count(nameof(DataOutputStrings.Item_NewlyOpen), c.NewlyOpen);
        Count(nameof(DataOutputStrings.Item_NotCheckable), c.NotCheckable);
        Count(nameof(DataOutputStrings.Item_ExceptionsValid), c.ExceptionsValid);
        Count(nameof(DataOutputStrings.Item_ExceptionsExpiring), c.ExceptionsExpiring);
        Count(nameof(DataOutputStrings.Item_ExceptionsExpired), c.ExceptionsExpired);
        Count(nameof(DataOutputStrings.Item_ExceptionsPending), c.ExceptionsPending);
        Count(nameof(DataOutputStrings.Item_TailoringEntries), c.TailoringEntries);
        Count(nameof(DataOutputStrings.Item_MeasuresPlanned), c.MeasuresPlanned);
        Count(nameof(DataOutputStrings.Item_MeasuresOverdue), c.MeasuresOverdue);
        Count(nameof(DataOutputStrings.Item_Machines), c.Machines);
        Count(nameof(DataOutputStrings.Item_MachinesScored), c.MachinesScored);
        Count(nameof(DataOutputStrings.Item_UnsupportedOs), c.UnsupportedOs);
        Count(nameof(DataOutputStrings.Item_StaleSnapshots), c.StaleSnapshots);
        Count(nameof(DataOutputStrings.Item_DomainControllers), c.DomainControllers);

        string score = t.Get(nameof(DataOutputStrings.Section_Score));
        var sv = d.Score;
        s.Add(score, t.Get(nameof(DataOutputStrings.Item_Score)), sv.Score);
        s.Add(score, t.Get(nameof(DataOutputStrings.Item_Documented)), sv.Documented);
        s.Add(score, t.Get(nameof(DataOutputStrings.Item_CheckedShare)), sv.CheckedShare);
        if (sv.Greyed) s.Add(score, t.Get(nameof(DataOutputStrings.Item_Greyed)), t.Get(nameof(DataOutputStrings.Value_OnlyCheckable), sv.CheckedShare));
        s.Add(score, t.Get(nameof(DataOutputStrings.Item_AuditCapped)), sv.AuditCapped);

        string categories = t.Get(nameof(DataOutputStrings.Section_Categories));
        foreach (var cat in d.Categories)
            s.Add(categories, t.Category(cat.Category), cat.Score is { } v
                ? t.Get(nameof(DataOutputStrings.Value_CategoryScore), v, cat.Pass, cat.Fail, cat.Exception) + (cat.Capped ? " " + t.Get(nameof(DataOutputStrings.Value_Capped)) : "")
                : t.Get(nameof(DataOutputStrings.Value_NoScore)));

        string banners = t.Get(nameof(DataOutputStrings.Section_Banners));
        foreach (var b in d.Banners) s.Add(banners, BannerText(b, t), Cells.Join(b.Hosts.Concat(b.Areas.Select(t.Category))));

        string baselines = t.Get(nameof(DataOutputStrings.Section_Baselines));
        foreach (var b in d.Baselines)
            s.Add(baselines, b.Baseline.Name, t.Get(nameof(DataOutputStrings.Value_BaselineScore),
                b.Score is { } bs ? bs + " %" : t.Get(nameof(DataOutputStrings.Value_NoScore)), b.Pass, b.Fail, b.Exception, b.NotCheckable)
                + (b.ContentIncluded ? "" : " – " + t.Get(nameof(DataOutputStrings.Verify_BaselineNotVerifiable))));

        string signOff = t.Get(nameof(DataOutputStrings.Section_SignOff));
        var so = d.SignOff;
        s.Add(signOff, t.Get(nameof(DataOutputStrings.Item_SignOffBinding)), t.Get(nameof(DataOutputStrings.Value_SignOffBinding),
            Hashes.Short(so.ExceptionsSha256), so.ExceptionCount, so.ExceptionValidCount, Hashes.Short(so.TailoringSha256)));
        foreach (var role in so.Roles) s.Add(signOff, StepText(role.Step, t), role.Role);
        s.Add(signOff, t.Get(nameof(DataOutputStrings.Item_RunIds)), Cells.Join(so.RunIds));
        s.Finish();
    }

    internal static string StepText(string step, OutputText t) => step switch
    {
        SignOffSteps.Created => t.Get(nameof(DataOutputStrings.Step_Created)),
        SignOffSteps.Reviewed => t.Get(nameof(DataOutputStrings.Step_Reviewed)),
        SignOffSteps.Approved => t.Get(nameof(DataOutputStrings.Step_Approved)),
        SignOffSteps.Accepted => t.Get(nameof(DataOutputStrings.Step_Accepted)),
        _ => step,
    };

    private static string BannerText(Banner b, OutputText t) => b.Kind switch
    {
        BannerKind.IncompleteCheck => t.Get(nameof(DataOutputStrings.Banner_IncompleteCheck), b.CheckedShare ?? 0),
        BannerKind.StaleSnapshot => t.Get(nameof(DataOutputStrings.Banner_StaleSnapshot), b.AgeDays ?? 0),
        BannerKind.StaleCatalogue => t.Get(nameof(DataOutputStrings.Banner_StaleCatalogue)),
        BannerKind.PreTranslation => t.Common("PreTranslation_Note"),
        BannerKind.ExperimentalGpoAttribution => t.Get(nameof(DataOutputStrings.Banner_ExperimentalGpoAttribution)),
        BannerKind.ClockSuspect => t.Get(nameof(DataOutputStrings.Banner_ClockSuspect)),
        BannerKind.DomainController => t.Get(nameof(DataOutputStrings.Banner_DomainController)),
        _ => b.Kind.ToString(),
    };

    // ---------------------------------------------------------------- Rechner

    private static void Machines(XLWorkbook wb, ReportData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_Machines)), Headers(t,
            nameof(DataOutputStrings.Col_Host), nameof(DataOutputStrings.Col_Label), nameof(DataOutputStrings.Col_Group),
            nameof(DataOutputStrings.Col_Role), nameof(DataOutputStrings.Col_MachineType), nameof(DataOutputStrings.Col_SerialNumber),
            nameof(DataOutputStrings.Col_OrderNumber), nameof(DataOutputStrings.Col_DeliveryDate), nameof(DataOutputStrings.Col_PlantSoftware),
            nameof(DataOutputStrings.Col_Builder), nameof(DataOutputStrings.Col_Location), nameof(DataOutputStrings.Col_Os),
            nameof(DataOutputStrings.Col_Build), nameof(DataOutputStrings.Col_Ubr), nameof(DataOutputStrings.Col_Edition),
            nameof(DataOutputStrings.Col_DomainRole), nameof(DataOutputStrings.Col_SupportEnd), nameof(DataOutputStrings.Col_Supported),
            nameof(DataOutputStrings.Col_OpenHigh), nameof(DataOutputStrings.Col_OpenMedium), nameof(DataOutputStrings.Col_OpenLow),
            nameof(DataOutputStrings.Col_Pass), nameof(DataOutputStrings.Col_Fail), nameof(DataOutputStrings.Col_Exception),
            nameof(DataOutputStrings.Col_OutOfScope), nameof(DataOutputStrings.Col_NotApplicable), nameof(DataOutputStrings.Col_NotCheckable),
            nameof(DataOutputStrings.Col_Info), nameof(DataOutputStrings.Col_Score), nameof(DataOutputStrings.Col_Documented),
            nameof(DataOutputStrings.Col_CheckedShare), nameof(DataOutputStrings.Col_CollectedAt), nameof(DataOutputStrings.Col_AgeDays),
            nameof(DataOutputStrings.Col_Admin), nameof(DataOutputStrings.Col_Minimal), nameof(DataOutputStrings.Col_Deep),
            nameof(DataOutputStrings.Col_Pseudonymized), nameof(DataOutputStrings.Col_Origin), nameof(DataOutputStrings.Col_Stale),
            nameof(DataOutputStrings.Col_RunFlags), nameof(DataOutputStrings.Col_RunId), nameof(DataOutputStrings.Col_SnapshotSha256),
            nameof(DataOutputStrings.Col_Collector), nameof(DataOutputStrings.Col_CollectorSha256), nameof(DataOutputStrings.Col_ImportId)),
            freezeFirstColumn: true);
        foreach (var m in d.Machines)
        {
            var id = m.Identity;
            var sum = m.Summary;
            var c = m.Collection;
            s.Add(m.HostName, m.Label, m.GroupName ?? m.GroupId, m.Role is { } role ? t.Get("MachineRole_" + role) : null,
                id?.MachineType, id?.SerialNumber, id?.OrderNumber, id?.DeliveryDate, id?.PlantSoftware, id?.Builder, id?.Location,
                m.Lifecycle?.ProductName, m.Machine.Build, m.Machine.Ubr, m.Machine.EditionId, m.Machine.DomainRole,
                m.Lifecycle?.EndOfSupport, m.Lifecycle?.Supported,
                sum.OpenHigh, sum.OpenMedium, sum.OpenLow, sum.Pass, sum.Fail, sum.Exception, sum.OutOfScope, sum.NotApplicable,
                sum.NotCheckable, sum.Info,
                m.IsDomainController ? t.Get(nameof(DataOutputStrings.Value_NotScoredDc)) : CellValue.Of(sum.Score),
                m.IsDomainController ? CellValue.Empty : CellValue.Of(sum.Documented), sum.CheckedShare,
                c.CollectedAtUtc, c.AgeDays, c.IsAdmin, c.Options.Minimal, c.Options.Deep, c.Options.Pseudonymized,
                t.Get("Origin_" + c.Origin), (m.Flags & RunFlags.StaleSnapshot) != 0, t.Flags(m.Flags), m.RunId, c.SnapshotSha256,
                Cells.Join([c.CollectorName, c.CollectorVersion], " "), c.CollectorExeSha256, c.ImportId);
        }
        s.Finish();
    }

    // ---------------------------------------------------------------- Befunde

    private static void Findings(XLWorkbook wb, ReportData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_Findings)), Headers(t,
            nameof(DataOutputStrings.Col_RuleSet), nameof(DataOutputStrings.Col_RuleId), nameof(DataOutputStrings.Col_Title),
            nameof(DataOutputStrings.Col_Category), nameof(DataOutputStrings.Col_Severity), nameof(DataOutputStrings.Col_OpenCount),
            nameof(DataOutputStrings.Col_ApplicableCount), nameof(DataOutputStrings.Col_Hosts), nameof(DataOutputStrings.Col_MeasureId),
            nameof(DataOutputStrings.Col_OwnerRole), nameof(DataOutputStrings.Col_DueDate), nameof(DataOutputStrings.Col_Overdue),
            nameof(DataOutputStrings.Col_Flags)));
        foreach (var f in d.Findings)
            s.Add(f.RuleSetId, f.RuleId, f.Title, t.Category(f.Category), t.Severity(f.Severity), f.OpenCount, f.ApplicableCount,
                Cells.JoinCapped(f.Hosts, f.MoreHosts), f.Measure?.MeasureId, f.Measure?.OwnerRole, f.Measure?.DueDate, f.Measure?.Overdue,
                t.Flags(f.Flags));
        s.Finish();
    }

    // ---------------------------------------------------------------- Ergebnisse (Rechner × Regel)

    private static void Results(XLWorkbook wb, ReportData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_Results)), Headers(t,
            nameof(DataOutputStrings.Col_Host), nameof(DataOutputStrings.Col_RuleSet), nameof(DataOutputStrings.Col_RuleId),
            nameof(DataOutputStrings.Col_Title), nameof(DataOutputStrings.Col_Category), nameof(DataOutputStrings.Col_Severity),
            nameof(DataOutputStrings.Col_Kind), nameof(DataOutputStrings.Col_Status), nameof(DataOutputStrings.Col_Flags),
            nameof(DataOutputStrings.Col_Expected), nameof(DataOutputStrings.Col_Actual), nameof(DataOutputStrings.Col_ValueSource),
            nameof(DataOutputStrings.Col_SourceDetail), nameof(DataOutputStrings.Col_ExceptionId), nameof(DataOutputStrings.Col_TailoringId),
            nameof(DataOutputStrings.Col_MeasureId), nameof(DataOutputStrings.Col_ReasonCode), nameof(DataOutputStrings.Col_Details),
            nameof(DataOutputStrings.Col_EvidenceKeys)), freezeFirstColumn: true);
        foreach (var m in d.Machines)
            foreach (var v in m.Results)
            {
                var r = v.Result;
                s.Add(m.HostName, r.RuleSetId, r.RuleId, v.Title, t.Category(v.Category), t.Severity(r.Severity),
                    t.Get("Kind_" + v.Kind), t.Status(r.Status), t.Flags(r.Flags), r.Expected,
                    r.Status is ResultStatus.Info ? r.Actual : t.Display(r.Actual), t.Get("ValueSource_" + r.Source), r.SourceDetail,
                    r.ExceptionId, r.TailoringId, r.MeasureId, r.ReasonCode, Cells.Join(r.Details, "\n"), Cells.Join(r.EvidenceKeys, "\n"));
            }
        s.Finish();
    }

    // ---------------------------------------------------------------- Register

    internal static string Scope(RegisterScope scope, OutputText t)
    {
        var parts = new List<string>();
        if (scope.AllMachines) parts.Add(t.Get(nameof(DataOutputStrings.Scope_All)));
        if (scope.Machines.Count > 0) parts.Add(t.Get(nameof(DataOutputStrings.Scope_Machines), string.Join(", ", scope.Machines)));
        if (scope.Groups.Count > 0) parts.Add(t.Get(nameof(DataOutputStrings.Scope_Groups), string.Join(", ", scope.Groups)));
        return string.Join("; ", parts);
    }

    internal static string State(RegisterEntryState state, OutputText t) => t.Get("EntryState_" + state);

    private static void Exceptions(XLWorkbook wb, ReportData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_Exceptions)), Headers(t,
            nameof(DataOutputStrings.Col_Id), nameof(DataOutputStrings.Col_RuleId), nameof(DataOutputStrings.Col_Title),
            nameof(DataOutputStrings.Col_Severity), nameof(DataOutputStrings.Col_State), nameof(DataOutputStrings.Col_RegisterFlags),
            nameof(DataOutputStrings.Col_Scope), nameof(DataOutputStrings.Col_ReasonCategory), nameof(DataOutputStrings.Col_Justification),
            nameof(DataOutputStrings.Col_CompensatingMeasure), nameof(DataOutputStrings.Col_Reference), nameof(DataOutputStrings.Col_AcceptedValue),
            nameof(DataOutputStrings.Col_ApprovedByRole), nameof(DataOutputStrings.Col_ApprovedByName), nameof(DataOutputStrings.Col_ApprovedOn),
            nameof(DataOutputStrings.Col_ReviewBy), nameof(DataOutputStrings.Col_ApprovalRecord), nameof(DataOutputStrings.Col_ContentSha256),
            nameof(DataOutputStrings.Col_CreatedOn), nameof(DataOutputStrings.Col_CreatedByRole), nameof(DataOutputStrings.Col_ModifiedOn),
            nameof(DataOutputStrings.Col_CatalogueVersion), nameof(DataOutputStrings.Col_AffectedHosts), nameof(DataOutputStrings.Col_Comment)));
        foreach (var v in d.Exceptions.Concat(d.ExpiredExceptions).OrderBy(v => v.Entry.Id, StringComparer.Ordinal))
        {
            var e = v.Entry;
            var a = e.Approval;
            s.Add(e.Id, e.RuleId, v.RuleTitle, t.Severity(v.Severity), State(v.State, t), t.Flags(v.Flags), Scope(e.Scope, t),
                t.Reason(e.Category), e.Justification, e.CompensatingMeasure, e.Reference, e.AcceptedValue, a.ApprovedByRole,
                a.ApprovedByName, a.ApprovedOn, a.ReviewBy, a.ApprovalRecord, v.ContentSha256, e.CreatedOn, e.CreatedByRole, e.ModifiedOn,
                e.CatalogueVersion, Cells.Join(v.AffectedHosts), e.Comment);
        }
        s.Finish();
    }

    private static void Tailoring(XLWorkbook wb, ReportData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_Tailoring)), Headers(t,
            nameof(DataOutputStrings.Col_Id), nameof(DataOutputStrings.Col_RuleId), nameof(DataOutputStrings.Col_Title),
            nameof(DataOutputStrings.Col_Severity), nameof(DataOutputStrings.Col_State), nameof(DataOutputStrings.Col_RegisterFlags),
            nameof(DataOutputStrings.Col_Scope), nameof(DataOutputStrings.Col_TailoringReason), nameof(DataOutputStrings.Col_ApprovedByRole),
            nameof(DataOutputStrings.Col_ApprovedByName), nameof(DataOutputStrings.Col_ApprovedOn), nameof(DataOutputStrings.Col_ReviewBy),
            nameof(DataOutputStrings.Col_ApprovalRecord), nameof(DataOutputStrings.Col_ContentSha256), nameof(DataOutputStrings.Col_CreatedOn),
            nameof(DataOutputStrings.Col_CreatedByRole), nameof(DataOutputStrings.Col_ModifiedOn), nameof(DataOutputStrings.Col_AffectedHosts),
            nameof(DataOutputStrings.Col_Comment)));
        foreach (var v in d.Tailoring.OrderBy(v => v.Entry.Id, StringComparer.Ordinal))
        {
            var e = v.Entry;
            var a = e.Approval;
            s.Add(e.Id, e.RuleId, v.RuleTitle, t.Severity(v.Severity), State(v.State, t), t.Flags(v.Flags), Scope(e.Scope, t), e.Reason,
                a.ApprovedByRole, a.ApprovedByName, a.ApprovedOn, a.ReviewBy, a.ApprovalRecord, v.ContentSha256, e.CreatedOn, e.CreatedByRole,
                e.ModifiedOn, Cells.Join(v.AffectedHosts), e.Comment);
        }
        s.Finish();
    }

    private static void Measures(XLWorkbook wb, ReportData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_Measures)), Headers(t,
            nameof(DataOutputStrings.Col_Id), nameof(DataOutputStrings.Col_RuleId), nameof(DataOutputStrings.Col_Title),
            nameof(DataOutputStrings.Col_Severity), nameof(DataOutputStrings.Col_Action), nameof(DataOutputStrings.Col_OwnerRole),
            nameof(DataOutputStrings.Col_DueDate), nameof(DataOutputStrings.Col_Ticket), nameof(DataOutputStrings.Col_MeasureState),
            nameof(DataOutputStrings.Col_Overdue), nameof(DataOutputStrings.Col_RegisterFlags), nameof(DataOutputStrings.Col_OpenCount),
            nameof(DataOutputStrings.Col_Scope), nameof(DataOutputStrings.Col_AffectedHosts), nameof(DataOutputStrings.Col_CreatedOn),
            nameof(DataOutputStrings.Col_CreatedByRole), nameof(DataOutputStrings.Col_ModifiedOn), nameof(DataOutputStrings.Col_Comment)));
        foreach (var v in d.Measures.OrderBy(v => v.Entry.Id, StringComparer.Ordinal))
        {
            var e = v.Entry;
            s.Add(e.Id, e.RuleId, v.RuleTitle, t.Severity(v.Severity), e.Action, e.OwnerRole, e.DueDate, e.Ticket,
                t.Get("MeasureState_" + e.State), v.Overdue, t.Flags(v.Flags), v.OpenCount, Scope(e.Scope, t), Cells.Join(v.AffectedHosts),
                e.CreatedOn, e.CreatedByRole, e.ModifiedOn, e.Comment);
        }
        s.Finish();
    }

    // ---------------------------------------------------------------- Änderungen

    private static void Changes(XLWorkbook wb, ReportData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_Changes)), Headers(t,
            nameof(DataOutputStrings.Col_Host), nameof(DataOutputStrings.Col_Basis), nameof(DataOutputStrings.Col_FromRun),
            nameof(DataOutputStrings.Col_ToRun), nameof(DataOutputStrings.Col_RuleSet), nameof(DataOutputStrings.Col_RuleId),
            nameof(DataOutputStrings.Col_Title), nameof(DataOutputStrings.Col_Severity), nameof(DataOutputStrings.Col_ChangeKind),
            nameof(DataOutputStrings.Col_FromStatus), nameof(DataOutputStrings.Col_ToStatus), nameof(DataOutputStrings.Col_FromFlags),
            nameof(DataOutputStrings.Col_ToFlags), nameof(DataOutputStrings.Col_Detail)));
        // „Verschlechtert“ zuerst (spec 2.14), dann nach Rechner und Regel
        foreach (var c in d.Changes.OrderBy(c => c.Change.Kind == StatusChangeKind.Worse ? 0 : 1)
                     .ThenBy(c => c.HostName, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Change.RuleId, StringComparer.Ordinal))
        {
            var ch = c.Change;
            s.Add(c.HostName, t.Get("Basis_" + c.Basis), c.FromRunId, c.ToRunId, ch.RuleSetId, ch.RuleId, c.Title, t.Severity(c.Severity),
                t.Get("ChangeKind_" + ch.Kind), ch.From is { } from ? t.Status(from) : null, ch.To is { } to ? t.Status(to) : null,
                t.Flags(ch.FromFlags), t.Flags(ch.ToFlags), ch.Detail);
        }
        s.Finish();
    }

    // ---------------------------------------------------------------- Versicherer-Fakten

    private static void Insurer(XLWorkbook wb, ReportData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_InsurerFacts)), Headers(t,
            nameof(DataOutputStrings.Col_Host), nameof(DataOutputStrings.Col_Os), nameof(DataOutputStrings.Col_Build),
            nameof(DataOutputStrings.Col_SupportEnd), nameof(DataOutputStrings.Col_Supported), nameof(DataOutputStrings.Col_AvProduct),
            nameof(DataOutputStrings.Col_AvState), nameof(DataOutputStrings.Col_SignatureAgeDays), nameof(DataOutputStrings.Col_DaysSinceUpdate),
            nameof(DataOutputStrings.Col_PatchMonth), nameof(DataOutputStrings.Col_LocalAdminCount), nameof(DataOutputStrings.Col_RdpEnabled),
            nameof(DataOutputStrings.Col_RdpNla), nameof(DataOutputStrings.Col_Smb1), nameof(DataOutputStrings.Col_BitLocker),
            nameof(DataOutputStrings.Col_FirewallOn), nameof(DataOutputStrings.Col_NetGateway), nameof(DataOutputStrings.Col_NetActiveAdapters),
            nameof(DataOutputStrings.Col_NetWireless), nameof(DataOutputStrings.Col_NetIpForwarding), nameof(DataOutputStrings.Col_NetProxy)),
            freezeFirstColumn: true);
        foreach (var r in d.InsurerFacts)
            s.Add(r.HostName, r.OsName, r.Build, r.SupportEnd, r.Supported, r.AvProduct, r.AvState, r.SignatureAgeDays, r.DaysSinceUpdate,
                r.PatchMonth, r.LocalAdminCount, r.RdpEnabled, r.RdpNla, r.Smb1, r.BitLocker, r.FirewallOn, r.Network.GatewayConfigured,
                r.Network.ActiveAdapters, r.Network.Wireless, r.Network.IpForwarding, r.Network.ProxyConfigured);
        s.Finish([t.Get(nameof(DataOutputStrings.Insurer_NotReadable))]);
    }

    // ---------------------------------------------------------------- Bestand

    private static void Inventory(XLWorkbook wb, ReportData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_Inventory)), Headers(t,
            nameof(DataOutputStrings.Col_Host), nameof(DataOutputStrings.Col_RuleId), nameof(DataOutputStrings.Col_Title),
            nameof(DataOutputStrings.Col_Renderer), nameof(DataOutputStrings.Col_Item)));
        foreach (var m in d.Machines)
            foreach (var inv in m.Inventory)
            {
                if (inv.Items.Count == 0) s.Add(m.HostName, inv.RuleId, inv.Title, t.Get("Renderer_" + inv.Renderer), CellValue.Empty);
                foreach (var item in inv.Items) s.Add(m.HostName, inv.RuleId, inv.Title, t.Get("Renderer_" + inv.Renderer), item);
            }
        s.Finish();
    }

    // ---------------------------------------------------------------- Regeln

    private static void Rules(XLWorkbook wb, ReportData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_Rules)), Headers(t,
            nameof(DataOutputStrings.Col_RuleSet), nameof(DataOutputStrings.Col_RuleId), nameof(DataOutputStrings.Col_Title),
            nameof(DataOutputStrings.Col_Category), nameof(DataOutputStrings.Col_Severity), nameof(DataOutputStrings.Col_Kind),
            nameof(DataOutputStrings.Col_Rationale), nameof(DataOutputStrings.Col_OtNote), nameof(DataOutputStrings.Col_Remedy),
            nameof(DataOutputStrings.Col_References)));
        // Jede Regel einmal (erste Fundstelle), sortiert nach Regelsatz und ID
        var seen = new HashSet<(string, string)>();
        var views = new List<RuleResultView>();
        foreach (var m in d.Machines)
            foreach (var v in m.Results)
                if (seen.Add((v.Result.RuleSetId, v.Result.RuleId))) views.Add(v);
        foreach (var v in views.OrderBy(v => v.Result.RuleSetId == "riegel" ? 0 : 1).ThenBy(v => v.Result.RuleSetId, StringComparer.Ordinal)
                     .ThenBy(v => v.Result.RuleId, StringComparer.Ordinal))
            s.Add(v.Result.RuleSetId, v.Result.RuleId, v.Title, t.Category(v.Category), t.Severity(v.Result.Severity), t.Get("Kind_" + v.Kind),
                v.Rationale, v.OtNote, v.Remedy, Cells.Join(v.References, "\n"));
        s.Finish();
    }

    // ---------------------------------------------------------------- Nachweise

    private static void EvidenceSheet(XLWorkbook wb, ReportData d, OutputText t)
    {
        var s = new SheetBuilder(wb, t.Get(nameof(DataOutputStrings.Sheet_Evidence)), Headers(t,
            nameof(DataOutputStrings.Col_EvidenceKind), nameof(DataOutputStrings.Col_Subject), nameof(DataOutputStrings.Col_Value)));
        var h = d.Header;
        string tool = t.Get(nameof(DataOutputStrings.Evidence_Tool));
        s.Add(tool, h.Tool.Name, h.Tool.Version);
        s.Add(tool, t.Get(nameof(DataOutputStrings.Evidence_Commit)), h.Tool.Commit);
        s.Add(tool, t.Get(nameof(DataOutputStrings.Evidence_ExeSha256)), h.Tool.ExeSha256);
        string catalogue = t.Get(nameof(DataOutputStrings.Evidence_Catalogue));
        s.Add(catalogue, h.Catalogue.Id + " " + h.Catalogue.Version, h.Catalogue.Sha256);
        s.Add(catalogue, t.Get(nameof(DataOutputStrings.Evidence_ReleasedOn)), h.Catalogue.ReleasedOn);
        string registers = t.Get(nameof(DataOutputStrings.Evidence_Register));
        s.Add(registers, "exceptions.json", h.Registers.Exceptions);
        s.Add(registers, "tailoring.json", h.Registers.Tailoring);
        s.Add(registers, "measures.json", h.Registers.Measures);
        string baseline = t.Get(nameof(DataOutputStrings.Evidence_Baseline));
        foreach (var b in h.Baselines)
            s.Add(baseline, b.Id + " – " + b.Name + (b.SourceFileName is { } f ? " (" + f + ")" : ""), b.SourceSha256);
        string snapshot = t.Get(nameof(DataOutputStrings.Evidence_Snapshot));
        string run = t.Get(nameof(DataOutputStrings.Evidence_Run));
        string collector = t.Get(nameof(DataOutputStrings.Evidence_Collector));
        foreach (var m in d.Machines)
        {
            s.Add(run, m.HostName, m.RunId);
            s.Add(snapshot, m.HostName, m.Collection.SnapshotSha256);
            if (m.Collection.CollectorExeSha256 is { } exe) s.Add(collector, m.HostName + " – " + m.Collection.CollectorName + " " + m.Collection.CollectorVersion, exe);
        }
        s.Finish();
    }
}
