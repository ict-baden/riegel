// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;

namespace Riegel.Tools.RuleTool;

/// <summary>
/// coverage: stellt die Belegschlüssel aller Regeln (spec 2.3, Spalte „Evidence“) dem Erfassungsplan
/// contracts/collection-plan.json gegenüber und listet, was der Sammler nicht erfasst. Ein „Rückfall“ ist eine nachrangige
/// Quelle einer Prüfart, deren vorrangige Quelle im Plan liegt.
/// </summary>
public static class CoverageAnalyzer
{
    /// <summary>Ein Schlüssel einer Regel, mit Rang innerhalb der Prüfart.</summary>
    public sealed record Evidence(string RuleId, string CheckType, string Key, bool Fallback);

    /// <summary>Ein Schlüssel, den der Plan nicht abdeckt.</summary>
    public sealed record Gap(string RuleId, string CheckType, string Key, bool Fallback);

    /// <summary>Liest den Plan.</summary>
    public static CollectionPlan LoadPlan(string path)
    {
        try { return RiegelJson.ReadFile<CollectionPlan>(path, 16 * 1024 * 1024); }
        catch (Exception e) { throw new RuleToolException(RglCodesCatalogue.PlanInvalid, $"{path}: {e.Message}"); }
    }

    /// <summary>Alle Belegschlüssel der Regeln, in Regelreihenfolge.</summary>
    public static IReadOnlyList<Evidence> EvidenceKeys(IEnumerable<Rule> rules)
    {
        var list = new List<Evidence>();
        foreach (var rule in rules.OrderBy(r => r.Id, RuleIdComparer.Instance))
            foreach (var check in RuleRepository.Flatten(rule.Check))
                foreach (var (key, fallback) in Keys(check))
                    list.Add(new Evidence(rule.Id, check.Type, key, fallback));
        return list.Distinct().ToList();
    }

    /// <summary>Schlüssel einer einzelnen Prüfung (zusammengesetzte Prüfungen liefern keine eigenen).</summary>
    public static IEnumerable<(string Key, bool Fallback)> Keys(CheckSpec check)
    {
        const string defender = @"wmi:root\Microsoft\Windows\Defender:";
        switch (check)
        {
            case RegistryCheck r:
                yield return (ItemKeys.Registry(r.Key), false);
                break;
            case RegistryEffectiveCheck e:
                foreach (var s in e.Sources) yield return (ItemKeys.Registry(s.Key), false);
                break;
            case SeceditCheck:
                yield return (ItemKeys.ParsedSecedit, false);
                break;
            case AccountPolicyCheck a:
                if (a.Field is AccountPolicyField.PasswordComplexity or AccountPolicyField.ClearTextPassword)
                    yield return (ItemKeys.ParsedSecedit, false);
                else
                {
                    yield return (ItemKeys.NetUserModals, false);
                    yield return (ItemKeys.ParsedSecedit, true);
                }
                break;
            case AuditSubcategoryCheck:
                yield return (ItemKeys.AuditPolicy, false);
                break;
            case UserRightCheck:
                yield return (ItemKeys.LsaRights, false);
                yield return (ItemKeys.ParsedSecedit, true);
                break;
            case ServiceCheck s:
                yield return (@"wmi:root\cimv2:Win32_Service", false);
                yield return (ItemKeys.Registry(@"SYSTEM\CurrentControlSet\Services\" + s.Name), true);
                break;
            case FeatureCheck:
                yield return (@"wmi:root\cimv2:Win32_OptionalFeature", false);
                break;
            case WmiPropertyCheck w:
                yield return (w.Item, false);
                break;
            case DefenderSettingCheck d:
                yield return (defender + (d.Source == DefenderSource.Status ? "MSFT_MpComputerStatus" : "MSFT_MpPreference"), false);
                break;
            case AsrRuleCheck:
                yield return (defender + "MSFT_MpPreference", false);
                yield return (ItemKeys.Registry(@"SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\ASR\Rules"), true);
                yield return (ItemKeys.Registry(@"SOFTWARE\Microsoft\PolicyManager\current\device\Defender"), true);
                break;
            case FirewallProfileCheck:
                yield return (ItemKeys.Firewall, false);
                yield return (ItemKeys.Registry(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy"), true);
                break;
            case FirewallExposureCheck:
                yield return (ItemKeys.Firewall, false);
                yield return (ItemKeys.ParsedFirewallRulesRegistry, false);
                break;
            case LifecycleCheck:
                break;
            case UpdateAgeCheck:
                yield return (@"wmi:root\cimv2:Win32_QuickFixEngineering", true);
                yield return (ItemKeys.WindowsUpdateHistory, true);
                break;
            case LsaProtectionCheck:
                yield return (ItemKeys.LsassProtection, false);
                yield return (ItemKeys.WininitEvent12, false);
                yield return (ItemKeys.Registry(@"SYSTEM\CurrentControlSet\Control\Lsa"), true);
                break;
            case DeviceGuardCheck:
                yield return (@"wmi:root\Microsoft\Windows\DeviceGuard:Win32_DeviceGuard", false);
                break;
            case AvStatusCheck:
                yield return (defender + "MSFT_MpComputerStatus", false);
                yield return (ItemKeys.WscAntivirus, true);
                yield return (@"wmi:root\SecurityCenter2:AntivirusProduct", true);
                break;
            case LocalAccountCheck:
                yield return (@"wmi:root\cimv2:Win32_UserAccount", false);
                yield return (ItemKeys.NetUserEnum, true);
                break;
            case LocalAccountsCheck:
                yield return (ItemKeys.NetUserEnum, false);
                break;
            case LocalGroupCheck g:
                yield return (ItemKeys.LocalGroup(g.Sid), false);
                break;
            case InventoryCheck i:
                foreach (var item in i.Items) yield return (item, false);
                break;
        }
    }

    /// <summary>Lücken: Schlüssel, die der Plan nicht abdeckt.</summary>
    public static IReadOnlyList<Gap> Gaps(IEnumerable<Rule> rules, CollectionPlan plan) =>
        EvidenceKeys(rules).Where(e => !Covers(plan, e.Key)).Select(e => new Gap(e.RuleId, e.CheckType, e.Key, e.Fallback)).ToList();

    /// <summary>Liegt der Schlüssel im Plan (Registry-Bereich, Einzelschlüssel, WMI-, API-, Ereignis- oder Rohdaten-Eintrag)?</summary>
    public static bool Covers(CollectionPlan plan, string key)
    {
        if (key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase))
            return RegistryCovered(plan, key[4..]);
        if (key.StartsWith("parsed:", StringComparison.OrdinalIgnoreCase))
        {
            return key.ToLowerInvariant() switch
            {
                "parsed:secedit" => Covers(plan, ItemKeys.RawSeceditInf),
                "parsed:registry-pol" => Covers(plan, ItemKeys.RawRegistryPol),
                "parsed:gpresult" => Covers(plan, ItemKeys.RawGpresultXml),
                "parsed:firewall-rules-registry" => RegistryCovered(plan, @"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules")
                                                    && RegistryCovered(plan, @"HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall\FirewallRules"),
                _ => false,
            };
        }
        return plan.Wmi.Any(w => ItemKeys.Same(w.Key, key))
            || plan.Api.Any(a => ItemKeys.Same(a.Key, key))
            || plan.Events.Any(e => ItemKeys.Same(e.Key, key))
            || plan.Raw.Any(r => ItemKeys.Same(r.Key, key));
    }

    /// <summary>Registry-Pfad (<c>HKLM\…</c>) innerhalb eines rekursiven Bereichs (bis MaxDepth), gleich einem Einzelschlüssel oder ein direkter Unterschlüssel eines children-Eintrags.</summary>
    public static bool RegistryCovered(CollectionPlan plan, string path)
    {
        var p = path.TrimEnd('\\');
        foreach (var root in plan.Registry.Roots.Concat(plan.Registry.Keys))
        {
            var r = root.Path.TrimEnd('\\');
            if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase)) return true;
            if (!p.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)) continue;
            var depth = p[(r.Length + 1)..].Split('\\').Length;
            switch (root.Kind)
            {
                case RegistryPlanKind.Recursive when depth <= root.MaxDepth:
                case RegistryPlanKind.Children when depth == 1:
                    return true;
            }
        }
        return false;
    }
}
