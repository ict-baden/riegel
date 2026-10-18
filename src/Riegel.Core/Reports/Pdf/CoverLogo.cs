// SPDX-License-Identifier: MIT
using System.Buffers.Binary;
using PdfSharp.Drawing;
using Riegel.Core.Model;

namespace Riegel.Core.Reports.Pdf;

/// <summary>
/// Logo für „Durchgeführt von“ auf dem Deckblatt (spec 5 O4, 7.12): eine vom Kunden oder Partner gelieferte PNG- oder
/// JPEG-Datei im Anlagenordner. Sie ist fremde Eingabe und wird nie vertraut: höchstens 1 MB, Dateikopf und Pixelmaße
/// werden vor dem Dekodieren selbst geprüft (keine Dekomprimierungsbombe), dann muss PDFsharp sie lesen können. Die
/// Datei wird genau einmal gelesen und als Bytes eingebettet – zwischen Prüfung und Satz kann sie niemand tauschen.
/// Jede Ablehnung ist ein RGL-Code; das Dokument wird dann nicht geschrieben (ein signierbares Dokument ändert sich
/// nicht stillschweigend).
/// </summary>
internal static class CoverLogo
{
    /// <summary>Höchstgröße der Datei (spec 7.12).</summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>Höchstmaß in Pixeln je Seite – ein Logo braucht nicht mehr, ein Vielfaches deutet auf eine Bombe hin.</summary>
    public const int MaxPixels = 4096;

    /// <summary>Geprüftes Logo: MigraDoc-Bildquelle („base64:…“) und Seitenverhältnis.</summary>
    internal sealed record Checked(string Source, int Width, int Height, string Format);

    /// <summary>Liest und prüft das Logo; null, wenn kein Pfad angegeben ist.</summary>
    /// <exception cref="RiegelException">RGL-0502 (zu groß), RGL-0503 (kein gültiges PNG/JPEG), RGL-0504 (nicht lesbar).</exception>
    public static Checked? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var name = Path.GetFileName(path);
        byte[] bytes;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxBytes)
                throw new RiegelException(RglCodesPdf.LogoTooLarge, RglCodesPdf.Message(RglCodesPdf.LogoTooLarge, name, stream.Length, MaxBytes));
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            // Wächst die Datei während des Lesens, zählt nur, was geprüft wurde
            if (stream.ReadByte() >= 0)
                throw new RiegelException(RglCodesPdf.LogoTooLarge, RglCodesPdf.Message(RglCodesPdf.LogoTooLarge, name, stream.Length, MaxBytes));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new RiegelException(RglCodesPdf.LogoUnreadable, RglCodesPdf.Message(RglCodesPdf.LogoUnreadable, name, e.GetType().Name), e);
        }
        return Check(bytes, name);
    }

    /// <summary>Prüft Logo-Bytes (auch für Tests ohne Datei).</summary>
    internal static Checked Check(byte[] bytes, string name)
    {
        if (bytes.Length > MaxBytes)
            throw new RiegelException(RglCodesPdf.LogoTooLarge, RglCodesPdf.Message(RglCodesPdf.LogoTooLarge, name, bytes.Length, MaxBytes));
        var (format, width, height) = Header(bytes);
        if (format is null || width <= 0 || height <= 0 || width > MaxPixels || height > MaxPixels)
            throw Invalid(name);
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var image = XImage.FromStream(stream);
            if (image.PixelWidth <= 0 || image.PixelHeight <= 0 || image.PixelWidth > MaxPixels || image.PixelHeight > MaxPixels)
                throw Invalid(name);
        }
        catch (RiegelException)
        {
            throw;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Jeder Fehler des Bildlesers bei fremder Eingabe ist „kein gültiges Bild“, nie ein Absturz
            throw new RiegelException(RglCodesPdf.LogoInvalid, RglCodesPdf.Message(RglCodesPdf.LogoInvalid, name), e);
        }
        return new Checked("base64:" + Convert.ToBase64String(bytes), width, height, format);
    }

    private static RiegelException Invalid(string name) =>
        new(RglCodesPdf.LogoInvalid, RglCodesPdf.Message(RglCodesPdf.LogoInvalid, name));

    /// <summary>Format und Pixelmaße aus dem Dateikopf (PNG: IHDR; JPEG: erster SOF-Marker), ohne zu dekodieren.</summary>
    internal static (string? Format, int Width, int Height) Header(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (data.Length >= 33 && data[..8].SequenceEqual(png) && data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            uint w = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
            uint h = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
            return ("png", w > int.MaxValue ? -1 : (int)w, h > int.MaxValue ? -1 : (int)h);
        }
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            int i = 2;
            while (i + 4 <= data.Length)
            {
                if (data[i] != 0xFF) return (null, 0, 0);
                byte marker = data[i + 1];
                if (marker == 0xFF) { i++; continue; } // Füllbytes
                if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7) { i += 2; continue; }
                if (marker is 0xD9 or 0xDA) return (null, 0, 0); // Bilddaten ohne vorherigen SOF
                int length = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 2)..]);
                if (length < 2 || i + 2 + length > data.Length) return (null, 0, 0);
                // SOF0–SOF15 außer DHT (C4), JPG (C8) und DAC (CC)
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                {
                    if (length < 7) return (null, 0, 0);
                    int h = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 5)..]);
                    int w = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 7)..]);
                    return ("jpeg", w, h);
                }
                i += 2 + length;
            }
        }
        return (null, 0, 0);
    }
}
