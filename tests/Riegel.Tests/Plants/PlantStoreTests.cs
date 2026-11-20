// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Plants;
using Riegel.Core.Runs;
using Riegel.Core.Services;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Plants.WorkflowKit;

namespace Riegel.Tests.Plants;

/// <summary>
/// Anlagenordner (spec 7.6): Anlegen, Öffnen, Speichern mit Sperre und optimistischer Nebenläufigkeit, Verlauf,
/// Import-Nachweise mit Begleitdateien und Rollen, Läufe, freigegebener Stand, Prüfermodus, bereinigte Pfade.
/// </summary>
public sealed class PlantStoreTests
{
    [Fact]
    public void Anlegen_schreibt_Anlage_und_Verlauf_ein_zweites_Mal_nicht()
    {
        using var temp = new TempFolder();
        var services = Services();
        var plant = services.Plants.Create(Path.Combine(temp.Path, "werk"), "Musterwerk – Linie 3");
        Assert.Equal("musterwerk-linie-3", plant.Id);
        Assert.Equal(Hashes.Sha256File(Path.Combine(plant.Folder!, "riegel-plant.json")), plant.FileSha256);
        SchemaValidator.AssertValid(File.ReadAllText(Path.Combine(plant.Folder!, "riegel-plant.json")), "PlantStoreTests.cs");
        Assert.Single(Directory.GetFiles(Path.Combine(plant.Folder!, "history", "plant")));
        Assert.Equal(RglCodesCommon.AlreadyExists,
            Assert.Throws<RiegelException>(() => services.Plants.Create(plant.Folder!, "Noch einmal")).Code);
        var opened = services.Plants.Open(plant.Folder!, readOnly: false);
        Assert.Equal("Musterwerk – Linie 3", opened.Name);
        Assert.False(opened.ReadOnly);
    }

    [Fact]
    public void Oeffnen_ohne_Anlagendatei_und_mit_ungueltiger_Anlage()
    {
        using var temp = new TempFolder();
        var services = Services();
        Assert.Equal(RglCodesWorkflow.NotAPlant, Assert.Throws<RiegelException>(() => services.Plants.Open(temp.Path, false)).Code);
        using var plant = new TempPlant(TempPlant.Default() with { Groups = [new("g", "A", MachineRole.Hmi), new("G", "B", MachineRole.Server)] });
        Assert.Equal(RglCodesWorkflow.InvalidPlant, Assert.Throws<RiegelException>(() => services.Plants.Open(plant.Folder, false)).Code);
    }

    [Fact]
    public void Anlage_speichern_mit_Pruefsumme_und_Verlauf()
    {
        using var plant = new TempPlant();
        var services = Services();
        var opened = services.Plants.Open(plant.Folder, false);
        var renamed = opened with { Description = "Neue Beschreibung" };
        services.Plants.SavePlant(renamed, opened.FileSha256!);
        var again = services.Plants.Open(plant.Folder, false);
        Assert.Equal("Neue Beschreibung", again.Description);
        Assert.Single(Directory.GetFiles(Path.Combine(plant.Folder, "history", "plant")));
        // mit dem alten Hash: von jemand anderem geändert
        Assert.Equal(RglCodesWorkflow.ChangedByOthers,
            Assert.Throws<RiegelException>(() => services.Plants.SavePlant(renamed with { Description = "x" }, opened.FileSha256!)).Code);
        Assert.Equal("Neue Beschreibung", services.Plants.Open(plant.Folder, false).Description);
    }

    [Fact]
    public void Zwei_Speicher_schreiben_gleichzeitig_genau_einer_gewinnt()
    {
        using var plant = new TempPlant();
        for (int round = 0; round < 5; round++)
        {
            var a = Services();
            var b = Services();
            var opened = a.Plants.Open(plant.Folder, false);
            var outcomes = new string?[2];
            using var go = new ManualResetEventSlim();
            var threads = new[] { a.Plants, b.Plants }.Select((store, i) => new Thread(() =>
            {
                go.Wait();
                try { store.SavePlant(opened with { Description = $"Runde {round} Instanz {i}" }, opened.FileSha256!); outcomes[i] = "ok"; }
                catch (RiegelException e) { outcomes[i] = e.Code; }
            })).ToList();
            threads.ForEach(t => t.Start());
            go.Set();
            threads.ForEach(t => t.Join());
            Assert.Equal(1, outcomes.Count(o => o == "ok"));
            Assert.Equal(1, outcomes.Count(o => o == RglCodesWorkflow.ChangedByOthers));
            var winner = Array.IndexOf(outcomes, "ok");
            Assert.Equal($"Runde {round} Instanz {winner}", a.Plants.Open(plant.Folder, false).Description);
        }
    }

    [Fact]
    public void Gehaltene_Sperre_ergibt_nach_der_Wartezeit_RGL_0404()
    {
        using var plant = new TempPlant();
        var store = new PlantStore(Services()) { LockTimeout = TimeSpan.FromMilliseconds(300) };
        var opened = store.Open(plant.Folder, false);
        using (new FileStream(plant.PathOf(".riegel.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var e = Assert.Throws<RiegelException>(() => store.SavePlant(opened with { Description = "x" }, opened.FileSha256!));
            Assert.Equal(RglCodesWorkflow.LockTimeout, e.Code);
            Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(250));
        }
        store.SavePlant(opened with { Description = "x" }, opened.FileSha256!);
        Assert.Equal(TimeSpan.FromSeconds(10), PlantLock.DefaultTimeout);
    }

    [Fact]
    public void Import_legt_Export_Begleitdatei_Lauf_und_Nachweis_mit_Rollen_an()
    {
        using var plant = new TempPlant(TempPlant.Default() with { Settings = PlantSettings.Default with { OperatorRole = "Betreiber" } });
        using var export = new TempFolder();
        var services = Services();
        var path = SnapshotBuilder.Windows11Workgroup("W11-TEST").WriteTo(export.Path);
        var record = services.Plants.Import(plant.Plant, [path], new ImportOptions("Servicetechniker", "Anlagenverantwortlicher", null));

        Assert.Matches(@"^20261018T090000Z-[0-9a-f]{6}$", record.Id);
        Assert.Equal("Servicetechniker", record.CollectedByRole);
        Assert.Equal("Anlagenverantwortlicher", record.WitnessRole);
        Assert.Equal("Betreiber", record.OperatorRole);
        var file = Assert.Single(record.Files);
        Assert.True(file.SidecarOk);
        Assert.Equal(Hashes.Sha256File(path), file.Sha256);
        Assert.Equal("W11-TEST", file.HostName);
        Assert.NotNull(file.RunId);
        var stored = Path.Combine(plant.Folder, "snapshots", "W11-TEST", Path.GetFileName(path));
        Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(stored));
        Assert.Equal(HashCheckStatus.Ok, SnapshotFile.Verify(stored).Status);
        var recordFile = Path.Combine(plant.Folder, "imports", record.Id + ".import.json");
        SchemaValidator.AssertValid(File.ReadAllText(recordFile), "PlantStoreTests.cs");
        var run = services.Plants.LoadRun(plant.Plant, file.RunId!);
        Assert.Equal(record.ImportedAtUtc, run.ImportedAtUtc);
        Assert.Equal($"snapshots/W11-TEST/{Path.GetFileName(path)}", run.Snapshot.Path);

        // erneut importiert: derselbe Export bleibt, die Auswertung ist eine erneute Auswertung
        var clock = new FixedClock(FixedClock.Default.AddHours(1));
        var again = Services(clock).Plants.Import(plant.Plant, [path], new ImportOptions(null, null, null));
        Assert.Equal(file.RunId, Services().Plants.LoadRun(plant.Plant, again.Files[0].RunId!).ReEvaluationOf);
    }

    [Fact]
    public void Import_wertet_mehrere_Exporte_eines_Rechners_in_Erfassungsreihenfolge_aus()
    {
        using var export = new TempFolder();
        var early = SnapshotBuilder.Windows11Workgroup("W11-TEST").CollectedAt(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero)).WriteTo(export.Path);
        var late = SnapshotBuilder.Windows11Workgroup("W11-TEST").CollectedAt(new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero)).WriteTo(export.Path);

        (Run Early, Run Late, ImportRecord Record) ImportIn(params string[] order)
        {
            var plant = new TempPlant();
            try
            {
                var services = Services();
                var record = services.Plants.Import(plant.Plant, order, new ImportOptions(null, null, null));
                var runs = record.Files.Select(f => services.Plants.LoadRun(plant.Plant, f.RunId!)).ToList();
                return (runs.Single(r => r.CollectedAtUtc.Day == 1), runs.Single(r => r.CollectedAtUtc.Day == 10), record);
            }
            finally { plant.Dispose(); }
        }

        // neuester zuerst übergeben (Mehrfachauswahl im Dateidialog): der Vorlauf ist trotzdem der ältere Export
        var reversed = ImportIn(late, early);
        Assert.Null(reversed.Early.PreviousRunId);
        Assert.Equal(reversed.Early.Id, reversed.Late.PreviousRunId);
        Assert.Equal([Path.GetFileName(early), Path.GetFileName(late)], reversed.Record.Files.Select(f => f.FileName));

        // dasselbe Ergebnis wie in chronologischer Reihenfolge
        var ordered = ImportIn(early, late);
        Assert.Equal(ordered.Record.Id, reversed.Record.Id);
        Assert.Equal(ordered.Late.Results.Select(r => (r.RuleId, r.Status, r.Flags)), reversed.Late.Results.Select(r => (r.RuleId, r.Status, r.Flags)));
    }

    [Fact]
    public void Import_mit_falscher_Pruefsumme_schreibt_nichts()
    {
        using var plant = new TempPlant();
        using var export = new TempFolder();
        var good = SnapshotBuilder.Windows11Workgroup("W11-TEST").WriteTo(export.Path);
        var bad = SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").WriteTo(export.Path);
        File.WriteAllText(SnapshotFile.SidecarPath(bad), SnapshotFile.SidecarLine(new string('0', 64), Path.GetFileName(bad)));
        var e = Assert.Throws<RiegelException>(() => Services().Plants.Import(plant.Plant, [good, bad], new ImportOptions(null, null, null)));
        Assert.Equal(RglCodesWorkflow.SidecarMismatch, e.Code);
        Assert.False(Directory.Exists(Path.Combine(plant.Folder, "snapshots")));
        Assert.False(Directory.Exists(Path.Combine(plant.Folder, "imports")));
        Assert.False(Directory.Exists(Path.Combine(plant.Folder, "runs")));
    }

    [Fact]
    public void Import_ohne_Begleitdatei_wird_vermerkt_und_Namenskonflikt_abgelehnt()
    {
        using var plant = new TempPlant();
        using var export = new TempFolder();
        var path = SnapshotBuilder.Windows11Workgroup("W11-TEST").WriteTo(export.Path);
        File.Delete(SnapshotFile.SidecarPath(path));
        var record = Services().Plants.Import(plant.Plant, [path], new ImportOptions(null, null, null, Evaluate: false));
        Assert.False(record.Files[0].SidecarOk);
        Assert.Null(record.Files[0].RunId);
        Assert.False(Directory.Exists(Path.Combine(plant.Folder, "runs")));

        // anderer Inhalt, gleicher Name (gleicher Rechner, gleiche Sekunde)
        using var other = new TempFolder();
        var conflicting = SnapshotBuilder.Windows11Workgroup("W11-TEST").NonAdmin().WriteTo(other.Path);
        Assert.Equal(Path.GetFileName(path), Path.GetFileName(conflicting));
        var fine = SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").WriteTo(other.Path);
        Assert.Equal(RglCodesWorkflow.SnapshotConflict,
            Assert.Throws<RiegelException>(() => Services().Plants.Import(plant.Plant, [fine, conflicting], new ImportOptions(null, null, null))).Code);
        Assert.False(Directory.Exists(Path.Combine(plant.Folder, "snapshots", "HMI-TEST")), "kein Teilimport");
    }

    [Fact]
    public void Laeufe_speichern_auflisten_laden()
    {
        using var plant = new TempPlant();
        var services = Services();
        var catalogue = new TestCatalogue([Check("NET-08")]);
        var runs = new RunService(services, new ScriptedEvaluator());
        var r1 = runs.EvaluateAndStore(plant.Plant, plant.AddSnapshot(SnapshotBuilder.Windows11Workgroup("W11-TEST").Script("NET-08", "pass|1").Build()),
            new RunOptions(Catalogue: catalogue));
        var listed = services.Plants.ListRuns(plant.Plant, "w11-test");
        Assert.Equal(r1.Id, Assert.Single(listed).Id);
        Assert.Equal(r1.Summary.OpenHigh, listed[0].OpenHigh);
        Assert.Equal(WorkflowKit.ToJson(r1), WorkflowKit.ToJson(services.Plants.LoadRun(plant.Plant, r1.Id)));
        SchemaValidator.AssertValid(File.ReadAllText(plant.PathOf($"runs/W11-TEST/{r1.Id}.results.json")), "PlantStoreTests.cs");

        // fremde Dateien im Laufordner zählen nicht
        File.WriteAllText(plant.PathOf("runs/W11-TEST/notiz.results.json"), "{}");
        Assert.Single(services.Plants.ListRuns(plant.Plant, "W11-TEST"));
        Assert.Equal(RglCodesWorkflow.InvalidRunId, Assert.Throws<RiegelException>(() => services.Plants.LoadRun(plant.Plant, "../../evil")).Code);
        Assert.Equal(RglCodesCommon.NotFound, Assert.Throws<RiegelException>(() => services.Plants.LoadRun(plant.Plant, "20260101T000000Z-abcdef")).Code);
        Assert.Equal(RglCodesWorkflow.InvalidRunId,
            Assert.Throws<RiegelException>(() => services.Plants.SaveRun(plant.Plant, r1 with { Id = "x/../../y" })).Code);
    }

    [Fact]
    public void Freigegebener_Stand_mit_Pruefsummen_und_Verlauf()
    {
        using var plant = new TempPlant();
        var services = Services();
        var runs = new RunService(services, new ScriptedEvaluator());
        var run = runs.EvaluateAndStore(plant.Plant, plant.AddSnapshot(SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").Script("NET-08", "pass|1").Build()),
            new RunOptions(Catalogue: new TestCatalogue([Check("NET-08")])));
        var approval = new Approval("Qualitätssicherung", null, null, null, "SAT-Protokoll SAT-7", null);
        services.Plants.SaveApproved(plant.Plant, new ApprovedState(SchemaIds.Approved, "hmi-test", run.Id, "", "", "", RegisterHashes.Empty, approval));
        var state = services.Plants.LoadApproved(plant.Plant, "HMI-TEST")!;
        Assert.Equal(run.Snapshot.Sha256, state.SnapshotSha256);
        Assert.Equal(Hashes.Sha256File(plant.PathOf($"runs/HMI-TEST/{run.Id}.results.json")), state.ResultsSha256);
        Assert.Equal("2026.10", state.CatalogueVersion);
        Assert.Equal(Today, state.Approval.ApprovedOn);
        Assert.Equal("HMI-TEST", state.HostName);
        SchemaValidator.AssertValid(File.ReadAllText(plant.PathOf("approved/HMI-TEST.json")), "PlantStoreTests.cs");
        var history = Assert.Single(Directory.GetFiles(plant.PathOf("history/approved/HMI-TEST")));
        Assert.Equal("20261018T090000Z.json", Path.GetFileName(history));

        Assert.Equal(RglCodesWorkflow.ApprovedStateMismatch, Assert.Throws<RiegelException>(() => services.Plants.SaveApproved(plant.Plant,
            state with { ResultsSha256 = new string('a', 64) })).Code);
        Assert.Equal(RglCodesWorkflow.ApprovalIncomplete, Assert.Throws<RiegelException>(() => services.Plants.SaveApproved(plant.Plant,
            state with { Approval = approval with { ApprovalRecord = null } })).Code);
        Assert.Null(services.Plants.LoadApproved(plant.Plant, "W11-TEST"));
    }

    [Fact]
    public void Pruefermodus_lehnt_jedes_Schreiben_ab_und_laesst_den_Ordner_unveraendert()
    {
        using var plant = new TempPlant();
        using var export = new TempFolder();
        var services = Services();
        var path = SnapshotBuilder.Windows11Workgroup("W11-TEST").WriteTo(export.Path);
        services.Plants.Import(plant.Plant, [path], new ImportOptions(null, null, null));
        var before = Listing(plant.Folder);

        var auditor = services.Plants.Open(plant.Folder, readOnly: true);
        Assert.True(auditor.ReadOnly);
        var run = services.Plants.LoadRun(auditor, services.Plants.ListRuns(auditor, "W11-TEST")[0].Id);
        var registers = services.Registers.Load(auditor);
        var refused = new Action[]
        {
            () => services.Plants.SavePlant(auditor with { Description = "x" }, auditor.FileSha256!),
            () => services.Plants.Import(auditor, [path], new ImportOptions(null, null, null)),
            () => services.Plants.SaveRun(auditor, run),
            () => services.Plants.SaveApproved(auditor, new ApprovedState(SchemaIds.Approved, run.HostName, run.Id, "", "", "", RegisterHashes.Empty,
                new Approval("QS", null, Today, null, "CHG-1", null))),
            () => services.Registers.Save(auditor, registers with { Measures = registers.Measures with { Measures = [Measure("M-0001", "NET-08", All, Today)] } }, registers.Hashes),
            () => services.Runs.EvaluateAndStore(auditor, path, new RunOptions()),
        };
        foreach (var write in refused)
            Assert.Equal(RglCodesWorkflow.ReadOnlyPlant, Assert.Throws<RiegelException>(write).Code);
        // Auswerten ohne Speichern ist erlaubt
        Assert.NotNull(services.Runs.EvaluateAndStore(auditor, path, new RunOptions(Store: false)));
        Assert.Equal(before, Listing(plant.Folder));
    }

    [Theory]
    [InlineData("HMI-01", "HMI-01")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.local", "_nul.local")]
    [InlineData("../../Windows", "Windows")]
    [InlineData("Bedien station/Ä", "Bedien-station-Ae")]
    [InlineData("", "unknown")]
    public void Rechnernamen_werden_fuer_Pfade_bereinigt(string host, string expected)
    {
        Assert.Equal(expected, FileNames.Host(host));
        using var plant = new TempPlant();
        var services = Services();
        var run = new RunService(services, new ScriptedEvaluator()).EvaluateOnly(
            SnapshotBuilder.Windows11Workgroup("W11-TEST").Header(m => m with { HostName = host }).Build(), new RunOptions(Catalogue: new TestCatalogue([])));
        services.Plants.SaveRun(plant.Plant, run);
        var files = Directory.GetFiles(Path.Combine(plant.Folder, "runs"), "*", SearchOption.AllDirectories);
        var file = Assert.Single(files);
        Assert.Equal(Path.Combine(plant.Folder, "runs", expected, run.Id + ".results.json"), file);
        Assert.True(FileNames.Host(new string('x', 100)).Length <= FileNames.MaxLength);
    }

    private static List<string> Listing(string folder) =>
        [.. Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f) + ":" + Hashes.Sha256File(f)).Order(StringComparer.Ordinal)];
}
