// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Riegel.Collector.Collection
{
    /// <summary>
    /// Registry nach dem Plan (spec 7.3): rekursive Bereiche bis zur Tiefe des Plans, einzelne Schlüssel und eine
    /// Ebene Unterschlüssel (children), höchstens <see cref="Plan.MaxValuesTotal"/> Werte. Je Planeintrag ein
    /// <see cref="CoverageInfo"/>: vollständig, verweigerte Unterschlüssel, Abbruchstelle. Ein fehlender Planschlüssel
    /// ist ein Eintrag KeyMissing.
    /// </summary>
    internal sealed class RegistryCollector
    {
        private readonly IRegistry _registry;
        private readonly Plan _plan;
        private readonly Pseudonymizer _pseudonymizer;
        private readonly SnapshotData _target;
        private readonly Dictionary<string, bool> _seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private int _values;
        private bool _limitReached;

        public RegistryCollector(IRegistry registry, Plan plan, Pseudonymizer pseudonymizer, SnapshotData target)
        {
            _registry = registry;
            _plan = plan;
            _pseudonymizer = pseudonymizer;
            _target = target;
        }

        /// <summary>Zahl der gelesenen Werte (für Tests und die Zusammenfassung).</summary>
        public int ValuesRead { get { return _values; } }

        public void CollectAll()
        {
            foreach (var entry in _plan.Registry) Collect(entry);
        }

        public void Collect(PlanRegistry entry)
        {
            var coverage = new CoverageInfo();
            coverage.Root = entry.Path;
            coverage.Recursive = entry.Kind != "key";
            coverage.MaxDepth = entry.Kind == "recursive" ? entry.MaxDepth : entry.Kind == "children" ? 1 : 0;
            _target.Coverage.Add(coverage);

            var watch = Stopwatch.StartNew();
            if (_limitReached)
            {
                coverage.Complete = false;
                coverage.TruncatedAt = entry.Path;
                Add(CollectedItem.Fail(ItemKey(entry.Path), Statuses.NotCollected, null, "value limit reached"), watch);
                return;
            }

            IRegistryKey key;
            int rc = _registry.Open(Relative(entry.Path), out key);
            if (rc == Native.ERROR_FILE_NOT_FOUND)
            {
                // Ein fehlender Bereich ist ein Beweis „nicht gesetzt“ (vollständige Abdeckung, spec 7.3)
                Add(new CollectedItem(ItemKey(entry.Path), Statuses.KeyMissing), watch);
                return;
            }
            if (rc != 0)
            {
                coverage.Complete = false;
                if (rc == Native.ERROR_ACCESS_DENIED) coverage.DeniedSubkeys.Add(entry.Path);
                Add(OpenFailure(entry.Path, rc), watch);
                return;
            }
            using (key) Visit(key, entry.Path, 0, entry, coverage, watch);
        }

        private void Visit(IRegistryKey key, string path, int depth, PlanRegistry entry, CoverageInfo coverage, Stopwatch watch)
        {
            string itemKey = ItemKey(path);
            if (_seen.ContainsKey(itemKey)) return;
            _seen[itemKey] = true;

            var values = new List<RegValue>();
            var subkeys = new List<string>();
            int rc = key.Values(values);
            if (rc == 0) rc = key.SubKeyNames(subkeys);
            if (rc != 0)
            {
                // Verweigerte Unterschlüssel stehen in deniedSubkeys; nur ein unlesbarer Bereich selbst oder ein anderer
                // Fehler macht den Bereich unvollständig
                if (rc == Native.ERROR_ACCESS_DENIED) coverage.DeniedSubkeys.Add(path);
                if (depth == 0 || rc != Native.ERROR_ACCESS_DENIED) coverage.Complete = false;
                Add(OpenFailure(path, rc), watch);
                return;
            }
            subkeys.Sort(StringComparer.OrdinalIgnoreCase);

            // Werte filtern (Plan „values“) und gegen die Gesamtgrenze zählen
            bool truncated = false;
            var encoded = new JObject();
            bool snmp = Redaction.IsSnmpCommunityKey(path);
            int snmpIndex = 0;
            foreach (var v in values)
            {
                if (entry.Values != null && !Contains(entry.Values, v.Name)) continue;
                if (_values >= _plan.MaxValuesTotal) { truncated = true; break; }
                _values++;
                string name = snmp ? "redacted-" + (++snmpIndex).ToString(CultureInfo.InvariantCulture) : v.Name;
                encoded.Add(name, Encode(path, v));
            }

            bool snmpTraps = Redaction.IsSnmpTrapKey(path);
            var subkeyList = new List<object>();
            for (int i = 0; i < subkeys.Count; i++)
                subkeyList.Add(snmpTraps ? "redacted-" + (i + 1).ToString(CultureInfo.InvariantCulture) : subkeys[i]);

            var value = new JObject().Add("values", encoded).Add("subkeys", subkeyList);
            if (entry.Values != null) value.Add("valueFilter", new List<object>(entry.Values));
            var item = CollectedItem.Ok(itemKey, value);
            if (truncated)
            {
                item.Truncated = true;
                Stop(coverage, path);
            }
            Add(item, watch);
            if (truncated) return;

            int maxDepth = coverage.MaxDepth;
            if (depth >= maxDepth || snmpTraps) return;
            foreach (string sub in subkeys)
            {
                string childPath = path + "\\" + sub;
                if (_limitReached)
                {
                    if (coverage.TruncatedAt == null) Stop(coverage, childPath);
                    return;
                }
                var childWatch = Stopwatch.StartNew();
                IRegistryKey child;
                rc = _registry.Open(Relative(childPath), out child);
                if (rc == Native.ERROR_FILE_NOT_FOUND) continue; // inzwischen entfernt
                if (rc == Native.ERROR_ACCESS_DENIED)
                {
                    // Unterschlüssel, die nur SYSTEM lesen darf (z. B. Lsa\Audit\AuditPolicy): nur in deniedSubkeys,
                    // kein eigener Eintrag – sonst wäre jede Erhebung als Administrator „unvollständig“
                    coverage.DeniedSubkeys.Add(childPath);
                    continue;
                }
                if (rc != 0)
                {
                    coverage.Complete = false;
                    Add(OpenFailure(childPath, rc), childWatch);
                    continue;
                }
                using (child) Visit(child, childPath, depth + 1, entry, coverage, childWatch);
            }
        }

        private void Stop(CoverageInfo coverage, string firstNotRead)
        {
            _limitReached = true;
            coverage.Complete = false;
            if (coverage.TruncatedAt == null) coverage.TruncatedAt = firstNotRead;
        }

        private void Add(CollectedItem item, Stopwatch watch)
        {
            item.DurationMs = (int)watch.ElapsedMilliseconds;
            _target.Items.Add(item);
        }

        private static CollectedItem OpenFailure(string path, int rc)
        {
            return rc == Native.ERROR_ACCESS_DENIED
                ? CollectedItem.Fail(ItemKey(path), Statuses.Denied, rc, "Access denied")
                : CollectedItem.Fail(ItemKey(path), Statuses.Error, rc, "Registry error");
        }

        internal static string ItemKey(string path) { return "reg:" + path; }

        internal static string Relative(string path)
        {
            return path.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase) ? path.Substring(5) : path;
        }

        private static bool Contains(string[] names, string name)
        {
            foreach (string n in names) if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Ein Wert in der Kodierung von spec 7.3 (t, v, redacted, empty, truncated, len).</summary>
        internal JObject Encode(string keyPath, RegValue value)
        {
            var o = new JObject();
            byte[] data = value.Data;
            o.Add("t", TypeName(value.Type, data.Length));
            if (Redaction.ShouldRedact(value.Name, value.Type))
            {
                bool empty = data.Length == 0 || (IsString(value.Type) && DecodeString(data, int.MaxValue).Length == 0);
                o.Add("redacted", true).Add("empty", empty);
                return o;
            }
            string t = (string)o.Get("t");
            switch (t)
            {
                case "dword": o.Add("v", BitConverter.ToUInt32(data, 0)); return o;
                case "qword": o.Add("v", BitConverter.ToUInt64(data, 0).ToString(CultureInfo.InvariantCulture)); return o;
                case "sz":
                case "expand_sz":
                    {
                        string s = DecodeString(data, int.MaxValue);
                        if (s.Length > _plan.MaxStringChars) { s = s.Substring(0, _plan.MaxStringChars); o.Add("truncated", true); }
                        o.Add("v", Text(keyPath, value.Name, s));
                        return o;
                    }
                case "multi_sz":
                    {
                        var list = new List<object>();
                        foreach (string s in SplitMulti(data)) list.Add(Text(keyPath, value.Name, s));
                        o.Add("v", list);
                        return o;
                    }
                default:
                    if (data.Length == 0 && t == "none") return o;
                    int max = _plan.MaxBinaryBytes;
                    if (data.Length > max)
                    {
                        o.Add("v", Convert.ToBase64String(data, 0, max)).Add("truncated", true).Add("len", data.Length);
                        return o;
                    }
                    o.Add("v", Convert.ToBase64String(data));
                    return o;
            }
        }

        private string Text(string keyPath, string valueName, string s)
        {
            if (_pseudonymizer == null) return s;
            if (keyPath.EndsWith(@"\Winlogon", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(valueName, "DefaultUserName", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(valueName, "AltDefaultUserName", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(valueName, "LastUsedUsername", StringComparison.OrdinalIgnoreCase)))
                return _pseudonymizer.Account(s);
            return _pseudonymizer.Paths(s);
        }

        private static bool IsString(int type) { return type == RegTypes.Sz || type == RegTypes.ExpandSz || type == RegTypes.MultiSz; }

        /// <summary>Typname des Vertrags; DWORD/QWORD mit falscher Länge sind „unknown“ (Bytes als base64).</summary>
        internal static string TypeName(int type, int length)
        {
            switch (type)
            {
                case RegTypes.None: return "none";
                case RegTypes.Sz: return "sz";
                case RegTypes.ExpandSz: return "expand_sz";
                case RegTypes.Binary: return "binary";
                case RegTypes.Dword: return length == 4 ? "dword" : "unknown";
                case RegTypes.MultiSz: return "multi_sz";
                case RegTypes.Qword: return length == 8 ? "qword" : "unknown";
                default: return "unknown";
            }
        }

        /// <summary>UTF-16 bis zur ersten NUL (Registry-Zeichenketten sind nicht immer terminiert).</summary>
        internal static string DecodeString(byte[] data, int maxChars)
        {
            int chars = data.Length / 2;
            string s = Encoding.Unicode.GetString(data, 0, chars * 2);
            int nul = s.IndexOf('\0');
            if (nul >= 0) s = s.Substring(0, nul);
            return s.Length > maxChars ? s.Substring(0, maxChars) : s;
        }

        /// <summary>REG_MULTI_SZ: Teile an NUL, ohne die leeren Teile am Ende.</summary>
        internal static List<string> SplitMulti(byte[] data)
        {
            int chars = data.Length / 2;
            string s = Encoding.Unicode.GetString(data, 0, chars * 2);
            var parts = new List<string>(s.Split('\0'));
            while (parts.Count > 0 && parts[parts.Count - 1].Length == 0) parts.RemoveAt(parts.Count - 1);
            return parts;
        }
    }
}
