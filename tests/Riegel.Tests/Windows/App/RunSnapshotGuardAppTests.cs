// SPDX-License-Identifier: MIT
using System.Text.Json.Nodes;
using Riegel.Core.Model;
using Riegel.Core.Plants;
using Riegel.Core.Settings;
using Riegel.Platform.Windows;
using Riegel.Session;
using Riegel.Tests.Cli;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Windows.App;

/// <summary>
/// Regressionstest (Review security-1) für die Oberfläche: „Neu auswerten“ überspringt einen Rechner, dessen Lauf auf
/// einen Export außerhalb der Anlage zeigt (Hinweis mit RGL-0415), und übernimmt diesen Export nicht in snapshots/;
/// die Abhilfe-Dateien brechen mit dem Code ab.
/// </summary>
[Collection(AppCollection.Name)]
public sealed class RunSnapshotGuardAppTests
{
    [Fact]
    public void Neu_auswerten_und_Abhilfe_nutzen_nur_gepruefte_Exporte() => Sta.Run(async () =>
    {
        using var temp = new TempFolder();
        using var plant = new TempPlant();
        var a = SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").CollectedAt(TestRuns.Collected).WriteTo(temp.Sub("a"));
        var b = SnapshotBuilder.Windows10Ltsc2019("W11-TEST").CollectedAt(TestRuns.Collected).WriteTo(temp.Sub("b"));
        Assert.True(Riegel.Tests.Cli.Cli.Run("import", a, b, "--plant", plant.Folder).Exit is 0 or 1 or 2);
        var outside = SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").CollectedAt(TestRuns.Collected.AddHours(-1)).WriteTo(temp.Sub("outside"));
        var runFile = Assert.Single(Directory.GetFiles(plant.PathOf("runs/HMI-TEST"), "*.results.json"));
        var node = JsonNode.Parse(File.ReadAllText(runFile))!;
        node["snapshot"]!["path"] = outside;
        File.WriteAllText(runFile, node.ToJsonString());
        int snapshotsBefore = Directory.GetFiles(plant.PathOf("snapshots"), "*.snapshot.json", SearchOption.AllDirectories).Length;

        using var session = new AppSession(AppMode.Normal, PlatformFactory.Create(), RiegelSettings.Empty, RiegelSettings.Empty, saveSettings: false);
        var notices = new List<(NoticeKind Kind, string Text)>();
        session.Notice += (k, t) => notices.Add((k, t));
        await session.OpenAsync(plant.Folder, readOnly: false);

        await session.ReEvaluateAsync();
        Assert.Contains(notices, n => n.Kind == NoticeKind.Warning && n.Text.Contains("HMI-TEST (" + RglCodesWorkflow.RunSnapshotOutsidePlant + ")", StringComparison.Ordinal));
        Assert.Equal(snapshotsBefore, Directory.GetFiles(plant.PathOf("snapshots"), "*.snapshot.json", SearchOption.AllDirectories).Length);
        Assert.Single(Directory.GetFiles(plant.PathOf("runs/HMI-TEST"), "*.results.json"));
        Assert.Equal(2, Directory.GetFiles(plant.PathOf("runs/W11-TEST"), "*.results.json").Length);

        var e = await Assert.ThrowsAsync<RiegelException>(() => session.GenerateAsync(new DocumentRequest(DocumentKind.Remediation, temp.Sub("out"), "en",
            Hosts: ["HMI-TEST"])));
        Assert.Equal(RglCodesWorkflow.RunSnapshotOutsidePlant, e.Code);
    });
}
