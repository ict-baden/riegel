// SPDX-License-Identifier: MIT
using Riegel.Core.Comparison;
using Riegel.Core.Model;
using Riegel.Core.Runs;
using Riegel.Tests.Plants;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Plants.WorkflowKit;

namespace Riegel.Tests.Comparison;

/// <summary>Statusänderungen, Rohwert-Abweichungen ohne flüchtige Liste und Stichprobe (spec 2.14).</summary>
public sealed class ComparisonTests
{
    private const string LanmanServer = @"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters";
    private const string Services = @"root\cimv2";
    private const string Defender = @"root\Microsoft\Windows\Defender";

    private static Run RunOf(params RuleResult[] results) =>
        new RunService(WorkflowKit.Services(), new ScriptedEvaluator())
            .EvaluateOnly(SnapshotBuilder.Windows11Workgroup().Build(), new RunOptions(Catalogue: new TestCatalogue([]))) with { Results = results };

    private static StatusChange Change(RuleResult from, RuleResult to) =>
        Assert.Single(new RunComparer().Compare(RunOf(from), null, RunOf(to), SnapshotBuilder.Windows11Workgroup().Build(), ComparisonBasis.Previous).StatusChanges);

    [Fact]
    public void Arten_der_Statusaenderung()
    {
        RuleResult R(ResultStatus s, ResultFlags f = ResultFlags.None) => Result("NET-08", s, flags: f) with { ExceptionId = s == ResultStatus.Exception || (f & (ResultFlags.ExceptionExpired | ResultFlags.ExceptionPending)) != 0 ? "EX-0001" : null, TailoringId = s == ResultStatus.OutOfScope ? "TL-0001" : null };
        Assert.Equal(StatusChangeKind.Worse, Change(R(ResultStatus.Pass), R(ResultStatus.Fail)).Kind);
        Assert.Equal(StatusChangeKind.Worse, Change(R(ResultStatus.NotApplicable), R(ResultStatus.Fail)).Kind);
        Assert.Equal(StatusChangeKind.Better, Change(R(ResultStatus.Fail), R(ResultStatus.Pass)).Kind);
        Assert.Equal(StatusChangeKind.ExceptionAdded, Change(R(ResultStatus.Fail), R(ResultStatus.Exception)).Kind);
        var expired = Change(R(ResultStatus.Exception), R(ResultStatus.Fail, ResultFlags.ExceptionExpired));
        Assert.Equal(StatusChangeKind.ExceptionExpired, expired.Kind);
        Assert.Equal("EX-0001", expired.Detail);
        Assert.Equal(StatusChangeKind.ExceptionRemoved, Change(R(ResultStatus.Exception), R(ResultStatus.Fail, ResultFlags.ExceptionPending)).Kind);
        Assert.Equal(StatusChangeKind.Worse, Change(R(ResultStatus.Exception), R(ResultStatus.Fail, ResultFlags.ExceptionValueMismatch)).Kind);
        Assert.Equal(StatusChangeKind.TailoringAdded, Change(R(ResultStatus.Fail), R(ResultStatus.OutOfScope)).Kind);
        Assert.Equal(StatusChangeKind.TailoringRemoved, Change(R(ResultStatus.OutOfScope), R(ResultStatus.Fail)).Kind);
        Assert.Equal(StatusChangeKind.Other, Change(R(ResultStatus.NotCheckable), R(ResultStatus.Fail)).Kind);

        var noChange = new RunComparer().Compare(RunOf(R(ResultStatus.Pass)), null, RunOf(R(ResultStatus.Pass)), SnapshotBuilder.Windows11Workgroup().Build(),
            ComparisonBasis.Previous);
        Assert.Empty(noChange.StatusChanges);
        var first = new RunComparer().Compare(null, null, RunOf(R(ResultStatus.Fail)), SnapshotBuilder.Windows11Workgroup().Build(), ComparisonBasis.Previous);
        Assert.Empty(first.StatusChanges);
        Assert.Null(first.FromRunId);
    }

    [Fact]
    public void Neue_Regel_und_Pruefumfang_geaendert()
    {
        var before = RunOf(Result("NET-08", ResultStatus.Pass));
        var after = RunOf(Result("NET-08", ResultStatus.NotCheckable), Result("NET-09", ResultStatus.Fail)) with { IsAdmin = false };
        var changes = new RunComparer().Compare(before, null, after, SnapshotBuilder.Windows11Workgroup().Build(), ComparisonBasis.Approved).StatusChanges;
        Assert.Equal(StatusChangeKind.ScopeChanged, changes.Single(c => c.RuleId == "NET-08").Kind);
        var added = changes.Single(c => c.RuleId == "NET-09");
        Assert.Equal(StatusChangeKind.NewRule, added.Kind);
        Assert.Null(added.From);
        var minimal = RunOf(Result("NET-08", ResultStatus.NotCheckable)) with { Options = new CollectionOptions(true, false, false) };
        Assert.Equal(StatusChangeKind.ScopeChanged, new RunComparer().Compare(before, null, minimal, SnapshotBuilder.Windows11Workgroup().Build(),
            ComparisonBasis.Previous).StatusChanges.Single().Kind);
    }

    [Fact]
    public void Rohwerte_Registry_Dienste_Administratoren_und_Status()
    {
        var before = SnapshotBuilder.Windows11Workgroup()
            .RegistryValue(LanmanServer, "SMB1", RegValueType.Dword, 0)
            .RegistryValue(LanmanServer, "RequireSecuritySignature", RegValueType.Dword, 1)
            .RegistryValue(LanmanServer, "Alt", RegValueType.Sz, "x")
            .WmiRows(Services, "Win32_Service", new { Name = "Spooler", StartMode = "Disabled", State = "Stopped" }, new { Name = "WinRM", StartMode = "Manual", State = "Stopped" })
            .Api(ItemKeys.LocalAdministrators, new[] { new { sid = "S-1-5-21-1-2-3-500", name = "Administrator", kind = "user", domain = "W11" } })
            .Api(ItemKeys.NetUserModals, new { minPwLen = 12, lockoutThreshold = 10 })
            .Build();
        var after = SnapshotBuilder.Windows11Workgroup()
            .RegistryValue(LanmanServer, "SMB1", RegValueType.Dword, 1)
            .RegistryValue(LanmanServer, "RequireSecuritySignature", RegValueType.Sz, "1")
            .RegistryValue(LanmanServer, "Neu", RegValueType.Dword, 7)
            .WmiRows(Services, "Win32_Service", new { Name = "WinRM", StartMode = "Manual", State = "Stopped" }, new { Name = "Spooler", StartMode = "Auto", State = "Running" })
            .Api(ItemKeys.LocalAdministrators, new[]
            {
                new { sid = "S-1-5-21-1-2-3-500", name = "Administrator", kind = "user", domain = "W11" },
                new { sid = "S-1-5-21-1-2-3-1003", name = "service", kind = "user", domain = "W11" },
            })
            .Denied(ItemKeys.NetUserModals)
            .Build();
        var drift = new RunComparer().Compare(RunOf(), before, RunOf(), after, ComparisonBasis.Previous).Drift;
        string Line(ValueDrift d) => $"{d.Kind}:{d.Path}:{d.From}->{d.To}";
        var byKey = drift.ToLookup(d => d.Key, StringComparer.OrdinalIgnoreCase);
        var lanman = byKey["reg:" + LanmanServer].Select(Line).ToList();
        Assert.Contains("Changed:SMB1:0->1", lanman);
        Assert.Contains("Changed:RequireSecuritySignature:1 (dword)->1 (sz)", lanman);
        Assert.Contains("Added:Neu:->7", lanman);
        Assert.Contains("Removed:Alt:x->", lanman);
        var services = byKey[ItemKeys.Wmi(Services, "Win32_Service")].Select(Line).ToList();
        Assert.Equal(["Changed:Spooler/StartMode:Disabled->Auto", "Changed:Spooler/State:Stopped->Running"], services);
        Assert.Equal(["Added:S-1-5-21-1-2-3-1003:->S-1-5-21-1-2-3-1003"], byKey[ItemKeys.LocalAdministrators].Select(Line));
        Assert.Equal(["Changed:#status:ok->denied"], byKey[ItemKeys.NetUserModals].Select(Line));
        Assert.Equal(drift.OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase).Select(d => d.Key), drift.Select(d => d.Key));
    }

    [Fact]
    public void Fluechtige_Werte_und_Reihenfolgen_zaehlen_nicht()
    {
        SnapshotBuilder Machine(int signatureAge, string lastUpdated, string product, string boot, int passwordAge, bool reversed) =>
            SnapshotBuilder.Windows11Workgroup()
                .Header(m => m with { LastBootUtc = DateTimeOffset.Parse(boot, System.Globalization.CultureInfo.InvariantCulture) })
                .WmiRows(Defender, "MSFT_MpComputerStatus", new { AMRunningMode = "Normal", RealTimeProtectionEnabled = true, AntivirusSignatureAge = signatureAge,
                    AntivirusSignatureLastUpdated = lastUpdated, AMProductVersion = product })
                .WmiRows(Services, "Win32_OperatingSystem", new { Caption = "Microsoft Windows 11 Pro", LastBootUpTime = boot })
                .Api(ItemKeys.NetUserEnum, new[] { new { name = "operator", sid = "S-1-5-21-1-2-3-1001", disabled = false, lastLogonDate = lastUpdated[..10], passwordAgeDays = passwordAge } })
                .Api(ItemKeys.WindowsUpdateHistory, new { serviceWasRunning = true, entries = new[] { new { dateUtc = lastUpdated, resultCode = 2 } } })
                .Api(ItemKeys.LsaRights, new { SeBackupPrivilege = reversed ? new[] { "S-1-5-32-551", "S-1-5-32-544" } : new[] { "S-1-5-32-544", "S-1-5-32-551" } })
                .RegistryValue(@"HKLM\SOFTWARE\Microsoft\Windows Defender\Signature Updates", "AVSignatureVersion", RegValueType.Sz, product)
                .Item("evt:system:wininit-12", ItemStatus.Ok, new { found = true, timeCreatedUtc = boot, bootTimeUtc = boot, eventData = new[] { "4" } }, durationMs: signatureAge);
        var monday = Machine(0, "2026-10-12T04:00:00Z", "4.18.1", "2026-10-12T06:30:00Z", 10, false).Build();
        var friday = Machine(3, "2026-10-16T04:00:00Z", "4.18.2", "2026-10-16T06:30:00Z", 14, true).Build();
        Assert.Empty(new RunComparer().Compare(RunOf(), monday, RunOf(), friday, ComparisonBasis.Previous).Drift);

        var sample = new RunComparer().CompareSnapshots(monday, friday);
        Assert.True(sample.Consistent);
        Assert.Empty(sample.Differences);
        Assert.Equal(monday.CollectedAtUtc, sample.PackedCollectedAtUtc);
    }

    [Fact]
    public void Stichprobe_meldet_Abweichungen_und_neu_aufgesetzten_Rechner()
    {
        var packed = SnapshotBuilder.Windows11Workgroup().RegistryValue(LanmanServer, "SMB1", RegValueType.Dword, 0).Build();
        var fresh = SnapshotBuilder.Windows11Workgroup()
            .Header(m => m with { MachineGuid = "22222222-0000-4000-8000-000000000001" })
            .RegistryValue(LanmanServer, "SMB1", RegValueType.Dword, 1).Build();
        var sample = new RunComparer().CompareSnapshots(packed, fresh);
        Assert.False(sample.Consistent);
        Assert.Contains(sample.Differences, d => d.Key == "machine" && d.Path == "machineGuid");
        Assert.Contains(sample.Differences, d => d.Path == "SMB1" && d.From == "0" && d.To == "1");
        Assert.NotEqual(sample.PackedSnapshotSha256, sample.FreshSnapshotSha256);

        var comparison = new RunComparer().Compare(RunOf(), packed, RunOf(), fresh, ComparisonBasis.Previous);
        Assert.True(comparison.MachineIdentityChanged);
        Assert.False(new RunComparer().Compare(RunOf(), packed, RunOf(), packed, ComparisonBasis.Previous).MachineIdentityChanged);
    }

    [Fact]
    public void Eintraege_die_fehlen_oder_neu_sind_ergeben_eine_Zeile()
    {
        var before = SnapshotBuilder.Windows11Workgroup().Api(ItemKeys.WscAntivirus, new[] { new { name = "Windows Defender", state = "on" } }).Build();
        var after = SnapshotBuilder.Windows11Workgroup().Without(ItemKeys.WscAntivirus)
            .Api(ItemKeys.WinHttpProxy, new { accessType = 1, proxy = (string?)null }).Build();
        var drift = new RunComparer().Compare(RunOf(), before, RunOf(), after, ComparisonBasis.Previous).Drift;
        Assert.Contains(drift, d => d.Key == ItemKeys.WscAntivirus && d.Path is null && d.Kind == DriftKind.Removed && d.From == "ok");
        Assert.Contains(drift, d => d.Key == ItemKeys.WinHttpProxy && d.Path is null && d.Kind == DriftKind.Added);
        Assert.Equal(2, drift.Count);
        Assert.Contains("api:windows-update-history", VolatileList.Items);
    }
}
