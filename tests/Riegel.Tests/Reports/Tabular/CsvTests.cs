// SPDX-License-Identifier: MIT
using System.Text;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Reports.Tabular;
using Riegel.Core.Samples;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Reports.Tabular;

/// <summary>results.csv (O3): Spalten, RFC 4180, ohne BOM, invariant, Rundreise.</summary>
public sealed class CsvTests
{
    private static readonly string[] Columns =
    [
        "plant", "host", "runId", "evaluatedAtUtc", "ruleSet", "ruleId", "category", "severity", "status", "flags", "expected",
        "actual", "valueSource", "exceptionId", "exceptionReviewBy", "tailoringId", "measureId", "measureDue", "title_en",
    ];

    private static (byte[] Bytes, List<List<string>> Rows) Write(ReportData d, ICatalogue? catalogue = null)
    {
        using var temp = new TempFolder();
        var path = TabularTestSupport.Writer(catalogue).WriteCsv(d, temp.Path);
        Assert.Equal("results.csv", Path.GetFileName(path));
        var bytes = File.ReadAllBytes(path);
        return (bytes, TabularTestSupport.ParseCsv(new UTF8Encoding(false, true).GetString(bytes)));
    }

    [Fact]
    public void Kopfzeile_ohne_BOM_mit_CRLF()
    {
        var (bytes, rows) = Write(SampleData.Report());
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "kein BOM");
        Assert.Equal(Columns, rows[0]);
        Assert.Equal(Columns, ResultsCsv.Columns);
        Assert.All(rows, r => Assert.Equal(Columns.Length, r.Count));
        var text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith(string.Join(",", Columns) + "\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Rundreise_ergibt_die_Werte_der_Berichtsdaten()
    {
        var d = SampleData.Report();
        var (_, rows) = Write(d);
        var data = rows.Skip(1).ToList();
        Assert.Equal(d.Machines.Sum(m => m.Results.Count), data.Count);
        int i = 0;
        foreach (var m in d.Machines)
            foreach (var v in m.Results)
            {
                var row = data[i++];
                var r = v.Result;
                Assert.Equal(SampleData.PlantId, row[0]);
                Assert.Equal(m.HostName, row[1]);
                Assert.Equal(m.RunId, row[2]);
                Assert.Equal(r.RuleSetId, row[4]);
                Assert.Equal(r.RuleId, row[5]);
                Assert.Equal(v.Category, row[6]);
                Assert.Equal(r.Expected ?? "", row[10]);
                Assert.Equal(r.Actual ?? "", row[11]);
                Assert.Equal(r.ExceptionId ?? "", row[13]);
                Assert.Equal(r.TailoringId ?? "", row[15]);
                Assert.Equal(r.MeasureId ?? "", row[16]);
                Assert.Equal(v.Title, row[18]);
            }

        // Invariante Codes und ISO-Daten
        var net01 = data.Single(r => r[1] == SampleData.Hmi && r[5] == "NET-01");
        Assert.Equal("2026-10-18T09:00:00Z", net01[3]);
        Assert.Equal("high", net01[7]);
        Assert.Equal("fail", net01[8]);
        Assert.Equal("regression", net01[9]);
        Assert.Equal("runtime", net01[12]);
        Assert.Equal("M-0003", net01[16]);
        Assert.Equal("2026-10-01", net01[17]);
        var logon = data.Single(r => r[1] == SampleData.Hmi && r[5] == "LOGON-10");
        Assert.Equal("exception", logon[8]);
        Assert.Equal("EX-0001", logon[13]);
        Assert.Equal("2027-04-20", logon[14]);
        Assert.Equal("localPolicy", logon[12]);
        var cred = data.Single(r => r[5] == "CRED-06");
        Assert.Equal("notCheckable", cred[8]);
        Assert.Equal("configuredOnly", cred[9]);
        var imported = data.Single(r => r[5] == "IMP-kunde-vorgaben-0001");
        Assert.Equal("kunde-vorgaben", imported[4]);
        Assert.Equal("domainGpo", data.Single(r => r[1] == SampleData.Engineering && r[5] == "RDP-01")[12]);
    }

    [Fact]
    public void Felder_mit_Komma_Anfuehrungszeichen_und_Zeilenumbruch_ueberstehen_die_Rundreise()
    {
        var d = SampleData.Report();
        var m = d.Machines[0];
        string[] values = ["a,b", "sagt \"hallo\"", "zwei\r\nZeilen", "nur\nLF", "  Leerzeichen  ", "Ümläüte ß 中文"];
        var results = m.Results.Take(values.Length).Select((v, i) => v with { Result = v.Result with { Actual = values[i] } }).ToList();
        var (_, rows) = Write(d with { Machines = [m with { Results = results }] });
        Assert.Equal(values, rows.Skip(1).Select(r => r[11]).ToArray());
    }

    [Fact]
    public void Englischer_Titel_kommt_aus_dem_Katalog()
    {
        var d = SampleData.Report();
        var rule = RuleBuilder.Check("NET-01", "network").Title("SMBv1 server disabled (catalogue)", "SMBv1-Server aus (Katalog)").Build();
        var (_, rows) = Write(d, new MiniCatalogue([rule]));
        Assert.Equal("SMBv1 server disabled (catalogue)", rows.Skip(1).First(r => r[5] == "NET-01")[18]);
        // Regeln, die der Katalog nicht kennt (Baselines), behalten den Titel der Berichtsdaten
        Assert.Equal("Customer requirement: OPC UA port only", rows.Skip(1).First(r => r[5] == "IMP-kunde-vorgaben-0001")[18]);
    }

    [Fact]
    public void Steuerzeichen_werden_ersetzt_und_brechen_die_Datei_nicht()
    {
        var d = SampleData.Report();
        var m = d.Machines[0];
        var results = m.Results.Take(1).Select(v => v with { Result = v.Result with { Actual = "NUL\0BEL\aESC" } }).ToList();
        var (_, rows) = Write(d with { Machines = [m with { Results = results }] });
        Assert.Equal("NUL�BEL�ESC", rows[1][11]);
    }
}

/// <summary>Kleiner Katalog für Tests (nur Regeln).</summary>
internal sealed class MiniCatalogue(IReadOnlyList<Rule> rules) : ICatalogue
{
    public string Id => "riegel";
    public string Version => "2026.10";
    public DateOnly ReleasedOn => new(2026, 10, 18);
    public string Sha256 => new('a', 64);
    public IReadOnlyList<Rule> Rules { get; } = rules;
    public Rule? Find(string id) => Rules.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
    public LifecycleTable Lifecycle => LifecycleTable.Empty;
    public BuildTable Builds => BuildTable.Empty;
    public CapabilityTable Capabilities => CapabilityTable.Empty;
    public CatalogueData Data => CatalogueData.Empty;
}
