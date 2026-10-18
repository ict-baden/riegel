// SPDX-License-Identifier: MIT
using System.Buffers.Binary;
using System.Text;

namespace Riegel.Core.Reports.Pdf;

/// <summary>Kontur-Art einer Schrift: nur TrueType-Konturen (glyf/loca) bettet PDFsharp 6.2.4 korrekt ein.</summary>
internal enum SfntOutlines { TrueType, Cff, Unknown }

/// <summary>Ein Schnitt in einer Schriftdatei (einzeln oder in einer Sammlung).</summary>
/// <param name="Family">Familienname (Name-ID 16, sonst 1), null = nicht lesbar.</param>
/// <param name="Subfamily">Schnitt (Name-ID 17, sonst 2), z. B. „Regular“, „Bold“.</param>
internal sealed record SfntFace(int Index, string? Family, string? Subfamily, SfntOutlines Outlines);

/// <summary>
/// Liest OpenType-/TrueType-Dateien und -Sammlungen (.ttc) so weit, wie die Schriftwahl der PDF-Dokumente es braucht,
/// und löst einen Schnitt aus einer Sammlung als eigenständige Schrift heraus. Hintergrund (Spike des Pakets pdf,
/// geprüft am Quelltext von PdfSharp.dll 6.2.4): PDFsharp liest keine Sammlungen („TrueType collection fonts are not
/// yet supported by PDFsharp.“) und bettet CFF-Schriften („OTTO“) ohne Teilmenge als /FontFile2 ein – das ist kein
/// gültiges PDF (CFF gehört in /FontFile3) und bläht jedes Dokument um die ganze Schrift auf. Deshalb nimmt Riegel nur
/// Schriften mit TrueType-Konturen und schneidet Sammlungen im Speicher auf.
/// Schriftdateien kommen aus den Schriftordnern des Systems; gelesen wird trotzdem mit Grenzprüfung (nie Absturz,
/// nur <see cref="InvalidDataException"/>).
/// </summary>
internal static class Sfnt
{
    /// <summary>Größere Schriftdateien werden nicht gelesen (die größten CJK-Sammlungen haben etwa 20–30 MB).</summary>
    public const int MaxFileBytes = 64 * 1024 * 1024;

    private const uint Ttcf = 0x74746366;      // "ttcf"
    private const uint Otto = 0x4F54544F;      // "OTTO" = CFF-Konturen
    private const uint TrueTypeV1 = 0x00010000;
    private const uint TrueTag = 0x74727565;   // "true" (alte Apple-Kennung)
    private const int MaxFaces = 64;
    private const int MaxTables = 512;

    /// <summary>Ist die Datei eine Sammlung (.ttc)?</summary>
    public static bool IsCollection(ReadOnlySpan<byte> data) => data.Length >= 12 && U32(data, 0) == Ttcf;

    /// <summary>Alle Schnitte der Datei mit Name und Kontur-Art.</summary>
    /// <exception cref="InvalidDataException">Keine lesbare Schriftdatei.</exception>
    public static IReadOnlyList<SfntFace> Faces(ReadOnlySpan<byte> data)
    {
        var faces = new List<SfntFace>();
        var offsets = FaceOffsets(data);
        for (int i = 0; i < offsets.Count; i++)
        {
            var tables = Tables(data, offsets[i]);
            uint version = U32(data, offsets[i]);
            var outlines = version == Otto || tables.ContainsKey("CFF ") || tables.ContainsKey("CFF2") ? SfntOutlines.Cff
                : version is TrueTypeV1 or TrueTag && tables.ContainsKey("glyf") && tables.ContainsKey("loca") ? SfntOutlines.TrueType
                : SfntOutlines.Unknown;
            string? family = null, subfamily = null;
            if (tables.TryGetValue("name", out var name))
            {
                family = Name(data, name, 16) ?? Name(data, name, 1);
                subfamily = Name(data, name, 17) ?? Name(data, name, 2);
            }
            faces.Add(new SfntFace(i, family, subfamily, outlines));
        }
        return faces;
    }

    /// <summary>
    /// Schnitt <paramref name="index"/> als eigenständige sfnt-Datei: Tabellen nach Kennung sortiert und lückenlos
    /// hintereinander, Prüfsummen und <c>head.checkSumAdjustment</c> neu berechnet, ohne digitale Signatur (DSIG – sie
    /// gälte für die Sammlung, nicht für den herausgelösten Schnitt). Eine Einzeldatei kommt genauso normalisiert zurück.
    /// </summary>
    /// <exception cref="InvalidDataException">Keine lesbare Schriftdatei oder Index außerhalb.</exception>
    public static byte[] Extract(ReadOnlySpan<byte> data, int index)
    {
        var offsets = FaceOffsets(data);
        if (index < 0 || index >= offsets.Count) throw new InvalidDataException($"Schnitt {index} gibt es nicht ({offsets.Count} Schnitte).");
        int start = offsets[index];
        uint version = U32(data, start);
        var tables = Tables(data, start).Where(t => t.Key != "DSIG").OrderBy(t => t.Key, StringComparer.Ordinal).ToList();
        int n = tables.Count;
        int entrySelector = 0;
        while ((1 << (entrySelector + 1)) <= n) entrySelector++;
        int searchRange = (1 << entrySelector) * 16;
        long size = 12 + 16L * n + tables.Sum(t => (long)Pad(t.Value.Length));
        if (size > MaxFileBytes) throw new InvalidDataException("Schrift zu groß.");

        var output = new byte[size];
        var span = output.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, version);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)n);
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(span[8..], (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(span[10..], (ushort)(n * 16 - searchRange));
        int position = 12 + 16 * n;
        int head = -1;
        for (int i = 0; i < n; i++)
        {
            var (tag, (offset, length)) = (tables[i].Key, tables[i].Value);
            data.Slice(offset, length).CopyTo(span[position..]);
            // head.checkSumAdjustment zählt für die Prüfsummen als 0 und wird am Ende gesetzt
            if (tag == "head" && length >= 12)
            {
                head = position;
                BinaryPrimitives.WriteUInt32BigEndian(span[(position + 8)..], 0);
            }
            var record = span[(12 + 16 * i)..];
            Encoding.ASCII.GetBytes(tag, record);
            BinaryPrimitives.WriteUInt32BigEndian(record[4..], Checksum(span.Slice(position, Pad(length))));
            BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)position);
            BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)length);
            position += Pad(length);
        }
        if (head >= 0)
            BinaryPrimitives.WriteUInt32BigEndian(span[(head + 8)..], unchecked(0xB1B0AFBA - Checksum(span)));
        return output;
    }

    /// <summary>
    /// Hat Schnitt <paramref name="index"/> für jedes Zeichen in <paramref name="codePoints"/> eine Glyphe (cmap Format 4
    /// oder 12)? So wird keine Schrift gewählt, der die vereinfachten chinesischen Zeichen fehlen (z. B. eine rein
    /// japanische).
    /// </summary>
    public static bool HasGlyphs(ReadOnlySpan<byte> data, int index, ReadOnlySpan<int> codePoints)
    {
        var offsets = FaceOffsets(data);
        if (index < 0 || index >= offsets.Count) return false;
        if (!Tables(data, offsets[index]).TryGetValue("cmap", out var cmap)) return false;
        var t = Slice(data, cmap.Offset, cmap.Length);
        int count = U16(t, 2);
        int best = -1, bestRank = int.MaxValue;
        for (int i = 0; i < count && 4 + 8 * (i + 1) <= t.Length; i++)
        {
            int platform = U16(t, 4 + 8 * i), encoding = U16(t, 6 + 8 * i);
            uint offset = U32(t, 8 + 8 * i);
            if (offset > (uint)(t.Length - 2)) continue;
            int format = U16(t, (int)offset);
            int rank = (platform, encoding, format) switch
            {
                (3, 10, 12) => 0,
                (0, _, 12) => 1,
                (3, 1, 4) => 2,
                (0, _, 4) => 3,
                _ => int.MaxValue,
            };
            if (rank < bestRank) (best, bestRank) = ((int)offset, rank);
        }
        if (best < 0) return false;
        var sub = t[best..];
        foreach (var c in codePoints)
            if (Glyph(sub, c) == 0) return false;
        return true;
    }

    private static int Glyph(ReadOnlySpan<byte> sub, int c)
    {
        if (U16(sub, 0) == 12)
        {
            uint groups = U32(sub, 12);
            for (long g = 0; g < groups && 16 + 12 * (g + 1) <= sub.Length; g++)
            {
                int r = (int)(16 + 12 * g);
                uint start = U32(sub, r), end = U32(sub, r + 4);
                if (c >= start && c <= end) return (int)(U32(sub, r + 8) + (uint)c - start);
            }
            return 0;
        }
        if (c > 0xFFFF) return 0;
        int segments = U16(sub, 6) / 2;
        int ends = 14, starts = 16 + 2 * segments, deltas = starts + 2 * segments, ranges = deltas + 2 * segments;
        for (int s = 0; s < segments; s++)
        {
            if (U16(sub, ends + 2 * s) < c) continue;
            int start = U16(sub, starts + 2 * s);
            if (start > c) return 0;
            int delta = (short)U16(sub, deltas + 2 * s);
            int range = U16(sub, ranges + 2 * s);
            if (range == 0) return (c + delta) & 0xFFFF;
            int address = ranges + 2 * s + range + 2 * (c - start);
            int glyph = U16(sub, address);
            return glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
        }
        return 0;
    }

    private static int Pad(int length) => (length + 3) & ~3;

    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        int i = 0;
        for (; i + 4 <= data.Length; i += 4) sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data[i..]));
        if (i < data.Length)
        {
            Span<byte> last = stackalloc byte[4];
            last.Clear();
            data[i..].CopyTo(last);
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(last));
        }
        return sum;
    }

    private static List<int> FaceOffsets(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12) throw new InvalidDataException("Zu kurz für eine Schriftdatei.");
        if (U32(data, 0) != Ttcf) return [0];
        uint count = U32(data, 8);
        if (count == 0 || count > MaxFaces) throw new InvalidDataException($"Unplausible Zahl von Schnitten: {count}.");
        var offsets = new List<int>((int)count);
        for (int i = 0; i < count; i++)
        {
            uint offset = U32(data, 12 + 4 * i);
            if (offset > (uint)(data.Length - 12)) throw new InvalidDataException("Schnitt außerhalb der Datei.");
            offsets.Add((int)offset);
        }
        return offsets;
    }

    private static Dictionary<string, (int Offset, int Length)> Tables(ReadOnlySpan<byte> data, int start)
    {
        int count = U16(data, start + 4);
        if (count > MaxTables) throw new InvalidDataException($"Unplausible Zahl von Tabellen: {count}.");
        var tables = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int record = start + 12 + 16 * i;
            var tag = Encoding.ASCII.GetString(Slice(data, record, 4));
            uint offset = U32(data, record + 8);
            uint length = U32(data, record + 12);
            if (offset > (uint)data.Length || length > (uint)data.Length - offset) throw new InvalidDataException($"Tabelle {tag} außerhalb der Datei.");
            tables[tag] = ((int)offset, (int)length);
        }
        return tables;
    }

    /// <summary>Eintrag der name-Tabelle; bevorzugt Windows/Englisch (USA), dann Windows, Unicode, Mac-Roman.</summary>
    private static string? Name(ReadOnlySpan<byte> data, (int Offset, int Length) table, int nameId)
    {
        var t = Slice(data, table.Offset, table.Length);
        if (t.Length < 6) return null;
        int count = U16(t, 2);
        int strings = U16(t, 4);
        string? best = null;
        int bestRank = int.MaxValue;
        for (int i = 0; i < count && 6 + 12 * (i + 1) <= t.Length; i++)
        {
            int r = 6 + 12 * i;
            int platform = U16(t, r), encoding = U16(t, r + 2), language = U16(t, r + 4), id = U16(t, r + 6);
            int length = U16(t, r + 8), offset = U16(t, r + 10);
            if (id != nameId || strings + offset + length > t.Length) continue;
            int rank = platform switch
            {
                3 when language == 0x0409 => 0,
                3 => 1,
                0 => 2,
                1 when encoding == 0 => 3,
                _ => int.MaxValue,
            };
            if (rank >= bestRank) continue;
            var bytes = t.Slice(strings + offset, length);
            best = platform == 1 ? Encoding.Latin1.GetString(bytes) : Encoding.BigEndianUnicode.GetString(bytes);
            bestRank = rank;
        }
        return string.IsNullOrWhiteSpace(best) ? null : best.Trim();
    }

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length) throw new InvalidDataException("Lesen außerhalb der Schriftdatei.");
        return data.Slice(offset, length);
    }

    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(Slice(data, offset, 4));

    private static int U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(Slice(data, offset, 2));
}
