// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
#nullable disable
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Security.Principal;

namespace Riegel.Collector.Collection
{
    /// <summary>Angaben über diesen Prozess und Rechner, die die Erfassung braucht (alle lesend).</summary>
    internal static class MachineFacts
    {
        /// <summary>Erhöhtes Token (Administratoren aktiv, nicht nur „deny only“)?</summary>
        public static bool IsAdmin()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (SystemException)
            {
                return false;
            }
        }

        /// <summary>32-Bit-Prozess auf 64-Bit-Windows (dann Sysnative statt System32)?</summary>
        public static bool IsWow64Process()
        {
            if (IntPtr.Size == 8) return false;
            try
            {
                bool wow64;
                return Native.IsWow64Process(Native.GetCurrentProcess(), out wow64) && wow64;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        /// <summary>Architektur des Betriebssystems (AMD64, x86, ARM64, ARM), auch aus einem emulierten Prozess.</summary>
        public static string Architecture()
        {
            try
            {
                ushort process, native;
                if (Native.IsWow64Process2(Native.GetCurrentProcess(), out process, out native))
                {
                    switch (native)
                    {
                        case 0x8664: return "AMD64";
                        case 0xAA64: return "ARM64";
                        case 0x014C: return "x86";
                        case 0x01C4: return "ARM";
                    }
                }
            }
            catch (EntryPointNotFoundException)
            {
                // IsWow64Process2 gibt es erst ab Windows 10 1709
            }
            Native.SYSTEM_INFO info;
            Native.GetNativeSystemInfo(out info);
            switch (info.ProcessorArchitecture)
            {
                case 9: return "AMD64";
                case 12: return "ARM64";
                case 0: return "x86";
                case 5: return "ARM";
                case 6: return "IA64";
                default: return null;
            }
        }

        /// <summary>SHA-256 einer Datei (die eigene Exe für den Kopf des Exports); null, wenn nicht lesbar.</summary>
        public static string FileSha256(string path)
        {
            try
            {
                return string.IsNullOrEmpty(path) || !File.Exists(path) ? null : RawCollector.Sha256Hex(File.ReadAllBytes(path));
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Kopf des Rechners aus den erfassten Einträgen: CurrentVersion (Build, UBR, Edition, Installationsart,
        /// DisplayVersion), Win32_OperatingSystem (ProductType, OSLanguage, LastBootUpTime, Build als Ersatz),
        /// Win32_ComputerSystem (DomainRole, Domäne), MachineGuid.
        /// </summary>
        public static void FillHeader(SnapshotData data)
        {
            MachineHeader m = data.Machine;
            m.HostName = Environment.MachineName;
            m.Architecture = Architecture();

            JObject current = RegValues(data, @"reg:HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (current != null)
            {
                m.Build = ToInt(RegString(current, "CurrentBuild") ?? RegString(current, "CurrentBuildNumber"));
                m.Ubr = ToInt(RegNumber(current, "UBR"));
                m.EditionId = RegString(current, "EditionID");
                m.InstallationType = RegString(current, "InstallationType");
                m.DisplayVersion = RegString(current, "DisplayVersion") ?? RegString(current, "ReleaseId");
            }
            JObject crypto = RegValues(data, @"reg:HKLM\SOFTWARE\Microsoft\Cryptography");
            if (crypto != null) m.MachineGuid = RegString(crypto, "MachineGuid");

            JObject os = FirstRow(data, @"wmi:root\cimv2:Win32_OperatingSystem");
            if (os != null)
            {
                m.ProductType = ToInt(os.Get("ProductType"));
                if (m.Build == null) m.Build = ToInt(os.Get("BuildNumber"));
                int? lcid = ToInt(os.Get("OSLanguage"));
                if (lcid.HasValue)
                {
                    try { m.OsLanguage = new CultureInfo(lcid.Value).Name; }
                    catch (ArgumentException) { m.OsLanguage = null; }
                }
                var boot = os.Get("LastBootUpTime") as string;
                DateTime parsed;
                if (boot != null && DateTime.TryParse(boot, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                    m.LastBootUtc = parsed;
            }
            JObject cs = FirstRow(data, @"wmi:root\cimv2:Win32_ComputerSystem");
            if (cs != null)
            {
                m.DomainRole = ToInt(cs.Get("DomainRole"));
                if (cs.Get("PartOfDomain") is bool && (bool)cs.Get("PartOfDomain")) m.DnsDomain = cs.Get("Domain") as string;
            }
        }

        private static JObject RegValues(SnapshotData data, string key)
        {
            CollectedItem item = data.Find(key);
            if (item == null || item.Status != Statuses.Ok) return null;
            var value = item.Value as JObject;
            return value != null ? value.Get("values") as JObject : null;
        }

        private static string RegString(JObject values, string name)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values.NameAt(i), name, StringComparison.OrdinalIgnoreCase))
                {
                    var v = values.ValueAt(i) as JObject;
                    return v != null ? v.Get("v") as string : null;
                }
            return null;
        }

        private static object RegNumber(JObject values, string name)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values.NameAt(i), name, StringComparison.OrdinalIgnoreCase))
                {
                    var v = values.ValueAt(i) as JObject;
                    return v != null ? v.Get("v") : null;
                }
            return null;
        }

        private static JObject FirstRow(SnapshotData data, string key)
        {
            CollectedItem item = data.Find(key);
            if (item == null || item.Status != Statuses.Ok) return null;
            var value = item.Value as JObject;
            var rows = value != null ? value.Get("rows") as IList : null;
            return rows != null && rows.Count > 0 ? rows[0] as JObject : null;
        }

        internal static int? ToInt(object value)
        {
            if (value == null) return null;
            if (value is string)
            {
                int parsed;
                return int.TryParse((string)value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? (int?)parsed : null;
            }
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch (OverflowException) { return null; }
            catch (InvalidCastException) { return null; }
            catch (FormatException) { return null; }
        }
    }
}
