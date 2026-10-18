// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Riegel.Core.Localization;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Reports.Pdf;
using Riegel.Core.Resources;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Reports.Pdf;

/// <summary>Texte, Fehlercodes und Quelltext des Pakets pdf (Richtlinie 13, spec 7.10, 7.12, 7.13).</summary>
public sealed partial class PdfStringsTests
{
    private static string PdfFolder => Path.Combine(RepoPaths.Src, "Riegel.Core", "Reports", "Pdf");

    private static string Code() => string.Join("\n", Directory.EnumerateFiles(PdfFolder, "*.cs").Select(File.ReadAllText));

    private static Dictionary<string, string> Strings(string language) =>
        XDocument.Load(Path.Combine(RepoPaths.Src, "Riegel.Core", "Resources", language == "en" ? "PdfStrings.resx" : $"PdfStrings.{language}.resx"))
            .Root!.Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    [GeneratedRegex("""\b(?:T|F)\("([A-Za-z0-9_]+)"[,)]""")]
    private static partial Regex Lookup();

    [Fact]
    public void Jeder_Text_den_der_Code_nachschlägt_steht_in_allen_fünf_Sprachen()
    {
        var used = Lookup().Matches(Code()).Select(m => m.Groups[1].Value).Where(k => !k.EndsWith('_')).ToHashSet(StringComparer.Ordinal);
        Assert.True(used.Count > 200, $"nur {used.Count} Schlüssel gefunden – Muster prüfen");
        foreach (var language in UiLanguage.Supported)
        {
            var strings = Strings(language);
            var missing = used.Where(k => !strings.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"{language}: fehlt {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void Zusammengesetzte_Schlüssel_gibt_es_für_jeden_Enum_Wert()
    {
        var t = new PdfTexts("en");
        foreach (var state in Enum.GetValues<RegisterEntryState>()) Assert.DoesNotContain("[", t.State(state));
        foreach (var source in Enum.GetValues<ValueSource>()) Assert.DoesNotContain("[", t.Source(source));
        foreach (var origin in Enum.GetValues<SnapshotOrigin>()) Assert.DoesNotContain("[", t.Origin(origin));
        foreach (var role in Enum.GetValues<MachineRole>()) Assert.DoesNotContain("[", t.Role(role));
        foreach (var kind in Enum.GetValues<StatusChangeKind>()) Assert.DoesNotContain("[", t.Change(kind));
        foreach (var basis in Enum.GetValues<ComparisonBasis>()) Assert.DoesNotContain("[", t.Basis(basis));
        foreach (var drift in Enum.GetValues<DriftKind>()) Assert.DoesNotContain("[", t.Drift(drift));
        foreach (var status in Enum.GetValues<ResultStatus>()) Assert.DoesNotContain("[", t.T("Legend_" + status));
        foreach (var state in Enum.GetValues<MeasureState>()) Assert.DoesNotContain("[", t.T("MeasureState_" + state));
        for (int role = 0; role <= 5; role++) Assert.DoesNotContain("[", t.DomainRole(role));
        foreach (var step in new[] { SignOffSteps.Created, SignOffSteps.Reviewed, SignOffSteps.Approved, SignOffSteps.Accepted })
            Assert.NotEqual(step, t.Step(step));
    }

    [Fact]
    public void Fehlercodes_liegen_im_Bereich_und_haben_Texte_in_fünf_Sprachen()
    {
        Assert.NotEmpty(RglCodesPdf.All);
        Assert.Equal(RglCodesPdf.All.Count, RglCodesPdf.All.Distinct().Count());
        foreach (var code in RglCodesPdf.All)
        {
            Assert.Matches(@"^RGL-\d{4}$", code);
            int number = int.Parse(code[4..], CultureInfo.InvariantCulture);
            Assert.InRange(number, RglCodesPdf.First, RglCodesPdf.Last);
            foreach (var language in UiLanguage.Supported)
            {
                var text = RglCodesPdf.Message(CultureInfo.GetCultureInfo(language), code, "a", "b", "c");
                Assert.False(string.IsNullOrWhiteSpace(text));
                Assert.NotEqual(code, text);
            }
        }
        Assert.Equal("RGL-0510", RglCodesPdf.NoFontForLanguage);
        // Jeder Code, den der Code benutzt, ist in All
        foreach (Match m in Regex.Matches(Code(), @"RglCodesPdf\.(\w+)"))
            if (m.Groups[1].Value is not ("All" or "Message" or "First" or "Last"))
                Assert.Contains((string)typeof(RglCodesPdf).GetField(m.Groups[1].Value)!.GetValue(null)!, RglCodesPdf.All);
    }

    [Fact]
    public void Unsichere_Vorübersetzungen_sind_markiert_und_Deutsch_ist_vollständig()
    {
        var english = Strings("en");
        var german = Strings("de");
        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), german.Keys.Order(StringComparer.Ordinal));
        // Deutsch ist geprüft: nichts ist dort nur englisch geblieben (Ausnahmen: Fachwörter und reine Platzhalter)
        string[] same = ["Percent", "Scores_Value", "Appendix_Machine", "Appendix_MachineLabel", "Source_Mdm", "Legend_Status", "Col_Baseline",
            "Footer_MachineReport", "SignOff_Name", "Role_Server", "Role_Historian", "Option_Minimal", "Machine_Support", "Machine_Admin",
            "Machine_ImportId", "RecordDoc_Import", "Exc_Reference", "Cover_Registers", "Machine_Admin", "Col_Licence", "Col_Source"];
        var untranslated = english.Where(p => german[p.Key] == p.Value && !same.Contains(p.Key) && p.Value.Any(char.IsLetter)).Select(p => p.Key).ToList();
        Assert.True(untranslated.Count == 0, "Deutsch fehlt: " + string.Join(", ", untranslated));
        // es/fr/zh-Hans: jede Datei hat Einträge mit TODO-übersetzen (vor dem ersten öffentlichen Release gegenlesen)
        foreach (var language in new[] { "es", "fr", "zh-Hans" })
        {
            var doc = XDocument.Load(Path.Combine(RepoPaths.Src, "Riegel.Core", "Resources", $"PdfStrings.{language}.resx"));
            Assert.Contains(doc.Root!.Elements("data"), d => (string?)d.Element("comment") == "TODO-übersetzen");
        }
    }

    [Fact]
    public void Paket_pdf_startet_nichts_und_öffnet_keine_Verbindung()
    {
        string[] forbidden =
        [
            @"\bProcess\b", @"ProcessStartInfo", @"System\.Diagnostics\.Process", @"\bHttpClient\b", @"\bWebRequest\b", @"\bWebClient\b",
            @"\bSocket\b", @"\bTcpClient\b", @"\bUdpClient\b", @"\bDns\b", @"System\.Net\b", @"\bShellExecute\b", @"Registry\.",
        ];
        var findings = new List<string>();
        foreach (var file in Directory.EnumerateFiles(PdfFolder, "*.cs"))
        {
            var code = Regex.Replace(File.ReadAllText(file), @"//.*$|/\*.*?\*/", "", RegexOptions.Multiline | RegexOptions.Singleline);
            foreach (var pattern in forbidden)
                foreach (Match m in Regex.Matches(code, pattern))
                    findings.Add($"{Path.GetFileName(file)}: {m.Value}");
        }
        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }

    [Fact]
    public void Kein_Text_fest_im_Code_der_Dokumente()
    {
        // Sichtbare Texte kommen aus PdfStrings/CommonStrings; im Code stehen nur Daten, Trennzeichen und Formate.
        // Grobe Prüfung: keine deutschen Wörter mit Umlaut in Zeichenketten der Layout-Dateien.
        var findings = new List<string>();
        foreach (var file in Directory.EnumerateFiles(PdfFolder, "*Layout.cs"))
        {
            var code = Regex.Replace(File.ReadAllText(file), @"//.*$|/// .*$", "", RegexOptions.Multiline);
            foreach (Match m in Regex.Matches(code, "\"[^\"\\n]*[äöüÄÖÜß][^\"\\n]*\""))
                findings.Add($"{Path.GetFileName(file)}: {m.Value}");
        }
        Assert.True(findings.Count == 0, string.Join("\n", findings));
        Assert.Equal("Hardening evidence report", PdfStrings.ResourceManager.GetString("Report_Title_Audit", CultureInfo.InvariantCulture));
    }
}
