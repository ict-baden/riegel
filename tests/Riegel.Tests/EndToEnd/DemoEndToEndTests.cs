// SPDX-License-Identifier: MIT
#if WINDOWS
using Riegel.Demo;
using Riegel.Platform.Windows;
using Riegel.Tests.TestSupport;
using CliApp = Riegel.Tests.Cli.Cli;

namespace Riegel.Tests.EndToEnd;

/// <summary>
/// Ende zu Ende mit der Demo-Anlage „Musterwerk – Linie 3“ über die echte Engine (spec 8.15, 9): der Demo-Bericht
/// schreibt jedes Dokument O1–O15, das Nachweispaket reproduziert sich mit verify, und die Stichprobe mit einem
/// gepackten Export ist konsistent. Nur unter Windows (Demo-Daten stecken in der App).
/// </summary>
[Collection(Riegel.Tests.Windows.App.AppCollection.Name)]
public sealed class DemoEndToEndTests
{
    [Fact]
    public void Demo_Anlage_liefert_O1_bis_O15_und_ein_reproduzierbares_Nachweispaket()
    {
        using var temp = new TempFolder();
        var folder = temp.Sub("bericht");
        var work = temp.Sub("arbeit");
        var entries = DemoReport.Write(folder, work, PlatformFactory.Create());

        Assert.All(entries, e => Assert.True(e.Skipped is null, $"{e.Output} übersprungen: {e.Skipped}"));
        Assert.All(entries, e => Assert.True(File.Exists(e.Path) || Directory.Exists(e.Path), $"{e.Output}: {e.Path} fehlt"));
        var outputs = entries.SelectMany(e => e.Output.Split(' ')[0].Split('/')).ToHashSet(StringComparer.Ordinal);
        foreach (var o in Enumerable.Range(1, 15).Select(n => "O" + n))
            Assert.Contains(o, outputs);

        // O1 Export mit Prüfsumme, O2 Ergebnisse, O8 Register liegen in der Kopie des Anlagenordners
        var plant = entries.Single(e => e.Output == "O1/O2/O8").Path!;
        Assert.NotEmpty(Directory.GetFiles(plant, "*.snapshot.json.sha256", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.GetFiles(plant, "*.results.json", SearchOption.AllDirectories));
        foreach (var register in new[] { "exceptions.json", "tailoring.json", "measures.json" })
            Assert.True(File.Exists(Path.Combine(plant, register)), register);
        // O5 je Rechner, O11 Konsolenzeile, O15 Übergabeblatt
        Assert.Equal(6, Directory.GetFiles(folder, "Riegel-Report_*_*-*.pdf").Count(f => Path.GetFileName(f).Count(c => c == '_') == 3));
        Assert.Contains("RIEGEL ", File.ReadAllText(entries.Single(e => e.Output == "O11").Path!), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(entries.Single(e => e.Output == "O15").Path!, "UEBERGABE.txt")));

        // verify reproduziert; compare-snapshot mit dem gepackten Export desselben Rechners ist konsistent
        var zip = entries.Single(e => e.Output == "O9").Path!;
        var verify = CliApp.Run("verify", zip);
        Assert.True(verify.Exit == 0, verify.ToString());
        var snapshot = Directory.GetFiles(Path.Combine(plant, "snapshots", "HMI-L3-02"), "*.snapshot.json").Order(StringComparer.Ordinal).Last();
        var sample = CliApp.Run("compare-snapshot", snapshot, "--evidence", zip, "--host", "HMI-L3-02");
        Assert.True(sample.Exit == 0, sample.ToString());
    }
}
#endif
