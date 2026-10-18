// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Evaluation;

/// <summary>
/// Die Sperrtabelle nach defender.mode aus spec 2.5, Zeile für Zeile, für eine Defender-Einstellung (wie DEF-01),
/// eine einzelne ASR-Regel (wie ASR-xx, Fähigkeit fehlt → n/a) und „ASR konfiguriert“ (wie DEF-09, Fähigkeit
/// fehlt → fail).
/// </summary>
public sealed class DefenderGatingTests
{
    private const string Ns = @"root\Microsoft\Windows\Defender";
    private const string AsrGuid = "00000000-0000-4000-8000-0000000000a1";

    /// <summary>Rechner je Modus; ASR-Regel im Block-Modus konfiguriert, Echtzeitschutz wie der Modus.</summary>
    private static Snapshot Machine(string mode, bool? thirdParty)
    {
        var b = SnapshotBuilder.Windows11Workgroup().CollectedAt(FixedClock.Default);
        switch (mode)
        {
            case "active":
                b.WmiRows(Ns, "MSFT_MpComputerStatus", new { AMRunningMode = "Normal", AntivirusEnabled = true, RealTimeProtectionEnabled = true });
                break;
            case "passive":
                b.WmiRows(Ns, "MSFT_MpComputerStatus", new { AMRunningMode = "Passive", AntivirusEnabled = false, RealTimeProtectionEnabled = false });
                break;
            case "edrBlock":
                b.WmiRows(Ns, "MSFT_MpComputerStatus", new { AMRunningMode = "EDR Block Mode", AntivirusEnabled = false, RealTimeProtectionEnabled = false });
                break;
            case "off":
                b.WmiRows(Ns, "MSFT_MpComputerStatus", new { AMServiceEnabled = true, AntivirusEnabled = false, RealTimeProtectionEnabled = false });
                break;
            case "absent":
                b.NamespaceMissing(ItemKeys.Wmi(Ns, "MSFT_MpComputerStatus")).NamespaceMissing(ItemKeys.Wmi(Ns, "MSFT_MpPreference"));
                break;
            default:
                b.Denied(ItemKeys.Wmi(Ns, "MSFT_MpComputerStatus"));
                break;
        }
        if (mode != "absent")
            b.WmiRows(Ns, "MSFT_MpPreference", new { AttackSurfaceReductionRules_Ids = new[] { AsrGuid }, AttackSurfaceReductionRules_Actions = new[] { 1 } });
        var products = new List<object> { new { name = "Windows Defender", state = mode == "active" ? "on" : "off", signature = "upToDate" } };
        if (thirdParty == true) products.Add(new { name = "Beispiel-Virenschutz (fiktiv)", state = "on", signature = "upToDate" });
        if (thirdParty is null) b.NotCollected(ItemKeys.WscAntivirus).NamespaceMissing(ItemKeys.Wmi(@"root\SecurityCenter2", "AntivirusProduct"));
        else b.Api(ItemKeys.WscAntivirus, products);
        return b.Build();
    }

    // Modus, Drittanbieter (null = unbekannt), DEF-01, ASR-xx, DEF-09
    public static TheoryData<string, bool?, ResultStatus, ResultStatus, ResultStatus> Table => new()
    {
        { "active", false, ResultStatus.Pass, ResultStatus.Pass, ResultStatus.Pass },
        { "active", true, ResultStatus.Pass, ResultStatus.Pass, ResultStatus.Pass },
        { "passive", true, ResultStatus.NotApplicable, ResultStatus.NotApplicable, ResultStatus.NotApplicable },
        { "edrBlock", true, ResultStatus.NotApplicable, ResultStatus.NotApplicable, ResultStatus.NotApplicable },
        { "passive", false, ResultStatus.Fail, ResultStatus.Fail, ResultStatus.Fail },
        { "edrBlock", false, ResultStatus.Fail, ResultStatus.Fail, ResultStatus.Fail },
        { "off", false, ResultStatus.Fail, ResultStatus.Fail, ResultStatus.Fail },
        { "absent", true, ResultStatus.NotApplicable, ResultStatus.NotApplicable, ResultStatus.NotApplicable },
        { "absent", false, ResultStatus.Fail, ResultStatus.NotApplicable, ResultStatus.Fail },
        { "unknown", false, ResultStatus.NotCheckable, ResultStatus.NotCheckable, ResultStatus.NotCheckable },
        { "passive", null, ResultStatus.NotCheckable, ResultStatus.NotCheckable, ResultStatus.NotCheckable },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Sperrtabelle_spec_2_5(string mode, bool? thirdParty, ResultStatus setting, ResultStatus asr, ResultStatus asrConfigured)
    {
        var snapshot = Machine(mode, thirdParty);
        Assert.Equal(setting, EngineHarness.Eval(EngineHarness.TestRule("TDEF-01"), snapshot).Status);
        Assert.Equal(asr, EngineHarness.Eval(EngineHarness.TestRule("TDEF-03"), snapshot).Status);
        Assert.Equal(asrConfigured, EngineHarness.Eval(EngineHarness.TestRule("TDEF-05"), snapshot).Status);
    }

    [Fact]
    public void Gruende_und_Texte_der_Sperre()
    {
        var thirdParty = EngineHarness.Eval(EngineHarness.TestRule("TDEF-01"), Machine("passive", true), EngineHarness.Context(language: "de"));
        Assert.Equal("RGL-0246", thirdParty.ReasonCode);
        Assert.Contains("Drittanbieter-Virenschutz aktiv", thirdParty.Details!);
        var noAv = EngineHarness.Eval(EngineHarness.TestRule("TDEF-01"), Machine("passive", false), EngineHarness.Context(language: "de"));
        Assert.Contains(noAv.Details!, d => d.StartsWith("kein aktiver Virenschutz", StringComparison.Ordinal));
        var asr = EngineHarness.Eval(EngineHarness.TestRule("TDEF-03"), Machine("passive", false), EngineHarness.Context(language: "de"));
        Assert.Contains("ASR konfiguriert, aber nicht wirksam", asr.Details!);
        var derived = EngineHarness.Eval(EngineHarness.TestRule("TDEF-01"), Machine("off", false));
        Assert.True(derived.Flags.HasFlag(ResultFlags.Derived));
    }
}
