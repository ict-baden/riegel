// SPDX-License-Identifier: MIT
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using MigraDoc.DocumentObjectModel;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Riegel.Core.Model;
using Riegel.Core.Reports.Pdf;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Reports.Pdf;

/// <summary>Test, der PDF-Dokumente setzt. Ohne Schrift auf dem System (z. B. Linux ohne fonts-liberation) übersprungen.</summary>
public sealed class PdfFactAttribute : FactAttribute
{
    public PdfFactAttribute()
    {
        if (!PdfFonts.IsAvailable) Skip = PdfTest.NoFont;
    }
}

/// <summary>Wie <see cref="PdfFactAttribute"/>, für Theorien.</summary>
public sealed class PdfTheoryAttribute : TheoryAttribute
{
    public PdfTheoryAttribute()
    {
        if (!PdfFonts.IsAvailable) Skip = PdfTest.NoFont;
    }
}

/// <summary>Hilfen für die PDF-Tests: Schreiber, Seiten, Schriften und Texte im fertigen PDF, erzeugte Testbilder.</summary>
internal static class PdfTest
{
    public const string NoFont = "Keine Schrift für die PDF-Dokumente (Segoe UI, Arial, Liberation Sans, DejaVu Sans …) auf diesem System.";

    public static PdfWriter Writer(Func<string?>? cjk = null, PdfLimits? limits = null) => new(cjk, limits);

    /// <summary>Text des ganzen Dokuments (vor dem Rendern) inklusive Kopf- und Fußzeilen.</summary>
    public static string Text(BuiltDocument built) => PdfDom.Text(built.Document);

    public static Section Section(BuiltDocument built) => built.Document.Sections.Cast<Section>().Single();

    /// <summary>Seiten des fertig formatierten Dokuments (unter derselben Satzsperre wie der Schreiber).</summary>
    public static List<List<PdfLayout.Item>> Pages(BuiltDocument built) => PdfLayout.Pages(built.Document, PdfWriter.RenderGate);

    /// <summary>Umbruchfehler (leer = alles gut).</summary>
    public static List<string> Problems(BuiltDocument built) => PdfLayout.Problems(built.Document, DocumentLayout.IntroStyle, PdfWriter.RenderGate);

    /// <summary>
    /// Seite, auf der der erste Absatz mit genau diesem Text steht (1-basiert), -1 = nicht gefunden. Einträge des
    /// Inhaltsverzeichnisses (mit Seitenverweis) zählen nicht.
    /// </summary>
    public static int PageOf(List<List<PdfLayout.Item>> pages, string text)
    {
        foreach (var item in pages.SelectMany(p => p))
            if (item.Object is Paragraph p && !p.Elements.OfType<MigraDoc.DocumentObjectModel.Fields.PageRefField>().Any() && PdfDom.Text(p).Trim() == text)
                return item.Page;
        return -1;
    }

    /// <summary>Text einer Tabellenzeile, Zellen mit Tabulator getrennt.</summary>
    public static string RowText(MigraDoc.DocumentObjectModel.Tables.Row row) =>
        string.Join("\t", row.Cells.Cast<MigraDoc.DocumentObjectModel.Tables.Cell>().Select(c => PdfDom.Text(c).TrimEnd('\n')));

    public static int PageCount(string path)
    {
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return pdf.PageCount;
    }

    /// <summary>Eingebettete Schriften: BaseFont, ob eine Schriftdatei (FontFile2) dabei ist, und die ToUnicode-Abbildung.</summary>
    public sealed record EmbeddedFont(string BaseFont, string Subtype, bool HasFontFile2, bool HasFontFile3, IReadOnlyDictionary<int, int> UnicodeToCid);

    public static List<EmbeddedFont> Fonts(string path)
    {
        var fonts = new List<EmbeddedFont>();
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
        foreach (var obj in pdf.Internals.GetAllObjects())
        {
            if (obj is not PdfDictionary d || d.Elements.GetName("/Type") != "/Font" || d.Elements.GetName("/Subtype") != "/Type0") continue;
            var descendants = d.Elements.GetArray("/DescendantFonts");
            var cid = descendants?.Elements.Count > 0 && descendants.Elements[0] is PdfReference r ? r.Value as PdfDictionary : null;
            var descriptor = cid?.Elements.GetDictionary("/FontDescriptor");
            var map = new Dictionary<int, int>();
            if (d.Elements["/ToUnicode"] is PdfReference tu && tu.Value is PdfDictionary toUnicode && toUnicode.Stream is not null)
                ParseToUnicode(Encoding.ASCII.GetString(toUnicode.Stream.UnfilteredValue), map);
            fonts.Add(new EmbeddedFont(d.Elements.GetName("/BaseFont"), d.Elements.GetName("/Subtype"),
                descriptor?.Elements.ContainsKey("/FontFile2") == true, descriptor?.Elements.ContainsKey("/FontFile3") == true, map));
        }
        return fonts;
    }

    /// <summary>bfrange-Einträge „&lt;CID&gt;&lt;CID&gt;&lt;Unicode&gt;“ der ToUnicode-CMap von PDFsharp.</summary>
    private static void ParseToUnicode(string cmap, Dictionary<int, int> map)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(cmap, @"<([0-9A-Fa-f]{4})><([0-9A-Fa-f]{4})><([0-9A-Fa-f]{4,8})>");
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            int first = Convert.ToInt32(m.Groups[1].Value, 16), last = Convert.ToInt32(m.Groups[2].Value, 16);
            var hex = m.Groups[3].Value;
            int unicode = hex.Length == 8 ? char.ConvertToUtf32((char)Convert.ToInt32(hex[..4], 16), (char)Convert.ToInt32(hex[4..], 16)) : Convert.ToInt32(hex, 16);
            for (int c = first; c <= last; c++) map[unicode + (c - first)] = c;
        }
    }

    /// <summary>Ein gültiges PNG (RGB, 8 Bit) ohne Bilddatei im Repo: Kopf, IHDR, IDAT (zlib), IEND.</summary>
    public static byte[] Png(int width, int height, int? declaredWidth = null, int? declaredHeight = null)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, declaredWidth ?? width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), declaredHeight ?? height);
        ihdr[8] = 8; // Bittiefe
        ihdr[9] = 2; // RGB
        Chunk(output, "IHDR", ihdr);
        using var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0); // kein Filter
            for (int x = 0; x < width; x++)
            {
                bool left = x < width / 2;
                raw.Write(left ? [0xF3, 0x82, 0x1D] : [0xAC, 0x28, 0x1B]);
            }
        }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw.ToArray());
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        output.Write(typeAndData);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData));
        output.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    /// <summary>Ein 16 × 8 Pixel großes JPEG (für diesen Test mit Pillow erzeugt, zwei Farbflächen in den Produktfarben).</summary>
    public static byte[] Jpeg() => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAYEBQYFBAYGBQYHBwYIChAKCgkJChQODwwQFxQYGBcUFhYaHSUfGhsjHBYWICwgIyYnKSopGR8tMC0oMCUoKSj/2wBDAQcHBwoIChMKChMoGhYaKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCj/wAARCAAIABADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDoK8coor4bKvt/L9T9LR//2Q==");

    /// <summary>Text eines PdfStrings-Schlüssels in einer Sprache.</summary>
    public static string S(string key, string language = "de") => new PdfTexts(language).T(key);

    /// <summary>Formatierter PdfStrings-Text in einer Sprache.</summary>
    public static string F(string key, string language, params object?[] args) => new PdfTexts(language).F(key, args);
}
