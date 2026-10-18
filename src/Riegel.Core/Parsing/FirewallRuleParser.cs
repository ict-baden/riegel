// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Core.Parsing;

/// <summary>Eine Firewall-Regel aus einem Registry-Speicher.</summary>
/// <param name="Store">„gpo“ (SOFTWARE\Policies\Microsoft\WindowsFirewall\FirewallRules) oder „local“ (FirewallPolicy\FirewallRules).</param>
/// <param name="Id">Name des Registry-Werts (laut [MS-GPFAS] steht er für wszRuleId).</param>
/// <param name="Protocol">IP-Protokoll 0–255; null = jedes (kein Protocol=).</param>
/// <param name="LocalPorts">Einträge aus LPort=, LPort2_10= und LPort2_20= (Ports, Bereiche „a-b“, Schlüsselwörter).</param>
/// <param name="RemoteAddresses">Einträge aus RA4=, RA6=, RA42= und RA62=; leer = jede Adresse.</param>
/// <param name="Profiles">domain, private, public; ohne Profile= alle drei.</param>
/// <param name="Invalid">Wert folgt nicht der Grammatik (oder ist kein Text) – die Regel ist dann unbekannt, nie „nicht vorhanden“.</param>
internal sealed record FirewallRule(string Store, string Id, string? Name, string? Action, bool Active, string? Direction, int? Protocol,
    IReadOnlyList<string> LocalPorts, IReadOnlyList<string> RemoteAddresses, IReadOnlyList<string> Profiles, string? App, string? Service,
    bool Invalid);

/// <summary>
/// Firewall-Regeln im Textformat aus [MS-GPFAS] 2.2.2.19 „Firewall Rule and the Firewall Rule Grammar Rule“,
/// https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpfas/2efe0b76-7b4a-41ff-9050-1023f8196d16
/// (gesehen 2026-10-18, Stand der Seite 2024-10-30): <c>RULE = "v" VERSION "|" 1*FIELD</c>, <c>FIELD = TYPE-VALUE "|"</c>.
/// Werte laut den Abschnitten 2.2.2.1 (Profile: Domain/Private/Public), 2.2.2.2 (Ports, Bereiche „a-b“), 2.2.2.3
/// (Port-Schlüsselwörter), 2.2.2.4 (Dir: In/Out), 2.2.2.5 (Action: Allow/Block/ByPass), 2.2.2.13 (Adress-Schlüsselwörter)
/// und 2.2.2.14 (TRUE/FALSE). Laut Grammatik gilt: ohne <c>Profile=</c> alle Profile, ohne <c>Active=</c> false, ohne
/// <c>Protocol=</c> jedes Protokoll. ABNF-Zeichenketten sind ohne Groß/klein (RFC 4234). Unbekannte Felder (neuere
/// Versionen) werden übergangen.
/// </summary>
internal static class FirewallRuleParser
{
    /// <summary>Speicher der Gruppenrichtlinie (laut [MS-GPFAS] 2.2.2.19).</summary>
    internal const string GpoStoreKey = @"HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall\FirewallRules";

    /// <summary>Lokaler Speicher (spec 2.3, firewallExposure).</summary>
    internal const string LocalStoreKey = @"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";

    private static readonly string[] PortKeywords =
        ["RPC", "RPC-EPMap", "Teredo", "IPTLSIn", "IPHTTPSIn", "IPTLSOut", "IPHTTPSOut", "DHCP", "Ply2Disc", "mDNS", "CortanaOut", "TcpCDPSvc"];

    /// <summary>Eine Regel. Fehlerhafte Regeln kommen mit <see cref="FirewallRule.Invalid"/> zurück.</summary>
    internal static FirewallRule Parse(string store, string id, string text)
    {
        var invalid = new FirewallRule(store, id, null, null, false, null, null, [], [], [], null, null, true);
        if (text.Length < 2 || (text[0] != 'v' && text[0] != 'V')) return invalid;
        int bar = text.IndexOf('|');
        if (bar < 0 || !ValidVersion(text[1..bar])) return invalid;
        var fields = text[(bar + 1)..].Split('|');
        // FIELD endet immer mit „|“: das letzte Stück ist leer
        if (fields.Length < 2 || fields[^1].Length != 0) return invalid;
        string? name = null, action = null, dir = null, app = null, svc = null;
        bool active = false;
        int? protocol = null;
        var ports = new List<string>();
        var remote = new List<string>();
        var profiles = new SortedSet<string>(StringComparer.Ordinal);
        bool anyProfile = false;
        foreach (var field in fields[..^1])
        {
            int eq = field.IndexOf('=');
            if (eq <= 0) return invalid;
            var type = field[..eq];
            var value = field[(eq + 1)..];
            switch (type.ToUpperInvariant())
            {
                case "ACTION":
                    if (action is not null) return invalid;
                    action = value.ToUpperInvariant() switch { "ALLOW" => "allow", "BLOCK" => "block", "BYPASS" => "bypass", _ => null };
                    if (action is null) return invalid;
                    break;
                case "DIR":
                    if (dir is not null) return invalid;
                    dir = value.ToUpperInvariant() switch { "IN" => "in", "OUT" => "out", _ => null };
                    if (dir is null) return invalid;
                    break;
                case "PROFILE":
                    var profile = value.ToUpperInvariant() switch { "DOMAIN" => "domain", "PRIVATE" => "private", "PUBLIC" => "public", _ => null };
                    if (profile is null) return invalid;
                    profiles.Add(profile);
                    anyProfile = true;
                    break;
                case "PROTOCOL":
                    if (protocol is not null || value.Length is 0 or > 3
                        || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var p) || p > 255) return invalid;
                    protocol = p;
                    break;
                case "LPORT":
                case "LPORT2_10":
                case "LPORT2_20":
                    if (!ValidPort(value)) return invalid;
                    ports.Add(value);
                    break;
                case "RA4":
                case "RA6":
                case "RA42":
                case "RA62":
                    if (value.Length == 0) return invalid;
                    remote.Add(value);
                    break;
                case "ACTIVE":
                    var b = value.ToUpperInvariant() switch { "TRUE" => (bool?)true, "FALSE" => false, _ => null };
                    if (b is null) return invalid;
                    active = b.Value;
                    break;
                case "NAME":
                    name = value;
                    break;
                case "APP":
                    app = value;
                    break;
                case "SVC":
                    svc = value;
                    break;
            }
        }
        IReadOnlyList<string> profileList = anyProfile ? [.. profiles] : ["domain", "private", "public"];
        return new FirewallRule(store, id, name, action, active, dir, protocol, ports, remote, profileList, app, svc, false);
    }

    private static bool ValidVersion(string text)
    {
        int dot = text.IndexOf('.');
        if (dot <= 0) return false;
        return Digits(text[..dot], 3) && Digits(text[(dot + 1)..], 3)
               && int.Parse(text[..dot], CultureInfo.InvariantCulture) <= 255 && int.Parse(text[(dot + 1)..], CultureInfo.InvariantCulture) <= 255;
    }

    private static bool Digits(string text, int max) => text.Length >= 1 && text.Length <= max && text.All(char.IsAsciiDigit);

    /// <summary>PORT (1–5 Ziffern, ≤ 65535), Bereich „a-b“ oder eines der Schlüsselwörter aus 2.2.2.3.</summary>
    private static bool ValidPort(string value)
    {
        if (PortKeywords.Contains(value, StringComparer.OrdinalIgnoreCase)) return true;
        int dash = value.IndexOf('-');
        if (dash < 0) return Port(value);
        return Port(value[..dash]) && Port(value[(dash + 1)..]);

        static bool Port(string p) => Digits(p, 5) && int.Parse(p, CultureInfo.InvariantCulture) <= 65535;
    }

    /// <summary>
    /// Wert von <c>parsed:firewall-rules-registry</c> (spec 7.3): <c>[ { store, id, name, action, active, dir, protocol,
    /// lports, ra, profiles, app, svc } ]</c>; fehlerhafte Regeln mit <c>invalid: true</c>. <c>id</c> und <c>invalid</c>
    /// ergänzen die Form aus spec 7.3.
    /// </summary>
    internal static JsonElement ToParsedItem(IEnumerable<FirewallRule> rules) => JsonValues.Build(w =>
    {
        w.WriteStartArray();
        foreach (var r in rules)
        {
            w.WriteStartObject();
            w.WriteString("store", r.Store);
            w.WriteString("id", r.Id);
            if (r.Invalid)
            {
                w.WriteBoolean("invalid", true);
                w.WriteEndObject();
                continue;
            }
            WriteNullable(w, "name", r.Name);
            WriteNullable(w, "action", r.Action);
            w.WriteBoolean("active", r.Active);
            WriteNullable(w, "dir", r.Direction);
            if (r.Protocol is { } protocol) w.WriteNumber("protocol", protocol);
            else w.WriteNull("protocol");
            WriteList(w, "lports", r.LocalPorts);
            WriteList(w, "ra", r.RemoteAddresses);
            WriteList(w, "profiles", r.Profiles);
            WriteNullable(w, "app", r.App);
            WriteNullable(w, "svc", r.Service);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    });

    private static void WriteNullable(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null) w.WriteNull(name);
        else w.WriteString(name, value);
    }

    private static void WriteList(Utf8JsonWriter w, string name, IReadOnlyList<string> values)
    {
        w.WritePropertyName(name);
        w.WriteStartArray();
        foreach (var v in values) w.WriteStringValue(v);
        w.WriteEndArray();
    }
}
