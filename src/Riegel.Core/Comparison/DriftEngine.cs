// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Core.Comparison;

/// <summary>
/// Rohwertvergleich zweier Exporte (spec 2.14): jeder Eintrag wird in Pfade mit kanonischen Werten zerlegt
/// (Registry: Wertname; WMI: Zeile/Eigenschaft mit der Kennung der Zeile, z. B. Dienstname; API-Listen: nach SID, Name
/// oder Schlüssel), dann werden die Pfade verglichen. Reihenfolgen in Listen ohne Bedeutung zählen nicht, die
/// flüchtige Liste (<see cref="VolatileList"/>) und die Rohdaten <c>raw:*</c> (abgeleitet in <c>parsed:*</c>) fehlen.
/// Ergebnis sortiert nach Eintrag und Pfad.
/// </summary>
internal static class DriftEngine
{
    /// <summary>Pfad für den Status eines Eintrags (ok, keyMissing, denied …).</summary>
    public const string StatusPath = "#status";

    /// <summary>Schlüssel der Abweichungen im Rechnerkopf.</summary>
    public const string HeaderKey = "machine";

    private sealed record FlatValue(string Path, string? Value, string? Type);

    private sealed record FlatItem(string Key, ItemStatus Status, Dictionary<string, FlatValue> Values);

    public static List<ValueDrift> Diff(Snapshot from, Snapshot to)
    {
        var drifts = new List<ValueDrift>();
        drifts.AddRange(Header(from.Machine, to.Machine));
        var a = Flatten(from);
        var b = Flatten(to);
        foreach (var key in a.Keys.Union(b.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            a.TryGetValue(key, out var x);
            b.TryGetValue(key, out var y);
            if (x is null)
            {
                drifts.Add(new ValueDrift(y!.Key, null, null, StatusName(y.Status), DriftKind.Added));
                continue;
            }
            if (y is null)
            {
                drifts.Add(new ValueDrift(x.Key, null, StatusName(x.Status), null, DriftKind.Removed));
                continue;
            }
            if (x.Status != y.Status && (x.Values.Count == 1 || y.Values.Count == 1))
            {
                // eine Seite ohne Wert (z. B. jetzt „denied“): nur der Status, nicht jeder Wert als „entfernt“
                drifts.Add(new ValueDrift(y.Key, StatusPath, x.Values[StatusPath].Value, y.Values[StatusPath].Value, DriftKind.Changed));
                continue;
            }
            foreach (var path in x.Values.Keys.Union(y.Values.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            {
                x.Values.TryGetValue(path, out var vx);
                y.Values.TryGetValue(path, out var vy);
                if (vx is null) drifts.Add(new ValueDrift(y.Key, vy!.Path, null, vy.Value, DriftKind.Added));
                else if (vy is null) drifts.Add(new ValueDrift(y.Key, vx.Path, vx.Value, null, DriftKind.Removed));
                else if (!Same(vx, vy))
                {
                    bool typeOnly = CanonicalValue.AreEqual(vx.Value, vy.Value);
                    drifts.Add(new ValueDrift(y.Key, vy.Path, typeOnly ? $"{vx.Value} ({vx.Type})" : vx.Value,
                        typeOnly ? $"{vy.Value} ({vy.Type})" : vy.Value, DriftKind.Changed));
                }
            }
        }
        return drifts;
    }

    private static bool Same(FlatValue a, FlatValue b) =>
        CanonicalValue.AreEqual(a.Value, b.Value) && string.Equals(a.Type, b.Type, StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- Rechnerkopf

    private static IEnumerable<ValueDrift> Header(MachineInfo a, MachineInfo b)
    {
        static string? I(int? v) => v?.ToString(CultureInfo.InvariantCulture);
        var fields = new (string Name, string? From, string? To)[]
        {
            ("hostName", a.HostName, b.HostName), ("dnsDomain", a.DnsDomain, b.DnsDomain), ("domainRole", I(a.DomainRole), I(b.DomainRole)),
            ("productType", I(a.ProductType), I(b.ProductType)), ("build", I(a.Build), I(b.Build)), ("ubr", I(a.Ubr), I(b.Ubr)),
            ("editionId", a.EditionId, b.EditionId), ("installationType", a.InstallationType, b.InstallationType),
            ("displayVersion", a.DisplayVersion, b.DisplayVersion), ("architecture", a.Architecture, b.Architecture),
            ("machineGuid", a.MachineGuid, b.MachineGuid), ("osLanguage", a.OsLanguage, b.OsLanguage),
        };
        foreach (var (name, from, to) in fields)
        {
            if (VolatileList.HeaderFields.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) continue;
            var kind = from is null ? DriftKind.Added : to is null ? DriftKind.Removed : DriftKind.Changed;
            yield return new ValueDrift(HeaderKey, name, from, to, kind);
        }
    }

    // ---------------------------------------------------------------- Zerlegen

    private static Dictionary<string, FlatItem> Flatten(Snapshot s)
    {
        var items = new Dictionary<string, FlatItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in s.Items)
        {
            if (item.Key.StartsWith("raw:", StringComparison.OrdinalIgnoreCase) || VolatileList.IsVolatileItem(item.Key)) continue;
            var values = new Dictionary<string, FlatValue>(StringComparer.OrdinalIgnoreCase)
            {
                [StatusPath] = new(StatusPath, StatusName(item.Status) + (item.Truncated ? " (truncated)" : ""), null),
            };
            if (item.Value is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } v)
            {
                if (item.Key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase)) Registry(v, values);
                else if (item.Key.StartsWith("api:localgroup:", StringComparison.OrdinalIgnoreCase)) Members(v, values);
                else if (item.Key.StartsWith("wmi:", StringComparison.OrdinalIgnoreCase)) Wmi(item.Key, v, values);
                else Generic("", v, values);
            }
            foreach (var path in values.Keys.Where(p => p != StatusPath && VolatileList.IsVolatilePath(p)).ToList()) values.Remove(path);
            // derselbe Eintrag zweimal (feindliche Eingabe): der erste zählt
            items.TryAdd(item.Key, new FlatItem(item.Key, item.Status, values));
        }
        return items;
    }

    private static void Registry(JsonElement v, Dictionary<string, FlatValue> values)
    {
        if (v.ValueKind != JsonValueKind.Object) { Add(values, "value", CanonicalValue.Render(v), null); return; }
        if (v.TryGetProperty("values", out var named) && named.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in named.EnumerateObject())
            {
                var type = p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString() : null;
                Add(values, p.Name.Length == 0 ? "(default)" : p.Name, CanonicalValue.RenderRegistry(p.Value), type);
            }
        }
        if (v.TryGetProperty("subkeys", out var subkeys) && subkeys.ValueKind == JsonValueKind.Array)
            foreach (var s in subkeys.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.String))
                Add(values, "\\" + s.GetString(), "present", null);
    }

    private static void Members(JsonElement v, Dictionary<string, FlatValue> values)
    {
        if (v.ValueKind != JsonValueKind.Array) { Generic("", v, values); return; }
        foreach (var member in v.EnumerateArray())
        {
            var sid = member.ValueKind == JsonValueKind.Object && member.TryGetProperty("sid", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString() : null;
            if (sid is not null) Add(values, sid, sid, null);
            else Add(values, "#" + CanonicalValue.Render(member), "present", null);
        }
    }

    private static void Wmi(string key, JsonElement v, Dictionary<string, FlatValue> values)
    {
        if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            Rows("", rows, values);
            return;
        }
        if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("out", out var output))
        {
            Generic("", output, values);
            return;
        }
        Generic("", v, values);
    }

    private static void Generic(string prefix, JsonElement v, Dictionary<string, FlatValue> values)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in v.EnumerateObject()) Generic(Join(prefix, p.Name), p.Value, values);
                break;
            case JsonValueKind.Array:
                var elements = v.EnumerateArray().ToList();
                if (elements.Count > 0 && elements.All(e => e.ValueKind == JsonValueKind.Object) && elements.All(e => Identity(e) is not null))
                    Rows(prefix, v, values);
                else if (elements.All(e => e.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)))
                    Add(values, prefix.Length == 0 ? "value" : prefix, SortedList(elements), null);
                else
                    Add(values, prefix.Length == 0 ? "value" : prefix, CanonicalValue.Render(v), null);
                break;
            default:
                Add(values, prefix.Length == 0 ? "value" : prefix, CanonicalValue.Render(v), null);
                break;
        }
    }

    /// <summary>Zeilen nach ihrer Kennung; doppelte Kennungen bekommen „#2“, „#3“ … in Reihenfolge.</summary>
    private static void Rows(string prefix, JsonElement rows, Dictionary<string, FlatValue> values)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (var row in rows.EnumerateArray())
        {
            index++;
            var id = row.ValueKind == JsonValueKind.Object ? Identity(row) : null;
            id ??= "#" + index.ToString(CultureInfo.InvariantCulture);
            seen[id] = seen.TryGetValue(id, out var n) ? n + 1 : 1;
            if (seen[id] > 1) id += "#" + seen[id].ToString(CultureInfo.InvariantCulture);
            var rowPrefix = Join(prefix, id);
            if (row.ValueKind == JsonValueKind.Object)
                foreach (var p in row.EnumerateObject()) Generic(Join(rowPrefix, p.Name), p.Value, values);
            else
                Add(values, rowPrefix, CanonicalValue.Render(row), null);
        }
    }

    /// <summary>
    /// Kennung einer Zeile: SID; Schlüssel und Wertname (Registry.pol, gpresult); GUID; HotFixID, SettingID,
    /// InstanceName, DriveLetter; Speicher und Name (Firewall-Regeln); Name; id. Null = keine Kennung.
    /// </summary>
    private static string? Identity(JsonElement row)
    {
        string? Text(params string[] names)
        {
            foreach (var name in names)
                foreach (var p in row.EnumerateObject())
                    if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                        return CanonicalValue.Render(p.Value);
            return null;
        }
        if (Text("sid") is { } sid) return sid;
        if (Text("key") is { } key && Text("value") is { } value) return key + "|" + value;
        if (Text("guid") is { } guid) return guid;
        if (Text("HotFixID", "SettingID", "InstanceName", "DriveLetter") is { } special) return special;
        if (Text("store") is { } store && Text("name") is { } named) return store + "|" + named;
        if (Text("name", "displayName") is { } name) return name;
        return Text("id");
    }

    private static string SortedList(List<JsonElement> elements) =>
        "[" + string.Join(",", elements.Select(e => CanonicalValue.Render(e) ?? "null").Order(StringComparer.Ordinal)) + "]";

    private static void Add(Dictionary<string, FlatValue> values, string path, string? value, string? type) =>
        values.TryAdd(path, new FlatValue(path, value, type));

    private static string Join(string prefix, string name) => prefix.Length == 0 ? name : prefix + "/" + name;

    internal static string StatusName(ItemStatus status) => JsonNamingPolicy.CamelCase.ConvertName(status.ToString());
}
