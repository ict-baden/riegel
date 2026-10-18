// SPDX-License-Identifier: MIT
using System.Text;

namespace Riegel.Core.Parsing;

/// <summary>
/// Kleiner CSV-Leser (RFC 4180): Felder durch <c>,</c> oder <c>;</c> getrennt (Excel mit deutscher Ländereinstellung
/// schreibt „;“), Felder in Anführungszeichen dürfen Trennzeichen, Zeilenumbrüche und verdoppelte Anführungszeichen
/// enthalten. Leere Zeilen werden übergangen.
/// </summary>
internal static class CsvText
{
    /// <summary>Eine Zeile mit ihrer Nummer in der Datei (1-basiert, Beginn der Zeile).</summary>
    internal sealed record Row(int Line, IReadOnlyList<string> Fields);

    /// <summary>Trennzeichen aus der Kopfzeile: „;“, wenn die erste Zeile „;“, aber kein „,“ außerhalb von Anführungszeichen enthält.</summary>
    internal static char DetectDelimiter(string text)
    {
        int commas = 0, semicolons = 0;
        bool quoted = false;
        foreach (char c in text)
        {
            if (c == '"') quoted = !quoted;
            else if (!quoted && (c == '\n' || c == '\r')) break;
            else if (!quoted && c == ',') commas++;
            else if (!quoted && c == ';') semicolons++;
        }
        return semicolons > 0 && commas == 0 ? ';' : ',';
    }

    /// <summary>Liest alle Zeilen. Ein offenes Anführungszeichen am Ende → <see cref="FormatException"/>.</summary>
    /// <param name="maxRows">Mehr Zeilen → <see cref="InvalidDataException"/>.</param>
    /// <param name="maxFieldChars">Längeres Feld → <see cref="InvalidDataException"/>.</param>
    internal static List<Row> Parse(string text, char delimiter, int maxRows, int maxFieldChars)
    {
        var rows = new List<Row>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, fieldStarted = false, rowHasContent = false;
        int line = 1, rowLine = 1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else
                {
                    if (c == '\n') line++;
                    field.Append(c);
                }
                if (field.Length > maxFieldChars) throw new InvalidDataException($"Feld zu lang (Zeile {rowLine}).");
                continue;
            }
            if (c == '"' && !fieldStarted)
            {
                quoted = true;
                fieldStarted = true;
                rowHasContent = true;
                continue;
            }
            if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
                rowHasContent = true;
                continue;
            }
            if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                EndRow();
                line++;
                rowLine = line;
                continue;
            }
            field.Append(c);
            fieldStarted = true;
            if (!char.IsWhiteSpace(c)) rowHasContent = true;
            if (field.Length > maxFieldChars) throw new InvalidDataException($"Feld zu lang (Zeile {rowLine}).");
        }
        if (quoted) throw new FormatException($"Anführungszeichen nicht geschlossen (Zeile {rowLine}).");
        EndRow();
        return rows;

        void EndRow()
        {
            fields.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
            if (rowHasContent)
            {
                if (rows.Count >= maxRows) throw new InvalidDataException($"Mehr als {maxRows} Zeilen.");
                rows.Add(new Row(rowLine, [.. fields]));
            }
            fields.Clear();
            rowHasContent = false;
        }
    }
}
