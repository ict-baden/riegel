// SPDX-License-Identifier: MIT
using System.Text;

namespace Riegel.Cli;

/// <summary>
/// Kodierung der Ausgaben, damit Umlaute und „Anführungszeichen“ überall richtig ankommen, ohne die Konsole des
/// Aufrufers zu verstellen (Muster aus Zutritts ConsoleSession, für ein Konsolenprogramm vereinfacht):
/// <list type="bullet">
/// <item><b>Windows-Konsole</b>: Unicode über WriteConsoleW. <see cref="Console.OutputEncoding"/> = UTF-16 ruft – anders
/// als jede andere Kodierung – kein SetConsoleOutputCP auf; die Codepage der Konsole bleibt, wie sie war.</item>
/// <item><b>Umgeleitet</b> (Datei, Pipe, Icinga, geplante Aufgabe) und <b>Linux</b>: UTF-8 ohne BOM. PowerShell liest
/// eine Pipe mit <c>[Console]::OutputEncoding</c>; wer dort Umlaute braucht, stellt sie vorher auf UTF-8
/// (docs/command-line.md). JSON und die Icinga-Zeile sind davon kaum betroffen.</item>
/// </list>
/// stdout und stderr werden getrennt entschieden.
/// </summary>
internal static class ConsoleOutput
{
    public static void Configure()
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        if (OperatingSystem.IsWindows())
        {
            bool outToConsole = !Console.IsOutputRedirected;
            bool errToConsole = !Console.IsErrorRedirected;
            if (outToConsole || errToConsole) Console.OutputEncoding = Encoding.Unicode;
            if (!outToConsole) Console.SetOut(Writer(Console.OpenStandardOutput(), utf8));
            if (!errToConsole) Console.SetError(Writer(Console.OpenStandardError(), utf8));
            return;
        }
        Console.SetOut(Writer(Console.OpenStandardOutput(), utf8));
        Console.SetError(Writer(Console.OpenStandardError(), utf8));
    }

    private static TextWriter Writer(Stream stream, Encoding encoding) =>
        TextWriter.Synchronized(new StreamWriter(stream, encoding, bufferSize: 4096, leaveOpen: true) { AutoFlush = true });
}
