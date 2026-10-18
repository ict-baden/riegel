// SPDX-License-Identifier: MIT
using Riegel.Tests.Cli;
using CliApp = Riegel.Tests.Cli.Cli;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.EndToEnd;

/// <summary>
/// Ende zu Ende über die Befehlszeile mit den echten Diensten, Katalog und Engine (spec 8.15), auf beiden
/// Zielplattformen (auch Linux): Anlagenordner aus synthetischen Exporten, Import mit Erhebungsprotokoll, Freigabe,
/// Ausnahmen-Vorlage, alle Dokumente, Nachweispaket, verify reproduziert, compare-snapshot ist konsistent.
/// </summary>
public sealed class CliEndToEndTests
{
    private static readonly string Hmi = RepoPaths.Synthetic("win10-ltsc2019-hmi");
    private static readonly string Member = RepoPaths.Synthetic("server2022-domain-member");

    private static void Ok(CliResult r) => Assert.True(r.Exit == 0, r.ToString());

    private static string Single(string folder, string pattern)
    {
        var files = Directory.GetFiles(folder, pattern, SearchOption.AllDirectories);
        Assert.True(files.Length == 1, $"{pattern}: {files.Length} Dateien in {folder}");
        return files[0];
    }

    [Fact]
    public void Anlage_Berichte_Nachweispaket_verify_und_Stichprobe()
    {
        using var temp = new TempFolder();
        var plant = temp.Sub("anlage");
        var outDir = temp.Sub("ausgabe");
        Ok(CliApp.Run("plant", "init", plant, "--name", "E2E Linie 7"));

        // Import mit Rollen und Erhebungsprotokoll (O14); Exitcode 0–2 ist ein Ergebnis
        var import = CliApp.Run("import", Hmi, Member, "--plant", plant, "--collected-by-role", "Servicetechniker",
            "--witness-role", "Anlagenverantwortlicher", "--record");
        Assert.True(import.Exit is >= 0 and <= 2, import.ToString());
        Assert.Equal(2, Directory.GetFiles(Path.Combine(plant, "runs"), "*.results.json", SearchOption.AllDirectories).Length);

        // Freigegebener Stand und Ausnahmen aus der Vorlage des Maschinenbauers (beantragt)
        Ok(CliApp.Run("approve", "--plant", plant, "--machine", "HMI-LTSC19", "--role", "Qualitätssicherung", "--record", "CHG-E2E-0001"));
        Ok(CliApp.Run("exceptions", "import-template",
            Path.Combine(RepoPaths.Root, "templates", "exceptions", "beispiel-anlagenbau.exceptions-template.json"), "--plant", plant));

        // Bericht (O4/O5), Excel (O7), CSV (O3), Ergebnisse (O2) und Nachweispaket (O9)
        Ok(CliApp.Run("report", "--plant", plant, "--format", "pdf,xlsx,csv,json", "--per-machine", "--evidence", "--out", outDir));
        Single(outDir, "Riegel-Report_E2E-Linie-7_*_HMI-LTSC19.pdf");
        Single(outDir, "Riegel-Overview_E2E-Linie-7_*.xlsx");
        Single(outDir, "results.csv");
        var zip = Single(outDir, "Riegel-Evidence_E2E-Linie-7_*.zip");

        // verify reproduziert mit dieser Engine (Exitcode 0), die Stichprobe mit demselben Export ist konsistent
        var verify = CliApp.Run("verify", zip);
        Assert.True(verify.Exit == 0, verify.ToString());
        var sample = CliApp.Run("compare-snapshot", Hmi, "--evidence", zip);
        Assert.True(sample.Exit == 0, sample.ToString());

        // Soll-Konfiguration (O13), Übersicht (O6), Erhebungsprotokoll (O14), Genehmigungsblatt (O12), Abhilfe (O10)
        Ok(CliApp.Run("target-config", "--plant", plant, "--out", outDir));
        Single(outDir, "Riegel-Soll-Konfiguration_E2E-Linie-7_*.pdf");
        Single(outDir, "Riegel-Soll-Konfiguration_E2E-Linie-7_*.xlsx");
        Ok(CliApp.Run("summary", "--plant", plant, "--out", outDir));
        Single(outDir, "Riegel-Summary_*.pdf");
        Ok(CliApp.Run("collection-record", "--plant", plant, "--latest", "--out", outDir));
        Single(outDir, "Riegel-Erhebungsprotokoll_E2E-Linie-7_*.pdf");
        Ok(CliApp.Run("exceptions", "approval-sheet", "--plant", plant, "--pending", "--out", outDir));
        Single(outDir, "Riegel-Ausnahmen-zur-Genehmigung_E2E-Linie-7_*.pdf");
        var remediation = temp.Sub("abhilfe");
        Ok(CliApp.Run("remediate", "--plant", plant, "--machine", "HMI-LTSC19", "--out", remediation));
        foreach (var name in new[] { "riegel-remediation.reg", "riegel-rollback.reg", "GptTmpl.inf", "audit.csv", "README.txt" })
            Single(remediation, name);

        // Icinga-Zeile (O11) gegen den gespeicherten Lauf
        var brief = CliApp.Run("check", "--plant", plant, "--snapshot", Hmi, "--no-store", "--brief");
        Assert.True(brief.Exit is >= 0 and <= 2, brief.ToString());
        Assert.StartsWith("RIEGEL ", brief.Out, StringComparison.Ordinal);
    }
}
