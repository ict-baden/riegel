// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Core.Evaluation;

/// <summary>Prüfarten firewallProfile und firewallExposure (spec 2.3).</summary>
internal sealed partial class EvaluationSession
{
    /// <summary>Profilzustand aus api:firewall (FwPolicy2, Laufzeit).</summary>
    private (ItemEvidence Item, JsonElement? Profile) FirewallRuntime(FirewallProfileName profile)
    {
        var item = Store.Item(ItemKeys.Firewall);
        if (!item.IsPresent) return (item, null);
        if (JsonRead.Get(item.Value, "profiles") is not { ValueKind: JsonValueKind.Object } profiles
            || JsonRead.Get(profiles, Known.ProfileName(profile)) is not { ValueKind: JsonValueKind.Object } p)
            return (item with { Class = EvidenceClass.Unavailable, Reason = RglCodesEngine.UnexpectedValue }, null);
        return (item, p);
    }

    /// <summary>Standard eingehend: „block“/„allow“ (Fixture-Kodierung) bzw. NET_FW_ACTION 0 = blockieren.</summary>
    private static bool? InboundBlocks(JsonElement profile) => JsonRead.Get(profile, "defaultInbound") switch
    {
        { ValueKind: JsonValueKind.String } s => s.GetString()!.ToLowerInvariant() switch { "block" => true, "allow" => false, _ => null },
        { ValueKind: JsonValueKind.Number } n when n.TryGetInt32(out var i) => i switch { 0 => true, 1 => false, _ => null },
        _ => null,
    };

    /// <summary>
    /// Wert eines Profils in der Registry: erst der Richtlinienspeicher (GPO), dann der lokale Speicher (DISA STIG
    /// V-241995/V-242000). Ergebnis: Wert, Beleg-Schlüssel, oder nicht verfügbar; beide nicht gesetzt → (null, NotSet).
    /// </summary>
    private (RegValueEvidence? Value, EvidenceClass Class, string? Reason) FirewallRegistry(FirewallProfileName profile, string subkey, string name)
    {
        var (policy, local) = Known.FirewallProfileKeys(profile);
        foreach (var root in new[] { policy, local })
        {
            var v = Store.RegistryValue(subkey.Length == 0 ? root : root + "\\" + subkey, name);
            if (v.Class == EvidenceClass.NotSet) continue;
            if (v.Class == EvidenceClass.Unavailable) return (v, EvidenceClass.Unavailable, v.Reason);
            return (v, EvidenceClass.Present, null);
        }
        return (null, EvidenceClass.NotSet, null);
    }

    /// <summary>
    /// Firewall-Profil: enabled und defaultInboundBlock aus der Laufzeit (COM), ohne Laufzeit „nicht prüfbar“ (spec 2.9);
    /// logDropped und allowLocalRules aus Richtlinie, dann lokal. Nicht gesetzt heißt „nicht konfiguriert“: das ist ein
    /// Befund, wenn die Regel die gehärtete Einstellung verlangt (logDropped true, allowLocalRules false), sonst ohne
    /// belegten Standardwert nicht prüfbar.
    /// </summary>
    private Outcome FirewallProfile(FirewallProfileCheck c)
    {
        var expected = c.Expected ? "true" : "false";
        if (c.Property is FirewallProfileProperty.Enabled or FirewallProfileProperty.DefaultInboundBlock)
        {
            var (item, profile) = FirewallRuntime(c.Profile);
            if (profile is not { } p) return Unavailable(item.Reason, item.Key, null, expected);
            bool? actual = c.Property == FirewallProfileProperty.Enabled ? JsonRead.Bool(p, "enabled") : InboundBlocks(p);
            if (actual is null) return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.UnexpectedValue) { Expected = expected };
            var o = new Outcome(actual == c.Expected ? ResultStatus.Pass : ResultStatus.Fail)
            {
                Expected = expected,
                Actual = actual.Value ? "true" : "false",
                Source = ValueSource.Runtime,
            }.DecidedBy(ItemKeys.Firewall);
            // Konfigurierter Wert als Kontext (spec 2.4 Punkt 6)
            var (configured, cls, _) = FirewallRegistry(c.Profile, "", c.Property == FirewallProfileProperty.Enabled ? "EnableFirewall" : "DefaultInboundAction");
            if (cls == EvidenceClass.Present && configured?.V is { } cv) o = o.With(T["Detail_Configured", CanonicalValue.Render(cv)]);
            return o;
        }
        var (value, evidenceClass, reason) = c.Property == FirewallProfileProperty.LogDropped
            ? FirewallRegistry(c.Profile, "Logging", "LogDroppedPackets")
            : FirewallRegistry(c.Profile, "", "AllowLocalPolicyMerge");
        if (evidenceClass == EvidenceClass.Unavailable) return Unavailable(reason, value?.ItemKey ?? Known.FirewallPolicyLocal, null, expected);
        if (evidenceClass == EvidenceClass.NotSet)
        {
            bool hardened = c.Property == FirewallProfileProperty.LogDropped ? c.Expected : !c.Expected;
            return hardened
                ? new Outcome(ResultStatus.Fail) { Expected = expected, Source = ValueSource.NotSet }.With(T["Detail_NotConfigured"])
                : new Outcome(ResultStatus.NotCheckable, RglCodesEngine.DefaultUnknown) { Expected = expected, Source = ValueSource.NotSet };
        }
        var (typed, wrong) = ConvertRegistry(value!.Type, value.V, RegValueType.Dword, null);
        if (wrong || typed is not Typed.Num n)
            return new Outcome(ResultStatus.Fail) { Expected = expected, Actual = value.Raw is { } r ? CanonicalValue.RenderRegistry(r) : null }
                .With(T["Detail_WrongType", value.Type ?? "?", "dword"]);
        bool on = n.Value != 0;
        var (source, detail) = SourceOf(value.ItemKey, value.ValueName, null);
        return new Outcome(on == c.Expected ? ResultStatus.Pass : ResultStatus.Fail)
        {
            Expected = expected,
            Actual = on ? "true" : "false",
            Source = source,
            SourceDetail = detail,
        }.DecidedBy(value.ItemKey);
    }

    // ---------------------------------------------------------------- firewallExposure

    private static readonly FirewallProfileName[] AllProfiles = [FirewallProfileName.Domain, FirewallProfileName.Private, FirewallProfileName.Public];

    /// <summary>Eine eingehende Freigabe aus COM oder einem Registry-Speicher, vereinheitlicht.</summary>
    private sealed record FwRule(string Name, string Store, bool? Enabled, bool? Inbound, bool? Allow, int? Protocol,
        IReadOnlyList<string> Ports, bool? AnyRemote, int? ProfileMask);

    /// <summary>
    /// Eingehende Freigaben für die Ports von beliebigen Adressen im wirksamen Regelsatz: COM-Regeln und
    /// parsed:firewall-rules-registry (GPO- und lokaler Speicher); lokale Regeln zählen nicht in Profilen, in denen die
    /// Zusammenführung aus ist. Ein abgeschaltetes Profil oder „eingehend erlauben“ ist eine Freigabe für alle Ports.
    /// Fail bei einem sicheren Treffer; „nicht prüfbar“, wenn ein Teil des Regelsatzes fehlt oder unklar ist.
    /// </summary>
    private Outcome FirewallExposure(FirewallExposureCheck c)
    {
        var expected = "[]";
        var api = Store.Item(ItemKeys.Firewall);
        if (!api.IsPresent) return Unavailable(api.Reason, api.Key, null, expected);
        var fw = api.Value;
        var considered = new List<FirewallProfileName>();
        if (c.Profiles == ProfileScope.All) considered.AddRange(AllProfiles);
        else
        {
            if (JsonRead.Integer(fw, "currentProfiles") is not { } current || current <= 0)
                return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.UnexpectedValue) { Expected = expected }.With(T["Detail_ActiveProfileUnknown"]);
            considered.AddRange(AllProfiles.Where(p => (current & Known.ProfileBit(p)) != 0));
        }
        var definite = new SortedSet<string>(StringComparer.Ordinal);
        var uncertain = new SortedSet<string>(StringComparer.Ordinal);
        var exposedPorts = new SortedSet<string>(StringComparer.Ordinal);
        string PortText(PortSpec p) => (p.Protocol == NetProtocol.Tcp ? "tcp/" : "udp/") + p.Port.ToString(CultureInfo.InvariantCulture);

        // Profil aus oder eingehend erlaubt: alles offen
        foreach (var profile in considered)
        {
            var (_, state) = FirewallRuntime(profile);
            if (state is not { } s) { uncertain.Add(T["Detail_ProfileStateUnknown", Known.ProfileName(profile)]); continue; }
            var enabled = JsonRead.Bool(s, "enabled");
            var blocks = InboundBlocks(s);
            if (enabled == false || blocks == false)
            {
                definite.Add(T[enabled == false ? "Detail_ProfileOff" : "Detail_ProfileInboundAllow", Known.ProfileName(profile)]);
                foreach (var port in c.Ports) exposedPorts.Add(PortText(port));
            }
            else if (enabled is null || blocks is null) uncertain.Add(T["Detail_ProfileStateUnknown", Known.ProfileName(profile)]);
        }

        bool rulesCollected = JsonRead.Bool(fw, "rulesCollected") == true;
        var parsed = Store.Item(ItemKeys.ParsedFirewallRulesRegistry);
        bool registryKnown = parsed.IsPresent && parsed.Value.ValueKind == JsonValueKind.Array;
        if (!rulesCollected) uncertain.Add(T["Detail_RulesNotCollected"]);
        if (!registryKnown) uncertain.Add(T["Detail_RegistryRulesMissing", Store.Canonical(ItemKeys.ParsedFirewallRulesRegistry)]);

        // Zusammenführung lokaler Regeln je Profil: true, false oder null (unbekannt); nicht gesetzt = Windows-Standard (an)
        var merge = new Dictionary<FirewallProfileName, bool?>();
        var mergeKeys = new Dictionary<FirewallProfileName, string[]>();
        var mergeUsed = new HashSet<FirewallProfileName>();
        foreach (var profile in considered)
        {
            var (policyKey, localKey) = Known.FirewallProfileKeys(profile);
            var (value, cls, _) = FirewallRegistry(profile, "", "AllowLocalPolicyMerge");
            // Ein gesetzter Wert entscheidet allein; „nicht gesetzt“ stützt sich auf beide Speicher
            mergeKeys[profile] = cls == EvidenceClass.Present && value is not null
                ? [value.ItemKey]
                : [EvidenceStore.RegistryItemKey(policyKey), EvidenceStore.RegistryItemKey(localKey)];
            merge[profile] = cls switch
            {
                EvidenceClass.NotSet => true,
                EvidenceClass.Present when ConvertRegistry(value!.Type, value.V, RegValueType.Dword, null).Value is Typed.Num n => n.Value != 0,
                _ => null,
            };
        }

        var rules = new List<FwRule>();
        if (rulesCollected)
            foreach (var r in JsonRead.Array(JsonRead.Get(fw, "rules"))) rules.Add(ComRule(r));
        if (registryKnown)
            foreach (var r in parsed.Value.EnumerateArray()) rules.Add(RegistryStoreRule(r));

        foreach (var rule in rules)
        {
            if (rule.Enabled == false || rule.Inbound == false || rule.Allow == false) continue;
            bool protocolUnclear = rule.Protocol == -1;
            bool unclearShape = rule.Enabled is null || rule.Inbound is null || rule.Allow is null || rule.AnyRemote is null || rule.ProfileMask is null
                                || protocolUnclear;
            if (rule.AnyRemote == false) continue;
            foreach (var port in c.Ports)
            {
                if ((!protocolUnclear && !ProtocolMatches(rule.Protocol, port.Protocol)) || !PortMatches(rule.Ports, port.Port)) continue;
                foreach (var profile in considered)
                {
                    if (rule.ProfileMask is { } mask && (mask & Known.ProfileBit(profile)) == 0) continue;
                    var line = T["Detail_RuleExposes", rule.Name, PortText(port), Known.ProfileName(profile)];
                    if (unclearShape) { uncertain.Add(line); continue; }
                    // GPO-Regeln gelten immer; lokale (und COM-Regeln ohne bekannten Speicher) nur bei Zusammenführung
                    if (rule.Store != "gpo") mergeUsed.Add(profile);
                    bool? effective = rule.Store switch
                    {
                        "gpo" => true,
                        "local" => merge[profile],
                        _ => merge[profile] == true ? true : merge[profile] == false && registryKnown ? false : null,
                    };
                    if (effective == true) { definite.Add(line); exposedPorts.Add(PortText(port)); }
                    else if (effective is null) uncertain.Add(line);
                }
            }
        }

        var actual = CanonicalValue.Render(JsonValues.Texts(exposedPorts));
        // Entscheidend: Laufzeit, Registry-Regeln und die Zusammenführung nur dort, wo sie eine Regel ein- oder ausschloss
        var outcome = new Outcome(ResultStatus.Fail) { Expected = expected, Actual = actual, Source = ValueSource.Runtime }
            .DecidedBy([ItemKeys.Firewall, ItemKeys.ParsedFirewallRulesRegistry, .. mergeUsed.SelectMany(p => mergeKeys[p])]);
        if (definite.Count > 0) return definite.Aggregate(outcome, (o, d) => o.With(d));
        if (uncertain.Count > 0)
            return uncertain.Aggregate(outcome with { Status = ResultStatus.NotCheckable, ReasonCode = rulesCollected ? RglCodesEngine.NoDecisiveEvidence : RglCodesEngine.ItemNotCollected },
                (o, d) => o.With(d));
        return outcome with { Status = ResultStatus.Pass };
    }

    /// <summary>Regel aus api:firewall (FwPolicy2): direction in/out, action allow/block, Protokoll 6/17/256, Profile als Bitmaske.</summary>
    private static FwRule ComRule(JsonElement r)
    {
        static bool? Word(JsonElement r, string name, string yes, string no) => JsonRead.String(r, name)?.ToLowerInvariant() switch
        {
            var v when v == yes => true,
            var v when v == no => false,
            _ => null,
        };
        var remote = JsonRead.String(r, "remoteAddresses");
        bool? anyRemote = JsonRead.TryGet(r, "remoteAddresses", out var ra) && ra.ValueKind == JsonValueKind.Null ? true
            : remote is null ? null : IsAnyAddress(remote.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        var ports = JsonRead.String(r, "localPorts");
        return new FwRule(JsonRead.String(r, "name") ?? "?", "com", JsonRead.Bool(r, "enabled"), Word(r, "direction", "in", "out"),
            Word(r, "action", "allow", "block"), ProtocolOf(JsonRead.Get(r, "protocol")),
            ports is null ? ["*"] : ports.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            anyRemote, JsonRead.Integer(r, "profiles") is { } p ? (int)(p & 0x7) : null);
    }

    /// <summary>Regel aus parsed:firewall-rules-registry ([MS-GPFAS]-Grammatik, vom Paket parsing zerlegt).</summary>
    private static FwRule RegistryStoreRule(JsonElement r)
    {
        static IReadOnlyList<string> List(JsonElement r, string name) => JsonRead.Get(r, name) switch
        {
            { ValueKind: JsonValueKind.Array } a => [.. a.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.Trim())],
            { ValueKind: JsonValueKind.String } s => s.GetString()!.Split([',', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            _ => [],
        };
        bool? active = JsonRead.Get(r, "active") switch
        {
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.False } => false,
            { ValueKind: JsonValueKind.String } s => s.GetString()!.ToUpperInvariant() switch { "TRUE" => true, "FALSE" => false, _ => null },
            _ => null,
        };
        static bool? Word(string? v, string yes, string no) => v?.ToLowerInvariant() switch
        {
            var x when x == yes => true,
            var x when x == no => false,
            _ => null,
        };
        var remote = List(r, "ra");
        var profiles = List(r, "profiles");
        int? mask = profiles.Count == 0 ? 7 : 0;
        foreach (var p in profiles)
            mask = p.ToLowerInvariant() switch
            {
                "domain" => mask | 1,
                "private" => mask | 2,
                "public" => mask | 4,
                _ => null,
            };
        var ports = List(r, "lports");
        return new FwRule(JsonRead.String(r, "name") ?? "?", (JsonRead.String(r, "store") ?? "").ToLowerInvariant(), active,
            Word(JsonRead.String(r, "dir"), "in", "out"), Word(JsonRead.String(r, "action"), "allow", "block"),
            ProtocolOf(JsonRead.Get(r, "protocol")), ports.Count == 0 ? ["*"] : ports, remote.Count == 0 ? true : IsAnyAddress(remote), mask);
    }

    /// <summary>Beliebige Adresse: „*“, „Any“ oder leer (spec 2.3).</summary>
    private static bool IsAnyAddress(IReadOnlyList<string> addresses) =>
        addresses.Count == 0 || addresses.Any(a => a == "*" || string.Equals(a, "Any", StringComparison.OrdinalIgnoreCase));

    /// <summary>Protokollnummer; null = jedes Protokoll (fehlt, 256 = NET_FW_IP_PROTOCOL_ANY, „Any“).</summary>
    private static int? ProtocolOf(JsonElement? v) => v switch
    {
        null => null,
        { ValueKind: JsonValueKind.Number } n when n.TryGetInt32(out var i) => i == 256 ? null : i,
        { ValueKind: JsonValueKind.String } s => s.GetString()!.ToUpperInvariant() switch
        {
            "TCP" => 6,
            "UDP" => 17,
            "ANY" or "*" or "" => null,
            var t when int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var j) => j == 256 ? null : j,
            _ => -1,
        },
        _ => -1,
    };

    private static bool ProtocolMatches(int? ruleProtocol, NetProtocol wanted) =>
        ruleProtocol is null || ruleProtocol == (wanted == NetProtocol.Tcp ? 6 : 17);

    /// <summary>
    /// Ports „*“/„Any“, Zahlen und Bereiche „a-b“. Schlüsselwörter für dynamische oder dienstbezogene Ports ([MS-FASP],
    /// z. B. RPC, RPC-EPMap) treffen die festen Ports der Regel nicht.
    /// </summary>
    private static bool PortMatches(IReadOnlyList<string> ports, int port)
    {
        foreach (var token in ports)
        {
            if (token == "*" || string.Equals(token, "Any", StringComparison.OrdinalIgnoreCase)) return true;
            int dash = token.IndexOf('-');
            if (dash > 0 && int.TryParse(token[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var lo)
                && int.TryParse(token[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var hi))
            {
                if (port >= lo && port <= hi) return true;
            }
            else if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var single) && single == port) return true;
        }
        return false;
    }
}
