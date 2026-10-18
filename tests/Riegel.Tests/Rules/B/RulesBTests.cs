// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.RegularExpressions;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Tests.Infrastructure;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Rules.B;

/// <summary>
/// Prüfungen des Regelpakets rules-b (DEF, ASR, APP, ENC, FW, SVC, SYS, Fähigkeiten „protection“, Datenlisten).
/// Die allgemeinen Katalogregeln stehen in <see cref="CatalogueInvariantTests"/>; hier nur, was dieses Paket
/// zusätzlich zusagt: ASR-GUIDs, Belege der Listen, Abdeckung durch den Erhebungsplan, Defender-Sperrfälle.
/// </summary>
public sealed partial class RulesBTests
{
    private static readonly string[] Prefixes = ["DEF", "ASR", "APP", "ENC", "FW", "SVC", "SYS"];

    /// <summary>
    /// ASR-GUID je Regel. Quelle: Microsoft, „Attack surface reduction rules reference“
    /// (https://learn.microsoft.com/en-us/defender-endpoint/attack-surface-reduction-rules-reference, ms.date
    /// 2026-09-09, gesehen 2026-10-18); dieselben GUIDs nennt die ACSC-Tabelle.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AsrGuids = new Dictionary<string, string>
    {
        ["ASR-01"] = "56a863a9-875e-4185-98a7-b882c64b5ce5", // Block abuse of exploited vulnerable signed drivers
        ["ASR-02"] = "9e6c4e1f-7d60-472f-ba1a-a39ef669e4b2", // Block credential stealing from LSASS
        ["ASR-05"] = "d4f940ab-401b-4efc-aadc-ad5f3c50688a", // Block all Office applications from creating child processes
        ["ASR-08"] = "5beb7efe-fd9a-4556-801d-275e5ffc04cc", // Block execution of potentially obfuscated scripts
        ["ASR-09"] = "d3e037e1-3eb8-44c8-a917-57927947596d", // Block JavaScript or VBScript from launching downloaded executable content
        ["ASR-13"] = "d1e49aac-8f56-4280-b9ba-993a6d77406c", // Block process creations originating from PSExec and WMI commands
        ["ASR-15"] = "b2b3f03d-6a65-4f7b-a9c7-1c7ef74a9ba4", // Block untrusted and unsigned processes that run from USB
    };

    /// <summary>Alle 19 GUIDs der Microsoft-Referenz (gleiche Quelle), auch die erst 2027.01 geplanten.</summary>
    private static readonly HashSet<string> ReferenceGuids = new(StringComparer.Ordinal)
    {
        "56a863a9-875e-4185-98a7-b882c64b5ce5", "9e6c4e1f-7d60-472f-ba1a-a39ef669e4b2", "e6db77e5-3df2-4cf1-b95a-636979351e5b",
        "7674ba52-37eb-4a4f-a9a1-f0f9a1619a2c", "d4f940ab-401b-4efc-aadc-ad5f3c50688a", "be9ba2d9-53ea-4cdc-84e5-9b1eeee46550",
        "01443614-cd74-433a-b99e-2ecdc07bfc25", "5beb7efe-fd9a-4556-801d-275e5ffc04cc", "d3e037e1-3eb8-44c8-a917-57927947596d",
        "3b576869-a4ec-4529-8536-b80a7769e899", "75668c1f-73b5-4cf0-bb93-3ecf5cb7cc84", "26190899-1602-49e8-8b27-eb1d0a1ce869",
        "d1e49aac-8f56-4280-b9ba-993a6d77406c", "33ddedf1-c6e0-47cb-833e-de6133960387", "b2b3f03d-6a65-4f7b-a9c7-1c7ef74a9ba4",
        "c0033c00-d16d-4114-a5a0-dc9b3a7d2ceb", "a8f5898e-1dc8-49a9-9878-85004b8a61e6", "92e97fa1-2edf-4476-bdd6-9dd0b4dddc7b",
        "c1db55ab-c21a-4637-bb3f-a12568109d35",
    };

    /// <summary>
    /// Schlüssel, die Regeln dieses Pakets brauchen, die der Erhebungsplan 1 aber noch nicht liest
    /// (Integrationsanfrage im Abschlussbericht). Bis dahin ergeben die betroffenen Regeln „nicht prüfbar“.
    /// </summary>
    private static readonly HashSet<string> PendingPlanKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        @"HKLM\SYSTEM\CurrentControlSet\Policies\Microsoft\FVE", // ENC-07 RDVDenyWriteAccess (BitLocker CSP: ADMX-Zuordnung)
        @"HKLM\SYSTEM\CurrentControlSet\Services\Spooler", // SVC-01: Rückfall auf den Start-Wert, nur im Vektor als keyMissing
    };

    private static IReadOnlyList<Rule> Rules() =>
        [.. Prefixes.SelectMany(p => RiegelJson.Deserialize<RuleFile>(File.ReadAllBytes(Path.Combine(RepoPaths.Rules, p + ".json"))).Rules)];

    private static CollectionPlan Plan() => RiegelJson.ReadFile<CollectionPlan>(RepoPaths.CollectionPlan, 10 * 1024 * 1024);

    [Fact]
    public void Alle_42_Regeln_des_Pakets_sind_vorhanden()
    {
        var ids = Rules().Select(r => r.Id).Order(StringComparer.Ordinal).ToList();
        var expected = Prefixes.SelectMany(p => CatalogueInvariantTests.Catalogue202610[p]).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expected, ids);
        Assert.Equal(42, ids.Count);
    }

    [Fact]
    public void Asr_GUIDs_sind_wohlgeformt_eindeutig_und_belegt()
    {
        var problems = new List<string>();
        var perRule = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rule in Rules())
        {
            var guids = CatalogueInvariantTests.Flatten(rule.Check).OfType<AsrRuleCheck>().Select(a => a.Guid).ToList();
            foreach (var guid in guids)
            {
                if (!AsrGuidPattern().IsMatch(guid)) problems.Add($"{rule.Id}: GUID {guid} nicht in Kleinbuchstaben-Form 8-4-4-4-12");
                if (!ReferenceGuids.Contains(guid)) problems.Add($"{rule.Id}: GUID {guid} steht nicht in der Microsoft-Referenz");
            }
            if (guids.Count != guids.Distinct(StringComparer.OrdinalIgnoreCase).Count()) problems.Add($"{rule.Id}: GUID doppelt");
            if (rule.Id.StartsWith("ASR-", StringComparison.Ordinal))
            {
                if (guids.Count != 1) { problems.Add($"{rule.Id}: genau eine asrRule erwartet, {guids.Count} gefunden"); continue; }
                if (!AsrGuids.TryGetValue(rule.Id, out var expected) || expected != guids[0])
                    problems.Add($"{rule.Id}: GUID {guids[0]} statt {AsrGuids.GetValueOrDefault(rule.Id)}");
                perRule[rule.Id] = guids[0];
            }
        }
        if (perRule.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != perRule.Count) problems.Add("zwei ASR-Regeln mit derselben GUID");

        // DEF-09 zählt die von ACSC empfohlenen Regeln, darunter alle einzeln geprüften.
        var def09 = CatalogueInvariantTests.Flatten(Rules().Single(r => r.Id == "DEF-09").Check).OfType<AsrRuleCheck>().Select(a => a.Guid).ToHashSet();
        foreach (var (id, guid) in AsrGuids)
            if (!def09.Contains(guid)) problems.Add($"DEF-09 enthält die GUID von {id} nicht");
        Assert.Equal(16, def09.Count);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Datenlisten_sind_gueltig_und_belegt()
    {
        var folder = Path.Combine(RepoPaths.Rules, "data");
        var files = Directory.EnumerateFiles(folder, "*.json").Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "office-detection.json", "remote-access-tools.json" }, files);
        var problems = new List<string>();
        foreach (var name in files)
        {
            var text = File.ReadAllText(Path.Combine(folder, name));
            SchemaValidator.AssertValid(text, "rules/data/" + name);
        }

        var tools = RiegelJson.Deserialize<DataFile>(File.ReadAllBytes(Path.Combine(folder, "remote-access-tools.json")));
        Assert.Equal(DataKind.RemoteAccessTools, tools.Kind);
        Assert.Null(tools.OfficeDetection);
        var products = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in tools.RemoteAccessTools ?? [])
        {
            if (!products.Add(tool.Product)) problems.Add($"{tool.Product}: doppelt");
            if (!tool.Source.StartsWith("https://", StringComparison.Ordinal)) problems.Add($"{tool.Product}: Quelle ohne https");
            if (tool.Seen != new DateOnly(2026, 10, 18)) problems.Add($"{tool.Product}: Datum {tool.Seen}");
            if (tool.ServiceNames.Count + tool.DisplayNameContains.Count == 0) problems.Add($"{tool.Product}: kein Muster");
            foreach (var pattern in tool.ServiceNames.Concat(tool.DisplayNameContains))
            {
                // Muster sind Teilzeichenketten bzw. genaue Namen, nie reguläre Ausdrücke (spec 7.12)
                if (pattern.IndexOfAny(['*', '?', '|', '^', '$', '[', ']', '(', ')', '\\']) >= 0) problems.Add($"{tool.Product}: Sonderzeichen in „{pattern}“");
                if (pattern.Trim().Length < 4) problems.Add($"{tool.Product}: Muster „{pattern}“ zu kurz (zu viele Treffer)");
            }
        }
        Assert.True(products.Count >= 10, "Liste der Fernwartungswerkzeuge zu kurz");

        var office = RiegelJson.Deserialize<DataFile>(File.ReadAllBytes(Path.Combine(folder, "office-detection.json")));
        Assert.Equal(DataKind.OfficeDetection, office.Kind);
        Assert.NotNull(office.OfficeDetection);
        var plan = Plan();
        var planKeys = plan.Registry.Keys.Select(k => k.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var detection = office.OfficeDetection!;
        foreach (var key in detection.ClickToRunKeys.Concat(detection.InstallRootKeys))
        {
            if (!key.Key.StartsWith(@"HKLM\", StringComparison.Ordinal)) problems.Add($"{key.Key}: nicht unter HKLM");
            if (!planKeys.Contains(key.Key)) problems.Add($"{key.Key}: wird vom Erhebungsplan nicht gelesen");
            if (key.Source is { } s && (!s.StartsWith("https://", StringComparison.Ordinal) || key.Seen is null)) problems.Add($"{key.Key}: Quelle ohne https oder Datum");
            if (key.Source is null && string.IsNullOrWhiteSpace(key.Note)) problems.Add($"{key.Key}: ohne Quelle muss die Notiz sagen, warum");
        }
        Assert.All(detection.ClickToRunKeys, k => Assert.NotNull(k.Source));
        Assert.NotEmpty(detection.UninstallDisplayNamePrefixes);
        foreach (var prefix in detection.UninstallDisplayNamePrefixes)
            if (prefix.Source is null || prefix.Seen is null) problems.Add($"Präfix „{prefix.Pattern}“ ohne Quelle");
        // Der Plan liest Uninstall nur mit DisplayName/DisplayVersion/Publisher – DisplayName muss dabei sein
        Assert.Contains(plan.Registry.Keys, k => k.Path.EndsWith(@"\Uninstall", StringComparison.OrdinalIgnoreCase) && (k.Values ?? []).Contains("DisplayName"));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Faehigkeiten_der_Schutzregeln_sind_belegt_und_bekannt()
    {
        var path = Path.Combine(RepoPaths.Rules, "capabilities", "protection.json");
        var text = File.ReadAllText(path);
        SchemaValidator.AssertValid(text, "rules/capabilities/protection.json");
        var file = RiegelJson.Deserialize<CapabilityFile>(File.ReadAllBytes(path));
        var problems = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in file.Capabilities)
        {
            if (!names.Add(entry.Capability)) problems.Add($"{entry.Capability}: doppelt");
            if (!entry.Source.StartsWith("https://learn.microsoft.com/", StringComparison.Ordinal)) problems.Add($"{entry.Capability}: keine Microsoft-Learn-Quelle");
            if (entry.Seen != new DateOnly(2026, 10, 18)) problems.Add($"{entry.Capability}: Datum {entry.Seen}");
            foreach (var c in entry.Exists)
                if (c.MinBuild is null && c.MaxBuild is null) problems.Add($"{entry.Capability}: Bedingung ohne Build-Grenze");
        }
        foreach (var rule in Rules().Where(r => r.AppliesTo.Capability is not null))
            if (!names.Contains(rule.AppliesTo.Capability!)) problems.Add($"{rule.Id}: Fähigkeit {rule.AppliesTo.Capability} fehlt in protection.json");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Erlaubnisliste_whenMissing_pass_passt_zu_den_Regeln()
    {
        var file = Path.Combine(RepoPaths.TestsProject, "Rules", "B", "when-missing-pass.txt");
        var listed = File.ReadAllLines(file).Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal);
        var used = Rules().Where(r => CatalogueInvariantTests.Flatten(r.Check).Any(c => c switch
        {
            RegistryCheck rc => rc.WhenMissing == WhenMissing.Pass,
            RegistryEffectiveCheck re => re.WhenMissing == WhenMissing.Pass,
            _ => false,
        })).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(used.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Defender_Sperrfaelle_sind_als_Vektoren_abgedeckt()
    {
        // Abnahme rules-b: aktiv, passiv mit Drittanbieter, aus ohne Virenschutz, fehlend (spec 2.5)
        var problems = new List<string>();
        foreach (var id in new[] { "DEF-01", "DEF-09", "ASR-02", "DEF-18" })
        {
            var tests = Rules().Single(r => r.Id == id).Tests;
            bool Has(string mode, bool? thirdParty) => tests.Any(t =>
                t.Facts is { } f && f.TryGetValue("defender.mode", out var m) && m.ValueKind == JsonValueKind.String && m.GetString() == mode
                && (thirdParty is null || (f.TryGetValue("av.thirdPartyActive", out var tp) && tp.ValueKind == (thirdParty.Value ? JsonValueKind.True : JsonValueKind.False))));
            if (!Has("active", null)) problems.Add($"{id}: kein Vektor „aktiv“");
            if (!Has("passive", true)) problems.Add($"{id}: kein Vektor „passiv mit Drittanbieter“");
            if (!Has("off", false)) problems.Add($"{id}: kein Vektor „aus ohne Virenschutz“");
            if (!Has("absent", null)) problems.Add($"{id}: kein Vektor „fehlend“");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Belege_und_Verweise_sind_vollstaendig()
    {
        var problems = new List<string>();
        foreach (var rule in Rules())
        {
            foreach (var v in rule.Verified)
            {
                if (v.Date != new DateOnly(2026, 10, 18)) problems.Add($"{rule.Id}: verified {v.Src} mit Datum {v.Date}");
                if (RuleSources.ValueBacking.Contains(v.Src) && v.Url is null) problems.Add($"{rule.Id}: verified {v.Src} ohne URL");
                if (v.Url is { } u && !u.StartsWith("https://", StringComparison.Ordinal)) problems.Add($"{rule.Id}: URL ohne https");
            }
            foreach (var reference in rule.References)
            {
                if (reference.Src == "ATTACK" && (reference.Ids ?? []).Any(i => !Mitigation().IsMatch(i))) problems.Add($"{rule.Id}: ATT&CK-Kennung nicht im Format M0000");
                if (reference.Src == "DISA" && (reference.Ids is not { Count: 2 } ids || !ids[0].StartsWith("V-", StringComparison.Ordinal))) problems.Add($"{rule.Id}: DISA-Verweis ohne V-ID und Regelversion");
                if (reference.Src == "ACSC" && string.IsNullOrWhiteSpace(reference.Section)) problems.Add($"{rule.Id}: ACSC-Verweis ohne Abschnitt");
            }
            if (rule.Kind == RuleKind.Check && rule.Remedy is null) problems.Add($"{rule.Id}: Prüfregel ohne remedy");
            if (rule.TextSources.Count != 0) problems.Add($"{rule.Id}: textSources – die Texte dieses Pakets sind eigene");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Gelesene_Eintraege_liegen_im_Erhebungsplan()
    {
        var plan = Plan();
        var wmi = plan.Wmi.ToDictionary(w => w.Key, StringComparer.OrdinalIgnoreCase);
        var api = plan.Api.Select(a => a.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();

        void Registry(string ruleId, string key)
        {
            if (PendingPlanKeys.Contains(key)) return;
            if (!Covered(plan, key)) problems.Add($"{ruleId}: {key} liegt außerhalb des Erhebungsplans");
        }

        void Item(string ruleId, string item, string? property = null)
        {
            if (item.StartsWith("reg:", StringComparison.OrdinalIgnoreCase)) { Registry(ruleId, item[4..]); return; }
            if (item.StartsWith("wmi:", StringComparison.OrdinalIgnoreCase))
            {
                if (!wmi.TryGetValue(item, out var entry)) { problems.Add($"{ruleId}: {item} nicht im Plan"); return; }
                if (property is not null && !entry.Properties.Contains(property, StringComparer.OrdinalIgnoreCase))
                    problems.Add($"{ruleId}: {item} ohne Eigenschaft {property} im Plan");
                return;
            }
            if (!api.Contains(item)) problems.Add($"{ruleId}: {item} nicht im Plan");
        }

        foreach (var rule in Rules())
        {
            foreach (var check in CatalogueInvariantTests.Flatten(rule.Check))
                switch (check)
                {
                    case RegistryCheck r: Registry(rule.Id, r.Key); break;
                    case WmiPropertyCheck w:
                        Item(rule.Id, w.Item, w.Property);
                        if (w.RowFilter is { } f) Item(rule.Id, w.Item, f.Property);
                        break;
                    case DefenderSettingCheck d:
                        Item(rule.Id, d.Source == DefenderSource.Status ? @"wmi:root\Microsoft\Windows\Defender:MSFT_MpComputerStatus"
                            : @"wmi:root\Microsoft\Windows\Defender:MSFT_MpPreference", d.Property);
                        break;
                    case InventoryCheck i:
                        foreach (var item in i.Items) Item(rule.Id, item);
                        break;
                    case ServiceCheck: Item(rule.Id, @"wmi:root\cimv2:Win32_Service", "StartMode"); break;
                    case FirewallProfileCheck or FirewallExposureCheck: Item(rule.Id, ItemKeys.Firewall); break;
                    case AvStatusCheck: Item(rule.Id, ItemKeys.WscAntivirus); break;
                }
            // Auch die Vektoren verwenden nur Schlüssel, die ein echter Snapshot haben kann
            foreach (var vector in rule.Tests)
                foreach (var key in vector.Items.Keys)
                    Item(rule.Id, key);
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Liegt der Schlüssel in einem rekursiven Bereich (innerhalb der Tiefe) oder ist er ein Einzelschlüssel des Plans?</summary>
    private static bool Covered(CollectionPlan plan, string key)
    {
        foreach (var root in plan.Registry.Roots.Concat(plan.Registry.Keys))
        {
            if (string.Equals(root.Path, key, StringComparison.OrdinalIgnoreCase)) return true;
            if (root.Kind == RegistryPlanKind.Key) continue;
            if (!key.StartsWith(root.Path + @"\", StringComparison.OrdinalIgnoreCase)) continue;
            var depth = key[(root.Path.Length + 1)..].Split('\\').Length;
            if (depth <= root.MaxDepth) return true;
        }
        return false;
    }

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")]
    private static partial Regex AsrGuidPattern();

    [GeneratedRegex(@"^M\d{4}$")]
    private static partial Regex Mitigation();
}
