// SPDX-License-Identifier: MIT
using System.Text.Json.Nodes;
using Riegel.Core.Model;
using Riegel.Core.Plants;
using Riegel.Core.Services;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Cli;

/// <summary>
/// Regressionstests (Review security-1): Rücknahme, Vergleich, erneute Auswertung und die Vergleichsbasis eines neuen
/// Laufs lesen den Export eines gespeicherten Laufs nur aus dem Anlagenordner und nur, wenn sein SHA-256 zum Lauf passt.
/// Ein manipulierter <c>snapshot.path</c> (absolut, „..“, fremder Rechner) oder ein ausgetauschter Export ergibt bei
/// <c>remediate</c> und dem Ziel von <c>compare</c> RGL-0415/0416, bei <c>check</c> nur einen Hinweis.
/// </summary>
public sealed class RunSnapshotGuardTests
{
    private static string Snapshot(TempFolder temp, string host, string sub, DateTimeOffset? at = null) =>
        SnapshotBuilder.Windows10Ltsc2019(host).CollectedAt(at ?? TestRuns.Collected).WriteTo(temp.Sub(sub));

    /// <summary>Anlage mit je einem Lauf für HMI-TEST und W11-TEST (über import, wie im Betrieb).</summary>
    private static TempPlant Plant(TempFolder temp)
    {
        var plant = new TempPlant();
        var result = Cli.Run("import", Snapshot(temp, "HMI-TEST", "a"), Snapshot(temp, "W11-TEST", "b"), "--plant", plant.Folder);
        Assert.True(result.Exit is 0 or 1 or 2, result.ToString());
        return plant;
    }

    private static string RunFile(TempPlant plant, string host) => Assert.Single(Directory.GetFiles(plant.PathOf("runs/" + host), "*.results.json"));

    /// <summary>snapshot.path im gespeicherten Lauf ersetzen (wie ein Angreifer mit Schreibrecht auf runs/).</summary>
    private static void SetSnapshotPath(string runFile, string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(runFile))!;
        node["snapshot"]!["path"] = path;
        File.WriteAllText(runFile, node.ToJsonString());
    }

    private static string SnapshotPathOf(string runFile) => JsonNode.Parse(File.ReadAllText(runFile))!["snapshot"]!["path"]!.GetValue<string>();

    [Fact]
    public void Remediate_lehnt_einen_absoluten_Pfad_ausserhalb_der_Anlage_ab()
    {
        using var temp = new TempFolder();
        using var plant = Plant(temp);
        var outside = Snapshot(temp, "HMI-TEST", "outside");
        SetSnapshotPath(RunFile(plant, "HMI-TEST"), outside);

        var result = Cli.Run("remediate", "--plant", plant.Folder, "--machine", "HMI-TEST");
        Assert.Equal(3, result.Exit);
        Assert.Contains(RglCodesWorkflow.RunSnapshotOutsidePlant, result.Err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(plant.PathOf("remediation")), "keine Rücknahme aus einem Export außerhalb der Anlage");
    }

    [Fact]
    public void Remediate_lehnt_Wege_aus_dem_Ordner_hinaus_ab()
    {
        using var temp = new TempFolder();
        using var plant = Plant(temp);
        var runFile = RunFile(plant, "HMI-TEST");
        // ../<Geschwisterordner>/…: liegt neben dem Anlagenordner
        var sibling = Path.Combine(Path.GetDirectoryName(plant.Folder)!, Path.GetFileName(plant.Folder) + "-outside");
        Directory.CreateDirectory(sibling);
        try
        {
            File.Copy(plant.PathOf(SnapshotPathOf(runFile)), Path.Combine(sibling, "x.snapshot.json"));
            SetSnapshotPath(runFile, "../" + Path.GetFileName(sibling) + "/x.snapshot.json");
            var result = Cli.Run("remediate", "--plant", plant.Folder, "--machine", "HMI-TEST", "--out", temp.Sub("out"));
            Assert.Equal(3, result.Exit);
            Assert.Contains(RglCodesWorkflow.RunSnapshotOutsidePlant, result.Err, StringComparison.Ordinal);
        }
        finally { Directory.Delete(sibling, recursive: true); }
    }

    [Fact]
    public void Remediate_lehnt_den_Export_eines_anderen_Rechners_ab()
    {
        using var temp = new TempFolder();
        using var plant = Plant(temp);
        SetSnapshotPath(RunFile(plant, "HMI-TEST"), SnapshotPathOf(RunFile(plant, "W11-TEST")));

        var result = Cli.Run("remediate", "--plant", plant.Folder, "--machine", "HMI-TEST", "--out", temp.Sub("out"));
        Assert.Equal(3, result.Exit);
        Assert.Contains(RglCodesWorkflow.RunSnapshotMismatch, result.Err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "out", "remediation")));
    }

    [Fact]
    public void Remediate_lehnt_einen_ausgetauschten_Export_ab()
    {
        using var temp = new TempFolder();
        using var plant = Plant(temp);
        var stored = plant.PathOf(SnapshotPathOf(RunFile(plant, "HMI-TEST")));
        File.WriteAllText(stored, File.ReadAllText(stored).Replace("\"HMI-TEST\"", "\"HMI-TEST\" ", StringComparison.Ordinal));

        var result = Cli.Run("remediate", "--plant", plant.Folder, "--machine", "HMI-TEST", "--out", temp.Sub("out"));
        Assert.Equal(3, result.Exit);
        Assert.Contains(RglCodesWorkflow.RunSnapshotMismatch, result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_lehnt_ein_manipuliertes_Ziel_ab()
    {
        using var temp = new TempFolder();
        using var plant = Plant(temp);
        SetSnapshotPath(RunFile(plant, "HMI-TEST"), Snapshot(temp, "HMI-TEST", "outside"));

        var result = Cli.Run("compare", "--plant", plant.Folder, "--machine", "HMI-TEST");
        Assert.Equal(3, result.Exit);
        Assert.Contains(RglCodesWorkflow.RunSnapshotOutsidePlant, result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_mit_Anlage_behandelt_einen_schlechten_alten_Export_als_fehlend()
    {
        using var temp = new TempFolder();
        using var plant = Plant(temp);
        SetSnapshotPath(RunFile(plant, "HMI-TEST"), Snapshot(temp, "HMI-TEST", "outside"));

        // Neuer Export desselben Rechners: der manipulierte Lauf ist der vorige Lauf (Vergleichsbasis)
        var newer = Snapshot(temp, "HMI-TEST", "newer", TestRuns.Collected.AddDays(1));
        var result = Cli.Run("check", "--snapshot", newer, "--plant", plant.Folder);
        Assert.True(result.Exit is 0 or 1 or 2, result.ToString());
        Assert.Contains(RglCodesWorkflow.RunSnapshotOutsidePlant, result.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"\\server\share\x.snapshot.json")]
    [InlineData("//server/share/x.snapshot.json")]
    [InlineData("C:x.snapshot.json")]
    [InlineData("/etc/x.snapshot.json")]
    [InlineData("snapshots/../../x.snapshot.json")]
    [InlineData("snapshots")]
    public void RunSnapshotPath_lehnt_Pfade_ausserhalb_ab_ohne_sie_anzufassen(string path)
    {
        using var temp = new TempFolder();
        using var plant = Plant(temp);
        var services = RiegelServices.Create(new NullPlatform(), new FixedClock());
        var opened = services.Plants.Open(plant.Folder, readOnly: true);
        var run = services.Plants.LoadRun(opened, Path.GetFileName(RunFile(plant, "HMI-TEST"))[..^".results.json".Length]);

        var e = Assert.Throws<RiegelException>(() => services.Plants.RunSnapshotPath(opened, run with { Snapshot = run.Snapshot with { Path = path } }));
        Assert.Equal(RglCodesWorkflow.RunSnapshotOutsidePlant, e.Code);

        // Der echte Export ist in Ordnung
        Assert.Equal(Path.GetFullPath(plant.PathOf(run.Snapshot.Path)), services.Plants.RunSnapshotPath(opened, run), ignoreCase: OperatingSystem.IsWindows());
        // Fehlt die Datei, RGL-0002
        var missing = Assert.Throws<RiegelException>(() => services.Plants.RunSnapshotPath(opened, run with { Snapshot = run.Snapshot with { Path = "snapshots/HMI-TEST/fehlt.snapshot.json" } }));
        Assert.Equal(RglCodesCommon.NotFound, missing.Code);
    }
}
