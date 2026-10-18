// SPDX-License-Identifier: MIT
using Riegel.Core.Model;

namespace Riegel.Core.Evaluation;

/// <summary>
/// Welche Snapshot-Einträge eine Prüfung lesen kann (ohne sie auszuwerten): für den Mutationstest (spec 8.3) und für
/// Werkzeuge, die Regeln gegen den Erfassungsplan abgleichen. Registry-Schlüssel als <c>reg:HKLM\…</c>.
/// </summary>
internal static class CheckEvidence
{
    private static string Reg(string key) => EvidenceStore.RegistryItemKey(key);

    public static IReadOnlyList<string> KeysOf(CheckSpec check)
    {
        var keys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(check, keys, 0);
        return [.. keys];
    }

    private static void Collect(CheckSpec check, SortedSet<string> keys, int depth)
    {
        if (check is null || depth > 32) return;
        IEnumerable<string> own = check switch
        {
            RegistryCheck r => [Reg(r.Key)],
            RegistryEffectiveCheck e => e.Sources.Select(s => Reg(s.Key)),
            SeceditCheck => [ItemKeys.ParsedSecedit],
            AccountPolicyCheck => [ItemKeys.NetUserModals, ItemKeys.ParsedSecedit],
            AuditSubcategoryCheck => [ItemKeys.AuditPolicy],
            UserRightCheck => [ItemKeys.LsaRights, ItemKeys.ParsedSecedit, ItemKeys.NetUserEnum, Known.UserAccount],
            ServiceCheck s => [Known.Service, Reg(Known.ServicesRoot + "\\" + s.Name)],
            FeatureCheck => [Known.OptionalFeature],
            WmiPropertyCheck w => [w.Item],
            DefenderSettingCheck d => [d.Source == DefenderSource.Status ? Known.MpStatus : Known.MpPreference, Known.MpStatus, ItemKeys.WscAntivirus, Known.SecurityCenterAv],
            AsrRuleCheck => [Known.MpPreference, Reg(Known.AsrPolicyRules), Reg(Known.MdmDefender), Known.MpStatus, ItemKeys.WscAntivirus, Known.SecurityCenterAv],
            FirewallProfileCheck f => [ItemKeys.Firewall, .. ProfileKeys(f.Profile)],
            FirewallExposureCheck => [ItemKeys.Firewall, ItemKeys.ParsedFirewallRulesRegistry,
                .. ProfileKeys(FirewallProfileName.Domain), .. ProfileKeys(FirewallProfileName.Private), .. ProfileKeys(FirewallProfileName.Public)],
            UpdateAgeCheck => [Known.QuickFix, ItemKeys.WindowsUpdateHistory],
            LsaProtectionCheck => [ItemKeys.LsassProtection, ItemKeys.WininitEvent12, Reg(Known.Lsa)],
            DeviceGuardCheck => [Known.DeviceGuard],
            AvStatusCheck => [Known.MpStatus, ItemKeys.WscAntivirus, Known.SecurityCenterAv],
            LocalAccountCheck => [Known.UserAccount, ItemKeys.NetUserEnum],
            LocalAccountsCheck => [ItemKeys.NetUserEnum],
            LocalGroupCheck g => [ItemKeys.LocalGroup(g.Sid), ItemKeys.NetUserEnum, Known.UserAccount],
            InventoryCheck i => i.Items,
            _ => [],
        };
        foreach (var k in own) keys.Add(k);
        IEnumerable<CheckSpec> children = check switch
        {
            FirstOfCheck f => f.Checks,
            AllOfCheck a => a.Checks,
            AnyOfCheck a => a.Checks,
            NoneOfCheck n => n.Checks,
            NotCheck n => n.Check is null ? [] : [n.Check],
            _ => [],
        };
        foreach (var child in children) Collect(child, keys, depth + 1);
    }

    private static IEnumerable<string> ProfileKeys(FirewallProfileName profile)
    {
        var (policy, local) = Known.FirewallProfileKeys(profile);
        return [Reg(policy), Reg(policy + "\\Logging"), Reg(local), Reg(local + "\\Logging")];
    }
}
