// SPDX-License-Identifier: MIT
using System.Security.Cryptography;
using System.Text;

namespace Riegel.Core.Model;

/// <summary>SHA-256 als klein geschriebener Hex-Text (wie sha256sum).</summary>
public static class Hashes
{
    public static string Sha256Hex(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    public static string Sha256File(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Die ersten 8 Zeichen, z. B. im Satz zur Freigabe und in Verlaufsdateien.</summary>
    public static string Short(string sha256) => sha256.Length >= 8 ? sha256[..8] : sha256;

    /// <summary>Ist das ein SHA-256 in Hex (64 Zeichen, Groß/klein egal)?</summary>
    public static bool IsSha256(string? text) =>
        text is { Length: 64 } && text.All(Uri.IsHexDigit);
}
