// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;

namespace Riegel.Platform.Windows;

/// <summary>
/// ShellExecuteEx für die zwei Fälle, die die App braucht (spec 7.2/7.6): Riegel selbst mit „runas“ für die
/// Erhebung mit Rechteerhöhung, und „open“ für von Riegel erzeugte Dateien und http/https-Links. Der Erfassungscode
/// (Collection/) startet Prozesse dagegen nur über ToolRunner.
/// </summary>
internal static class Shell
{
    private const int SEE_MASK_NOCLOSEPROCESS = 0x00000040;
    private const int SEE_MASK_FLAG_NO_UI = 0x00000400;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNORMAL = 1;
    public const int ERROR_CANCELLED = 1223;
    private const uint WAIT_OBJECT_0 = 0;
    private const uint WAIT_FAILED = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public int dwHotKey;
        public IntPtr hIconOrMonitor;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ShellExecuteExW")]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out int exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>Öffnet eine Datei, einen Ordner oder einen Link mit der zugeordneten Anwendung; 0 oder der Win32-Fehler.</summary>
    public static int Open(string target)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_FLAG_NO_UI,
            lpVerb = "open",
            lpFile = target,
            nShow = SW_SHOWNORMAL,
        };
        return ShellExecuteEx(ref info) ? 0 : Marshal.GetLastWin32Error();
    }

    /// <summary>
    /// Startet <paramref name="exe"/> mit „runas“ (UAC) und wartet auf das Ende. Rückgabe: Win32-Fehler des Starts
    /// (1223 = abgebrochen) oder 0 mit dem Exitcode; bei Abbruch über <paramref name="ct"/> wird nicht weiter gewartet.
    /// </summary>
    public static int RunElevatedAndWait(string exe, string arguments, CancellationToken ct, out int exitCode, out bool cancelled)
    {
        exitCode = -1;
        cancelled = false;
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_NOCLOSEPROCESS,
            lpVerb = "runas",
            lpFile = exe,
            lpParameters = arguments,
            lpDirectory = Path.GetDirectoryName(exe),
            nShow = SW_HIDE,
        };
        if (!ShellExecuteEx(ref info)) return Marshal.GetLastWin32Error();
        if (info.hProcess == IntPtr.Zero) return 0;
        try
        {
            uint wait;
            while ((wait = WaitForSingleObject(info.hProcess, 250)) != WAIT_OBJECT_0)
            {
                if (wait == WAIT_FAILED) return Marshal.GetLastWin32Error();
                if (ct.IsCancellationRequested)
                {
                    cancelled = true;
                    return 0;
                }
            }
            if (GetExitCodeProcess(info.hProcess, out var code)) exitCode = code;
            return 0;
        }
        finally
        {
            CloseHandle(info.hProcess);
        }
    }
}
