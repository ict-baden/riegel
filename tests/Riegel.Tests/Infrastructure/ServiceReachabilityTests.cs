// SPDX-License-Identifier: MIT
using Riegel.Commands;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Model.Reports;
using Riegel.Core.Samples;
using Riegel.Core.Services;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Infrastructure;

/// <summary>
/// Jeder Dienst aus <see cref="RiegelServices.CreateDefault"/> ist da und aufrufbar (spec 7.2). „Aufrufbar“ heißt:
/// ein Ergebnis oder ein <see cref="RiegelException"/> mit RGL-Code – nie eine andere Ausnahme. So bleibt der Test
/// gültig, wenn die Pakete die Platzhalter ersetzen.
/// </summary>
public sealed class ServiceReachabilityTests
{
    private static RiegelServices Services() => RiegelServices.Create(new NullPlatform(), new FixedClock());

    private static void Callable(Action action, string what)
    {
        try { action(); }
        catch (RiegelException e) { Assert.Matches("^RGL-\\d{4}$", e.Code); }
        catch (Exception e) { Assert.Fail($"{what}: {e.GetType().Name}: {e.Message}"); }
    }

    [Fact]
    public void Alle_Dienste_sind_vorhanden()
    {
        var s = RiegelServices.CreateDefault(new NullPlatform());
        object[] all = [s.Platform, s.Clock, s.Catalogue, s.Facts, s.Evaluator, s.Snapshots, s.Attribution, s.Baselines, s.Plants, s.Registers,
                        s.Comparison, s.Scoring, s.Runs, s.ReportData, s.Pdf, s.Tabular, s.Evidence, s.Remediation];
        Assert.All(all, Assert.NotNull);
        Assert.False(s.Platform.CanCollect);
    }

    [Fact]
    public void Katalog_Fakten_Auswertung_und_Leser_sind_aufrufbar()
    {
        var s = Services();
        var catalogue = s.Catalogue.LoadEmbedded();
        Assert.Equal("riegel", catalogue.Id);
        Assert.Equal("2026.10", catalogue.Version);
        Assert.True(Core.Model.Hashes.IsSha256(catalogue.Sha256));
        var snapshot = s.Snapshots.Read(RepoPaths.Synthetic("win11-24h2-workgroup-admin"), ReadLimits.Default);
        Assert.NotNull(s.Facts.Derive(snapshot, catalogue));
        var context = new EvaluationContext(new DateOnly(2026, 10, 18), FixedClock.Default, null, 35, 90, false, "en");
        var results = s.Evaluator.Evaluate(snapshot, catalogue, context);
        Assert.Equal(catalogue.Rules.Count, results.Count);
        Assert.NotNull(s.Attribution.Build(snapshot));
        Assert.NotNull(s.Snapshots.VerifySidecar(RepoPaths.Synthetic("win11-24h2-workgroup-admin")));
        Callable(() => s.Snapshots.FromBuiltinBundle(Path.Combine(RepoPaths.SyntheticFixtures, "PG-ALT-01_20261010.riegel-builtin"), ReadLimits.Default), "FromBuiltinBundle");
        using var stream = new MemoryStream(RiegelJson.SerializeToUtf8Bytes(new CatalogueDocument(SchemaIds.Catalogue, catalogue.Id, catalogue.Version,
            catalogue.ReleasedOn, catalogue.Rules, catalogue.Lifecycle, catalogue.Builds, catalogue.Capabilities, catalogue.Data)));
        Callable(() => Assert.Equal(catalogue.Rules.Count, s.Catalogue.Load(stream, ReadLimits.Default).Rules.Count), "Catalogue.Load");
        Callable(() => s.Catalogue.Combine(catalogue, []), "Catalogue.Combine");
    }

    [Fact]
    public void Baseline_Import_ist_aufrufbar()
    {
        var s = Services();
        using var temp = new TempFolder();
        var file = Path.Combine(temp.Path, "kunde.baseline.json");
        var baseline = new BaselineFile(SchemaIds.Baseline, "kunde", "Kundenvorgaben (Test)", null, null, null, null,
            [new BaselineRule("R1", "OPC UA only", Severity.Medium, "network",
                new RegistryCheck(@"HKLM\SOFTWARE\Policies\Beispiel", "Port", RegValueType.Dword, null, CheckOperator.Eq, JsonValues.Number(4840), WhenMissing.Fail, null, null, null),
                null, null)]);
        RiegelJson.WriteFile(file, baseline);
        SchemaValidator.AssertValid(File.ReadAllText(file), "Baseline");
        Callable(() => Assert.NotEmpty(s.Baselines.Import(file, "Kundenvorgaben", ReadLimits.Default).Rules), "Baselines.Import");
    }

    [Fact]
    public void Anlagenordner_Register_Laeufe_und_Vergleich_sind_aufrufbar()
    {
        var s = Services();
        using var temp = new TempFolder();
        var plant = s.Plants.Create(Path.Combine(temp.Path, "anlage"), "Testanlage");
        var opened = s.Plants.Open(plant.Folder!, readOnly: false);
        Assert.Equal("Testanlage", opened.Name);
        var snapshotPath = SnapshotBuilder.Windows11Workgroup("W11-REACH").WriteTo(Path.Combine(temp.Path, "export"));
        ImportRecord? record = null;
        Callable(() => record = s.Plants.Import(opened, [snapshotPath], new ImportOptions("Servicetechniker", "Anlagenverantwortlicher", null)), "Plants.Import");
        var runs = s.Plants.ListRuns(opened, "W11-REACH");
        if (record is not null) Assert.NotEmpty(runs);
        var registers = s.Registers.Load(opened);
        Callable(() => s.Registers.Save(opened, registers, registers.Hashes), "Registers.Save");
        Assert.NotNull(s.Registers.Validate(registers, s.Catalogue.LoadEmbedded(), opened, new DateOnly(2026, 10, 18)));
        Assert.NotNull(s.Registers.Approve(registers, RegisterKind.Exceptions, [], new Approval("ISB", null, new DateOnly(2026, 10, 18), new DateOnly(2027, 10, 18), "CHG-TEST", null)));
        if (runs.Count > 0)
        {
            var run = s.Plants.LoadRun(opened, runs[^1].Id);
            Assert.NotNull(s.Registers.Apply(run.Results, registers, opened, run.HostName, new DateOnly(2026, 10, 18)));
            var snapshot = s.Snapshots.Read(snapshotPath, ReadLimits.Default);
            Assert.NotNull(s.Comparison.Compare(null, null, run, snapshot, ComparisonBasis.Previous));
            Assert.NotNull(s.Comparison.CompareSnapshots(snapshot, snapshot));
            Assert.NotNull(s.Scoring.Score(run.Results, s.Catalogue.LoadEmbedded(), null, false).Item1);
            Callable(() => s.Plants.SaveApproved(opened, new ApprovedState(SchemaIds.Approved, run.HostName, run.Id, run.Snapshot.Sha256,
                new string('a', 64), run.Catalogue.Version, run.Registers, new Approval("QS", null, new DateOnly(2026, 10, 18), null, "CHG-TEST", null))), "SaveApproved");
            Callable(() => s.Plants.LoadApproved(opened, run.HostName), "LoadApproved");
            Callable(() => s.Remediation.Write(run, snapshot, s.Catalogue.LoadEmbedded(), registers, temp.Sub("abhilfe"), "de"), "Remediation.Write");
        }
        var template = Path.Combine(temp.Path, "x.exceptions-template.json");
        RiegelJson.WriteFile(template, new ExceptionFile(SchemaIds.ExceptionsTemplate, "vorlage", []));
        Callable(() => s.Registers.ImportTemplate(registers, template, ReadLimits.Default), "ImportTemplate");
        Assert.NotNull(s.Runs.EvaluateOnly(SnapshotBuilder.Windows10Ltsc2019().Build(), new RunOptions(Language: "de")));
    }

    [Fact]
    public void Berichtsdaten_und_Schreiber_sind_aufrufbar()
    {
        var s = Services();
        using var plant = new TempPlant();
        using var output = new TempFolder();
        var lang = "de";
        Callable(() => s.ReportData.Build(new ReportRequest(plant.Plant, null, new DateOnly(2026, 10, 18), lang, ReportPreset.Audit, false, true,
            [ReportFormat.Pdf, ReportFormat.Xlsx], false, false, false, output.Path)), "ReportData.Build");
        Callable(() => s.ReportData.BuildSummary([plant.Plant], new DateOnly(2026, 10, 18), lang), "BuildSummary");
        Callable(() => s.ReportData.BuildTargetConfig(plant.Plant, null, lang), "BuildTargetConfig");
        Callable(() => s.ReportData.BuildApprovalSheet(plant.Plant, null, lang), "BuildApprovalSheet");
        Callable(() => s.ReportData.BuildCollectionRecord(plant.Plant, "latest", lang), "BuildCollectionRecord");

        var options = new PdfOptions(output.Sub("pdf"), PerMachine: true);
        Assert.True(File.Exists(s.Pdf.WriteReport(SampleData.Report(), options)));
        Assert.True(File.Exists(s.Pdf.WriteSummary(SampleData.Summary(), options)));
        Assert.True(File.Exists(s.Pdf.WriteTargetConfig(SampleData.TargetConfig(), options)));
        Assert.True(File.Exists(s.Pdf.WriteApprovalSheet(SampleData.ApprovalSheet(), options)));
        Assert.True(File.Exists(s.Pdf.WriteCollectionRecord(SampleData.CollectionRecord(), options)));
        Assert.True(File.Exists(s.Tabular.WriteWorkbook(SampleData.Report(), output.Sub("xlsx"))));
        Assert.True(File.Exists(s.Tabular.WriteTargetConfigWorkbook(SampleData.TargetConfig(), output.Sub("xlsx"))));
        Assert.True(File.Exists(s.Tabular.WriteCsv(SampleData.Report(), output.Sub("csv"))));
        var zip = s.Evidence.Write(new EvidenceRequest(SampleData.Plant(), null, SampleData.Date, [], false, null, lang, output.Sub("evidence")));
        Assert.True(File.Exists(zip));
        Assert.NotNull(s.Evidence.Verify(zip, ReadLimits.Default));
        Assert.NotNull(s.Evidence.CompareSample(RepoPaths.Synthetic("win11-24h2-workgroup-admin"), zip, null, ReadLimits.Default));
    }

    [Fact]
    public void Ausgaben_tragen_die_Dateinamen_aus_spec_5()
    {
        var s = Services();
        using var output = new TempFolder();
        var options = new PdfOptions(output.Path, PerMachine: true);
        Assert.Equal("Riegel-Report_Musterwerk-Linie-3_2026-10-18.pdf", Path.GetFileName(s.Pdf.WriteReport(SampleData.Report(), options)));
        Assert.Equal("Riegel-Summary_2026-10-18.pdf", Path.GetFileName(s.Pdf.WriteSummary(SampleData.Summary(), options)));
        Assert.Equal("Riegel-Overview_Musterwerk-Linie-3_2026-10-18.xlsx", Path.GetFileName(s.Tabular.WriteWorkbook(SampleData.Report(), output.Path)));
        Assert.Equal("results.csv", Path.GetFileName(s.Tabular.WriteCsv(SampleData.Report(), output.Path)));
        Assert.True(File.Exists(Path.Combine(output.Path, "Riegel-Report_Musterwerk-Linie-3_2026-10-18_HMI-L3-01.pdf")));
    }

    [Fact]
    public void Befehlszeile_ist_erreichbar()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(CommandApp.Ok, CommandApp.Run(["--version"], new NullPlatform(), output, error));
        Assert.Contains("2026.10", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(CommandApp.Unknown, CommandApp.Run(["--gibt-es-nicht"], new NullPlatform(), output, error));
        Assert.Contains(RglCodesCli.UsageError, error.ToString(), StringComparison.Ordinal);
        Assert.Equal(CommandApp.Unknown, CommandApp.Run(["collect"], new NullPlatform(), output, error));
    }
}
