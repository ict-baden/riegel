// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
#nullable disable
using System;
using System.Collections;
using System.Diagnostics;
using System.Management;

namespace Riegel.Collector.Collection
{
    /// <summary>Abbruchwunsch des Aufrufers (net35 kennt kein CancellationToken).</summary>
    internal delegate bool CancelRequested();

    /// <summary>Was und wie erfasst wird; Kopfangaben des Sammlers kommen vom Aufrufer (Exe oder App).</summary>
    internal sealed class CollectSettings
    {
        public bool Minimal;
        public bool Deep;
        public bool Pseudonymize;
        public string Origin = "collector";
        public string CollectorName = "Riegel.Collector";
        public string CollectorVersion = "unknown";
        public string ExeSha256;
        public DateTime CollectedAtUtc = DateTime.UtcNow;
        public CancelRequested Cancel = null;

        /// <summary>Ersatz für Tests; null = echte Registry.</summary>
        public IRegistry Registry = null;
        public IAuditPolicy Audit = null;
        public ILsaRights Lsa = null;
    }

    /// <summary>
    /// Erfasst diesen Rechner nach dem eingebetteten Plan (spec 7.3, 7.4): Registry, WMI, APIs, Ereignis, Rohdaten.
    /// Jede Abfrage steht in einem eigenen try/catch – ein Fehler wird ein Status, nie ein Abbruch.
    /// </summary>
    internal static class Collector
    {
        public static SnapshotData Collect(Plan plan, CollectSettings settings)
        {
            var data = new SnapshotData();
            data.CollectorName = settings.CollectorName;
            data.CollectorVersion = settings.CollectorVersion;
            data.ExeSha256 = settings.ExeSha256;
            data.Clr = Environment.Version.ToString();
            data.Is64BitProcess = IntPtr.Size == 8;
            data.CollectedAtUtc = settings.CollectedAtUtc;
            data.IsAdmin = MachineFacts.IsAdmin();
            data.Origin = settings.Origin;
            data.Minimal = settings.Minimal;
            data.Deep = settings.Deep;
            data.Pseudonymized = settings.Pseudonymize;

            IRegistry registry = settings.Registry ?? new Win32Registry();
            Pseudonymizer pseudonymizer = settings.Pseudonymize ? new Pseudonymizer(MachineGuid(registry), Environment.MachineName) : null;

            // 1. Registry
            Guard(data, "reg:HKLM", delegate { new RegistryCollector(registry, plan, pseudonymizer, data).CollectAll(); });
            if (Cancelled(settings)) return Finish(data);

            // 2. WMI
            var wmi = new WmiCollector(plan, data.IsAdmin, pseudonymizer);
            foreach (PlanWmi entry in plan.Wmi)
            {
                if (Cancelled(settings)) return Finish(data);
                PlanWmi current = entry;
                if (!Wanted(current.Mode, settings, data, current.Key)) continue;
                Guard(data, current.Key, delegate { data.Items.Add(wmi.Collect(current)); });
            }
            MachineFacts.FillHeader(data);

            // 3. APIs
            foreach (PlanApi entry in plan.Api)
            {
                if (Cancelled(settings)) return Finish(data);
                PlanApi current = entry;
                if (!Wanted(current.Mode, settings, data, current.Key)) continue;
                Guard(data, current.Key, delegate { data.Items.Add(Timed(delegate { return Api(plan, current, settings, data, pseudonymizer); })); });
            }

            // 4. Ereignisse
            foreach (PlanEvent entry in plan.Events)
            {
                PlanEvent current = entry;
                if (!Wanted(current.Mode, settings, data, current.Key)) continue;
                Guard(data, current.Key, delegate { data.Items.Add(Timed(delegate { return SystemCollector.WininitEvent(current, data.Machine.LastBootUtc); })); });
            }

            // 5. Rohdaten (secedit und gpresult nur ohne --minimal, im eigenen Arbeitsordner)
            TempWorkspace workspace = null;
            try
            {
                foreach (PlanRaw entry in plan.Raw)
                {
                    if (Cancelled(settings)) return Finish(data);
                    PlanRaw current = entry;
                    if (!Wanted(current.Mode, settings, data, current.Key)) continue;
                    if (current.Kind == "file")
                    {
                        Guard(data, current.Key, delegate { data.Items.Add(RawCollector.RegistryPol(current)); });
                        continue;
                    }
                    if (workspace == null) workspace = TempWorkspace.Create(plan.TempPathMaxChars);
                    TempWorkspace ws = workspace;
                    Guard(data, current.Key, delegate { data.Items.Add(RawCollector.Tool(current, ws, plan.ToolTimeoutSeconds, data.IsAdmin, pseudonymizer)); });
                }
            }
            finally
            {
                if (workspace != null) workspace.Dispose();
            }
            return Finish(data);
        }

        private delegate void Step();

        private delegate CollectedItem ItemStep();

        /// <summary>Ein unerwarteter Fehler in einem Schritt wird ein Eintrag „error“, nie ein Abbruch.</summary>
        private static void Guard(SnapshotData data, string key, Step step)
        {
            try
            {
                step();
            }
            catch (Exception e)
            {
                if (data.Find(key) != null) return;
                // COM- und Zugriffsfehler mit Code; sonst nur der Typname (nie der Text, er kann Pfade enthalten)
                var com = e as System.Runtime.InteropServices.COMException;
                if (e is UnauthorizedAccessException) data.Items.Add(CollectedItem.Fail(key, Statuses.Denied, unchecked((int)0x80070005), "Access denied"));
                else if (com != null) data.Items.Add(CollectedItem.Fail(key, WmiCollector.StatusFor(com.ErrorCode), com.ErrorCode, "COM error"));
                else data.Items.Add(CollectedItem.Fail(key, Statuses.Error, null, e.GetType().Name));
            }
        }

        private static CollectedItem Timed(ItemStep step)
        {
            var watch = Stopwatch.StartNew();
            CollectedItem item = step();
            if (item.DurationMs == 0) item.DurationMs = (int)watch.ElapsedMilliseconds;
            return item;
        }

        private static bool Cancelled(CollectSettings settings)
        {
            return settings.Cancel != null && settings.Cancel();
        }

        private static SnapshotData Finish(SnapshotData data)
        {
            if (data.Machine.HostName == null) MachineFacts.FillHeader(data);
            return data;
        }

        /// <summary>Planmodus gegen die Schalter: übersprungene Einträge werden NotCollected mit Grund.</summary>
        private static bool Wanted(string mode, CollectSettings settings, SnapshotData data, string key)
        {
            if (mode == PlanModes.SkipInMinimal && settings.Minimal)
            {
                data.Items.Add(CollectedItem.Fail(key, Statuses.NotCollected, null, "skipped with --minimal"));
                return false;
            }
            if (mode == PlanModes.DeepOnly && !settings.Deep)
            {
                data.Items.Add(CollectedItem.Fail(key, Statuses.NotCollected, null, "only with --deep"));
                return false;
            }
            return true;
        }

        private static CollectedItem Api(Plan plan, PlanApi entry, CollectSettings settings, SnapshotData data, Pseudonymizer pseudonymizer)
        {
            switch (entry.Api)
            {
                case "netusermodals": return AccountCollector.UserModals(entry.Key);
                case "netuserenum": return AccountCollector.UserEnum(entry.Key, pseudonymizer);
                case "lsa-rights": return AccountCollector.Rights(entry.Key, entry.Rights, settings.Lsa ?? new Win32LsaRights());
                case "localgroup": return AccountCollector.LocalGroup(entry.Key, entry.Sid, pseudonymizer);
                case "audit-policy": return SystemCollector.AuditPolicy(entry.Key, settings.Audit ?? new Win32AuditPolicy());
                case "lsass-protection": return SystemCollector.LsassProtection(entry.Key);
                // --minimal: nur die Profile, keine Regelliste
                case "firewall": return ComCollector.Firewall(entry.Key, !settings.Minimal, pseudonymizer);
                case "windows-update-history":
                    return ServiceRunning("wuauserv")
                        ? ComCollector.UpdateHistory(entry.Key, plan.WindowsUpdateHistoryEntries)
                        : CollectedItem.Fail(entry.Key, Statuses.NotCollected, null, "Windows Update not running \u2013 not queried");
                case "winhttp-proxy": return SystemCollector.WinHttpProxy(entry.Key);
                case "wsc-av": return ComCollector.SecurityCenter(entry.Key);
                default: return CollectedItem.Fail(entry.Key, Statuses.NotCollected, null, "unknown API in the plan");
            }
        }

        /// <summary>Läuft der Dienst schon? (Win32_Service.State, lesend; startet nichts.)</summary>
        internal static bool ServiceRunning(string name)
        {
            var options = new ConnectionOptions();
            options.Impersonation = ImpersonationLevel.Impersonate;
            var scope = new ManagementScope(@"\\.\root\cimv2", options);
            using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT State FROM Win32_Service WHERE Name='" + name + "'")))
            using (ManagementObjectCollection result = searcher.Get())
                foreach (ManagementBaseObject service in result)
                    using (service)
                        if (string.Equals(service["State"] as string, "Running", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>MachineGuid für den Schlüssel der Pseudonyme (ohne Eintrag; der Eintrag kommt aus dem Plan).</summary>
        private static string MachineGuid(IRegistry registry)
        {
            IRegistryKey key;
            if (registry.Open(@"SOFTWARE\Microsoft\Cryptography", out key) != 0) return null;
            using (key)
            {
                var values = new System.Collections.Generic.List<RegValue>();
                if (key.Values(values) != 0) return null;
                foreach (RegValue v in values)
                    if (string.Equals(v.Name, "MachineGuid", StringComparison.OrdinalIgnoreCase) && v.Type == RegTypes.Sz)
                        return RegistryCollector.DecodeString(v.Data, 64);
            }
            return null;
        }
    }
}
