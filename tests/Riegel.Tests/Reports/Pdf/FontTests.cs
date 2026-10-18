// SPDX-License-Identifier: MIT
using System.Buffers.Binary;
using System.Text;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Reports.Pdf;
using Riegel.Core.Samples;
using Riegel.Tests.TestSupport;
using Xunit.Abstractions;
using static Riegel.Tests.Reports.Pdf.PdfTest;

namespace Riegel.Tests.Reports.Pdf;

/// <summary>
/// Schriften (spec 7.10): Chinesisch mit einer System-Schrift mit TrueType-Konturen (Sammlungen im Speicher
/// aufgeschnitten), CFF-Schriften abgelehnt, sonst Englisch mit RGL-0510. Riegel liefert keine Schriftdateien aus.
/// </summary>
public sealed class FontTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static ReportData Chinese()
    {
        var r = SampleData.Report();
        return r with { Header = r.Header with { Language = "zh-Hans", PreTranslated = true } };
    }

    /// <summary>Zeichen aus Titel und Überschriften des chinesischen Berichts.</summary>
    private const string ChineseProbe = "安全加固证明报告管理层摘要";

    [PdfFact]
    public void Chinesisch_bettet_die_Glyphen_ein_oder_fällt_dokumentiert_auf_Englisch_zurück()
    {
        var path = Writer().WriteReport(Chinese(), new PdfOptions(_temp.Path));
        var family = PdfFonts.CjkFamily;
        var fonts = Fonts(path);
        // Für das Protokoll der CI (trx): welche Schrift dieses System nimmt und was verworfen wurde
        output.WriteLine($"Latein: {PdfFonts.LatinFamily}; CJK: {family ?? "(keine – Rückfall auf Englisch, RGL-0510)"}");
        foreach (var line in PdfFonts.CjkSearchLog) output.WriteLine("CJK-Suche: " + line);
        foreach (var f in fonts) output.WriteLine($"eingebettet: {f.BaseFont} FontFile2={f.HasFontFile2} FontFile3={f.HasFontFile3} Glyphen={f.UnicodeToCid.Count}");
        output.WriteLine($"PDF: {new FileInfo(path).Length} Bytes");
        if (family is null)
        {
            // Rückfall (spec 7.10): Englisch, Hinweis RGL-0510 auf dem Deckblatt
            var built = Writer().BuildReport(Chinese());
            Assert.Equal("en", built.Language);
            Assert.Contains(RglCodesPdf.NoFontForLanguage, Text(built));
            return;
        }
        var cjk = fonts.Where(f => f.BaseFont.Contains(family.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)
            || f.BaseFont.Contains(family, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(cjk.Count > 0, $"Keine eingebettete Schrift {family}; vorhanden: {string.Join(", ", fonts.Select(f => f.BaseFont))}");
        Assert.All(cjk, f => Assert.True(f.HasFontFile2 && !f.HasFontFile3, $"{f.BaseFont}: TrueType-Schrift erwartet (FontFile2)"));
        // Teilmenge (BaseFont „ABCDEF+Name“) – nicht die ganze Schrift
        Assert.All(cjk, f => Assert.Matches("^/?[A-Z]{6}\\+", f.BaseFont));
        var map = cjk.SelectMany(f => f.UnicodeToCid).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First().Value);
        foreach (var c in ChineseProbe)
        {
            Assert.True(map.TryGetValue(c, out var cid), $"Zeichen {c} (U+{(int)c:X4}) nicht eingebettet");
            Assert.True(cid != 0, $"Zeichen {c} auf .notdef abgebildet");
        }
        // Die Datei bleibt klein: nur die benutzten Glyphen
        Assert.True(new FileInfo(path).Length < 3_000_000, $"PDF {new FileInfo(path).Length} Bytes – Schrift nicht als Teilmenge eingebettet?");
    }

    [PdfFact]
    public void Ohne_CJK_Schrift_Englisch_mit_RGL_0510_auf_dem_Deckblatt_und_ohne_Vorübersetzungsvermerk()
    {
        var w = Writer(cjk: () => null);
        foreach (var built in new[]
        {
            w.BuildReport(Chinese()),
            w.BuildSummary(SampleData.Summary() with { Language = "zh-Hans", PreTranslated = true }),
            w.BuildTargetConfig(SampleData.TargetConfig() with { Language = "zh-Hans", PreTranslated = true }),
            w.BuildApprovalSheet(SampleData.ApprovalSheet() with { Language = "zh-Hans", PreTranslated = true }),
            w.BuildCollectionRecord(SampleData.CollectionRecord() with { Language = "zh-Hans", PreTranslated = true }),
        })
        {
            Assert.Equal("en", built.Language);
            Assert.Equal("zh-Hans", built.FallbackFrom);
            var text = Text(built);
            Assert.Contains("RGL-0510: No font with Chinese characters (TrueType outlines) was found on this system for language zh-Hans; this document is therefore written in English.", text);
            Assert.DoesNotContain("Machine pre-translation", text);
            Assert.DoesNotContain("安全", text);
        }
        var report = w.BuildReport(Chinese());
        Assert.Equal("Hardening evidence report", report.Title);
        // Der Hinweis steht auf dem Deckblatt
        var pages = Pages(report);
        var note = pages.SelectMany(p => p).First(i => i.Object is MigraDoc.DocumentObjectModel.Tables.Table t && PdfDom.Text(t).Contains("RGL-0510"));
        Assert.Equal(1, note.Page);
        var path = Path.Combine(_temp.Path, "fallback.pdf");
        PdfWriter.Save(report, path);
        Assert.True(PageCount(path) > 5);
    }

    [PdfFact]
    public void Chinesischer_Bericht_hat_keinen_Umbruchfehler()
    {
        if (PdfFonts.CjkFamily is null) return; // dann gilt der Rückfall-Test
        var problems = Problems(Writer().BuildReport(Chinese()));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void CJK_Suche_protokolliert_was_sie_geprüft_hat()
    {
        if (!PdfFonts.IsAvailable) return;
        var log = PdfFonts.CjkSearchLog;
        Assert.NotEmpty(log);
        if (PdfFonts.CjkFamily is { } family) Assert.Contains(log, l => l.StartsWith(family + ":", StringComparison.Ordinal) && l.Contains("– used", StringComparison.Ordinal));
    }

    /// <summary>Installierte Schriften mit CFF-Konturen (Linux-Runner: Noto Sans CJK; Windows: Source Han Sans, falls vorhanden).</summary>
    public static TheoryData<string> CffFonts()
    {
        var data = new TheoryData<string>();
        string[] candidates =
        [
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/opentype/noto/NotoSerifCJK-Regular.ttc",
        ];
        foreach (var c in candidates.Where(File.Exists)) data.Add(c);
        foreach (var dir in PdfFonts.Directories().Where(Directory.Exists))
            foreach (var f in Directory.EnumerateFiles(dir, "SourceHanSans*.otf").Take(1)) data.Add(f);
        if (data.Count == 0) data.Add("");
        return data;
    }

    [Theory]
    [MemberData(nameof(CffFonts))]
    public void CFF_Schriften_werden_erkannt_und_abgelehnt(string file)
    {
        if (file.Length == 0) return; // keine CFF-Schrift auf diesem System
        var bytes = File.ReadAllBytes(file);
        var faces = Sfnt.Faces(bytes);
        output.WriteLine($"{file}: {faces.Count} Schnitte, {string.Join(", ", faces.Select(f => $"{f.Family} ({f.Outlines})"))}, collection={Sfnt.IsCollection(bytes)}");
        Assert.All(faces, f => Assert.Equal(SfntOutlines.Cff, f.Outlines));
        Assert.Null(PdfFonts.Load(file, faces[0].Family ?? "", out var reason));
        Assert.Contains("CFF", reason);
    }

    // ---- Sfnt mit erzeugten Schriften (keine Schriftdatei im Repo) -------------------------------------

    /// <summary>Minimale sfnt-Datei: head, loca, glyf, name (Familie), cmap Format 4 für die angegebenen Zeichen.</summary>
    private static byte[] FakeFont(string family, uint version, params char[] mapped)
    {
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["head"] = new byte[54],
            ["loca"] = new byte[8],
            ["glyf"] = new byte[4],
            ["name"] = NameTable(family),
            ["cmap"] = CmapTable(mapped),
        };
        if (version == 0x4F54544F) { tables.Remove("glyf"); tables.Remove("loca"); tables["CFF "] = new byte[8]; }
        return Assemble(version, tables, 0);
    }

    private static byte[] Assemble(uint version, SortedDictionary<string, byte[]> tables, int baseOffset)
    {
        int n = tables.Count;
        var output = new List<byte>();
        var header = new byte[12 + 16 * n];
        BinaryPrimitives.WriteUInt32BigEndian(header, version);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)n);
        int position = baseOffset + header.Length;
        var data = new List<byte>();
        int i = 0;
        foreach (var (tag, bytes) in tables)
        {
            var record = header.AsSpan(12 + 16 * i);
            Encoding.ASCII.GetBytes(tag, record);
            BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)(position + data.Count));
            BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)bytes.Length);
            data.AddRange(bytes);
            while (data.Count % 4 != 0) data.Add(0);
            i++;
        }
        output.AddRange(header);
        output.AddRange(data);
        return [.. output];
    }

    private static byte[] NameTable(string family)
    {
        var text = Encoding.BigEndianUnicode.GetBytes(family);
        var t = new byte[6 + 12 + text.Length];
        BinaryPrimitives.WriteUInt16BigEndian(t.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(t.AsSpan(4), 18);
        var r = t.AsSpan(6);
        BinaryPrimitives.WriteUInt16BigEndian(r, 3);
        BinaryPrimitives.WriteUInt16BigEndian(r[2..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(r[4..], 0x0409);
        BinaryPrimitives.WriteUInt16BigEndian(r[6..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(r[8..], (ushort)text.Length);
        text.CopyTo(t, 18);
        return t;
    }

    /// <summary>cmap mit einer Untertabelle (3,1) Format 4: je Zeichen ein Segment auf Glyphe 1, dazu das Endsegment 0xFFFF.</summary>
    private static byte[] CmapTable(char[] mapped)
    {
        var chars = mapped.Distinct().Order().ToList();
        int segments = chars.Count + 1;
        int length = 16 + 8 * segments;
        var sub = new byte[length];
        BinaryPrimitives.WriteUInt16BigEndian(sub, 4);
        BinaryPrimitives.WriteUInt16BigEndian(sub.AsSpan(2), (ushort)length);
        BinaryPrimitives.WriteUInt16BigEndian(sub.AsSpan(6), (ushort)(segments * 2));
        int ends = 14, starts = 16 + 2 * segments, deltas = starts + 2 * segments, ranges = deltas + 2 * segments;
        for (int s = 0; s < segments; s++)
        {
            ushort c = s < chars.Count ? chars[s] : (ushort)0xFFFF;
            BinaryPrimitives.WriteUInt16BigEndian(sub.AsSpan(ends + 2 * s), c);
            BinaryPrimitives.WriteUInt16BigEndian(sub.AsSpan(starts + 2 * s), c);
            BinaryPrimitives.WriteInt16BigEndian(sub.AsSpan(deltas + 2 * s), (short)(s < chars.Count ? 1 - c : 1));
            BinaryPrimitives.WriteUInt16BigEndian(sub.AsSpan(ranges + 2 * s), 0);
        }
        var t = new byte[4 + 8 + sub.Length];
        BinaryPrimitives.WriteUInt16BigEndian(t.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(t.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt16BigEndian(t.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt32BigEndian(t.AsSpan(8), 12);
        sub.CopyTo(t, 12);
        return t;
    }

    /// <summary>Sammlung aus mehreren Schriften: ttcf-Kopf, dann die Schriften mit Tabellen-Offsets relativ zum Dateianfang.</summary>
    private static byte[] Collection(params (string Family, uint Version, char[] Mapped)[] fonts)
    {
        int headerLength = 12 + 4 * fonts.Length;
        var output = new List<byte>(new byte[headerLength]);
        var offsets = new List<int>();
        foreach (var (family, version, mapped) in fonts)
        {
            var single = FakeFont(family, version, mapped);
            // Tabellen-Offsets um die Position in der Sammlung verschieben
            int at = output.Count;
            int n = BinaryPrimitives.ReadUInt16BigEndian(single.AsSpan(4));
            for (int i = 0; i < n; i++)
            {
                var record = single.AsSpan(12 + 16 * i + 8);
                BinaryPrimitives.WriteUInt32BigEndian(record, BinaryPrimitives.ReadUInt32BigEndian(record) + (uint)at);
            }
            offsets.Add(at);
            output.AddRange(single);
        }
        var bytes = output.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 0x74746366);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), (uint)fonts.Length);
        for (int i = 0; i < offsets.Count; i++) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12 + 4 * i), (uint)offsets[i]);
        return bytes;
    }

    [Fact]
    public void Sammlung_wird_erkannt_und_ein_Schnitt_eigenständig_herausgelöst()
    {
        var ttc = Collection(("Test Hei", 0x00010000, ['中', '文', '报', '告']), ("Test Hei UI", 0x00010000, ['A']));
        Assert.True(Sfnt.IsCollection(ttc));
        var faces = Sfnt.Faces(ttc);
        Assert.Equal(["Test Hei", "Test Hei UI"], faces.Select(f => f.Family));
        Assert.All(faces, f => Assert.Equal(SfntOutlines.TrueType, f.Outlines));

        var single = Sfnt.Extract(ttc, 1);
        Assert.False(Sfnt.IsCollection(single));
        var face = Assert.Single(Sfnt.Faces(single));
        Assert.Equal("Test Hei UI", face.Family);
        // Tabellen sortiert und 4-Byte-ausgerichtet, Prüfsumme der ganzen Datei = 0xB1B0AFBA (über checkSumAdjustment)
        int n = BinaryPrimitives.ReadUInt16BigEndian(single.AsSpan(4));
        var tags = Enumerable.Range(0, n).Select(i => Encoding.ASCII.GetString(single, 12 + 16 * i, 4)).ToList();
        Assert.Equal(tags.Order(StringComparer.Ordinal), tags);
        Assert.All(Enumerable.Range(0, n), i => Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(single.AsSpan(12 + 16 * i + 8)) % 4));
        uint sum = 0;
        for (int i = 0; i + 4 <= single.Length; i += 4) sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(single.AsSpan(i)));
        Assert.Equal(0xB1B0AFBAu, sum);
    }

    [Fact]
    public void Glyphenprüfung_über_cmap()
    {
        var ttc = Collection(("Test Hei", 0x00010000, ['中', '文', '报', '告']), ("Nur Latein", 0x00010000, ['A', 'B']));
        Assert.True(Sfnt.HasGlyphs(ttc, 0, [0x4E2D, 0x6587, 0x62A5, 0x544A]));
        Assert.False(Sfnt.HasGlyphs(ttc, 1, [0x4E2D]));
        Assert.True(Sfnt.HasGlyphs(ttc, 1, ['A']));
    }

    [Fact]
    public void CFF_in_einer_Sammlung_wird_als_CFF_erkannt()
    {
        var ttc = Collection(("Test Sans CJK", 0x4F54544F, ['中']));
        Assert.Equal(SfntOutlines.Cff, Assert.Single(Sfnt.Faces(ttc)).Outlines);
        var file = Path.Combine(_temp.Path, "cff.ttc");
        File.WriteAllBytes(file, ttc);
        Assert.Null(PdfFonts.Load(file, "Test Sans CJK", out var reason));
        Assert.Contains("CFF", reason);
    }

    [Fact]
    public void Schrift_ohne_chinesische_Zeichen_wird_abgelehnt()
    {
        var file = Path.Combine(_temp.Path, "latein.ttf");
        File.WriteAllBytes(file, FakeFont("Nur Latein", 0x00010000, 'A', 'B'));
        Assert.Null(PdfFonts.Load(file, "Nur Latein", out var reason));
        Assert.Contains("Chinese", reason);

        var cjk = Path.Combine(_temp.Path, "cjk.ttc");
        File.WriteAllBytes(cjk, Collection(("Andere", 0x00010000, ['A']), ("Test Hei", 0x00010000, ['中', '文', '报', '告'])));
        var extracted = PdfFonts.Load(cjk, "Test Hei", out reason);
        Assert.NotNull(extracted);
        Assert.Equal("Test Hei", Assert.Single(Sfnt.Faces(extracted)).Family);
    }

    [Fact]
    public void Kaputte_Schriftdateien_geben_nur_InvalidDataException()
    {
        var valid = Collection(("Test Hei", 0x00010000, ['中']));
        var random = new Random(42);
        for (int i = 0; i < 500; i++)
        {
            var bytes = i % 2 == 0 ? valid[..random.Next(0, valid.Length)] : (byte[])valid.Clone();
            if (i % 2 == 1) for (int k = 0; k < 8; k++) bytes[random.Next(bytes.Length)] = (byte)random.Next(256);
            try
            {
                foreach (var f in Sfnt.Faces(bytes))
                {
                    Sfnt.HasGlyphs(bytes, f.Index, [0x4E2D]);
                    Sfnt.Extract(bytes, f.Index);
                }
            }
            catch (InvalidDataException)
            {
                // erwartet
            }
        }
        // Unplausible Zahlen werden abgelehnt, nicht ausgeführt
        var hostile = (byte[])valid.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(hostile.AsSpan(8), 100_000);
        Assert.Throws<InvalidDataException>(() => Sfnt.Faces(hostile));
        Assert.Throws<InvalidDataException>(() => Sfnt.Faces([1, 2, 3]));
    }

    [Fact]
    public void Riegel_liefert_keine_Schriftdateien_aus()
    {
        string[] fontFiles = ["*.ttf", "*.ttc", "*.otf", "*.woff", "*.woff2"];
        var found = fontFiles.SelectMany(p => Directory.EnumerateFiles(RepoPaths.Src, p, SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.Empty(found);
    }
}
