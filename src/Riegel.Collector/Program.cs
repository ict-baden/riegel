// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using Riegel.Collector.Collection;

namespace Riegel.Collector
{
    /// <summary>
    /// Portabler Sammler (spec 7.4): <c>Riegel.Collector.exe [--out &lt;Ordner&gt;] [--gzip] [--minimal] [--deep]
    /// [--pseudonymize] [--quiet] [--nur-lesen] [--version] [--help]</c>. Legt zuerst die Exportdatei an
    /// (FileMode.CreateNew, sonst Exitcode 3 mit RGL-0102), erfasst dann nach dem eingebetteten Plan und schreibt den
    /// Export und &lt;Datei&gt;.sha256. Exitcodes: 0 vollständig, 1 geschrieben, aber unvollständig, 3 nichts geschrieben.
    /// </summary>
    internal static class Program
    {
        public const int Complete = 0;
        public const int Incomplete = 1;
        public const int NothingWritten = 3;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                // Niedrige Priorität, damit eine Anlage nichts merkt; secedit und gpresult erben sie
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception)
            {
                // Priorität ist eine Höflichkeit, kein Muss
            }
            var messages = ConsoleMessages.Load(CultureInfo.CurrentUICulture, ConsoleEncoding());
            try
            {
                return Run(args, messages, Console.Out);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(messages.Format("Fatal", e.GetType().Name + ": " + e.Message));
                return NothingWritten;
            }
        }

        /// <summary>Optionen der Befehlszeile.</summary>
        internal sealed class Options
        {
            public string Out;
            public bool Gzip, Minimal, Deep, Pseudonymize, Quiet, ReadOnly, Version, Help;
            public string Error;
        }

        internal static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (Is(a, "--out") || Is(a, "-o"))
                {
                    if (i + 1 >= args.Length) { o.Error = "MissingValue|" + a; return o; }
                    o.Out = args[++i];
                }
                else if (Is(a, "--gzip")) o.Gzip = true;
                else if (Is(a, "--minimal")) o.Minimal = true;
                else if (Is(a, "--deep")) o.Deep = true;
                else if (Is(a, "--pseudonymize")) o.Pseudonymize = true;
                else if (Is(a, "--quiet") || Is(a, "-q")) o.Quiet = true;
                // spec 7.4 / Richtlinie 09: angenommen, ohne weitere Wirkung – der Sammler schreibt nie Konfiguration
                else if (Is(a, "--nur-lesen") || Is(a, "--read-only")) o.ReadOnly = true;
                else if (Is(a, "--version")) o.Version = true;
                else if (Is(a, "--help") || Is(a, "--hilfe") || Is(a, "-h") || Is(a, "/?")) o.Help = true;
                else { o.Error = "UnknownOption|" + a; return o; }
            }
            return o;
        }

        internal static int Run(string[] args, ConsoleMessages messages, TextWriter output)
        {
            Options o = Parse(args);
            string version = Version();
            if (o.Error != null)
            {
                string[] parts = o.Error.Split(new[] { '|' }, 2);
                Console.Error.WriteLine(messages.Format(parts[0], parts[1]));
                output.WriteLine(messages.Get("Help"));
                return NothingWritten;
            }
            if (o.Version)
            {
                output.WriteLine(messages.Format("Version", version));
                return Complete;
            }
            if (o.Help)
            {
                output.WriteLine(messages.Format("Version", version));
                output.WriteLine(messages.Get("Help"));
                return Complete;
            }

            Plan plan;
            try { plan = Plan.LoadEmbedded(); }
            catch (FormatException e)
            {
                Console.Error.WriteLine(messages.Format("PlanError", e.Message));
                return NothingWritten;
            }

            // Zuerst die Exportdatei anlegen: ist das Ziel schreibgeschützt (USB gesperrt), wird nichts erfasst
            DateTime collectedAt = DateTime.UtcNow;
            collectedAt = new DateTime(collectedAt.Ticks - collectedAt.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
            string exe = typeof(Program).Assembly.Location;
            string directory = o.Out ?? Path.GetDirectoryName(exe);
            string path;
            FileStream stream;
            try
            {
                path = Path.Combine(Path.GetFullPath(directory), ExportNames.FileName(Environment.MachineName, collectedAt, o.Gzip));
                stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            catch (Exception e)
            {
                if (!(e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException
                      || e is System.Security.SecurityException)) throw;
                Console.Error.WriteLine(messages.Format("TargetNotWritable", directory));
                return NothingWritten;
            }

            SnapshotData data;
            byte[] content;
            var watch = Stopwatch.StartNew();
            using (stream)
            {
                if (!o.Quiet)
                {
                    output.WriteLine(messages.Format("Version", version));
                    output.WriteLine(messages.Get("Collecting"));
                }
                var settings = new CollectSettings();
                settings.Minimal = o.Minimal;
                settings.Deep = o.Deep;
                settings.Pseudonymize = o.Pseudonymize;
                settings.Origin = "collector";
                settings.CollectorName = "Riegel.Collector";
                settings.CollectorVersion = version;
                settings.ExeSha256 = MachineFacts.FileSha256(exe);
                settings.CollectedAtUtc = collectedAt;
                data = Collection.Collector.Collect(plan, settings);
                content = new UTF8Encoding(false).GetBytes(JsonWriter.Write(data.ToJson()));
                if (o.Gzip) content = Gzip(content);
                try
                {
                    stream.Write(content, 0, content.Length);
                    stream.Flush();
                }
                catch (IOException e)
                {
                    Console.Error.WriteLine(messages.Format("WriteFailed", e.Message));
                    return NothingWritten;
                }
            }

            string hash = RawCollector.Sha256Hex(content);
            try
            {
                using (var sidecar = new FileStream(path + ".sha256", FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    byte[] line = new UTF8Encoding(false).GetBytes(hash + "  " + Path.GetFileName(path) + "\n");
                    sidecar.Write(line, 0, line.Length);
                }
            }
            catch (IOException e)
            {
                Console.Error.WriteLine(messages.Format("SidecarFailed", e.Message));
            }
            catch (UnauthorizedAccessException e)
            {
                Console.Error.WriteLine(messages.Format("SidecarFailed", e.Message));
            }

            bool incomplete = data.Incomplete;
            if (!o.Quiet)
            {
                if (!data.IsAdmin) output.WriteLine(messages.Get("NotAdmin"));
                output.WriteLine(messages.Format("Summary", data.Items.Count,
                    data.Count(Statuses.Denied) + data.Count(Statuses.Error), data.Count(Statuses.NotCollected),
                    (watch.ElapsedMilliseconds / 1000.0).ToString("0.0", CultureInfo.CurrentCulture)));
            }
            output.WriteLine(messages.Format("Written", path));
            output.WriteLine(messages.Format("Sha256", hash));
            if (!o.Quiet) output.WriteLine(messages.Get(incomplete ? "Incomplete" : "Complete"));
            return incomplete ? Incomplete : Complete;
        }

        private static byte[] Gzip(byte[] content)
        {
            using (var buffer = new MemoryStream())
            {
                using (var zip = new GZipStream(buffer, CompressionMode.Compress, true)) zip.Write(content, 0, content.Length);
                return buffer.ToArray();
            }
        }

        private static bool Is(string arg, string name)
        {
            return string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Version aus collector-version.txt (über die Assembly-Angaben des Builds).</summary>
        internal static string Version()
        {
            var attributes = typeof(Program).Assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
            return attributes.Length > 0 ? ((AssemblyInformationalVersionAttribute)attributes[0]).InformationalVersion : "unknown";
        }

        private static Encoding ConsoleEncoding()
        {
            try { return Console.OutputEncoding; }
            catch (IOException) { return null; }
        }
    }
}
