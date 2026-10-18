// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Text;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Reports.Tabular;
using Riegel.Core.Resources;

namespace Riegel.Tests.Reports.Tabular;

/// <summary>Hilfen der Tabellen-Tests: CSV nach RFC 4180 lesen, Rohdaten einer xlsx-Datei, feindliche Berichtsdaten.</summary>
internal static class TabularTestSupport
{
    /// <summary>Schreiber ohne Katalog (title_en kommt dann aus den Berichtsdaten).</summary>
    public static TabularWriter Writer(ICatalogue? catalogue = null) =>
        new(() => catalogue ?? throw new RiegelException("RGL-0002", "kein Katalog im Test"));

    /// <summary>Strenger Leser für RFC 4180: Felder mit Komma, CRLF als Zeilenende, „"“ verdoppelt in Anführungszeichen.</summary>
    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, inQuotes = false;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    inQuotes = false;
                    i++;
                    continue;
                }
                field.Append(c);
                i++;
                continue;
            }
            switch (c)
            {
                case '"':
                    Assert.True(field.Length == 0 && !quoted, $"Anführungszeichen mitten im Feld bei {i}");
                    inQuotes = quoted = true;
                    i++;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    quoted = false;
                    i++;
                    break;
                case '\r':
                    Assert.True(i + 1 < text.Length && text[i + 1] == '\n', $"CR ohne LF bei {i}");
                    row.Add(field.ToString());
                    field.Clear();
                    quoted = false;
                    rows.Add(row);
                    row = [];
                    i += 2;
                    break;
                case '\n':
                    Assert.Fail($"LF ohne CR außerhalb von Anführungszeichen bei {i}");
                    break;
                default:
                    Assert.False(quoted, $"Zeichen nach schließendem Anführungszeichen bei {i}");
                    field.Append(c);
                    i++;
                    break;
            }
        }
        Assert.False(inQuotes, "Anführungszeichen nicht geschlossen");
        Assert.True(field.Length == 0 && row.Count == 0, "Letzte Zeile ohne CRLF");
        return rows;
    }

    /// <summary>XML aller Arbeitsblätter einer xlsx-Datei.</summary>
    public static IReadOnlyList<string> SheetXml(string xlsx)
    {
        using var zip = ZipFile.OpenRead(xlsx);
        return [.. zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal))
            .Select(e => { using var r = new StreamReader(e.Open(), Encoding.UTF8); return r.ReadToEnd(); })];
    }

    /// <summary>Werte, die Tabellenprogramme als Formel lesen würden (OWASP CSV Injection).</summary>
    public static IReadOnlyList<string> FormulaValues { get; } =
    [
        "=HYPERLINK(\"http://example.invalid\",\"x\")", "+cmd|' /C calc'!A0", "-2+3", "@SUM(1+1)", "\tTAB=1", "\rCR=1",
    ];

    /// <summary>
    /// Beispieldaten mit formelartigen Werten an allen Stellen, die aus Rechner- oder Registerdaten stammen: Anlage,
    /// Rechnername, Ist-Wert, Details, Begründung, Bestand. Dazu ein NUL-Zeichen, das XML nicht erlaubt.
    /// </summary>
    public static ReportData Hostile()
    {
        var d = Core.Samples.SampleData.Report();
        var machine = d.Machines[0];
        var results = machine.Results.Select((v, i) => v with
        {
            Result = v.Result with
            {
                Actual = FormulaValues[i % FormulaValues.Count],
                Details = [FormulaValues[(i + 1) % FormulaValues.Count], "NUL\0im Text"],
                SourceDetail = FormulaValues[(i + 2) % FormulaValues.Count],
            },
            Title = FormulaValues[(i + 3) % FormulaValues.Count],
        }).ToList();
        var hostile = machine with
        {
            HostName = "=HOST()",
            Label = "@label",
            Results = results,
            Inventory = [new InventoryView("ACCT-15", "-Inventar", InventoryRenderer.LocalAdmins, ["=evil()", "+1", "S-1-5-32-544"])],
        };
        var ex = d.Exceptions[0];
        return d with
        {
            Header = d.Header with { PlantName = "-Anlage", PlantId = "=ID()" },
            Machines = [hostile, .. d.Machines.Skip(1)],
            Exceptions = [ex with { Entry = ex.Entry with { Justification = "=1+1 Begründung mit Formel am Anfang", Comment = "\t@x" } }, .. d.Exceptions.Skip(1)],
        };
    }

    public static string T(string key, string lang) =>
        DataOutputStrings.ResourceManager.GetString(key, System.Globalization.CultureInfo.GetCultureInfo(lang))!;
}
