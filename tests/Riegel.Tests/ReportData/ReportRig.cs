// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.ReportBuilding;
using Riegel.Core.Runs;
using Riegel.Core.Services;
using Riegel.Tests.Plants;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Plants.WorkflowKit;

// Namensraum ReportBuilding wie in Core: ein Namensraum „ReportData“ verdeckte den Typ ReportData (auch in fremden Tests)
namespace Riegel.Tests.ReportBuilding;

/// <summary>
/// Beispielanlage für die Berichtsdaten: zwölf Bedienstationen, ein Server mit ESU, ein Domänencontroller; Läufe über
/// die geskriptete Auswertung, Register mit jedem Zustand. Alle Namen und Daten sind erfunden.
/// </summary>
internal sealed class ReportRig : IDisposable
{
    public static readonly Plant Definition = new(SchemaIds.Plant, "musteranlage", "Musteranlage", null,
        [new MachineGroup("g-hmi", "Bedienstationen", MachineRole.Hmi), new MachineGroup("g-srv", "Server", MachineRole.Server)],
        [
            .. Enumerable.Range(1, 12).Select(i => new PlantMachine($"HMI-{i:00}", "g-hmi", $"Bedienstation {i}", false, null, null)),
            new PlantMachine("SRV-01", "g-srv", "Betriebsserver", true, null, null),
        ],
        PlantSettings.Default with { RiskAcceptanceRoles = ["Geschäftsführung"] });

    public static readonly TestCatalogue Catalogue = new(
    [
        Check("NET-01", severity: Severity.High), Check("NET-08"), Check("PS-02", "powershell", Severity.Low), Check("ENC-01", "encryption"),
        Check("FW-01", "firewall", Severity.High), Check("AUD-01", "audit"), WorkflowKit.Inventory("ACCT-15"),
        RuleBuilder.Check("NET-30", "network").Retired().Build(),
    ], releasedOn: new DateOnly(2026, 3, 1))
    {
        Lifecycle = new LifecycleTable(SchemaIds.Lifecycle,
        [
            // erfundene Testdaten, keine Lebenszyklus-Angaben von Microsoft
            new LifecycleEntry("test-17763", "Testsystem Client", "client", 17763, ["EnterpriseS"], null, "ltsc", null, new DateOnly(2030, 6, 30), null,
                "https://example.invalid/test", Today, null, null),
            new LifecycleEntry("test-20348", "Testsystem Server", "server", 20348, null, null, "server", null, new DateOnly(2026, 1, 1),
                new DateOnly(2027, 1, 1), "https://example.invalid/test", Today, null, null),
        ]),
    };

    public ReportRig(Plant? plant = null, TestCatalogue? catalogue = null)
    {
        Plant = new TempPlant(plant ?? Definition);
        Clock = new FixedClock();
        Services = WorkflowKit.Services(Clock);
        Runs = new RunService(Services, new ScriptedEvaluator());
        Cat = catalogue ?? Catalogue;
    }

    public TempPlant Plant { get; }
    public FixedClock Clock { get; }
    public RiegelServices Services { get; }
    public RunService Runs { get; }
    public ICatalogue Cat { get; }
    public ReportDataBuilder Builder => new(Services, Cat);

    public Run Add(SnapshotBuilder snapshot, DateOnly? evaluationDate = null) =>
        Runs.EvaluateAndStore(Plant.Plant, Plant.AddSnapshot(snapshot.Build()), new RunOptions(Catalogue: Cat, EvaluationDate: evaluationDate));

    public Core.Model.Reports.ReportData Build(string language = "de", ReportPreset preset = ReportPreset.Audit, DateOnly? date = null,
        IReadOnlyList<string>? hosts = null) =>
        Builder.Build(new ReportRequest(Plant.Plant, hosts, date ?? Today, language, preset, false, true, [ReportFormat.Pdf, ReportFormat.Xlsx],
            false, false, false, Plant.PathOf("reports")));

    /// <summary>Das ganze Szenario: Register, Vorläufe, zwölf Bedienstationen, Server, Domänencontroller.</summary>
    public ReportRig Scenario()
    {
        Plant.WriteRegisters(Set(
        [
            Exception("EX-0001", "PS-02", Groups("g-hmi"), accepted: "*").Approve(),
            Exception("EX-0002", "NET-08", All, accepted: "*").Approve(),
            Exception("EX-0003", "FW-01", Machines("HMI-01"), accepted: "*").Approve(),
            Exception("EX-0004", "NET-30", All, accepted: "*").Approve(),
            Exception("EX-0005", "ENC-01", All, accepted: "*").Approve(Today.AddMonths(-13), Today.AddDays(-1)),
            Exception("EX-0006", "NET-01", Machines("HMI-03"), accepted: "*"),
        ],
        [Tailoring("TL-0001", "AUD-01", Groups("g-hmi")).Approve(), Tailoring("TL-0002", "NET-01", Machines("SRV-01"))],
        [
            Measure("M-0001", "ENC-01", Machines("HMI-01"), Today.AddDays(-1)),
            Measure("M-0002", "NET-01", Machines("HMI-02"), Today.AddDays(-10), MeasureState.Done),
            Measure("M-0003", "NET-08", All, Today.AddDays(40)),
        ], plantId: "musteranlage"));

        // Vorlauf HMI-01 (NET-01 noch erfüllt) und freigegebener Stand HMI-02
        Add(Hmi(1, new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero), net01: "pass|1"));
        var approved = Add(Hmi(2, new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero), net01: "pass|1"));
        Services.Plants.SaveApproved(Plant.Plant, new ApprovedState(SchemaIds.Approved, "HMI-02", approved.Id, "", "", "", RegisterHashes.Empty,
            new Approval("Qualitätssicherung", null, Today.AddDays(-20), null, "SAT-1", null)));
        Clock.Advance(TimeSpan.FromMinutes(1));

        for (int i = 1; i <= 12; i++)
            Add(Hmi(i, i == 12 ? new DateTimeOffset(2026, 9, 8, 8, 0, 0, TimeSpan.Zero) : new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero)));
        Add(SnapshotBuilder.Server2022Member("SRV-01").CollectedAt(new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero))
            .Script("NET-01", "pass|1").Script("NET-08", "fail|0||domainGpo").Script("PS-02", "pass|1").Script("ENC-01", "pass|1").Script("FW-01", "pass|1")
            .Script("ACCT-15", "info")
            .Api(ItemKeys.LocalAdministrators, new[] { new { sid = "S-1-5-32-544-a", kind = "user" }, new { sid = "S-1-5-32-544-b", kind = "group" } })
            .Api(ItemKeys.Firewall, new { profiles = new { domain = new { enabled = true }, @private = new { enabled = true }, @public = new { enabled = true } } })
            .RegistryValue(@"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", RegValueType.Dword, 0)
            .WmiMethod(@"root\Microsoft\Windows\SMB", "MSFT_SmbServerConfiguration", "GetConfiguration", new { EnableSMB1Protocol = false })
            .WmiRows(@"root\SecurityCenter2", "AntivirusProduct", new { displayName = "Beispiel-Virenschutz (fiktiv)", productState = 266240 }));
        Add(SnapshotBuilder.DomainController("DC-01").CollectedAt(new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero))
            .Script("NET-01", "pass|1").Script("NET-08", "pass|1").Script("PS-02", "pass|1").Script("ENC-01", "pass|1").Script("FW-01", "pass|1")
            .Script("AUD-01", "notCheckable||clockSuspect"));
        return this;
    }

    public static SnapshotBuilder Hmi(int i, DateTimeOffset collected, string net01 = "fail|0") =>
        SnapshotBuilder.Windows10Ltsc2019($"HMI-{i:00}").CollectedAt(collected)
            .Script("NET-01", net01).Script("NET-08", "pass|1").Script("PS-02", "fail|0").Script("ENC-01", "fail|0").Script("FW-01", "pass|1")
            .Script("AUD-01", "fail|0").Script("ACCT-15", "info");

    public void Dispose() => Plant.Dispose();
}
