// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Registers;

namespace Riegel.Core.ReportBuilding;

/// <summary>
/// Registersichten der Berichte (spec 5 O4 Abschnitte 5–7, O7): Zustand am Stichtag, betroffene Rechner und die
/// Kennzeichen <c>unused</c> (Regel besteht überall, „Ausnahme nicht mehr nötig?“), <c>overlap</c>,
/// <c>ruleRetired</c>, <c>valueMismatch</c> und <c>measureDoneButFailing</c>.
/// </summary>
internal sealed class RegisterViews
{
    public required IReadOnlyList<ExceptionView> Exceptions { get; init; }
    public required IReadOnlyList<TailoringView> Tailoring { get; init; }
    public required IReadOnlyList<MeasureView> Measures { get; init; }

    public static RegisterViews Build(PlantView view, DateOnly date, string language)
    {
        var plant = view.Plant;
        var catalogue = view.Catalogue;
        var r = view.Registers;
        var runs = view.Runs;
        var hosts = view.Hosts;
        string Title(string ruleId) => catalogue.Find(ruleId)?.Title.Get(language) ?? ruleId;
        IEnumerable<RuleResult> ResultsOf(string ruleId, IEnumerable<string> onHosts) =>
            runs.Where(run => onHosts.Contains(run.HostName, StringComparer.OrdinalIgnoreCase))
                .Select(run => run.Results.FirstOrDefault(x => string.Equals(x.RuleId, ruleId, StringComparison.OrdinalIgnoreCase)))
                .OfType<RuleResult>();
        List<string> Affected(RegisterScope scope) =>
            [.. hosts.Where(h => RegisterRules.Specificity(scope, h, RegisterRules.GroupOf(plant, h)) > 0)];
        Severity? SeverityOf(string ruleId) => catalogue.Find(ruleId)?.Severity ?? runs.SelectMany(x => x.Results)
            .FirstOrDefault(x => string.Equals(x.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))?.Severity;
        bool Retired(string ruleId) => catalogue.Find(ruleId)?.State == RuleState.Retired;
        bool Overlaps(string id, string ruleId, RegisterScope scope) =>
            r.Exceptions.Exceptions.Any(o => o.Id != id && Same(o.RuleId, ruleId) && RegisterRules.Overlap(o.Scope, scope, plant)) ||
            r.Tailoring.Entries.Any(o => o.Id != id && Same(o.RuleId, ruleId) && RegisterRules.Overlap(o.Scope, scope, plant));
        var duplicates = new HashSet<string>(r.Exceptions.Exceptions.Select(e => e.Id).Concat(r.Tailoring.Entries.Select(t => t.Id))
            .GroupBy(i => i, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.OrdinalIgnoreCase);

        var exceptions = r.Exceptions.Exceptions.OrderBy(e => e.Id, StringComparer.Ordinal).Select(e =>
        {
            var rule = catalogue.Find(e.RuleId);
            var state = duplicates.Contains(e.Id) ? RegisterEntryState.Invalid
                : RegisterRules.ExceptionState(e, rule?.Severity ?? SeverityOf(e.RuleId), rule?.Kind, plant, date);
            var affected = Affected(e.Scope);
            var results = ResultsOf(e.RuleId, affected).ToList();
            var flags = RegisterFlags.None;
            if (results.Count > 0 && results.Any(x => x.Status == ResultStatus.Pass) &&
                results.All(x => x.Status is ResultStatus.Pass or ResultStatus.NotApplicable or ResultStatus.OutOfScope or ResultStatus.Info))
                flags |= RegisterFlags.Unused;
            if (Overlaps(e.Id, e.RuleId, e.Scope)) flags |= RegisterFlags.Overlap;
            if (Retired(e.RuleId)) flags |= RegisterFlags.RuleRetired;
            if (results.Any(x => x.ExceptionId == e.Id && (x.Flags & ResultFlags.ExceptionValueMismatch) != 0)) flags |= RegisterFlags.ValueMismatch;
            return new ExceptionView(e, state, flags, Title(e.RuleId), rule?.Severity ?? SeverityOf(e.RuleId), affected, ContentHashes.Of(e));
        }).ToList();

        var tailoring = r.Tailoring.Entries.OrderBy(t => t.Id, StringComparer.Ordinal).Select(t =>
        {
            var rule = catalogue.Find(t.RuleId);
            var state = duplicates.Contains(t.Id) ? RegisterEntryState.Invalid
                : RegisterRules.TailoringState(t, rule?.Severity ?? SeverityOf(t.RuleId), plant, date);
            var flags = RegisterFlags.None;
            if (Overlaps(t.Id, t.RuleId, t.Scope)) flags |= RegisterFlags.Overlap;
            if (Retired(t.RuleId)) flags |= RegisterFlags.RuleRetired;
            return new TailoringView(t, state, flags, Title(t.RuleId), rule?.Severity ?? SeverityOf(t.RuleId), Affected(t.Scope), ContentHashes.Of(t));
        }).ToList();

        var measures = r.Measures.Measures.OrderBy(m => m.Id, StringComparer.Ordinal).Select(m =>
        {
            var affected = Affected(m.Scope);
            int open = ResultsOf(m.RuleId, affected).Count(x => x.Status == ResultStatus.Fail);
            var flags = RegisterFlags.None;
            if (m.State == MeasureState.Done && open > 0) flags |= RegisterFlags.MeasureDoneButFailing;
            if (Retired(m.RuleId)) flags |= RegisterFlags.RuleRetired;
            bool overdue = RegisterRules.IsPastDue(m, date) && open > 0;
            return new MeasureView(m, overdue, flags, Title(m.RuleId), SeverityOf(m.RuleId), affected, open);
        }).ToList();

        return new RegisterViews { Exceptions = exceptions, Tailoring = tailoring, Measures = measures };
    }

    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
