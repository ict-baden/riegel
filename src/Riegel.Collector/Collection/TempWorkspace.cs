// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
// Die einzige Stelle, an der der Sammler löscht – und zwar nur in seinem eigenen, frisch angelegten Arbeitsordner.
#nullable disable
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Riegel.Collector.Collection
{
    /// <summary>
    /// Frischer Arbeitsordner <c>%TEMP%\riegel-&lt;zufällig&gt;</c> für die Dateien von secedit und gpresult
    /// (spec 4.4). Der Pfad bleibt höchstens <c>tempPathMaxChars</c> lang (gpresult verlangt ≤ 127 Zeichen), sonst
    /// über den kurzen 8.3-Namen von %TEMP%. Beim Aufräumen werden nur Dateien gelöscht, die dieser Ordner enthält,
    /// dann der Ordner selbst.
    /// </summary>
    internal sealed class TempWorkspace : IDisposable
    {
        private TempWorkspace(string path) { Path = path; }

        public string Path { get; private set; }

        /// <summary>Wurde der Ordner beim Aufräumen vollständig entfernt?</summary>
        public bool Removed { get; private set; }

        /// <summary>Legt den Ordner an; null, wenn kein ausreichend kurzer, neuer Ordner möglich ist.</summary>
        public static TempWorkspace Create(int maxPathChars)
        {
            string temp = System.IO.Path.GetTempPath();
            if (temp.Length + 40 > maxPathChars)
            {
                var shortPath = new StringBuilder(260);
                int length = Native.GetShortPathName(temp, shortPath, shortPath.Capacity);
                if (length > 0 && length < shortPath.Capacity) temp = shortPath.ToString();
            }
            var random = new Random();
            for (int attempt = 0; attempt < 5; attempt++)
            {
                string name = "riegel-" + random.Next(0x10000000, int.MaxValue).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
                string path = System.IO.Path.Combine(temp, name);
                // Platz für den längsten Dateinamen darin („\gpresult.xml“)
                if (path.Length + 13 > maxPathChars) return null;
                if (Directory.Exists(path) || System.IO.File.Exists(path)) continue;
                Directory.CreateDirectory(path);
                return new TempWorkspace(path);
            }
            return null;
        }

        public string File(string name) { return System.IO.Path.Combine(Path, name); }

        public void Dispose()
        {
            if (Path == null) return;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (Directory.Exists(Path))
                    {
                        foreach (string file in Directory.GetFiles(Path)) System.IO.File.Delete(file);
                        Directory.Delete(Path, false);
                    }
                    Removed = !Directory.Exists(Path);
                    if (Removed) return;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                Thread.Sleep(200); // secedit gibt seine Protokolldatei manchmal erst kurz nach dem Ende frei
            }
        }
    }
}
