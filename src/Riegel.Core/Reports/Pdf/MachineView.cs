// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;

namespace Riegel.Core.Reports.Pdf;

/// <summary>
/// Berichtsdaten für den Rechnerbericht (O5, <c>--per-machine</c>): dieselbe Gliederung wie der Anlagenbericht, auf
/// einen Rechner zugeschnitten. Zählungen, Werte, Befunde, Änderungen und Registersichten kommen aus dem Lauf des
/// Rechners bzw. aus den Einträgen, die auf ihn wirken; Kopf, Methode und Registerprüfsummen bleiben die der Anlage.
/// </summary>
internal static class MachineView
{
    public static ReportData For(ReportData d, MachineReport m)
    {
        var host = m.HostName;
        bool Affects(IReadOnlyList<string> hosts) => hosts.Contains(host, StringComparer.OrdinalIgnoreCase);
        var s = m.Summary;
        var measures = d.Measures.Where(v => Affects(v.AffectedHosts)).ToList();
        var exceptions = d.Exceptions.Where(v => Affects(v.AffectedHosts)).ToList();
        var expired = d.ExpiredExceptions.Where(v => Affects(v.AffectedHosts)).ToList();
        var tailoring = d.Tailoring.Where(v => Affects(v.AffectedHosts)).ToList();
        var measureById = d.Measures.GroupBy(v => v.Entry.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var findings = m.Results.Where(r => r.Result.Status == ResultStatus.Fail)
            .OrderByDescending(r => r.Result.Severity).ThenBy(r => r.Result.RuleId, StringComparer.Ordinal)
            .Select(r =>
            {
                MeasureSummary? measure = r.Result.MeasureId is { } id && measureById.TryGetValue(id, out var mv)
                    ? new MeasureSummary(id, mv.Entry.OwnerRole, mv.Entry.DueDate, mv.Overdue)
                    : null;
                return new AggregatedFinding(r.Result.RuleSetId, r.Result.RuleId, r.Title, r.Category, r.Result.Severity, 1, 1, [host],
                    false, measure, r.Result.Flags);
            }).ToList();

        bool dc = m.IsDomainController;
        var counts = new ReportCounts(1, dc ? 0 : 1, s.OpenHigh, s.OpenMedium, s.OpenLow, s.Regressions, s.NewlyOpen, s.NotCheckable,
            exceptions.Count(e => e.State is RegisterEntryState.Valid or RegisterEntryState.Expiring), s.ExceptionsExpiring,
            s.ExceptionsExpired, s.ExceptionsPending, tailoring.Count, measures.Count(v => v.Entry.State == MeasureState.Planned),
            s.MeasuresOverdue, m.Lifecycle?.Supported == false ? 1 : 0, (m.Flags & RunFlags.StaleSnapshot) != 0 ? 1 : 0, dc ? 1 : 0);
        bool incomplete = (m.Flags & RunFlags.Incomplete) != 0 || s.CheckedShare < 90 || !m.Collection.IsAdmin;
        var score = new ScoreView(dc ? null : s.Score, dc ? null : s.Documented, s.CheckedShare, incomplete,
            m.Categories.Any(c => c.Capped));
        var banners = d.Banners.Where(b => b.Hosts.Count == 0 || Affects(b.Hosts))
            .Select(b => b.Hosts.Count == 0 ? b : b with { Hosts = [host] }).ToList();
        if (incomplete && banners.All(b => b.Kind != BannerKind.IncompleteCheck))
            banners.Insert(0, new Banner(BannerKind.IncompleteCheck, s.CheckedShare, [host], null, []));

        var header = d.Header with { Hosts = [host] };
        var signOff = d.SignOff with { RunIds = [m.RunId] };
        return d with
        {
            Header = header,
            Banners = banners,
            Counts = counts,
            Score = score,
            Categories = m.Categories,
            Findings = findings,
            Changes = d.Changes.Where(c => string.Equals(c.HostName, host, StringComparison.OrdinalIgnoreCase)).ToList(),
            ExpiredExceptions = expired,
            Exceptions = exceptions,
            Tailoring = tailoring,
            Measures = measures,
            Machines = [m],
            InsurerFacts = d.InsurerFacts.Where(i => string.Equals(i.HostName, host, StringComparison.OrdinalIgnoreCase)).ToList(),
            SignOff = signOff,
        };
    }
}
