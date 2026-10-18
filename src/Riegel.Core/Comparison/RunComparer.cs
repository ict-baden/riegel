// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;

namespace Riegel.Core.Comparison;

/// <summary>
/// Vergleich zweier Stände eines Rechners (spec 2.14): Statusänderungen je Regel (schlechter, besser, Ausnahme
/// hinzu/weg/abgelaufen, Zuschnitt hinzu/weg, neue Regel, „Prüfumfang geändert“), Rohwert-Abweichungen ohne die
/// flüchtige Liste und der Wechsel der MachineGuid. Für die Stichprobe des Prüfers werden zwei Exporte verglichen.
/// </summary>
internal sealed class RunComparer : IRunComparer
{
    public RunComparison Compare(Run? from, Snapshot? fromSnapshot, Run to, Snapshot toSnapshot, ComparisonBasis b)
    {
        var changes = new List<StatusChange>();
        if (from is not null)
        {
            bool scopeChanged = ScopeChanged(from, to);
            var before = from.Results.GroupBy(r => (r.RuleSetId, r.RuleId)).ToDictionary(g => g.Key, g => g.First());
            var seen = new HashSet<(string, string)>();
            foreach (var now in to.Results)
            {
                seen.Add((now.RuleSetId, now.RuleId));
                if (!before.TryGetValue((now.RuleSetId, now.RuleId), out var old))
                {
                    changes.Add(new StatusChange(now.RuleSetId, now.RuleId, StatusChangeKind.NewRule, null, now.Status, ResultFlags.None, now.Flags, null));
                    continue;
                }
                if (Classify(old, now, scopeChanged) is { } change) changes.Add(change);
            }
            foreach (var old in from.Results.Where(r => !seen.Contains((r.RuleSetId, r.RuleId))))
                changes.Add(new StatusChange(old.RuleSetId, old.RuleId, StatusChangeKind.Other, old.Status, null, old.Flags, ResultFlags.None, null));
        }
        var drift = fromSnapshot is null ? [] : DriftEngine.Diff(fromSnapshot, toSnapshot);
        bool identityChanged = fromSnapshot is not null
            ? GuidChanged(fromSnapshot.Machine.MachineGuid, toSnapshot.Machine.MachineGuid)
            : b == ComparisonBasis.Previous && (to.Flags & RunFlags.MachineIdentityChanged) != 0;
        return new RunComparison(to.HostName, from?.Id, to.Id, b,
            [.. changes.OrderBy(c => c.RuleSetId, StringComparer.Ordinal).ThenBy(c => c.RuleId, StringComparer.Ordinal)], drift, identityChanged);
    }

    public SampleComparison CompareSnapshots(Snapshot packed, Snapshot fresh)
    {
        var differences = DriftEngine.Diff(packed, fresh);
        return new SampleComparison(fresh.Machine.HostName, differences.Count == 0, differences,
            Hashes.Sha256Hex(RiegelJson.SerializeToUtf8Bytes(packed)), Hashes.Sha256Hex(RiegelJson.SerializeToUtf8Bytes(fresh)),
            packed.CollectedAtUtc, fresh.CollectedAtUtc);
    }

    /// <summary>Hat sich die MachineGuid geändert (beide bekannt)? „Rechner neu aufgesetzt?“ (spec 2.8).</summary>
    internal static bool GuidChanged(string? before, string? now) =>
        !string.IsNullOrWhiteSpace(before) && !string.IsNullOrWhiteSpace(now) &&
        !string.Equals(before.Trim(), now.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>„Prüfumfang geändert“: mit/ohne Admin oder --minimal/--deep anders erfasst.</summary>
    internal static bool ScopeChanged(Run from, Run to) =>
        from.IsAdmin != to.IsAdmin || from.Options.Minimal != to.Options.Minimal || from.Options.Deep != to.Options.Deep;

    /// <summary>Art einer Statusänderung; null = keine Änderung.</summary>
    internal static StatusChange? Classify(RuleResult old, RuleResult now, bool scopeChanged)
    {
        if (old.Status == now.Status) return null;
        var (o, n) = (old.Status, now.Status);
        StatusChange Change(StatusChangeKind kind, string? detail) =>
            new(now.RuleSetId, now.RuleId, kind, o, n, old.Flags, now.Flags, detail);

        if (n == ResultStatus.OutOfScope) return Change(StatusChangeKind.TailoringAdded, now.TailoringId);
        if (o == ResultStatus.OutOfScope) return Change(StatusChangeKind.TailoringRemoved, old.TailoringId);
        if (n == ResultStatus.Exception && o is ResultStatus.Fail or ResultStatus.Pass or ResultStatus.NotApplicable)
            return Change(StatusChangeKind.ExceptionAdded, now.ExceptionId);
        if (o == ResultStatus.Exception && n == ResultStatus.Fail)
        {
            if ((now.Flags & ResultFlags.ExceptionExpired) != 0) return Change(StatusChangeKind.ExceptionExpired, now.ExceptionId ?? old.ExceptionId);
            if ((now.Flags & ResultFlags.ExceptionValueMismatch) != 0) return Change(StatusChangeKind.Worse, now.ExceptionId);
            return Change(StatusChangeKind.ExceptionRemoved, old.ExceptionId);
        }
        if (n == ResultStatus.Fail && o is ResultStatus.Pass or ResultStatus.NotApplicable) return Change(StatusChangeKind.Worse, null);
        if (o is ResultStatus.Fail or ResultStatus.Exception && n is ResultStatus.Pass or ResultStatus.NotApplicable)
            return Change(StatusChangeKind.Better, o == ResultStatus.Exception ? old.ExceptionId : null);
        if (scopeChanged && (o == ResultStatus.NotCheckable || n == ResultStatus.NotCheckable)) return Change(StatusChangeKind.ScopeChanged, null);
        return Change(StatusChangeKind.Other, null);
    }
}
