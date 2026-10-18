// SPDX-License-Identifier: MIT
using System.Text;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Plants;

/// <summary>
/// Aufbau des Anlagenordners (spec 7.6) und die Schreibhelfer: atomar (temporäre Datei daneben, dann Ersetzen),
/// Verlaufskopien nur anhängend, Exporte unter <c>snapshots/&lt;host&gt;/</c> mit Begleitdatei.
/// </summary>
internal static class PlantFiles
{
    public const string PlantFile = "riegel-plant.json";
    public const string ExceptionsFile = "exceptions.json";
    public const string TailoringFile = "tailoring.json";
    public const string MeasuresFile = "measures.json";
    public const string Snapshots = "snapshots";
    public const string Runs = "runs";
    public const string Imports = "imports";
    public const string Approved = "approved";
    public const string History = "history";
    public const string Baselines = "baselines";
    public const string ImportExtension = ".import.json";

    /// <summary>Versuche für das Ersetzen (je 50 ms Abstand).</summary>
    private const int MoveAttempts = 20;
    public const string RunExtension = ".results.json";

    /// <summary>Verlaufsordner je Art (exceptions, tailoring, measures, plant).</summary>
    public static class HistoryKind
    {
        public const string Exceptions = "exceptions";
        public const string Tailoring = "tailoring";
        public const string Measures = "measures";
        public const string Plant = "plant";
    }

    /// <summary>Vollständiger Pfad im Anlagenordner aus einem relativen Pfad mit „/“.</summary>
    public static string Full(string folder, string relative) =>
        Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Pfad relativ zum Anlagenordner mit „/“; null, wenn <paramref name="path"/> außerhalb liegt.</summary>
    public static string? Relative(string folder, string path)
    {
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.StartsWith(root, comparison) ? full[root.Length..].Replace(Path.DirectorySeparatorChar, '/') : null;
    }

    /// <summary>
    /// Ordner eines Rechners unter <paramref name="root"/> (runs/, snapshots/, history/approved/): Hostnamen sind unter
    /// Windows ohne Groß/klein – ein vorhandener Ordner in anderer Schreibweise wird auch unter Linux wiederverwendet.
    /// </summary>
    public static string HostDirectory(string root, string host)
    {
        var name = FileNames.Host(host);
        if (Directory.Exists(root))
            foreach (var dir in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
                if (string.Equals(Path.GetFileName(dir), name, StringComparison.OrdinalIgnoreCase)) return dir;
        return Path.Combine(root, name);
    }

    /// <summary>Datei <c>&lt;host&gt;&lt;extension&gt;</c> in <paramref name="directory"/>, vorhandene Schreibweise bevorzugt.</summary>
    public static string HostFile(string directory, string host, string extension)
    {
        var name = FileNames.Host(host) + extension;
        if (Directory.Exists(directory))
            foreach (var file in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
                if (string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase)) return file;
        return Path.Combine(directory, name);
    }

    /// <summary>Pfad der Ergebnisdatei eines Laufs (<c>runs/&lt;host&gt;/&lt;runId&gt;.results.json</c>).</summary>
    public static string RunPath(string folder, string host, string runId) =>
        Path.Combine(HostDirectory(Path.Combine(folder, Runs), host), runId + RunExtension);

    /// <summary>Bytes atomar schreiben (temporäre Datei im selben Ordner, dann Ersetzen).</summary>
    public static void WriteAtomic(string path, ReadOnlySpan<byte> bytes)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            // Unter Windows scheitert das Ersetzen kurz, solange ein anderer Vorgang die Datei gerade liest
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(temp, path, overwrite: true);
                    break;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    if (attempt >= MoveAttempts) throw new RiegelException(RglCodesCommon.IoError, RglCodesCommon.Message(RglCodesCommon.IoError, path), e);
                    Thread.Sleep(50);
                }
            }
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>
    /// Verlaufskopie <c>history/&lt;kind&gt;/&lt;yyyyMMddTHHmmssZ&gt;_&lt;sha8&gt;.json</c> (nur anhängend). Gibt es die Datei
    /// schon, hat sie denselben Inhalt (gleicher Zeitstempel und Hash) und bleibt unverändert.
    /// </summary>
    public static string WriteHistory(string folder, string kind, DateTimeOffset now, byte[] content)
    {
        var sha = Hashes.Sha256Hex(content);
        var path = Path.Combine(folder, History, kind, $"{FileNames.Timestamp(now)}_{Hashes.Short(sha)}.json");
        if (!File.Exists(path)) WriteAtomic(path, content);
        return path;
    }

    /// <summary>Verlaufskopie eines freigegebenen Stands: <c>history/approved/&lt;host&gt;/&lt;ts&gt;.json</c>.</summary>
    public static string WriteApprovedHistory(string folder, string host, DateTimeOffset now, byte[] content)
    {
        var directory = HostDirectory(Path.Combine(folder, History, Approved), host);
        var stamp = FileNames.Timestamp(now);
        var path = Path.Combine(directory, stamp + ".json");
        if (File.Exists(path))
        {
            if (Hashes.Sha256File(path) == Hashes.Sha256Hex(content)) return path;
            // zweite Freigabe in derselben Sekunde: Hash anhängen, nichts überschreiben
            path = Path.Combine(directory, $"{stamp}_{Hashes.Short(Hashes.Sha256Hex(content))}.json");
            if (File.Exists(path)) return path;
        }
        WriteAtomic(path, content);
        return path;
    }

    /// <summary>
    /// Export in <c>snapshots/&lt;host&gt;/</c> ablegen (Name nach <see cref="SnapshotFile.FileName"/>) und Begleitdatei
    /// mit dem SHA-256 der abgelegten Datei schreiben. Liegt dort schon dieselbe Datei, bleibt sie; eine andere Datei
    /// mit demselben Namen ist RGL-0405. Gibt den vollständigen Pfad zurück.
    /// </summary>
    public static string StoreSnapshot(string folder, Snapshot snapshot, byte[] content, bool gzip)
    {
        var target = SnapshotTarget(folder, snapshot, gzip);
        var sha = Hashes.Sha256Hex(content);
        if (File.Exists(target))
        {
            if (!string.Equals(Hashes.Sha256File(target), sha, StringComparison.Ordinal))
                throw RglCodesWorkflow.Error(RglCodesWorkflow.SnapshotConflict, Relative(folder, target));
        }
        else
        {
            WriteAtomic(target, content);
        }
        var sidecar = SnapshotFile.SidecarPath(target);
        var line = Encoding.UTF8.GetBytes(SnapshotFile.SidecarLine(sha, Path.GetFileName(target)));
        if (!File.Exists(sidecar) || !File.ReadAllBytes(sidecar).AsSpan().SequenceEqual(line)) WriteAtomic(sidecar, line);
        return target;
    }

    /// <summary>Ablageort eines Exports: <c>snapshots/&lt;host&gt;/&lt;HOST&gt;_&lt;yyyyMMddTHHmmssZ&gt;.snapshot.json</c> (oder <c>.json.gz</c>).</summary>
    public static string SnapshotTarget(string folder, Snapshot snapshot, bool gzip) =>
        Path.Combine(HostDirectory(Path.Combine(folder, Snapshots), snapshot.Machine.HostName),
            SnapshotFile.FileName(snapshot.Machine.HostName, snapshot.CollectedAtUtc, gzip));

    /// <summary>JSON eines Riegel-Typs (wie <see cref="RiegelJson.SerializeToUtf8Bytes{T}"/>) atomar schreiben; gibt den SHA-256 zurück.</summary>
    public static string WriteJson<T>(string path, T value, out byte[] bytes)
    {
        bytes = RiegelJson.SerializeToUtf8Bytes(value);
        WriteAtomic(path, bytes);
        return Hashes.Sha256Hex(bytes);
    }

    /// <summary>SHA-256 einer Datei, "" wenn es sie nicht gibt.</summary>
    public static string ShaOrEmpty(string path) => File.Exists(path) ? Hashes.Sha256File(path) : "";

    private static void TryDelete(string path)
    {
        if (!File.Exists(path)) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
