// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Riegel.Collector.Collection
{
    /// <summary>Status eines Eintrags, geschrieben wie im Vertrag (riegel.snapshot/1, camelCase).</summary>
    internal static class Statuses
    {
        public const string Ok = "ok";
        public const string ValueMissing = "valueMissing";
        public const string KeyMissing = "keyMissing";
        public const string Denied = "denied";
        public const string NotApplicable = "notApplicable";
        public const string NamespaceMissing = "namespaceMissing";
        public const string NotCollected = "notCollected";
        public const string Error = "error";
    }

    /// <summary>Ein erfasster Eintrag: Schlüssel, Status, Wert (JSON-Baum), Fehlercode, kurzer Text, Dauer.</summary>
    internal sealed class CollectedItem
    {
        public string Key;
        public string Status;
        public object Value;
        public int? ErrorCode;
        public string Error;
        public int DurationMs;
        public bool Truncated;

        public CollectedItem(string key, string status)
        {
            Key = key;
            Status = status;
        }

        public static CollectedItem Ok(string key, object value)
        {
            var item = new CollectedItem(key, Statuses.Ok);
            item.Value = value;
            return item;
        }

        public static CollectedItem Fail(string key, string status, int? code, string error)
        {
            var item = new CollectedItem(key, status);
            item.ErrorCode = code;
            item.Error = error;
            return item;
        }
    }

    /// <summary>Erfasster Registry-Bereich (spec 7.3), ein Eintrag je Registry-Eintrag des Plans.</summary>
    internal sealed class CoverageInfo
    {
        public string Root;
        public bool Recursive;
        public int MaxDepth;
        public bool Complete = true;
        public readonly List<string> DeniedSubkeys = new List<string>();
        public string TruncatedAt;
    }

    /// <summary>Kopfangaben des Rechners (MachineInfo im Vertrag); null = unbekannt.</summary>
    internal sealed class MachineHeader
    {
        public string HostName;
        public string DnsDomain;
        public int? DomainRole;
        public int? ProductType;
        public int? Build;
        public int? Ubr;
        public string EditionId;
        public string InstallationType;
        public string DisplayVersion;
        public string Architecture;
        public string MachineGuid;
        public string OsLanguage;
        public DateTime? LastBootUtc;
    }

    /// <summary>Ein ganzer Export im Speicher, bevor er als riegel.snapshot/1 geschrieben wird.</summary>
    internal sealed class SnapshotData
    {
        public const string Schema = "riegel.snapshot/1";

        public string CollectorName;
        public string CollectorVersion;
        public string ExeSha256;
        public string Clr;
        public bool Is64BitProcess;
        public DateTime CollectedAtUtc;
        public bool IsAdmin;
        public string Origin;
        public bool Minimal;
        public bool Deep;
        public bool Pseudonymized;
        public MachineHeader Machine = new MachineHeader();
        public readonly List<CoverageInfo> Coverage = new List<CoverageInfo>();
        public readonly List<CollectedItem> Items = new List<CollectedItem>();

        /// <summary>Unvollständig = nicht als Administrator oder ein Eintrag verweigert/fehlerhaft (Exitcode 1, spec 7.4).</summary>
        public bool Incomplete
        {
            get
            {
                if (!IsAdmin) return true;
                foreach (var item in Items)
                    if (item.Status == Statuses.Denied || item.Status == Statuses.Error) return true;
                return false;
            }
        }

        /// <summary>Zahl der Einträge mit diesem Status.</summary>
        public int Count(string status)
        {
            int n = 0;
            foreach (var item in Items) if (item.Status == status) n++;
            return n;
        }

        /// <summary>Eintrag mit diesem Schlüssel (ohne Groß/klein), null wenn nicht erfasst.</summary>
        public CollectedItem Find(string key)
        {
            foreach (var item in Items)
                if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)) return item;
            return null;
        }

        /// <summary>Der Export als JSON-Baum in der Reihenfolge des Vertrags (null-Felder entfallen).</summary>
        public JObject ToJson()
        {
            var collector = new JObject().Add("name", CollectorName).Add("version", CollectorVersion);
            if (ExeSha256 != null) collector.Add("exeSha256", ExeSha256);
            collector.Add("clr", Clr).Add("is64BitProcess", Is64BitProcess);

            var machine = new JObject().Add("hostName", Machine.HostName);
            AddIf(machine, "dnsDomain", Machine.DnsDomain);
            AddIf(machine, "domainRole", Machine.DomainRole);
            AddIf(machine, "productType", Machine.ProductType);
            AddIf(machine, "build", Machine.Build);
            AddIf(machine, "ubr", Machine.Ubr);
            AddIf(machine, "editionId", Machine.EditionId);
            AddIf(machine, "installationType", Machine.InstallationType);
            AddIf(machine, "displayVersion", Machine.DisplayVersion);
            AddIf(machine, "architecture", Machine.Architecture);
            AddIf(machine, "machineGuid", Machine.MachineGuid);
            AddIf(machine, "osLanguage", Machine.OsLanguage);
            if (Machine.LastBootUtc.HasValue) machine.Add("lastBootUtc", Iso(Machine.LastBootUtc.Value));

            var coverage = new List<object>();
            foreach (var c in Coverage)
            {
                var o = new JObject().Add("root", c.Root).Add("recursive", c.Recursive).Add("maxDepth", c.MaxDepth)
                    .Add("complete", c.Complete).Add("deniedSubkeys", new List<object>(c.DeniedSubkeys.ToArray()));
                if (c.TruncatedAt != null) o.Add("truncatedAt", c.TruncatedAt);
                coverage.Add(o);
            }

            var items = new List<object>();
            foreach (var i in Items)
            {
                var o = new JObject().Add("key", i.Key).Add("status", i.Status);
                if (i.Value != null) o.Add("value", i.Value);
                if (i.ErrorCode.HasValue) o.Add("errorCode", i.ErrorCode.Value);
                if (i.Error != null) o.Add("error", i.Error);
                o.Add("durationMs", i.DurationMs < 0 ? 0 : i.DurationMs);
                if (i.Truncated) o.Add("truncated", true);
                items.Add(o);
            }

            return new JObject()
                .Add("schema", Schema)
                .Add("collector", collector)
                .Add("collectedAtUtc", Iso(CollectedAtUtc))
                .Add("isAdmin", IsAdmin)
                .Add("origin", Origin)
                .Add("options", new JObject().Add("minimal", Minimal).Add("deep", Deep).Add("pseudonymized", Pseudonymized))
                .Add("machine", machine)
                .Add("coverage", coverage)
                .Add("items", items);
        }

        private static void AddIf(JObject o, string name, object value)
        {
            if (value != null) o.Add(name, value);
        }

        /// <summary>ISO 8601 in UTC mit „Z“, Sekundenbruchteile nur wenn vorhanden (wie RiegelJson).</summary>
        public static string Iso(DateTime utc)
        {
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
        }
    }
}
