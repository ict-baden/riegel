// SPDX-License-Identifier: MIT
using System.Text;
using Riegel.Core.Evidence;
using Riegel.Core.Model;
using Riegel.Core.Resources;

namespace Riegel.Core.Remediation;

/// <summary>
/// README.txt der Abhilfe (spec 5 O10): je Einstellung Regel, alter → neuer Wert, OT-Hinweis, geplante Maßnahme,
/// Hinweise zur Rücknahme; die weggelassenen Regeln mit Grund; manuelle Schritte. UTF-8 ohne BOM, CRLF.
/// </summary>
internal static class RemediationReadme
{
    public static byte[] Build(string header, string info, IReadOnlyList<RemediationWriter.RuleBlock> blocks,
        IReadOnlyList<RemediationExclusion> excluded, RegisterSet reg, OutputText t)
    {
        var lines = new List<string>
        {
            header,
            new('=', 72),
            info,
            "",
            t.Get(nameof(DataOutputStrings.Remediation_NotExecuted)),
            "",
            t.Get(nameof(DataOutputStrings.Remediation_FilesHeading)),
            new('-', 72),
            "  riegel-remediation.reg   " + t.Get(nameof(DataOutputStrings.Remediation_FileReg)),
            "  riegel-rollback.reg      " + t.Get(nameof(DataOutputStrings.Remediation_FileRollback)),
            "  GptTmpl.inf              " + t.Get(nameof(DataOutputStrings.Remediation_FileTemplate)),
            "  audit.csv                " + t.Get(nameof(DataOutputStrings.Remediation_FileAudit)),
            "  README.txt               " + t.Get(nameof(DataOutputStrings.Remediation_FileReadme)),
            "",
            t.Get(nameof(DataOutputStrings.Remediation_SettingsHeading)),
            new('-', 72),
        };
        var withSettings = blocks.Where(b => b.Settings.Count > 0 || b.Notes.Count > 0).ToList();
        if (withSettings.Count == 0) lines.Add("  " + t.Get(nameof(DataOutputStrings.Remediation_NoSettings)));
        foreach (var b in withSettings)
        {
            lines.Add("");
            lines.Add($"[{b.Rule.Id}] {Clean(b.Rule.Title.Get(t.Language))} ({t.Severity(b.Rule.Severity)})");
            lines.AddRange(b.Settings.Select(s => "  " + s));
            if (b.Rule.OtNote?.Get(t.Language) is { Length: > 0 } ot)
                lines.Add("  " + t.Get(nameof(DataOutputStrings.Remediation_OtNote), Clean(ot)));
            if (b.Measure is { } m)
                lines.Add("  " + t.Get(nameof(DataOutputStrings.Remediation_Measure), m.Id, Clean(m.Action), Clean(m.OwnerRole), OutputText.Date(m.DueDate),
                    t.Get("MeasureState_" + m.State)));
            lines.AddRange(b.Notes.Select(n => "  ! " + n));
        }
        lines.Add("");

        lines.Add(t.Get(nameof(DataOutputStrings.Remediation_ExcludedHeading)));
        lines.Add(new('-', 72));
        var listed = excluded.Where(e => e.Reason != RemediationExclusionReason.NotFailing).ToList();
        if (listed.Count == 0) lines.Add("  " + t.Get(nameof(DataOutputStrings.Remediation_NoneExcluded)));
        foreach (var e in listed.OrderBy(e => e.RuleId, StringComparer.Ordinal))
            lines.Add("  " + e.RuleId + " – " + t.Get("Exclusion_" + e.Reason, e.EntryId ?? "–") + EntryDetail(e, reg, t));
        lines.Add("");

        lines.Add(t.Get(nameof(DataOutputStrings.Remediation_ManualHeading)));
        lines.Add(new('-', 72));
        var manual = blocks.Where(b => b.Manual is not null).ToList();
        if (manual.Count == 0) lines.Add("  " + t.Get(nameof(DataOutputStrings.Remediation_NoManual)));
        foreach (var b in manual)
        {
            lines.Add($"  [{b.Rule.Id}] {Clean(b.Rule.Title.Get(t.Language))}");
            lines.Add("    " + Clean(b.Manual!));
        }
        lines.Add("");

        lines.Add(t.Get(nameof(DataOutputStrings.Remediation_HowToHeading)));
        lines.Add(new('-', 72));
        lines.Add(t.Get(nameof(DataOutputStrings.Remediation_HowToReg)));
        lines.Add(t.Get(nameof(DataOutputStrings.Remediation_HowToRollback)));
        lines.Add(t.Get(nameof(DataOutputStrings.Remediation_HowToTemplate)));
        lines.Add(t.Get(nameof(DataOutputStrings.Remediation_HowToAudit)));
        lines.Add(t.Get(nameof(DataOutputStrings.Remediation_NoScript)));
        return new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines) + "\r\n");
    }

    /// <summary>Begründung bzw. Grund des Register-Eintrags (gekürzt), damit der Hersteller die Entscheidung sieht.</summary>
    private static string EntryDetail(RemediationExclusion e, RegisterSet reg, OutputText t)
    {
        if (e.EntryId is null) return "";
        string? text = e.Reason switch
        {
            RemediationExclusionReason.ValidException or RemediationExclusionReason.PendingException =>
                reg.Exceptions.Exceptions.FirstOrDefault(x => x.Id == e.EntryId)?.Justification,
            RemediationExclusionReason.Tailored => reg.Tailoring.Entries.FirstOrDefault(x => x.Id == e.EntryId)?.Reason,
            _ => null,
        };
        return text is null ? "" : ": " + Values.Printable(Clean(text), 160);
    }

    /// <summary>Texte aus Katalog und Registern: Zeilenumbrüche zu Leerzeichen, Steuerzeichen ersetzt.</summary>
    private static string Clean(string text) => Values.Printable(text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' '), 4000);
}
