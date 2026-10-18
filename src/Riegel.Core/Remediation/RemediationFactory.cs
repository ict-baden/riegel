// SPDX-License-Identifier: MIT
using Riegel.Core.Evidence;
using Riegel.Core.Model;
using Riegel.Core.Resources;
using Riegel.Core.Services;

namespace Riegel.Core.Remediation;

/// <summary>
/// Factory der Abhilfe-Dateien (Paket data-outputs; Signatur fest). Riegel schreibt nur Dateien (O10) und führt nie
/// etwas davon aus: kein Prozessstart, kein Registry-Zugriff, keine .ps1-Datei.
/// </summary>
public static class RemediationFactory
{
    public static IRemediationWriter Create(RiegelServices services) => new RemediationWriter();
}

/// <summary>
/// Schreibt <c>remediation/&lt;host&gt;/&lt;runId&gt;/</c> (spec 5 O10): <c>riegel-remediation.reg</c>,
/// <c>riegel-rollback.reg</c>, <c>GptTmpl.inf</c>, <c>audit.csv</c>, <c>README.txt</c>. Nur offene Befunde
/// (<c>fail</c>) mit Abhilfe im Katalog; Regeln mit gültiger oder beantragter Ausnahme oder mit Zuschnitt bleiben
/// draußen und stehen im README. Schlüssel und Wertnamen kommen ausschließlich aus dem Katalog.
/// </summary>
internal sealed class RemediationWriter : IRemediationWriter
{
    /// <summary>Ein Abschnitt einer .reg-Datei.</summary>
    private sealed class RegSection(string key)
    {
        public string Key { get; } = key;
        public List<(string Name, RegData Data)> Values { get; } = [];
    }

    /// <summary>Was das README zu einer Regel sagt.</summary>
    internal sealed class RuleBlock(Rule rule, RuleResult result)
    {
        public Rule Rule { get; } = rule;
        public RuleResult Result { get; } = result;
        public List<string> Settings { get; } = [];
        public List<string> Notes { get; } = [];
        public string? Manual { get; set; }
        public MeasureEntry? Measure { get; set; }
    }

    private enum Decision { Include, Skip, Exclude }

    public RemediationOutput Write(Run r, Snapshot s, ICatalogue c, RegisterSet reg, string outDir, string lang)
    {
        var t = OutputText.For(lang);
        var runFolder = RunIds.IsValid(r.Id) ? r.Id : FileNames.Token(r.Id, "run");
        var folder = Path.Combine(outDir, "remediation", FileNames.Host(r.HostName), runFolder);
        Directory.CreateDirectory(folder);
        var header = t.Common("Remediation_Header");

        var remediation = new List<RegSection>();
        var rollback = new List<RegSection>();
        var template = new List<TemplateFile.Line>();
        var audit = new List<AuditCsv.Row>();
        var included = new List<string>();
        var excluded = new List<RemediationExclusion>();
        var manualIds = new List<string>();
        var blocks = new List<RuleBlock>();
        var seenRegistry = new HashSet<string>(StringComparer.Ordinal);
        var seenTemplate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenAudit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var machineSid = TemplateFile.MachineSidPrefix(s);

        var results = r.Results.GroupBy(x => (x.RuleSetId, x.RuleId)).Select(g => g.First())
            .OrderBy(x => x.RuleSetId == "riegel" ? 0 : 1).ThenBy(x => x.RuleSetId, StringComparer.Ordinal).ThenBy(x => x.RuleId, StringComparer.Ordinal);
        foreach (var result in results)
        {
            var decision = Decide(result, reg, r.HostName, r.EvaluationDate, out var exclusion, out var expiredNote, t);
            if (decision == Decision.Skip) continue;
            if (decision == Decision.Exclude)
            {
                excluded.Add(exclusion!);
                continue;
            }
            var rule = c.Find(result.RuleId);
            if (rule is null || !string.Equals(rule.RuleSetId, result.RuleSetId, StringComparison.Ordinal) || rule.Remediation is not { } spec)
            {
                excluded.Add(new RemediationExclusion(result.RuleId, RemediationExclusionReason.NoRemediation, null));
                continue;
            }
            var block = new RuleBlock(rule, result) { Measure = MeasureFor(result, reg, r.HostName) };
            if (expiredNote is not null) block.Notes.Add(expiredNote);
            bool wrote = false;

            foreach (var action in spec.Registry ?? [])
                wrote |= AddRegistry(action, s, remediation, rollback, seenRegistry, block, t);

            // Der Istwert der Prüfung gehört zur Regel; bei mehreren Vorlagen- oder Überwachungs-Einträgen steht er einmal davor
            bool single = (spec.SecurityTemplate?.Count ?? 0) + (spec.Audit?.Count ?? 0) == 1;
            if ((spec.SecurityTemplate?.Count ?? 0) + (spec.Audit?.Count ?? 0) > 1)
                block.Settings.Add(t.Get(nameof(DataOutputStrings.Remediation_ActualOfRule), Shown(result.Actual, t)));
            foreach (var action in spec.SecurityTemplate ?? [])
            {
                var line = TemplateFile.Build(action, machineSid, out bool needsManual);
                var where = "[" + Values.Printable(action.Section, 60) + "] " + Values.Printable(action.Key, 120);
                if (line is null)
                {
                    block.Notes.Add(needsManual
                        ? t.Get(nameof(DataOutputStrings.Remediation_TemplateManual), where)
                        : t.Get(nameof(DataOutputStrings.Remediation_ActionInvalid), where, RglCodesDataOutputs.RemediationActionInvalid));
                    continue;
                }
                if (!seenTemplate.Add(line.Section + "|" + line.Key))
                {
                    block.Notes.Add(t.Get(nameof(DataOutputStrings.Remediation_Duplicate), where));
                    continue;
                }
                template.Add(line);
                block.Settings.Add(single
                    ? t.Get(nameof(DataOutputStrings.Remediation_SettingTemplate), where, Shown(result.Actual, t), line.NewValue)
                    : t.Get(nameof(DataOutputStrings.Remediation_SettingTemplateNew), where, line.NewValue));
                wrote = true;
            }

            foreach (var action in spec.Audit ?? [])
            {
                var row = AuditCsv.Build(action);
                var where = Values.Printable(action.Subcategory, 120) + " " + Values.Printable(action.Guid, 40);
                if (row is null)
                {
                    block.Notes.Add(t.Get(nameof(DataOutputStrings.Remediation_ActionInvalid), where, RglCodesDataOutputs.RemediationActionInvalid));
                    continue;
                }
                if (!seenAudit.Add(row.Guid))
                {
                    block.Notes.Add(t.Get(nameof(DataOutputStrings.Remediation_Duplicate), where));
                    continue;
                }
                audit.Add(row);
                block.Settings.Add(single
                    ? t.Get(nameof(DataOutputStrings.Remediation_SettingAudit), where, Shown(result.Actual, t), OutputText.Camel(row.Setting))
                    : t.Get(nameof(DataOutputStrings.Remediation_SettingAuditNew), where, OutputText.Camel(row.Setting)));
                wrote = true;
            }

            if (spec.Manual is { } manual && manual.Get(t.Language) is { Length: > 0 } text)
            {
                block.Manual = text;
                manualIds.Add(rule.Id);
            }
            if (wrote) included.Add(rule.Id);
            else if (block.Manual is null) excluded.Add(new RemediationExclusion(rule.Id, RemediationExclusionReason.NoRemediation, null));
            if (wrote || block.Manual is not null || block.Notes.Count > 0) blocks.Add(block);
        }

        var infoLine = t.Get(nameof(DataOutputStrings.Remediation_InfoLine), About.AppInfo.Name, About.AppInfo.Version, r.HostName, r.Id,
            OutputText.Date(r.EvaluationDate));
        var files = new List<string>();
        string Write(string name, byte[] bytes)
        {
            var path = Path.Combine(folder, name);
            OutputFiles.WriteAtomic(path, bytes);
            files.Add(path);
            return path;
        }
        Write(ReportFileNames.RemediationFiles[0], RegFile.Encode(RegLines(header, infoLine, t.Get(nameof(DataOutputStrings.Remediation_RegComment)), remediation)));
        Write(ReportFileNames.RemediationFiles[1], RegFile.Encode(RegLines(header, infoLine, t.Get(nameof(DataOutputStrings.Remediation_RollbackComment)), rollback)));
        Write(ReportFileNames.RemediationFiles[2], TemplateFile.Encode([header, infoLine], template));
        Write(ReportFileNames.RemediationFiles[3], AuditCsv.Encode(header, audit));
        Write(ReportFileNames.RemediationFiles[4], RemediationReadme.Build(header, infoLine, blocks, excluded, reg, t));
        return new RemediationOutput(folder, files, included, excluded, manualIds);
    }

    private static string Shown(string? canonical, OutputText t) => Values.Printable(t.Display(canonical), 200);

    /// <summary>Zeilen einer .reg-Datei: Versionszeile, Hinweise als Kommentar, Leerzeile, Abschnitte, Leerzeile am Ende.</summary>
    private static IEnumerable<string> RegLines(string header, string info, string what, IReadOnlyList<RegSection> sections)
    {
        yield return RegFile.VersionLine;
        yield return RegFile.Comment(header);
        yield return RegFile.Comment(info);
        yield return RegFile.Comment(what);
        foreach (var section in sections)
        {
            if (section.Values.Count == 0) continue;
            yield return "";
            yield return RegFile.KeyLine(section.Key);
            foreach (var (name, data) in section.Values)
                foreach (var line in RegFile.ValueLines(name, data)) yield return line;
        }
        yield return "";
    }

    /// <summary>Ein Registry-Wert aus dem Katalog in Abhilfe und Rücknahme. true = geschrieben.</summary>
    private static bool AddRegistry(RegistryAction action, Snapshot s, List<RegSection> remediation, List<RegSection> rollback,
        HashSet<string> seen, RuleBlock block, OutputText t)
    {
        var key = Values.MachineKey(action.Key);
        var where = Values.Printable(action.Key, 200) + "\\" + (action.Value is { Length: > 0 } ? Values.Printable(action.Value, 120) : "@");
        if (key is null || !Values.IsValueName(action.Value))
        {
            block.Notes.Add(t.Get(nameof(DataOutputStrings.Remediation_ActionInvalid), where, RglCodesDataOutputs.RemediationActionInvalid));
            return false;
        }
        RegData? data = action.Delete ? new RegData.Delete() : Values.FromCatalogue(action.Type, action.Data, out _);
        if (data is null)
        {
            block.Notes.Add(t.Get(nameof(DataOutputStrings.Remediation_ActionInvalid), where, RglCodesDataOutputs.RemediationActionInvalid));
            return false;
        }
        if (!seen.Add(Values.Normal(key) + "|" + action.Value.ToLowerInvariant()))
        {
            block.Notes.Add(t.Get(nameof(DataOutputStrings.Remediation_Duplicate), where));
            return false;
        }
        Section(remediation, key).Values.Add((action.Value, data));

        var old = Values.FromSnapshot(s, action.Key, action.Value);
        string oldText;
        switch (old)
        {
            case OldValue.NotSet:
                oldText = t.NotSet;
                if (data is not RegData.Delete) Section(rollback, key).Values.Add((action.Value, new RegData.Delete()));
                break;
            case OldValue.Present p:
                oldText = Values.Printable(p.Data.Describe(), 200);
                Section(rollback, key).Values.Add((action.Value, p.Data));
                break;
            case OldValue.Omitted o:
                oldText = t.Get("OmitReason_" + o.Reason);
                block.Notes.Add(t.Get(nameof(DataOutputStrings.Remediation_RollbackOmitted), where, t.Get("OmitReason_" + o.Reason),
                    RglCodesDataOutputs.RollbackValueOmitted));
                break;
            default:
                oldText = "";
                break;
        }
        block.Settings.Add(t.Get(nameof(DataOutputStrings.Remediation_SettingRegistry), where, oldText,
            data is RegData.Delete ? t.Get(nameof(DataOutputStrings.Remediation_Deleted)) : Values.Printable(data.Describe(), 200)));
        return true;
    }

    private static RegSection Section(List<RegSection> sections, string key)
    {
        var found = sections.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        if (found is not null) return found;
        found = new RegSection(key);
        sections.Add(found);
        return found;
    }

    /// <summary>
    /// Was mit einem Ergebnis geschieht. Maßgeblich sind die Angaben im Ergebnis (Register schon angewendet); dazu
    /// prüft der Schreiber selbst die Register-Einträge, die genau diesen Rechner oder die ganze Anlage betreffen –
    /// lieber eine Regel zu viel weglassen als eine bewusst offene Einstellung zur Änderung vorschlagen.
    /// </summary>
    private static Decision Decide(RuleResult result, RegisterSet reg, string host, DateOnly date, out RemediationExclusion? exclusion,
        out string? expiredNote, OutputText t)
    {
        exclusion = null;
        expiredNote = null;
        switch (result.Status)
        {
            case ResultStatus.Exception:
                exclusion = new(result.RuleId, RemediationExclusionReason.ValidException, result.ExceptionId);
                return Decision.Exclude;
            case ResultStatus.OutOfScope:
                exclusion = new(result.RuleId, RemediationExclusionReason.Tailored, result.TailoringId);
                return Decision.Exclude;
            case ResultStatus.Fail:
                break;
            default:
                return Decision.Skip;
        }
        var flags = result.Flags;
        if (result.ExceptionId is { } exceptionId)
        {
            if ((flags & ResultFlags.ExceptionExpired) == 0)
            {
                bool pending = (flags & (ResultFlags.ExceptionPending | ResultFlags.ExceptionChangedAfterApproval)) != 0;
                exclusion = new(result.RuleId, pending ? RemediationExclusionReason.PendingException : RemediationExclusionReason.ValidException, exceptionId);
                return Decision.Exclude;
            }
            expiredNote = t.Get(nameof(DataOutputStrings.Remediation_ExceptionExpired), exceptionId);
        }
        if (result.TailoringId is { } tailoringId && (flags & ResultFlags.TailoringPending) != 0)
        {
            exclusion = new(result.RuleId, RemediationExclusionReason.Tailored, tailoringId);
            return Decision.Exclude;
        }
        foreach (var e in reg.Exceptions.Exceptions.Where(e => SameRule(e.RuleId, result.RuleId) && Covers(e.Scope, host)).OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            var a = e.Approval;
            if (a.ReviewBy is { } reviewBy && reviewBy < date)
            {
                expiredNote ??= t.Get(nameof(DataOutputStrings.Remediation_ExceptionExpired), e.Id);
                continue;
            }
            bool complete = !string.IsNullOrWhiteSpace(a.ApprovedByRole) && a.ApprovedOn is not null && a.ReviewBy is not null
                            && !string.IsNullOrWhiteSpace(a.ApprovalRecord)
                            && string.Equals(a.ApprovedContentSha256, ContentHashes.Of(e), StringComparison.OrdinalIgnoreCase);
            exclusion = new(result.RuleId, complete ? RemediationExclusionReason.ValidException : RemediationExclusionReason.PendingException, e.Id);
            return Decision.Exclude;
        }
        foreach (var tl in reg.Tailoring.Entries.Where(x => SameRule(x.RuleId, result.RuleId) && Covers(x.Scope, host)).OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            if (tl.Approval.ReviewBy is { } reviewBy && reviewBy < date) continue;
            exclusion = new(result.RuleId, RemediationExclusionReason.Tailored, tl.Id);
            return Decision.Exclude;
        }
        return Decision.Include;
    }

    private static bool SameRule(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Rechner genannt oder ganze Anlage (Gruppen sind ohne Anlage nicht auflösbar; dafür gilt das Ergebnis).</summary>
    private static bool Covers(RegisterScope scope, string host) =>
        scope.AllMachines || scope.Machines.Any(m => string.Equals(m, host, StringComparison.OrdinalIgnoreCase));

    private static MeasureEntry? MeasureFor(RuleResult result, RegisterSet reg, string host)
    {
        if (result.MeasureId is { } id && reg.Measures.Measures.FirstOrDefault(m => m.Id == id) is { } byId) return byId;
        return reg.Measures.Measures.Where(m => m.State == MeasureState.Planned && SameRule(m.RuleId, result.RuleId) && Covers(m.Scope, host))
            .OrderBy(m => m.DueDate).FirstOrDefault();
    }
}
