// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Riegel.Collector.Collection
{
    /// <summary>
    /// WMI-Einträge des Plans (spec 7.3): WQL und Eigenschaften aus dem Plan, Methoden nur aus der Erlaubnisliste
    /// (<see cref="WmiMethods"/>). Fehlende Namespaces und Klassen, fehlende Rechte und Fehler werden Status.
    /// </summary>
    internal sealed class WmiCollector
    {
        private readonly Plan _plan;
        private readonly bool _isAdmin;
        private readonly Pseudonymizer _pseudonymizer;

        public WmiCollector(Plan plan, bool isAdmin, Pseudonymizer pseudonymizer)
        {
            _plan = plan;
            _isAdmin = isAdmin;
            _pseudonymizer = pseudonymizer;
        }

        public CollectedItem Collect(PlanWmi entry)
        {
            var watch = Stopwatch.StartNew();
            CollectedItem item;
            try
            {
                PlanWmiMethod staticMethod = null;
                foreach (var m in entry.Methods) if (m.Kind == "static") staticMethod = m;
                item = staticMethod != null ? CollectStatic(entry, staticMethod) : CollectRows(entry);
            }
            catch (Exception e)
            {
                item = Failure(entry.Key, e);
            }
            item.DurationMs = (int)watch.ElapsedMilliseconds;
            return item;
        }

        /// <summary>Status und Code zu einer Ausnahme aus WMI/COM.</summary>
        internal static CollectedItem Failure(string key, Exception e)
        {
            int code;
            var me = e as ManagementException;
            if (me != null) code = (int)me.ErrorCode;
            else if (e is UnauthorizedAccessException) code = unchecked((int)0x80070005);
            else if (e is COMException) code = ((COMException)e).ErrorCode;
            else return CollectedItem.Fail(key, Statuses.Error, null, e.GetType().Name);
            return CollectedItem.Fail(key, StatusFor(code), code, TextFor(code));
        }

        /// <summary>Status zu einem WMI-/COM-Fehlercode.</summary>
        internal static string StatusFor(int code)
        {
            switch (unchecked((uint)code))
            {
                case 0x8004100E: return Statuses.NamespaceMissing; // WBEM_E_INVALID_NAMESPACE
                case 0x80041010: return Statuses.NamespaceMissing; // WBEM_E_INVALID_CLASS
                case 0x80041003: return Statuses.Denied;           // WBEM_E_ACCESS_DENIED
                case 0x80070005: return Statuses.Denied;           // E_ACCESSDENIED
                default: return Statuses.Error;
            }
        }

        private static string TextFor(int code)
        {
            switch (unchecked((uint)code))
            {
                case 0x8004100E: return "Invalid namespace";
                case 0x80041010: return "Invalid class";
                case 0x80041003:
                case 0x80070005: return "Access denied";
                default: return "WMI error";
            }
        }

        private static ManagementScope Scope(string ns)
        {
            var options = new ConnectionOptions();
            options.Impersonation = ImpersonationLevel.Impersonate;
            // BitLocker (MicrosoftVolumeEncryption) verlangt Paketverschlüsselung; für die übrigen schadet sie nicht
            options.Authentication = AuthenticationLevel.PacketPrivacy;
            var scope = new ManagementScope(@"\\.\" + ns, options);
            scope.Connect();
            return scope;
        }

        private static List<ManagementBaseObject> Query(ManagementScope scope, string wql)
        {
            var options = new System.Management.EnumerationOptions();
            options.ReturnImmediately = true;
            options.Rewindable = false;
            var rows = new List<ManagementBaseObject>();
            using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql), options))
            using (var result = searcher.Get())
                foreach (ManagementBaseObject row in result) rows.Add(row);
            return rows;
        }

        private CollectedItem CollectRows(PlanWmi entry)
        {
            var scope = Scope(entry.Namespace);
            bool instanceMethods = false;
            foreach (var m in entry.Methods) if (m.Kind == "perRow" && m.ViaClass == null) instanceMethods = true;
            string where = entry.Where != null ? " WHERE " + entry.Where : "";
            string projected = "SELECT " + string.Join(",", entry.Properties) + " FROM " + entry.Class + where;
            string all = "SELECT * FROM " + entry.Class + where;
            List<ManagementBaseObject> rows;
            if (instanceMethods) rows = Query(scope, all); // Instanzmethoden brauchen den vollständigen Pfad
            else
            {
                try { rows = Query(scope, projected); }
                catch (ManagementException e)
                {
                    // Ältere Windows kennen nicht jede Eigenschaft des Plans: dann alle lesen und nur die vorhandenen schreiben
                    if (e.ErrorCode != ManagementStatus.InvalidQuery) throw;
                    rows = Query(scope, all);
                }
            }

            var list = new List<object>();
            try
            {
                foreach (var row in rows)
                {
                    var r = new JObject();
                    foreach (string property in entry.Properties)
                    {
                        PropertyData data = Property(row, property);
                        if (data == null) continue; // gibt es auf diesem System nicht
                        object value = Convert(data.Value, data.Type, data.IsArray);
                        if (Contains(entry.AdminOnlyProperties, property) && (!_isAdmin || IsNotAvailableText(value)))
                        {
                            // Ohne Adminrechte liefert Defender hier einen Hinweistext statt der Liste: nicht als Wert schreiben
                            r.Add(property + "@denied", true);
                            continue;
                        }
                        if (string.Equals(entry.Class, "Win32_QuickFixEngineering", StringComparison.OrdinalIgnoreCase)
                            && property == "InstalledOn")
                        {
                            AddInstalledOn(r, value as string);
                            continue;
                        }
                        if (value is string && ((string)value).Length > 0 && data.Type == CimType.DateTime) { AddDate(r, property, (string)value); continue; }
                        r.Add(property, Privacy(entry.Class, property, value));
                    }
                    foreach (var method in entry.Methods)
                        if (method.Kind == "perRow") r.Add(method.Name, RowMethod(scope, entry, row, method));
                    list.Add(r);
                }
            }
            finally
            {
                foreach (var row in rows) row.Dispose();
            }
            return CollectedItem.Ok(entry.Key, new JObject().Add("rows", list));
        }

        private CollectedItem CollectStatic(PlanWmi entry, PlanWmiMethod method)
        {
            if (!WmiMethods.IsAllowed(method.Name, _plan))
                return CollectedItem.Fail(entry.Key, Statuses.NotCollected, null, "method not on the allow-list");
            var scope = Scope(entry.Namespace);
            using (var cls = new ManagementClass(scope, new ManagementPath(entry.Class), null))
            using (ManagementBaseObject result = cls.InvokeMethod(method.Name, null, null))
            {
                int returnValue = ReturnValue(result);
                if (returnValue != 0) return CollectedItem.Fail(entry.Key, Statuses.Error, returnValue, "method returned an error");
                // GetConfiguration liefert die Werte in einem eingebetteten Objekt („Output“)
                ManagementBaseObject source = result;
                foreach (PropertyData p in result.Properties)
                    if (p.Type == CimType.Object && !p.IsArray && p.Value is ManagementBaseObject) { source = (ManagementBaseObject)p.Value; break; }
                var output = new JObject();
                foreach (string name in method.OutProperties)
                {
                    PropertyData data = Property(source, name);
                    if (data != null) output.Add(name, Convert(data.Value, data.Type, data.IsArray));
                }
                return CollectedItem.Ok(entry.Key, new JObject().Add("out", output));
            }
        }

        private object RowMethod(ManagementScope scope, PlanWmi entry, ManagementBaseObject row, PlanWmiMethod method)
        {
            var result = new JObject();
            if (!WmiMethods.IsAllowed(method.Name, _plan)) return result.Add("error", "method not on the allow-list");
            try
            {
                if (method.ViaClass != null)
                {
                    // Methode auf einer anderen Klasse, deren Instanz die Schlüsseleigenschaft der Zeile bestimmt
                    PropertyData keyData = Property(row, method.KeyProperty);
                    string keyValue = keyData != null ? keyData.Value as string : null;
                    if (keyValue == null) return result.Add("error", "key property missing");
                    string path = method.ViaClass + "." + method.KeyProperty + "=\"" + keyValue.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                    using (var target = new ManagementObject(scope, new ManagementPath(path), null))
                    using (ManagementBaseObject output = target.InvokeMethod(method.Name, Inputs(target, method.Name), null))
                        return MethodOutput(output, method);
                }
                var instance = row as ManagementObject;
                if (instance == null) return result.Add("error", "no instance");
                using (ManagementBaseObject output = instance.InvokeMethod(method.Name, Inputs(instance, method.Name), null))
                {
                    JObject o = MethodOutput(output, method);
                    if (method.Name == "GetKeyProtectors" && ReturnValue(output) == 0) o.Add("KeyProtectorTypes", ProtectorTypes(instance, o.Get("VolumeKeyProtectorID") as IList));
                    return o;
                }
            }
            catch (Exception e)
            {
                CollectedItem f = Failure(entry.Key, e);
                return result.Add("ReturnValue", null).Add("ErrorCode", f.ErrorCode);
            }
        }

        /// <summary>
        /// Leere Eingabeparameter der Methode: der BitLocker-Anbieter verträgt bei Methoden mit (optionalen)
        /// Eingaben keinen Null-Zeiger und bricht sonst mit einem RPC-Fehler ab. Methoden ohne Eingaben: null.
        /// </summary>
        private static ManagementBaseObject Inputs(ManagementObject target, string method)
        {
            try { return target.GetMethodParameters(method); }
            catch (ManagementException) { return null; }
        }

        /// <summary>
        /// Typ je Schlüsselschutz: GetKeyProtectors mit dem Filter KeyProtectorType 1–10 (dieselbe erlaubte Methode),
        /// in der Reihenfolge der IDs; nicht zugeordnete IDs erhalten 0.
        /// </summary>
        private static List<object> ProtectorTypes(ManagementObject volume, IList ids)
        {
            var types = new List<object>();
            if (ids == null) return types;
            var byId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int type = 1; type <= 10; type++)
            {
                using (ManagementBaseObject input = volume.GetMethodParameters("GetKeyProtectors"))
                {
                    input["KeyProtectorType"] = (uint)type;
                    using (ManagementBaseObject output = volume.InvokeMethod("GetKeyProtectors", input, null))
                    {
                        if (ReturnValue(output) != 0) continue;
                        var found = output["VolumeKeyProtectorID"] as string[];
                        if (found != null) foreach (string id in found) byId[id] = type;
                    }
                }
            }
            foreach (object id in ids)
            {
                int type;
                types.Add(id is string && byId.TryGetValue((string)id, out type) ? type : 0);
            }
            return types;
        }

        private JObject MethodOutput(ManagementBaseObject output, PlanWmiMethod method)
        {
            var o = new JObject().Add("ReturnValue", ReturnValue(output));
            foreach (string name in method.OutProperties)
            {
                PropertyData data = Property(output, name);
                if (data == null) continue;
                if (name == "Descriptor") { o.Add("Aces", Aces(data.Value as ManagementBaseObject)); continue; }
                o.Add(name, Convert(data.Value, data.Type, data.IsArray));
            }
            return o;
        }

        /// <summary>DACL einer Freigabe, verkürzt auf SID, Zugriffsmaske und ACE-Typ; null = keine DACL.</summary>
        private static object Aces(ManagementBaseObject descriptor)
        {
            if (descriptor == null) return null;
            var dacl = descriptor["DACL"] as ManagementBaseObject[];
            if (dacl == null) return null;
            var list = new List<object>();
            foreach (ManagementBaseObject ace in dacl)
            {
                string sid = null;
                var trustee = ace["Trustee"] as ManagementBaseObject;
                if (trustee != null)
                {
                    sid = trustee["SIDString"] as string;
                    var binary = trustee["SID"] as byte[];
                    if (string.IsNullOrEmpty(sid) && binary != null) sid = new SecurityIdentifier(binary, 0).Value;
                }
                list.Add(new JObject().Add("Sid", sid).Add("AccessMask", Convert(ace["AccessMask"], CimType.UInt32, false))
                    .Add("AceType", Convert(ace["AceType"], CimType.UInt32, false)));
            }
            return list;
        }

        private static int ReturnValue(ManagementBaseObject output)
        {
            if (output == null) return -1;
            PropertyData data = Property(output, "ReturnValue");
            if (data == null || data.Value == null) return 0;
            return System.Convert.ToInt32(data.Value, CultureInfo.InvariantCulture);
        }

        private static PropertyData Property(ManagementBaseObject row, string name)
        {
            foreach (PropertyData p in row.Properties)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        private object Privacy(string cls, string property, object value)
        {
            if (_pseudonymizer == null) return value;
            if (cls == "Win32_UserAccount" && property == "Name") return _pseudonymizer.Name(value as string);
            if (cls == "Win32_Service" && property == "StartName") return LocalAccount(value as string);
            var s = value as string;
            if (s != null) return _pseudonymizer.Paths(s);
            var list = value as IList;
            if (list != null)
                for (int i = 0; i < list.Count; i++) if (list[i] is string) list[i] = _pseudonymizer.Paths((string)list[i]);
            return value;
        }

        /// <summary>Dienstkonto „.\name“ oder „HOST\name“ ist ein lokales Konto: Pseudonym; andere bleiben.</summary>
        private string LocalAccount(string account)
        {
            if (string.IsNullOrEmpty(account)) return account;
            if (account.StartsWith(@".\", StringComparison.Ordinal)
                || account.StartsWith(Environment.MachineName + "\\", StringComparison.OrdinalIgnoreCase))
                return _pseudonymizer.Account(account);
            return account;
        }

        private static bool IsNotAvailableText(object value)
        {
            var list = value as IList;
            if (list != null && list.Count > 0 && list[0] is string) return ((string)list[0]).StartsWith("N/A", StringComparison.Ordinal);
            var s = value as string;
            return s != null && s.StartsWith("N/A", StringComparison.Ordinal);
        }

        private static bool Contains(string[] names, string name)
        {
            foreach (string n in names) if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void AddDate(JObject row, string property, string dmtf)
        {
            DateTime utc;
            if (TryParseDmtf(dmtf, out utc)) row.Add(property, SnapshotData.Iso(utc));
            else row.Add(property, null).Add(property + "@raw", dmtf);
        }

        /// <summary>
        /// Win32_QuickFixEngineering.InstalledOn ist eine Zeichenkette („10/16/2026“, auf alten Systemen ein FILETIME in
        /// Hex): als ISO-Datum schreiben, sonst roh unter „InstalledOn@raw“.
        /// </summary>
        internal static void AddInstalledOn(JObject row, string text)
        {
            DateTime date;
            if (TryParseInstalledOn(text, out date)) row.Add("InstalledOn", SnapshotData.Iso(date));
            else if (string.IsNullOrEmpty(text)) row.Add("InstalledOn", null);
            else row.Add("InstalledOn", null).Add("InstalledOn@raw", text);
        }

        internal static bool TryParseInstalledOn(string text, out DateTime utc)
        {
            utc = DateTime.MinValue;
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            if (DateTime.TryParseExact(text, new[] { "M/d/yyyy", "yyyyMMdd" }, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc))
                return true;
            long fileTime;
            if (text.Length == 16 && long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out fileTime) && fileTime > 0)
            {
                try { utc = DateTime.FromFileTimeUtc(fileTime); return true; }
                catch (ArgumentOutOfRangeException) { return false; }
            }
            return false;
        }

        /// <summary>CIM-Datum „yyyymmddHHMMSS.mmmmmmsUUU“ (UUU = Minuten zu UTC) als UTC; Intervalle und Platzhalter → false.</summary>
        internal static bool TryParseDmtf(string dmtf, out DateTime utc)
        {
            utc = DateTime.MinValue;
            if (dmtf == null || dmtf.Length != 25 || dmtf[14] != '.' || (dmtf[21] != '+' && dmtf[21] != '-')) return false;
            for (int i = 0; i < 25; i++)
                if (i != 14 && i != 21 && (dmtf[i] < '0' || dmtf[i] > '9')) return false;
            try
            {
                var local = new DateTime(Int(dmtf, 0, 4), Int(dmtf, 4, 2), Int(dmtf, 6, 2), Int(dmtf, 8, 2), Int(dmtf, 10, 2), Int(dmtf, 12, 2), DateTimeKind.Utc)
                    .AddTicks(Int(dmtf, 15, 6) * 10L);
                int offset = Int(dmtf, 22, 3) * (dmtf[21] == '-' ? -1 : 1);
                utc = local.AddMinutes(-offset);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static int Int(string s, int start, int length)
        {
            return int.Parse(s.Substring(start, length), NumberStyles.None, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// CIM-Wert nach spec 7.3: Ganzzahlen als Zahl (uint64 als Zeichenkette), Wahrheitswerte, Zeichenketten, Arrays;
        /// eingebettete Objekte mit ihren Eigenschaften. Datumswerte behandelt der Aufrufer (Rohtext → ISO).
        /// </summary>
        internal static object Convert(object value, CimType type, bool isArray)
        {
            if (value == null) return null;
            if (isArray || (value is Array && !(value is string)))
            {
                var list = new List<object>();
                foreach (object element in (IEnumerable)value) list.Add(Convert(element, type, false));
                return list;
            }
            switch (type)
            {
                case CimType.Boolean: return (bool)value;
                case CimType.UInt64: return System.Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                case CimType.UInt32: return System.Convert.ToUInt32(value, CultureInfo.InvariantCulture);
                case CimType.SInt8:
                case CimType.UInt8:
                case CimType.SInt16:
                case CimType.UInt16:
                case CimType.SInt32:
                case CimType.SInt64: return System.Convert.ToInt64(value, CultureInfo.InvariantCulture);
                case CimType.Real32:
                case CimType.Real64: return System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                case CimType.Char16: return System.Convert.ToChar(value, CultureInfo.InvariantCulture).ToString();
                case CimType.Object:
                    {
                        var embedded = value as ManagementBaseObject;
                        if (embedded == null) return System.Convert.ToString(value, CultureInfo.InvariantCulture);
                        var o = new JObject();
                        foreach (PropertyData p in embedded.Properties) o.Add(p.Name, Convert(p.Value, p.Type, p.IsArray));
                        return o;
                    }
                default:
                    if (value is bool) return value;
                    return System.Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// Die einzigen WMI-Methoden, die ein Sammler aufrufen darf (spec 7.4). Eine Methode muss hier UND in
    /// <c>allowedWmiMethods</c> des Plans stehen – eine Planänderung allein öffnet keine neue Methode.
    /// </summary>
    internal static class WmiMethods
    {
        public static readonly string[] AllowList =
        {
            "GetConfiguration", "GetConversionStatus", "GetEncryptionMethod", "GetKeyProtectors", "GetProtectionStatus", "GetSecurityDescriptor",
        };

        public static bool IsAllowed(string name, Plan plan)
        {
            bool code = false, inPlan = false;
            foreach (string n in AllowList) if (string.Equals(n, name, StringComparison.Ordinal)) code = true;
            if (plan != null) foreach (string n in plan.AllowedWmiMethods) if (string.Equals(n, name, StringComparison.Ordinal)) inPlan = true;
            return code && inPlan;
        }
    }
}
