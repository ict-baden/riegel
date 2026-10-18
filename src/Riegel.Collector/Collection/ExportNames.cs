// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Riegel.Collector.Collection
{
    /// <summary>Dateinamen der Exporte (wie SnapshotFile in Core, aber ohne Core: der Sammler läuft unter CLR 2).</summary>
    internal static class ExportNames
    {
        /// <summary>
        /// &lt;HOST&gt;_&lt;yyyyMMddTHHmmssZ&gt;.snapshot.json(.gz); Hostname wie FileNames.Host in Core: nur
        /// [A-Za-z0-9._-], Umlaute umschrieben, höchstens 63 Zeichen, reservierte Gerätenamen mit „_“, groß geschrieben.
        /// </summary>
        public static string FileName(string host, DateTime collectedAtUtc, bool gzip)
        {
            return SafeHost(host).ToUpperInvariant() + "_" + collectedAtUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)
                + (gzip ? ".snapshot.json.gz" : ".snapshot.json");
        }

        public static string SafeHost(string host)
        {
            var sb = new StringBuilder();
            foreach (char c in (host ?? "").Normalize(NormalizationForm.FormC))
            {
                switch (c)
                {
                    case 'ä': sb.Append("ae"); continue;
                    case 'ö': sb.Append("oe"); continue;
                    case 'ü': sb.Append("ue"); continue;
                    case 'Ä': sb.Append("Ae"); continue;
                    case 'Ö': sb.Append("Oe"); continue;
                    case 'Ü': sb.Append("Ue"); continue;
                    case 'ß': sb.Append("ss"); continue;
                }
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                sb.Append(ok ? c : '-');
            }
            var collapsed = new StringBuilder();
            foreach (char c in sb.ToString())
            {
                if (c == '-' && collapsed.Length > 0 && collapsed[collapsed.Length - 1] == '-') continue;
                collapsed.Append(c);
            }
            string result = collapsed.ToString().Trim('-', '.', ' ');
            if (result.Length > 63) result = result.Substring(0, 63).TrimEnd('-', '.');
            if (result.Length == 0) result = "unknown";
            string stem = result.Split('.')[0].ToUpperInvariant();
            foreach (string reserved in new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" })
                if (stem == reserved) { result = "_" + result; break; }
            return result.Length > 63 ? result.Substring(0, 63) : result;
        }
    }
}
