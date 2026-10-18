// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Rules.C;

/// <summary>
/// Überwachungs-Unterkategorien (Regelpaket rules-c): Jede GUID in den Regeln ist eine der von Microsoft dokumentierten
/// Unterkategorien, gehört zu genau einem Namen und wird nur von einer Regel geprüft; die Abhilfe nennt dieselbe GUID
/// mit dem passenden englischen Namen.
/// </summary>
public sealed class AuditSubcategoryGuidTests
{
    /// <summary>
    /// [MS-GPAC] 2.2.1.2 „Subcategory and SubcategoryGUID“,
    /// https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpac/77878370-0712-47cd-997d-b07053429f6d
    /// (gesehen 2026-10-18; Seitenstand 2024-10-30). Nur Kennungen und Namen, keine Texte.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> MsGpacSubcategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["{0CCE9213-69AE-11D9-BED3-505054503030}"] = "IPsec Driver",
        ["{0CCE9212-69AE-11D9-BED3-505054503030}"] = "System Integrity",
        ["{0CCE9211-69AE-11D9-BED3-505054503030}"] = "Security System Extension",
        ["{0CCE9210-69AE-11D9-BED3-505054503030}"] = "Security State Change",
        ["{0CCE9214-69AE-11D9-BED3-505054503030}"] = "Other System Events",
        ["{0CCE9243-69AE-11D9-BED3-505054503030}"] = "Network Policy Server",
        ["{0CCE921C-69AE-11D9-BED3-505054503030}"] = "Other Logon/Logoff Events",
        ["{0CCE921B-69AE-11D9-BED3-505054503030}"] = "Special Logon",
        ["{0CCE921A-69AE-11D9-BED3-505054503030}"] = "IPsec Extended Mode",
        ["{0CCE9219-69AE-11D9-BED3-505054503030}"] = "IPsec Quick Mode",
        ["{0CCE9218-69AE-11D9-BED3-505054503030}"] = "IPsec Main Mode",
        ["{0CCE9217-69AE-11D9-BED3-505054503030}"] = "Account Lockout",
        ["{0CCE9216-69AE-11D9-BED3-505054503030}"] = "Logoff",
        ["{0CCE9215-69AE-11D9-BED3-505054503030}"] = "Logon",
        ["{0CCE9223-69AE-11D9-BED3-505054503030}"] = "Handle Manipulation",
        ["{0CCE9244-69AE-11D9-BED3-505054503030}"] = "Detailed File Share",
        ["{0CCE9227-69AE-11D9-BED3-505054503030}"] = "Other Object Access Events",
        ["{0CCE9226-69AE-11D9-BED3-505054503030}"] = "Filtering Platform Connection",
        ["{0CCE9225-69AE-11D9-BED3-505054503030}"] = "Filtering Platform Packet Drop",
        ["{0CCE9224-69AE-11D9-BED3-505054503030}"] = "File Share",
        ["{0CCE9222-69AE-11D9-BED3-505054503030}"] = "Application Generated",
        ["{0CCE9221-69AE-11D9-BED3-505054503030}"] = "Certification Services",
        ["{0CCE9220-69AE-11D9-BED3-505054503030}"] = "SAM",
        ["{0CCE921F-69AE-11D9-BED3-505054503030}"] = "Kernel Object",
        ["{0CCE921E-69AE-11D9-BED3-505054503030}"] = "Registry",
        ["{0CCE921D-69AE-11D9-BED3-505054503030}"] = "File System",
        ["{0CCE9229-69AE-11D9-BED3-505054503030}"] = "Non Sensitive Privilege Use",
        ["{0CCE922A-69AE-11D9-BED3-505054503030}"] = "Other Privilege Use Events",
        ["{0CCE9228-69AE-11D9-BED3-505054503030}"] = "Sensitive Privilege Use",
        ["{0CCE922D-69AE-11D9-BED3-505054503030}"] = "DPAPI Activity",
        ["{0CCE922C-69AE-11D9-BED3-505054503030}"] = "Process Termination",
        ["{0CCE922B-69AE-11D9-BED3-505054503030}"] = "Process Creation",
        ["{0CCE922E-69AE-11D9-BED3-505054503030}"] = "RPC Events",
        ["{0CCE9232-69AE-11D9-BED3-505054503030}"] = "MPSSVC Rule-Level Policy Change",
        ["{0CCE9234-69AE-11D9-BED3-505054503030}"] = "Other Policy Change Events",
        ["{0CCE9233-69AE-11D9-BED3-505054503030}"] = "Filtering Platform Policy Change",
        ["{0CCE922F-69AE-11D9-BED3-505054503030}"] = "Audit Policy Change",
        ["{0CCE9231-69AE-11D9-BED3-505054503030}"] = "Authorization Policy Change",
        ["{0CCE9230-69AE-11D9-BED3-505054503030}"] = "Authentication Policy Change",
        ["{0CCE923A-69AE-11D9-BED3-505054503030}"] = "Other Account Management Events",
        ["{0CCE9239-69AE-11D9-BED3-505054503030}"] = "Application Group Management",
        ["{0CCE9238-69AE-11D9-BED3-505054503030}"] = "Distribution Group Management",
        ["{0CCE9237-69AE-11D9-BED3-505054503030}"] = "Security Group Management",
        ["{0CCE9236-69AE-11D9-BED3-505054503030}"] = "Computer Account Management",
        ["{0CCE9235-69AE-11D9-BED3-505054503030}"] = "User Account Management",
        ["{0CCE923E-69AE-11D9-BED3-505054503030}"] = "Detailed Directory Service Replication",
        ["{0CCE923B-69AE-11D9-BED3-505054503030}"] = "Directory Service Access",
        ["{0CCE923D-69AE-11D9-BED3-505054503030}"] = "Directory Service Replication",
        ["{0CCE923C-69AE-11D9-BED3-505054503030}"] = "Directory Service Changes",
        ["{0CCE9241-69AE-11D9-BED3-505054503030}"] = "Other Account Logon Events",
        ["{0CCE9240-69AE-11D9-BED3-505054503030}"] = "Kerberos Service Ticket Operations",
        ["{0CCE923F-69AE-11D9-BED3-505054503030}"] = "Credential Validation",
        ["{0CCE9242-69AE-11D9-BED3-505054503030}"] = "Kerberos Authentication Service",
        ["{0CCE9245-69AE-11D9-BED3-505054503030}"] = "Removable Storage",
        ["{0CCE9246-69AE-11D9-BED3-505054503030}"] = "Central Access Policy Staging",
        ["{0CCE9247-69AE-11D9-BED3-505054503030}"] = "User/Device Claims",
        ["{0CCE9248-69AE-11D9-BED3-505054503030}"] = "PNP Activity",
        ["{0CCE9249-69AE-11D9-BED3-505054503030}"] = "Group Membership",
    };

    /// <summary>Erwartete Zuordnung Regel → Unterkategorie (spec 2.1 und research-regeln.md, Anhang).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> ExpectedPerRule = new Dictionary<string, string[]>
    {
        ["AUD-01"] = ["Credential Validation"],
        ["AUD-02"] = ["Security Group Management"],
        ["AUD-03"] = ["User Account Management"],
        ["AUD-04"] = ["Process Creation"],
        ["AUD-05"] = ["Logon"],
        ["AUD-06"] = ["Logoff"],
        ["AUD-07"] = ["Account Lockout"],
        ["AUD-08"] = ["Special Logon"],
        ["AUD-09"] = ["Other Logon/Logoff Events"],
        ["AUD-10"] = ["Audit Policy Change"],
        ["AUD-11"] = ["Authentication Policy Change"],
        ["AUD-12"] = ["Sensitive Privilege Use"],
        ["AUD-13"] = ["Security State Change"],
        ["AUD-14"] = ["Security System Extension"],
        ["AUD-15"] = ["System Integrity"],
        ["AUD-16"] = ["Removable Storage"],
        ["AUD-17"] = ["PNP Activity"],
        ["AUD-18"] = ["Other Object Access Events"],
        ["AUD-19"] = ["MPSSVC Rule-Level Policy Change"],
        ["AUD-20"] = ["File Share", "Detailed File Share"],
    };

    private static IReadOnlyList<Rule> AudRules() =>
        RiegelJson.Deserialize<RuleFile>(File.ReadAllBytes(Path.Combine(RepoPaths.Rules, "AUD.json"))).Rules;

    private static IEnumerable<AuditSubcategoryCheck> AuditChecks(Rule rule) =>
        RulesCPackage.Flatten(rule.Check).OfType<AuditSubcategoryCheck>();

    [Fact]
    public void Die_Microsoft_Liste_ist_eindeutig()
    {
        Assert.Equal(58, MsGpacSubcategories.Count); // so viele Zeilen hat die Tabelle in [MS-GPAC] 2.2.1.2
        Assert.Equal(MsGpacSubcategories.Count, MsGpacSubcategories.Values.Distinct(StringComparer.Ordinal).Count());
        foreach (var guid in MsGpacSubcategories.Keys)
            Assert.Matches(@"^\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\}$", guid);
    }

    [Fact]
    public void Jede_GUID_ist_dokumentiert_eindeutig_und_genau_einer_Regel_zugeordnet()
    {
        var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        foreach (var rule in AudRules())
        {
            var checks = AuditChecks(rule).ToList();
            foreach (var check in checks)
            {
                if (!MsGpacSubcategories.ContainsKey(check.Guid)) problems.Add($"{rule.Id}: {check.Guid} ist keine Unterkategorie aus [MS-GPAC]");
                else if (!owner.TryAdd(check.Guid, rule.Id) && owner[check.Guid] != rule.Id)
                    problems.Add($"{rule.Id}: {check.Guid} wird schon von {owner[check.Guid]} geprüft");
            }
            if (ExpectedPerRule.TryGetValue(rule.Id, out var names))
            {
                var actual = checks.Select(c => MsGpacSubcategories.GetValueOrDefault(c.Guid, "?")).Order(StringComparer.Ordinal);
                if (!actual.SequenceEqual(names.Order(StringComparer.Ordinal))) problems.Add($"{rule.Id}: prüft {string.Join(", ", actual)}");
            }
            else if (checks.Count > 0) problems.Add($"{rule.Id}: unerwartete Unterkategorie-Prüfung");
        }
        Assert.Equal(ExpectedPerRule.Values.Sum(v => v.Length), owner.Count);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Abhilfe_nennt_dieselben_GUIDs_mit_dem_englischen_Namen()
    {
        var problems = new List<string>();
        foreach (var rule in AudRules())
        {
            var checks = AuditChecks(rule).Select(c => c.Guid.ToUpperInvariant()).Order(StringComparer.Ordinal).ToList();
            var audit = rule.Remediation?.Audit ?? [];
            if (checks.Count == 0)
            {
                if (audit.Count > 0) problems.Add($"{rule.Id}: Überwachungs-Abhilfe ohne Unterkategorie-Prüfung");
                continue;
            }
            if (!audit.Select(a => a.Guid.ToUpperInvariant()).Order(StringComparer.Ordinal).SequenceEqual(checks))
                problems.Add($"{rule.Id}: Abhilfe und Prüfung nennen verschiedene GUIDs");
            foreach (var action in audit)
            {
                if (!MsGpacSubcategories.TryGetValue(action.Guid, out var name)) { problems.Add($"{rule.Id}: {action.Guid} unbekannt"); continue; }
                if (action.Subcategory != "Audit " + name) problems.Add($"{rule.Id}: {action.Subcategory} statt „Audit {name}“");
                if (action.Setting == AuditSetting.None) problems.Add($"{rule.Id}: Abhilfe schaltet die Überwachung ab");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Vektoren_nutzen_nur_die_GUIDs_der_Regel()
    {
        var problems = new List<string>();
        foreach (var rule in AudRules())
        {
            var guids = AuditChecks(rule).Select(c => c.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (guids.Count == 0) continue;
            foreach (var test in rule.Tests)
                if (test.Items.TryGetValue("api:audit-policy", out var item) && item.Value is { ValueKind: System.Text.Json.JsonValueKind.Object } value)
                    foreach (var property in value.EnumerateObject())
                    {
                        if (!guids.Contains(property.Name)) problems.Add($"{rule.Id} „{test.Name}“: fremde GUID {property.Name}");
                        if (property.Value.ValueKind != System.Text.Json.JsonValueKind.Number || property.Value.GetInt32() is < 0 or > 3)
                            problems.Add($"{rule.Id} „{test.Name}“: Wert {property.Value} ist nicht 0–3");
                    }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
