// SPDX-License-Identifier: MIT
using ClosedXML.Excel;

namespace Riegel.Core.Reports.Tabular;

/// <summary>Ein Zellwert vor dem Schreiben: Text (immer mit Formel-Schutz), Zahl, Datum, Zeitpunkt, Wahrheitswert oder leer.</summary>
internal readonly record struct CellValue(CellKind Kind, string? Text = null, double Number = 0, DateTime Time = default, bool Flag = false)
{
    public static CellValue Empty { get; } = new(CellKind.Empty);

    public static CellValue Of(string? text) => text is null ? Empty : new(CellKind.Text, text);

    public static CellValue Of(int? number) => number is { } n ? new(CellKind.Number, Number: n) : Empty;

    public static CellValue Of(long? number) => number is { } n ? new(CellKind.Number, Number: n) : Empty;

    public static CellValue Of(double? number) => number is { } n ? new(CellKind.Number, Number: n) : Empty;

    public static CellValue Of(bool? flag) => flag is { } f ? new(CellKind.Bool, Flag: f) : Empty;

    public static CellValue Of(DateOnly? date) =>
        date is { } d ? new(CellKind.Date, Time: d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified)) : Empty;

    public static CellValue Of(DateTimeOffset? time) =>
        time is { } t ? new(CellKind.Timestamp, Time: DateTime.SpecifyKind(t.UtcDateTime, DateTimeKind.Unspecified)) : Empty;

    public static implicit operator CellValue(string? text) => Of(text);
    public static implicit operator CellValue(int number) => Of(number);
    public static implicit operator CellValue(int? number) => Of(number);
    public static implicit operator CellValue(bool flag) => Of(flag);
    public static implicit operator CellValue(bool? flag) => Of(flag);
    public static implicit operator CellValue(DateOnly date) => Of(date);
    public static implicit operator CellValue(DateOnly? date) => Of(date);
    public static implicit operator CellValue(DateTimeOffset time) => Of(time);
    public static implicit operator CellValue(DateTimeOffset? time) => Of(time);
}

internal enum CellKind { Empty, Text, Number, Bool, Date, Timestamp }

/// <summary>
/// Baut ein Tabellenblatt einheitlich: Kopfzeile (fett), Autofilter über die Tabelle, fixierte Kopfzeile (und auf
/// Wunsch erste Spalte), Spaltenbreiten aus dem Inhalt (deterministisch, ohne Schriftmessung), Daten im ISO-Format
/// <c>yyyy-mm-dd</c>, Zeitpunkte als UTC <c>yyyy-mm-ddThh:mm:ssZ</c>. Jeder Text läuft durch <see cref="TextGuard"/>.
/// </summary>
internal sealed class SheetBuilder
{
    /// <summary>Zahlenformat für Daten (ISO 8601).</summary>
    public const string DateFormat = "yyyy-mm-dd";

    /// <summary>Zahlenformat für Zeitpunkte (ISO 8601, UTC).</summary>
    public const string TimestampFormat = "yyyy-mm-dd\"T\"hh:mm:ss\"Z\"";

    private const int MinWidth = 8;
    private const int MaxWidth = 60;

    private readonly IXLWorksheet _sheet;
    private readonly int[] _widths;
    private int _row = 1;

    public SheetBuilder(XLWorkbook workbook, string name, IReadOnlyList<string> headers, bool freezeFirstColumn = false)
    {
        _sheet = workbook.Worksheets.Add(SheetName(name));
        FreezeFirstColumn = freezeFirstColumn;
        Columns = headers.Count;
        _widths = new int[headers.Count];
        for (int c = 0; c < headers.Count; c++)
        {
            var cell = _sheet.Cell(1, c + 1);
            SetText(cell, headers[c]);
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0xE7, 0xE6, 0xE6);
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            Track(c, headers[c]);
        }
    }

    public IXLWorksheet Sheet => _sheet;

    public int Columns { get; }

    public bool FreezeFirstColumn { get; }

    /// <summary>Zahl der Datenzeilen.</summary>
    public int Rows => _row - 1;

    /// <summary>Eine Datenzeile; fehlende Werte am Ende bleiben leer.</summary>
    public void Add(params CellValue[] values)
    {
        if (values.Length > Columns) throw new ArgumentException("Mehr Werte als Spalten.", nameof(values));
        _row++;
        for (int c = 0; c < values.Length; c++) Write(_sheet.Cell(_row, c + 1), values[c], c);
    }

    /// <summary>Kopfzeile, Autofilter, fixierte Bereiche und Breiten setzen.</summary>
    public void Finish(IReadOnlyList<string>? notesBelow = null)
    {
        _sheet.Range(1, 1, Math.Max(1, _row), Columns).SetAutoFilter();
        if (FreezeFirstColumn) _sheet.SheetView.Freeze(1, 1);
        else _sheet.SheetView.FreezeRows(1);
        if (notesBelow is { Count: > 0 })
        {
            // Hinweise unter der Tabelle, durch eine Leerzeile getrennt und außerhalb des Filterbereichs
            int row = _row + 2;
            foreach (var note in notesBelow)
            {
                var cell = _sheet.Cell(row++, 1);
                SetText(cell, note);
                cell.Style.Font.Italic = true;
            }
        }
        for (int c = 0; c < Columns; c++)
            _sheet.Column(c + 1).Width = Math.Clamp(_widths[c] + 2, MinWidth, MaxWidth);
    }

    private void Write(IXLCell cell, CellValue v, int column)
    {
        switch (v.Kind)
        {
            case CellKind.Text:
                SetText(cell, v.Text);
                Track(column, v.Text);
                break;
            case CellKind.Number:
                cell.Value = v.Number;
                Track(column, v.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case CellKind.Bool:
                cell.Value = v.Flag;
                Track(column, new string('x', 6));
                break;
            case CellKind.Date:
                cell.Value = v.Time;
                cell.Style.NumberFormat.Format = DateFormat;
                Track(column, "yyyy-mm-dd");
                break;
            case CellKind.Timestamp:
                cell.Value = v.Time;
                cell.Style.NumberFormat.Format = TimestampFormat;
                Track(column, "yyyy-mm-ddThh:mm:ssZ");
                break;
        }
    }

    /// <summary>
    /// Text immer als Textzelle (nie als Formel). Formelartige Werte (<see cref="TextGuard.IsFormulaLike"/>) bekommen
    /// zusätzlich das Textformat „@“ und das Anführungszeichen-Präfix, damit sie auch nach dem Bearbeiten in Excel Text
    /// bleiben.
    /// </summary>
    internal static void SetText(IXLCell cell, string? text)
    {
        var value = TextGuard.ForExcel(text);
        cell.Value = value;
        if (TextGuard.IsFormulaLike(value))
        {
            cell.Style.NumberFormat.Format = "@";
            cell.Style.IncludeQuotePrefix = true;
        }
    }

    private void Track(int column, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        int longest = 0;
        foreach (var line in text.Split('\n')) longest = Math.Max(longest, line.Length);
        _widths[column] = Math.Max(_widths[column], Math.Min(longest, MaxWidth));
    }

    /// <summary>
    /// Gültiger Blattname: höchstens 31 Zeichen, ohne <c>[ ] : * ? / \</c> und ohne Apostroph am Rand (Excel-Regeln).
    /// </summary>
    internal static string SheetName(string name)
    {
        var chars = name.Select(c => c is '[' or ']' or ':' or '*' or '?' or '/' or '\\' ? '-' : c).ToArray();
        var result = new string(chars).Trim('\'', ' ');
        if (result.Length > 31) result = result[..31];
        return result.Length == 0 ? "Sheet" : result;
    }
}

/// <summary>Kleine Helfer für die Werte der Blätter.</summary>
internal static class Cells
{
    public static string Join(IEnumerable<string>? values, string separator = "; ") =>
        values is null ? "" : string.Join(separator, values.Where(v => !string.IsNullOrEmpty(v)));

    /// <summary>Die Einträge, bei weiteren Einträgen mit „…“ am Ende.</summary>
    public static string JoinCapped(IReadOnlyList<string> values, bool more, string separator = "; ") =>
        string.Join(separator, values) + (more ? separator + "…" : "");
}
