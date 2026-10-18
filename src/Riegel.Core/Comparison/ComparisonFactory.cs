// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Services;

namespace Riegel.Core.Comparison;

/// <summary>
/// Factory des Vergleichs (gehört ab dem Gerüst dem Paket workflow; Signatur fest). Der Platzhalter meldet nur
/// Statusänderungen schlechter/besser und neue Regeln sowie geänderte Einträge als ganze; die Rohwert-Abweichungen mit
/// flüchtiger Liste (spec 2.14) kommen mit dem Paket workflow.
/// </summary>
public static class ComparisonFactory
{
    public static IRunComparer Create(RiegelServices services) => new StubRunComparer();
}

/// <summary>Platzhalter (Gerüst).</summary>
internal sealed class StubRunComparer : IRunComparer
{
    public RunComparison Compare(Run? from, Snapshot? fromSnapshot, Run to, Snapshot toSnapshot, ComparisonBasis b)
    {
        var before = (from?.Results ?? []).ToDictionary(r => (r.RuleSetId, r.RuleId));
        var changes = new List<StatusChange>();
        foreach (var result in to.Results)
        {
            if (!before.TryGetValue((result.RuleSetId, result.RuleId), out var old))
            {
                if (from is not null)
                    changes.Add(new StatusChange(result.RuleSetId, result.RuleId, StatusChangeKind.NewRule, null, result.Status, ResultFlags.None, result.Flags, null));
                continue;
            }
            if (old.Status == result.Status) continue;
            var kind = result.Status == ResultStatus.Fail ? StatusChangeKind.Worse
                : old.Status == ResultStatus.Fail ? StatusChangeKind.Better : StatusChangeKind.Other;
            changes.Add(new StatusChange(result.RuleSetId, result.RuleId, kind, old.Status, result.Status, old.Flags, result.Flags, null));
        }
        return new RunComparison(to.HostName, from?.Id, to.Id, b, changes, [], false);
    }

    public SampleComparison CompareSnapshots(Snapshot packed, Snapshot fresh)
    {
        var a = packed.Items.ToDictionary(i => i.Key.ToLowerInvariant(), i => i.Value?.GetRawText());
        var b = fresh.Items.ToDictionary(i => i.Key.ToLowerInvariant(), i => i.Value?.GetRawText());
        var differences = new List<ValueDrift>();
        foreach (var key in a.Keys.Union(b.Keys).Order(StringComparer.Ordinal))
        {
            a.TryGetValue(key, out var x);
            b.TryGetValue(key, out var y);
            if (!a.ContainsKey(key)) differences.Add(new ValueDrift(key, null, null, y, DriftKind.Added));
            else if (!b.ContainsKey(key)) differences.Add(new ValueDrift(key, null, x, null, DriftKind.Removed));
            else if (!string.Equals(x, y, StringComparison.Ordinal)) differences.Add(new ValueDrift(key, null, x, y, DriftKind.Changed));
        }
        return new SampleComparison(fresh.Machine.HostName, differences.Count == 0, differences,
            Hashes.Sha256Hex(Model.Json.RiegelJson.SerializeToUtf8Bytes(packed)), Hashes.Sha256Hex(Model.Json.RiegelJson.SerializeToUtf8Bytes(fresh)),
            packed.CollectedAtUtc, fresh.CollectedAtUtc);
    }
}
