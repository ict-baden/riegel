// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Riegel.Collector.Collection
{
    /// <summary>Überwachungsrichtlinie, LSASS-Schutz, WinHTTP-Proxy und das Wininit-Ereignis 12 (spec 7.3).</summary>
    internal static class SystemCollector
    {
        // ------------------------------------------------------------------ Überwachungsrichtlinie

        /// <summary>
        /// api:audit-policy – AuditQuerySystemPolicy für alle Unterkategorien, nachdem SeSecurityPrivilege im eigenen
        /// Token eingeschaltet wurde (danach zurückgesetzt). Ohne Adminrechte: Status statt Absturz (auch bei Null-Zeiger).
        /// </summary>
        public static CollectedItem AuditPolicy(string key, IAuditPolicy audit)
        {
            int error;
            if (!audit.EnableSecurityPrivilege(out error))
                return CollectedItem.Fail(key, Statuses.Denied, error == 0 ? Native.ERROR_PRIVILEGE_NOT_HELD : error, "SeSecurityPrivilege not held");
            try
            {
                var subCategories = new List<Guid>();
                var settings = new List<int>();
                if (!audit.Query(subCategories, settings, out error))
                {
                    bool denied = error == Native.ERROR_PRIVILEGE_NOT_HELD || error == Native.ERROR_ACCESS_DENIED;
                    return CollectedItem.Fail(key, denied ? Statuses.Denied : Statuses.Error, error, denied ? "Access denied" : "Audit policy query failed");
                }
                var result = new JObject();
                for (int i = 0; i < subCategories.Count && i < settings.Count; i++)
                    result.Add(subCategories[i].ToString("B").ToUpperInvariant(), (long)(settings[i] & 3));
                return CollectedItem.Ok(key, result);
            }
            finally { audit.RestorePrivilege(); }
        }

        // ------------------------------------------------------------------ LSASS (nur --deep)

        /// <summary>api:lsass-protection – OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) und NtQueryInformationProcess(61).</summary>
        public static CollectedItem LsassProtection(string key)
        {
            int pid = -1;
            Process[] processes = Process.GetProcessesByName("lsass");
            try { if (processes.Length > 0) pid = processes[0].Id; }
            finally { foreach (var p in processes) p.Dispose(); }
            if (pid < 0) return CollectedItem.Fail(key, Statuses.Error, null, "lsass not found");
            IntPtr handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                return CollectedItem.Fail(key, error == Native.ERROR_ACCESS_DENIED ? Statuses.Denied : Statuses.Error, error, "OpenProcess failed");
            }
            try
            {
                byte protection;
                int length;
                int status = Native.NtQueryInformationProcess(handle, Native.ProcessProtectionInformation, out protection, 1, out length);
                // STATUS_INVALID_INFO_CLASS: die Abfrage gibt es erst ab Windows 8.1 / Server 2012 R2
                if (status == unchecked((int)0xC0000003)) return CollectedItem.Fail(key, Statuses.NotApplicable, status, "not available on this Windows version");
                if (status != 0) return CollectedItem.Fail(key, Statuses.Error, status, "NtQueryInformationProcess failed");
                return CollectedItem.Ok(key, Protection(protection));
            }
            finally { Native.CloseHandle(handle); }
        }

        /// <summary>PS_PROTECTION: Bits 0–2 Typ (0 keiner, 1 Light/PPL, 2 voll), Bits 4–7 Signierer.</summary>
        internal static JObject Protection(byte value)
        {
            int type = value & 0x7;
            string name = type == 0 ? "none" : type == 1 ? "light" : type == 2 ? "full" : "unknown";
            return new JObject().Add("protectionByte", (long)value).Add("type", name).Add("signer", (long)(value >> 4));
        }

        // ------------------------------------------------------------------ WinHTTP-Proxy

        /// <summary>api:winhttp-proxy – WinHttpGetDefaultProxyConfiguration (liest die Einstellung, keine Verbindung).</summary>
        public static CollectedItem WinHttpProxy(string key)
        {
            var info = new Native.WINHTTP_PROXY_INFO();
            if (!Native.WinHttpGetDefaultProxyConfiguration(ref info))
            {
                int error = Marshal.GetLastWin32Error();
                return CollectedItem.Fail(key, Statuses.Error, error, "WinHTTP error");
            }
            try
            {
                return CollectedItem.Ok(key, new JObject()
                    .Add("accessType", (long)info.AccessType)
                    .Add("proxy", info.Proxy != IntPtr.Zero ? Marshal.PtrToStringUni(info.Proxy) : null)
                    .Add("bypass", info.ProxyBypass != IntPtr.Zero ? Marshal.PtrToStringUni(info.ProxyBypass) : null));
            }
            finally
            {
                if (info.Proxy != IntPtr.Zero) Native.GlobalFree(info.Proxy);
                if (info.ProxyBypass != IntPtr.Zero) Native.GlobalFree(info.ProxyBypass);
            }
        }

        // ------------------------------------------------------------------ Ereignis Wininit 12

        /// <summary>
        /// evt:system:wininit-12 – neuestes Ereignis per EvtQuery (feste XPath aus dem Plan, rückwärts), nur wenn es
        /// nach dem letzten Start liegt. found=false, wenn es keines gibt.
        /// </summary>
        public static CollectedItem WininitEvent(PlanEvent entry, DateTime? bootUtc)
        {
            if (!bootUtc.HasValue) return CollectedItem.Fail(entry.Key, Statuses.NotCollected, null, "boot time unknown");
            IntPtr query = Native.EvtQuery(IntPtr.Zero, entry.Log, entry.Xpath, Native.EvtQueryChannelPath | Native.EvtQueryReverseDirection);
            if (query == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                return CollectedItem.Fail(entry.Key, error == Native.ERROR_ACCESS_DENIED ? Statuses.Denied : Statuses.Error, error, "EvtQuery failed");
            }
            try
            {
                var events = new IntPtr[1];
                int returned;
                string xml = null;
                if (Native.EvtNext(query, 1, events, 5000, 0, out returned) && returned == 1)
                {
                    try { xml = Render(events[0]); }
                    finally { Native.EvtClose(events[0]); }
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != Native.ERROR_NO_MORE_ITEMS) return CollectedItem.Fail(entry.Key, Statuses.Error, error, "EvtNext failed");
                }
                return CollectedItem.Ok(entry.Key, EventValue(xml, bootUtc.Value));
            }
            finally { Native.EvtClose(query); }
        }

        private static string Render(IntPtr handle)
        {
            int used, count;
            Native.EvtRender(IntPtr.Zero, handle, Native.EvtRenderEventXml, 0, IntPtr.Zero, out used, out count);
            if (used <= 0) return null;
            IntPtr buffer = Marshal.AllocHGlobal(used);
            try
            {
                if (!Native.EvtRender(IntPtr.Zero, handle, Native.EvtRenderEventXml, used, buffer, out used, out count)) return null;
                return Marshal.PtrToStringUni(buffer);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        /// <summary>Wert aus dem Ereignis-XML (oder null = keines): Zeitpunkt, EventData-Texte, Startzeit.</summary>
        internal static JObject EventValue(string xml, DateTime bootUtc)
        {
            var o = new JObject();
            DateTime created = DateTime.MinValue;
            bool found = xml != null && TryCreated(xml, out created) && created >= bootUtc;
            o.Add("found", found);
            o.Add("timeCreatedUtc", found ? SnapshotData.Iso(created) : null);
            o.Add("eventData", found ? (object)Data(xml) : new List<object>());
            o.Add("bootTimeUtc", SnapshotData.Iso(bootUtc));
            return o;
        }

        private static bool TryCreated(string xml, out DateTime utc)
        {
            utc = DateTime.MinValue;
            int i = xml.IndexOf("<TimeCreated", StringComparison.Ordinal);
            if (i < 0) return false;
            int a = xml.IndexOf("SystemTime=", i, StringComparison.Ordinal);
            if (a < 0) return false;
            char quote = xml[a + 11];
            int end = xml.IndexOf(quote, a + 12);
            if (end < 0) return false;
            string text = xml.Substring(a + 12, end - a - 12);
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);
        }

        private static List<object> Data(string xml)
        {
            var list = new List<object>();
            int section = xml.IndexOf("<EventData", StringComparison.Ordinal);
            if (section < 0) return list;
            int sectionEnd = xml.IndexOf("</EventData>", section, StringComparison.Ordinal);
            if (sectionEnd < 0) sectionEnd = xml.Length;
            int from = section;
            while (true)
            {
                int start = xml.IndexOf("<Data", from, StringComparison.Ordinal);
                if (start < 0 || start >= sectionEnd) break;
                int close = xml.IndexOf('>', start);
                if (close < 0) break;
                if (xml[close - 1] == '/') { list.Add(""); from = close + 1; continue; }
                int end = xml.IndexOf("</Data>", close, StringComparison.Ordinal);
                if (end < 0) break;
                list.Add(Unescape(xml.Substring(close + 1, end - close - 1)));
                from = end + 7;
            }
            return list;
        }

        private static string Unescape(string s)
        {
            return s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&");
        }
    }

    /// <summary>Lesender Zugriff auf die Überwachungsrichtlinie (in Tests ersetzt).</summary>
    internal interface IAuditPolicy
    {
        /// <summary>SeSecurityPrivilege im eigenen Token einschalten; false (mit Fehler), wenn das Token es nicht hat.</summary>
        bool EnableSecurityPrivilege(out int error);

        /// <summary>Alle Unterkategorien mit ihrer Einstellung (Bit 1 Erfolg, Bit 2 Fehler).</summary>
        bool Query(List<Guid> subCategories, List<int> settings, out int error);

        /// <summary>Vorherigen Zustand des Privilegs wiederherstellen.</summary>
        void RestorePrivilege();
    }

    /// <summary>AuditEnumerateCategories/SubCategories und AuditQuerySystemPolicy (advapi32).</summary>
    internal sealed class Win32AuditPolicy : IAuditPolicy
    {
        private IntPtr _token;
        private Native.TOKEN_PRIVILEGES _previous;
        private bool _changed;

        public bool EnableSecurityPrivilege(out int error)
        {
            error = 0;
            if (!Native.OpenProcessToken(Native.GetCurrentProcess(), Native.TOKEN_ADJUST_PRIVILEGES | Native.TOKEN_QUERY, out _token))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }
            Native.LUID luid;
            if (!Native.LookupPrivilegeValue(null, "SeSecurityPrivilege", out luid))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }
            var state = new Native.TOKEN_PRIVILEGES();
            state.PrivilegeCount = 1;
            state.Luid = luid;
            state.Attributes = Native.SE_PRIVILEGE_ENABLED;
            _previous = new Native.TOKEN_PRIVILEGES();
            int length;
            if (!Native.AdjustTokenPrivileges(_token, false, ref state, Marshal.SizeOf(typeof(Native.TOKEN_PRIVILEGES)), ref _previous, out length))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }
            error = Marshal.GetLastWin32Error();
            if (error == Native.ERROR_NOT_ALL_ASSIGNED) return false; // das Token hat das Privileg nicht (kein Admin)
            _changed = true;
            error = 0;
            return true;
        }

        public bool Query(List<Guid> subCategories, List<int> settings, out int error)
        {
            error = 0;
            IntPtr categories;
            int categoryCount;
            if (!Native.AuditEnumerateCategories(out categories, out categoryCount) || categories == IntPtr.Zero)
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }
            int guidSize = Marshal.SizeOf(typeof(Guid));
            try
            {
                for (int i = 0; i < categoryCount; i++)
                {
                    var category = (Guid)Marshal.PtrToStructure(new IntPtr(categories.ToInt64() + (long)i * guidSize), typeof(Guid));
                    IntPtr subs;
                    int subCount;
                    if (!Native.AuditEnumerateSubCategories(ref category, false, out subs, out subCount) || subs == IntPtr.Zero) continue;
                    try
                    {
                        for (int j = 0; j < subCount; j++)
                            subCategories.Add((Guid)Marshal.PtrToStructure(new IntPtr(subs.ToInt64() + (long)j * guidSize), typeof(Guid)));
                    }
                    finally { Native.AuditFree(subs); }
                }
            }
            finally { Native.AuditFree(categories); }
            if (subCategories.Count == 0)
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }
            IntPtr policy;
            if (!Native.AuditQuerySystemPolicy(subCategories.ToArray(), subCategories.Count, out policy) || policy == IntPtr.Zero)
            {
                error = Marshal.GetLastWin32Error();
                if (error == 0) error = Native.ERROR_PRIVILEGE_NOT_HELD;
                return false;
            }
            try
            {
                int size = Marshal.SizeOf(typeof(Native.AUDIT_POLICY_INFORMATION));
                for (int i = 0; i < subCategories.Count; i++)
                {
                    var info = (Native.AUDIT_POLICY_INFORMATION)Marshal.PtrToStructure(new IntPtr(policy.ToInt64() + (long)i * size), typeof(Native.AUDIT_POLICY_INFORMATION));
                    settings.Add(info.AuditingInformation);
                }
            }
            finally { Native.AuditFree(policy); }
            return true;
        }

        public void RestorePrivilege()
        {
            if (_token == IntPtr.Zero) return;
            if (_changed)
            {
                var ignored = new Native.TOKEN_PRIVILEGES();
                int length;
                Native.AdjustTokenPrivileges(_token, false, ref _previous, Marshal.SizeOf(typeof(Native.TOKEN_PRIVILEGES)), ref ignored, out length);
            }
            Native.CloseHandle(_token);
            _token = IntPtr.Zero;
            _changed = false;
        }
    }
}
