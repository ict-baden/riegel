// SPDX-License-Identifier: MIT
using Riegel.Core.Localization;
using Riegel.Core.Model;
using Riegel.Core.ReportBuilding;
using Riegel.Tests.TestSupport;

// Namensraum ReportBuilding wie in Core: ein Namensraum „ReportData“ verdeckte den Typ ReportData (auch in fremden Tests)
namespace Riegel.Tests.ReportBuilding;

/// <summary>Sollwerte als lesbarer Text (O13) für jede Prüfart in allen fünf Sprachen; kein Textschlüssel bleibt stehen.</summary>
public sealed class ExpectationTextTests
{
    private static readonly string[] KeyPrefixes =
    [
        "Expect_", "Op_", "Origin_", "AccountPolicy_", "Audit_", "Right_", "Service_", "Absent_", "Feature_", "Rows_", "Asr_", "Profile_", "FwProp_",
        "Bool_", "Scope_", "DeviceGuard_", "Renderer_", "Conj_", "AppliesTo_", "Missing_",
    ];

    private static IEnumerable<CheckSpec> AllChecks()
    {
        var one = JsonValues.Number(1);
        var registry = new RegistryCheck(@"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "RequireSecuritySignature", RegValueType.Dword,
            null, CheckOperator.Eq, one, WhenMissing.OsDefault, null, null, null);
        yield return registry;
        yield return registry with { Value = "", Op = CheckOperator.Exists, Expected = null, WhenMissing = WhenMissing.Pass };
        foreach (var op in Enum.GetValues<CheckOperator>()) yield return registry with { Op = op, WhenMissing = WhenMissing.Fail };
        yield return new RegistryEffectiveCheck([new EffectiveSource(@"HKLM\SOFTWARE\Policies\X", "V", ValueOrigin.Policy), new EffectiveSource(@"HKLM\SOFTWARE\X", "V", ValueOrigin.Local),
            new EffectiveSource(@"HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\X", "V", ValueOrigin.Mdm), new EffectiveSource(@"HKLM\Y", "V", ValueOrigin.Other)],
            RegValueType.Dword, null, CheckOperator.Ge, one, WhenMissing.NotCheckable, null, null, null);
        yield return new SeceditCheck("System Access", "MinimumPasswordLength", CheckOperator.Ge, JsonValues.Number(14), SeceditValueKind.Number, WhenMissing.Fail);
        foreach (var field in Enum.GetValues<AccountPolicyField>()) yield return new AccountPolicyCheck(field, CheckOperator.Le, one);
        foreach (var e in Enum.GetValues<AuditExpectation>()) yield return new AuditSubcategoryCheck("{0CCE923F-69AE-11D9-BED3-505054503030}", e);
        foreach (var m in Enum.GetValues<UserRightMode>()) yield return new UserRightCheck("SeDebugPrivilege", m, ["S-1-5-32-544"]);
        foreach (var a in Enum.GetValues<AbsentOutcome>())
        {
            yield return new ServiceCheck("Spooler", ["Disabled"], ["Stopped"], a);
            yield return new FeatureCheck("SMB1Protocol", [2, 3], a);
        }
        yield return new FeatureCheck("Telnet", [1], AbsentOutcome.Pass);
        yield return new WmiPropertyCheck(@"wmi:root\Microsoft\Windows\SMB:MSFT_SmbServerConfiguration.GetConfiguration", "RequireSecuritySignature",
            new RowFilter("DriveLetter", CheckOperator.Eq, JsonValues.Text("C:")), RowsMode.All, CheckOperator.Eq, JsonValues.True, NoRowsOutcome.NotCheckable);
        yield return new DefenderSettingCheck(DefenderSource.Status, "AntivirusSignatureLastUpdated", CheckOperator.AgeDaysLe, JsonValues.Number(7));
        foreach (var a in Enum.GetValues<AsrAccept>()) yield return new AsrRuleCheck("9e6c4e1f-7d60-472f-ba1a-a39ef669e4b2", a);
        foreach (var p in Enum.GetValues<FirewallProfileName>())
            foreach (var prop in Enum.GetValues<FirewallProfileProperty>())
                yield return new FirewallProfileCheck(p, prop, prop != FirewallProfileProperty.AllowLocalRules);
        foreach (var s in Enum.GetValues<ProfileScope>()) yield return new FirewallExposureCheck([new PortSpec(NetProtocol.Tcp, 3389), new PortSpec(NetProtocol.Udp, 445)], s);
        yield return new LifecycleCheck();
        yield return new UpdateAgeCheck(null);
        yield return new LsaProtectionCheck();
        foreach (var s in Enum.GetValues<DeviceGuardService>()) yield return new DeviceGuardCheck(s);
        yield return new AvStatusCheck(null);
        yield return new LocalAccountCheck(501, LocalAccountExpectation.Disabled);
        foreach (var c in Enum.GetValues<LocalAccountCondition>()) yield return new LocalAccountsCheck(c, [500, 501], true);
        yield return new LocalGroupCheck("S-1-5-32-544", 2, ["S-1-5-32-544"]);
        foreach (var r in Enum.GetValues<InventoryRenderer>()) yield return new InventoryCheck(r, []);
        yield return new FirstOfCheck([registry, new LsaProtectionCheck()]);
        yield return new AllOfCheck([new AnyOfCheck([registry, registry]), registry]);
        yield return new AnyOfCheck([registry, new AllOfCheck([registry])]);
        yield return new NoneOfCheck([registry]);
        yield return new NotCheck(registry);
    }

    [Fact]
    public void Jede_Pruefart_in_jeder_Sprache_ohne_Textschluessel()
    {
        var checks = AllChecks().ToList();
        Assert.Contains(checks, c => c.Type == CheckTypes.Not);
        Assert.Equal(CheckTypes.All.Order(StringComparer.Ordinal), checks.Select(c => c.Type).Distinct().Order(StringComparer.Ordinal));
        foreach (var language in UiLanguage.Supported)
        {
            var text = new ExpectationText(language, PlantSettings.Default);
            foreach (var check in checks)
            {
                var rule = RuleBuilder.Check("X-01").With(check).Build();
                var rendered = text.Render(rule);
                Assert.False(string.IsNullOrWhiteSpace(rendered), $"{language}: {check.Type}");
                foreach (var prefix in KeyPrefixes)
                    Assert.False(rendered.Contains(prefix, StringComparison.Ordinal), $"{language}: {check.Type}: „{rendered}“ enthält {prefix}");
            }
        }
    }

    [Fact]
    public void Beispiele_auf_Englisch_und_Deutsch()
    {
        var en = new ExpectationText("en", PlantSettings.Default);
        var de = new ExpectationText("de", PlantSettings.Default);
        var registry = RuleBuilder.Check("NET-08").Registry(@"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "RequireSecuritySignature",
            RegValueType.Dword, CheckOperator.Eq, 1, WhenMissing.OsDefault).Build();
        Assert.Equal(@"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters\RequireSecuritySignature = 1 (if not set: documented Windows default)",
            en.Render(registry));
        Assert.Equal(@"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters\RequireSecuritySignature = 1 (nicht gesetzt: belegter Windows-Standardwert)",
            de.Render(registry));
        var audit = RuleBuilder.Check("AUD-01", "audit").With(new AuditSubcategoryCheck("{0CCE923F-69AE-11D9-BED3-505054503030}", AuditExpectation.SuccessAndFailure))
            .Remediation(new RemediationSpec(null, null, [new AuditAction("0CCE923F-69AE-11D9-BED3-505054503030", "Credential Validation", AuditSetting.SuccessAndFailure)], null))
            .Build();
        Assert.Equal("Audit Credential Validation: Success and Failure", en.Render(audit));
        Assert.Equal("Überwachung Credential Validation: Erfolg und Fehler", de.Render(audit));
        Assert.Equal("Latest cumulative update at most 35 days old", en.Render(RuleBuilder.Check("LIFE-02", "lifecycle").With(new UpdateAgeCheck(null)).Build()));
        Assert.Equal("Firewall, Profil Öffentlich: eingehende Verbindungen standardmäßig blockiert: ja",
            de.Render(RuleBuilder.Check("FW-06", "firewall").With(new FirewallProfileCheck(FirewallProfileName.Public, FirewallProfileProperty.DefaultInboundBlock, true)).Build()));
        var first = RuleBuilder.Check("CRED-06").With(new FirstOfCheck([new LsaProtectionCheck(), new DeviceGuardCheck(DeviceGuardService.Vbs)])).Build();
        Assert.Equal("first proof decides: LSA protection running (proven at runtime, not only configured) → Virtualization-based security running", en.Render(first));
    }

    [Fact]
    public void Anwendbarkeit_als_Text()
    {
        var en = new ExpectationText("en", PlantSettings.Default);
        Assert.Null(en.AppliesTo(RuleBuilder.Check("X-01").Build().AppliesTo));
        var applies = RuleBuilder.Check("X-01").Os("server").Builds(16299, 26100).Domain(DomainScope.Joined).ExcludeDc().Capability("asr", AbsentCapability.NotApplicable)
            .When("office.installed", "eq", true).Build().AppliesTo;
        Assert.Equal("servers only; build 16299 or later; up to build 26100; domain-joined machines only; not on domain controllers; " +
            "where the operating system has asr; office.installed eq true", en.AppliesTo(applies));
        Assert.Equal("nur Clients; nur Arbeitsgruppenrechner", new ExpectationText("de", PlantSettings.Default)
            .AppliesTo(RuleBuilder.Check("X-01").Os("client").Domain(DomainScope.Standalone).Build().AppliesTo));
    }
}
