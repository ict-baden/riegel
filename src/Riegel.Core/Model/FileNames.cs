// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;

namespace Riegel.Core.Model;

/// <summary>
/// Sichere Namensteile für Dateien und Ordner (spec 7.6): nur <c>[A-Za-z0-9._-]</c>, höchstens 63 Zeichen,
/// reservierte Gerätenamen von Windows (CON, NUL, COM1 …) mit „_“ davor. Umlaute werden umschrieben (ä → ae).
/// </summary>
public static class FileNames
{
    public const int MaxLength = 63;

    private static readonly string[] Reserved =
    [
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>Hostname als Datei- und Ordnername; leer oder nur Sonderzeichen ergibt „unknown“.</summary>
    public static string Host(string hostName) => Token(hostName, "unknown");

    private static string WithoutLoneSurrogates(string text)
    {
        StringBuilder? sb = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool paired = char.IsHighSurrogate(c) ? i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])
                : !char.IsLowSurrogate(c) || (i > 0 && char.IsHighSurrogate(text[i - 1]));
            if (!paired)
            {
                sb ??= new StringBuilder(text, 0, i, text.Length);
                sb.Append('\uFFFD');
                continue;
            }
            sb?.Append(c);
        }
        return sb?.ToString() ?? text;
    }

    /// <summary>Freier Text (z. B. Anlagenname) als Namensteil; Leerzeichen und Sonderzeichen werden zu „-“.</summary>
    public static string Token(string? text, string fallback = "x")
    {
        var sb = new StringBuilder();
        // Einzelne Surrogate (z. B. aus einem JSON-Escape \uD800) ließen Normalize mit einer Ausnahme scheitern –
        // vorher ersetzen; sie würden ohnehin zu „-“ (spec 7.12: feindliche Eingabe ist ein RGL-Code, kein Absturz)
        foreach (var c in WithoutLoneSurrogates(text ?? "").Normalize(NormalizationForm.FormC))
        {
            string part = c switch
            {
                'ä' => "ae", 'ö' => "oe", 'ü' => "ue", 'Ä' => "Ae", 'Ö' => "Oe", 'Ü' => "Ue", 'ß' => "ss",
                _ when c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-' => c.ToString(),
                _ => "-",
            };
            sb.Append(part);
        }
        // Folgen von „-“ zusammenfassen, Ränder säubern
        var collapsed = new StringBuilder();
        foreach (var c in sb.ToString())
        {
            if (c == '-' && collapsed.Length > 0 && collapsed[^1] == '-') continue;
            collapsed.Append(c);
        }
        var result = collapsed.ToString().Trim('-', '.', ' ');
        if (result.Length > MaxLength) result = result[..MaxLength].TrimEnd('-', '.');
        if (result.Length == 0) result = fallback;
        var stem = result.Split('.')[0];
        if (Reserved.Contains(stem, StringComparer.OrdinalIgnoreCase)) result = "_" + result;
        return result.Length > MaxLength ? result[..MaxLength] : result;
    }

    /// <summary>Zeitstempel für Dateinamen: <c>yyyyMMddTHHmmssZ</c> (UTC).</summary>
    public static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Datum für Dateinamen: <c>yyyy-MM-dd</c>.</summary>
    public static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>
/// Dateinamen der Ausgaben (spec 5, invariant Englisch bzw. wie dort festgelegt). Schreiber und Tests nehmen genau
/// diese Namen, damit CI und Doku sie finden.
/// </summary>
public static class ReportFileNames
{
    /// <summary>O4 <c>Riegel-Report_&lt;plant&gt;_&lt;yyyy-MM-dd&gt;.pdf</c></summary>
    public static string Report(string plantName, DateOnly date) => $"Riegel-Report_{FileNames.Token(plantName)}_{FileNames.Date(date)}.pdf";

    /// <summary>O5 <c>Riegel-Report_&lt;plant&gt;_&lt;yyyy-MM-dd&gt;_&lt;host&gt;.pdf</c></summary>
    public static string MachineReport(string plantName, DateOnly date, string host) =>
        $"Riegel-Report_{FileNames.Token(plantName)}_{FileNames.Date(date)}_{FileNames.Host(host)}.pdf";

    /// <summary>O6 <c>Riegel-Summary_&lt;yyyy-MM-dd&gt;.pdf</c></summary>
    public static string Summary(DateOnly date) => $"Riegel-Summary_{FileNames.Date(date)}.pdf";

    /// <summary>O7 <c>Riegel-Overview_&lt;plant&gt;_&lt;yyyy-MM-dd&gt;.xlsx</c></summary>
    public static string Workbook(string plantName, DateOnly date) => $"Riegel-Overview_{FileNames.Token(plantName)}_{FileNames.Date(date)}.xlsx";

    /// <summary>O3</summary>
    public const string ResultsCsv = "results.csv";

    /// <summary>O9 <c>Riegel-Evidence_&lt;plant&gt;_&lt;yyyy-MM-dd&gt;.zip</c></summary>
    public static string Evidence(string plantName, DateOnly date) => $"Riegel-Evidence_{FileNames.Token(plantName)}_{FileNames.Date(date)}.zip";

    /// <summary>O12 <c>Riegel-Ausnahmen-zur-Genehmigung_&lt;plant&gt;_&lt;date&gt;.pdf</c></summary>
    public static string ApprovalSheet(string plantName, DateOnly date) =>
        $"Riegel-Ausnahmen-zur-Genehmigung_{FileNames.Token(plantName)}_{FileNames.Date(date)}.pdf";

    /// <summary>O13 <c>Riegel-Soll-Konfiguration_&lt;plant&gt;_&lt;date&gt;.pdf</c></summary>
    public static string TargetConfigPdf(string plantName, DateOnly date) =>
        $"Riegel-Soll-Konfiguration_{FileNames.Token(plantName)}_{FileNames.Date(date)}.pdf";

    /// <summary>O13 <c>Riegel-Soll-Konfiguration_&lt;plant&gt;_&lt;date&gt;.xlsx</c></summary>
    public static string TargetConfigWorkbook(string plantName, DateOnly date) =>
        $"Riegel-Soll-Konfiguration_{FileNames.Token(plantName)}_{FileNames.Date(date)}.xlsx";

    /// <summary>O14 <c>Riegel-Erhebungsprotokoll_&lt;plant&gt;_&lt;importId&gt;.pdf</c></summary>
    public static string CollectionRecord(string plantName, string importId) =>
        $"Riegel-Erhebungsprotokoll_{FileNames.Token(plantName)}_{FileNames.Token(importId)}.pdf";

    /// <summary>O2 <c>runs/&lt;host&gt;/&lt;runId&gt;.results.json</c> (relativ zum Anlagenordner, mit „/“).</summary>
    public static string RunFile(string host, string runId) => $"runs/{FileNames.Host(host)}/{runId}.results.json";

    /// <summary>O10-Ordner <c>remediation/&lt;host&gt;/&lt;runId&gt;</c> (relativ, mit „/“).</summary>
    public static string RemediationFolder(string host, string runId) => $"remediation/{FileNames.Host(host)}/{runId}";

    /// <summary>Dateien in <see cref="RemediationFolder"/> (O10).</summary>
    public static IReadOnlyList<string> RemediationFiles { get; } =
        ["riegel-remediation.reg", "riegel-rollback.reg", "GptTmpl.inf", "audit.csv", "README.txt"];

    /// <summary>O15: Übergabezettel neben dem kopierten Sammler.</summary>
    public const string CollectorHandover = "UEBERGABE.txt";
}
