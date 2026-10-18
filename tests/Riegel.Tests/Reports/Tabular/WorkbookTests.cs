// SPDX-License-Identifier: MIT
using ClosedXML.Excel;
using Riegel.Core.Localization;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Reports.Tabular;
using Riegel.Core.Resources;
using Riegel.Core.Samples;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Reports.Tabular;

/// <summary>Excel-Mappe der Anlage (O7) und Soll-Konfiguration (O13): Blätter, Spalten, Filter, Fixierung, Breiten, Daten.</summary>
public sealed class WorkbookTests
{
    private static XLWorkbook Open(ReportData d, TempFolder temp, out string path)
    {
        path = TabularTestSupport.Writer().WriteWorkbook(d, temp.Path);
        return new XLWorkbook(path);
    }

    private static string T(string key, string lang = "de") => TabularTestSupport.T(key, lang);

    private static List<string> Headers(IXLWorksheet ws) =>
        [.. ws.Row(1).CellsUsed().Select(c => c.GetString())];

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("zh-Hans")]
    public void Mappe_hat_alle_Blaetter_der_Spezifikation_in_der_Berichtssprache(string lang)
    {
        using var temp = new TempFolder();
        var data = SampleData.Report() with { Header = SampleData.Report().Header with { Language = lang } };
        using var wb = Open(data, temp, out var path);
        Assert.Equal("Riegel-Overview_Musterwerk-Linie-3_2026-10-18.xlsx", Path.GetFileName(path));
        var expected = OverviewWorkbook.SheetKeys.Select(k => T(k, lang)).ToList();
        Assert.Equal(expected, wb.Worksheets.Select(w => w.Name).ToList());
        Assert.All(expected, n => Assert.InRange(n.Length, 1, 31));
        Assert.Equal(expected.Count, expected.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Jedes_Blatt_hat_Kopfzeile_Autofilter_fixierte_Kopfzeile_und_Breiten()
    {
        using var temp = new TempFolder();
        using var wb = Open(SampleData.Report(), temp, out _);
        foreach (var ws in wb.Worksheets)
        {
            var header = ws.Row(1).CellsUsed().ToList();
            Assert.NotEmpty(header);
            Assert.All(header, c => Assert.True(c.Style.Font.Bold, $"{ws.Name}: Kopfzeile fett"));
            Assert.All(header, c => Assert.False(string.IsNullOrWhiteSpace(c.GetString()), $"{ws.Name}: leere Überschrift"));
            Assert.True(ws.AutoFilter.IsEnabled, $"{ws.Name}: Autofilter");
            Assert.Equal(1, ws.AutoFilter.Range.RangeAddress.FirstAddress.RowNumber);
            Assert.Equal(header.Count, ws.AutoFilter.Range.ColumnCount());
            Assert.Equal(1, ws.SheetView.SplitRow);
            foreach (var c in header)
                Assert.InRange(ws.Column(c.Address.ColumnNumber).Width, 8, 60);
        }
    }

    [Fact]
    public void Spalten_der_Rechner_Befunde_Ergebnisse_und_Versicherer_Fakten()
    {
        using var temp = new TempFolder();
        using var wb = Open(SampleData.Report(), temp, out _);
        var machines = Headers(wb.Worksheet(T(nameof(DataOutputStrings.Sheet_Machines))));
        // spec 5 O7: Gruppe/Rolle, Identität, Betriebssystem + Supportende, Zählungen, Werte, Prüfumfang, Erfassung, Admin, veraltet
        foreach (var key in new[] { "Col_Host", "Col_Group", "Col_Role", "Col_MachineType", "Col_SerialNumber", "Col_Os", "Col_SupportEnd",
                     "Col_OpenHigh", "Col_Score", "Col_Documented", "Col_CheckedShare", "Col_CollectedAt", "Col_Admin", "Col_Stale" })
            Assert.Contains(T(key), machines);
        Assert.Equal(1, wb.Worksheet(T(nameof(DataOutputStrings.Sheet_Machines))).SheetView.SplitColumn);

        var results = Headers(wb.Worksheet(T(nameof(DataOutputStrings.Sheet_Results))));
        Assert.Equal(new[] { "Col_Host", "Col_RuleSet", "Col_RuleId", "Col_Title", "Col_Category", "Col_Severity", "Col_Kind", "Col_Status",
            "Col_Flags", "Col_Expected", "Col_Actual", "Col_ValueSource", "Col_SourceDetail", "Col_ExceptionId", "Col_TailoringId",
            "Col_MeasureId", "Col_ReasonCode", "Col_Details", "Col_EvidenceKeys" }.Select(k => T(k)), results);

        var insurer = wb.Worksheet(T(nameof(DataOutputStrings.Sheet_InsurerFacts)));
        var insurerHeaders = Headers(insurer);
        foreach (var key in new[] { "Col_Os", "Col_SupportEnd", "Col_AvProduct", "Col_AvState", "Col_SignatureAgeDays", "Col_DaysSinceUpdate",
                     "Col_PatchMonth", "Col_LocalAdminCount", "Col_RdpEnabled", "Col_RdpNla", "Col_Smb1", "Col_BitLocker", "Col_FirewallOn",
                     "Col_NetGateway", "Col_NetActiveAdapters", "Col_NetWireless", "Col_NetIpForwarding", "Col_NetProxy" })
            Assert.Contains(T(key), insurerHeaders);
        // Die Zeile „nicht aus dem Rechner ablesbar“ steht unter der Tabelle, außerhalb des Filters
        var note = insurer.CellsUsed().Single(c => c.GetString() == T(nameof(DataOutputStrings.Insurer_NotReadable)));
        Assert.True(note.Address.RowNumber > insurer.AutoFilter.Range.RangeAddress.LastAddress.RowNumber);
        Assert.Contains("MFA", note.GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Inhalte_stimmen_mit_den_Berichtsdaten_ueberein()
    {
        using var temp = new TempFolder();
        var d = SampleData.Report();
        using var wb = Open(d, temp, out _);
        var results = wb.Worksheet(T(nameof(DataOutputStrings.Sheet_Results)));
        Assert.Equal(d.Machines.Sum(m => m.Results.Count), results.RangeUsed()!.RowCount() - 1);
        var machines = wb.Worksheet(T(nameof(DataOutputStrings.Sheet_Machines)));
        Assert.Equal(d.Machines.Count, machines.RangeUsed()!.RowCount() - 1);
        Assert.Equal(d.Findings.Count, wb.Worksheet(T(nameof(DataOutputStrings.Sheet_Findings))).RangeUsed()!.RowCount() - 1);
        Assert.Equal(d.Exceptions.Count + d.ExpiredExceptions.Count, wb.Worksheet(T(nameof(DataOutputStrings.Sheet_Exceptions))).RangeUsed()!.RowCount() - 1);
        Assert.Equal(d.Tailoring.Count, wb.Worksheet(T(nameof(DataOutputStrings.Sheet_Tailoring))).RangeUsed()!.RowCount() - 1);
        Assert.Equal(d.Measures.Count, wb.Worksheet(T(nameof(DataOutputStrings.Sheet_Measures))).RangeUsed()!.RowCount() - 1);
        Assert.Equal(d.Changes.Count, wb.Worksheet(T(nameof(DataOutputStrings.Sheet_Changes))).RangeUsed()!.RowCount() - 1);

        // Status, Schwere und Flags in der Berichtssprache; Ist „(nicht gesetzt)“, wenn er fehlt
        var row = results.RowsUsed().First(r => r.Cell(3).GetString() == "NET-01" && r.Cell(1).GetString() == SampleData.Hmi);
        Assert.Equal("Nicht erfüllt", row.Cell(8).GetString());
        Assert.Equal("Hoch", row.Cell(6).GetString());
        Assert.Equal("Verschlechtert", row.Cell(9).GetString());
        var notSet = results.RowsUsed().First(r => r.Cell(3).GetString() == "NET-08" && r.Cell(1).GetString() == SampleData.Hmi);
        Assert.Equal("(nicht gesetzt)", notSet.Cell(11).GetString());

        // Domänencontroller: „nicht bewertet“ statt Härtungsgrad
        var dc = machines.RowsUsed().First(r => r.Cell(1).GetString() == SampleData.DomainController);
        var scoreColumn = Headers(machines).IndexOf(T("Col_Score")) + 1;
        Assert.Equal(T(nameof(DataOutputStrings.Value_NotScoredDc)), dc.Cell(scoreColumn).GetString());

        // „Verschlechtert“ zuerst im Blatt Änderungen
        var changes = wb.Worksheet(T(nameof(DataOutputStrings.Sheet_Changes)));
        Assert.Equal(T("ChangeKind_Worse"), changes.Cell(2, 9).GetString());
    }

    [Fact]
    public void Daten_im_ISO_Format_und_als_echte_Datumszellen()
    {
        using var temp = new TempFolder();
        using var wb = Open(SampleData.Report(), temp, out _);
        var machines = wb.Worksheet(T(nameof(DataOutputStrings.Sheet_Machines)));
        var headers = Headers(machines);
        var collected = machines.Cell(2, headers.IndexOf(T("Col_CollectedAt")) + 1);
        Assert.Equal(XLDataType.DateTime, collected.DataType);
        Assert.Equal(SheetBuilder.TimestampFormat, collected.Style.NumberFormat.Format);
        Assert.Equal(new DateTime(2026, 10, 16, 7, 30, 0), collected.GetDateTime());
        var delivery = machines.Cell(2, headers.IndexOf(T("Col_DeliveryDate")) + 1);
        Assert.Equal(SheetBuilder.DateFormat, delivery.Style.NumberFormat.Format);
        Assert.Equal(new DateTime(2026, 3, 2), delivery.GetDateTime());

        var exceptions = wb.Worksheet(T(nameof(DataOutputStrings.Sheet_Exceptions)));
        var reviewBy = exceptions.Cell(2, Headers(exceptions).IndexOf(T("Col_ReviewBy")) + 1);
        Assert.Equal(SheetBuilder.DateFormat, reviewBy.Style.NumberFormat.Format);
        // Zahlen als Zahlen
        var openHigh = machines.Cell(2, headers.IndexOf(T("Col_OpenHigh")) + 1);
        Assert.Equal(XLDataType.Number, openHigh.DataType);
    }

    [Fact]
    public void Leere_Berichtsdaten_ergeben_eine_gueltige_Mappe()
    {
        using var temp = new TempFolder();
        var d = SampleData.Report() with
        {
            Machines = [], Findings = [], Changes = [], Exceptions = [], ExpiredExceptions = [], Tailoring = [], Measures = [],
            InsurerFacts = [], Banners = [], Baselines = [], Categories = [],
        };
        using var wb = Open(d, temp, out _);
        Assert.Equal(OverviewWorkbook.SheetKeys.Count, wb.Worksheets.Count);
    }

    [Fact]
    public void Soll_Konfiguration_als_Mappe()
    {
        using var temp = new TempFolder();
        var d = SampleData.TargetConfig();
        var path = TabularTestSupport.Writer().WriteTargetConfigWorkbook(d, temp.Path);
        Assert.Equal("Riegel-Soll-Konfiguration_Musterwerk-Linie-3_2026-10-18.xlsx", Path.GetFileName(path));
        using var wb = new XLWorkbook(path);
        Assert.Equal(TargetConfigWorkbook.SheetKeys.Select(k => T(k)).ToList(), wb.Worksheets.Select(w => w.Name).ToList());
        var rules = wb.Worksheet(T(nameof(DataOutputStrings.Sheet_TargetRules)));
        Assert.Equal(d.Groups.Sum(g => g.Rules.Count), rules.RangeUsed()!.RowCount() - 1);
        Assert.Contains(rules.RowsUsed(), r => r.Cell(5).GetString() == "NET-08" && r.Cell(10).GetString() == "RequireSecuritySignature = 1");
        var overview = wb.Worksheet(T(nameof(DataOutputStrings.Sheet_TargetOverview)));
        Assert.Contains(overview.CellsUsed(), c => c.GetString() == SampleData.Catalogue.Sha256);
        Assert.Contains(overview.CellsUsed(), c => c.GetString() == T(nameof(DataOutputStrings.Section_Provenance)));
        var signOff = wb.Worksheet(T(nameof(DataOutputStrings.Sheet_TargetSignOff)));
        for (int i = 0; i < d.SignOff.Count; i++) Assert.Equal(d.SignOff[i].Role, signOff.Cell(i + 2, 2).GetString());
        Assert.Contains(signOff.CellsUsed(), c => c.GetString().Contains(Core.Model.Hashes.Short(d.TailoringSha256), StringComparison.Ordinal));
        foreach (var ws in wb.Worksheets)
        {
            Assert.True(ws.AutoFilter.IsEnabled, ws.Name);
            Assert.Equal(1, ws.SheetView.SplitRow);
        }
    }

    [Fact]
    public void Jede_Aufzaehlung_hat_einen_Text_in_jeder_Sprache()
    {
        var keys = new List<string>();
        keys.AddRange(Enum.GetNames<MachineRole>().Select(n => "MachineRole_" + n));
        keys.AddRange(Enum.GetNames<SnapshotOrigin>().Select(n => "Origin_" + n));
        keys.AddRange(Enum.GetNames<RuleKind>().Select(n => "Kind_" + n));
        keys.AddRange(Enum.GetNames<ValueSource>().Select(n => "ValueSource_" + n));
        keys.AddRange(Enum.GetNames<RegisterEntryState>().Select(n => "EntryState_" + n));
        keys.AddRange(Enum.GetNames<MeasureState>().Select(n => "MeasureState_" + n));
        keys.AddRange(Enum.GetNames<ComparisonBasis>().Select(n => "Basis_" + n));
        keys.AddRange(Enum.GetNames<StatusChangeKind>().Select(n => "ChangeKind_" + n));
        keys.AddRange(Enum.GetNames<InventoryRenderer>().Select(n => "Renderer_" + n));
        keys.AddRange(Enum.GetNames<BaselineSourceKind>().Select(n => "SourceKind_" + n));
        keys.AddRange(Enum.GetNames<ReportPreset>().Select(n => "Preset_" + n));
        keys.AddRange(Enum.GetNames<RemediationExclusionReason>().Select(n => "Exclusion_" + n));
        foreach (var lang in UiLanguage.Supported)
            foreach (var key in keys)
                Assert.False(string.IsNullOrEmpty(DataOutputStrings.ResourceManager.GetString(key, System.Globalization.CultureInfo.GetCultureInfo(lang))), $"{lang}: {key}");
    }
}
