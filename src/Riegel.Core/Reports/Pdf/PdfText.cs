// SPDX-License-Identifier: MIT
using System.Text;
using MigraDoc.DocumentObjectModel;
using PdfSharp.Drawing;

namespace Riegel.Core.Reports.Pdf;

/// <summary>
/// (Aus Zutritt übernommen.) Setzt Text in MigraDoc-Absätze und bricht überlange Wörter – Registry-Pfade, Prüfsummen,
/// chinesische Sätze ohne Leerzeichen – selbst um. MigraDoc bricht nur an Leerzeichen um und ließe solche Wörter über
/// den Zellenrand hinauslaufen. Getrennt wird bevorzugt nach „\“, „/“, „_“, „-“, „.“ und „:“, notfalls mitten im Wort,
/// und nur dort, wo es nötig ist: kurze Werte bleiben beim Kopieren aus dem PDF unverändert.
/// </summary>
internal sealed class PdfText : IDisposable
{
    /// <summary>Etwas Luft gegenüber der gemessenen Breite (Rundung, Glyphen-Überhang).</summary>
    private const double Safety = 0.96;

    /// <summary>
    /// Mehr Zeilen bekommt eine Zelle nicht. MigraDoc kann Tabellenzeilen nicht über Seiten teilen; eine Zeile höher als
    /// die Seite würde abgeschnitten.
    /// </summary>
    internal const int MaxLines = 30;

    private readonly string _family;
    private readonly string _shortened;
    private readonly XGraphics _gfx;
    private readonly Dictionary<(double Size, bool Bold), XFont> _fonts = [];
    private readonly Dictionary<(double Size, bool Bold, string Glyph), double> _glyphs = [];

    /// <param name="shortened">Anhang an gekürzte Texte, z. B. „ … (gekürzt – vollständig im Excel-Bericht)“.</param>
    public PdfText(string family, string shortened)
    {
        _family = family;
        _shortened = shortened;
        _gfx = XGraphics.CreateMeasureContext(new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);
    }

    /// <summary>
    /// Hängt <paramref name="text"/> an <paramref name="paragraph"/> an; keine Zeile wird breiter als
    /// <paramref name="width"/> Punkt. <paramref name="size"/> und <paramref name="bold"/> müssen der Schrift des Absatzes
    /// entsprechen, sonst stimmt die Messung nicht.
    /// </summary>
    public void Append(Paragraph paragraph, string? text, double width, double size, bool bold = false, Color? color = null)
    {
        var segments = Split(Limit(Clean(text), width, size), width, size, bold);
        for (int i = 0; i < segments.Count; i++)
        {
            if (i > 0) paragraph.AddLineBreak();
            if (segments[i].Length == 0) continue;
            if (bold || color is not null)
            {
                var formatted = paragraph.AddFormattedText(segments[i]);
                if (bold) formatted.Bold = true;
                if (color is { } c) formatted.Color = c;
            }
            else paragraph.AddText(segments[i]);
        }
    }

    /// <summary>
    /// Entfernt Zeichen, die keine Schrift darstellen kann: Steuerzeichen und einzelne Surrogat-Hälften (Exporte und
    /// Register sind fremde Eingaben). Ersetzt sie durch „�“, damit sichtbar bleibt, dass dort etwas stand.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        int i = 0;
        while (i < text.Length && IsValidAt(text, i, out int step)) i += step;
        if (i == text.Length) return text;
        var sb = new StringBuilder(text.Length);
        sb.Append(text, 0, i);
        while (i < text.Length)
        {
            if (IsValidAt(text, i, out int length))
            {
                sb.Append(text, i, length);
                i += length;
            }
            else
            {
                sb.Append('�');
                i++;
            }
        }
        return sb.ToString();
    }

    private static bool IsValidAt(string text, int i, out int length)
    {
        char c = text[i];
        length = 1;
        if (char.IsHighSurrogate(c))
        {
            if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { length = 2; return true; }
            return false;
        }
        if (char.IsLowSurrogate(c)) return false;
        return c is '\n' or '\r' || (c >= 0x20 && c != 0x7F && c != 0xFFFE && c != 0xFFFF && !(c >= 0x80 && c < 0xA0));
    }

    /// <summary>
    /// Kürzt Texte, die umbrochen höher als <see cref="MaxLines"/> Zeilen würden. Zählt auch die Zeilenumbrüche im Text.
    /// </summary>
    internal string Limit(string text, double width, double size)
    {
        // Durchschnittliche Zeichenbreite grob ½ Geviert – reicht als Schätzung (CJK: ganzes Geviert, siehe unten)
        int perLine = Math.Max(10, (int)(width / (0.5 * size)));
        int lines = 0;
        int start = 0;
        while (true)
        {
            int end = text.IndexOf('\n', start);
            int stop = end < 0 ? text.Length : end;
            int weight = 0;
            for (int k = start; k < stop; k++) weight += text[k] >= 0x2E80 ? 2 : 1;
            int needed = Math.Max(1, (weight + perLine - 1) / perLine);
            if (lines + needed > MaxLines)
            {
                int budget = (MaxLines - lines) * perLine;
                int cut = start;
                for (int used = 0; cut < stop && used + (text[cut] >= 0x2E80 ? 2 : 1) <= budget; cut++) used += text[cut] >= 0x2E80 ? 2 : 1;
                if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--;
                return text[..cut].TrimEnd('\r', '\n', ' ') + _shortened;
            }
            lines += needed;
            if (end < 0) return text;
            start = end + 1;
        }
    }

    /// <summary>
    /// Zerlegt <paramref name="text"/> in Abschnitte, zwischen denen ein erzwungener Zeilenumbruch nötig ist. Normale
    /// Wörter bleiben zusammen (die bricht MigraDoc selbst an Leerzeichen um).
    /// </summary>
    internal IReadOnlyList<string> Split(string text, double width, double size, bool bold)
    {
        var font = Font(size, bold);
        double available = Math.Max(1, width * Safety);
        var segments = new List<string>();
        var current = new StringBuilder();
        double x = 0;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int l = 0; l < lines.Length; l++)
        {
            if (l > 0)
            {
                segments.Add(current.ToString());
                current.Clear();
                x = 0;
            }
            foreach (var word in Words(lines[l]))
            {
                var core = word.TrimEnd(' ');
                double coreWidth = core.Length == 0 ? 0 : Measure(core, font);
                if (core.Length == 0 || coreWidth <= available)
                {
                    // MigraDoc bricht vor diesem Wort um, wenn es nicht mehr in die Zeile passt
                    if (x > 0 && x + coreWidth > available) x = 0;
                    current.Append(word);
                    x += Measure(word, font);
                    continue;
                }
                // Überlanges Wort: in der laufenden Zeile weitermachen, dann Stücke von Zeilenbreite
                var pieces = Pieces(core, font, available, x);
                for (int p = 0; p < pieces.Count; p++)
                {
                    current.Append(pieces[p]);
                    if (p < pieces.Count - 1)
                    {
                        segments.Add(current.ToString());
                        current.Clear();
                    }
                }
                current.Append(word, core.Length, word.Length - core.Length);
                x = Measure(pieces[^1] + word[core.Length..], font);
            }
        }
        segments.Add(current.ToString());
        return segments;
    }

    /// <summary>Wörter samt folgender Leerzeichen.</summary>
    private static IEnumerable<string> Words(string line)
    {
        int start = 0;
        int i = 0;
        while (i < line.Length)
        {
            while (i < line.Length && line[i] != ' ') i++;
            while (i < line.Length && line[i] == ' ') i++;
            yield return line[start..i];
            start = i;
        }
    }

    /// <summary>
    /// Ein zu langes Wort in Stücke teilen, die jeweils in die Breite passen; das erste Stück füllt den Rest der
    /// laufenden Zeile ab <paramref name="startX"/> (bei Chinesisch sonst halbleere Zeilen vor jedem Satz).
    /// </summary>
    private List<string> Pieces(string word, XFont font, double available, double startX)
    {
        var pieces = new List<string>();
        int start = 0;
        int lastBreak = -1;
        double width = 0;
        double limit = available - startX;
        // Passt in den Rest nicht einmal ein Zeichen sinnvoll, beginnt das Wort in der nächsten Zeile
        if (limit < available * 0.25) limit = available;
        int i = 0;
        while (i < word.Length)
        {
            int length = char.IsHighSurrogate(word[i]) && i + 1 < word.Length && char.IsLowSurrogate(word[i + 1]) ? 2 : 1;
            double glyph = Glyph(word.Substring(i, length), font);
            if (width + glyph > limit && i > start)
            {
                int cut = lastBreak > start ? lastBreak : i;
                pieces.Add(word[start..cut]);
                start = cut;
                lastBreak = -1;
                limit = available;
                width = start < i ? Measure(word[start..i], font) : 0;
                continue; // dasselbe Zeichen gegen die neue Zeile prüfen
            }
            width += glyph;
            i += length;
            if (IsBreakAfter(word[i - 1])) lastBreak = i;
        }
        if (start < word.Length) pieces.Add(word[start..]);
        if (pieces.Count == 0) pieces.Add("");
        return pieces;
    }

    private static bool IsBreakAfter(char c) => c is '\\' or '/' or '_' or '-' or '.' or ',' or ';' or ':' or '，' or '、' or '。' or '：' or '；';

    private XFont Font(double size, bool bold)
    {
        if (!_fonts.TryGetValue((size, bold), out var font))
            _fonts[(size, bold)] = font = new XFont(_family, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        return font;
    }

    /// <summary>
    /// Zeilen, die MigraDoc für <paramref name="text"/> in <paramref name="width"/> Punkt braucht – zum Abschätzen von
    /// Tabellenhöhen. Bricht wie MigraDoc an Leerzeichen um; überlange Wörter hat <see cref="Append"/> schon mit
    /// Zeilenumbrüchen versehen.
    /// </summary>
    internal int Lines(string text, double width, double size, bool bold = false)
    {
        var font = Font(size, bold);
        double available = Math.Max(1, width * Safety);
        int lines = 0;
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            lines++;
            double x = 0;
            foreach (var word in Words(line))
            {
                if (x > 0 && x + Measure(word.TrimEnd(' '), font) > available)
                {
                    lines++;
                    x = 0;
                }
                x += Measure(word, font);
            }
        }
        return lines;
    }

    /// <summary>Breite in Punkt, so wie MigraDoc sie setzt.</summary>
    internal double Width(string text, double size, bool bold = false) => Measure(text, Font(size, bold));

    private double Measure(string text, XFont font) => text.Length == 0 ? 0 : _gfx.MeasureString(text, font).Width;

    private double Glyph(string glyph, XFont font)
    {
        var key = (font.Size, font.Bold, glyph);
        if (!_glyphs.TryGetValue(key, out var width))
            _glyphs[key] = width = Measure(glyph, font);
        return width;
    }

    public void Dispose() => _gfx.Dispose();
}
