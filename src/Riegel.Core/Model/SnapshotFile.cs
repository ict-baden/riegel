// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Text;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Model;

/// <summary>
/// Dateien der Exporte (O1): Name <c>&lt;HOST&gt;_&lt;yyyyMMddTHHmmssZ&gt;.snapshot.json</c> (optional <c>.json.gz</c>)
/// und Begleitdatei <c>&lt;name&gt;.sha256</c> im sha256sum-Format (<c>&lt;hex&gt;␠␠&lt;name&gt;\n</c>).
/// </summary>
public static class SnapshotFile
{
    public const string Extension = ".snapshot.json";
    public const string GzipExtension = ".snapshot.json.gz";
    public const string SidecarExtension = ".sha256";

    /// <summary>Kanonischer Dateiname; Hostname bereinigt und groß geschrieben.</summary>
    public static string FileName(string hostName, DateTimeOffset collectedAtUtc, bool gzip = false) =>
        $"{FileNames.Host(hostName).ToUpperInvariant()}_{FileNames.Timestamp(collectedAtUtc)}{(gzip ? GzipExtension : Extension)}";

    /// <summary>Pfad der Begleitdatei.</summary>
    public static string SidecarPath(string snapshotPath) => snapshotPath + SidecarExtension;

    /// <summary>Eine Zeile im sha256sum-Format, mit LF.</summary>
    public static string SidecarLine(string sha256, string fileName) => $"{sha256.ToLowerInvariant()}  {fileName}\n";

    /// <summary>Liest eine sha256sum-Zeile („&lt;hex&gt;  &lt;name&gt;“ oder „&lt;hex&gt; *&lt;name&gt;“).</summary>
    public static bool TryParseSidecar(string text, out string sha256, out string fileName)
    {
        sha256 = "";
        fileName = "";
        var line = text.Split('\n')[0].TrimEnd('\r');
        if (line.Length < 66 || (line[64] != ' ')) return false;
        var hash = line[..64];
        if (!Hashes.IsSha256(hash)) return false;
        var name = line[65..];
        if (name.StartsWith(' ') || name.StartsWith('*')) name = name[1..];
        if (name.Length == 0) return false;
        sha256 = hash.ToLowerInvariant();
        fileName = name;
        return true;
    }

    /// <summary>
    /// Schreibt den Export und die Begleitdatei in <paramref name="directory"/> und gibt den Pfad zurück. Vorhandene
    /// Dateien werden nicht überschrieben (<see cref="IOException"/>).
    /// </summary>
    public static string Write(Snapshot snapshot, string directory, bool gzip = false)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName(snapshot.Machine.HostName, snapshot.CollectedAtUtc, gzip));
        var json = RiegelJson.SerializeToUtf8Bytes(snapshot);
        byte[] content = json;
        if (gzip)
        {
            using var buffer = new MemoryStream();
            using (var zip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true)) zip.Write(json);
            content = buffer.ToArray();
        }
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.Write(content);
        var hash = Hashes.Sha256Hex(content);
        File.WriteAllText(SidecarPath(path), SidecarLine(hash, Path.GetFileName(path)), new UTF8Encoding(false));
        return path;
    }

    /// <summary>Prüft die Begleitdatei gegen die Datei.</summary>
    public static HashCheck Verify(string snapshotPath)
    {
        var actual = Hashes.Sha256File(snapshotPath);
        var sidecar = SidecarPath(snapshotPath);
        if (!File.Exists(sidecar)) return new HashCheck(HashCheckStatus.Missing, null, actual, null);
        var text = File.ReadAllText(sidecar, Encoding.UTF8);
        if (!TryParseSidecar(text, out var expected, out var name) ||
            !string.Equals(name, Path.GetFileName(snapshotPath), StringComparison.OrdinalIgnoreCase))
            return new HashCheck(HashCheckStatus.Malformed, null, actual, sidecar);
        return new HashCheck(expected == actual ? HashCheckStatus.Ok : HashCheckStatus.Mismatch, expected, actual, sidecar);
    }
}
