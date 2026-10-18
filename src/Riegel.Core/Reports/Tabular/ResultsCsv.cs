// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using Riegel.Core.Evidence;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Model.Reports;

namespace Riegel.Core.Reports.Tabular;

/// <summary>
/// <c>results.csv</c> (spec 5 O3): eine Zeile je Rechner und Regel, RFC 4180 (Komma, CRLF, Felder mit Komma,
/// Anführungszeichen oder Zeilenumbruch in Anführungszeichen, „"“ verdoppelt), UTF-8 ohne BOM, invariant
/// (Zahlen, ISO-8601-Daten, Codes statt übersetzter Namen). Formel-Schutz für jedes Feld (<see cref="TextGuard"/>).
/// </summary>
internal static class ResultsCsv
{
    /// <summary>Spalten von results.csv (spec 5 O3), invariant.</summary>
    public static IReadOnlyList<string> Columns { get; } =
    [
        "plant", "host", "runId", "evaluatedAtUtc", "ruleSet", "ruleId", "category", "severity", "status", "flags", "expected",
        "actual", "valueSource", "exceptionId", "exceptionReviewBy", "tailoringId", "measureId", "measureDue", "title_en",
    ];

    /// <summary>Die Zeilen als Felder (ohne Kopfzeile), vor Formel-Schutz und Maskierung.</summary>
    /// <param name="englishTitle">Englischer Titel einer Regel (RuleSetId, RuleId), null = Titel aus den Berichtsdaten.</param>
    public static IEnumerable<IReadOnlyList<string>> Rows(ReportData d, Func<string, string, string?> englishTitle)
    {
        var reviewBy = new Dictionary<string, DateOnly?>(StringComparer.Ordinal);
        foreach (var e in d.Exceptions.Concat(d.ExpiredExceptions)) reviewBy.TryAdd(e.Entry.Id, e.Entry.Approval.ReviewBy);
        var measureDue = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        foreach (var m in d.Measures) measureDue.TryAdd(m.Entry.Id, m.Entry.DueDate);

        foreach (var machine in d.Machines)
        {
            var evaluatedAt = EvaluatedAt(machine.RunId);
            foreach (var view in machine.Results)
            {
                var r = view.Result;
                yield return
                [
                    d.Header.PlantId,
                    machine.HostName,
                    machine.RunId,
                    evaluatedAt,
                    r.RuleSetId,
                    r.RuleId,
                    view.Category,
                    OutputText.Camel(r.Severity),
                    OutputText.Camel(r.Status),
                    string.Join(";", FlagsArrayConverter<ResultFlags>.Names(r.Flags)),
                    r.Expected ?? "",
                    r.Actual ?? "",
                    OutputText.Camel(r.Source),
                    r.ExceptionId ?? "",
                    r.ExceptionId is { } ex && reviewBy.TryGetValue(ex, out var rb) ? OutputText.Date(rb) : "",
                    r.TailoringId ?? "",
                    r.MeasureId ?? "",
                    r.MeasureId is { } mid && measureDue.TryGetValue(mid, out var due) ? OutputText.Date(due) : "",
                    englishTitle(r.RuleSetId, r.RuleId) ?? view.Title,
                ];
            }
        }
    }

    /// <summary>Schreibt die Datei und gibt ihren Pfad zurück.</summary>
    public static string Write(ReportData d, string path, Func<string, string, string?> englishTitle)
    {
        var sb = new StringBuilder();
        AppendLine(sb, Columns);
        foreach (var row in Rows(d, englishTitle)) AppendLine(sb, [.. row.Select(TextGuard.ForCsv)]);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(sb.ToString());
        OutputFiles.WriteAtomic(path, bytes);
        return path;
    }

    /// <summary>Eine Zeile nach RFC 4180 mit CRLF.</summary>
    public static void AppendLine(StringBuilder sb, IReadOnlyList<string> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Quote(fields[i]));
        }
        sb.Append("\r\n");
    }

    /// <summary>Feld nach RFC 4180 2.6/2.7: in Anführungszeichen, wenn nötig; „"“ wird verdoppelt.</summary>
    public static string Quote(string field)
    {
        if (field.IndexOfAny([',', '"', '\r', '\n']) < 0) return field;
        return "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>Auswertungszeit aus der Lauf-ID (deren Zeitstempel ist die Auswertungszeit, spec 7.6).</summary>
    public static string EvaluatedAt(string runId)
    {
        if (!RunIds.IsValid(runId)) return "";
        return DateTime.TryParseExact(runId[..16], "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? t.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : "";
    }
}
