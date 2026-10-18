// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Commands;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Samples;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Cli;

/// <summary>
/// Jeder Befehl aus spec 7.7 gegen die Stubs des Gerüsts (und, wo die Stubs nichts liefern, gegen Fälschungen):
/// Aufruf, Ausgabe, geschriebene Dateien, Exitcode, Prüfermodus. Nur Temp-Ordner und Fixtures, nie dieser PC.
/// </summary>
public sealed class CommandTests
{
    private static string Snapshot(TempFolder temp, string host = "HMI-TEST", bool admin = true, string sub = "in")
    {
        var builder = SnapshotBuilder.Windows10Ltsc2019(host).CollectedAt(TestRuns.Collected);
        if (!admin) builder.NonAdmin();
        return builder.WriteTo(temp.Sub(sub));
    }

    // ================= global =================

    [Fact]
    public void Version_info_und_lizenzen()
    {
        using var _ = new UiCulture("en");
        var version = Cli.Run("--version");
        Assert.Equal(0, version.Exit);
        Assert.Matches(@"^Riegel \S+.* · collector \S+ · catalogue 2026\.10", version.Out);

        var info = Cli.Run("--info");
        Assert.Equal(0, info.Exit);
        foreach (var text in new[] { "Riegel (Härtungs-Check)", "© 2026 ICT-Baden", "MIT", "https://github.com/ict-baden/riegel", "https://ict-baden.de/impressum", "--licenses" })
            Assert.Contains(text, info.Out, StringComparison.Ordinal);

        foreach (var word in new[] { "--licenses", "--lizenzen" })
        {
            var licences = Cli.Run(word);
            Assert.Equal(0, licences.Exit);
            Assert.Contains("MIT License", licences.Out, StringComparison.Ordinal);
        }
        Assert.Equal(3, Cli.Run("--version", "extra").Exit);
    }

    [Theory]
    [InlineData("gibtsnicht")]
    [InlineData("--demo")]
    [InlineData("--screenshots")]
    [InlineData("--demo-report")]
    public void Unbekannte_und_reine_Oberflaechenbefehle_sind_Aufruffehler(string command)
    {
        var result = Cli.Run(command);
        Assert.Equal(3, result.Exit);
        Assert.Matches(@"^RGL-04[5-9]\d: ", result.Err);
        Assert.Equal("", result.Out);
    }

    [Fact]
    public void Ohne_Argumente_die_Hilfe()
    {
        var result = Cli.Run();
        Assert.Equal(0, result.Exit);
        Assert.Contains("check", result.Out, StringComparison.Ordinal);
    }

    // ================= check =================

    [Fact]
    public void Check_ohne_Anlage_gibt_JSON_mit_Lauf_und_schreibt_nichts()
    {
        using var temp = new TempFolder();
        var file = Snapshot(temp);
        var before = Directory.GetFiles(temp.Path, "*", SearchOption.AllDirectories).Length;
        var result = Cli.Run("check", "--snapshot", file, "--format", "json");

        Assert.True(result.Exit is 0 or 1 or 2, result.ToString());
        var json = result.Json();
        Assert.Equal("riegel.cli.check/1", json.GetProperty("schema").GetString());
        Assert.Equal(result.Exit, json.GetProperty("exitCode").GetInt32());
        Assert.Equal("state", json.GetProperty("alert").GetProperty("mode").GetString());
        var machine = json.GetProperty("machines")[0];
        Assert.Equal("HMI-TEST", machine.GetProperty("host").GetString());
        Assert.Equal(SchemaIds.Run, machine.GetProperty("run").GetProperty("schema").GetString());
        Assert.Equal(12, machine.GetProperty("tokens").EnumerateObject().Count());
        Assert.Equal(before, Directory.GetFiles(temp.Path, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void Check_mit_Anlage_legt_sie_an_und_nimmt_Export_und_Lauf_auf()
    {
        using var culture = new UiCulture("en");
        using var temp = new TempFolder();
        var file = Snapshot(temp);
        var plant = Path.Combine(temp.Path, "neue-anlage");
        var result = Cli.Run("check", "--plant", plant, "--snapshot", file);

        Assert.True(result.Exit is 0 or 1 or 2, result.ToString());
        Assert.True(File.Exists(Path.Combine(plant, "riegel-plant.json")));
        Assert.Single(Directory.GetFiles(Path.Combine(plant, "runs", "HMI-TEST"), "*.results.json"));
        Assert.True(File.Exists(Path.Combine(plant, "snapshots", "HMI-TEST", Path.GetFileName(file))));
        Assert.Single(Directory.GetFiles(Path.Combine(plant, "imports"), "*.import.json"));
        Assert.Contains("Alert mode change", result.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_no_store_und_nur_lesen_schreiben_nichts_in_die_Anlage()
    {
        using var culture = new UiCulture("en");
        using var temp = new TempFolder();
        using var plant = new TempPlant();
        var file = Snapshot(temp);
        foreach (var args in new[] { new[] { "--no-store" }, ["--read-only"], ["--nur-lesen"] })
        {
            var result = Cli.Run(["check", "--plant", plant.Folder, "--snapshot", file, .. args]);
            Assert.True(result.Exit is 0 or 1 or 2, result.ToString());
            Assert.False(Directory.Exists(plant.PathOf("runs")), string.Join(" ", args));
            Assert.False(Directory.Exists(plant.PathOf("snapshots")), string.Join(" ", args));
            Assert.Contains("not stored", result.Out, StringComparison.Ordinal);
        }
        // Im Prüfermodus wird auch kein fehlender Anlagenordner angelegt
        var missing = Path.Combine(temp.Path, "fehlt");
        var readOnly = Cli.Run("--read-only", "check", "--plant", missing, "--snapshot", file);
        Assert.Equal(3, readOnly.Exit);
        Assert.Contains(RglCodesCli.PlantNotFound, readOnly.Err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void Check_eines_Exports_aus_dem_Anlagenordner_wertet_neu_aus_ohne_neuen_Import()
    {
        using var temp = new TempFolder();
        using var plant = new TempPlant();
        var inside = plant.AddSnapshot(SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").CollectedAt(TestRuns.Collected).Build());
        var result = Cli.Run("check", "--plant", plant.Folder, "--snapshot", inside);
        Assert.True(result.Exit is 0 or 1 or 2, result.ToString());
        Assert.Single(Directory.GetFiles(plant.PathOf("runs/HMI-TEST"), "*.results.json"));
        Assert.False(Directory.Exists(plant.PathOf("imports")));
        Assert.Single(Directory.GetFiles(plant.PathOf("snapshots/HMI-TEST"), "*.snapshot.json"));
    }

    [Fact]
    public void Check_mit_falscher_Pruefsumme_endet_mit_3()
    {
        using var temp = new TempFolder();
        var file = Snapshot(temp);
        File.WriteAllText(SnapshotFile.SidecarPath(file), SnapshotFile.SidecarLine(new string('0', 64), Path.GetFileName(file)));
        var result = Cli.Run("check", "--snapshot", file);
        Assert.Equal(3, result.Exit);
        Assert.Contains(RglCodesCli.SidecarMismatch, result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_dieser_PC_unter_Linux_und_in_Tests_ohne_Erfassung_endet_mit_3()
    {
        foreach (var args in new[] { new[] { "check" }, ["check", "--this-pc"] })
        {
            var result = Cli.Run(args);
            Assert.Equal(3, result.Exit);
            Assert.Contains(RglCodesCli.WindowsOnly, result.Err, StringComparison.Ordinal);
            Assert.Contains("check --snapshot", result.Err, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Check_dieser_PC_mit_Erfassung_wertet_den_frischen_Export_aus()
    {
        using var temp = new TempFolder();
        using var plant = new TempPlant();
        var platform = new FakePlatform(SnapshotBuilder.Windows11Workgroup("W11-TEST").CollectedAt(TestRuns.Collected).Build());
        // ohne --snapshot ist --this-pc der Standard, wenn die Plattform erfassen kann
        var result = Cli.Run(Cli.With(platform: platform), "check", "--plant", plant.Folder, "--brief");
        Assert.True(result.Exit is 0 or 1 or 2, result.ToString());
        Assert.Single(platform.Collected);
        Assert.StartsWith("RIEGEL ", result.Out, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(plant.PathOf("snapshots/W11-TEST"), "*.snapshot.json"));
        Assert.Single(Directory.GetFiles(plant.PathOf("runs/W11-TEST"), "*.results.json"));
    }

    [Fact]
    public void Check_lehnt_widerspruechliche_Angaben_ab()
    {
        Assert.Equal(3, Cli.Run("check", "--snapshot", "a.json", "--this-pc").Exit);
        Assert.Equal(3, Cli.Run("check", "--snapshot", "a.json", "--brief", "--format", "json").Exit);
        Assert.Equal(3, Cli.Run("check", "--snapshot", "a.json", "--alert", "sometimes").Exit);
        Assert.Equal(3, Cli.Run("check", "--snapshot").Exit);
        Assert.Equal(3, Cli.Run("check", "--plant", "a", "--plant", "b").Exit);
    }

    // ================= collect =================

    [Fact]
    public void Collect_ohne_Erfassung_endet_mit_3_und_klarer_Meldung()
    {
        using var _ = new UiCulture("en");
        var result = Cli.Run("collect");
        Assert.Equal(3, result.Exit);
        Assert.Contains($"{RglCodesCli.WindowsOnly}: collect works only on Windows", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Collect_schreibt_Export_und_Pruefsumme_Exitcode_nach_Vollstaendigkeit()
    {
        using var temp = new TempFolder();
        var admin = new FakePlatform(SnapshotBuilder.Windows11Workgroup("W11-TEST").CollectedAt(TestRuns.Collected).Build());
        var result = Cli.Run(Cli.With(platform: admin), "collect", "--out", temp.Sub("a"), "--minimal", "--gzip");
        Assert.True(result.Exit == 0, result.ToString());
        var file = Assert.Single(Directory.GetFiles(temp.Sub("a"), "*.snapshot.json.gz"));
        Assert.Equal(HashCheckStatus.Ok, SnapshotFile.Verify(file).Status);
        Assert.Equal(new CollectOptions(Minimal: true), admin.Collected.Single());
        Assert.Contains(Hashes.Sha256File(file), result.Out, StringComparison.Ordinal);

        var nonAdmin = new FakePlatform(SnapshotBuilder.Windows11Workgroup("W11-TEST").NonAdmin().CollectedAt(TestRuns.Collected).Build());
        Assert.Equal(1, Cli.Run(Cli.With(platform: nonAdmin), "collect", "--out", temp.Sub("b")).Exit);
    }

    [Fact]
    public void Collect_handoff_schreibt_nur_in_den_selbst_angelegten_Ordner()
    {
        using var temp = new TempFolder();
        var platform = new FakePlatform(SnapshotBuilder.Windows11Workgroup("W11-TEST").CollectedAt(TestRuns.Collected).Build(), temp.Sub("handoff"));
        var token = "0123456789abcdefABCDEF_-";
        var result = Cli.Run(Cli.With(platform: platform), "collect", "--handoff", token, "--deep");
        Assert.True(result.Exit == 0, result.ToString());
        Assert.Equal([token], platform.Handoffs);
        var files = Directory.GetFiles(Path.Combine(temp.Path, "handoff", token)).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(2, files.Count);
        Assert.EndsWith(".snapshot.json", files[0], StringComparison.Ordinal);
        Assert.EndsWith(".snapshot.json.sha256", files[1], StringComparison.Ordinal);

        // vorhandener Ordner: die Plattform lehnt ab, es wird nichts erfasst
        var again = Cli.Run(Cli.With(platform: platform), "collect", "--handoff", token);
        Assert.Equal(3, again.Exit);
        Assert.Single(platform.Collected);
    }

    [Theory]
    [InlineData("kurz")]
    [InlineData("..\\..\\..\\Windows")]
    [InlineData("abcdefghijklmnop/q")]
    [InlineData("abcdefghijklmnop.q")]
    public void Collect_handoff_lehnt_ungueltige_Tokens_ab(string token)
    {
        using var temp = new TempFolder();
        var platform = new FakePlatform(SnapshotBuilder.Windows11Workgroup().Build(), temp.Path);
        var result = Cli.Run(Cli.With(platform: platform), "collect", "--handoff", token);
        Assert.Equal(3, result.Exit);
        Assert.Contains(RglCodesCli.InvalidHandoffToken, result.Err, StringComparison.Ordinal);
        Assert.Empty(platform.Handoffs);
        Assert.Equal(3, Cli.Run(Cli.With(platform: platform), "collect", "--handoff", "0123456789abcdef", "--out", temp.Path).Exit);
    }

    // ================= import =================

    [Fact]
    public void Import_nimmt_Exporte_auf_schreibt_Importnachweis_und_Erhebungsprotokoll()
    {
        using var temp = new TempFolder();
        using var plant = new TempPlant();
        var a = Snapshot(temp, "HMI-TEST", sub: "a");
        var b = Snapshot(temp, "W11-TEST", sub: "b");
        var result = Cli.Run("import", a, b, "--plant", plant.Folder, "--collected-by-role", "Servicetechniker", "--witness-role", "Anlagenverantwortlicher",
            "--record", "--format", "json");

        Assert.True(result.Exit is 0 or 1 or 2, result.ToString());
        var json = result.Json();
        Assert.Equal("riegel.cli.import/1", json.GetProperty("schema").GetString());
        Assert.Equal("change", json.GetProperty("alert").GetProperty("mode").GetString());
        Assert.Equal(2, json.GetProperty("machines").GetArrayLength());
        var record = json.GetProperty("import");
        Assert.Equal("Servicetechniker", record.GetProperty("collectedByRole").GetString());
        Assert.True(File.Exists(plant.PathOf($"imports/{record.GetProperty("id").GetString()}.import.json")));
        Assert.Single(Directory.GetFiles(plant.PathOf("reports"), "Riegel-Erhebungsprotokoll_*.pdf", SearchOption.AllDirectories));
    }

    [Fact]
    public void Import_mit_verfaelschtem_Export_nimmt_gar_nichts_auf()
    {
        using var temp = new TempFolder();
        using var plant = new TempPlant();
        var good = Snapshot(temp, "HMI-TEST", sub: "a");
        var bad = Snapshot(temp, "W11-TEST", sub: "b");
        File.AppendAllText(bad, " ");
        var result = Cli.Run("import", good, bad, "--plant", plant.Folder);
        Assert.Equal(3, result.Exit);
        Assert.Contains(RglCodesCli.SidecarMismatch, result.Err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(plant.PathOf("snapshots")));
        Assert.False(Directory.Exists(plant.PathOf("imports")));
    }

    [Fact]
    public void Import_im_Pruefermodus_und_ohne_Anlage_endet_mit_3()
    {
        using var temp = new TempFolder();
        using var plant = new TempPlant();
        var file = Snapshot(temp);
        var readOnly = Cli.Run("--nur-lesen", "import", file, "--plant", plant.Folder);
        Assert.Equal(3, readOnly.Exit);
        Assert.Contains(RglCodesCli.ReadOnlyRefused, readOnly.Err, StringComparison.Ordinal);
        Assert.Equal(3, Cli.Run("import", file).Exit);
        var noPlant = Cli.Run("import", file, "--plant", Path.Combine(temp.Path, "fehlt"));
        Assert.Contains(RglCodesCli.PlantNotFound, noPlant.Err, StringComparison.Ordinal);
    }

    // ================= Dokumente =================

    private static TempPlant PlantWithRuns(TempFolder temp)
    {
        var plant = new TempPlant();
        plant.WriteRegisters(SampleData.Registers());
        var result = Cli.Run("import", Snapshot(temp, "HMI-TEST", sub: "a"), Snapshot(temp, "W11-TEST", sub: "b"), "--plant", plant.Folder);
        Assert.True(result.Exit is 0 or 1 or 2, result.ToString());
        return plant;
    }

    [Fact]
    public void Report_schreibt_alle_Formate_und_das_Nachweispaket()
    {
        using var temp = new TempFolder();
        using var plant = PlantWithRuns(temp);
        var outDir = temp.Sub("out");
        var result = Cli.Run("report", "--plant", plant.Folder, "--format", "pdf,xlsx,csv,json", "--per-machine", "--evidence", "--out", outDir);

        Assert.True(result.Exit == 0, result.ToString());
        var date = "2026-10-18";
        foreach (var name in new[] { $"Riegel-Report_Testanlage_{date}.pdf", $"Riegel-Report_Testanlage_{date}_HMI-TEST.pdf",
                     $"Riegel-Overview_Testanlage_{date}.xlsx", "results.csv", $"Riegel-Evidence_Testanlage_{date}.zip" })
            Assert.True(File.Exists(Path.Combine(outDir, name)), name);
        var runFile = Assert.Single(Directory.GetFiles(Path.Combine(outDir, "runs", "HMI-TEST"), "*.results.json"));
        var stored = plant.PathOf($"runs/HMI-TEST/{Path.GetFileName(runFile)}");
        Assert.Equal(File.ReadAllBytes(stored), File.ReadAllBytes(runFile));
    }

    [Fact]
    public void Report_ohne_out_in_den_Anlagenordner_im_Pruefermodus_nur_mit_out()
    {
        using var temp = new TempFolder();
        using var plant = PlantWithRuns(temp);
        var result = Cli.Run("report", "--plant", plant.Folder);
        Assert.True(result.Exit == 0, result.ToString());
        var folder = Assert.Single(Directory.GetDirectories(plant.PathOf("reports")));
        Assert.Matches(@"\\?/?2026-10-18T090000Z$", folder);
        Assert.Equal(2, Directory.GetFiles(folder).Length); // Standard: PDF und Excel

        var readOnly = Cli.Run("--read-only", "report", "--plant", plant.Folder, "--machine", "HMI-TEST");
        Assert.Equal(3, readOnly.Exit);
        Assert.Contains(RglCodesCli.ReadOnlyNeedsOut, readOnly.Err, StringComparison.Ordinal);
        var outDir = temp.Sub("auditor");
        Assert.Equal(0, Cli.Run("--read-only", "report", "--plant", plant.Folder, "--out", outDir, "--format", "csv").Exit);
        Assert.True(File.Exists(Path.Combine(outDir, "results.csv")));
        Assert.Single(Directory.GetDirectories(plant.PathOf("reports")));
    }

    [Fact]
    public void Report_prueft_seine_Optionen()
    {
        using var temp = new TempFolder();
        using var plant = PlantWithRuns(temp);
        Assert.Equal(3, Cli.Run("report", "--plant", plant.Folder, "--format", "docx").Exit);
        Assert.Equal(3, Cli.Run("report", "--plant", plant.Folder, "--include-baseline-content").Exit);
        Assert.Equal(3, Cli.Run("report", "--plant", plant.Folder, "--pdfa", "--format", "xlsx").Exit);
        Assert.Equal(3, Cli.Run("report", "--plant", plant.Folder, "--preset", "fancy").Exit);
        Assert.Equal(3, Cli.Run("report", "--plant", plant.Folder, "--language", "klingonisch").Exit);
        Assert.Equal(3, Cli.Run("report").Exit);
    }

    [Fact]
    public void Summary_ueber_zwei_Anlagen()
    {
        using var temp = new TempFolder();
        using var a = PlantWithRuns(temp);
        using var b = new TempPlant();
        var outDir = temp.Sub("summary");
        var result = Cli.Run("summary", "--plant", a.Folder, "--plant", b.Folder, "--out", outDir, "--language", "de");
        Assert.True(result.Exit == 0, result.ToString());
        Assert.True(File.Exists(Path.Combine(outDir, "Riegel-Summary_2026-10-18.pdf")));
        Assert.Equal(3, Cli.Run("summary").Exit);
    }

    [Fact]
    public void Target_config_schreibt_PDF_und_Excel()
    {
        using var temp = new TempFolder();
        using var plant = PlantWithRuns(temp);
        var outDir = temp.Sub("soll");
        var result = Cli.Run("target-config", "--plant", plant.Folder, "--group", "g-hmi", "--out", outDir);
        Assert.True(result.Exit == 0, result.ToString());
        Assert.True(File.Exists(Path.Combine(outDir, "Riegel-Soll-Konfiguration_Testanlage_2026-10-18.pdf")));
        Assert.True(File.Exists(Path.Combine(outDir, "Riegel-Soll-Konfiguration_Testanlage_2026-10-18.xlsx")));
        Assert.Equal(3, Cli.Run("target-config", "--plant", plant.Folder, "--group", "g-gibtsnicht", "--out", outDir).Exit);
    }

    [Fact]
    public void Collection_record_des_neuesten_oder_eines_bestimmten_Imports()
    {
        using var temp = new TempFolder();
        using var plant = PlantWithRuns(temp);
        var outDir = temp.Sub("record");
        var id = Path.GetFileName(Directory.GetFiles(plant.PathOf("imports")).Single())[..^".import.json".Length];
        Assert.Equal(0, Cli.Run("collection-record", "--plant", plant.Folder, "--latest", "--out", outDir).Exit);
        Assert.Equal(0, Cli.Run("collection-record", "--plant", plant.Folder, "--import", id, "--out", outDir).Exit);
        Assert.True(File.Exists(Path.Combine(outDir, $"Riegel-Erhebungsprotokoll_Testanlage_{id}.pdf")));

        var unknown = Cli.Run("collection-record", "--plant", plant.Folder, "--import", "20260101T000000Z-abcdef", "--out", outDir);
        Assert.Equal(3, unknown.Exit);
        Assert.Contains(RglCodesCli.NoImports, unknown.Err, StringComparison.Ordinal);
        Assert.Equal(3, Cli.Run("collection-record", "--plant", plant.Folder, "--import", "..\\x", "--out", outDir).Exit);
        using var empty = new TempPlant();
        Assert.Contains(RglCodesCli.NoImports, Cli.Run("collection-record", "--plant", empty.Folder, "--out", outDir).Err, StringComparison.Ordinal);
    }

    // ================= Vergleich, Freigabe, Abhilfe =================

    [Fact]
    public void Approve_und_compare_gegen_den_freigegebenen_Stand()
    {
        using var culture = new UiCulture("en");
        using var temp = new TempFolder();
        using var plant = PlantWithRuns(temp);

        var noApproval = Cli.Run("compare", "--plant", plant.Folder, "--machine", "HMI-TEST", "--against", "approved");
        Assert.Equal(3, noApproval.Exit);
        Assert.Contains(RglCodesCli.NoApprovedState, noApproval.Err, StringComparison.Ordinal);

        var approve = Cli.Run("approve", "--plant", plant.Folder, "--machine", "HMI-TEST", "--role", "Qualitätssicherung", "--record", "CHG-TEST-0001",
            "--date", "2026-10-17");
        Assert.True(approve.Exit == 0, approve.ToString());
        var state = RiegelJson.ReadFile<ApprovedState>(plant.PathOf("approved/HMI-TEST.json"), 1 << 20);
        Assert.Equal("Qualitätssicherung", state.Approval.ApprovedByRole);
        Assert.Equal("CHG-TEST-0001", state.Approval.ApprovalRecord);
        Assert.Equal(new DateOnly(2026, 10, 17), state.Approval.ApprovedOn);
        var runFile = Directory.GetFiles(plant.PathOf("runs/HMI-TEST")).Single();
        Assert.Equal(Hashes.Sha256File(runFile), state.ResultsSha256);

        var compare = Cli.Run("compare", "--plant", plant.Folder, "--machine", "HMI-TEST", "--against", "approved", "--format", "json");
        Assert.True(compare.Exit == 0, compare.ToString());
        Assert.Equal(state.RunId, compare.Json().GetProperty("fromRunId").GetString());
        Assert.Equal("approved", compare.Json().GetProperty("comparison").GetProperty("basis").GetString());

        var previous = Cli.Run("compare", "--plant", plant.Folder, "--machine", "HMI-TEST");
        Assert.Equal(0, previous.Exit);
        Assert.Contains("no earlier run", previous.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Approve_braucht_Rolle_Nachweis_und_einen_Lauf()
    {
        using var temp = new TempFolder();
        using var plant = PlantWithRuns(temp);
        Assert.Equal(3, Cli.Run("approve", "--plant", plant.Folder, "--machine", "HMI-TEST", "--role", "QS").Exit);
        Assert.Equal(3, Cli.Run("approve", "--plant", plant.Folder, "--machine", "HMI-TEST", "--record", "X").Exit);
        Assert.Equal(3, Cli.Run("approve", "--plant", plant.Folder, "--machine", "HMI-TEST", "--role", " ", "--record", "X").Exit);
        var noRun = Cli.Run("approve", "--plant", plant.Folder, "--machine", "GIBTSNICHT", "--role", "QS", "--record", "X");
        Assert.Contains(RglCodesCli.NoRuns, noRun.Err, StringComparison.Ordinal);
        var otherRun = Directory.GetFiles(plant.PathOf("runs/W11-TEST")).Single();
        var wrong = Cli.Run("approve", "--plant", plant.Folder, "--machine", "HMI-TEST", "--run", Path.GetFileName(otherRun)[..23], "--role", "QS", "--record", "X");
        Assert.Contains(RglCodesCli.RunOfOtherMachine, wrong.Err, StringComparison.Ordinal);
        Assert.Contains(RglCodesCli.ReadOnlyRefused,
            Cli.Run("--read-only", "approve", "--plant", plant.Folder, "--machine", "HMI-TEST", "--role", "QS", "--record", "X").Err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(plant.PathOf("approved")));
    }

    [Fact]
    public void Compare_snapshot_Exitcodes_0_1_und_3()
    {
        using var temp = new TempFolder();
        var fresh = Snapshot(temp);
        var zip = Path.Combine(temp.Path, "Riegel-Evidence_x.zip");
        File.WriteAllBytes(zip, [0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        var comparison = new SampleComparison("HMI-TEST", true, [], "aa", "bb", TestRuns.Collected, TestRuns.Collected);

        var consistent = Cli.Run(Cli.With(evidence: new FakeEvidence(sample: new SampleResult(SampleStatus.Consistent, comparison, null, null))),
            "compare-snapshot", fresh, "--evidence", zip);
        Assert.True(consistent.Exit == 0, consistent.ToString());

        var different = comparison with { Consistent = false, Differences = [new ValueDrift("reg:hklm\\x", "y", "1", "0", DriftKind.Changed)] };
        var differs = Cli.Run(Cli.With(evidence: new FakeEvidence(sample: new SampleResult(SampleStatus.Different, different, null, null))),
            "compare-snapshot", fresh, "--evidence", zip, "--format", "json");
        Assert.Equal(1, differs.Exit);
        Assert.Equal(1, differs.Json().GetProperty("exitCode").GetInt32());

        // Stub des Gerüsts: noch nicht umgesetzt → 3
        Assert.Equal(3, Cli.Run("compare-snapshot", fresh, "--evidence", zip).Exit);
        Assert.Equal(3, Cli.Run("compare-snapshot", fresh).Exit);
    }

    [Fact]
    public void Verify_Exitcodes_0_2_und_3()
    {
        using var temp = new TempFolder();
        var zip = Path.Combine(temp.Path, "Riegel-Evidence_x.zip");
        File.WriteAllBytes(zip, [0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        using (new UiCulture("en"))
        {
            var ok = Cli.Run(Cli.With(evidence: new FakeEvidence(new VerifyResult(VerifyStatus.Reproduced, "0.1.0", "0.1.0", [], 3, 459, []))), "verify", zip);
            Assert.Equal(0, ok.Exit);
            Assert.Contains("Reproduced with engine 0.1.0 (packed: 0.1.0)", ok.Out, StringComparison.Ordinal);
        }
        var mismatch = new VerifyResult(VerifyStatus.Mismatch, "0.1.0", "0.0.9", [new VerifyFinding("RGL-0560", "runs/x.json", "HMI-TEST", "NET-01", "differs")], 1, 153, []);
        var bad = Cli.Run(Cli.With(evidence: new FakeEvidence(mismatch)), "verify", zip, "--format", "json");
        Assert.Equal(2, bad.Exit);
        Assert.Equal("mismatch", bad.Json().GetProperty("result").GetProperty("status").GetString());
        Assert.Equal(3, Cli.Run("verify", zip).Exit);
        Assert.Equal(3, Cli.Run("verify", Path.Combine(temp.Path, "fehlt.zip")).Exit);
    }

    [Fact]
    public void Remediate_schreibt_nur_Dateien_unter_remediation()
    {
        using var temp = new TempFolder();
        using var plant = PlantWithRuns(temp);
        var result = Cli.Run("remediate", "--plant", plant.Folder, "--machine", "HMI-TEST");
        Assert.True(result.Exit == 0, result.ToString());
        var folder = Assert.Single(Directory.GetDirectories(plant.PathOf("remediation/HMI-TEST")));
        Assert.Equal(ReportFileNames.RemediationFiles.Order(StringComparer.Ordinal), Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        var outDir = temp.Sub("vorschlag");
        Assert.Equal(0, Cli.Run("--read-only", "remediate", "--plant", plant.Folder, "--machine", "HMI-TEST", "--out", outDir, "--language", "fr").Exit);
        Assert.True(Directory.Exists(Path.Combine(outDir, "remediation", "HMI-TEST")));
        Assert.Equal(3, Cli.Run("--read-only", "remediate", "--plant", plant.Folder, "--machine", "HMI-TEST").Exit);
    }

    // ================= Register =================

    [Fact]
    public void Exceptions_list_zeigt_den_Zustand_jeder_Ausnahme()
    {
        using var plant = new TempPlant();
        plant.WriteRegisters(SampleData.Registers());
        var result = Cli.Run("exceptions", "list", "--plant", plant.Folder, "--format", "json");
        Assert.True(result.Exit == 0, result.ToString());
        var states = result.Json().GetProperty("entries").EnumerateArray()
            .ToDictionary(e => e.GetProperty("entry").GetProperty("id").GetString()!, e => e.GetProperty("state").GetString());
        Assert.Equal("valid", states["EX-0001"]);
        Assert.Equal("pending", states["EX-0002"]);
        Assert.Equal("changedAfterApproval", states["EX-0003"]);
        Assert.Equal("expiring", states["EX-0004"]);
        Assert.Equal("expired", states["EX-0005"]);

        var expiring = Cli.Run("exceptions", "expiring", "--plant", plant.Folder, "--format", "json");
        Assert.Equal(["EX-0004", "EX-0005"], expiring.Json().GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("entry").GetProperty("id").GetString()));
        var text = Cli.Run("exceptions", "list", "--plant", plant.Folder);
        Assert.Contains("EX-0003", text.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Exceptions_validate_Exitcodes_0_1_2()
    {
        using var plant = new TempPlant();
        plant.WriteRegisters(SampleData.Registers());
        Assert.Equal(0, Cli.Run("exceptions", "validate", "--plant", plant.Folder).Exit);

        var inner = Cli.Inner();
        var warning = new ValidationIssue(RegisterKind.Exceptions, "EX-0005", IssueSeverity.Warning, "RGL-0410", "acceptedValue", "„*“ akzeptiert jeden Wert");
        var error = new ValidationIssue(RegisterKind.Exceptions, "EX-0002", IssueSeverity.Error, "RGL-0411", "justification", "zu kurz");
        var tailoringOnly = new ValidationIssue(RegisterKind.Tailoring, "TL-0002", IssueSeverity.Error, "RGL-0412", null, "x");
        Assert.Equal(1, Cli.Run(new Riegel.Commands.CommandServices(inner) { Registers = new FakeValidation(inner.Registers, warning, tailoringOnly) },
            "exceptions", "validate", "--plant", plant.Folder).Exit);
        var invalid = Cli.Run(new Riegel.Commands.CommandServices(inner) { Registers = new FakeValidation(inner.Registers, warning, error) },
            "exceptions", "validate", "--plant", plant.Folder, "--format", "json");
        Assert.Equal(2, invalid.Exit);
        Assert.Equal(2, invalid.Json().GetProperty("issues").GetArrayLength());
        Assert.Equal(2, Cli.Run(new Riegel.Commands.CommandServices(inner) { Registers = new FakeValidation(inner.Registers, tailoringOnly) },
            "tailoring", "validate", "--plant", plant.Folder).Exit);
    }

    [Fact]
    public void Exceptions_approve_traegt_Freigabe_ein_und_bindet_den_Inhalt()
    {
        using var plant = new TempPlant();
        plant.WriteRegisters(SampleData.Registers());
        var noReview = Cli.Run("exceptions", "approve", "--plant", plant.Folder, "--ids", "EX-0002", "--role", "Informationssicherheitsbeauftragter",
            "--record", "Freigabeblatt FB-12");
        Assert.Equal(3, noReview.Exit);
        Assert.Contains(RglCodesCli.ReviewDateMissing, noReview.Err, StringComparison.Ordinal);
        var unknown = Cli.Run("exceptions", "approve", "--plant", plant.Folder, "--ids", "EX-0002,EX-9999", "--role", "ISB", "--record", "FB-12",
            "--review-by", "2027-10-01");
        Assert.Contains(RglCodesCli.UnknownEntries, unknown.Err, StringComparison.Ordinal);

        var ok = Cli.Run("exceptions", "approve", "--plant", plant.Folder, "--ids", "EX-0002", "--role", "Informationssicherheitsbeauftragter",
            "--record", "Freigabeblatt FB-12", "--review-by", "2027-10-01");
        Assert.True(ok.Exit == 0, ok.ToString());
        var file = RiegelJson.ReadFile<ExceptionFile>(plant.PathOf("exceptions.json"), 1 << 20);
        var entry = file.Exceptions.Single(e => e.Id == "EX-0002");
        Assert.Equal("Freigabeblatt FB-12", entry.Approval.ApprovalRecord);
        Assert.Equal(new DateOnly(2026, 10, 18), entry.Approval.ApprovedOn);
        Assert.Equal(new DateOnly(2027, 10, 1), entry.Approval.ReviewBy);
        Assert.Equal(ContentHashes.Of(entry), entry.Approval.ApprovedContentSha256);

        // EX-0004 hat schon einen Prüftermin: der bleibt
        Assert.Equal(0, Cli.Run("exceptions", "approve", "--plant", plant.Folder, "--ids", "EX-0004", "--role", "ISB", "--record", "FB-13").Exit);
        var renewed = RiegelJson.ReadFile<ExceptionFile>(plant.PathOf("exceptions.json"), 1 << 20).Exceptions.Single(e => e.Id == "EX-0004");
        Assert.Equal(new DateOnly(2026, 11, 5), renewed.Approval.ReviewBy);
    }

    [Fact]
    public void Exceptions_approval_sheet_und_import_template()
    {
        using var temp = new TempFolder();
        using var plant = new TempPlant();
        plant.WriteRegisters(SampleData.Registers());
        var outDir = temp.Sub("blatt");
        Assert.Equal(0, Cli.Run("exceptions", "approval-sheet", "--plant", plant.Folder, "--pending", "--out", outDir).Exit);
        Assert.Single(Directory.GetFiles(outDir, "Riegel-Ausnahmen-zur-Genehmigung_Testanlage_2026-10-18.pdf"));
        Assert.Equal(0, Cli.Run("exceptions", "approval-sheet", "--plant", plant.Folder, "--ids", "EX-0002", "TL-0002", "--out", outDir).Exit);
        Assert.Contains(RglCodesCli.UnknownEntries, Cli.Run("exceptions", "approval-sheet", "--plant", plant.Folder, "--ids", "EX-4242", "--out", outDir).Err,
            StringComparison.Ordinal);

        var template = Path.Combine(temp.Path, "hersteller.exceptions-template.json");
        var entry = SampleData.Exceptions()[1] with { Id = "EX-0100", Approval = Approval.None, CreatedByRole = "Hersteller" };
        RiegelJson.WriteFile(template, new ExceptionFile(SchemaIds.ExceptionsTemplate, "vorlage", [entry]));
        var imported = Cli.Run("exceptions", "import-template", template, "--plant", plant.Folder);
        Assert.True(imported.Exit == 0, imported.ToString());
        var file = RiegelJson.ReadFile<ExceptionFile>(plant.PathOf("exceptions.json"), 1 << 20);
        Assert.Equal(Approval.None, file.Exceptions.Single(e => e.Id == "EX-0100").Approval);
        Assert.Equal(3, Cli.Run("--read-only", "exceptions", "import-template", template, "--plant", plant.Folder).Exit);
    }

    [Fact]
    public void Tailoring_list_approve_und_import_template()
    {
        using var temp = new TempFolder();
        using var plant = new TempPlant();
        plant.WriteRegisters(SampleData.Registers());
        var list = Cli.Run("tailoring", "list", "--plant", plant.Folder, "--format", "json");
        Assert.Equal(3, list.Json().GetProperty("entries").GetArrayLength());

        Assert.Equal(0, Cli.Run("tailoring", "approve", "--plant", plant.Folder, "--ids", "TL-0002", "--role", "ISB", "--record", "CHG-7",
            "--review-by", "2028-10-01").Exit);
        var file = RiegelJson.ReadFile<TailoringFile>(plant.PathOf("tailoring.json"), 1 << 20);
        Assert.Equal("CHG-7", file.Entries.Single(e => e.Id == "TL-0002").Approval.ApprovalRecord);

        var template = Path.Combine(temp.Path, "hmi.tailoring-template.json");
        RiegelJson.WriteFile(template, new TailoringFile(SchemaIds.TailoringTemplate, "hmi", [SampleData.Tailoring()[0] with { Id = "TL-0100" }]));
        Assert.Equal(0, Cli.Run("tailoring", "import-template", template, "--plant", plant.Folder).Exit);
        Assert.Contains(RglCodesCli.UsageError, Cli.Run("tailoring", "expiring", "--plant", plant.Folder).Err, StringComparison.Ordinal);
        Assert.Contains(RglCodesCli.MissingOption, Cli.Run("tailoring").Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Measures_add_list_overdue_und_close()
    {
        using var plant = new TempPlant();
        plant.WriteRegisters(SampleData.Registers());
        var inner = Cli.Inner();
        var services = new Riegel.Commands.CommandServices(inner)
        {
            Catalogue = new CatalogueWith(inner.Catalogue, RuleBuilder.Check("NET-01", "network").Severity(Severity.High).Build()),
        };

        var overdue = Cli.Run(services, "measures", "overdue", "--plant", plant.Folder, "--format", "json");
        Assert.Equal(["M-0002"], overdue.Json().GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("entry").GetProperty("id").GetString()));

        var add = Cli.Run(services, "measures", "add", "--plant", plant.Folder, "--rule", "net-01", "--action", "SMBv1 abschalten",
            "--owner-role", "Anlagenverantwortlicher", "--due", "2026-12-31", "--group", "g-hmi", "--ticket", "T-1");
        Assert.True(add.Exit == 0, add.ToString());
        var file = RiegelJson.ReadFile<MeasureFile>(plant.PathOf("measures.json"), 1 << 20);
        var added = file.Measures.Single(m => m.Id == "M-0005");
        Assert.Equal("NET-01", added.RuleId);
        Assert.Equal(["g-hmi"], added.Scope.Groups);
        Assert.False(added.Scope.AllMachines);
        Assert.Equal(new DateOnly(2026, 10, 18), added.CreatedOn);

        Assert.Equal(0, Cli.Run(services, "measures", "close", "M-0005", "--plant", plant.Folder, "--state", "dropped", "--comment", "anders gelöst").Exit);
        var closed = RiegelJson.ReadFile<MeasureFile>(plant.PathOf("measures.json"), 1 << 20).Measures.Single(m => m.Id == "M-0005");
        Assert.Equal(MeasureState.Dropped, closed.State);
        Assert.Equal("anders gelöst", closed.Comment);

        Assert.Contains(RglCodesCli.UnknownRule, Cli.Run(services, "measures", "add", "--plant", plant.Folder, "--rule", "XYZ-99", "--action", "a",
            "--owner-role", "r", "--due", "2026-12-31").Err, StringComparison.Ordinal);
        Assert.Contains(RglCodesCli.InvalidValue, Cli.Run(services, "measures", "add", "--plant", plant.Folder, "--rule", "NET-01", "--action", "a",
            "--owner-role", "r", "--due", "31.12.2026").Err, StringComparison.Ordinal);
        Assert.Contains(RglCodesCli.UnknownEntries, Cli.Run(services, "measures", "close", "M-4242", "--plant", plant.Folder).Err, StringComparison.Ordinal);
        Assert.Equal(0, Cli.Run(services, "measures", "list", "--plant", plant.Folder).Exit);
    }

    // ================= Baselines, Regeln, Sammler, Anlage =================

    [Fact]
    public void Baseline_import_list_und_remove()
    {
        using var temp = new TempFolder();
        using var plant = new TempPlant();
        var source = Path.Combine(temp.Path, "kunde.baseline.json");
        var check = RuleBuilder.Check("X", "network").Build().Check;
        RiegelJson.WriteFile(source, new BaselineFile(SchemaIds.Baseline, "kunde-l3", "Kundenvorgaben", null, null, null, null,
            [new BaselineRule("K-01", "OPC-Dienst läuft", Severity.High, "services", check, null, null)]));

        var imported = Cli.Run("baseline", "import", source, "--plant", plant.Folder, "--name", "Kundenvorgaben Linie 3");
        Assert.True(imported.Exit == 0, imported.ToString());
        var stored = RiegelJson.ReadFile<BaselineFile>(plant.PathOf("baselines/kunde-l3.json"), 1 << 20);
        Assert.Equal("Kundenvorgaben Linie 3", stored.Name);
        Assert.Equal(Hashes.Sha256File(source), stored.SourceSha256);
        Assert.Equal("K-01", stored.Rules.Single().Id);
        Assert.Equal(3, Cli.Run("baseline", "import", source, "--plant", plant.Folder, "--name", "noch einmal").Exit);

        var list = Cli.Run("baseline", "list", "--plant", plant.Folder, "--format", "json");
        Assert.Equal("kunde-l3", list.Json().GetProperty("baselines")[0].GetProperty("id").GetString());

        Assert.Equal(0, Cli.Run("baseline", "remove", "kunde-l3", "--plant", plant.Folder).Exit);
        Assert.False(File.Exists(plant.PathOf("baselines/kunde-l3.json")));
        Assert.Contains(RglCodesCli.BaselineNotFound, Cli.Run("baseline", "remove", "kunde-l3", "--plant", plant.Folder).Err, StringComparison.Ordinal);
        Assert.Contains(RglCodesCli.BaselineNotFound, Cli.Run("baseline", "remove", "..\\riegel-plant", "--plant", plant.Folder).Err, StringComparison.Ordinal);
        Assert.True(File.Exists(plant.PathOf("riegel-plant.json")));
    }

    [Fact]
    public void Rules_als_Text_JSON_und_Markdown()
    {
        var inner = Cli.Inner();
        var services = new Riegel.Commands.CommandServices(inner)
        {
            Catalogue = new CatalogueWith(inner.Catalogue, RuleBuilder.Check("NET-01", "network").Severity(Severity.High).Build(),
                RuleBuilder.Check("SYS-01").Build()),
        };
        using var _ = new UiCulture("de");
        var text = Cli.Run(services, "rules");
        Assert.Equal(0, text.Exit);
        Assert.Contains("NET-01  Hoch", text.Out, StringComparison.Ordinal);
        Assert.Contains("Testregel NET-01", text.Out, StringComparison.Ordinal);

        var json = Cli.Run(services, "rules", "--format", "json");
        Assert.Equal(2, json.Json().GetProperty("rules").GetArrayLength());
        Assert.Equal("2026.10", json.Json().GetProperty("version").GetString());

        var md = Cli.Run(services, "rules", "--format", "md");
        Assert.StartsWith("| ID | Schwere | Kategorie | Titel |", md.Out, StringComparison.Ordinal);

        var one = Cli.Run(services, "rules", "--id", "sys-01");
        Assert.Contains("SYS-01 – Testregel SYS-01", one.Out, StringComparison.Ordinal);
        Assert.Contains(RglCodesCli.UnknownRule, Cli.Run(services, "rules", "--id", "NOPE-1").Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Collector_export_nur_mit_Erfassung()
    {
        using var temp = new TempFolder();
        var linux = Cli.Run("collector", "export", temp.Path);
        Assert.Equal(3, linux.Exit);
        Assert.Contains(RglCodesCli.WindowsOnly, linux.Err, StringComparison.Ordinal);
        var target = Path.Combine(temp.Path, "stick");
        var windows = Cli.Run(Cli.With(platform: new FakePlatform(SnapshotBuilder.Windows11Workgroup().Build())), "collector", "export", target);
        Assert.Equal(0, windows.Exit);
        Assert.True(File.Exists(Path.Combine(target, "UEBERGABE.txt")));
        Assert.Equal(3, Cli.Run("collector").Exit);
        Assert.Equal(3, Cli.Run("collector", "import", temp.Path).Exit);
    }

    [Fact]
    public void Plant_init_legt_an_einmal()
    {
        using var temp = new TempFolder();
        var folder = Path.Combine(temp.Path, "werk");
        var result = Cli.Run("plant", "init", folder, "--name", "Werk Süd – Linie 2");
        Assert.True(result.Exit == 0, result.ToString());
        var plant = RiegelJson.ReadFile<Plant>(Path.Combine(folder, "riegel-plant.json"), 1 << 20);
        Assert.Equal("Werk Süd – Linie 2", plant.Name);
        Assert.Equal(3, Cli.Run("plant", "init", folder, "--name", "noch einmal").Exit);
        Assert.Equal(3, Cli.Run("plant", "init", folder).Exit);
        Assert.Equal(3, Cli.Run("--read-only", "plant", "init", Path.Combine(temp.Path, "zwei"), "--name", "x").Exit);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "zwei")));
    }

    [Fact]
    public void Jeder_Befehl_aus_spec_7_7_ist_angebunden()
    {
        string[] spec = ["check", "collect", "import", "report", "summary", "target-config", "collection-record", "compare", "compare-snapshot",
            "approve", "exceptions", "tailoring", "measures", "remediate", "baseline", "rules", "verify", "collector", "plant"];
        Assert.Equal(spec.Order(StringComparer.Ordinal), CommandApp.CommandNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Ausgabe_ist_JSON_ohne_Zusatztext_auf_stdout()
    {
        using var temp = new TempFolder();
        var file = Snapshot(temp);
        File.Delete(SnapshotFile.SidecarPath(file));
        var result = Cli.Run("check", "--snapshot", file, "--format", "json");
        using var document = JsonDocument.Parse(result.Out);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Contains(".sha256", result.Err, StringComparison.Ordinal);
        Assert.EndsWith("\n", result.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", result.Out, StringComparison.Ordinal);
    }
}
