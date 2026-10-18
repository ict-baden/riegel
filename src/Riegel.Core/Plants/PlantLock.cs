// SPDX-License-Identifier: MIT
using System.Diagnostics;
using Riegel.Core.Model;

namespace Riegel.Core.Plants;

/// <summary>
/// Sperre des Anlagenordners (spec 7.6): <c>.riegel.lock</c> mit <see cref="FileShare.None"/>, höchstens 10 s warten,
/// sonst RGL-0404. Gehalten um Vergleich der Prüfsumme, Ersetzen und Verlaufskopie.
/// <para>
/// Unter Windows ist FileShare.None verbindlich (auch über SMB-Freigaben), die Datei wird beim Schließen gelöscht
/// (<see cref="FileOptions.DeleteOnClose"/>). Unter Linux setzt .NET dafür eine beratende Sperre (flock). Dort bleibt die
/// leere Datei liegen: würde sie beim Schließen gelöscht, könnte ein Wartender die Sperre auf der gelöschten Datei
/// bekommen, während ein Dritter eine neue anlegt – dann hielten zwei Vorgänge „die“ Sperre.
/// </para>
/// </summary>
internal sealed class PlantLock : IDisposable
{
    public const string FileName = ".riegel.lock";

    /// <summary>Wartezeit nach spec 7.6.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(40);

    private readonly FileStream _stream;

    private PlantLock(FileStream stream) => _stream = stream;

    /// <summary>Sperre nehmen oder nach <paramref name="timeout"/> mit RGL-0404 aufgeben.</summary>
    public static PlantLock Acquire(string folder, TimeSpan? timeout = null)
    {
        var path = Path.Combine(folder, FileName);
        var limit = timeout ?? DefaultTimeout;
        var watch = Stopwatch.StartNew();
        Exception? last = null;
        while (true)
        {
            try
            {
                var options = OperatingSystem.IsWindows() ? FileOptions.DeleteOnClose : FileOptions.None;
                return new PlantLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, options));
            }
            catch (IOException e)
            {
                // belegt (Windows: Freigabeverletzung, Linux: flock) – erneut versuchen
                last = e;
            }
            catch (UnauthorizedAccessException e)
            {
                // Windows: Datei wird gerade gelöscht (Löschen beim Schließen) – kurz darauf frei
                last = e;
            }
            if (watch.Elapsed >= limit)
            {
                if (last is UnauthorizedAccessException)
                    throw new RiegelException(RglCodesCommon.AccessDenied, RglCodesCommon.Message(RglCodesCommon.AccessDenied, path), last);
                throw new RiegelException(RglCodesWorkflow.LockTimeout, RglCodesWorkflow.Message(RglCodesWorkflow.LockTimeout), last!);
            }
            Thread.Sleep(Poll);
        }
    }

    public void Dispose() => _stream.Dispose();
}
