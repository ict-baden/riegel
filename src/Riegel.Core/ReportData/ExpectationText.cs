// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Plants;

namespace Riegel.Core.ReportBuilding;

/// <summary>
/// Sollwerte als lesbarer Text aus der Prüfung (Soll-Konfiguration O13, spec 5): je Prüfart eine Vorlage aus
/// WorkflowStrings, Werte in kanonischer Form (spec 7.3), Operatoren als Zeichen oder Wort. Verschachtelte Prüfungen
/// werden mit „→“ (firstOf: der erste Beleg entscheidet), „und“, „oder“ verbunden.
/// </summary>
internal sealed class ExpectationText(string language, PlantSettings settings)
{
    public string Render(Rule rule) => Render(rule.Check, rule);

    private string Render(CheckSpec check, Rule rule) => check switch
    {
        RegistryCheck c => Join(Registry(c.Key, c.Value) + " " + Op(c.Op, c.Expected), Missing(c.WhenMissing)),
        RegistryEffectiveCheck c => Join(
            (c.Sources.Count > 0 ? Registry(c.Sources[0].Key, c.Sources[0].Value) : "?") + " " + Op(c.Op, c.Expected),
            F("Expect_EffectiveSources", string.Join(", ", c.Sources.Select(s => T("Origin_" + s.Origin)))), Missing(c.WhenMissing)),
        SeceditCheck c => Join($"[{c.Section}] {c.Key} {Op(c.Op, c.Expected)}", Missing(c.WhenMissing)),
        AccountPolicyCheck c => $"{T("AccountPolicy_" + c.Field)} {Op(c.Op, c.Expected)}",
        AuditSubcategoryCheck c => F("Expect_Audit", AuditName(c.Guid, rule), T("Audit_" + c.Expected)),
        UserRightCheck c => F("Right_" + c.Mode, c.Right, string.Join(", ", c.Sids)),
        ServiceCheck c => Join(F("Expect_Service", c.Name, string.Join("; ", ServiceParts(c))), T("Absent_" + c.IfAbsent)),
        FeatureCheck c => Join(F("Expect_Feature", c.Name, string.Join(" / ", c.InstallState.Select(s => T("Feature_" + s.ToString(CultureInfo.InvariantCulture))))),
            T("Absent_" + c.IfAbsent)),
        WmiPropertyCheck c => Join(F("Expect_Wmi", WmiClass(c.Item), c.Property, Op(c.Op, c.Expected)),
            c.RowFilter is { } filter ? F("Expect_RowFilter", filter.Property, Op(filter.Op, filter.Value)) : null,
            c.Rows == RowsMode.All ? T("Rows_All") : null),
        DefenderSettingCheck c => F("Expect_Defender", c.Property, Op(c.Op, c.Expected)),
        AsrRuleCheck c => F("Expect_Asr", c.Guid, T("Asr_" + c.Accept)),
        FirewallProfileCheck c => F("Expect_Firewall", T("Profile_" + c.Profile), T("FwProp_" + c.Property), T(c.Expected ? "Bool_True" : "Bool_False")),
        FirewallExposureCheck c => F("Expect_Exposure", string.Join(", ", c.Ports.Select(p => $"{p.Protocol.ToString().ToUpperInvariant()} {p.Port.ToString(CultureInfo.InvariantCulture)}")),
            T("Scope_" + c.Profiles)),
        LifecycleCheck => T("Expect_Lifecycle"),
        UpdateAgeCheck c => F("Expect_UpdateAge", c.MaxDays ?? settings.UpdateMaxDays),
        LsaProtectionCheck => T("Expect_LsaProtection"),
        DeviceGuardCheck c => F("Expect_DeviceGuard", T("DeviceGuard_" + c.Service)),
        AvStatusCheck c => F("Expect_AvStatus", c.MaxSignatureAgeDays ?? 7),
        LocalAccountCheck c => F("Expect_LocalAccount", c.Rid),
        LocalAccountsCheck c => Join(F("Expect_LocalAccounts_" + c.Condition, settings.UnusedAccountDays),
            c.ExcludeRids is { Count: > 0 } rids ? F("Expect_ExceptRids", string.Join(", ", rids)) : null),
        LocalGroupCheck c => Join(F("Expect_LocalGroup", c.Sid),
            c.MaxMembers is { } max ? F("Expect_MaxMembers", max) : null,
            c.AllowedSids is { Count: > 0 } allowed ? F("Expect_AllowedSids", string.Join(", ", allowed)) : null),
        InventoryCheck c => F("Expect_Inventory", T("Renderer_" + c.Renderer)),
        FirstOfCheck c => F("Expect_FirstOf", string.Join(" → ", c.Checks.Select(x => Render(x, rule)))),
        AllOfCheck c => string.Join(" " + T("Conj_And") + " ", c.Checks.Select(x => Bracket(x, rule))),
        AnyOfCheck c => string.Join(" " + T("Conj_Or") + " ", c.Checks.Select(x => Bracket(x, rule))),
        NoneOfCheck c => F("Expect_NoneOf", string.Join("; ", c.Checks.Select(x => Render(x, rule)))),
        NotCheck c => F("Expect_Not", Render(c.Check, rule)),
        _ => check.Type,
    };

    /// <summary>Anwendbarkeit als Text (z. B. „nur Domänenmitglieder; nicht auf Domänencontrollern“), null = überall.</summary>
    public string? AppliesTo(Applicability a)
    {
        var parts = new List<string>();
        bool client = a.Os.Contains("client", StringComparer.OrdinalIgnoreCase), server = a.Os.Contains("server", StringComparer.OrdinalIgnoreCase);
        if (client && !server) parts.Add(T("AppliesTo_Clients"));
        if (server && !client) parts.Add(T("AppliesTo_Servers"));
        if (a.MinBuild is { } min) parts.Add(F("AppliesTo_MinBuild", min));
        if (a.MaxBuild is { } max) parts.Add(F("AppliesTo_MaxBuild", max));
        if (a.Domain == DomainScope.Joined) parts.Add(T("AppliesTo_Joined"));
        if (a.Domain == DomainScope.Standalone) parts.Add(T("AppliesTo_Standalone"));
        if (a.ExcludeDc) parts.Add(T("AppliesTo_NotDc"));
        if (!string.IsNullOrWhiteSpace(a.Capability)) parts.Add(F("AppliesTo_Capability", a.Capability));
        foreach (var w in a.When) parts.Add(F("AppliesTo_Fact", w.Fact, w.Op, CanonicalValue.Render(w.Value) ?? "null"));
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private string Bracket(CheckSpec check, Rule rule)
    {
        var text = Render(check, rule);
        return check is AllOfCheck or AnyOfCheck ? "(" + text + ")" : text;
    }

    private IEnumerable<string> ServiceParts(ServiceCheck c)
    {
        if (c.StartMode is { Count: > 0 } modes) yield return F("Service_StartMode", string.Join("/", modes));
        if (c.State is { Count: > 0 } states) yield return F("Service_State", string.Join("/", states));
    }

    private string Registry(string key, string value) =>
        key.TrimEnd('\\') + "\\" + (value.Length == 0 ? T("Expect_DefaultValue") : value);

    private string? Missing(WhenMissing missing) => missing switch
    {
        WhenMissing.OsDefault => T("Missing_OsDefault"),
        WhenMissing.Pass => T("Missing_Pass"),
        _ => null,
    };

    /// <summary>Operator mit Sollwert: Zeichen für Vergleiche, Wörter für Mengen, Bits und Alter.</summary>
    private string Op(CheckOperator op, JsonElement? expected)
    {
        var value = Value(expected);
        return op switch
        {
            CheckOperator.Eq => "= " + value,
            CheckOperator.Ne => "≠ " + value,
            CheckOperator.Lt => "< " + value,
            CheckOperator.Le => "≤ " + value,
            CheckOperator.Gt => "> " + value,
            CheckOperator.Ge => "≥ " + value,
            CheckOperator.Exists => T("Op_Exists"),
            CheckOperator.NotExists => T("Op_NotExists"),
            _ => F("Op_" + op, value),
        };
    }

    private static string Value(JsonElement? expected) => CanonicalValue.Render(expected) ?? "";

    private static string WmiClass(string item)
    {
        var colon = item.LastIndexOf(':');
        return colon >= 0 ? item[(colon + 1)..] : item;
    }

    private static string AuditName(string guid, Rule rule) =>
        rule.Remediation?.Audit?.FirstOrDefault(a => string.Equals(a.Guid.Trim('{', '}'), guid.Trim('{', '}'), StringComparison.OrdinalIgnoreCase))?.Subcategory
        ?? guid;

    private static string Join(string main, params string?[] extras)
    {
        var parts = extras.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        return parts.Count == 0 ? main : main + " (" + string.Join("; ", parts) + ")";
    }

    private string T(string key) => WorkflowText.Get(key, language);

    private string F(string key, params object?[] args) => WorkflowText.Format(key, language, args);
}
