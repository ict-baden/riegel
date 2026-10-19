// SPDX-License-Identifier: MIT
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Evaluation;
using Riegel.Core.Model;
using Riegel.Core.Services;

namespace Riegel.Core.Facts;

/// <summary>
/// Leitet die Fakten aus spec 2.5 ab. Jeder Fakt ist ein Wert oder null (unbekannt); nie wird aus fehlenden Daten ein
/// Wert geraten. Kopf-Felder haben Vorrang, Registry und WMI ergänzen, wenn der Kopf leer ist (Bordmittel-Pakete).
/// </summary>
internal sealed class FactsDeriver : IFactsDeriver
{
    public IReadOnlyDictionary<string, JsonElement> Derive(Snapshot s, ICatalogue c) => DeriveDetailed(new EvidenceStore(s), c).ToDictionary();

    /// <summary>Fakten mit Herkunft (gelesene Einträge, Derived, Heuristic).</summary>
    public static FactSet DeriveDetailed(EvidenceStore store, ICatalogue catalogue)
    {
        var facts = new FactSet();
        var m = store.Snapshot.Machine;
        var previous = store.Observer;
        try
        {
            // ---- Betriebssystem
            var productType = Track(store, facts, "machine.productType", () => m.ProductType is { } p ? JsonValues.Number(p) : ProductTypeFallback(store));
            facts.Values.Remove("machine.productType");
            var productSources = facts.SourcesOf("machine.productType");
            facts.Sources.Remove("machine.productType");
            facts.Values[FactNames.OsKind] = productType.ValueKind == JsonValueKind.Number && productType.TryGetInt32(out var pt)
                ? pt switch { 1 => JsonValues.Text("client"), 2 or 3 => JsonValues.Text("server"), _ => JsonValues.Null }
                : JsonValues.Null;
            facts.Sources[FactNames.OsKind] = productSources;

            Track(store, facts, FactNames.OsBuild, () => m.Build is { } b ? JsonValues.Number(b) : RegistryNumber(store, Known.CurrentVersion, "CurrentBuild"));
            Track(store, facts, FactNames.OsUbr, () => m.Ubr is { } u ? JsonValues.Number(u) : RegistryNumber(store, Known.CurrentVersion, "UBR"));
            Track(store, facts, FactNames.OsEdition, () => m.EditionId is { } e ? JsonValues.Text(e) : RegistryText(store, Known.CurrentVersion, "EditionID"));
            Track(store, facts, FactNames.OsInstallationType, () => m.InstallationType is { } i ? JsonValues.Text(i) : RegistryText(store, Known.CurrentVersion, "InstallationType"));
            facts.Values[FactNames.OsServerCore] = facts.Text(FactNames.OsInstallationType) is { } it
                ? JsonValues.Bool(string.Equals(it, "Server Core", StringComparison.OrdinalIgnoreCase))
                : JsonValues.Null;
            facts.Sources[FactNames.OsServerCore] = facts.SourcesOf(FactNames.OsInstallationType);
            Track(store, facts, FactNames.OsArch, () => m.Architecture is { } a ? JsonValues.Text(a) : JsonValues.Null);

            // ---- Domäne
            var role = Track(store, facts, "machine.domainRole", () => m.DomainRole is { } r ? JsonValues.Number(r) : WmiRowNumber(store, Known.ComputerSystem, "DomainRole"));
            var roleSources = facts.SourcesOf("machine.domainRole");
            facts.Values.Remove("machine.domainRole");
            facts.Sources.Remove("machine.domainRole");
            int? domainRole = role.ValueKind == JsonValueKind.Number && role.TryGetInt32(out var dr) && dr is >= 0 and <= 5 ? dr : null;
            facts.Values[FactNames.DomainJoined] = domainRole is { } d1 ? JsonValues.Bool(d1 is 1 or 3 or 4 or 5) : JsonValues.Null;
            facts.Values[FactNames.DomainIsDc] = domainRole is { } d2 ? JsonValues.Bool(d2 is 4 or 5) : JsonValues.Null;
            facts.Sources[FactNames.DomainJoined] = roleSources;
            facts.Sources[FactNames.DomainIsDc] = roleSources;

            // ---- Rechner
            Track(store, facts, FactNames.MachineMobile, () => WmiRowNumber(store, Known.ComputerSystem, "PCSystemType") is { ValueKind: JsonValueKind.Number } t
                ? JsonValues.Bool(t.GetInt64() == 2) : JsonValues.Null);
            facts.LastBootUtc = m.LastBootUtc ?? LastBootFallback(store);

            // ---- Virenschutz
            Track(store, facts, FactNames.AvThirdPartyActive, () => ThirdPartyAv(store, facts));
            Track(store, facts, FactNames.DefenderRtp, () => DefenderRow(store) is { } row && JsonRead.Bool(row, "RealTimeProtectionEnabled") is { } rtp
                ? JsonValues.Bool(rtp) : JsonValues.Null);
            Track(store, facts, FactNames.DefenderMode, () => DefenderMode(store, facts));
            // Eine Herleitung stützt sich auch auf den Drittanbieter-Fakt und seine Belege
            if (facts.Derived.Contains(FactNames.DefenderMode))
                facts.Sources[FactNames.DefenderMode] = facts.SourcesOf(FactNames.DefenderMode).Union(facts.SourcesOf(FactNames.AvThirdPartyActive));

            // ---- Office, RDP, Netz
            Track(store, facts, FactNames.OfficeInstalled, () => Office(store, catalogue.Data.OfficeDetection));
            Track(store, facts, FactNames.RdpEnabled, () => Rdp(store));
            Track(store, facts, FactNames.NetGatewayConfigured, () => Gateway(store));
            Track(store, facts, FactNames.NetActiveAdapters, () => ActiveAdapters(store));
            Track(store, facts, FactNames.NetWireless, () => Wireless(store));
            Track(store, facts, FactNames.NetIpForwarding, () => RegistryNumber(store, Known.TcpipParameters, "IPEnableRouter") is { ValueKind: JsonValueKind.Number } f
                ? JsonValues.Bool(f.GetDecimal() != 0) : JsonValues.Null);
            Track(store, facts, FactNames.NetProxyConfigured, () => Proxy(store));

            // ---- Fähigkeiten (aus der Tabelle, Kopf-Felder wie oben)
            foreach (var entry in catalogue.Capabilities.Capabilities.OrderBy(e => e.Capability, StringComparer.Ordinal))
            {
                var exists = CapabilityMatcher.Exists(entry, facts.Kind, facts.Build, facts.Ubr, facts.Edition, facts.InstallationType);
                var name = FactNames.CapabilityPrefix + entry.Capability;
                facts.Values[name] = exists is { } x ? JsonValues.Bool(x) : JsonValues.Null;
                facts.Sources[name] = facts.SourcesOf(FactNames.OsKind).Union(facts.SourcesOf(FactNames.OsBuild)).Union(facts.SourcesOf(FactNames.OsUbr))
                    .Union(facts.SourcesOf(FactNames.OsEdition)).Union(facts.SourcesOf(FactNames.OsInstallationType));
            }
        }
        finally
        {
            store.Observer = previous;
        }
        return facts;
    }

    private static JsonElement Track(EvidenceStore store, FactSet facts, string fact, Func<JsonElement> derive)
    {
        var keys = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        var previous = store.Observer;
        store.Observer = k => { keys.Add(k); previous?.Invoke(k); };
        try
        {
            var value = derive();
            facts.Values[fact] = value;
            facts.Sources[fact] = keys.ToImmutable();
            return value;
        }
        finally
        {
            store.Observer = previous;
        }
    }

    // ---------------------------------------------------------------- Betriebssystem

    private static JsonElement ProductTypeFallback(EvidenceStore store)
    {
        if (WmiRowNumber(store, Known.OperatingSystem, "ProductType") is { ValueKind: JsonValueKind.Number } n) return n;
        // ProductOptions\ProductType: WinNT = Client, ServerNT = Server, LanmanNT = Domänencontroller
        var v = store.RegistryValue(Known.ProductOptions, "ProductType");
        if (v.IsPresent && v.V is { ValueKind: JsonValueKind.String } s)
            return s.GetString()?.ToUpperInvariant() switch
            {
                "WINNT" => JsonValues.Number(1),
                "LANMANNT" => JsonValues.Number(2),
                "SERVERNT" => JsonValues.Number(3),
                _ => JsonValues.Null,
            };
        return JsonValues.Null;
    }

    private static DateTimeOffset? LastBootFallback(EvidenceStore store)
    {
        var os = store.Item(Known.OperatingSystem);
        if (!os.IsPresent || JsonRead.Rows(os.Value) is not { Count: > 0 } rows) return null;
        return JsonRead.Time(JsonRead.Get(rows[0], "LastBootUpTime"));
    }

    private static JsonElement RegistryNumber(EvidenceStore store, string key, string name)
    {
        var v = store.RegistryValue(key, name);
        if (!v.IsPresent || v.Redacted || v.V is not { } raw) return JsonValues.Null;
        return raw.ValueKind switch
        {
            JsonValueKind.Number when raw.TryGetInt64(out var n) && n >= 0 => JsonValues.Number(n),
            JsonValueKind.String when long.TryParse(raw.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var s) => JsonValues.Number(s),
            _ => JsonValues.Null,
        };
    }

    private static JsonElement RegistryText(EvidenceStore store, string key, string name)
    {
        var v = store.RegistryValue(key, name);
        return v.IsPresent && !v.Redacted && v.V is { ValueKind: JsonValueKind.String } s ? JsonValues.Text(s.GetString()!) : JsonValues.Null;
    }

    private static JsonElement WmiRowNumber(EvidenceStore store, string key, string property)
    {
        var item = store.Item(key);
        if (!item.IsPresent || JsonRead.Rows(item.Value) is not { Count: > 0 } rows) return JsonValues.Null;
        return JsonRead.Integer(rows[0], property) is { } n ? JsonValues.Number(n) : JsonValues.Null;
    }

    // ---------------------------------------------------------------- Virenschutz

    internal static JsonElement? DefenderRow(EvidenceStore store)
    {
        var item = store.Item(Known.MpStatus);
        return item.IsPresent && JsonRead.Rows(item.Value) is { Count: > 0 } rows ? rows[0] : null;
    }

    /// <summary>
    /// defender.mode: AMRunningMode zuerst (Microsoft nennt „Normal“, „Passive“, „EDR Block Mode“); ohne lesbaren Modus
    /// hergeleitet (Flag Derived) aus AntivirusEnabled, RealTimeProtectionEnabled und dem Drittanbieter-Fakt; fehlt der
    /// Defender-Namespace ganz → absent.
    /// </summary>
    private static JsonElement DefenderMode(EvidenceStore store, FactSet facts)
    {
        var item = store.Item(Known.MpStatus);
        if (item.Status == ItemStatus.NamespaceMissing) return JsonValues.Text(FactNames.Defender.Absent);
        if (!item.IsPresent || JsonRead.Rows(item.Value) is not { Count: > 0 } rows) return JsonValues.Text(FactNames.Defender.Unknown);
        var row = rows[0];
        var mode = JsonRead.String(row, "AMRunningMode")?.Trim();
        var mapped = mode?.ToUpperInvariant() switch
        {
            "NORMAL" => FactNames.Defender.Active,
            // „Passive“ steht in Microsofts Doku; „Passive Mode“ ist die Form der synthetischen Fixtures (unverified)
            "PASSIVE" or "PASSIVE MODE" => FactNames.Defender.Passive,
            "EDR BLOCK MODE" => FactNames.Defender.EdrBlock,
            _ => null,
        };
        if (mapped is not null) return JsonValues.Text(mapped);
        // Hergeleitet: nur eindeutige Fälle, sonst unknown
        var av = JsonRead.Bool(row, "AntivirusEnabled");
        var service = JsonRead.Bool(row, "AMServiceEnabled");
        var rtp = JsonRead.Bool(row, "RealTimeProtectionEnabled");
        string derived;
        if (av == false || service == false)
        {
            // Review evidence-12: mit eingeschränktem Token liefert MpComputerStatus Platzhalter („Not running“, alles
            // false, Signaturalter 65535). Meldet die dokumentierte WSC-API Microsoft Defender als eingeschaltet, ist
            // „aus“ widerlegt: unbekannt statt eines falschen „kein aktiver Virenschutz“.
            if (WscReportsDefenderOn(store)) return JsonValues.Text(FactNames.Defender.Unknown);
            derived = FactNames.Defender.Off;
        }
        else if (av == true && rtp == true && facts.Bool(FactNames.AvThirdPartyActive) == false) derived = FactNames.Defender.Active;
        else return JsonValues.Text(FactNames.Defender.Unknown);
        facts.Derived.Add(FactNames.DefenderMode);
        return JsonValues.Text(derived);
    }

    /// <summary>Meldet die WSC-API (api:wsc-av) Microsoft Defender mit Zustand „on“?</summary>
    private static bool WscReportsDefenderOn(EvidenceStore store)
    {
        var wsc = store.Item(ItemKeys.WscAntivirus);
        return wsc.IsPresent && wsc.Value.ValueKind == JsonValueKind.Array && wsc.Value.EnumerateArray().Any(p =>
            p.ValueKind == JsonValueKind.Object && IsDefenderName(JsonRead.String(p, "name"))
            && string.Equals(JsonRead.String(p, "state"), "on", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Microsoft Defender in den Produktnamen von WSC und SecurityCenter2.</summary>
    internal static bool IsDefenderName(string? name) =>
        name is not null && (name.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase)
                             || name.Contains("Microsoft Defender", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// SecurityCenter2 productState: Bit 0x1000 = eingeschaltet, Bits 0x00F0 ≠ 0 = Signaturen veraltet. Microsoft
    /// dokumentiert das nicht – deshalb nur Heuristik (Flag Heuristic), nie alleiniger Beleg für „erfüllt“.
    /// </summary>
    internal static (bool On, bool UpToDate)? DecodeProductState(JsonElement row)
    {
        if (JsonRead.Integer(row, "productState") is not { } state || state < 0) return null;
        return ((state & 0x1000) != 0, (state & 0x00F0) == 0);
    }

    /// <summary>av.thirdPartyActive: WSC-API zuerst, sonst SecurityCenter2 (heuristisch).</summary>
    private static JsonElement ThirdPartyAv(EvidenceStore store, FactSet facts)
    {
        var wsc = store.Item(ItemKeys.WscAntivirus);
        if (wsc.IsPresent && wsc.Value.ValueKind == JsonValueKind.Array)
        {
            bool any = false, known = true;
            foreach (var product in wsc.Value.EnumerateArray())
            {
                if (product.ValueKind != JsonValueKind.Object) { known = false; continue; }
                if (IsDefenderName(JsonRead.String(product, "name"))) continue;
                var state = JsonRead.String(product, "state");
                if (string.Equals(state, "on", StringComparison.OrdinalIgnoreCase)) any = true;
                else if (state is null) known = false;
            }
            if (any) return JsonValues.True;
            if (known) return JsonValues.False;
        }
        var sc = store.Item(Known.SecurityCenterAv);
        if (sc.IsPresent && JsonRead.Rows(sc.Value) is { } rows)
        {
            bool any = false, known = true;
            foreach (var row in rows)
            {
                if (IsDefenderName(JsonRead.String(row, "displayName"))) continue;
                if (DecodeProductState(row) is { } decoded) any |= decoded.On;
                else known = false;
            }
            if (!any && !known) return JsonValues.Null;
            facts.Heuristic.Add(FactNames.AvThirdPartyActive);
            return JsonValues.Bool(any);
        }
        return JsonValues.Null;
    }

    // ---------------------------------------------------------------- Office, RDP, Netz

    /// <summary>office.installed über rules/data/office-detection.json; ohne Erkennungsdaten unbekannt.</summary>
    private static JsonElement Office(EvidenceStore store, OfficeDetection? detection)
    {
        if (detection is null) return JsonValues.Null;
        bool unknown = false;
        foreach (var key in detection.ClickToRunKeys.Concat(detection.InstallRootKeys))
        {
            var found = KeyOrValueExists(store, key);
            if (found == true) return JsonValues.True;
            if (found is null) unknown = true;
        }
        if (detection.UninstallDisplayNamePrefixes.Count > 0)
            foreach (var root in new[] { Known.UninstallNative, Known.UninstallWow })
            {
                var k = store.RegistryKey(root);
                if (k.Class == EvidenceClass.NotSet) continue;
                if (k.Class == EvidenceClass.Unavailable) { unknown = true; continue; }
                foreach (var sub in k.Subkeys)
                {
                    var name = store.RegistryValue(root + "\\" + sub, "DisplayName");
                    if (name.IsPresent && name.V is { ValueKind: JsonValueKind.String } text)
                    {
                        var display = text.GetString() ?? "";
                        if (detection.UninstallDisplayNamePrefixes.Any(p => !string.IsNullOrEmpty(p.Pattern) && display.StartsWith(p.Pattern, StringComparison.OrdinalIgnoreCase)))
                            return JsonValues.True;
                    }
                    else if (name.Class == EvidenceClass.Unavailable) unknown = true;
                }
            }
        return unknown ? JsonValues.Null : JsonValues.False;
    }

    private static bool? KeyOrValueExists(EvidenceStore store, DetectionKey key)
    {
        if (string.IsNullOrWhiteSpace(key.Key)) return false;
        if (string.IsNullOrEmpty(key.Value))
        {
            var k = store.RegistryKey(key.Key);
            return k.Class switch { EvidenceClass.Present => true, EvidenceClass.NotSet => false, _ => null };
        }
        var v = store.RegistryValue(key.Key, key.Value);
        return v.Class switch { EvidenceClass.Present => true, EvidenceClass.NotSet => false, _ => null };
    }

    /// <summary>rdp.enabled: fDenyTSConnections der Richtlinie vor dem lokalen Wert; 0 = Verbindungen erlaubt.</summary>
    private static JsonElement Rdp(EvidenceStore store)
    {
        foreach (var key in new[] { Known.TerminalServicesPolicy, Known.TerminalServer })
        {
            var v = store.RegistryValue(key, "fDenyTSConnections");
            if (v.Class == EvidenceClass.Unavailable) return JsonValues.Null;
            if (v.Class == EvidenceClass.NotSet) continue;
            if (v.Type == "dword" && v.V is { ValueKind: JsonValueKind.Number } n) return JsonValues.Bool(n.GetDecimal() == 0);
            return JsonValues.Null;
        }
        return JsonValues.Null;
    }

    private static JsonElement Gateway(EvidenceStore store)
    {
        var item = store.Item(Known.AdapterConfiguration);
        if (!item.IsPresent || JsonRead.Rows(item.Value) is not { } rows) return JsonValues.Null;
        bool any = rows.Any(r => JsonRead.Array(JsonRead.Get(r, "DefaultIPGateway")).Any(g => g.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(g.GetString())));
        return JsonValues.Bool(any);
    }

    /// <summary>Physische Adapter mit NetConnectionStatus 2 (Connected, Win32_NetworkAdapter).</summary>
    private static JsonElement ActiveAdapters(EvidenceStore store)
    {
        var item = store.Item(Known.Adapter);
        if (!item.IsPresent || JsonRead.Rows(item.Value) is not { } rows) return JsonValues.Null;
        return JsonValues.Number(rows.Count(r => JsonRead.Integer(r, "NetConnectionStatus") == 2));
    }

    /// <summary>
    /// net.wireless: NdisPhysicalMediumType 1 (WirelessLan) oder 9 (Native802_11) in MSNdis_PhysicalMediumType, oder
    /// AdapterTypeID 9 (Wireless) in Win32_NetworkAdapter. Die NDIS-Zahlen folgen der Reihenfolge der Aufzählung auf
    /// Microsofts Seite OID_GEN_PHYSICAL_MEDIUM (gesehen 2026-10-18); AdapterTypeID 9 steht in der Win32_NetworkAdapter-Doku.
    /// </summary>
    internal static JsonElement Wireless(EvidenceStore store)
    {
        bool? result = null;
        var medium = store.Item(Known.PhysicalMedium);
        if (medium.IsPresent && JsonRead.Rows(medium.Value) is { } rows)
        {
            result = rows.Any(r => JsonRead.Integer(r, "NdisPhysicalMediumType") is 1 or 9);
            if (result == true) return JsonValues.True;
        }
        var adapters = store.Item(Known.Adapter);
        if (adapters.IsPresent && JsonRead.Rows(adapters.Value) is { } list)
        {
            if (list.Any(r => JsonRead.Integer(r, "AdapterTypeID") == 9)) return JsonValues.True;
            result ??= false;
        }
        return result is { } x ? JsonValues.Bool(x) : JsonValues.Null;
    }

    /// <summary>net.proxyConfigured: WinHTTP-Standardproxy mit Proxyliste.</summary>
    private static JsonElement Proxy(EvidenceStore store)
    {
        var item = store.Item(ItemKeys.WinHttpProxy);
        if (!item.IsPresent || item.Value.ValueKind != JsonValueKind.Object) return JsonValues.Null;
        if (!JsonRead.TryGet(item.Value, "proxy", out var proxy)) return JsonValues.Null;
        return proxy.ValueKind switch
        {
            JsonValueKind.Null => JsonValues.False,
            JsonValueKind.String => JsonValues.Bool(!string.IsNullOrWhiteSpace(proxy.GetString())),
            _ => JsonValues.Null,
        };
    }
}
