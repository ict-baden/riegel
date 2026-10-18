// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Riegel.Collector.Collection
{
    /// <summary>
    /// Abfragen über COM (spec 7.3): Firewall (HNetCfg.FwPolicy2, spät gebunden), Windows-Update-Verlauf
    /// (Microsoft.Update.Session, nur wenn wuauserv schon läuft) und Windows-Sicherheitscenter (IWSCProductList).
    /// Nur lesende Eigenschaften und Methoden.
    /// </summary>
    internal static class ComCollector
    {
        // ------------------------------------------------------------------ Firewall

        /// <summary>
        /// api:firewall – Profile (an/aus, Standard ein-/ausgehend) und, außer mit --minimal, die Regeln.
        /// Profile: 1 Domäne, 2 privat, 4 öffentlich (NET_FW_PROFILE_TYPE2).
        /// </summary>
        public static CollectedItem Firewall(string key, bool rules, Pseudonymizer pseudonymizer)
        {
            Type type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2", false);
            if (type == null) return CollectedItem.Fail(key, Statuses.NotApplicable, null, "HNetCfg.FwPolicy2 not available");
            object policy = Activator.CreateInstance(type);
            try
            {
                var profiles = new JObject();
                profiles.Add("domain", Profile(policy, 1));
                profiles.Add("private", Profile(policy, 2));
                profiles.Add("public", Profile(policy, 4));
                var value = new JObject()
                    .Add("currentProfiles", Number(Get(policy, "CurrentProfileTypes")))
                    .Add("profiles", profiles)
                    .Add("rulesCollected", rules)
                    .Add("rules", rules ? Rules(policy, pseudonymizer) : new List<object>());
                return CollectedItem.Ok(key, value);
            }
            finally { Release(policy); }
        }

        private static JObject Profile(object policy, int profile)
        {
            return new JObject()
                .Add("enabled", Get(policy, "FirewallEnabled", profile))
                .Add("defaultInbound", Action(Get(policy, "DefaultInboundAction", profile)))
                .Add("defaultOutbound", Action(Get(policy, "DefaultOutboundAction", profile)));
        }

        /// <summary>NET_FW_ACTION: 0 block, 1 allow.</summary>
        internal static string Action(object value)
        {
            if (value == null) return null;
            int action = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return action == 0 ? "block" : action == 1 ? "allow" : action.ToString(CultureInfo.InvariantCulture);
        }

        private static List<object> Rules(object policy, Pseudonymizer pseudonymizer)
        {
            var list = new List<object>();
            object rules = Get(policy, "Rules");
            try
            {
                foreach (object rule in Enumerate(rules))
                {
                    try
                    {
                        int direction = Convert.ToInt32(Get(rule, "Direction"), CultureInfo.InvariantCulture);
                        string application = Get(rule, "ApplicationName") as string;
                        if (pseudonymizer != null) application = pseudonymizer.Paths(application);
                        list.Add(new JObject()
                            .Add("name", Get(rule, "Name") as string)
                            .Add("group", Get(rule, "Grouping") as string)
                            .Add("enabled", Get(rule, "Enabled"))
                            .Add("direction", direction == 1 ? "in" : direction == 2 ? "out" : direction.ToString(CultureInfo.InvariantCulture))
                            .Add("action", Action(Get(rule, "Action")))
                            .Add("protocol", Number(Get(rule, "Protocol")))
                            .Add("localPorts", Get(rule, "LocalPorts") as string)
                            .Add("remoteAddresses", Get(rule, "RemoteAddresses") as string)
                            .Add("profiles", Number(Get(rule, "Profiles")))
                            .Add("application", application)
                            .Add("service", Get(rule, "ServiceName") as string));
                    }
                    finally { Release(rule); }
                }
            }
            finally { Release(rules); }
            return list;
        }

        // ------------------------------------------------------------------ Windows Update

        /// <summary>
        /// api:windows-update-history – die neuesten Einträge über Microsoft.Update.Session.QueryHistory. Nur aufrufen,
        /// wenn wuauserv bereits läuft (der Aufrufer prüft das); startet den Dienst also nie.
        /// </summary>
        public static CollectedItem UpdateHistory(string key, int maxEntries)
        {
            Type type = Type.GetTypeFromProgID("Microsoft.Update.Session", false);
            if (type == null) return CollectedItem.Fail(key, Statuses.NotApplicable, null, "Microsoft.Update.Session not available");
            object session = Activator.CreateInstance(type);
            object searcher = null, history = null;
            try
            {
                searcher = Call(session, "CreateUpdateSearcher");
                int total = Convert.ToInt32(Call(searcher, "GetTotalHistoryCount"), CultureInfo.InvariantCulture);
                var entries = new List<object>();
                if (total > 0)
                {
                    history = Call(searcher, "QueryHistory", 0, Math.Min(total, maxEntries));
                    int count = Convert.ToInt32(Get(history, "Count"), CultureInfo.InvariantCulture);
                    var dated = new List<KeyValuePair<DateTime, JObject>>();
                    for (int i = 0; i < count; i++)
                    {
                        object entry = Get(history, "Item", i);
                        try
                        {
                            DateTime date = Convert.ToDateTime(Get(entry, "Date"), CultureInfo.InvariantCulture);
                            var o = new JObject()
                                .Add("dateUtc", SnapshotData.Iso(DateTime.SpecifyKind(date, DateTimeKind.Utc)))
                                .Add("resultCode", Number(Get(entry, "ResultCode")))
                                .Add("operation", Number(Get(entry, "Operation")))
                                .Add("title", Get(entry, "Title") as string);
                            dated.Add(new KeyValuePair<DateTime, JObject>(date, o));
                        }
                        finally { Release(entry); }
                    }
                    // Neueste zuerst
                    dated.Sort(delegate(KeyValuePair<DateTime, JObject> a, KeyValuePair<DateTime, JObject> b) { return b.Key.CompareTo(a.Key); });
                    foreach (var pair in dated) entries.Add(pair.Value);
                }
                return CollectedItem.Ok(key, new JObject().Add("serviceWasRunning", true).Add("entries", entries));
            }
            finally
            {
                Release(history);
                Release(searcher);
                Release(session);
            }
        }

        // ------------------------------------------------------------------ Windows-Sicherheitscenter

        /// <summary>
        /// api:wsc-av – Virenschutzprodukte über IWSCProductList (WSC-API, Windows 8 und neuer, nur Clients). Läuft in
        /// einem eigenen STA-Thread. Nicht verfügbar (Server, Windows 7) → NotCollected.
        /// </summary>
        public static CollectedItem SecurityCenter(string key)
        {
            CollectedItem result = null;
            var thread = new Thread(delegate() { result = SecurityCenterOnThread(key); });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            if (!thread.Join(30000)) return CollectedItem.Fail(key, Statuses.Error, null, "timeout");
            return result ?? CollectedItem.Fail(key, Statuses.Error, null, "no result");
        }

        private static CollectedItem SecurityCenterOnThread(string key)
        {
            object instance = null;
            try
            {
                Type type = Type.GetTypeFromCLSID(new Guid(Wsc.ClsidProductList), false);
                instance = Activator.CreateInstance(type);
                var list = (Wsc.IWSCProductList)instance;
                list.Initialize(Wsc.ProviderAntivirus);
                var products = new List<object>();
                int count = list.Count;
                for (uint i = 0; i < (uint)count; i++)
                {
                    Wsc.IWscProduct product = list[i];
                    try
                    {
                        products.Add(new JObject()
                            .Add("name", product.ProductName)
                            .Add("state", Wsc.State(product.ProductState))
                            .Add("signature", product.SignatureStatus == 1 ? "upToDate" : "outOfDate"));
                    }
                    finally { Release(product); }
                }
                return CollectedItem.Ok(key, products);
            }
            catch (COMException e)
            {
                // REGDB_E_CLASSNOTREG: die Klasse gibt es hier nicht (Server, Windows 7)
                if (e.ErrorCode == unchecked((int)0x80040154)) return CollectedItem.Fail(key, Statuses.NotCollected, e.ErrorCode, "WSC API not available");
                return CollectedItem.Fail(key, Statuses.Error, e.ErrorCode, "WSC API error");
            }
            catch (InvalidCastException)
            {
                return CollectedItem.Fail(key, Statuses.NotCollected, null, "WSC API interface not available");
            }
            catch (Exception e)
            {
                return CollectedItem.Fail(key, Statuses.Error, null, e.GetType().Name);
            }
            finally { Release(instance); }
        }

        // ------------------------------------------------------------------ Hilfen für spät gebundenes COM

        internal static object Get(object target, string name, params object[] args)
        {
            return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, args, CultureInfo.InvariantCulture);
        }

        private static object Call(object target, string name, params object[] args)
        {
            return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args, CultureInfo.InvariantCulture);
        }

        /// <summary>Elemente einer COM-Sammlung (IEnumerable über _NewEnum; sonst DISPID_NEWENUM direkt).</summary>
        private static IEnumerable Enumerate(object collection)
        {
            var enumerable = collection as IEnumerable;
            if (enumerable != null) return enumerable;
            var list = new List<object>();
            var enumerator = collection.GetType().InvokeMember("[DISPID=-4]", BindingFlags.InvokeMethod | BindingFlags.GetProperty, null, collection, null, CultureInfo.InvariantCulture) as IEnumerator;
            if (enumerator != null) while (enumerator.MoveNext()) list.Add(enumerator.Current);
            return list;
        }

        private static object Number(object value)
        {
            if (value == null) return null;
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        internal static void Release(object com)
        {
            if (com != null && Marshal.IsComObject(com))
            {
                try { Marshal.ReleaseComObject(com); }
                catch (ArgumentException) { }
            }
        }
    }

    /// <summary>
    /// Windows-Sicherheitscenter-API (iwscapi.h). Verifiziert am 2026-10-18: Microsoft Learn
    /// https://learn.microsoft.com/en-us/windows/win32/api/iwscapi/nn-iwscapi-iwscproductlist und …/nn-iwscapi-iwscproduct
    /// (Methoden, mindestens Windows 8, kein Server) sowie iwscapi.h und wscapi.h des Windows SDK 10.0.26100.0
    /// (IIDs, CLSID, Reihenfolge der Methoden, Enum-Werte).
    /// </summary>
    internal static class Wsc
    {
        public const string ClsidProductList = "17072F7B-9ABE-4A74-A261-1EB76B55107A";

        /// <summary>WSC_SECURITY_PROVIDER_ANTIVIRUS (wscapi.h).</summary>
        public const uint ProviderAntivirus = 0x4;

        /// <summary>WSC_SECURITY_PRODUCT_STATE: 0 an, 1 aus, 2 pausiert, 3 abgelaufen.</summary>
        public static string State(int state)
        {
            switch (state)
            {
                case 0: return "on";
                case 1: return "off";
                case 2: return "snoozed";
                case 3: return "expired";
                default: return state.ToString(CultureInfo.InvariantCulture);
            }
        }

        [ComImport, Guid("722A338C-6E8E-4E72-AC27-1417FB0C81C2"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        public interface IWSCProductList
        {
            void Initialize(uint provider);

            int Count { get; }

            IWscProduct this[uint index] { get; }
        }

        [ComImport, Guid("8C38232E-3A45-4A27-92B0-1A16A975F669"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        public interface IWscProduct
        {
            string ProductName { [return: MarshalAs(UnmanagedType.BStr)] get; }

            int ProductState { get; }

            int SignatureStatus { get; }

            string RemediationPath { [return: MarshalAs(UnmanagedType.BStr)] get; }

            string ProductStateTimestamp { [return: MarshalAs(UnmanagedType.BStr)] get; }
        }
    }
}
