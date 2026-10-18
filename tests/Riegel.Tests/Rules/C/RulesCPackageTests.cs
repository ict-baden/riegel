// SPDX-License-Identifier: MIT
using System.Text.RegularExpressions;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Rules.C;

/// <summary>Gemeinsame Hilfen der Tests des Regelpakets rules-c.</summary>
internal static class RulesCPackage
{
    public static readonly string[] Prefixes = ["AUD", "PS", "RDP", "MED", "TLS"];

    /// <summary>Bestandsregeln dieses Pakets (spec 2.1: RDP-09; nach der Prüfung der Quellen auch RDP-08 und die Cipher-Regeln).</summary>
    public static readonly string[] InventoryRules = ["RDP-08", "RDP-09", "TLS-06", "TLS-07", "TLS-08", "TLS-11"];

    /// <summary>Fähigkeiten aus rules/capabilities/logging.json.</summary>
    public static readonly string[] Capabilities = ["ps2Engine", "schannelSsl2", "schannelTls11", "schannelTls12", "auditPnpActivity"];

    /// <summary>
    /// Auf attack.mitre.org (ATT&amp;CK v20, gesehen 2026-10-18) geprüfte Mitigations, die dieses Paket nennt.
    /// </summary>
    public static readonly string[] AttackIds =
        ["M1028", "M1034", "M1035", "M1038", "M1041", "M1042", "M1045", "M1047", "M0808", "M0928", "M0934", "M0935", "M0938", "M0942", "M0945", "M0947"];

    /// <summary>
    /// Registry-Schlüssel, die die Regeln lesen, aber der Erfassungsplan v1 noch nicht erfasst (Integrationsanfrage).
    /// Bis dahin ergeben die lokalen Quellen „nicht prüfbar“; die Testvektoren geben ihre Abdeckung selbst an.
    /// </summary>
    public static readonly string[] PlanGaps =
    [
        @"HKLM\SYSTEM\CurrentControlSet\Services\W32Time\Parameters",
        @"HKLM\SYSTEM\CurrentControlSet\Services\W32Time\TimeProviders\NtpClient",
    ];

    public static IReadOnlyList<Rule> Rules() =>
        [.. Prefixes.SelectMany(p => RiegelJson.Deserialize<RuleFile>(File.ReadAllBytes(Path.Combine(RepoPaths.Rules, p + ".json"))).Rules)];

    public static IEnumerable<CheckSpec> Flatten(CheckSpec check)
    {
        yield return check;
        IEnumerable<CheckSpec> children = check switch
        {
            FirstOfCheck f => f.Checks,
            AllOfCheck a => a.Checks,
            AnyOfCheck a => a.Checks,
            NoneOfCheck n => n.Checks,
            NotCheck n => [n.Check],
            _ => [],
        };
        foreach (var child in children)
            foreach (var nested in Flatten(child)) yield return nested;
    }

    /// <summary>Alle Registry-Schlüssel, die eine Prüfung liest.</summary>
    public static IEnumerable<string> RegistryKeys(CheckSpec check) =>
        Flatten(check).SelectMany(c => c switch
        {
            RegistryCheck r => [r.Key],
            RegistryEffectiveCheck e => e.Sources.Select(s => s.Key),
            _ => Enumerable.Empty<string>(),
        });
}

/// <summary>
/// Paketeigene Prüfungen für rules-c (AUD, PS, RDP, MED, TLS) über die gemeinsamen Katalogtests hinaus.
/// </summary>
public sealed class RulesCPackageTests
{
    [Fact]
    public void Alle_55_Regeln_des_Pakets_sind_da_und_die_Bestandsregeln_stimmen()
    {
        var rules = RulesCPackage.Rules();
        Assert.Equal(55, rules.Count);
        Assert.All(rules, r => Assert.Equal(RuleState.Active, r.State));
        Assert.Equal(RulesCPackage.InventoryRules.Order(StringComparer.Ordinal),
            rules.Where(r => r.Kind == RuleKind.Inventory).Select(r => r.Id).Order(StringComparer.Ordinal));
        foreach (var rule in rules.Where(r => r.Kind == RuleKind.Inventory))
        {
            Assert.IsType<InventoryCheck>(rule.Check);
            Assert.All(rule.Tests, t => Assert.Equal(ResultStatus.Info, t.Expect));
        }
        foreach (var rule in rules.Where(r => r.Kind == RuleKind.Check))
            Assert.DoesNotContain(RulesCPackage.Flatten(rule.Check), c => c is InventoryCheck);
    }

    [Fact]
    public void Kategorien_passen_zum_Praefix()
    {
        var expected = new Dictionary<string, string> { ["AUD"] = "audit", ["PS"] = "powershell", ["RDP"] = "rdp", ["MED"] = "media", ["TLS"] = "tls" };
        foreach (var rule in RulesCPackage.Rules())
            Assert.Equal(expected[rule.Id.Split('-')[0]], rule.Category);
    }

    [Fact]
    public void Faehigkeiten_sind_belegt_und_werden_genutzt()
    {
        var file = RiegelJson.Deserialize<CapabilityFile>(File.ReadAllBytes(Path.Combine(RepoPaths.Rules, "capabilities", "logging.json")));
        Assert.Equal(RulesCPackage.Capabilities.Order(StringComparer.Ordinal), file.Capabilities.Select(c => c.Capability).Order(StringComparer.Ordinal));
        foreach (var entry in file.Capabilities)
        {
            Assert.StartsWith("https://", entry.Source, StringComparison.Ordinal);
            Assert.Equal(new DateOnly(2026, 10, 18), entry.Seen);
            Assert.NotEmpty(entry.Exists);
            Assert.All(entry.Exists, e => Assert.Contains(e.Os, new[] { "client", "server" }));
        }
        var used = RulesCPackage.Rules().Select(r => r.AppliesTo.Capability).OfType<string>().ToHashSet(StringComparer.Ordinal);
        Assert.Equal(RulesCPackage.Capabilities.Order(StringComparer.Ordinal), used.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Texte_sind_in_Englisch_und_Deutsch_vollstaendig()
    {
        var problems = new List<string>();
        foreach (var rule in RulesCPackage.Rules())
        {
            foreach (var (field, text) in new[] { ("otNote", rule.OtNote), ("remedy", rule.Remedy), ("remediation.manual", rule.Remediation?.Manual) })
                if (text is not null)
                    foreach (var code in new[] { "en", "de" })
                        if (!text.Has(code)) problems.Add($"{rule.Id}: {field}.{code} leer");
            if (rule.Kind == RuleKind.Check && rule.Remedy is null) problems.Add($"{rule.Id}: remedy fehlt");
            if (rule.TextSources.Count != 0) problems.Add($"{rule.Id}: textSources – das Paket schreibt eigene Texte");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Verweise_nennen_ATTACK_und_Grundschutz_Kennungen()
    {
        var problems = new List<string>();
        foreach (var rule in RulesCPackage.Rules())
        {
            var attack = rule.References.Where(r => r.Src == "ATTACK").SelectMany(r => r.Ids ?? []).ToList();
            if (attack.Count == 0) problems.Add($"{rule.Id}: kein ATT&CK-Verweis");
            foreach (var id in attack.Where(i => !RulesCPackage.AttackIds.Contains(i))) problems.Add($"{rule.Id}: ATT&CK {id} nicht geprüft");
            if (!rule.References.Any(r => r.Src == "GSPP")) problems.Add($"{rule.Id}: keine Grundschutz++-Kennung");
            if (rule.References.Any(r => r.Src == "DISA")) problems.Add($"{rule.Id}: STIG-Verweis ohne aktuelle XCCDF (spec 2.2)");
            foreach (var msb in rule.References.Where(r => r.Src == "MSB"))
                if (string.IsNullOrWhiteSpace(msb.Doc) || string.IsNullOrWhiteSpace(msb.Setting)) problems.Add($"{rule.Id}: MSB-Verweis ohne Dokument oder Einstellung");
            foreach (var acsc in rule.References.Where(r => r.Src == "ACSC"))
                if (string.IsNullOrWhiteSpace(acsc.Section)) problems.Add($"{rule.Id}: ACSC-Verweis ohne Abschnitt");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Belege_haben_URL_und_Datum()
    {
        var problems = new List<string>();
        foreach (var rule in RulesCPackage.Rules())
        {
            if (rule.Verified.Count == 0) problems.Add($"{rule.Id}: kein verified-Eintrag");
            foreach (var v in rule.Verified)
            {
                if (v.Url is null || !v.Url.StartsWith("https://", StringComparison.Ordinal)) problems.Add($"{rule.Id}: verified ohne https-URL ({v.Src})");
                if (v.Date != new DateOnly(2026, 10, 18)) problems.Add($"{rule.Id}: verified-Datum {v.Date}");
                if (!RuleSources.ValueBacking.Contains(v.Src)) problems.Add($"{rule.Id}: verified-Quelle {v.Src}");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Standardwerte_sind_nur_belegt_und_nie_fuer_ungelistete_Builds()
    {
        var problems = new List<string>();
        foreach (var rule in RulesCPackage.Rules())
            foreach (var check in RulesCPackage.Flatten(rule.Check))
            {
                var (osDefaults, whenMissing) = check switch
                {
                    RegistryCheck r => (r.OsDefaults, r.WhenMissing),
                    RegistryEffectiveCheck e => (e.OsDefaults, e.WhenMissing),
                    _ => (null, WhenMissing.NotCheckable),
                };
                if (whenMissing == WhenMissing.Pass) problems.Add($"{rule.Id}: whenMissing pass (nicht vorgesehen in rules-c)");
                foreach (var d in osDefaults ?? [])
                {
                    if (d.Seen != new DateOnly(2026, 10, 18) || !d.Source.StartsWith("https://", StringComparison.Ordinal)) problems.Add($"{rule.Id}: osDefault ohne Quelle/Datum");
                    // TLS: Microsoft listet die Standardzustände bis Windows 11 24H2 / Server 2025 (Build 26100)
                    if (rule.Category == "tls" && (d.MaxBuild is null || d.MaxBuild > 26100 || d.MinBuild is null))
                        problems.Add($"{rule.Id}: TLS-Standardwert ohne geschlossenen Buildbereich bis 26100");
                }
            }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Gelesene_Registry_Schluessel_liegen_im_Erfassungsplan()
    {
        var plan = RiegelJson.ReadFile<CollectionPlan>(RepoPaths.CollectionPlan, 10 * 1024 * 1024);
        bool Covered(string key) =>
            plan.Registry.Roots.Any(r => key.Equals(r.Path, StringComparison.OrdinalIgnoreCase) || key.StartsWith(r.Path + "\\", StringComparison.OrdinalIgnoreCase)) ||
            plan.Registry.Keys.Any(k => key.Equals(k.Path, StringComparison.OrdinalIgnoreCase));
        var problems = new List<string>();
        foreach (var rule in RulesCPackage.Rules())
        {
            var keys = RulesCPackage.RegistryKeys(rule.Check).ToList();
            if (rule.Check is InventoryCheck inventory)
                keys.AddRange(inventory.Items.Where(i => i.StartsWith("reg:", StringComparison.Ordinal)).Select(i => i[4..]));
            foreach (var key in keys.Distinct(StringComparer.OrdinalIgnoreCase))
                if (!Covered(key) && !RulesCPackage.PlanGaps.Contains(key, StringComparer.OrdinalIgnoreCase))
                    problems.Add($"{rule.Id}: {key} liegt nicht im Erfassungsplan");
        }
        foreach (var gap in RulesCPackage.PlanGaps)
            Assert.False(Covered(gap), $"{gap} ist inzwischen im Plan – aus PlanGaps streichen");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Pruefregeln_haben_Vektoren_fuer_nicht_anwendbar_wo_sie_Bedingungen_haben()
    {
        var problems = new List<string>();
        foreach (var rule in RulesCPackage.Rules().Where(r => r.Kind == RuleKind.Check))
        {
            var a = rule.AppliesTo;
            var conditional = a.When.Count > 0 || a.ExcludeDc || a.Os.Count < 2 || a.MinBuild is not null || a.MaxBuild is not null ||
                              (a.Capability is not null && a.IfCapabilityAbsent == AbsentCapability.NotApplicable);
            if (conditional && !rule.Tests.Any(t => t.Expect == ResultStatus.NotApplicable)) problems.Add($"{rule.Id}: kein n/a-Vektor");
            if (a.Capability is not null && a.IfCapabilityAbsent == AbsentCapability.Fail && rule.Tests.Count(t => t.Expect == ResultStatus.Fail) < 2)
                problems.Add($"{rule.Id}: fehlende Fähigkeit muss als fail getestet sein");
            if (!rule.Tests.Any(t => t.Expect == ResultStatus.NotCheckable && t.Items.Values.Any(i => i.Status == ItemStatus.NotCollected)))
                problems.Add($"{rule.Id}: kein Vektor „nicht erfasst → nicht prüfbar“");
            // pass ohne vorhandenen Beleg nur aus einem belegten Standardwert (Flag fromDefault)
            foreach (var test in rule.Tests.Where(t => t.Expect == ResultStatus.Pass && t.Items.Values.All(i => i.Status != ItemStatus.Ok)))
                if (!(test.ExpectFlags ?? []).Contains("fromDefault")) problems.Add($"{rule.Id} „{test.Name}“: pass ohne Beleg und ohne fromDefault");
            foreach (var test in rule.Tests.Where(t => (t.ExpectFlags ?? []).Contains("fromDefault") && t.Expect != ResultStatus.Pass))
                problems.Add($"{rule.Id} „{test.Name}“: fromDefault nur bei pass");
            if (rule.Tests.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != rule.Tests.Count) problems.Add($"{rule.Id}: doppelte Vektornamen");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Abhilfe_aendert_nur_was_die_Pruefung_liest()
    {
        var problems = new List<string>();
        foreach (var rule in RulesCPackage.Rules())
        {
            var read = RulesCPackage.Flatten(rule.Check).SelectMany(c => c switch
            {
                RegistryCheck r => [(r.Key, r.Value)],
                RegistryEffectiveCheck e => e.Sources.Select(s => (s.Key, s.Value)),
                _ => Enumerable.Empty<(string, string)>(),
            }).ToHashSet();
            foreach (var action in rule.Remediation?.Registry ?? [])
            {
                var related = read.Contains((action.Key, action.Value)) ||
                              // Abschalten eines Protokolls schreibt nach Microsoft auch DisabledByDefault = 1
                              (rule.Category == "tls" && action.Value == "DisabledByDefault" && read.Contains((action.Key, "Enabled")));
                if (!related) problems.Add($"{rule.Id}: Abhilfe schreibt {action.Key}\\{action.Value}, das die Prüfung nicht liest");
                if (action.Delete) problems.Add($"{rule.Id}: Abhilfe löscht einen Wert");
            }
            if (rule.Kind == RuleKind.Check && rule.Remediation is null) problems.Add($"{rule.Id}: keine Abhilfe");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Keine_Personennamen_oder_fremde_Kennungen_in_den_Regeldateien()
    {
        foreach (var prefix in RulesCPackage.Prefixes.Append("capabilities/logging"))
        {
            var text = File.ReadAllText(Path.Combine(RepoPaths.Rules, prefix + ".json"));
            Assert.DoesNotMatch(new Regex(@"OneDrive|\\Users\\|@microsoft\.com|ms\.author", RegexOptions.IgnoreCase), text);
        }
    }
}
