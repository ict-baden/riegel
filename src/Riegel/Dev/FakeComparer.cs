// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;

namespace Riegel.Dev;

/// <summary>
/// Nur Entwicklung (RIEGEL_FAKE_SERVICES=1, Demo-Anlage): Vergleich mit Statusänderungen nach spec 2.14 und einer
/// einfachen Abweichung der Rohwerte (Registry-Werte, WMI-Zeilen, lokale Administratoren) ohne flüchtige Werte. Der
/// echte Vergleich kommt mit dem Paket workflow.
/// </summary>
internal sealed class FakeComparer : IRunComparer
{
    /// <summary>Flüchtige Eigenschaften, die keine Abweichung sind (Signaturen, Zeiten, Laufzeiten).</summary>
    private static readonly HashSet<string> Volatile = new(StringComparer.OrdinalIgnoreCase)
    {
        "AntivirusSignatureAge", "AntivirusSignatureLastUpdated", "AMProductVersion", "LastBootUpTime", "timestamp", "timeCreatedUtc",
        "bootTimeUtc", "lastLogonDate", "passwordAgeDays", "productState",
    };

    public RunComparison Compare(Run? from, Snapshot? fromSnapshot, Run to, Snapshot toSnapshot, ComparisonBasis b)
    {
        var before = (from?.Results ?? []).ToDictionary(r => (r.RuleSetId, r.RuleId));
        var changes = new List<StatusChange>();
        if (from is not null && (from.IsAdmin != to.IsAdmin || from.Options.Minimal != to.Options.Minimal))
            changes.Add(new StatusChange("riegel", "", StatusChangeKind.ScopeChanged, null, null, ResultFlags.None, ResultFlags.None,
                $"{(from.IsAdmin ? "admin" : "user")} → {(to.IsAdmin ? "admin" : "user")}"));
        foreach (var r in to.Results)
        {
            if (!before.TryGetValue((r.RuleSetId, r.RuleId), out var old))
            {
                if (from is not null) changes.Add(new(r.RuleSetId, r.RuleId, StatusChangeKind.NewRule, null, r.Status, ResultFlags.None, r.Flags, null));
                continue;
            }
            if (old.Status == r.Status) continue;
            var kind = (old.Status, r.Status) switch
            {
                (_, ResultStatus.Fail) when (r.Flags & ResultFlags.ExceptionExpired) != 0 => StatusChangeKind.ExceptionExpired,
                (ResultStatus.Exception, ResultStatus.Fail) => StatusChangeKind.ExceptionRemoved,
                (ResultStatus.Pass or ResultStatus.NotApplicable or ResultStatus.OutOfScope, ResultStatus.Fail) => StatusChangeKind.Worse,
                (ResultStatus.Fail, ResultStatus.Exception) => StatusChangeKind.ExceptionAdded,
                (_, ResultStatus.OutOfScope) => StatusChangeKind.TailoringAdded,
                (ResultStatus.OutOfScope, _) => StatusChangeKind.TailoringRemoved,
                (ResultStatus.Fail, ResultStatus.Pass) => StatusChangeKind.Better,
                _ => StatusChangeKind.Other,
            };
            changes.Add(new(r.RuleSetId, r.RuleId, kind, old.Status, r.Status, old.Flags, r.Flags,
                kind == StatusChangeKind.ExceptionExpired ? r.ExceptionId : null));
        }
        var drift = fromSnapshot is null ? [] : Drift(fromSnapshot, toSnapshot);
        bool identity = fromSnapshot?.Machine.MachineGuid is { } a && toSnapshot.Machine.MachineGuid is { } bGuid
                        && !string.Equals(a, bGuid, StringComparison.OrdinalIgnoreCase);
        return new RunComparison(to.HostName, from?.Id, to.Id, b, changes, drift, identity);
    }

    public SampleComparison CompareSnapshots(Snapshot packed, Snapshot fresh)
    {
        var differences = Drift(packed, fresh);
        return new SampleComparison(fresh.Machine.HostName, differences.Count == 0, differences,
            Hashes.Sha256Hex(RiegelJson.SerializeToUtf8Bytes(packed)), Hashes.Sha256Hex(RiegelJson.SerializeToUtf8Bytes(fresh)),
            packed.CollectedAtUtc, fresh.CollectedAtUtc);
    }

    /// <summary>Normalisierte Abweichung zweier Exporte: je Registry-Wert, je WMI-Zeile und Eigenschaft, je Gruppenmitglied.</summary>
    internal static IReadOnlyList<ValueDrift> Drift(Snapshot a, Snapshot b)
    {
        var x = Flatten(a);
        var y = Flatten(b);
        var drift = new List<ValueDrift>();
        foreach (var key in x.Keys.Union(y.Keys).Order(StringComparer.OrdinalIgnoreCase))
        {
            x.TryGetValue(key, out var from);
            y.TryGetValue(key, out var to);
            if (string.Equals(from?.Value, to?.Value, StringComparison.Ordinal)) continue;
            var (item, path) = from is not null ? (from.Item, from.Path) : (to!.Item, to.Path);
            drift.Add(new ValueDrift(item, path, from?.Value, to?.Value,
                from is null ? DriftKind.Added : to is null ? DriftKind.Removed : DriftKind.Changed));
        }
        return drift;
    }

    private sealed record Entry(string Item, string Path, string? Value);

    private static Dictionary<string, Entry> Flatten(Snapshot s)
    {
        var map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in s.Items)
        {
            if (item.Status != ItemStatus.Ok || item.Value is not { } v) continue;
            if (item.Key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase) && v.TryGetProperty("values", out var values))
            {
                foreach (var p in values.EnumerateObject())
                    map[item.Key + "|" + p.Name] = new(item.Key, p.Name, CanonicalValue.RenderRegistry(p.Value));
            }
            else if (item.Key.StartsWith("wmi:", StringComparison.OrdinalIgnoreCase) && v.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (var row in rows.EnumerateArray())
                {
                    var id = RowId(row) ?? (index++).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    foreach (var p in row.EnumerateObject())
                    {
                        if (Volatile.Contains(p.Name) || p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) continue;
                        map[item.Key + "|" + id + "/" + p.Name] = new(item.Key, id + "/" + p.Name, CanonicalValue.Render(p.Value));
                    }
                }
            }
            else if (item.Key.StartsWith("api:localgroup:", StringComparison.OrdinalIgnoreCase) && v.ValueKind == JsonValueKind.Array)
            {
                foreach (var member in v.EnumerateArray())
                    if (member.TryGetProperty("sid", out var sid) && sid.GetString() is { } text)
                        map[item.Key + "|" + text] = new(item.Key, text, member.TryGetProperty("name", out var n) ? n.GetString() ?? text : text);
            }
        }
        return map;
    }

    private static string? RowId(JsonElement row)
    {
        foreach (var name in new[] { "Name", "HotFixID", "SID", "DriveLetter", "InstanceName", "SettingID", "GUID", "displayName" })
            if (row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();
        return null;
    }
}
