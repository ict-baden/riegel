// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;

namespace Riegel.Interop;

/// <summary>
/// Die wenigen Windows-Aufrufe der Oberfläche (aus Zutritt): Konsole für den Befehlszeilenmodus, Fensterrahmen (DWM),
/// Fenstergröße für Screenshots, AppUserModelID und Vordergrund für die zweite Instanz. Riegel.exe ist ein
/// Fensterprogramm (WinExe); für die Befehlszeile hängt es sich an die Konsole des Aufrufers, statt eine eigene zu öffnen
/// – ein Doppelklick zeigt so nie ein schwarzes Fenster. Für Skripte gibt es Riegel.Cli.exe (spec 4.2).
/// </summary>
internal static partial class Native
{
    // ---------- Konsole ----------
    public const int ATTACH_PARENT_PROCESS = -1;
    public const int STD_OUTPUT_HANDLE = -11;
    public const int STD_ERROR_HANDLE = -12;
    public const uint FILE_TYPE_DISK = 0x0001;
    public const uint FILE_TYPE_CHAR = 0x0002;
    public const uint FILE_TYPE_PIPE = 0x0003;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GetStdHandle(int stdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetStdHandle(int stdHandle, nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint GetFileType(nint handle);

    /// <summary>Codepage der angehängten Konsole (0 ohne Konsole) – mit ihr dekodiert PowerShell, was ein Programm in die Pipe schreibt.</summary>
    [LibraryImport("kernel32.dll")]
    public static partial uint GetConsoleOutputCP();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetFileSizeEx(nint handle, out long size);

    // ---------- DWM ----------
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int Left, Right, Top, Bottom; }

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS margins);

    // ---------- Fenstergröße ----------
    // Ohne sichtbaren Desktop begrenzt Windows Fenster auf die Bildschirmgröße. Für Screenshots in fester Größe hebt
    // WM_GETMINMAXINFO diese Grenze auf.
    public const int WM_GETMINMAXINFO = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    // ---------- Taskleiste und zweite Instanz ----------

    /// <summary>AppUserModelID des Prozesses (Gruppierung in der Taskleiste, spec 6: ICT-Baden.Riegel).</summary>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SetCurrentProcessExplicitAppUserModelID(string appId);

    public const int ASFW_ANY = -1;

    /// <summary>Die gerade gestartete Instanz darf den Vordergrund an die erste abgeben.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(int processId);
}
