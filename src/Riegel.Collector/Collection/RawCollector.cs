// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Riegel.Collector.Collection
{
    /// <summary>
    /// Rohdaten (spec 7.3): <c>raw:secedit-inf</c> (secedit /export, UTF-16 → Text; Namen in [Privilege Rights]
    /// beim Erfassen in SIDs übersetzt), <c>raw:gpresult-xml</c> (gpresult /x) und <c>raw:registry-pol</c>
    /// (Datei als base64 mit SHA-256). Werkzeuge nur über <see cref="ToolRunner"/>, Dateien nur im eigenen
    /// <see cref="TempWorkspace"/>.
    /// </summary>
    internal static class RawCollector
    {
        public static CollectedItem Tool(PlanRaw entry, TempWorkspace workspace, int timeoutSeconds, bool isAdmin, Pseudonymizer pseudonymizer)
        {
            var watch = Stopwatch.StartNew();
            CollectedItem item = ToolCore(entry, workspace, timeoutSeconds, isAdmin, pseudonymizer);
            item.DurationMs = (int)watch.ElapsedMilliseconds;
            return item;
        }

        private static CollectedItem ToolCore(PlanRaw entry, TempWorkspace workspace, int timeoutSeconds, bool isAdmin, Pseudonymizer pseudonymizer)
        {
            if (!ToolRunner.IsAllowed(entry.Tool, entry.Arguments))
                return CollectedItem.Fail(entry.Key, Statuses.NotCollected, null, "tool or arguments not on the allow-list");
            if (workspace == null) return CollectedItem.Fail(entry.Key, Statuses.NotCollected, null, "no temporary folder with a short path");
            string stem = Path.GetFileNameWithoutExtension(entry.Tool);
            string outFile = workspace.File(stem == "gpresult" ? "gpresult.xml" : stem + ".inf");
            string logFile = workspace.File(stem + ".log");
            ToolResult run = ToolRunner.Run(entry.Tool, entry.Arguments, outFile, logFile, workspace.Path, timeoutSeconds);
            if (!run.Started)
            {
                int code = run.StartError ?? -1;
                return code == 740 || code == Native.ERROR_ACCESS_DENIED
                    ? CollectedItem.Fail(entry.Key, Statuses.Denied, code, "The requested operation requires elevation")
                    : CollectedItem.Fail(entry.Key, Statuses.Error, code, "tool could not be started");
            }
            if (run.TimedOut) return CollectedItem.Fail(entry.Key, Statuses.Error, null, "timeout");
            if (run.ExitCode != 0 || !File.Exists(outFile))
                return CollectedItem.Fail(entry.Key, isAdmin ? Statuses.Error : Statuses.Denied, run.ExitCode, isAdmin ? "tool failed" : "Access denied");
            long length = new FileInfo(outFile).Length;
            if (length > entry.MaxBytes) return CollectedItem.Fail(entry.Key, Statuses.NotCollected, null, "larger than the limit of the plan");
            string text;
            using (var reader = new StreamReader(outFile, Encoding.Unicode, true)) text = reader.ReadToEnd();
            if (stem == "secedit") text = TranslatePrivilegeRights(text, pseudonymizer);
            return CollectedItem.Ok(entry.Key, new JObject().Add("text", text));
        }

        /// <summary>
        /// [Privilege Rights] mischt SIDs („*S-1-5-32-544“) und übersetzte Namen („Gast“): Namen werden hier, auf dem
        /// Zielrechner, in „*&lt;SID&gt;“ übersetzt; nicht auflösbare bleiben (mit --pseudonymize als Pseudonym).
        /// In [System Access] werden mit --pseudonymize auch NewAdministratorName und NewGuestName ersetzt.
        /// </summary>
        internal static string TranslatePrivilegeRights(string inf, Pseudonymizer pseudonymizer)
        {
            var lines = inf.Replace("\r\n", "\n").Split('\n');
            string section = null;
            var sb = new StringBuilder(inf.Length + 256);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)) section = trimmed;
                else if (section == "[Privilege Rights]" && trimmed.IndexOf('=') > 0) line = TranslateRight(trimmed, pseudonymizer);
                else if (pseudonymizer != null && section == "[System Access]" && trimmed.IndexOf('=') > 0)
                {
                    int eq = trimmed.IndexOf('=');
                    string name = trimmed.Substring(0, eq).Trim();
                    if (name == "NewAdministratorName" || name == "NewGuestName")
                        line = name + " = \"" + pseudonymizer.Name(trimmed.Substring(eq + 1).Trim().Trim('"')) + "\"";
                }
                sb.Append(line);
                if (i < lines.Length - 1) sb.Append("\r\n");
            }
            return sb.ToString();
        }

        private static string TranslateRight(string line, Pseudonymizer pseudonymizer)
        {
            int eq = line.IndexOf('=');
            string right = line.Substring(0, eq).Trim();
            string[] members = line.Substring(eq + 1).Split(',');
            var result = new List<string>();
            foreach (string raw in members)
            {
                string member = raw.Trim();
                if (member.Length == 0) continue;
                if (member.StartsWith("*", StringComparison.Ordinal)) { result.Add(member); continue; }
                string sid = AccountCollector.Sid(member);
                if (sid != null) result.Add("*" + sid);
                else result.Add(pseudonymizer != null ? pseudonymizer.Account(member) : member);
            }
            return right + " = " + string.Join(",", result.ToArray());
        }

        /// <summary>raw:registry-pol – die Datei (Pfad aus dem Plan, %SystemRoot% ersetzt) als base64 mit SHA-256.</summary>
        public static CollectedItem RegistryPol(PlanRaw entry)
        {
            var watch = Stopwatch.StartNew();
            CollectedItem item;
            try
            {
                string path = FilePath(entry.File);
                var info = new FileInfo(path);
                if (!info.Exists) item = new CollectedItem(entry.Key, Statuses.KeyMissing);
                else if (info.Length > entry.MaxBytes) item = CollectedItem.Fail(entry.Key, Statuses.NotCollected, null, "larger than the limit of the plan");
                else
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    item = CollectedItem.Ok(entry.Key, new JObject().Add("base64", Convert.ToBase64String(bytes)).Add("sha256", Sha256Hex(bytes)));
                }
            }
            catch (UnauthorizedAccessException)
            {
                item = CollectedItem.Fail(entry.Key, Statuses.Denied, Native.ERROR_ACCESS_DENIED, "Access denied");
            }
            catch (IOException e)
            {
                item = CollectedItem.Fail(entry.Key, Statuses.Error, null, e.GetType().Name);
            }
            item.DurationMs = (int)watch.ElapsedMilliseconds;
            return item;
        }

        /// <summary>„%SystemRoot%\System32\…“ → echter Pfad; ein 32-Bit-Prozess auf 64-Bit-Windows liest über Sysnative.</summary>
        internal static string FilePath(string planPath)
        {
            string root = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            string path = planPath.Replace("%SystemRoot%", root);
            if (MachineFacts.IsWow64Process())
                path = path.Replace(root + @"\System32\", root + @"\Sysnative\");
            return path;
        }

        public static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(64);
                foreach (byte b in hash) sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
