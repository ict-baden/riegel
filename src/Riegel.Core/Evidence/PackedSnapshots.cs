// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Services;

namespace Riegel.Core.Evidence;

/// <summary>
/// Liest einen gepackten Export mit dem Export-Leser der Dienste (derselbe Weg wie beim Import: Schwärzen,
/// Neuberechnung von <c>parsed:*</c>, Grenzen). <see cref="ISnapshotReader"/> liest nur Pfade; deshalb werden die
/// bereits geprüften Bytes in eine eigene, frisch angelegte Temp-Datei mit festem Namen geschrieben und danach gelöscht.
/// Der Eintragsname aus dem Paket wird nie als Pfad benutzt (kein Entpacken, spec 7.12).
/// </summary>
internal static class PackedSnapshots
{
    public static Snapshot Read(ISnapshotReader reader, byte[] bytes, bool gzip, ReadLimits limits)
    {
        var folder = Path.Combine(Path.GetTempPath(), "riegel-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, gzip ? "packed" + SnapshotFile.GzipExtension : "packed" + SnapshotFile.Extension);
            using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(bytes);
            return reader.Read(file, limits);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

/// <summary>Uhr mit fester Zeit: verify wertet mit der Auswertungszeit des gepackten Laufs aus (ClockSuspect, Lauf-ID).</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private readonly DateTimeOffset _now = now.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
