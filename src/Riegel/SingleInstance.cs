// SPDX-License-Identifier: MIT
using Riegel.Interop;

namespace Riegel;

/// <summary>
/// Nur eine Oberfläche je Sitzung (spec 6: Mutex <c>Local\ICT-Baden.Riegel.SingleInstance</c>; Muster aus Lupe). Ein
/// zweiter Start meldet sich beim ersten, der sein Fenster nach vorn holt, und beendet sich. Befehlszeile,
/// <c>--screenshots</c> und <c>--demo-report</c> nehmen den Mutex nicht.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\ICT-Baden.Riegel.SingleInstance";
    public const string ShowEventName = @"Local\ICT-Baden.Riegel.Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle show)
    {
        _mutex = mutex;
        _show = show;
    }

    /// <summary>Erste Instanz: Besitz übernehmen. Läuft schon eine, null.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
        if (!created)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName));
    }

    /// <summary>Zweite Instanz: die erste bitten, ihr Fenster zu zeigen.</summary>
    public static void SignalFirst()
    {
        Native.AllowSetForegroundWindow(Native.ASFW_ANY);
        try
        {
            using var signal = EventWaitHandle.OpenExisting(ShowEventName);
            signal.Set();
        }
        catch (Exception e) when (e is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
        {
            // Die erste Instanz startet gerade oder beendet sich
        }
    }

    /// <summary>Bei jedem weiteren Start (Hintergrund-Thread) <paramref name="onShow"/> aufrufen.</summary>
    public void Listen(Action onShow)
    {
        _wait?.Unregister(null);
        _wait = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => onShow(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _show.Dispose();
        try { _mutex.ReleaseMutex(); }
        catch (ApplicationException) { }
        _mutex.Dispose();
    }
}
