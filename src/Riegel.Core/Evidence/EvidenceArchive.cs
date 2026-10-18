// SPDX-License-Identifier: MIT
using System.IO.Compression;
using Riegel.Core.Model;

namespace Riegel.Core.Evidence;

/// <summary>
/// Ein Nachweispaket, vollständig im Speicher gelesen (spec 7.12): nichts wird in Pfade entpackt. Grenzen für die
/// Datei, je Eintrag und für die Summe der entpackten Einträge (ZIP-Bombe); Eintragsnamen mit „..“, absolutem Pfad,
/// Laufwerk, Backslash, Steuerzeichen oder doppelt (ohne Groß/klein) werden abgelehnt. Jeder Fehler ist ein RGL-Code.
/// </summary>
internal sealed class EvidenceArchive
{
    /// <summary>Höchstzahl der Einträge eines Pakets.</summary>
    public const int MaxEntries = 20_000;

    /// <summary>Höchstlänge eines Eintragsnamens.</summary>
    public const int MaxNameLength = 400;

    private readonly SortedDictionary<string, byte[]> _entries;

    private EvidenceArchive(SortedDictionary<string, byte[]> entries) => _entries = entries;

    /// <summary>Alle Dateien (ohne Ordnereinträge) nach Name, ordinal sortiert.</summary>
    public IReadOnlyDictionary<string, byte[]> Entries => _entries;

    public bool Contains(string name) => _entries.ContainsKey(name);

    public byte[]? Get(string name) => _entries.TryGetValue(name, out var bytes) ? bytes : null;

    /// <summary>Liest das Paket mit den Grenzen aus <paramref name="limits"/>.</summary>
    /// <exception cref="RiegelException">RGL-0550 … RGL-0566 bei unlesbaren oder feindlichen Paketen.</exception>
    public static EvidenceArchive Read(string zipPath, ReadLimits limits)
    {
        FileInfo info;
        try { info = new FileInfo(zipPath); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw RglCodesDataOutputs.Error(RglCodesDataOutputs.ZipUnreadable, zipPath);
        }
        if (!info.Exists) throw RiegelException.Common(RglCodesCommon.NotFound, zipPath);
        if (info.Length > limits.MaxEvidenceZipBytes)
            throw RglCodesDataOutputs.Error(RglCodesDataOutputs.ZipTooLarge, info.Length, limits.MaxEvidenceZipBytes);
        try
        {
            using var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Read(stream, limits);
        }
        catch (IOException e) when (e is not FileNotFoundException)
        {
            throw new RiegelException(RglCodesDataOutputs.ZipUnreadable, RglCodesDataOutputs.Message(RglCodesDataOutputs.ZipUnreadable, zipPath), e);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new RiegelException(RglCodesCommon.AccessDenied, RglCodesCommon.Message(RglCodesCommon.AccessDenied, zipPath), e);
        }
    }

    /// <summary>Liest ein Paket aus einem Datenstrom (Tests, Stichprobe).</summary>
    public static EvidenceArchive Read(Stream stream, ReadLimits limits)
    {
        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > MaxEntries)
                throw RglCodesDataOutputs.Error(RglCodesDataOutputs.TooManyEntries, zip.Entries.Count, MaxEntries);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                if (!IsSafeName(name, out bool directory))
                    throw RglCodesDataOutputs.Error(RglCodesDataOutputs.UnsafeEntryName, Printable(name));
                if (!names.Add(directory ? name.TrimEnd('/') : name))
                    throw RglCodesDataOutputs.Error(RglCodesDataOutputs.UnsafeEntryName, Printable(name));
                if (directory)
                {
                    if (entry.Length != 0) throw RglCodesDataOutputs.Error(RglCodesDataOutputs.UnsafeEntryName, Printable(name));
                    continue;
                }
                // Angabe im Verzeichnis zuerst (schneller Abbruch), beim Lesen trotzdem hart begrenzen: die Angabe kann lügen
                if (entry.Length > limits.MaxEvidenceEntryBytes || entry.Length < 0)
                    throw RglCodesDataOutputs.Error(RglCodesDataOutputs.EntryTooLarge, Printable(name), limits.MaxEvidenceEntryBytes);
                if (total + entry.Length > limits.MaxEvidenceZipBytes)
                    throw RglCodesDataOutputs.Error(RglCodesDataOutputs.EntryTooLarge, Printable(name), limits.MaxEvidenceZipBytes);
                var bytes = ReadCapped(entry, Math.Min(limits.MaxEvidenceEntryBytes, limits.MaxEvidenceZipBytes - total), name);
                total += bytes.Length;
                entries[name] = bytes;
            }
        }
        catch (InvalidDataException e)
        {
            throw new RiegelException(RglCodesDataOutputs.ZipUnreadable, RglCodesDataOutputs.Message(RglCodesDataOutputs.ZipUnreadable, e.Message), e);
        }
        catch (NotSupportedException e)
        {
            // z. B. unbekanntes Kompressionsverfahren oder verschlüsselter Eintrag
            throw new RiegelException(RglCodesDataOutputs.ZipUnreadable, RglCodesDataOutputs.Message(RglCodesDataOutputs.ZipUnreadable, e.Message), e);
        }
        return new EvidenceArchive(entries);
    }

    private static byte[] ReadCapped(ZipArchiveEntry entry, long cap, string name)
    {
        using var source = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > cap) throw RglCodesDataOutputs.Error(RglCodesDataOutputs.EntryTooLarge, Printable(name), cap);
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length != entry.Length) throw RglCodesDataOutputs.Error(RglCodesDataOutputs.ZipUnreadable, Printable(name));
        return buffer.ToArray();
    }

    /// <summary>
    /// Zulässiger Eintragsname: relativ, mit „/“ getrennt, ohne leere, „.“- oder „..“-Teile, ohne Backslash, Doppelpunkt
    /// (Laufwerk, alternative Datenströme) und Steuerzeichen. Ordner enden mit „/“.
    /// </summary>
    public static bool IsSafeName(string name, out bool directory)
    {
        directory = false;
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength) return false;
        if (name[0] == '/' || name.Contains('\\') || name.Contains(':')) return false;
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c < 0x20 || c == 0x7F || char.IsLowSurrogate(c)) return false;
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= name.Length || !char.IsLowSurrogate(name[i + 1])) return false;
                i++;
            }
        }
        directory = name.EndsWith('/');
        var body = directory ? name[..^1] : name;
        if (body.Length == 0) return false;
        foreach (var part in body.Split('/'))
        {
            if (part.Length == 0 || part is "." or "..") return false;
            if (part.EndsWith(' ') || part.EndsWith('.')) return false;
        }
        return true;
    }

    /// <summary>Name für Meldungen: Steuerzeichen sichtbar, gekürzt.</summary>
    internal static string Printable(string name)
    {
        var chars = name.Take(120).Select(c => c < 0x20 || c == 0x7F ? '?' : c).ToArray();
        return new string(chars) + (name.Length > 120 ? "…" : "");
    }
}
