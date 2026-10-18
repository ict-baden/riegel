// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Core.Evaluation;

/// <summary>Beweisklasse eines gelesenen Eintrags (spec 2.4 Punkt 1).</summary>
internal enum EvidenceClass
{
    /// <summary>Eintrag Ok und Wert vorhanden.</summary>
    Present,

    /// <summary>Nachweislich nicht gesetzt: KeyMissing oder Wert fehlt, innerhalb einer vollständigen Abdeckung.</summary>
    NotSet,

    /// <summary>Alles andere: verweigert, Fehler, nicht erfasst, abgeschnitten, außerhalb der Abdeckung …</summary>
    Unavailable,
}

/// <summary>Ein Eintrag (wmi:, api:, evt:, raw:, parsed:) mit Beweisklasse.</summary>
/// <param name="Reason">RGL-Code, wenn nicht Present.</param>
internal sealed record ItemEvidence(string Key, EvidenceClass Class, ItemStatus? Status, JsonElement Value, string? Reason)
{
    public bool IsPresent => Class == EvidenceClass.Present;
}

/// <summary>Ein Registry-Schlüssel mit Beweisklasse; bei Present ist <see cref="Value"/> das Objekt <c>{ values, subkeys }</c>.</summary>
internal sealed record RegKeyEvidence(string Key, EvidenceClass Class, JsonElement Value, string? Reason)
{
    /// <summary>Unterschlüssel des vorhandenen Schlüssels.</summary>
    public IEnumerable<string> Subkeys => Class == EvidenceClass.Present && JsonRead.TryGet(Value, "subkeys", out var s)
        ? s.ValueKind == JsonValueKind.Array ? s.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!) : []
        : [];
}

/// <summary>Ein Registry-Wert mit Beweisklasse; bei Present Typ und Rohwert der Snapshot-Kodierung.</summary>
/// <param name="Raw">Das Objekt <c>{ t, v, redacted?, empty?, truncated? }</c> (für die kanonische Darstellung).</param>
internal sealed record RegValueEvidence(string ItemKey, string ValueName, EvidenceClass Class, string? Type, JsonElement? V,
    JsonElement? Raw, bool Redacted, bool RedactedEmpty, string? Reason)
{
    public bool IsPresent => Class == EvidenceClass.Present;
}

/// <summary>
/// Zugriff auf die Einträge eines Exports mit den Regeln von spec 2.4 und 7.3: welche Klasse ein Beleg hat, was
/// innerhalb der Abdeckung „nicht gesetzt“ heißt und was „nicht erfasst“. Schlüssel ohne Groß/klein. Jeder Zugriff
/// wird an <see cref="Observer"/> gemeldet (gelesene Belege, entscheidende Belege).
/// </summary>
internal sealed class EvidenceStore
{
    private readonly Dictionary<string, SnapshotItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _conflicts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Root, CoverageEntry Entry)> _coverage = [];

    public EvidenceStore(Snapshot snapshot)
    {
        Snapshot = snapshot;
        foreach (var item in snapshot.Items)
        {
            if (item?.Key is null) continue;
            // Doppelte Schlüssel sind ein Widerspruch im Export: dann gilt keiner von beiden als Beleg
            if (!_items.TryAdd(item.Key, item)) _conflicts.Add(item.Key);
        }
        foreach (var entry in snapshot.Coverage)
            if (entry?.Root is not null) _coverage.Add((NormalizePath(entry.Root), entry));
    }

    public Snapshot Snapshot { get; }

    /// <summary>Wird bei jedem Lesezugriff mit dem Schlüssel des Eintrags aufgerufen.</summary>
    public Action<string>? Observer { get; set; }

    /// <summary>Schreibweise des Schlüssels wie im Export, sonst wie übergeben.</summary>
    public string Canonical(string key) => _items.TryGetValue(key, out var item) ? item.Key : key;

    /// <summary>Roh-Eintrag (ohne Bewertung), null wenn nicht vorhanden.</summary>
    public SnapshotItem? Raw(string key) => _items.TryGetValue(key, out var item) && !_conflicts.Contains(key) ? item : null;

    // ---------------------------------------------------------------- allgemeine Einträge

    /// <summary>Ein Eintrag wmi:/api:/evt:/raw:/parsed:. Nur Ok mit Wert ist Present; KeyMissing gilt hier als nicht beweisbar.</summary>
    public ItemEvidence Item(string key)
    {
        Observer?.Invoke(Canonical(key));
        if (_conflicts.Contains(key)) return new(key, EvidenceClass.Unavailable, ItemStatus.Error, default, RglCodesEngine.UnexpectedValue);
        if (!_items.TryGetValue(key, out var item))
            return new(key, EvidenceClass.Unavailable, ItemStatus.NotCollected, default, RglCodesEngine.ItemNotCollected);
        if (item.Truncated) return new(item.Key, EvidenceClass.Unavailable, item.Status, default, RglCodesEngine.ItemTruncated);
        if (item.Status == ItemStatus.Ok)
        {
            if (item.Value is not { } v || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return new(item.Key, EvidenceClass.Unavailable, item.Status, default, RglCodesEngine.UnexpectedValue);
            return new(item.Key, EvidenceClass.Present, item.Status, v, null);
        }
        return new(item.Key, EvidenceClass.Unavailable, item.Status, default, ReasonFor(item.Status));
    }

    /// <summary>RGL-Code je Eintragsstatus (spec 2.4 Punkt 2).</summary>
    public static string ReasonFor(ItemStatus status) => status switch
    {
        ItemStatus.Denied => RglCodesEngine.ItemDenied,
        ItemStatus.Error => RglCodesEngine.ItemError,
        ItemStatus.NotCollected => RglCodesEngine.ItemNotCollected,
        ItemStatus.NamespaceMissing => RglCodesEngine.ItemNamespaceMissing,
        ItemStatus.NotApplicable => RglCodesEngine.ItemNotApplicable,
        ItemStatus.KeyMissing or ItemStatus.ValueMissing => RglCodesEngine.AbsenceNotProvable,
        _ => RglCodesEngine.UnexpectedValue,
    };

    // ---------------------------------------------------------------- Registry

    /// <summary>„HKLM\…“ ohne „reg:“ und ohne „\“ am Ende.</summary>
    public static string NormalizePath(string key)
    {
        var k = key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase) ? key[4..] : key;
        k = k.Trim().TrimEnd('\\');
        if (k.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase)) k = @"HKLM\" + k[19..];
        return k.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase) ? @"HKLM\" + k[5..] : @"HKLM\" + k;
    }

    /// <summary>Schlüssel des Eintrags zu einem Registry-Pfad.</summary>
    public static string RegistryItemKey(string path) => "reg:" + NormalizePath(path);

    /// <summary>Ein Registry-Schlüssel: vorhanden, nachweislich nicht vorhanden oder nicht beweisbar.</summary>
    public RegKeyEvidence RegistryKey(string key)
    {
        var path = NormalizePath(key);
        var itemKey = "reg:" + path;
        Observer?.Invoke(Canonical(itemKey));
        if (_conflicts.Contains(itemKey)) return new(itemKey, EvidenceClass.Unavailable, default, RglCodesEngine.UnexpectedValue);
        if (_items.TryGetValue(itemKey, out var item))
        {
            itemKey = item.Key;
            if (item.Truncated) return new(itemKey, EvidenceClass.Unavailable, default, RglCodesEngine.ItemTruncated);
            switch (item.Status)
            {
                case ItemStatus.Ok:
                case ItemStatus.ValueMissing:
                    if (item.Value is not { ValueKind: JsonValueKind.Object } v)
                        return new(itemKey, EvidenceClass.Unavailable, default, RglCodesEngine.UnexpectedValue);
                    return new(itemKey, EvidenceClass.Present, v, null);
                case ItemStatus.KeyMissing:
                {
                    var reason = AbsenceProof(path, requireRecursive: false);
                    return reason is null
                        ? new(itemKey, EvidenceClass.NotSet, default, null)
                        : new(itemKey, EvidenceClass.Unavailable, default, reason);
                }
                default:
                    return new(itemKey, EvidenceClass.Unavailable, default, ReasonFor(item.Status));
            }
        }
        // Kein Eintrag: nur innerhalb eines vollständigen rekursiven Bereichs heißt das „nicht vorhanden“ (spec 7.3)
        var proof = AbsenceProof(path, requireRecursive: true);
        if (proof is not null) return new(itemKey, EvidenceClass.Unavailable, default, proof);
        var ancestor = AncestorVerdict(path);
        return ancestor is null
            ? new(itemKey, EvidenceClass.NotSet, default, null)
            : new(itemKey, EvidenceClass.Unavailable, default, ancestor);
    }

    /// <summary>Ein Registry-Wert (spec 2.4 Punkt 1); <paramref name="name"/> "" ist der Standardwert.</summary>
    public RegValueEvidence RegistryValue(string key, string name)
    {
        var k = RegistryKey(key);
        if (k.Class == EvidenceClass.NotSet) return new(k.Key, name, EvidenceClass.NotSet, null, null, null, false, false, null);
        if (k.Class == EvidenceClass.Unavailable) return new(k.Key, name, EvidenceClass.Unavailable, null, null, null, false, false, k.Reason);
        if (!JsonRead.TryGet(k.Value, "values", out var values) || values.ValueKind != JsonValueKind.Object)
            return new(k.Key, name, EvidenceClass.Unavailable, null, null, null, false, false, RglCodesEngine.UnexpectedValue);
        // Wertfilter des Plans: ein Wert außerhalb ist nicht erfasst, nie „nicht gesetzt“
        if (JsonRead.TryGet(k.Value, "valueFilter", out var filter) && filter.ValueKind == JsonValueKind.Array
            && !filter.EnumerateArray().Any(f => f.ValueKind == JsonValueKind.String && string.Equals(f.GetString(), name, StringComparison.OrdinalIgnoreCase)))
            return new(k.Key, name, EvidenceClass.Unavailable, null, null, null, false, false, RglCodesEngine.AbsenceNotProvable);
        if (JsonRead.TryGet(values, name, out var raw))
        {
            if (raw.ValueKind != JsonValueKind.Object)
                return new(k.Key, name, EvidenceClass.Unavailable, null, null, raw, false, false, RglCodesEngine.UnexpectedValue);
            var type = JsonRead.String(raw, "t");
            if (JsonRead.Bool(raw, "redacted") == true)
                return new(k.Key, name, EvidenceClass.Present, type, null, raw, true, JsonRead.Bool(raw, "empty") == true, null);
            if (JsonRead.Bool(raw, "truncated") == true)
                return new(k.Key, name, EvidenceClass.Unavailable, type, null, raw, false, false, RglCodesEngine.ItemTruncated);
            if (type is null) return new(k.Key, name, EvidenceClass.Unavailable, null, null, raw, false, false, RglCodesEngine.UnexpectedValue);
            JsonElement? v = JsonRead.TryGet(raw, "v", out var vv) ? vv : null;
            return new(k.Key, name, EvidenceClass.Present, type, v, raw, false, false, null);
        }
        // Schlüssel da, Wert fehlt: „nicht gesetzt“ nur, wenn der Schlüssel vollständig erfasst ist
        var reason = AbsenceProof(NormalizePath(key), requireRecursive: false);
        return reason is null
            ? new(k.Key, name, EvidenceClass.NotSet, null, null, null, false, false, null)
            : new(k.Key, name, EvidenceClass.Unavailable, null, null, null, false, false, reason);
    }

    /// <summary>
    /// null, wenn das Fehlen von <paramref name="path"/> bewiesen ist (vollständige Abdeckung, nicht verweigert,
    /// nicht abgeschnitten), sonst der Grund. <paramref name="requireRecursive"/>: ein einzelner Planschlüssel deckt nur
    /// einen ausdrücklichen KeyMissing-Eintrag bzw. seine eigenen Werte ab, keine fehlenden Einträge.
    /// </summary>
    private string? AbsenceProof(string path, bool requireRecursive)
    {
        CoverageEntry? best = null;
        string? bestRoot = null;
        foreach (var (root, entry) in _coverage)
        {
            bool match;
            if (entry.Recursive)
            {
                if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) match = true;
                else if (path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    int depth = path[(root.Length + 1)..].Count(c => c == '\\') + 1;
                    match = depth <= entry.MaxDepth;
                }
                else match = false;
            }
            else match = !requireRecursive && path.Equals(root, StringComparison.OrdinalIgnoreCase);
            if (match && (bestRoot is null || root.Length > bestRoot.Length))
            {
                best = entry;
                bestRoot = root;
            }
        }
        if (best is null) return RglCodesEngine.AbsenceNotProvable;
        if (!best.Complete || best.TruncatedAt is not null) return RglCodesEngine.ItemTruncated;
        foreach (var denied in best.DeniedSubkeys)
        {
            if (string.IsNullOrWhiteSpace(denied)) continue;
            var d = NormalizePath(denied);
            if (path.Equals(d, StringComparison.OrdinalIgnoreCase) || path.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase))
                return RglCodesEngine.ItemDenied;
        }
        return null;
    }

    /// <summary>
    /// Für einen Schlüssel ohne eigenen Eintrag: der nächste vorhandene Vorfahre entscheidet. Verweigert/Fehler →
    /// nicht beweisbar; führt er den Unterschlüssel noch auf (ohne Eintrag) → Widerspruch, nicht beweisbar; sonst null.
    /// </summary>
    private string? AncestorVerdict(string path)
    {
        var child = path;
        for (int cut = path.LastIndexOf('\\'); cut > 4; cut = path.LastIndexOf('\\', cut - 1))
        {
            var ancestor = path[..cut];
            var childName = child[(ancestor.Length + 1)..];
            int next = childName.IndexOf('\\');
            if (next >= 0) childName = childName[..next];
            if (_conflicts.Contains("reg:" + ancestor)) return RglCodesEngine.UnexpectedValue;
            if (_items.TryGetValue("reg:" + ancestor, out var item))
            {
                if (item.Truncated) return RglCodesEngine.ItemTruncated;
                switch (item.Status)
                {
                    case ItemStatus.KeyMissing:
                        return null;
                    case ItemStatus.Ok:
                    case ItemStatus.ValueMissing:
                        if (item.Value is { ValueKind: JsonValueKind.Object } v && JsonRead.TryGet(v, "subkeys", out var subkeys)
                            && subkeys.ValueKind == JsonValueKind.Array
                            && subkeys.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && string.Equals(s.GetString(), childName, StringComparison.OrdinalIgnoreCase)))
                            return RglCodesEngine.ItemNotCollected;
                        return null;
                    default:
                        return ReasonFor(item.Status);
                }
            }
        }
        return null;
    }
}
