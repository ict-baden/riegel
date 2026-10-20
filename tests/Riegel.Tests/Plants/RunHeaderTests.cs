// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Plants;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Plants.WorkflowKit;

namespace Riegel.Tests.Plants;

/// <summary>
/// ListRuns liest nur die Kopfangaben der Läufe (Prüfung vom 2026-10-20: bei 1.100 Läufen eines Rechners brauchte check
/// 1,1 s statt 0,4 s, weil jeder Lauf vollständig gelesen wurde). Die Angaben müssen dieselben bleiben.
/// </summary>
public sealed class RunHeaderTests
{
    [Fact]
    public void Kopfangaben_gleichen_dem_vollstaendig_gelesenen_Lauf()
    {
        var clock = new FixedClock();
        var services = Services(clock);
        using var plant = new TempPlant();
        var first = services.Runs.EvaluateAndStore(plant.Plant, plant.AddSnapshot(SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").Build()), new RunOptions());
        clock.Advance(TimeSpan.FromHours(1));
        var second = services.Runs.EvaluateAndStore(plant.Plant,
            plant.AddSnapshot(SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").CollectedAt(new DateTimeOffset(2026, 10, 18, 7, 0, 0, TimeSpan.Zero)).NonAdmin().Build()),
            new RunOptions());

        var listed = services.Plants.ListRuns(plant.Plant, "hmi-test");
        Assert.Equal(2, listed.Count);
        foreach (var run in new[] { first, second })
        {
            var full = RiegelJson.ReadFile<Run>(plant.PathOf(ReportFileNames.RunFile(run.HostName, run.Id)), 1 << 26);
            var expected = new RunInfo(full.Id, full.HostName, full.EvaluatedAtUtc, full.EvaluationDate, full.CollectedAtUtc, full.Snapshot.Sha256,
                full.Catalogue.Version, full.ReEvaluationOf, full.Flags, full.Summary.Score, full.Summary.OpenHigh);
            Assert.Equal(expected, Assert.Single(listed, r => r.Id == run.Id));
        }
    }

    [Fact]
    public void Kaputter_Lauf_nennt_die_Datei()
    {
        var services = Services();
        using var plant = new TempPlant();
        var run = services.Runs.EvaluateAndStore(plant.Plant, plant.AddSnapshot(SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").Build()), new RunOptions());
        var file = plant.PathOf(ReportFileNames.RunFile(run.HostName, run.Id));
        File.WriteAllText(file, File.ReadAllText(file)[..500]);
        var e = Assert.Throws<RiegelException>(() => services.Plants.ListRuns(plant.Plant, "HMI-TEST"));
        Assert.Equal(RglCodesCommon.InvalidJson, e.Code);
        Assert.Contains($"runs/HMI-TEST/{run.Id}.results.json", e.Message, StringComparison.Ordinal);
    }
}
