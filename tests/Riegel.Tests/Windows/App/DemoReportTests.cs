// SPDX-License-Identifier: MIT
using Riegel.Demo;
using Riegel.Platform.Windows;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Windows.App;

/// <summary>
/// <c>Riegel.exe --demo-report &lt;Ordner&gt;</c> (spec 7.9): jedes Dokument aus spec 5 von der Demo-Anlage über die echten
/// Dienste – dieselben Dateien, die der CI-Schritt „Demo-Bericht“ erwartet.
/// </summary>
[Collection(AppCollection.Name)]
public sealed class DemoReportTests
{
    private static readonly string[] Expected =
    [
        "Riegel-Report_*.pdf", "Riegel-Summary_*.pdf", "Riegel-Overview_*.xlsx", "results.csv", "Riegel-Evidence_*.zip",
        "Riegel-Soll-Konfiguration_*.pdf", "Riegel-Soll-Konfiguration_*.xlsx", "Riegel-Ausnahmen-zur-Genehmigung_*.pdf",
        "Riegel-Erhebungsprotokoll_*.pdf", "riegel-remediation.reg", "riegel-rollback.reg", "GptTmpl.inf", "audit.csv",
    ];

    [Fact]
    public void Schreibt_jedes_Dokument()
    {
        using var temp = new TempFolder();
        var output = new StringWriter();
        var error = new StringWriter();
        int code = DemoReport.Run(temp.Path, PlatformFactory.Create(), output, error);
        Assert.True(code == 0, error.ToString());
        foreach (var pattern in Expected)
            Assert.True(Directory.EnumerateFiles(temp.Path, pattern, SearchOption.AllDirectories).Any(), pattern + " fehlt");
        // Anlagenordner (O1/O2/O8), Prüfung der Befehlszeile (O11) und Übersicht der Dokumente
        Assert.True(File.Exists(Path.Combine(temp.Path, "plant", "riegel-plant.json")));
        Assert.True(File.Exists(Path.Combine(temp.Path, "check.txt")));
        var manifest = File.ReadAllText(Path.Combine(temp.Path, "demo-report.txt"));
        Assert.Contains("O4/O5", manifest);
        Assert.Contains("O14", manifest);
    }
}
