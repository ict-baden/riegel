// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;

namespace Riegel.Tests.TestSupport;

/// <summary>
/// Baut Exporte für Tests in der Kodierung von spec 7.3, ohne Fixtures von Hand zu schreiben:
/// <code>
/// var snapshot = SnapshotBuilder.Windows11Workgroup()
///     .RegistryValue(@"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "RequireSecuritySignature", RegValueType.Dword, 1)
///     .WmiMethod(@"root\Microsoft\Windows\SMB", "MSFT_SmbServerConfiguration", "GetConfiguration", new { RequireSecuritySignature = true })
///     .NotCollected(ItemKeys.AuditPolicy)
///     .Build();
/// </code>
/// Ohne weitere Angabe gilt volle Abdeckung des Erfassungsplans (<see cref="FullPlanCoverage"/>): ein nicht
/// eingetragener Schlüssel in einem Planbereich ist dann KeyMissing. <c>parsed:*</c> nur bei Herkunft synthetic.
/// </summary>
public sealed class SnapshotBuilder
{
    private static readonly JsonSerializerOptions Raw = new() { PropertyNamingPolicy = null, WriteIndented = false };

    private readonly SortedDictionary<string, RegKey> _registry = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedDictionary<string, SnapshotItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private List<CoverageEntry>? _coverage;
    private MachineInfo _machine;
    private DateTimeOffset _collected = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private bool _admin = true;
    private SnapshotOrigin _origin = SnapshotOrigin.Synthetic;
    private CollectionOptions _options = new(false, false, false);
    private CollectorInfo _collector = new("Riegel.Collector", "0.1.0", null, "4.0.30319.42000", true);

    private sealed class RegKey
    {
        public SortedDictionary<string, JsonElement> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public SortedSet<string> Subkeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string>? ValueFilter { get; set; }
        public ItemStatus Status { get; set; } = ItemStatus.Ok;
    }

    private SnapshotBuilder(MachineInfo machine) => _machine = machine;

    // ---------------------------------------------------------------- Rechner-Vorlagen

    /// <summary>Windows 11 24H2 Pro, Arbeitsgruppe (Build 26100).</summary>
    public static SnapshotBuilder Windows11Workgroup(string host = "W11-TEST") =>
        new(new MachineInfo(host, null, 0, 1, 26100, 6899, "Professional", "Client", "24H2", "AMD64", "00000000-0000-4000-8000-000000000001", "en-US", null));

    /// <summary>Windows 10 Enterprise LTSC 2019 (Build 17763), Bedienstation.</summary>
    public static SnapshotBuilder Windows10Ltsc2019(string host = "HMI-TEST") =>
        new(new MachineInfo(host, null, 0, 1, 17763, 7792, "EnterpriseS", "Client", null, "AMD64", "00000000-0000-4000-8000-000000000002", "de-DE", null));

    /// <summary>Windows Server 2022 Standard als Domänenmitglied (DomainRole 3).</summary>
    public static SnapshotBuilder Server2022Member(string host = "SRV-TEST") =>
        new(new MachineInfo(host, "corp.example", 3, 3, 20348, 4294, "ServerStandard", "Server", "21H2", "AMD64", "00000000-0000-4000-8000-000000000003", "en-US", null));

    /// <summary>Windows Server 2016 Standard, Arbeitsgruppe.</summary>
    public static SnapshotBuilder Server2016(string host = "HIST-TEST") =>
        new(new MachineInfo(host, null, 2, 3, 14393, 8519, "ServerStandard", "Server", null, "AMD64", "00000000-0000-4000-8000-000000000004", "en-US", null));

    /// <summary>Windows 7 SP1 (CLR 2).</summary>
    public static SnapshotBuilder Windows7(string host = "W7-TEST") =>
        new SnapshotBuilder(new MachineInfo(host, null, 0, 1, 7601, 0, "Professional", "Client", null, "AMD64", "00000000-0000-4000-8000-000000000005", "en-US", null))
            .Clr("2.0.50727.8000");

    /// <summary>Domänencontroller (DomainRole 5, ProductType 2).</summary>
    public static SnapshotBuilder DomainController(string host = "DC-TEST") =>
        new(new MachineInfo(host, "corp.example", 5, 2, 20348, 4294, "ServerStandard", "Server", "21H2", "AMD64", "00000000-0000-4000-8000-000000000006", "en-US", null));

    /// <summary>Beliebiger Rechner; nicht angegebene Kopf-Felder bleiben null (wie bei Bordmittel-Paketen).</summary>
    public static SnapshotBuilder Machine(string host, int? build, int? productType, int? domainRole, string? edition = null) =>
        new(new MachineInfo(host, null, domainRole, productType, build, null, edition, null, null, null, null, null, null));

    // ---------------------------------------------------------------- Kopf

    public SnapshotBuilder Host(string host) { _machine = _machine with { HostName = host }; return this; }
    public SnapshotBuilder Header(Func<MachineInfo, MachineInfo> change) { _machine = change(_machine); return this; }
    public SnapshotBuilder Admin(bool isAdmin = true) { _admin = isAdmin; return this; }
    public SnapshotBuilder NonAdmin() => Admin(false);
    public SnapshotBuilder Origin(SnapshotOrigin origin) { _origin = origin; return this; }
    public SnapshotBuilder CollectedAt(DateTimeOffset when) { _collected = when.ToUniversalTime(); return this; }
    public SnapshotBuilder Options(bool minimal = false, bool deep = false, bool pseudonymized = false) { _options = new(minimal, deep, pseudonymized); return this; }
    public SnapshotBuilder Clr(string clr) { _collector = _collector with { Clr = clr }; return this; }
    public SnapshotBuilder Collector(CollectorInfo collector) { _collector = collector; return this; }

    // ---------------------------------------------------------------- Abdeckung

    /// <summary>Ein Eintrag je Registry-Bereich des Erfassungsplans, alle vollständig (Standard).</summary>
    public SnapshotBuilder FullPlanCoverage()
    {
        _coverage = PlanCoverage();
        return this;
    }

    /// <summary>Keine Abdeckung: jeder fehlende Schlüssel ist dann NotCollected (z. B. Bordmittel ohne diesen Export).</summary>
    public SnapshotBuilder NoCoverage()
    {
        _coverage = [];
        return this;
    }

    /// <summary>Einen Bereich hinzufügen oder ersetzen.</summary>
    public SnapshotBuilder Coverage(string root, bool recursive = true, bool complete = true, int maxDepth = 8, string? truncatedAt = null,
        params string[] deniedSubkeys)
    {
        _coverage ??= PlanCoverage();
        _coverage.RemoveAll(c => string.Equals(c.Root, root, StringComparison.OrdinalIgnoreCase));
        _coverage.Add(new CoverageEntry(root, recursive, recursive ? maxDepth : 0, complete, deniedSubkeys, truncatedAt));
        return this;
    }

    /// <summary>Bereich als unvollständig markieren (Grenze erreicht).</summary>
    public SnapshotBuilder Incomplete(string root, string truncatedAt)
    {
        var existing = (_coverage ??= PlanCoverage()).FirstOrDefault(c => string.Equals(c.Root, root, StringComparison.OrdinalIgnoreCase));
        return Coverage(root, existing?.Recursive ?? true, false, existing?.MaxDepth ?? 8, truncatedAt);
    }

    /// <summary>Unterschlüssel ohne Leserecht in einem Bereich.</summary>
    public SnapshotBuilder DeniedSubkey(string root, string subkey)
    {
        var existing = (_coverage ??= PlanCoverage()).FirstOrDefault(c => string.Equals(c.Root, root, StringComparison.OrdinalIgnoreCase));
        var denied = new List<string>(existing?.DeniedSubkeys ?? []) { subkey };
        return Coverage(root, existing?.Recursive ?? true, existing?.Complete ?? true, existing?.MaxDepth ?? 8, existing?.TruncatedAt, [.. denied]);
    }

    // ---------------------------------------------------------------- Registry

    /// <summary>Schlüssel vorhanden (ohne Werte), optional mit Unterschlüsseln. Übergeordnete Schlüssel entstehen mit.</summary>
    public SnapshotBuilder RegistryKey(string key, params string[] subkeys)
    {
        var k = Key(key);
        foreach (var s in subkeys) k.Subkeys.Add(s);
        return this;
    }

    /// <summary>Ein Registry-Wert (DWORD als Zahl, QWORD als Dezimaltext, multi_sz als Liste, binary als Bytes).</summary>
    public SnapshotBuilder RegistryValue(string key, string name, RegValueType type, object value)
    {
        Key(key).Values[name] = RegValue(type, value);
        return this;
    }

    /// <summary>Ein Wert mit eigenem Typtext (z. B. „none“, „unknown“ oder ein falscher Typ für Tests).</summary>
    public SnapshotBuilder RegistryRawValue(string key, string name, string type, object? value)
    {
        Key(key).Values[name] = JsonValues.Build(w =>
        {
            w.WriteStartObject();
            w.WriteString("t", type);
            w.WritePropertyName("v");
            JsonSerializer.Serialize(w, value, Raw);
            w.WriteEndObject();
        });
        return this;
    }

    /// <summary>Geschwärzter Wert (spec 7.3): <c>{ "t": …, "redacted": true, "empty": … }</c>.</summary>
    public SnapshotBuilder RegistryRedacted(string key, string name, RegValueType type = RegValueType.Sz, bool empty = false)
    {
        Key(key).Values[name] = JsonValues.Build(w =>
        {
            w.WriteStartObject();
            w.WriteString("t", TypeName(type));
            w.WriteBoolean("redacted", true);
            w.WriteBoolean("empty", empty);
            w.WriteEndObject();
        });
        return this;
    }

    /// <summary>Nur bestimmte Werte gelesen (Filter des Plans, z. B. MachineGuid).</summary>
    public SnapshotBuilder RegistryValueFilter(string key, params string[] values)
    {
        Key(key).ValueFilter = [.. values];
        return this;
    }

    /// <summary>Schlüssel ausdrücklich nicht vorhanden (Eintrag KeyMissing, z. B. für einen einzelnen Planschlüssel).</summary>
    public SnapshotBuilder RegistryKeyMissing(string key)
    {
        var path = Normalize(key);
        _registry.Remove(path);
        _items[ItemKeys.Registry(path)] = new SnapshotItem(ItemKeys.Registry(path), ItemStatus.KeyMissing, null, null, null, 0);
        return this;
    }

    // ---------------------------------------------------------------- WMI, API, Rohdaten

    /// <summary>WMI-Klasse mit Zeilen (anonyme Objekte oder Wörterbücher; Eigenschaftsnamen wie in WMI).</summary>
    public SnapshotBuilder WmiRows(string ns, string className, params object[] rows) =>
        Item(ItemKeys.Wmi(ns, className), ItemStatus.Ok, new { rows });

    /// <summary>Statische WMI-Methode, Ergebnis unter <c>out</c>.</summary>
    public SnapshotBuilder WmiMethod(string ns, string className, string method, object output) =>
        Item(ItemKeys.WmiMethod(ns, className, method), ItemStatus.Ok, new { @out = output });

    /// <summary>Ein API-Eintrag (<c>api:…</c>, <c>evt:…</c>, <c>raw:…</c>) mit Wert.</summary>
    public SnapshotBuilder Api(string key, object value) => Item(key, ItemStatus.Ok, value);

    /// <summary>Abgeleiteter Eintrag <c>parsed:*</c> – nur für synthetische Exporte erlaubt (spec 7.3).</summary>
    public SnapshotBuilder Parsed(string key, object value)
    {
        if (!key.StartsWith("parsed:", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("parsed:* erwartet", nameof(key));
        if (_origin != SnapshotOrigin.Synthetic) throw new InvalidOperationException("parsed:* nur bei Herkunft synthetic.");
        return Item(key, ItemStatus.Ok, value);
    }

    /// <summary>Beliebiger Eintrag mit Status, Wert und Fehlerangaben.</summary>
    public SnapshotBuilder Item(string key, ItemStatus status, object? value = null, int? errorCode = null, string? error = null,
        bool truncated = false, int durationMs = 1)
    {
        JsonElement? element = value switch
        {
            null => null,
            JsonElement e => e,
            _ => JsonSerializer.SerializeToElement(value, Raw),
        };
        _items[key] = new SnapshotItem(key, status, element, errorCode, error, durationMs, truncated);
        if (key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase)) _registry.Remove(Normalize(key));
        return this;
    }

    // ---------------------------------------------------------------- Status

    /// <summary>Status eines Eintrags setzen; ein vorhandener Wert bleibt nur bei Ok/ValueMissing erhalten.</summary>
    public SnapshotBuilder Status(string key, ItemStatus status, int? errorCode = null, string? error = null)
    {
        if (key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase) && _registry.TryGetValue(Normalize(key), out var reg))
        {
            if (status is ItemStatus.Ok or ItemStatus.ValueMissing) { reg.Status = status; return this; }
            _registry.Remove(Normalize(key));
        }
        _items.TryGetValue(key, out var existing);
        var keep = status is ItemStatus.Ok or ItemStatus.ValueMissing ? existing?.Value : null;
        _items[key] = new SnapshotItem(key, status, keep, errorCode, error, existing?.DurationMs ?? 1, existing?.Truncated ?? false);
        return this;
    }

    public SnapshotBuilder Denied(string key, int errorCode = 5) => Status(key, ItemStatus.Denied, errorCode, "Access denied");
    public SnapshotBuilder NotCollected(string key, string? reason = null) => Status(key, ItemStatus.NotCollected, null, reason);
    public SnapshotBuilder NamespaceMissing(string key) => Status(key, ItemStatus.NamespaceMissing, unchecked((int)0x8004100E), "Invalid namespace");
    public SnapshotBuilder Error(string key, int errorCode = 1, string? error = "Error") => Status(key, ItemStatus.Error, errorCode, error);
    public SnapshotBuilder NotApplicable(string key) => Status(key, ItemStatus.NotApplicable);

    /// <summary>Eintrag als abgeschnitten markieren (gilt als Unavailable, spec 2.4).</summary>
    public SnapshotBuilder Truncated(string key)
    {
        var item = Materialize(key);
        _items[key] = item with { Truncated = true };
        _registry.Remove(Normalize(key));
        return this;
    }

    /// <summary>Eintrag ganz entfernen (fehlt dann im Export).</summary>
    public SnapshotBuilder Without(string key)
    {
        _items.Remove(key);
        if (key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase)) _registry.Remove(Normalize(key));
        return this;
    }

    // ---------------------------------------------------------------- Ergebnis

    public Snapshot Build()
    {
        var items = new SortedDictionary<string, SnapshotItem>(_items, StringComparer.OrdinalIgnoreCase);
        foreach (var (path, key) in _registry)
        {
            var itemKey = ItemKeys.Registry(path);
            if (items.ContainsKey(itemKey)) continue;
            items[itemKey] = Materialize(itemKey, key);
        }
        return new Snapshot(SchemaIds.Snapshot, _collector, _collected, _admin, _origin, _options, _machine,
            _coverage ?? PlanCoverage(), [.. items.Values]);
    }

    /// <summary>Export als JSON (wie die Datei).</summary>
    public string ToJson() => RiegelJson.Serialize(Build());

    /// <summary>Export mit Begleitdatei in <paramref name="directory"/> schreiben; gibt den Pfad zurück.</summary>
    public string WriteTo(string directory, bool gzip = false) => SnapshotFile.Write(Build(), directory, gzip);

    // ---------------------------------------------------------------- Helfer

    /// <summary>Volle Abdeckung laut contracts/collection-plan.json.</summary>
    public static List<CoverageEntry> PlanCoverage()
    {
        var plan = RiegelJson.ReadFile<CollectionPlan>(RepoPaths.CollectionPlan, 10 * 1024 * 1024);
        return [.. plan.Registry.Roots.Concat(plan.Registry.Keys)
            .Select(e => new CoverageEntry(e.Path, e.Kind != RegistryPlanKind.Key, e.MaxDepth, true, [], null))];
    }

    private SnapshotItem Materialize(string itemKey)
    {
        if (_items.TryGetValue(itemKey, out var existing)) return existing;
        if (itemKey.StartsWith("reg:", StringComparison.OrdinalIgnoreCase) && _registry.TryGetValue(Normalize(itemKey), out var reg))
            return Materialize(itemKey, reg);
        throw new InvalidOperationException($"Eintrag {itemKey} gibt es im Builder nicht.");
    }

    private static SnapshotItem Materialize(string itemKey, RegKey key)
    {
        var value = JsonValues.Build(w =>
        {
            w.WriteStartObject();
            w.WritePropertyName("values");
            w.WriteStartObject();
            foreach (var (name, v) in key.Values)
            {
                w.WritePropertyName(name);
                v.WriteTo(w);
            }
            w.WriteEndObject();
            w.WritePropertyName("subkeys");
            w.WriteStartArray();
            foreach (var s in key.Subkeys) w.WriteStringValue(s);
            w.WriteEndArray();
            if (key.ValueFilter is { } filter)
            {
                w.WritePropertyName("valueFilter");
                w.WriteStartArray();
                foreach (var f in filter) w.WriteStringValue(f);
                w.WriteEndArray();
            }
            w.WriteEndObject();
        });
        return new SnapshotItem(itemKey, key.Status, value, null, null, 1);
    }

    private RegKey Key(string key)
    {
        var path = Normalize(key);
        _items.Remove(ItemKeys.Registry(path));
        if (!_registry.TryGetValue(path, out var k)) _registry[path] = k = new RegKey();
        // Übergeordnete Schlüssel bis zur Wurzel des rekursiven Planbereichs anlegen, mit diesem als Unterschlüssel
        var root = RecursiveRoots.Value
            .Where(r => path.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Length).FirstOrDefault();
        if (root is null) return k;
        var current = path;
        while (current.Length > root.Length)
        {
            int cut = current.LastIndexOf('\\');
            var child = current[(cut + 1)..];
            current = current[..cut];
            if (!_registry.TryGetValue(current, out var parent)) _registry[current] = parent = new RegKey();
            parent.Subkeys.Add(child);
        }
        return k;
    }

    private static readonly Lazy<IReadOnlyList<string>> RecursiveRoots = new(() =>
        [.. PlanCoverage().Where(c => c.Recursive).Select(c => c.Root)]);

    private static string Normalize(string key)
    {
        var k = key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase) ? key[4..] : key;
        k = k.TrimEnd('\\');
        return k.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase) ? @"HKLM\" + k[5..] : @"HKLM\" + k;
    }

    private static string TypeName(RegValueType type) => type switch
    {
        RegValueType.Dword => "dword",
        RegValueType.Qword => "qword",
        RegValueType.Sz => "sz",
        RegValueType.ExpandSz => "expand_sz",
        RegValueType.MultiSz => "multi_sz",
        _ => "binary",
    };

    private static JsonElement RegValue(RegValueType type, object value) => JsonValues.Build(w =>
    {
        w.WriteStartObject();
        w.WriteString("t", TypeName(type));
        w.WritePropertyName("v");
        switch (type)
        {
            case RegValueType.Dword:
                w.WriteNumberValue(Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case RegValueType.Qword:
                w.WriteStringValue(Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case RegValueType.MultiSz:
                w.WriteStartArray();
                foreach (var s in (IEnumerable<string>)value) w.WriteStringValue(s);
                w.WriteEndArray();
                break;
            case RegValueType.Binary:
                w.WriteStringValue(Convert.ToBase64String((byte[])value));
                break;
            default:
                w.WriteStringValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
        }
        w.WriteEndObject();
    });
}
