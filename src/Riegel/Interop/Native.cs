// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;

namespace Riegel.Interop;

/// <summary>
/// Konsolen-Aufrufe der Oberfläche (aus Zutritt): Riegel.exe ist ein Fensterprogramm (WinExe). Für die Befehlszeile
/// hängt es sich an die Konsole des Aufrufers, statt eine eigene zu öffnen – ein Doppelklick zeigt so nie ein
/// schwarzes Fenster. Für Skripte gibt es Riegel.Cli.exe (spec 4.2).
/// </summary>
internal static partial class Native
{
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
}
