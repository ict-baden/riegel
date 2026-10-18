// SPDX-License-Identifier: MIT
using System.Text;

namespace Riegel.Core.Reports.Tabular;

/// <summary>
/// Schutz der Tabellenausgaben vor Werten aus Rechner- und Registerdaten (spec 7.12): Zellen, die mit
/// <c>= + - @</c>, Tabulator oder Wagenrücklauf beginnen, würden Excel oder LibreOffice sonst als Formel lesen
/// (CSV-/Formel-Einschleusung). In der CSV bekommen sie ein vorangestelltes <c>'</c>, in Excel werden sie als
/// ausdrückliche Textzelle geschrieben. Steuerzeichen, die XML oder CSV-Leser verwirren, werden ersetzt.
/// </summary>
internal static class TextGuard
{
    /// <summary>Höchstlänge einer Excel-Zelle (Excel-Spezifikation: 32.767 Zeichen).</summary>
    public const int ExcelCellMax = 32_767;

    /// <summary>Ersatz für unzulässige Zeichen.</summary>
    public const char Replacement = (char)0xFFFD;

    /// <summary>Beginnt der Text so, dass ein Tabellenprogramm ihn als Formel lesen könnte?</summary>
    public static bool IsFormulaLike(string? text) =>
        text is { Length: > 0 } && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r';

    /// <summary>
    /// Für die CSV: Steuerzeichen außer Tabulator, CR und LF ersetzen, dann bei formelartigem Anfang ein
    /// <c>'</c> voranstellen.
    /// </summary>
    public static string ForCsv(string? text)
    {
        var clean = Clean(text ?? "", keepLineBreaks: true);
        return IsFormulaLike(clean) ? "'" + clean : clean;
    }

    /// <summary>
    /// Für Excel: XML-unzulässige Zeichen ersetzen (Open XML erlaubt nur Zeichen nach XML 1.0) und auf die Zellgrenze
    /// kürzen. Ob die Zelle ausdrücklich Text wird, entscheidet <see cref="IsFormulaLike"/> am Ergebnis.
    /// </summary>
    public static string ForExcel(string? text)
    {
        var clean = Clean(text ?? "", keepLineBreaks: true);
        if (clean.Length > ExcelCellMax)
        {
            int cut = ExcelCellMax - 1;
            if (char.IsHighSurrogate(clean[cut - 1])) cut--;
            clean = clean[..cut] + "…";
        }
        return clean;
    }

    /// <summary>
    /// Ersetzt C0-Steuerzeichen (außer TAB, LF und – wenn gewünscht – CR), DEL-freie Sonderfälle U+FFFE/U+FFFF und
    /// einzelne Surrogate durch <see cref="Replacement"/>.
    /// </summary>
    public static string Clean(string text, bool keepLineBreaks)
    {
        StringBuilder? sb = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool bad;
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    sb?.Append(c).Append(text[i + 1]);
                    i++;
                    continue;
                }
                bad = true;
            }
            else if (char.IsLowSurrogate(c)) bad = true;
            else if (c is '\t') bad = false;
            else if (c is '\n' or '\r') bad = !keepLineBreaks;
            else bad = c < 0x20 || (int)c is 0xFFFE or 0xFFFF;
            if (bad)
            {
                sb ??= new StringBuilder(text, 0, i, text.Length);
                sb.Append(Replacement);
            }
            else sb?.Append(c);
        }
        return sb?.ToString() ?? text;
    }
}
