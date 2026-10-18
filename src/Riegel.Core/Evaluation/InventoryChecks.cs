// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Facts;
using Riegel.Core.Model;

namespace Riegel.Core.Evaluation;

/// <summary>
/// Prüfart inventory (spec 2.3): feste Darstellungen, Status info mit einer Liste in den Details. Ist keiner der
/// genannten Einträge lesbar, ist die Bestandsregel „nicht prüfbar“ statt einer leeren, scheinbar beruhigenden Liste.
/// </summary>
internal sealed partial class EvaluationSession
{
    private Outcome Inventory(InventoryCheck c)
    {
        // Alle genannten Einträge lesen (Belege), nicht verfügbare merken
        var evidence = c.Items.Select(key => key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase)
            ? (Key: key, Available: Store.RegistryKey(key).Class != EvidenceClass.Unavailable, Reason: Store.RegistryKey(key).Reason)
            : (Key: key, Available: Store.Item(key).IsPresent, Reason: Store.Item(key).Reason)).ToList();
        if (evidence.Count > 0 && evidence.All(e => !e.Available))
            return Unavailable(evidence[0].Reason, evidence[0].Key, null, null);
        var o = c.Renderer switch
        {
            InventoryRenderer.LocalAdmins => LocalAdmins(c),
            InventoryRenderer.RemoteAccessTools => RemoteAccessTools(c),
            InventoryRenderer.RdpState => RdpState(),
            InventoryRenderer.DefenderExclusions => DefenderExclusions(),
            InventoryRenderer.Shares => Shares(adminOnly: false),
            InventoryRenderer.AdminShares => Shares(adminOnly: true),
            InventoryRenderer.NetworkFacts => NetworkFacts(),
            InventoryRenderer.AppControlState => AppControlState(),
            _ => Wireless(),
        };
        foreach (var e in evidence.Where(e => !e.Available))
            o = o.With(T["Detail_Evidence", Store.Canonical(e.Key), RglCodesEngine.Message(T.Culture, e.Reason ?? RglCodesEngine.ItemNotCollected)]);
        return o with { Source = ValueSource.Runtime };
    }

    private Outcome LocalAdmins(InventoryCheck c)
    {
        var key = c.Items.FirstOrDefault(i => i.StartsWith("api:localgroup:", StringComparison.OrdinalIgnoreCase)) ?? ItemKeys.LocalAdministrators;
        var item = Store.Item(key);
        if (!item.IsPresent || item.Value.ValueKind != JsonValueKind.Array) return Unavailable(item.Reason ?? RglCodesEngine.UnexpectedValue, key, null, null);
        var members = item.Value.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.Object).ToList();
        var o = new Outcome(ResultStatus.Info) { Actual = CanonicalValue.Render(JsonValues.Texts(members.Select(m => JsonRead.String(m, "sid") ?? "?"))) };
        o = o.With(T["Inv_MemberCount", members.Count]);
        foreach (var m in members)
            o = o.With(T["Detail_Member", JsonRead.String(m, "name") ?? "?", JsonRead.String(m, "sid") ?? "?", JsonRead.String(m, "kind") ?? "?"]);
        return o;
    }

    /// <summary>Fernwartungswerkzeuge nach rules/data: genaue Dienstnamen und Teilzeichenketten der Anzeigenamen, ohne Groß/klein, nie als regulärer Ausdruck.</summary>
    private Outcome RemoteAccessTools(InventoryCheck c)
    {
        var tools = Catalogue.Data.RemoteAccessTools;
        if (tools.Count == 0) return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.CatalogueDataMissing).With(T["Detail_NoToolPatterns"]);
        var found = new SortedSet<string>(StringComparer.Ordinal);
        var lines = new SortedSet<string>(StringComparer.Ordinal);
        var services = Store.Item(Known.Service);
        if (services.IsPresent && JsonRead.Rows(services.Value) is { } rows)
            foreach (var row in rows)
            {
                var name = JsonRead.String(row, "Name") ?? "";
                var display = JsonRead.String(row, "DisplayName") ?? "";
                foreach (var tool in tools)
                    if (tool.ServiceNames.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase))
                        || tool.DisplayNameContains.Any(p => p.Length > 0 && display.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        found.Add(tool.Product);
                        lines.Add(T["Inv_ToolService", tool.Product, name, JsonRead.String(row, "State") ?? "?", JsonRead.String(row, "StartMode") ?? "?"]);
                    }
            }
        foreach (var root in c.Items.Where(i => i.StartsWith("reg:", StringComparison.OrdinalIgnoreCase)).DefaultIfEmpty(ItemKeys.Registry(Known.UninstallNative)))
        {
            var k = Store.RegistryKey(root);
            if (k.Class != EvidenceClass.Present) continue;
            foreach (var sub in k.Subkeys)
            {
                var display = Store.RegistryValue(root + "\\" + sub, "DisplayName");
                if (!display.IsPresent || display.V is not { ValueKind: JsonValueKind.String } text) continue;
                var shown = text.GetString()!;
                foreach (var tool in tools)
                    if (tool.DisplayNameContains.Any(p => p.Length > 0 && shown.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        found.Add(tool.Product);
                        lines.Add(T["Inv_ToolInstalled", tool.Product, shown]);
                    }
            }
        }
        var o = new Outcome(ResultStatus.Info) { Actual = CanonicalValue.Render(JsonValues.Texts(found)) };
        if (found.Count == 0) o = o.With(T["Inv_NoTools"]);
        return lines.Aggregate(o, (x, l) => x.With(l));
    }

    private Outcome RdpState()
    {
        string Show(RegValueEvidence v) => v.Class switch
        {
            EvidenceClass.Present => v.Raw is { } r ? CanonicalValue.RenderRegistry(r) ?? "?" : "?",
            EvidenceClass.NotSet => T.Value(null),
            _ => T.ItemStatus(null),
        };
        var enabled = Facts.Bool(FactNames.RdpEnabled);
        var o = new Outcome(ResultStatus.Info) { Actual = enabled switch { true => "true", false => "false", _ => null } };
        o = o.With(T["Inv_RdpEnabled", enabled switch { true => T["Word_Yes"], false => T["Word_No"], _ => T["Word_Unknown"] }]);
        o = o.With(T["Inv_RdpNla", Show(Store.RegistryValue(Known.TerminalServicesPolicy, "UserAuthentication")), Show(Store.RegistryValue(Known.RdpTcp, "UserAuthentication"))]);
        o = o.With(T["Inv_RdpPort", Show(Store.RegistryValue(Known.RdpTcp, "PortNumber"))]);
        var services = Store.Item(Known.Service);
        if (services.IsPresent && JsonRead.Rows(services.Value) is { } rows
            && rows.FirstOrDefault(r => string.Equals(JsonRead.String(r, "Name"), "TermService", StringComparison.OrdinalIgnoreCase)) is { ValueKind: JsonValueKind.Object } term)
            o = o.With(T["Inv_RdpService", JsonRead.String(term, "State") ?? "?", JsonRead.String(term, "StartMode") ?? "?"]);
        var users = Store.Item(ItemKeys.LocalRemoteDesktopUsers);
        if (users.IsPresent && users.Value.ValueKind == JsonValueKind.Array) o = o.With(T["Inv_RdpUsers", users.Value.GetArrayLength()]);
        return o;
    }

    /// <summary>Ausnahmen von Defender: MpPreference (nur als Administrator lesbar), sonst Richtlinien- und lokale Registry.</summary>
    private Outcome DefenderExclusions()
    {
        var lists = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        bool readable = false;
        var pref = Store.Item(Known.MpPreference);
        if (pref.IsPresent && JsonRead.Rows(pref.Value) is { Count: > 0 } rows)
        {
            readable = true;
            foreach (var property in new[] { "ExclusionPath", "ExclusionExtension", "ExclusionProcess", "ExclusionIpAddress" })
            {
                var v = JsonRead.Get(rows[0], property);
                if (v is { } value && IsAdminOnlyNotice(value)) { readable = false; break; }
                foreach (var e in JsonRead.Array(v).Where(e => e.ValueKind == JsonValueKind.String))
                    lists.Add(T["Inv_Exclusion", property["Exclusion".Length..], e.GetString()!]);
            }
        }
        if (!readable)
        {
            lists.Clear();
            foreach (var root in new[] { Known.DefenderExclusionsPolicy, Known.DefenderExclusionsLocal })
                foreach (var (sub, label) in new[] { ("Paths", "Path"), ("Extensions", "Extension"), ("Processes", "Process"), ("IpAddresses", "IpAddress") })
                {
                    var k = Store.RegistryKey(root + "\\" + sub);
                    if (k.Class == EvidenceClass.Unavailable) return new Outcome(ResultStatus.NotCheckable, k.Reason ?? RglCodesEngine.ItemDenied).With(T["Detail_ExclusionsNeedAdmin"]);
                    if (k.Class == EvidenceClass.NotSet) continue;
                    if (JsonRead.Get(k.Value, "values") is { ValueKind: JsonValueKind.Object } values)
                        foreach (var p in values.EnumerateObject()) lists.Add(T["Inv_Exclusion", label, p.Name]);
                }
        }
        var o = new Outcome(ResultStatus.Info) { Actual = lists.Count.ToString(CultureInfo.InvariantCulture) };
        if (lists.Count == 0) o = o.With(T["Inv_NoExclusions"]);
        return lists.Aggregate(o, (x, l) => x.With(l));
    }

    /// <summary>
    /// Freigaben aus Win32_Share: Typ mit gesetztem Bit 0x80000000 = Verwaltungsfreigabe (ADMIN$, C$, IPC$). Für die
    /// übrigen werden Zugriffe für Jeder (S-1-1-0), Anonym (S-1-5-7) und Gäste (S-1-5-32-546) hervorgehoben.
    /// </summary>
    private Outcome Shares(bool adminOnly)
    {
        var item = Store.Item(Known.Share);
        if (!item.IsPresent || JsonRead.Rows(item.Value) is not { } rows) return Unavailable(item.Reason ?? RglCodesEngine.UnexpectedValue, Known.Share, null, null);
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>();
        foreach (var row in rows)
        {
            var name = JsonRead.String(row, "Name") ?? "?";
            var type = JsonRead.Integer(row, "Type") ?? 0;
            bool isAdmin = (type & 0x80000000L) != 0;
            if (isAdmin != adminOnly) continue;
            names.Add(name);
            var open = new SortedSet<string>(StringComparer.Ordinal);
            if (JsonRead.Get(row, "GetSecurityDescriptor") is { } sd)
                foreach (var ace in JsonRead.Array(JsonRead.Get(sd, "Aces")))
                {
                    var sid = JsonRead.String(ace, "Sid") ?? "";
                    if (sid is "S-1-1-0" or "S-1-5-7" or "S-1-5-32-546" && JsonRead.Integer(ace, "AceType") is null or 0) open.Add(sid);
                }
            lines.Add(open.Count > 0
                ? T["Inv_ShareOpen", name, JsonRead.String(row, "Path") ?? "", string.Join(", ", open)]
                : T["Inv_Share", name, JsonRead.String(row, "Path") ?? ""]);
        }
        if (adminOnly)
            foreach (var value in new[] { "AutoShareServer", "AutoShareWks" })
            {
                var v = Store.RegistryValue(Known.LanmanServer, value);
                if (v.IsPresent && v.Raw is { } raw) lines.Add(T["Inv_AutoShare", value, CanonicalValue.RenderRegistry(raw)]);
            }
        var o = new Outcome(ResultStatus.Info) { Actual = CanonicalValue.Render(JsonValues.Texts(names)) };
        if (names.Count == 0) o = o.With(T["Inv_NoShares"]);
        return lines.Aggregate(o, (x, l) => x.With(l));
    }

    private Outcome NetworkFacts()
    {
        string Show(string fact) => Facts.Get(fact) is { } v ? CanonicalValue.Render(v) ?? "" : T["Word_Unknown"];
        var o = new Outcome(ResultStatus.Info);
        foreach (var fact in new[] { FactNames.DomainJoined, FactNames.NetGatewayConfigured, FactNames.NetActiveAdapters, FactNames.NetWireless,
                     FactNames.NetIpForwarding, FactNames.NetProxyConfigured, FactNames.RdpEnabled })
            o = o.With(T["Inv_Fact", fact, Show(fact)]);
        return o;
    }

    /// <summary>App Control (Win32_DeviceGuard: 0 aus, 1 Überwachung, 2 erzwungen), Smart App Control, Treiber-Sperrliste, AppLocker.</summary>
    private Outcome AppControlState()
    {
        var o = new Outcome(ResultStatus.Info);
        var dg = Store.Item(Known.DeviceGuard);
        if (dg.IsPresent && JsonRead.Rows(dg.Value) is { Count: > 0 } rows)
        {
            o = o.With(T["Inv_CodeIntegrity", JsonRead.Integer(rows[0], "CodeIntegrityPolicyEnforcementStatus")?.ToString(CultureInfo.InvariantCulture) ?? "?",
                JsonRead.Integer(rows[0], "UsermodeCodeIntegrityPolicyEnforcementStatus")?.ToString(CultureInfo.InvariantCulture) ?? "?"]);
            o = o with { Actual = JsonRead.Integer(rows[0], "UsermodeCodeIntegrityPolicyEnforcementStatus")?.ToString(CultureInfo.InvariantCulture) };
        }
        var sac = Store.RegistryValue(Known.CodeIntegrityPolicy, "VerifiedAndReputablePolicyState");
        if (sac.IsPresent && sac.Raw is { } s) o = o.With(T["Inv_SmartAppControl", CanonicalValue.RenderRegistry(s)]);
        var blocklist = Store.RegistryValue(Known.CodeIntegrityConfig, "VulnerableDriverBlocklistEnable");
        if (blocklist.IsPresent && blocklist.Raw is { } b) o = o.With(T["Inv_DriverBlocklist", CanonicalValue.RenderRegistry(b)]);
        var appLocker = Store.RegistryKey(Known.AppLockerPolicy);
        o = o.With(T["Inv_AppLocker", appLocker.Class switch
        {
            EvidenceClass.Present => string.Join(", ", appLocker.Subkeys),
            EvidenceClass.NotSet => T["Word_None"],
            _ => T["Word_Unknown"],
        }]);
        var services = Store.Item(Known.Service);
        if (services.IsPresent && JsonRead.Rows(services.Value) is { } list
            && list.FirstOrDefault(r => string.Equals(JsonRead.String(r, "Name"), "AppIDSvc", StringComparison.OrdinalIgnoreCase)) is { ValueKind: JsonValueKind.Object } svc)
            o = o.With(T["Inv_AppIdService", JsonRead.String(svc, "State") ?? "?", JsonRead.String(svc, "StartMode") ?? "?"]);
        return o;
    }

    private Outcome Wireless()
    {
        var wireless = Facts.Bool(FactNames.NetWireless);
        var o = new Outcome(ResultStatus.Info) { Actual = wireless switch { true => "true", false => "false", _ => null } };
        o = o.With(T["Inv_Wireless", wireless switch { true => T["Word_Yes"], false => T["Word_No"], _ => T["Word_Unknown"] }]);
        var medium = Store.Item(Known.PhysicalMedium);
        if (medium.IsPresent && JsonRead.Rows(medium.Value) is { } rows)
            foreach (var r in rows.Where(r => JsonRead.Integer(r, "NdisPhysicalMediumType") is 1 or 9))
                o = o.With(T["Inv_WirelessAdapter", JsonRead.String(r, "InstanceName") ?? "?", JsonRead.Bool(r, "Active") == true ? T["Word_Yes"] : T["Word_No"]]);
        return o;
    }
}
