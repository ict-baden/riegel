// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
// Die EINZIGE Stelle, an der der Sammler einen Prozess startet (spec 7.4, Quelltext-Test).
#nullable disable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Riegel.Collector.Collection
{
    /// <summary>Ergebnis eines Werkzeuglaufs.</summary>
    internal sealed class ToolResult
    {
        public bool Started;
        public bool TimedOut;
        public int ExitCode;
        public int? StartError;
        public int DurationMs;
    }

    /// <summary>
    /// Startet ausschließlich <c>secedit.exe /export /cfg {out} /log {log} /quiet</c> und
    /// <c>gpresult.exe /scope computer /x {out} /f</c> aus dem Systemordner – mit genau diesen festen
    /// Argumentlisten, ohne Shell (nie eine UAC-Abfrage), ohne Fenster, mit Zeitlimit. Andere Werkzeuge oder
    /// Argumente lehnt er ab, auch wenn ein Plan sie nennen würde.
    /// </summary>
    internal static class ToolRunner
    {
        private static readonly string[][] Allowed =
        {
            new[] { "secedit.exe", "/export", "/cfg", "{out}", "/log", "{log}", "/quiet" },
            new[] { "gpresult.exe", "/scope", "computer", "/x", "{out}", "/f" },
        };

        /// <summary>Steht dieses Werkzeug mit genau diesen Argumenten auf der Erlaubnisliste?</summary>
        public static bool IsAllowed(string tool, string[] arguments)
        {
            if (tool == null || arguments == null) return false;
            foreach (string[] allowed in Allowed)
            {
                if (!string.Equals(allowed[0], tool, StringComparison.OrdinalIgnoreCase) || allowed.Length - 1 != arguments.Length) continue;
                bool same = true;
                for (int i = 0; i < arguments.Length; i++)
                    if (!string.Equals(allowed[i + 1], arguments[i], StringComparison.Ordinal)) { same = false; break; }
                if (same) return true;
            }
            return false;
        }

        /// <summary>Befehlszeile mit eingesetzten Dateien; Pfade in Anführungszeichen. Dateien dürfen kein „"“ enthalten.</summary>
        internal static string CommandLine(string[] arguments, string outFile, string logFile)
        {
            var sb = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (sb.Length > 0) sb.Append(' ');
                if (argument == "{out}") sb.Append('"').Append(outFile).Append('"');
                else if (argument == "{log}") sb.Append('"').Append(logFile).Append('"');
                else sb.Append(argument);
            }
            return sb.ToString();
        }

        /// <summary>Pfad des Werkzeugs: System32, bei einem 32-Bit-Prozess auf 64-Bit-Windows Sysnative.</summary>
        internal static string ToolPath(string tool)
        {
            string root = Environment.GetEnvironmentVariable("SystemRoot");
            if (string.IsNullOrEmpty(root)) root = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.System));
            return Path.Combine(Path.Combine(root, MachineFacts.IsWow64Process() ? "Sysnative" : "System32"), tool);
        }

        /// <summary>Startet das Werkzeug, wartet höchstens <paramref name="timeoutSeconds"/> und beendet es sonst.</summary>
        public static ToolResult Run(string tool, string[] arguments, string outFile, string logFile, string workingDirectory, int timeoutSeconds)
        {
            var result = new ToolResult();
            if (!IsAllowed(tool, arguments)) throw new InvalidOperationException("Werkzeug nicht erlaubt: " + tool);
            if ((outFile ?? "").IndexOf('"') >= 0 || (logFile ?? "").IndexOf('"') >= 0) throw new ArgumentException("Dateiname mit Anführungszeichen");
            var info = new ProcessStartInfo(ToolPath(tool), CommandLine(arguments, outFile, logFile));
            info.UseShellExecute = false; // nie über die Shell: keine Rechteerhöhung, keine Dateizuordnungen
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.WorkingDirectory = workingDirectory;
            var watch = Stopwatch.StartNew();
            Process process;
            try
            {
                process = Process.Start(info);
            }
            catch (Win32Exception e)
            {
                // 740 = ERROR_ELEVATION_REQUIRED: das Werkzeug verlangt Adminrechte
                result.StartError = e.NativeErrorCode;
                result.DurationMs = (int)watch.ElapsedMilliseconds;
                return result;
            }
            using (process)
            {
                result.Started = true;
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
                // Ausgaben lesen und verwerfen, damit volle Puffer den Prozess nicht blockieren
                process.OutputDataReceived += delegate { };
                process.ErrorDataReceived += delegate { };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(timeoutSeconds * 1000))
                {
                    result.TimedOut = true;
                    try { process.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                    process.WaitForExit(5000);
                }
                else
                {
                    process.WaitForExit();
                    result.ExitCode = process.ExitCode;
                }
            }
            result.DurationMs = (int)watch.ElapsedMilliseconds;
            return result;
        }
    }
}
