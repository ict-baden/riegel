// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Plants;

namespace Riegel.Core.Registers;

/// <summary>
/// Prüft die drei Register (spec 2.11–2.13): Pflichtfelder, Mindestlängen, Grenzen des Prüftermins, Begründungsart,
/// akzeptierter Wert, Rollen für die Risikoübernahme, Freigabe und Inhalts-Hash, Überschneidungen, unbekannte und
/// zurückgezogene Regeln, Gruppen und Rechner. Ergebnis sortiert nach Register, Eintrag und Code.
/// </summary>
internal static class RegisterValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(RegisterSet r, ICatalogue c, Plant p, DateOnly today)
    {
        var result = new List<ValidationIssue>();
        var knownHosts = KnownHosts(p);
        var groups = new HashSet<string>(p.Groups.Select(g => g.Id), StringComparer.OrdinalIgnoreCase);

        void Add(RegisterKind kind, string? id, IEnumerable<Issue> issues)
        {
            foreach (var i in issues)
                result.Add(new ValidationIssue(kind, id, i.Severity, i.Code, i.Field, RglCodesWorkflow.Message(i.Code, i.Args)));
        }

        void FileIssues(RegisterKind kind, string plantId)
        {
            if (!string.Equals(plantId, p.Id, StringComparison.Ordinal))
                Add(kind, null, [new Issue(RglCodesWorkflow.PlantIdMismatch, IssueSeverity.Warning, "plantId", plantId, p.Id)]);
        }

        List<Issue> RuleAndScope(string ruleId, RegisterScope scope)
        {
            var list = new List<Issue>();
            if (!string.IsNullOrWhiteSpace(ruleId))
            {
                var rule = c.Find(ruleId.Trim());
                if (rule is null) list.Add(new(RglCodesWorkflow.UnknownRule, IssueSeverity.Error, "ruleId", ruleId));
                else if (rule.State == RuleState.Retired) list.Add(new(RglCodesWorkflow.RuleRetired, IssueSeverity.Warning, "ruleId", ruleId));
            }
            foreach (var g in scope.Groups.Where(g => !string.IsNullOrWhiteSpace(g) && !groups.Contains(g.Trim())))
                list.Add(new(RglCodesWorkflow.UnknownGroup, IssueSeverity.Error, "scope.groups", g));
            foreach (var m in scope.Machines.Where(m => !string.IsNullOrWhiteSpace(m) && !knownHosts.Contains(FileNames.Host(m.Trim()))))
                list.Add(new(RglCodesWorkflow.UnknownMachine, IssueSeverity.Warning, "scope.machines", m));
            return list;
        }

        // ---- Ausnahmen
        FileIssues(RegisterKind.Exceptions, r.Exceptions.PlantId);
        var exceptions = r.Exceptions.Exceptions;
        foreach (var e in exceptions)
        {
            var rule = string.IsNullOrWhiteSpace(e.RuleId) ? null : c.Find(e.RuleId.Trim());
            var issues = RuleAndScope(e.RuleId, e.Scope);
            RegisterRules.ExceptionState(e, rule?.Severity, rule?.Kind, p, today, issues);
            issues.AddRange(Duplicates(e.Id, exceptions.Select(x => x.Id)));
            foreach (var other in exceptions.Where(x => !ReferenceEquals(x, e) && SameRule(x.RuleId, e.RuleId) && RegisterRules.Overlap(x.Scope, e.Scope, p)))
                issues.Add(new(RglCodesWorkflow.Overlap, IssueSeverity.Warning, "scope", other.Id));
            foreach (var t in r.Tailoring.Entries.Where(t => SameRule(t.RuleId, e.RuleId) && RegisterRules.Overlap(t.Scope, e.Scope, p)))
                issues.Add(new(RglCodesWorkflow.Overlap, IssueSeverity.Warning, "scope", t.Id));
            Add(RegisterKind.Exceptions, e.Id, issues);
        }

        // ---- Zuschnitt
        FileIssues(RegisterKind.Tailoring, r.Tailoring.PlantId);
        var tailoring = r.Tailoring.Entries;
        foreach (var t in tailoring)
        {
            var rule = string.IsNullOrWhiteSpace(t.RuleId) ? null : c.Find(t.RuleId.Trim());
            var issues = RuleAndScope(t.RuleId, t.Scope);
            RegisterRules.TailoringState(t, rule?.Severity, p, today, issues);
            issues.AddRange(Duplicates(t.Id, tailoring.Select(x => x.Id)));
            foreach (var other in tailoring.Where(x => !ReferenceEquals(x, t) && SameRule(x.RuleId, t.RuleId) && RegisterRules.Overlap(x.Scope, t.Scope, p)))
                issues.Add(new(RglCodesWorkflow.Overlap, IssueSeverity.Warning, "scope", other.Id));
            Add(RegisterKind.Tailoring, t.Id, issues);
        }

        // ---- Maßnahmen
        FileIssues(RegisterKind.Measures, r.Measures.PlantId);
        var measures = r.Measures.Measures;
        foreach (var m in measures)
        {
            var issues = RuleAndScope(m.RuleId, m.Scope);
            RegisterRules.MeasureIssues(m, today, issues);
            issues.AddRange(Duplicates(m.Id, measures.Select(x => x.Id)));
            Add(RegisterKind.Measures, m.Id, issues);
        }

        return [.. result.OrderBy(i => i.Register).ThenBy(i => i.EntryId ?? "", StringComparer.Ordinal).ThenBy(i => i.Code, StringComparer.Ordinal)
            .ThenBy(i => i.Field ?? "", StringComparer.Ordinal).ThenBy(i => i.Message, StringComparer.Ordinal)];
    }

    /// <summary>Höchster Schweregrad als Exitcode von <c>exceptions validate</c>: 0 gültig, 1 Warnungen, 2 ungültig (spec 7.7).</summary>
    public static int ExitCode(IReadOnlyList<ValidationIssue> issues) =>
        issues.Any(i => i.Severity == IssueSeverity.Error) ? 2 : issues.Count > 0 ? 1 : 0;

    private static bool SameRule(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Issue> Duplicates(string id, IEnumerable<string> all)
    {
        if (!string.IsNullOrWhiteSpace(id) && all.Count(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)) > 1)
            yield return new Issue(RglCodesWorkflow.DuplicateId, IssueSeverity.Error, "id", id);
    }

    /// <summary>Bekannte Rechner (bereinigte Namen): Einträge der Anlage und Ordner unter runs/ (unbekannte Rechner erscheinen über ihre Läufe).</summary>
    internal static HashSet<string> KnownHosts(Plant p)
    {
        var hosts = new HashSet<string>(p.Machines.Select(m => FileNames.Host(m.HostName)), StringComparer.OrdinalIgnoreCase);
        if (p.Folder is { } folder && Directory.Exists(Path.Combine(folder, PlantFiles.Runs)))
            foreach (var dir in Directory.EnumerateDirectories(Path.Combine(folder, PlantFiles.Runs)))
                hosts.Add(Path.GetFileName(dir));
        return hosts;
    }
}
