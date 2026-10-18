// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Dev;

/// <summary>
/// Nur Entwicklung (RIEGEL_FAKE_SERVICES=1, Demo-Anlage): bequemer Lesezugriff der nachgebildeten Engine auf einen
/// Export. Folgt grob den Belegregeln aus spec 2.4 (vorhanden / nachweislich nicht gesetzt / nicht verfügbar), ist aber
/// keine Engine – die echte kommt mit dem Paket engine und ersetzt diese Nachbildung vollständig.
/// </summary>
internal sealed class FakeView
{
    private readonly Dictionary<string, SnapshotItem> _items;

    public FakeView(Snapshot snapshot, EvaluationContext context, IReadOnlyDictionary<string, JsonElement> facts)
    {
        Snapshot = snapshot;
        Context = context;
        Facts = facts;
        _items = new Dictionary<string, SnapshotItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in snapshot.Items) _items[item.Key] = item;
        Gpo = ReadGpoAssignments();
    }

    public Snapshot Snapshot { get; }

    public EvaluationContext Context { get; }

    public IReadOnlyDictionary<string, JsonElement> Facts { get; }

    public bool German => Context.Language.StartsWith("de", StringComparison.OrdinalIgnoreCase);

    /// <summary>Text in Deutsch oder Englisch (die Nachbildung kennt nur diese beiden).</summary>
    public string T(string en, string de) => German ? de : en;

    public bool IsServer => Snapshot.Machine.ProductType is 2 or 3;

    public int Build => Snapshot.Machine.Build ?? 0;

    public bool IsBuiltin => Snapshot.Origin == SnapshotOrigin.Builtin;

    public string? Fact(string name) =>
        Facts.TryGetValue(name, out var v) && v.ValueKind != JsonValueKind.Null ? CanonicalValue.Render(v) : null;

    /// <summary>Zuordnung Registry-Wert → Domänen-GPO aus parsed:gpresult (experimentell, spec 2.10).</summary>
    private Dictionary<string, string> Gpo { get; }

    // ---------------------------------------------------------------- Einträge

    public SnapshotItem? Item(string key) => _items.GetValueOrDefault(key);

    /// <summary>Eintrag gelesen (Status ok mit Wert)?</summary>
    public bool TryValue(string key, out JsonElement value)
    {
        value = default;
        if (Item(key) is not { Status: ItemStatus.Ok, Value: { } v }) return false;
        value = v;
        return true;
    }

    /// <summary>Zeilen eines WMI-Eintrags; null = nicht verfügbar.</summary>
    public IReadOnlyList<JsonElement>? Rows(string key) =>
        TryValue(key, out var v) && v.ValueKind == JsonValueKind.Object && v.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array
            ? [.. rows.EnumerateArray()]
            : null;

    /// <summary>Ausgabe einer statischen WMI-Methode; null = nicht verfügbar.</summary>
    public JsonElement? Out(string key) =>
        TryValue(key, out var v) && v.ValueKind == JsonValueKind.Object && v.TryGetProperty("out", out var o) ? o : null;

    public ItemStatus? StatusOf(string key) =>
        Item(key)?.Status ?? (Covered(key) ? ItemStatus.KeyMissing : ItemStatus.NotCollected);

    // ---------------------------------------------------------------- Registry

    public enum Evidence { Present, NotSet, Unavailable }

    public readonly record struct RegRead(Evidence Evidence, JsonElement Value, string? Type, ItemStatus Status, string Key);

    /// <summary>Einen Registry-Wert lesen (Belegklassen nach spec 2.4 Punkt 1, vereinfacht).</summary>
    public RegRead Reg(string path, string name)
    {
        var key = ItemKeys.Registry(path);
        var item = Item(key);
        if (item is null)
            return Covered(key) ? new(Evidence.NotSet, default, null, ItemStatus.KeyMissing, key) : new(Evidence.Unavailable, default, null, ItemStatus.NotCollected, key);
        switch (item.Status)
        {
            case ItemStatus.Ok when item.Value is { } v && v.TryGetProperty("values", out var values):
                foreach (var p in values.EnumerateObject())
                {
                    if (!p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    var type = p.Value.TryGetProperty("t", out var t) ? t.GetString() : null;
                    if (p.Value.TryGetProperty("redacted", out var r) && r.ValueKind == JsonValueKind.True)
                        return new(Evidence.Present, JsonValues.Text("(redacted)"), type, item.Status, key);
                    return p.Value.TryGetProperty("v", out var value)
                        ? new(Evidence.Present, value, type, item.Status, key)
                        : new(Evidence.NotSet, default, type, item.Status, key);
                }
                return new(Evidence.NotSet, default, null, item.Status, key);
            case ItemStatus.Ok:
            case ItemStatus.KeyMissing:
            case ItemStatus.ValueMissing:
                return new(Evidence.NotSet, default, null, item.Status, key);
            default:
                return new(Evidence.Unavailable, default, null, item.Status, key);
        }
    }

    public bool RegHas(string path, string name) =>
        Item(ItemKeys.Registry(path)) is { Status: ItemStatus.Ok, Value: { } v } && v.TryGetProperty("values", out var values)
        && values.EnumerateObject().Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Herkunft eines Registry-Werts (spec 2.10, vereinfacht).</summary>
    public (ValueSource Source, string? Detail) SourceOf(string path, string name)
    {
        var p = path.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase) ? path : @"HKLM\" + path;
        if (Gpo.TryGetValue(p.ToLowerInvariant() + "|" + name.ToLowerInvariant(), out var gpo)) return (ValueSource.DomainGpo, gpo);
        bool policy = p.StartsWith(@"HKLM\SOFTWARE\Policies", StringComparison.OrdinalIgnoreCase)
                      || p.StartsWith(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies", StringComparison.OrdinalIgnoreCase);
        if (p.StartsWith(@"HKLM\SOFTWARE\Microsoft\PolicyManager", StringComparison.OrdinalIgnoreCase)) return (ValueSource.Mdm, null);
        if (policy) return (Snapshot.IsAdmin ? ValueSource.LocalPolicy : ValueSource.Unknown, null);
        return (ValueSource.DirectlySet, null);
    }

    private bool Covered(string key)
    {
        if (!key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase)) return false;
        var path = key[4..];
        foreach (var c in Snapshot.Coverage)
        {
            if (!c.Complete) continue;
            if (path.Equals(c.Root, StringComparison.OrdinalIgnoreCase)) return true;
            if (c.Recursive && path.StartsWith(c.Root + @"\", StringComparison.OrdinalIgnoreCase)
                && !c.DeniedSubkeys.Any(d => path.StartsWith(d, StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    private Dictionary<string, string> ReadGpoAssignments()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!TryValue(ItemKeys.ParsedGpresult, out var gp) || !gp.TryGetProperty("registry", out var registry)) return map;
        foreach (var entry in registry.EnumerateArray())
        {
            var key = entry.GetProperty("key").GetString() ?? "";
            var value = entry.GetProperty("value").GetString() ?? "";
            var gpo = entry.GetProperty("gpo").GetString() ?? "";
            map[key.ToLowerInvariant() + "|" + value.ToLowerInvariant()] = gpo;
        }
        return map;
    }

    // ---------------------------------------------------------------- Werte

    public static string? Canon(JsonElement value) => CanonicalValue.Render(value);

    public static long? Number(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt64(out var n) => n,
        JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
        _ => null,
    };

    public static string? Text(JsonElement row, string property) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static long? Num(JsonElement row, string property) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(property, out var v) ? Number(v) : null;

    public static bool? Bool(JsonElement row, string property) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(property, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.ValueKind == JsonValueKind.True
            : null;

    public static DateTimeOffset? Date(JsonElement row, string property) =>
        Text(row, property) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}
