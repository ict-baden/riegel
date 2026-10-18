// SPDX-License-Identifier: MIT
using System.Text;
using Riegel.Core.Model;

namespace Riegel.Core.Parsing;

/// <summary>
/// Lesen von Text- und Binärdateien aus feindlichen Quellen (spec 7.12): Größe zuerst prüfen, dann streng dekodieren.
/// Ungültige Bytefolgen (z. B. ein einzelnes Ersatzzeichen in UTF-16) sind ein Fehler, nie ein stilles „�“.
/// </summary>
internal static class TextFiles
{
    /// <summary>UTF-16 LE ohne BOM-Ausgabe, wirft bei ungültigen Folgen.</summary>
    internal static readonly UnicodeEncoding StrictUtf16 = new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);

    /// <summary>UTF-8 ohne BOM-Ausgabe, wirft bei ungültigen Folgen.</summary>
    internal static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Kodierung einer Textdatei laut BOM (spec 7.5: secedit/.reg in UTF-16 LE mit BOM, CSV/Text in ANSI).</summary>
    internal enum Bom { None, Utf8, Utf16Le, Utf16Be }

    /// <summary>Liest höchstens <paramref name="maxBytes"/> Bytes; größer → RGL-0261 bzw. der angegebene Code.</summary>
    internal static byte[] ReadCapped(string path, long maxBytes, string tooLargeCode = RglCodesParsing.BundleFileTooLarge)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw RiegelException.Common(RglCodesCommon.NotFound, path);
        if (info.Length > maxBytes) throw RglCodesParsing.Error(tooLargeCode, Path.GetFileName(path), maxBytes);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                // Die Datei kann während des Lesens wachsen
                if (buffer.Length + read > maxBytes) throw RglCodesParsing.Error(tooLargeCode, Path.GetFileName(path), maxBytes);
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            throw RiegelException.Common(RglCodesCommon.AccessDenied, path);
        }
        catch (IOException e)
        {
            throw new RiegelException(RglCodesCommon.IoError, RglCodesCommon.Message(RglCodesCommon.IoError, path), e);
        }
    }

    internal static Bom DetectBom(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Bom.Utf8;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Bom.Utf16Le;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Bom.Utf16Be;
        return Bom.None;
    }

    /// <summary>UTF-16 LE mit BOM (secedit /export, reg export). Ohne BOM oder ungültig → RGL-0262.</summary>
    internal static string DecodeUtf16WithBom(byte[] bytes, string fileName)
    {
        if (DetectBom(bytes) != Bom.Utf16Le) throw RglCodesParsing.Error(RglCodesParsing.InvalidEncoding, fileName, "UTF-16 LE (BOM FF FE)");
        return DecodeUtf16(bytes.AsSpan(2), fileName);
    }

    /// <summary>UTF-16 LE ohne BOM; ungerade Länge oder einzelne Ersatzzeichen → RGL-0262.</summary>
    internal static string DecodeUtf16(ReadOnlySpan<byte> bytes, string fileName)
    {
        if (bytes.Length % 2 != 0) throw RglCodesParsing.Error(RglCodesParsing.InvalidEncoding, fileName, "UTF-16 LE");
        try
        {
            return StrictUtf16.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw RglCodesParsing.Error(RglCodesParsing.InvalidEncoding, fileName, "UTF-16 LE");
        }
    }

    /// <summary>
    /// Text in UTF-16 LE mit BOM, UTF-8 (mit oder ohne BOM) oder – wenn kein gültiges UTF-8 – als Latin-1 (ANSI-Ausgaben
    /// von <c>reg query</c>, <c>auditpol /backup</c>; die Zeichen außerhalb ASCII sind dort nur Anzeigetexte).
    /// </summary>
    internal static string DecodeConsoleText(byte[] bytes, string fileName)
    {
        switch (DetectBom(bytes))
        {
            case Bom.Utf16Le:
                return DecodeUtf16(bytes.AsSpan(2), fileName);
            case Bom.Utf16Be:
                throw RglCodesParsing.Error(RglCodesParsing.InvalidEncoding, fileName, "UTF-16 BE");
            case Bom.Utf8:
                return DecodeUtf8(bytes.AsSpan(3), fileName);
        }
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>Streng UTF-8 (ein BOM wurde vorher entfernt).</summary>
    internal static string DecodeUtf8(ReadOnlySpan<byte> bytes, string fileName)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw RglCodesParsing.Error(RglCodesParsing.InvalidEncoding, fileName, "UTF-8");
        }
    }

    /// <summary>Text in Zeilen (CRLF, LF oder CR), ohne die Zeilenenden.</summary>
    internal static List<string> Lines(string text)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\r' && c != '\n') continue;
            lines.Add(text[start..i]);
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }

    /// <summary>Enthält der Text ein Steuerzeichen außer TAB, CR und LF (z. B. NUL)?</summary>
    internal static bool HasControlCharacters(string text)
    {
        foreach (char c in text)
            if (char.IsControl(c) && c is not '\t' and not '\r' and not '\n') return true;
        return false;
    }
}
