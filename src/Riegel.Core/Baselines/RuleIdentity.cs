// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Core.Baselines;

/// <summary>
/// Identität einer importierten Regel (spec 2.15): normalisierter Schlüssel plus Prüfart, ohne den Sollwert. So bleibt
/// die ID gleich, wenn sich nur die Erwartung ändert („Baseline-Erwartung geändert“), und Ausnahmen verwaisen nicht.
/// ID-Teil: die ersten 8 Hex-Zeichen von SHA-1 über den UTF-8-Text der Identität.
/// </summary>
internal static class RuleIdentity
{
    /// <summary>„&lt;Prüfart&gt;|&lt;normalisierter Schlüssel&gt;“, klein geschrieben (invariant).</summary>
    internal static string Of(CheckSpec check) => (check switch
    {
        RegistryCheck r => Join(CheckTypes.Registry, Key(r.Key), r.Value),
        RegistryEffectiveCheck r => Join(CheckTypes.RegistryEffective, string.Join(";", r.Sources.Select(s => Key(s.Key) + "|" + s.Value))),
        SeceditCheck s => Join(CheckTypes.Secedit, s.Section.Trim(), s.Key.Trim()),
        AccountPolicyCheck a => Join(CheckTypes.AccountPolicy, a.Field.ToString()),
        AuditSubcategoryCheck a => Join(CheckTypes.AuditSubcategory, a.Guid.Trim().Trim('{', '}')),
        UserRightCheck u => Join(CheckTypes.UserRight, u.Right.Trim()),
        ServiceCheck s => Join(CheckTypes.Service, s.Name.Trim()),
        FeatureCheck f => Join(CheckTypes.Feature, f.Name.Trim()),
        WmiPropertyCheck w => Join(CheckTypes.WmiProperty, w.Item.Trim(), w.Property.Trim(),
            w.RowFilter is { } rf ? rf.Property + " " + rf.Op + " " + CanonicalValue.Render(rf.Value) : ""),
        DefenderSettingCheck d => Join(CheckTypes.DefenderSetting, d.Source.ToString(), d.Property.Trim()),
        AsrRuleCheck a => Join(CheckTypes.AsrRule, a.Guid.Trim().Trim('{', '}')),
        FirewallProfileCheck f => Join(CheckTypes.FirewallProfile, f.Profile.ToString(), f.Property.ToString()),
        FirewallExposureCheck f => Join(CheckTypes.FirewallExposure, f.Profiles.ToString(),
            string.Join(";", f.Ports.Select(p => p.Protocol + "/" + p.Port.ToString(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal))),
        DeviceGuardCheck d => Join(CheckTypes.DeviceGuard, d.Service.ToString()),
        LocalAccountCheck a => Join(CheckTypes.LocalAccount, a.Rid.ToString(CultureInfo.InvariantCulture)),
        LocalAccountsCheck a => Join(CheckTypes.LocalAccounts, a.Condition.ToString()),
        LocalGroupCheck g => Join(CheckTypes.LocalGroup, g.Sid.Trim()),
        InventoryCheck i => Join(CheckTypes.Inventory, i.Renderer.ToString()),
        FirstOfCheck c => Composite(CheckTypes.FirstOf, c.Checks),
        AllOfCheck c => Composite(CheckTypes.AllOf, c.Checks),
        AnyOfCheck c => Composite(CheckTypes.AnyOf, c.Checks),
        NoneOfCheck c => Composite(CheckTypes.NoneOf, c.Checks),
        NotCheck n => CheckTypes.Not + "(" + Of(n.Check) + ")",
        // lifecycle, updateAge, lsaProtection, avStatus: die Prüfart allein ist die Identität
        _ => check.Type,
    }).ToLowerInvariant();

    /// <summary>Die ersten 8 Hex-Zeichen von SHA-1 über die Identität.</summary>
    internal static string Hash8(string identity) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()[..8];

    /// <summary>Kanonische Form einer Prüfung (für „gleicher Inhalt“ beim Erkennen von Kollisionen).</summary>
    internal static string Content(CheckSpec check)
    {
        var json = Model.Json.RiegelJson.SerializeCompact(check);
        using var document = JsonDocument.Parse(json);
        return CanonicalValue.Render(document.RootElement) ?? "";
    }

    private static string Key(string key) => key.Trim().TrimEnd('\\');

    private static string Join(params string[] parts) => string.Join("|", parts);

    private static string Composite(string type, IReadOnlyList<CheckSpec> checks) => type + "(" + string.Join(",", checks.Select(Of)) + ")";
}
