// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Riegel.Tools.SnapshotAnonymizer;

/// <summary>
/// Anonymisiert einen aufgezeichneten Export zu einer Fixture (spec 8.1): ersetzt Hostname, Domänen, Benutzernamen,
/// Rechner-SID, MachineGuid, Adapter-GUIDs, Schlüsselschutz-IDs, Defender-Ausnahmen, Programmpfade fremder
/// Firewall-Regeln, fremde Software und Dienste, Gateways (→ RFC 5737) und Lizenz-/Zertifikatskennungen. Was auf
/// der <see cref="AllowList"/> steht, bleibt. Herkunft wird „recorded“. Dieselbe Zuordnung gilt für die Rohdateien
/// (secedit.inf, auditpol-CSV, gpresult.xml) desselben Laufs.
/// </summary>
internal sealed partial class Anonymizer
{
    private readonly string _host;
    private readonly string _hostReplacement;
    private readonly Dictionary<string, string> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _sidPrefixes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _guids = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _domains = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _gateways = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _machineGuid;
    private int _software, _services, _rules, _adapters, _shares, _exclusions, _updates;

    public const string ReplacementGuid = "00000000-0000-4000-8000-0000000000a1";

    public Anonymizer(JsonObject snapshot, string hostReplacement)
    {
        _hostReplacement = hostReplacement;
        var machine = snapshot["machine"]!.AsObject();
        _host = (string)machine["hostName"]!;
        _machineGuid = (string?)machine["machineGuid"];
        var items = Items(snapshot);

        // Domänen (DNS) aus Kopf und Tcpip\Parameters
        foreach (var domain in new[] { (string?)machine["dnsDomain"] }.Concat(TcpipDomains(items)))
            foreach (var part in (domain ?? "").Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (part.Contains('.') && !_domains.ContainsKey(part)) _domains[part] = "example.invalid";

        // Rechner-SIDs: S-1-5-21-a-b-c → S-1-5-21-1000-2000-3000 (weitere Domänen 1100-2100-3100 …)
        foreach (Match m in DomainSid().Matches(snapshot.ToJsonString()))
        {
            string prefix = m.Value;
            if (AllowList.IsWellKnownSid(prefix + "-1") || _sidPrefixes.ContainsKey(prefix)) continue;
            int n = _sidPrefixes.Count;
            _sidPrefixes[prefix] = $"S-1-5-21-{1000 + n * 100}-{2000 + n * 100}-{3000 + n * 100}";
        }

        // Lokale Benutzer: eingebaute Konten mit ihrem Standardnamen bleiben, alle anderen werden user-NN
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in Rows(items, "api:netuserenum"))
        {
            string? name = (string?)user["name"];
            long? rid = user["rid"]?.GetValue<long>();
            if (name is null) continue;
            if (rid is not null && AllowList.BuiltInAccounts.TryGetValue(rid.Value, out var builtIn) && builtIn.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            names.Add(name);
        }
        foreach (var row in WmiRows(items, @"wmi:root\cimv2:Win32_UserAccount"))
        {
            string? name = (string?)row["Name"], sid = (string?)row["SID"];
            long rid = sid is null ? -1 : long.Parse(sid[(sid.LastIndexOf('-') + 1)..], CultureInfo.InvariantCulture);
            if (name is null || (AllowList.BuiltInAccounts.TryGetValue(rid, out var builtIn) && builtIn.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            names.Add(name);
        }
        foreach (var key in new[] { "api:localgroup:S-1-5-32-544", "api:localgroup:S-1-5-32-555" })
            foreach (var member in Rows(items, key))
                if ((string?)member["kind"] == "user" && string.Equals((string?)member["domain"], _host, StringComparison.OrdinalIgnoreCase)
                    && member["name"] is JsonValue v && !AllowList.BuiltInAccounts.Values.Contains((string)v!, StringComparer.OrdinalIgnoreCase))
                    names.Add((string)v!);
        foreach (var value in new[] { "DefaultUserName", "AltDefaultUserName", "LastUsedUsername" })
            if (RegValue(items, @"reg:HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", value) is string s && s.Length > 0
                && !AllowList.BuiltInAccounts.Values.Contains(s, StringComparer.OrdinalIgnoreCase)) names.Add(s[(s.LastIndexOf('\\') + 1)..]);
        foreach (var name in names) _users[name] = $"user-{_users.Count + 1:00}";

        // GUIDs der Adapter und der BitLocker-Schlüsselschutze
        int b = 0, c = 0;
        foreach (var row in WmiRows(items, @"wmi:root\cimv2:Win32_NetworkAdapter")) AddGuid((string?)row["GUID"], 'b', ref b);
        foreach (var row in WmiRows(items, @"wmi:root\cimv2:Win32_NetworkAdapterConfiguration")) AddGuid((string?)row["SettingID"], 'b', ref b);
        foreach (var item in items)
            foreach (Match m in AdapterGuidInKey().Matches((string)item["key"]!)) AddGuid(m.Groups[1].Value, 'b', ref b);
        foreach (var row in WmiRows(items, @"wmi:root\cimv2\Security\MicrosoftVolumeEncryption:Win32_EncryptableVolume"))
            if (row["GetKeyProtectors"]?["VolumeKeyProtectorID"] is JsonArray ids)
                foreach (var id in ids) AddGuid((string?)id, 'c', ref c);
    }

    private void AddGuid(string? guid, char kind, ref int n)
    {
        if (string.IsNullOrEmpty(guid)) return;
        string bare = guid.Trim('{', '}');
        if (!Guid.TryParse(bare, out _) || _guids.ContainsKey(bare)) return;
        n++;
        _guids[bare] = $"00000000-0000-4000-8000-{(kind == 'b' ? 0xb00 : 0xc00) + n:x12}";
    }

    // ------------------------------------------------------------------ Export

    public JsonObject Snapshot(JsonObject snapshot)
    {
        snapshot["origin"] = "recorded";
        var machine = snapshot["machine"]!.AsObject();
        if (machine["dnsDomain"] is not null) machine["dnsDomain"] = "example.invalid";
        if (_machineGuid is not null) machine["machineGuid"] = ReplacementGuid;

        foreach (var item in Items(snapshot))
        {
            string key = (string)item["key"]!;
            if (item["value"] is not JsonNode value) continue;
            switch (key)
            {
                case @"wmi:root\cimv2:Win32_Service": Services(value); break;
                case @"wmi:root\cimv2:Win32_ComputerSystem": Hardware(value); break;
                case @"wmi:root\cimv2:Win32_NetworkAdapter": foreach (var r in Each(value)) r["Name"] = AdapterName((string?)r["Name"]); break;
                case @"wmi:root\wmi:MSNdis_PhysicalMediumType": foreach (var r in Each(value)) r["InstanceName"] = AdapterName((string?)r["InstanceName"]); break;
                case @"wmi:root\cimv2:Win32_NetworkAdapterConfiguration": foreach (var r in Each(value)) r["DefaultIPGateway"] = Gateways(r["DefaultIPGateway"]); break;
                case @"wmi:root\cimv2:Win32_Share": Shares(value); break;
                case @"wmi:root\Microsoft\Windows\Defender:MSFT_MpPreference": Exclusions(value); break;
                case @"wmi:root\SecurityCenter2:AntivirusProduct": foreach (var r in Each(value)) r["displayName"] = Antivirus((string?)r["displayName"]); break;
                case "api:wsc-av": foreach (var r in value.AsArray().OfType<JsonObject>()) r["name"] = Antivirus((string?)r["name"]); break;
                case "api:firewall": FirewallRules(value); break;
                case "api:windows-update-history": UpdateTitles(value); break;
                case "api:winhttp-proxy":
                    if (value["proxy"] is not null) value["proxy"] = "proxy.example.invalid:8080";
                    if (value["bypass"] is not null) value["bypass"] = "<local>";
                    break;
            }
            if (key.StartsWith("reg:", StringComparison.Ordinal)) Registry(item, key, value.AsObject());
        }
        UninstallEntries(snapshot);
        return (JsonObject)Strings(snapshot)!;
    }

    // ------------------------------------------------------------------ Rohdateien

    public string Text(string text)
    {
        foreach (var (from, to) in _gateways) text = text.Replace(from, to, StringComparison.Ordinal);
        return Replace(text);
    }

    // ------------------------------------------------------------------ Regeln je Eintrag

    private void Services(JsonNode value)
    {
        foreach (var row in Each(value))
        {
            if (AllowList.IsSystemPath((string?)row["PathName"])) continue;
            int n = ++_services;
            row["Name"] = $"example-service-{n:00}";
            row["DisplayName"] = $"Example Service {n:00}";
            row["PathName"] = $@"C:\Program Files\Example\service-{n:00}.exe";
            string? account = (string?)row["StartName"];
            if (account is not null && !AllowList.ServiceAccounts.Any(a => account.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
                row["StartName"] = @".\service-account";
        }
    }

    private static void Hardware(JsonNode value)
    {
        foreach (var row in Each(value))
        {
            // Hersteller und Modell nur bei Microsoft (virtuelle Maschine des Runners) stehen lassen
            if (AllowList.IsMicrosoftName((string?)row["Manufacturer"])) continue;
            row["Manufacturer"] = "Example Manufacturer";
            row["Model"] = "Example Model";
        }
    }

    private readonly Dictionary<string, string> _adapterNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adapternamen fremder Hersteller; derselbe Adapter heißt in Win32_NetworkAdapter und MSNdis gleich.</summary>
    private string? AdapterName(string? name)
    {
        if (name is null || AllowList.IsMicrosoftName(name)) return name;
        if (!_adapterNames.TryGetValue(name, out var replacement)) _adapterNames[name] = replacement = $"Example Network Adapter {++_adapters:00}";
        return replacement;
    }

    private JsonNode? Gateways(JsonNode? gateways)
    {
        if (gateways is not JsonArray list) return gateways;
        var result = new JsonArray();
        foreach (var g in list)
        {
            string address = (string)g!;
            if (!_gateways.TryGetValue(address, out var doc))
            {
                // RFC 5737 (IPv4) und RFC 3849 (IPv6): Adressen nur für Dokumentation
                string[] v4 = ["192.0.2.1", "198.51.100.1", "203.0.113.1"];
                doc = address.Contains(':') ? $"2001:db8::{_gateways.Count + 1}" : v4[_gateways.Count % v4.Length];
                _gateways[address] = doc;
            }
            result.Add(doc);
        }
        return result;
    }

    private void Shares(JsonNode value)
    {
        foreach (var row in Each(value))
        {
            string? name = (string?)row["Name"];
            if (name is null || name.EndsWith('$')) continue;
            int n = ++_shares;
            row["Name"] = $"share-{n:00}";
            row["Path"] = $@"C:\Example\Share-{n:00}";
        }
    }

    private void Exclusions(JsonNode value)
    {
        foreach (var row in Each(value))
        {
            foreach (var property in new[] { "ExclusionPath", "ExclusionProcess" })
                if (row[property] is JsonArray paths)
                    row[property] = new JsonArray(paths.Select(_ => (JsonNode?)JsonValue.Create($@"C:\Example\Excluded-{++_exclusions:00}")).ToArray());
            if (row["ExclusionIpAddress"] is JsonArray ips)
                row["ExclusionIpAddress"] = new JsonArray(ips.Select((_, i) => (JsonNode?)JsonValue.Create($"192.0.2.{10 + i}")).ToArray());
        }
    }

    private static string? Antivirus(string? name) =>
        name is null || AllowList.MicrosoftAntivirus.Contains(name, StringComparer.OrdinalIgnoreCase) ? name : "Example Antivirus";

    private void FirewallRules(JsonNode value)
    {
        if (value["rules"] is not JsonArray rules) return;
        foreach (var rule in rules.OfType<JsonObject>())
        {
            if (AllowList.IsSystemPath((string?)rule["application"]) && AllowList.IsWindowsRuleGroup((string?)rule["group"])) continue;
            int n = ++_rules;
            rule["name"] = $"Example rule {n:00}";
            rule["group"] = rule["group"] is null ? null : "Example group";
            rule["application"] = $@"C:\Program Files\Example\app-{n:00}.exe";
            if (rule["service"] is not null) rule["service"] = $"example-service-{n:00}";
        }
    }

    private void UpdateTitles(JsonNode value)
    {
        if (value["entries"] is not JsonArray entries) return;
        foreach (var entry in entries.OfType<JsonObject>())
        {
            string? title = (string?)entry["title"];
            if (title is not null && !AllowList.IsMicrosoftName(title) && !KbNumber().IsMatch(title)) entry["title"] = $"Example update {++_updates:00}";
        }
    }

    private void Registry(JsonObject item, string key, JsonObject value)
    {
        if (value["values"] is not JsonObject values) return;
        if (key.Equals(@"reg:HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", StringComparison.OrdinalIgnoreCase))
        {
            SetString(values, "RegisteredOwner", "Example Owner");
            SetString(values, "RegisteredOrganization", "Example Organization");
            SetString(values, "ProductId", "00000-00000-00000-AAAAA");
            ZeroBinary(values, "DigitalProductId");
            ZeroBinary(values, "DigitalProductId4");
        }
        if (key.EndsWith(@"\Terminal Server\RCM", StringComparison.OrdinalIgnoreCase)) { ZeroBinary(values, "Certificate"); ZeroBinary(values, "CertificateOld"); }
        if (key.EndsWith(@"\WinStations\RDP-Tcp", StringComparison.OrdinalIgnoreCase)) ZeroBinary(values, "SSLCertificateSHA1Hash");
        if (key.EndsWith(@"\Services\Tcpip\Parameters", StringComparison.OrdinalIgnoreCase))
            foreach (var name in new[] { "Domain", "NV Domain", "DhcpDomain", "SearchList", "ICSDomain" })
                if (values[name]?["v"] is JsonValue v && ((string?)v)?.Length > 0) values[name]!["v"] = "example.invalid";
        if (key.Contains(@"\NetBT\Parameters\Interfaces\", StringComparison.OrdinalIgnoreCase))
            foreach (var name in new[] { "NameServerList", "DhcpNameServerList" })
                if (values[name]?["v"] is JsonArray list && list.Count > 0) values[name]!["v"] = new JsonArray(list.Select((_, i) => (JsonNode?)JsonValue.Create($"192.0.2.{53 + i}")).ToArray());
        if (key.Contains(@"\Windows Defender\Exclusions\", StringComparison.OrdinalIgnoreCase))
            RenameValues(values, _ => $@"C:\Example\Excluded-{++_exclusions:00}");
        if (key.Contains(@"\FirewallPolicy\", StringComparison.OrdinalIgnoreCase) && key.EndsWith(@"\FirewallRules", StringComparison.OrdinalIgnoreCase))
            RegistryFirewallRules(values);
    }

    /// <summary>Regeln im Registry-Speicher („v2.31|Action=…|App=…|Name=…|“): fremde Programme ersetzen.</summary>
    private void RegistryFirewallRules(JsonObject values)
    {
        foreach (var (name, node) in values.ToList())
        {
            if (node?["v"] is not JsonValue v || (string?)v is not { } rule) continue;
            var app = RuleField().Match(rule);
            var context = RuleContext().Match(rule);
            bool systemApp = !app.Success || AllowList.IsSystemPath(app.Groups[1].Value);
            if (systemApp && AllowList.IsWindowsRuleGroup(context.Success ? context.Groups[1].Value : null)) continue;
            int n = ++_rules;
            string replaced = app.Success ? RuleField().Replace(rule, $@"App=C:\Program Files\Example\app-{n:00}.exe|") : rule;
            replaced = RuleText().Replace(replaced, m => $"{m.Groups[1].Value}=Example rule {n:00}|");
            node["v"] = replaced;
            if (!name.StartsWith("{", StringComparison.Ordinal) && !AllowList.IsMicrosoftName(name)) Rename(values, name, $"ExampleRule-{n:00}");
        }
    }

    private void UninstallEntries(JsonObject snapshot)
    {
        var items = Items(snapshot).ToList();
        foreach (var parent in new[] { @"reg:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"reg:HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            var parentItem = items.FirstOrDefault(i => (string?)i["key"] == parent);
            if (parentItem?["value"]?["subkeys"] is not JsonArray subkeys) continue;
            foreach (var child in items.Where(i => ((string)i["key"]!).StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                if (child["value"]?["values"] is not JsonObject values) continue;
                string? publisher = (string?)values["Publisher"]?["v"], display = (string?)values["DisplayName"]?["v"];
                bool microsoft = publisher is not null ? AllowList.MicrosoftPublishers.Contains(publisher, StringComparer.OrdinalIgnoreCase)
                    : display is null || display.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase);
                if (microsoft) continue;
                int n = ++_software;
                string oldName = ((string)child["key"]!)[(parent.Length + 1)..];
                string newName = $"ExampleSoftware{n:00}";
                child["key"] = parent + "\\" + newName;
                SetString(values, "DisplayName", $"Example Software {n:00}");
                SetString(values, "Publisher", "Example Vendor");
                for (int i = 0; i < subkeys.Count; i++)
                    if (string.Equals((string?)subkeys[i], oldName, StringComparison.OrdinalIgnoreCase)) subkeys[i] = newName;
            }
        }
    }

    // ------------------------------------------------------------------ Zeichenketten überall

    /// <summary>Wendet Host, Domänen, SIDs, GUIDs und Benutzer auf jede Zeichenkette und jeden Eigenschaftsnamen an.</summary>
    private JsonNode? Strings(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                var copy = new JsonObject();
                foreach (var (name, child) in o.ToList())
                {
                    o.Remove(name);
                    string newName = Replace(name);
                    while (copy.ContainsKey(newName)) newName += "~";
                    copy[newName] = Strings(child);
                }
                return copy;
            case JsonArray a:
                var array = new JsonArray();
                foreach (var child in a.ToList())
                {
                    a.Remove(child);
                    array.Add(Strings(child));
                }
                return array;
            case JsonValue v when v.TryGetValue<string>(out var s):
                return JsonValue.Create(Replace(s));
            default:
                return node;
        }
    }

    internal string Replace(string s)
    {
        if (s.Length == 0) return s;
        if (_host.Length > 0) s = Regex.Replace(s, Regex.Escape(_host), _hostReplacement, RegexOptions.IgnoreCase);
        foreach (var (from, to) in _domains) s = Regex.Replace(s, Regex.Escape(from), to, RegexOptions.IgnoreCase);
        foreach (var (from, to) in _sidPrefixes) s = Regex.Replace(s, Regex.Escape(from) + "(?=-|$|[^0-9])", to, RegexOptions.IgnoreCase);
        foreach (var (from, to) in _guids) s = Regex.Replace(s, Regex.Escape(from), to, RegexOptions.IgnoreCase);
        if (_machineGuid is not null) s = Regex.Replace(s, Regex.Escape(_machineGuid), ReplacementGuid, RegexOptions.IgnoreCase);
        foreach (var (from, to) in _users)
            s = Regex.Replace(s, @"(?<![A-Za-z0-9_.$-])" + Regex.Escape(from) + @"(?![A-Za-z0-9_$-])", to, RegexOptions.IgnoreCase);
        return s;
    }

    // ------------------------------------------------------------------ Hilfen

    private static IEnumerable<JsonObject> Items(JsonObject snapshot) => snapshot["items"]!.AsArray().OfType<JsonObject>();

    private static IEnumerable<JsonObject> Rows(IEnumerable<JsonObject> items, string key) =>
        items.FirstOrDefault(i => (string?)i["key"] == key)?["value"] is JsonArray a ? a.OfType<JsonObject>() : [];

    private static IEnumerable<JsonObject> WmiRows(IEnumerable<JsonObject> items, string key) =>
        items.FirstOrDefault(i => (string?)i["key"] == key)?["value"]?["rows"] is JsonArray a ? a.OfType<JsonObject>() : [];

    private static IEnumerable<JsonObject> Each(JsonNode value) => value["rows"] is JsonArray a ? a.OfType<JsonObject>() : [];

    private static object? RegValue(IEnumerable<JsonObject> items, string key, string name) =>
        items.FirstOrDefault(i => (string?)i["key"] == key)?["value"]?["values"]?[name]?["v"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static IEnumerable<string?> TcpipDomains(IEnumerable<JsonObject> items)
    {
        foreach (var name in new[] { "Domain", "NV Domain", "DhcpDomain", "SearchList" })
            yield return RegValue(items, @"reg:HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", name) as string;
    }

    private static void SetString(JsonObject values, string name, string replacement)
    {
        if (values[name] is JsonObject v && v["v"] is JsonValue s && ((string?)s)?.Length > 0) v["v"] = replacement;
    }

    private static void ZeroBinary(JsonObject values, string name)
    {
        if (values[name] is not JsonObject v || v["v"] is not JsonValue s || !s.TryGetValue<string>(out var b64)) return;
        v["v"] = Convert.ToBase64String(new byte[Convert.FromBase64String(b64).Length]);
    }

    private static void RenameValues(JsonObject values, Func<string, string> rename)
    {
        foreach (var name in values.Select(p => p.Key).ToList()) Rename(values, name, rename(name));
    }

    private static void Rename(JsonObject values, string from, string to)
    {
        var node = values[from];
        values.Remove(from);
        values[to] = node;
    }

    [GeneratedRegex(@"S-1-5-21-\d+-\d+-\d+")]
    private static partial Regex DomainSid();

    [GeneratedRegex(@"Tcpip_\{([0-9A-Fa-f-]{36})\}")]
    private static partial Regex AdapterGuidInKey();

    [GeneratedRegex(@"\bKB\d{6,8}\b")]
    private static partial Regex KbNumber();

    [GeneratedRegex(@"App=([^|]*)\|")]
    private static partial Regex RuleField();

    [GeneratedRegex(@"EmbedCtxt=([^|]*)\|")]
    private static partial Regex RuleContext();

    [GeneratedRegex(@"(Name|Desc|EmbedCtxt)=[^|]*\|")]
    private static partial Regex RuleText();
}
