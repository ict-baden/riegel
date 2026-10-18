// SPDX-License-Identifier: MIT
using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Fields;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;
using PdfSharp.Pdf.IO;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Reports.Pdf;
using Riegel.Core.Samples;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Reports.Pdf.PdfTest;

namespace Riegel.Tests.Reports.Pdf;

/// <summary>Anlagenbericht (O4) und Rechnerbericht (O5): Aufbau, Inhalt, Seitenumbruch und die Datei (spec 5).</summary>
public sealed class ReportPdfTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static ReportData Data(string language = "de")
    {
        var r = SampleData.Report();
        return language == "de" ? r : r with { Header = r.Header with { Language = language, PreTranslated = language is "es" or "fr" or "zh-Hans" } };
    }

    [PdfFact]
    public void Datei_ist_ein_PDF_mit_Metadaten_und_dem_Namen_aus_spec_5()
    {
        var path = Writer().WriteReport(Data(), new PdfOptions(_temp.Path));

        Assert.Equal("Riegel-Report_Musterwerk-Linie-3_2026-10-18.pdf", Path.GetFileName(path));
        var head = new byte[5];
        using (var stream = File.OpenRead(path)) stream.ReadExactly(head);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(head));
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount > 5);
        Assert.Equal("Härtungsnachweis", pdf.Info.Title);
        Assert.Equal("Riegel", pdf.Info.Author);
        Assert.Equal(SampleData.PlantName, pdf.Info.Subject);
        Assert.Equal("RPT-20261018T090500Z", pdf.Info.Keywords);
        Assert.StartsWith("Riegel ", pdf.Info.Creator);
        // Nur die Datei selbst, keine temporären Reste
        Assert.Equal([path], Directory.GetFiles(_temp.Path));
    }

    [PdfFact]
    public void Abschnitte_in_der_Reihenfolge_aus_spec_5_Verschlechtert_vor_den_Befunden()
    {
        var headings = PdfDom.Headings(Writer().BuildReport(Data()).Document);
        Assert.Equal(
        [
            S("Summary_Heading"), S("Changes_Heading"), S("Findings_Heading"), S("Exceptions_Heading"), S("Tailoring_Heading"),
            S("Measures_Heading"), S("Machines_Heading"), S("Method_Heading"), S("SignOff_Heading"), S("Appendix_Heading"),
        ], headings);
        Assert.Equal("Verschlechtert seit dem letzten Lauf / seit der Freigabe", headings[1]);
        Assert.True(headings.IndexOf(S("Changes_Heading")) < headings.IndexOf(S("Findings_Heading")));
    }

    [PdfFact]
    public void Fusszeile_auf_jeder_Seite_mit_Version_Bericht_Seite_x_von_y_und_Vertraulichkeit()
    {
        var built = Writer().BuildReport(Data());
        var section = Section(built);
        Assert.Equal(PageFormat.A4, section.PageSetup.PageFormat);
        Assert.True(section.PageSetup.DifferentFirstPageHeaderFooter);
        foreach (var footer in new[] { section.Footers.Primary, section.Footers.FirstPage })
        {
            var text = PdfDom.Text(footer);
            Assert.Contains("Riegel 0.1.0 · Bericht RPT-20261018T090500Z · Seite ", text);
            Assert.Contains("Vertraulich – enthält Sicherheitskonfiguration", text);
            var elements = footer.Elements.OfType<Paragraph>().First().Elements.Cast<DocumentObject>().ToList();
            Assert.Contains(elements, e => e is PageField);
            Assert.Contains(elements, e => e is NumPagesField);
            // Deutsch ist geprüft: keine Zeile zur Vorübersetzung
            Assert.DoesNotContain("Vorübersetzung", text);
        }
        // Kopfzeile ab Seite 2 mit Titel und Anlage
        Assert.Contains("Härtungsnachweis", PdfDom.Text(section.Headers.Primary));
        Assert.Contains(SampleData.PlantName, PdfDom.Text(section.Headers.Primary));
    }

    [PdfTheory]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("zh-Hans")]
    public void Vorübersetzte_Sprachen_tragen_den_Vermerk_in_jeder_Fusszeile_und_auf_dem_Deckblatt(string language)
    {
        var built = Writer().BuildReport(Data(language));
        if (built.FallbackFrom is not null) return; // ohne CJK-Schrift Englisch – eigener Test
        var section = Section(built);
        var local = new PdfTexts(language).Common("PreTranslation_Note");
        foreach (var footer in new[] { section.Footers.Primary, section.Footers.FirstPage })
        {
            var text = PdfDom.Text(footer);
            Assert.Contains(local, text);
            Assert.Contains("Machine pre-translation – not reviewed", text);
        }
        // Banner auf dem Deckblatt (vor dem Inhaltsverzeichnis)
        var all = PdfDom.Text(built.Document);
        int banner = all.IndexOf(local + " / Machine pre-translation – not reviewed\n", StringComparison.Ordinal);
        Assert.True(banner >= 0 && banner < all.IndexOf(new PdfTexts(language).T("Contents") + "\n", StringComparison.Ordinal));
    }

    [PdfTheory]
    [InlineData("en")]
    [InlineData("de")]
    public void Geprüfte_Sprachen_ohne_Vorübersetzungsvermerk_auch_wenn_die_Daten_ein_Banner_haben(string language)
    {
        // SampleData enthält das Banner PreTranslation (jede Art einmal); es zählt nur die tatsächliche Sprache
        var text = Text(Writer().BuildReport(Data(language)));
        Assert.DoesNotContain("Machine pre-translation", text);
        Assert.DoesNotContain("Vorübersetzung", text);
    }

    [PdfFact]
    public void Banner_erscheinen_genau_dann_wenn_die_Daten_sie_enthalten()
    {
        var data = Data();
        var text = Text(Writer().BuildReport(data));
        Assert.Contains("Unvollständige Prüfung: nur 62 % prüfbar", text);
        Assert.Contains("Veraltete Erfassung: Stand der Erfassung vor 76 Tagen", text);
        Assert.Contains("Regelwerk älter als 6 Monate – Lebenszyklus- und Patchangaben können veraltet sein", text);
        Assert.Contains("Quelle: Domänen-GPO (nicht in echter Domäne getestet)", text);
        Assert.Contains("Uhr unplausibel", text);
        Assert.Contains(S("Banner_DomainController"), text);
        Assert.Contains("Betroffene Rechner: HIST-01", text);

        var none = Text(Writer().BuildReport(data with { Banners = [] }));
        foreach (var key in new[] { "Banner_StaleCatalogue", "Banner_ExperimentalGpo", "Banner_ClockSuspect", "Banner_DomainController" })
            Assert.DoesNotContain(S(key), none);
        Assert.DoesNotContain("Unvollständige Prüfung: nur 62 % prüfbar", none);
        Assert.DoesNotContain("Veraltete Erfassung:", none);
    }

    [PdfFact]
    public void Deckblatt_mit_Durchgeführt_von_Prüfsummen_Vertraulichkeit_und_Einordnung()
    {
        var data = Data();
        var text = Text(Writer().BuildReport(data));
        var h = data.Header;
        Assert.Contains("Durchgeführt von", text);
        Assert.Contains("Beispiel-Dienstleister (fiktiv)", text);
        Assert.Contains(h.Tool.ExeSha256!, text);
        Assert.Contains("Riegel 0.1.0 (Commit abc1234)", text);
        Assert.Contains("SHA-256 " + h.Catalogue.Sha256, text);
        Assert.Contains(h.Registers.Exceptions, text);
        Assert.Contains(h.Registers.Tailoring, text);
        Assert.Contains(h.Registers.Measures, text);
        Assert.Contains(SampleData.Baseline.SourceSha256, text);
        Assert.Contains("Vertraulich – enthält Sicherheitskonfiguration", text);
        Assert.Contains("Einordnung", text);
        Assert.Contains("Riegel unterstützt Nachweise für:", text);
        Assert.Contains("ISO/IEC 27001:2022 A.8.9", text);
        Assert.Contains("keine Konformitätsaussage", text);

        // Das Deckblatt bleibt eine Seite: die zweite Seite beginnt mit dem Inhaltsverzeichnis
        var pages = Pages(Writer().BuildReport(data));
        Assert.Equal(2, PageOf(pages, "Inhalt"));
    }

    [PdfFact]
    public void Zusammenfassung_für_die_Leitung_ist_genau_eine_Seite_mit_Zahlen_zuerst()
    {
        var built = Writer().BuildReport(Data());
        var pages = Pages(built);
        int summary = PageOf(pages, S("Summary_Heading"));
        int changes = PageOf(pages, S("Changes_Heading"));
        Assert.True(summary > 0);
        Assert.Equal(summary + 1, changes);

        // Zählungen vor den Prozentwerten: die erste Tabelle nach der Überschrift sind die Zahlenkacheln
        var elements = Section(built).Elements.Cast<DocumentObject>().ToList();
        int heading = elements.FindIndex(e => e is Paragraph p && p.Style == StyleNames.Heading1 && PdfDom.Text(p).Trim() == S("Summary_Heading"));
        var firstTable = (Table)elements.Skip(heading).First(e => e is Table);
        var chips = PdfDom.Text(firstTable);
        Assert.Contains("offen – hoch", chips);
        Assert.StartsWith("6", chips.TrimStart());
        var text = Text(built);
        Assert.True(text.IndexOf("offen – hoch", StringComparison.Ordinal) < text.IndexOf("Härtungsgrad\n", StringComparison.Ordinal));
    }

    [PdfFact]
    public void Wert_grau_bei_unvollständiger_Prüfung_sonst_nicht()
    {
        var data = Data();
        var greyed = Text(Writer().BuildReport(data));
        Assert.Contains("Unvollständige Prüfung: nur 87 % prüfbar – die Werte sind nicht belastbar und grau dargestellt.", greyed);

        var complete = data with { Score = data.Score with { Greyed = false, CheckedShare = 97 } };
        Assert.DoesNotContain("die Werte sind nicht belastbar", Text(Writer().BuildReport(complete)));
    }

    [PdfFact]
    public void Kategoriewerte_mit_Fussnote_zum_Audit_Deckel()
    {
        var text = Text(Writer().BuildReport(Data()));
        Assert.Contains("Überwachung / Protokollierung ¹", text);
        Assert.Contains("¹ " + S("Score_AuditCapFootnote"), text);

        var data = Data();
        var uncapped = data with { Score = data.Score with { AuditCapped = false }, Categories = [.. data.Categories.Select(c => c with { Capped = false })] };
        Assert.DoesNotContain(S("Score_AuditCapFootnote"), Text(Writer().BuildReport(uncapped)));
    }

    [PdfFact]
    public void Höchstens_zehn_Befunde_in_der_Zusammenfassung()
    {
        var data = Data();
        var many = Enumerable.Range(1, 15).Select(i => data.Findings[0] with { RuleId = $"NET-{i:D2}", Title = $"Regel {i:D2}" }).ToList();
        var text = Text(Writer().BuildReport(data with { Findings = many }));
        Assert.Contains("Weitere Befunde: 5", text);
        var built = Writer().BuildReport(data with { Findings = many });
        var top = Section(built).Elements.OfType<Table>().First(t => RowText(t.Rows[0]).Contains("Offen auf"));
        Assert.Equal(11, top.Rows.Count); // Kopf + 10
        Assert.Contains("NET-01", text);
        Assert.Contains("1 von 4", text);
    }

    [PdfFact]
    public void Verschlechtert_und_abgelaufene_Ausnahmen_im_eigenen_Block()
    {
        var text = Text(Writer().BuildReport(Data()));
        int changes = text.IndexOf(S("Changes_Heading") + "\n", StringComparison.Ordinal);
        int expired = text.IndexOf(S("Expired_Heading"), changes, StringComparison.Ordinal);
        int findings = text.IndexOf(S("Findings_Heading") + "\n", changes, StringComparison.Ordinal);
        Assert.True(changes >= 0 && expired > changes && findings > expired);
        var block = text[changes..findings];
        Assert.Contains("NET-01 SMBv1-Server deaktiviert", block);
        Assert.Contains("Erfüllt → Nicht erfüllt", block);
        Assert.Contains("EX-0005", block);
        // Neue Regel und Prüfumfang sind keine Verschlechterung, sondern „Weitere Änderungen“
        int other = block.IndexOf(S("Changes_Other"), StringComparison.Ordinal);
        Assert.True(other > 0);
        Assert.Contains("neue Regel", block[other..]);
        Assert.Contains("Prüfumfang geändert", block[other..]);
    }

    [PdfFact]
    public void Offene_Befunde_je_Regel_mit_Rechnern_und_Maßnahme()
    {
        var text = Text(Writer().BuildReport(Data()));
        Assert.Contains("12 von 14", text);
        Assert.Contains("SRV-A01, SRV-A02, SRV-A03, SRV-A04, SRV-A05, SRV-A06, SRV-A07, SRV-A08, SRV-A09, OPS-01, …", text.Replace("\n", " "));
        Assert.Contains("M-0002 überfällig seit 2026-09-30", text.Replace("\n", " "));
        Assert.Contains("M-0001 fällig 2026-12-15", text.Replace("\n", " "));
    }

    [PdfFact]
    public void Register_Zuschnitt_und_Maßnahmenplan_mit_allen_Zuständen()
    {
        var text = Text(Writer().BuildReport(Data())).Replace("\n", " ");
        foreach (var id in new[] { "EX-0001", "EX-0002", "EX-0003", "EX-0004", "EX-0005", "EX-0006", "EX-0007", "EX-0008", "TL-0001", "TL-0002", "TL-0003", "M-0001", "M-0002", "M-0003", "M-0004" })
            Assert.Contains(id, text);
        foreach (var state in new[] { "gültig", "beantragt", "nach Freigabe geändert", "läuft bald ab", "abgelaufen", "ungültig" })
            Assert.Contains(state, text);
        Assert.Contains("Ausnahme nicht mehr nötig?", text);
        Assert.Contains("Wert weicht ab", text);
        Assert.Contains("Maßnahme erledigt, Regel weiter nicht erfüllt", text);
        Assert.Contains("freigegebenvonInformationssicherheitsbeauftragteram2026-04-20,NachweisCHG-DEMO-0001,prüfenbis2027-04-20", text.Replace(" ", ""));
        Assert.Contains("Register 33333333, Einträge: 8", text);
    }

    [PdfFact]
    public void Rechnerübersicht_Domänencontroller_nicht_bewertet()
    {
        var built = Writer().BuildReport(Data());
        var table = Section(built).Elements.OfType<Table>().First(t => RowText(t.Rows[0]).StartsWith("Rechner\tBetriebssystem"));
        var dc = table.Rows.Cast<Row>().Single(r => PdfDom.Text(r.Cells[0]).StartsWith(SampleData.DomainController));
        Assert.Contains("nicht bewertet", PdfDom.Text(dc.Cells[4]));
        Assert.Equal(1 + SampleData.Hosts.Count, table.Rows.Count);
    }

    [PdfFact]
    public void Methode_und_Quellen_mit_Namensnennung_ohne_CIS_und_BSI_mit_Integritätsaussage()
    {
        var data = Data();
        var text = Text(Writer().BuildReport(data));
        foreach (var source in data.Method.Sources)
        {
            Assert.Contains(source.Attribution, text);
            Assert.Contains(source.Licence, text);
        }
        Assert.Contains("keine CIS- oder BSI-Inhalte", text);
        Assert.Contains("Die SHA-256-Prüfsummen zeigen, dass eine Datei seit dem Erzeugen der Prüfsumme nicht verändert wurde.", text);
        Assert.Contains("Kein GMP-Validierungspaket", text);
        Assert.Contains(S("Method_Limit_Gpo"), text); // ExperimentalGpoAttribution = true
        Assert.Contains("Regeln: 153, davon Prüfungen: 146 und Bestandsregeln: 7", text);
    }

    [PdfFact]
    public void Freigabeblock_bindet_die_Register_mit_ihren_Prüfsummen()
    {
        var data = Data();
        var built = Writer().BuildReport(data);
        var text = Text(built);
        Assert.Contains("Mit der Freigabe werden die Ausnahmen im Register 33333333 (Einträge: 7, davon gültig: 3) und der Zuschnitt 44444444 (Einträge: 3) bestätigt.", text);
        Assert.Contains("Der Maßnahmenplan 55555555 (Einträge: 4) ist Teil dieses Berichts.", text);
        Assert.Contains("Bezieht sich auf Bericht RPT-20261018T090500Z mit den Läufen 20261018T090000Z-a1b2c3, ", text.Replace("\n", " "));
        // Rollen der Voreinstellung audit aus den Anlageneinstellungen
        Assert.Contains("Erstellt · Rolle: Anlagenverantwortlicher", text);
        Assert.Contains("Geprüft · Rolle: Informationssicherheitsbeauftragter", text);
        Assert.Contains("Freigegeben · Rolle: Qualitätssicherung", text);
        // Seitenzahl als Feld im Bezug
        Assert.Contains(Section(built).Elements.OfType<Paragraph>(), p => PdfDom.Text(p).StartsWith("Bezieht sich auf Bericht") && p.Elements.OfType<NumPagesField>().Any());
        // Jeder Unterschriftsblock bleibt auf einer Seite
        var blocks = Section(built).Elements.OfType<Table>().Where(t => RowText(t.Rows[0]).Contains("· Rolle:")).ToList();
        Assert.Equal(3, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(b.Rows.Count - 1, b.Rows[0].KeepWith));
        var pages = Pages(built);
        foreach (var b in blocks)
            Assert.Single(pages.SelectMany(p => p).Where(i => ReferenceEquals(i.Object, b)).Select(i => i.Page).Distinct());
    }

    [PdfFact]
    public void Voreinstellung_Auslieferungszustand_mit_Titel_Identität_Abweichungen_und_Herstellerrollen()
    {
        var data = Data();
        var exceptions = data.Exceptions.Select((e, i) => i == 0 ? e with { Entry = e.Entry with { CreatedByRole = "Hersteller" } } : e).ToList();
        var asDelivered = data with
        {
            Header = data.Header with { Preset = ReportPreset.AsDelivered },
            Exceptions = exceptions,
        };
        var built = Writer().BuildReport(asDelivered);
        var text = Text(built);
        Assert.Contains("Auslieferungszustand – Härtungsnachweis", text);
        Assert.Contains(S("Vendor_Heading"), PdfDom.Headings(built.Document));
        Assert.Contains("Maschinenidentität", text);
        Assert.Contains("Abfüllmaschine Typ X-100 (fiktiv)", text);
        Assert.Contains("SN-0000-0001", text);
        Assert.Contains("Erstellt · Rolle: Hersteller", text);
        Assert.Contains("Geprüft · Rolle: Hersteller", text);
        Assert.Contains("Übernommen · Rolle: Betreiber", text);
        Assert.DoesNotContain("Freigegeben · Rolle:", text);
        // Die Herstellerausnahme steht unter „Abweichungen der Maschinensoftware“
        int vendor = text.IndexOf(S("Vendor_Heading") + "\n", StringComparison.Ordinal);
        int register = text.IndexOf(S("Exceptions_Heading") + "\n", StringComparison.Ordinal);
        Assert.Contains("EX-0001", text[vendor..register]);
        Assert.DoesNotContain("EX-0002", text[vendor..register]);
        Assert.Equal("Auslieferungszustand – Härtungsnachweis", built.Title);
    }

    [PdfFact]
    public void Anhang_lässt_sich_abschalten_und_hat_je_Rechner_Verschlechtert_zuerst()
    {
        var with = Writer().BuildReport(Data(), includeAppendix: true);
        var without = Writer().BuildReport(Data(), includeAppendix: false);
        Assert.Contains(S("Appendix_Heading"), PdfDom.Headings(with.Document));
        Assert.DoesNotContain(S("Appendix_Heading"), PdfDom.Headings(without.Document));
        Assert.DoesNotContain("A.1 HMI-L3-01", Text(without));

        var text = Text(with);
        int machine = text.IndexOf("A.1 HMI-L3-01 – Bedienstation Linie 3", StringComparison.Ordinal);
        int worse = text.IndexOf(S("Machine_Worse") + "\n", machine, StringComparison.Ordinal);
        int open = text.IndexOf("Offene Befunde (3)", machine, StringComparison.Ordinal);
        Assert.True(machine > 0 && worse > machine && open > worse);
        // Offene Befunde mit Soll, Ist, Quelle und Vermerken
        var block = text[open..text.IndexOf("A.2 ", open, StringComparison.Ordinal)];
        Assert.Contains("successAndFailure", block.Replace("\n", ""));
        Assert.Contains("Laufzeitzustand", block);
        Assert.Contains("Standardwert angenommen", block); // NET-08 erfüllt mit Standardwert
        Assert.Contains("Abweichungen der Rohwerte (gesamt 3", block);
        Assert.Contains("S-1-5-21-1000-2000-3000-500 (deaktiviert)", block);
        Assert.Contains("2026-10-16 07:30 UTC (Alter in Tagen: 2)", text[machine..open]);
        // Je Rechner eine neue Seite
        var pages = Pages(with);
        Assert.True(PageOf(pages, "A.2 ENG-L3-01 – Engineering-Station") > PageOf(pages, "A.1 HMI-L3-01 – Bedienstation Linie 3"));
    }

    [PdfFact]
    public void Rechnerberichte_je_Rechner_mit_Identität_und_Erhebung()
    {
        var path = Writer().WriteReport(Data(), new PdfOptions(_temp.Path, PerMachine: true, IncludeAppendix: false));
        var files = Directory.GetFiles(_temp.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(1 + SampleData.Hosts.Count, files.Count);
        foreach (var host in SampleData.Hosts)
            Assert.Contains($"Riegel-Report_Musterwerk-Linie-3_2026-10-18_{host}.pdf", files);

        var built = Writer().BuildReport(Data(), includeAppendix: false, machineHost: SampleData.Hmi);
        var text = Text(built);
        Assert.Contains("HMI-L3-01 – Rechnerbericht, Anlage Musterwerk – Linie 3", text);
        Assert.Contains("Bericht RPT-20261018T090500Z (HMI-L3-01)", PdfDom.Text(Section(built).Footers.Primary));
        // Der Rechnerbericht enthält den Anhang immer (Identität, Erhebung)
        Assert.Contains(S("Appendix_Heading"), PdfDom.Headings(built.Document));
        Assert.Contains("Abfüllmaschine Typ X-100 (fiktiv)", text);
        Assert.Contains("2026-10-16 07:30 UTC", text);
        // Nur die Befunde dieses Rechners und die Register, die auf ihn wirken
        Assert.Contains("NET-01", text);
        Assert.DoesNotContain("CRED-18 UAC", text);
        Assert.DoesNotContain("A.2 ", text);
        Assert.Contains("Läufen 20261018T090000Z-a1b2c3.", text.Replace("\n", " "));
        Assert.True(PageCount(path) > 5);
    }

    [PdfFact]
    public void Kein_Seitenumbruchfehler_im_Anlagen_und_Rechnerbericht()
    {
        var problems = new List<string>();
        foreach (var language in new[] { "de", "en" })
        {
            problems.AddRange(Problems(Writer().BuildReport(Data(language))).Select(p => $"{language}: {p}"));
            foreach (var host in SampleData.Hosts)
                problems.AddRange(Problems(Writer().BuildReport(Data(language), machineHost: host)).Select(p => $"{language}/{host}: {p}"));
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [PdfFact]
    public void Viele_Einträge_kein_Umbruchfehler_und_Tabellenköpfe_wiederholen_sich()
    {
        var data = Data();
        var exceptions = Enumerable.Range(1, 60).Select(i => data.Exceptions[i % data.Exceptions.Count] with
        {
            Entry = data.Exceptions[i % data.Exceptions.Count].Entry with { Id = $"EX-{i:D4}" },
        }).ToList();
        var findings = Enumerable.Range(1, 80).Select(i => data.Findings[i % data.Findings.Count] with { RuleId = $"R-{i:D3}" }).ToList();
        var big = data with { Exceptions = exceptions, Findings = findings };
        var built = Writer().BuildReport(big);
        var problems = Problems(built);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        var tables = Section(built).Elements.OfType<Table>().Where(t => t.Rows.Count > 20 && t.Columns.Count > 2).ToList();
        Assert.NotEmpty(tables);
        Assert.All(tables, t => Assert.True(t.Rows[0].HeadingFormat));
    }

    [PdfFact]
    public void Begrenzung_kürzt_mit_Hinweis()
    {
        var limits = new PdfLimits(Findings: 2, Changes: 1, RegisterRows: 3, Machines: 2, ResultsPerMachine: 3);
        var text = Text(Writer(limits: limits).BuildReport(Data()));
        Assert.Contains("Gekürzt: 2 von 6 gezeigt", text);
        Assert.Contains("Gekürzt: 3 von 8 gezeigt", text);
        Assert.Contains("Gekürzt: 2 von 5 gezeigt", text);
    }

    [PdfFact]
    public void Sonderzeichen_Steuerzeichen_und_lange_Wörter_rendern_ohne_Fehler()
    {
        var data = Data();
        var hostile = data with
        {
            Header = data.Header with { PlantName = "Anlage \u0001 \uD800 „Zitat“ 😀 中文", PerformedBy = new string('x', 400) },
            Findings = [data.Findings[0] with { Title = @"HKLM\SOFTWARE\Policies\" + string.Join(@"\", Enumerable.Range(0, 40).Select(i => $"Sehr_langer_Schlüssel_{i:D2}")) }],
        };
        var path = Writer().WriteReport(hostile, new PdfOptions(_temp.Path));
        Assert.True(PageCount(path) > 5);
        Assert.DoesNotContain("\u0001", Text(Writer().BuildReport(hostile)), StringComparison.Ordinal);
    }

    [PdfFact]
    public void Leere_Anlage_rendert()
    {
        var data = Data();
        var empty = data with
        {
            Banners = [], Findings = [], Changes = [], ExpiredExceptions = [], Exceptions = [], Tailoring = [], Measures = [], Machines = [],
            InsurerFacts = [], Baselines = [], Categories = [],
        };
        var built = Writer().BuildReport(empty);
        var text = Text(built);
        Assert.Contains(S("Findings_None"), text);
        Assert.Contains(S("Changes_None"), text);
        Assert.Contains(S("Exceptions_None"), text);
        Assert.DoesNotContain(S("Appendix_Heading"), PdfDom.Headings(built.Document));
        Assert.True(PageCount(Writer().WriteReport(empty, new PdfOptions(_temp.Path))) >= 4);
    }

    [PdfTheory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("zh-Hans")]
    public void Alle_Dokumente_in_jeder_Sprache_ohne_fehlende_Texte(string language)
    {
        var w = Writer();
        var docs = new[]
        {
            w.BuildReport(Data(language)),
            w.BuildSummary(SampleData.Summary() with { Language = language }),
            w.BuildTargetConfig(SampleData.TargetConfig() with { Language = language }),
            w.BuildApprovalSheet(SampleData.ApprovalSheet() with { Language = language }),
            w.BuildCollectionRecord(SampleData.CollectionRecord() with { Language = language }),
        };
        foreach (var d in docs)
        {
            var text = Text(d);
            Assert.DoesNotMatch(@"\[[A-Z][A-Za-z]+_[A-Za-z0-9_]+\]", text); // fehlender Schlüssel erschiene als [Key]
            Assert.True(d.FallbackFrom is null ? d.Language == language : d.Language == "en");
        }
        var path = w.WriteReport(Data(language), new PdfOptions(_temp.Sub(language)));
        Assert.True(PageCount(path) > 5);
    }

    [PdfFact]
    public void PdfA_wird_mit_RGL_0520_abgelehnt_und_nichts_geschrieben()
    {
        var options = new PdfOptions(_temp.Path, PdfA: true);
        var w = Writer();
        foreach (var write in new Action[]
        {
            () => w.WriteReport(Data(), options),
            () => w.WriteSummary(SampleData.Summary(), options),
            () => w.WriteTargetConfig(SampleData.TargetConfig(), options),
            () => w.WriteApprovalSheet(SampleData.ApprovalSheet(), options),
            () => w.WriteCollectionRecord(SampleData.CollectionRecord(), options),
        })
        {
            var e = Assert.Throws<RiegelException>(write);
            Assert.Equal(RglCodesPdf.PdfANotSupported, e.Code);
            Assert.Contains("PDF/A", e.Message);
        }
        Assert.Empty(Directory.GetFiles(_temp.Path));
    }

    [PdfFact]
    public void Nicht_schreibbares_Ziel_gibt_RGL_0500()
    {
        var file = Path.Combine(_temp.Path, "eine-datei");
        File.WriteAllText(file, "x");
        var e = Assert.Throws<RiegelException>(() => Writer().WriteReport(Data(), new PdfOptions(Path.Combine(file, "unter"))));
        Assert.Equal(RglCodesPdf.WriteFailed, e.Code);
    }

    [WindowsFact("Dateisperren gelten nur unter Windows verbindlich")]
    public void Geöffnete_Datei_RGL_0500_alte_Datei_bleibt()
    {
        if (!PdfFonts.IsAvailable) return;
        var path = Path.Combine(_temp.Path, "Riegel-Report_Musterwerk-Linie-3_2026-10-18.pdf");
        File.WriteAllText(path, "alt");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var e = Assert.Throws<RiegelException>(() => Writer().WriteReport(Data(), new PdfOptions(_temp.Path)));
            Assert.Equal(RglCodesPdf.WriteFailed, e.Code);
        }
        Assert.Equal("alt", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_temp.Path));
    }

    [PdfFact]
    public void Parallel_gleicher_Satz_wie_nacheinander()
    {
        var data = Data();
        var reference = PageContents(Writer().WriteReport(data, new PdfOptions(_temp.Sub("seriell"))));
        Parallel.For(0, 4, i => Writer().WriteReport(data, new PdfOptions(_temp.Sub($"parallel-{i}"))));
        for (int i = 0; i < 4; i++)
            Assert.Equal(reference, PageContents(Path.Combine(_temp.Path, $"parallel-{i}", "Riegel-Report_Musterwerk-Linie-3_2026-10-18.pdf")));
    }

    /// <summary>Inhalt jeder Seite (Zeichenbefehle mit Positionen) als Prüfsumme – gleich gesetzt heißt gleich.</summary>
    private static List<string> PageContents(string path)
    {
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
        return pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>()
            .Select(p => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(p.Contents.CreateSingleContent().Stream!.UnfilteredValue)))
            .ToList();
    }

    [PdfFact]
    public void Über_die_Dienste_erreichbar()
    {
        var services = Riegel.Core.Services.RiegelServices.CreateDefault(new Riegel.Core.Services.NullPlatform());
        Assert.IsType<PdfWriter>(services.Pdf);
        var path = services.Pdf.WriteReport(Data(), new PdfOptions(_temp.Path));
        Assert.True(PageCount(path) > 5);
    }

    [PdfFact]
    public void Logo_wird_geprüft_eingebettet()
    {
        foreach (var (name, bytes) in new[] { ("logo.png", Png(120, 40)), ("logo.jpg", Jpeg()) })
        {
            var file = Path.Combine(_temp.Path, name);
            File.WriteAllBytes(file, bytes);
            var data = Data();
            data = data with { Header = data.Header with { LogoPath = file } };
            var logo = CoverLogo.Load(file);
            Assert.NotNull(logo);
            var built = Writer().BuildReport(data, logo: logo);
            var images = Section(built).Elements.OfType<Table>().SelectMany(t => t.Rows.Cast<Row>())
                .SelectMany(r => r.Cells.Cast<Cell>()).SelectMany(c => c.Elements.OfType<Paragraph>())
                .SelectMany(p => p.Elements.OfType<Image>()).ToList();
            var image = Assert.Single(images);
            Assert.StartsWith("base64:", image.Name);
            var path = Writer().WriteReport(data, new PdfOptions(_temp.Sub("aus-" + name)));
            Assert.True(PageCount(path) > 5);
        }
    }
}
