// SPDX-License-Identifier: MIT
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Fields;
using MigraDoc.DocumentObjectModel.Tables;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Reports.Pdf;
using Riegel.Core.Samples;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Reports.Pdf.PdfTest;

namespace Riegel.Tests.Reports.Pdf;

/// <summary>Übersicht für die Geschäftsführung (O6), Soll-Konfiguration (O13), Genehmigungsblatt (O12), Erhebungsprotokoll (O14).</summary>
public sealed class DocumentPdfTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    // ---- O6 ----------------------------------------------------------------------------------------------

    /// <summary>Zwölf Anlagen mit langen Namen, allen Bannern und fünf Top-Befunden – der schwerste Fall für eine Seite.</summary>
    private static SummaryData TwelvePlants(string language)
    {
        var sample = SampleData.Summary();
        var rows = Enumerable.Range(1, SummaryLayout.PlantsPerPage).Select(i => sample.Plants[i % sample.Plants.Count] with
        {
            PlantId = $"werk-{i:D2}",
            PlantName = $"Musterwerk Süd – Abfüllung und Verpackung Linie {i:D2}",
            Incomplete = i % 3 == 0,
        }).ToList();
        var report = SampleData.Report();
        var top = Enumerable.Range(1, 5).Select(i => report.Findings[0] with { RuleId = $"NET-{i:D2}", Title = "SMBv1-Server deaktiviert und nicht mehr erreichbar" }).ToList();
        return sample with { Language = language, Plants = rows, TopFindings = top, Banners = [.. report.Banners.Take(3)], PreTranslated = language is "es" or "fr" or "zh-Hans" };
    }

    [PdfTheory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("zh-Hans")]
    public void Übersicht_passt_für_zwölf_Anlagen_auf_genau_eine_A4_Seite(string language)
    {
        var w = Writer();
        var path = w.WriteSummary(TwelvePlants(language), new PdfOptions(_temp.Sub(language)));
        Assert.Equal("Riegel-Summary_2026-10-18.pdf", Path.GetFileName(path));
        Assert.Equal(1, PageCount(path));
        Assert.Equal(1, PageCount(w.WriteSummary(SampleData.Summary() with { Language = language }, new PdfOptions(_temp.Sub(language, "beispiel")))));
        var pages = Pages(w.BuildSummary(TwelvePlants(language)));
        Assert.Single(pages);
    }

    [PdfFact]
    public void Übersicht_zeigt_je_Anlage_Verlauf_Werte_und_die_fünf_häufigsten_hohen_Befunde()
    {
        var built = Writer().BuildSummary(SampleData.Summary());
        var text = Text(built);
        Assert.Contains("Härtungsstand – Übersicht für die Geschäftsführung", text);
        Assert.Contains("Offen hoch je Monatsende 2026-06 … 2026-09", text);
        Assert.Contains("9 · 8 · 7 · 5", text);
        Assert.Contains("– · 14 · 12 · 11", text);
        Assert.Contains("Labor Nord (fiktiv)", text);
        Assert.Contains("Alle Anlagen", text);
        Assert.Contains("Die häufigsten hohen Befunde über alle Anlagen", text);
        Assert.Contains("Unvollständige Prüfung: nur 62 % prüfbar", text);
        Assert.Contains("Übersicht 2026-10-18 · Seite ", PdfDom.Text(Section(built).Footers.FirstPage));
        // Grau, wenn die Anlage unvollständig geprüft ist
        var table = Section(built).Elements.OfType<Table>().First(t => RowText(t.Rows[0]).StartsWith("Anlage"));
        var row = table.Rows.Cast<Row>().Single(r => PdfDom.Text(r.Cells[0]).StartsWith("Musterwerk – Linie 3"));
        var value = row.Cells[8].Elements.OfType<Paragraph>().First();
        Assert.Equal(PdfColors.Rgb(PdfColors.Greyed), value.Format.Font.Color);
    }

    // ---- O13 ---------------------------------------------------------------------------------------------

    [PdfFact]
    public void Soll_Konfiguration_je_Gruppe_mit_Sollwerten_Herkunft_Zuschnitt_und_Freigabe()
    {
        var data = SampleData.TargetConfig();
        var w = Writer();
        var path = w.WriteTargetConfig(data, new PdfOptions(_temp.Path));
        Assert.Equal("Riegel-Soll-Konfiguration_Musterwerk-Linie-3_2026-10-18.pdf", Path.GetFileName(path));
        var built = w.BuildTargetConfig(data);
        var text = Text(built);
        Assert.Contains("Soll-Konfiguration", text);
        Assert.Contains("Gruppe Bedienstationen (Rolle: Bedienstation (HMI))", PdfDom.Headings(built.Document));
        Assert.Contains("Gruppe Engineering (Rolle: Engineering-Station)", PdfDom.Headings(built.Document));
        Assert.Contains("RequireSecuritySignature = 1", text);
        Assert.Contains("TCP 4840 only (customer requirement)", text);
        Assert.Contains("domain-joined machines only", text);
        Assert.Contains("auch in MS-Baseline", text);
        Assert.Contains("Herkunft der Sollwerte", text);
        Assert.Contains("belegt durch ACSC (CC BY 4.0)\n\t61", text);
        Assert.Contains("TL-0001", text);
        Assert.Contains("SHA-256 " + data.Catalogue.Sha256, text);
        Assert.Contains("SHA-256 " + data.TailoringSha256, text);
        Assert.Contains("Diese Freigabe umfasst das Regelwerk 2026.10 (22222222), den Zuschnitt 44444444 und Gruppen: 2.", text);
        Assert.Contains("Freigegeben · Rolle: Qualitätssicherung", text);
        Assert.Contains("keine CIS- oder BSI-Inhalte", text);
        var problems = Problems(built);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ---- O12 ---------------------------------------------------------------------------------------------

    [PdfFact]
    public void Genehmigungsblatt_mit_Inhalts_Hash_Register_Prüfsummen_und_Unterschrift_je_Genehmiger()
    {
        var data = SampleData.ApprovalSheet();
        var w = Writer();
        var path = w.WriteApprovalSheet(data, new PdfOptions(_temp.Path));
        Assert.Equal("Riegel-Ausnahmen-zur-Genehmigung_Musterwerk-Linie-3_2026-10-18.pdf", Path.GetFileName(path));
        var built = w.BuildApprovalSheet(data);
        var text = Text(built);
        Assert.Contains("Ausnahmen zur Genehmigung", text);
        foreach (var e in data.Entries)
        {
            Assert.Contains(e.Id, text);
            Assert.Contains(e.ContentSha256, text);
            Assert.Contains(e.Text, text.Replace("\n", " "));
        }
        Assert.Contains("SHA-256 " + data.ExceptionsSha256, text);
        Assert.Contains("SHA-256 " + data.TailoringSha256, text);
        Assert.Contains("Zugriff auf Port 445 nur aus dem Leitstandnetz", text);
        Assert.Contains("M-0003", text);
        Assert.Contains("[ ] genehmigt · [ ] abgelehnt", text);
        Assert.Contains("„Freigabeblatt FB-12 vom 2026-10-18“", text.Replace("\n", " "));
        Assert.Contains("Blatt FB-12 bezieht sich auf das Ausnahmeregister 33333333 und den Zuschnitt 44444444 (Einträge auf diesem Blatt: 3).", text);
        Assert.Contains("Freigegeben · Rolle: Informationssicherheitsbeauftragter", text);
        Assert.Contains("Freigegeben · Rolle: Qualitätssicherung", text);
        Assert.Contains("Freigabeblatt FB-12 · Seite ", PdfDom.Text(Section(built).Footers.Primary));
        var problems = Problems(built);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // ---- O14 ---------------------------------------------------------------------------------------------

    [PdfFact]
    public void Erhebungsprotokoll_mit_Prüfsummen_Zeuge_Integritätsaussage_und_Unterschriften()
    {
        var data = SampleData.CollectionRecord();
        var w = Writer();
        var path = w.WriteCollectionRecord(data, new PdfOptions(_temp.Path));
        Assert.Equal("Riegel-Erhebungsprotokoll_Musterwerk-Linie-3_20261017T120000Z-1a2b3c.pdf", Path.GetFileName(path));
        var built = w.BuildCollectionRecord(data);
        var text = Text(built);
        foreach (var f in data.Import.Files)
        {
            Assert.Contains(f.Sha256, text);
            Assert.Contains(f.FileName, text);
            Assert.Contains(f.HostName, text);
        }
        Assert.Contains("Servicetechniker", text);
        Assert.Contains("Anlagenverantwortlicher (Zeuge)", text);
        Assert.Contains("passt", text);
        Assert.Contains("fehlt oder passt nicht", text);
        Assert.Contains("minimal, pseudonymisiert", text);
        Assert.Contains("Bordmittel", text);
        Assert.Contains("Die SHA-256-Prüfsummen zeigen, dass eine Datei seit dem Erzeugen der Prüfsumme nicht verändert wurde.", text);
        Assert.Contains("Erhoben von · Rolle: Servicetechniker", text);
        Assert.Contains("Zeuge (Betreiber) · Rolle: Anlagenverantwortlicher (Zeuge)", text);
        var problems = Problems(built);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [PdfFact]
    public void Kurze_Dokumente_haben_die_Fusszeile_mit_Seitenzahlen_auf_jeder_Seite()
    {
        var w = Writer();
        foreach (var built in new[] { w.BuildSummary(SampleData.Summary()), w.BuildTargetConfig(SampleData.TargetConfig()),
                     w.BuildApprovalSheet(SampleData.ApprovalSheet()), w.BuildCollectionRecord(SampleData.CollectionRecord()) })
        {
            var section = Section(built);
            foreach (var footer in new[] { section.Footers.Primary, section.Footers.FirstPage })
            {
                Assert.StartsWith("Riegel 0.1.0 · ", PdfDom.Text(footer));
                Assert.Contains("Vertraulich – enthält Sicherheitskonfiguration", PdfDom.Text(footer));
                var elements = footer.Elements.OfType<Paragraph>().First().Elements.Cast<DocumentObject>().ToList();
                Assert.Contains(elements, e => e is PageField);
                Assert.Contains(elements, e => e is NumPagesField);
            }
        }
    }

    [PdfFact]
    public void Leere_Dokumente_rendern()
    {
        var w = Writer();
        var summary = SampleData.Summary() with { Plants = [], TopFindings = [], Banners = [] };
        var target = SampleData.TargetConfig() with { Groups = [], Baselines = [] };
        var sheet = SampleData.ApprovalSheet() with { Entries = [], Approvers = [] };
        var record = SampleData.CollectionRecord();
        record = record with { Import = record.Import with { Files = [] } };
        Assert.Equal(1, PageCount(w.WriteSummary(summary, new PdfOptions(_temp.Sub("s")))));
        Assert.True(PageCount(w.WriteTargetConfig(target, new PdfOptions(_temp.Sub("t")))) >= 1);
        Assert.True(PageCount(w.WriteApprovalSheet(sheet, new PdfOptions(_temp.Sub("a")))) >= 1);
        Assert.True(PageCount(w.WriteCollectionRecord(record, new PdfOptions(_temp.Sub("r")))) >= 1);
        Assert.Contains(S("ApprovalDoc_NoEntries"), Text(w.BuildApprovalSheet(sheet)));
        // Ohne Genehmiger: die Freigabe-Rolle der Voreinstellung
        Assert.Contains("Freigegeben · Rolle: Betriebsleitung", Text(w.BuildApprovalSheet(sheet)));
    }
}
