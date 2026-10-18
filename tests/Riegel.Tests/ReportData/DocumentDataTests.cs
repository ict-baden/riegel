// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Model.Reports;
using Riegel.Core.Plants;
using Riegel.Core.ReportBuilding;
using Riegel.Core.Runs;
using Riegel.Tests.Plants;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Plants.WorkflowKit;

// Namensraum ReportBuilding wie in Core: ein Namensraum „ReportData“ verdeckte den Typ ReportData (auch in fremden Tests)
namespace Riegel.Tests.ReportBuilding;

/// <summary>Übersicht (O6), Soll-Konfiguration (O13), Genehmigungsblatt (O12), Erhebungsprotokoll (O14) und Baseline-Abschnitte.</summary>
public sealed class DocumentDataTests
{
    private static readonly TestCatalogue Trend = new([Check("NET-01", severity: Severity.High), Check("FW-01", "firewall", Severity.High),
        Check("CRED-05", "credentials", Severity.High)])
    {
        Lifecycle = new LifecycleTable(SchemaIds.Lifecycle,
        [
            // erfundene Testdaten
            new LifecycleEntry("test-old", "Testsystem alt", "client", 17763, null, null, null, null, new DateOnly(2026, 1, 1), null, "https://example.invalid/test", Today, null, null),
            new LifecycleEntry("test-new", "Testsystem neu", "client", 26100, null, null, null, null, new DateOnly(2030, 1, 1), null, "https://example.invalid/test", Today, null, null),
        ]),
    };

    private static SnapshotBuilder Machine(string host, DateOnly evaluated, int openHigh, bool newOs = false)
    {
        var b = (newOs ? SnapshotBuilder.Windows11Workgroup(host) : SnapshotBuilder.Windows10Ltsc2019(host))
            .CollectedAt(new DateTimeOffset(evaluated.AddDays(-1).ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero));
        string[] rules = ["NET-01", "FW-01", "CRED-05"];
        for (int i = 0; i < rules.Length; i++) b = b.Script(rules[i], i < openHigh ? "fail|0" : "pass|1");
        return b;
    }

    [Fact]
    public void Trend_offene_hohe_Befunde_an_den_letzten_vier_Monatsenden()
    {
        using var a = new ReportRig(TempPlant.Default() with { Id = "linie-1", Name = "Linie 1" }, Trend);
        a.Add(Machine("HMI-TEST", new(2026, 7, 15), 2), new(2026, 7, 15));
        a.Add(Machine("HMI-TEST", new(2026, 8, 20), 1), new(2026, 8, 20));
        a.Add(Machine("HMI-TEST", new(2026, 10, 10), 0), new(2026, 10, 10));
        a.Add(Machine("W11-TEST", new(2026, 8, 5), 1), new(2026, 8, 5));
        a.Add(Machine("W11-TEST", new(2026, 9, 29), 3), new(2026, 9, 29));
        // nach dem Stichtag ausgewertet: zählt nicht
        a.Add(Machine("W11-TEST", new(2026, 10, 20), 0), new(2026, 10, 20));
        using var b = new ReportRig(TempPlant.Default() with { Id = "labor", Name = "Labor" }, Trend);
        b.Add(Machine("LAB-01", new(2026, 10, 1), 1, newOs: true), new(2026, 10, 1));

        var summary = a.Builder.BuildSummary([a.Plant.Plant, b.Plant.Plant], Today, "de");
        Assert.Equal(Today, summary.Date);
        var line1 = summary.Plants[0];
        Assert.Equal(("linie-1", 2, 3), (line1.PlantId, line1.Machines, line1.OpenHigh));
        Assert.Equal([new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30)], line1.Trend.Select(t => t.MonthEnd));
        Assert.Equal([null, 2, 2, 4], line1.Trend.Select(t => t.OpenHigh));
        Assert.Equal(0, line1.SupportedOsPercent);
        var lab = summary.Plants[1];
        Assert.Equal((1, 100), (lab.OpenHigh, lab.SupportedOsPercent));
        Assert.Equal([null, null, null, null], lab.Trend.Select(t => t.OpenHigh));

        // Top-Befunde über beide Anlagen
        var top = summary.TopFindings;
        Assert.Equal("NET-01", top[0].RuleId);
        Assert.Equal(2, top[0].OpenCount);
        Assert.All(top, f => Assert.Equal(Severity.High, f.Severity));
        Assert.True(top.Count <= 5);
        Assert.Equal("Vertraulich – enthält Sicherheitskonfiguration", summary.Confidentiality);
    }

    [Theory]
    [InlineData("2026-10-18", "2026-06-30,2026-07-31,2026-08-31,2026-09-30")]
    [InlineData("2026-09-30", "2026-06-30,2026-07-31,2026-08-31,2026-09-30")]
    [InlineData("2026-03-01", "2025-11-30,2025-12-31,2026-01-31,2026-02-28")]
    [InlineData("2028-03-15", "2027-11-30,2027-12-31,2028-01-31,2028-02-29")]
    public void Monatsenden(string date, string expected) =>
        Assert.Equal(expected, string.Join(",", ReportDataBuilder.MonthEnds(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), 4)
            .Select(FileNames.Date)));

    [Fact]
    public void Soll_Konfiguration_je_Gruppe_ohne_zugeschnittene_Regeln()
    {
        using var rig = new ReportRig();
        rig.Plant.WriteRegisters(Set(tailoring: [Tailoring("TL-0001", "AUD-01", Groups("g-hmi")).Approve(), Tailoring("TL-0002", "NET-01", Machines("SRV-01")).Approve()],
            plantId: "musteranlage"));
        var data = rig.Builder.BuildTargetConfig(rig.Plant.Plant, null, "en");
        Assert.Equal(["g-hmi", "g-srv"], data.Groups.Select(g => g.GroupId));
        var hmi = data.Groups[0];
        Assert.Equal(12, hmi.Hosts.Count);
        Assert.DoesNotContain(hmi.Rules, r => r.RuleId == "AUD-01");
        Assert.DoesNotContain(hmi.Rules, r => r.RuleId == "NET-30");
        Assert.Equal("TL-0001", Assert.Single(hmi.Tailoring).Entry.Id);
        var srv = data.Groups[1];
        Assert.Contains(srv.Rules, r => r.RuleId == "AUD-01");
        Assert.Contains(srv.Rules, r => r.RuleId == "NET-01"); // Zuschnitt nur für einen Rechner: Regel bleibt in der Gruppe
        Assert.Equal("TL-0002", Assert.Single(srv.Tailoring).Entry.Id);
        var net08 = hmi.Rules.Single(r => r.RuleId == "NET-08");
        Assert.Equal(@"HKLM\SOFTWARE\Policies\Beispiel\NET-08 = 1", net08.Expectation);
        Assert.Equal(["ACSC"], net08.Sources);
        Assert.False(net08.AlsoInMsBaseline);
        Assert.Null(net08.AppliesTo);
        Assert.Equal(RuleCategories.All.Where(c => hmi.Rules.Any(r => r.Category == c)), hmi.Rules.Select(r => r.Category).Distinct());
        Assert.Equal(new ProvenanceCounts(6, 6, 0, 0, 0, 0), data.Provenance);
        Assert.Equal(rig.Services.Registers.Load(rig.Plant.Plant).Hashes.Tailoring, data.TailoringSha256);

        var one = rig.Builder.BuildTargetConfig(rig.Plant.Plant, "g-srv", "de");
        Assert.Equal("g-srv", Assert.Single(one.Groups).GroupId);
        Assert.Equal(RglCodesCommon.NotFound, Assert.Throws<RiegelException>(() => rig.Builder.BuildTargetConfig(rig.Plant.Plant, "g-gibt-es-nicht", "de")).Code);

        // Rechner ohne Gruppe (auch nur über Läufe bekannt) bekommen eine eigene Gruppe
        rig.Add(SnapshotBuilder.DomainController("DC-01"));
        var withDc = rig.Builder.BuildTargetConfig(rig.Plant.Plant, null, "de");
        var none = withDc.Groups[^1];
        Assert.Null(none.GroupId);
        Assert.Equal("Ohne Gruppe", none.Name);
        Assert.Equal(["DC-01"], none.Hosts);
    }

    [Fact]
    public void Genehmigungsblatt_mit_beantragten_und_geaenderten_Eintraegen()
    {
        using var rig = new ReportRig();
        var changed = Exception("EX-0002", "NET-08", All).Approve() is var approved
            ? approved with { Justification = approved.Justification + " Ergänzt." } : null!;
        rig.Plant.WriteRegisters(Set(
            [
                Exception("EX-0001", "NET-01", Groups("g-hmi"), category: ReasonCategory.RiskAccepted), changed,
                Exception("EX-0003", "PS-02", All).Approve(),
            ],
            [Tailoring("TL-0001", "AUD-01", Groups("g-hmi"))],
            [Measure("M-0001", "NET-01", Machines("HMI-01"), Today.AddDays(10))], plantId: "musteranlage"));
        var sheet = rig.Builder.BuildApprovalSheet(rig.Plant.Plant, null, "de");
        Assert.Equal(["EX-0001", "EX-0002", "TL-0001"], sheet.Entries.Select(e => e.Id));
        Assert.Equal([RegisterEntryState.Pending, RegisterEntryState.ChangedAfterApproval, RegisterEntryState.Pending], sheet.Entries.Select(e => e.State));
        var first = sheet.Entries[0];
        Assert.Equal((RegisterKind.Exceptions, "Testregel NET-01", Severity.High), (first.Kind, first.RuleTitle, first.Severity));
        Assert.Equal("M-0001: Einstellung im Wartungsfenster korrigieren (Anlagenverantwortlicher, fällig 2026-10-28)", first.Measure);
        Assert.Equal(ContentHashes.Of(rig.Services.Registers.Load(rig.Plant.Plant).Exceptions.Exceptions[0]), first.ContentSha256);
        Assert.Equal(RegisterKind.Tailoring, sheet.Entries[2].Kind);
        Assert.Matches(@"^FB-20261018-[0-9a-f]{6}$", sheet.SheetId);
        Assert.Equal(sheet.SheetId, rig.Builder.BuildApprovalSheet(rig.Plant.Plant, null, "de").SheetId);
        var hashes = rig.Services.Registers.Load(rig.Plant.Plant).Hashes;
        Assert.Equal((hashes.Exceptions, hashes.Tailoring), (sheet.ExceptionsSha256, sheet.TailoringSha256));
        // Risikoübernahme einer hohen Regel: die berechtigte Rolle unterschreibt mit
        Assert.Contains(sheet.Approvers, a => a.Role == "Geschäftsführung");
        Assert.All(sheet.Approvers, a => Assert.Equal(SignOffSteps.Approved, a.Step));

        var selected = rig.Builder.BuildApprovalSheet(rig.Plant.Plant, ["ex-0003"], "de");
        Assert.Equal(RegisterEntryState.Valid, Assert.Single(selected.Entries).State);
        Assert.Equal(RglCodesWorkflow.EntryNotFound,
            Assert.Throws<RiegelException>(() => rig.Builder.BuildApprovalSheet(rig.Plant.Plant, ["EX-0099"], "de")).Code);
    }

    [Fact]
    public void Erhebungsprotokoll_zum_Import()
    {
        using var plant = new TempPlant();
        using var export = new TempFolder();
        var services = Services();
        var record = services.Plants.Import(plant.Plant, [SnapshotBuilder.Windows11Workgroup("W11-TEST").WriteTo(export.Path)],
            new ImportOptions("Servicetechniker", "Anlagenverantwortlicher", "Betreiber"));
        var builder = new ReportDataBuilder(services);
        var latest = builder.BuildCollectionRecord(plant.Plant, "latest", "fr");
        Assert.Equal(record.Id, latest.Import.Id);
        Assert.True(latest.PreTranslated);
        Assert.Equal(("Servicetechniker", "Anlagenverantwortlicher"), (latest.Import.CollectedByRole, latest.Import.WitnessRole));
        Assert.Equal(record.Id, builder.BuildCollectionRecord(plant.Plant, record.Id, "de").Import.Id);
        Assert.Equal(RglCodesWorkflow.InvalidImportId, Assert.Throws<RiegelException>(() => builder.BuildCollectionRecord(plant.Plant, "../x", "de")).Code);
        Assert.Equal(RglCodesCommon.NotFound,
            Assert.Throws<RiegelException>(() => builder.BuildCollectionRecord(plant.Plant, "20200101T000000Z-abcdef", "de")).Code);

        // Bericht: Import-ID am Rechner
        var report = builder.Build(new ReportRequest(plant.Plant, null, Today, "de", ReportPreset.Audit, false, true, [], false, false, false, export.Path));
        Assert.Equal(record.Id, Assert.Single(report.Machines).Collection.ImportId);
    }

    [Fact]
    public void Kunden_Baseline_hat_einen_eigenen_Abschnitt()
    {
        using var plant = new TempPlant();
        var baseline = new BaselineFile(SchemaIds.Baseline, "kunde", "Kundenvorgaben (fiktiv)", null, "kunde.csv", new string('6', 64),
            new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero),
            [new BaselineRule("R1", "OPC UA nur Port 4840", Severity.High, "network",
                new RegistryCheck(@"HKLM\SOFTWARE\Policies\Beispiel", "Port", RegValueType.Dword, null, CheckOperator.Eq, JsonValues.Number(4840), WhenMissing.Fail, null, null, null),
                null, null)]);
        RiegelJson.WriteFile(plant.PathOf("baselines/kunde.json"), baseline);
        var services = Services();
        var run = new RunService(services, new ScriptedEvaluator()).EvaluateAndStore(plant.Plant,
            plant.AddSnapshot(SnapshotBuilder.Windows11Workgroup("W11-TEST").Script("IMP-kunde-R1", "fail|0").Build()), new RunOptions());
        var reference = Assert.Single(run.Baselines);
        Assert.Equal(("kunde", "Kundenvorgaben (fiktiv)", new string('6', 64)), (reference.Id, reference.Name, reference.SourceSha256));
        Assert.Equal(1, run.Summary.Fail);
        Assert.Equal(1, run.Summary.OpenHigh);
        Assert.Null(run.Summary.Score); // eigener Katalog ohne Regeln: kein Härtungsgrad aus Kundenregeln

        var data = new ReportDataBuilder(services).Build(new ReportRequest(plant.Plant, null, Today, "de", ReportPreset.Audit, false, true, [], false, false, false, plant.Folder));
        var section = Assert.Single(data.Baselines);
        Assert.Equal((0, 1, 0, 0, false), (section.Pass, section.Fail, section.Exception, section.NotCheckable, section.ContentIncluded));
        Assert.Equal(0, section.Score);
        Assert.Equal("kunde", Assert.Single(data.Header.Baselines).Id);
        Assert.Equal("IMP-kunde-R1", Assert.Single(data.Findings).RuleId);
    }
}
