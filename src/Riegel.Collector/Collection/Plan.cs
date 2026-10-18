// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Riegel.Collector.Collection
{
    /// <summary>
    /// Der Erfassungsplan contracts/collection-plan.json (riegel.collection-plan/1, spec 7.3) in der Form, die der
    /// Sammler braucht. Er wird eingebettet; nichts außerhalb des Plans wird erfasst.
    /// </summary>
    internal sealed class Plan
    {
        public int MaxValuesTotal;
        public int RecursiveMaxDepth;
        public int MaxBinaryBytes;
        public int ToolTimeoutSeconds;
        public long MaxRawTextBytes;
        public int WindowsUpdateHistoryEntries;
        public int TempPathMaxChars;
        public int MaxStringChars;
        public readonly List<PlanRegistry> Registry = new List<PlanRegistry>();
        public readonly List<PlanWmi> Wmi = new List<PlanWmi>();
        public readonly List<PlanApi> Api = new List<PlanApi>();
        public readonly List<PlanEvent> Events = new List<PlanEvent>();
        public readonly List<PlanRaw> Raw = new List<PlanRaw>();
        public readonly List<string> AllowedWmiMethods = new List<string>();

        /// <summary>Name der eingebetteten Ressource (csproj: LogicalName).</summary>
        public const string ResourceName = "collection-plan.json";

        /// <summary>Liest den eingebetteten Plan aus der Assembly, die diesen Code enthält.</summary>
        public static Plan LoadEmbedded()
        {
            using (Stream stream = typeof(Plan).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null) throw new FormatException("Erfassungsplan nicht eingebettet");
                using (var reader = new StreamReader(stream, Encoding.UTF8)) return Parse(reader.ReadToEnd());
            }
        }

        /// <summary>Liest einen Plan aus JSON-Text; falsche Form → <see cref="FormatException"/>.</summary>
        public static Plan Parse(string json)
        {
            var root = JsonReader.Parse(json) as JObject;
            if (root == null || Str(root, "schema") != "riegel.collection-plan/1") throw new FormatException("kein riegel.collection-plan/1");
            var plan = new Plan();
            var limits = Obj(root, "limits");
            plan.MaxValuesTotal = (int)Num(limits, "maxValuesTotal");
            plan.RecursiveMaxDepth = (int)Num(limits, "recursiveMaxDepth");
            plan.MaxBinaryBytes = (int)Num(limits, "maxBinaryBytes");
            plan.ToolTimeoutSeconds = (int)Num(limits, "toolTimeoutSeconds");
            plan.MaxRawTextBytes = Num(limits, "maxRawTextBytes");
            plan.WindowsUpdateHistoryEntries = (int)Num(limits, "windowsUpdateHistoryEntries");
            plan.TempPathMaxChars = (int)Num(limits, "tempPathMaxChars");
            plan.MaxStringChars = (int)Num(limits, "maxStringChars");

            var registry = Obj(root, "registry");
            foreach (string section in new[] { "roots", "keys" })
                foreach (object e in Arr(registry, section))
                {
                    var o = (JObject)e;
                    var entry = new PlanRegistry();
                    entry.Path = Str(o, "path");
                    entry.Kind = Str(o, "kind");
                    entry.MaxDepth = (int)Num(o, "maxDepth");
                    entry.Values = StrArray(o, "values");
                    entry.NeedsAdmin = Bool(o, "needsAdmin");
                    entry.Mode = Str(o, "mode");
                    if (entry.Kind != "recursive" && entry.Kind != "key" && entry.Kind != "children")
                        throw new FormatException("unbekannte Art " + entry.Kind);
                    if (!entry.Path.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase)) throw new FormatException("nur HKLM: " + entry.Path);
                    plan.Registry.Add(entry);
                }

            foreach (object e in Arr(root, "wmi"))
            {
                var o = (JObject)e;
                var w = new PlanWmi();
                w.Key = Str(o, "key");
                w.Namespace = Str(o, "namespace");
                w.Class = Str(o, "class");
                w.Where = Str(o, "where");
                w.Properties = StrArray(o, "properties") ?? new string[0];
                w.AdminOnlyProperties = StrArray(o, "adminOnlyProperties") ?? new string[0];
                w.NeedsAdmin = Bool(o, "needsAdmin");
                w.Mode = Str(o, "mode");
                foreach (object m in Arr(o, "methods"))
                {
                    var mo = (JObject)m;
                    var method = new PlanWmiMethod();
                    method.Name = Str(mo, "name");
                    method.Kind = Str(mo, "kind");
                    method.ViaClass = Str(mo, "viaClass");
                    method.KeyProperty = Str(mo, "keyProperty");
                    method.OutProperties = StrArray(mo, "outProperties") ?? new string[0];
                    w.Methods.Add(method);
                }
                plan.Wmi.Add(w);
            }

            foreach (object e in Arr(root, "api"))
            {
                var o = (JObject)e;
                var a = new PlanApi();
                a.Key = Str(o, "key");
                a.Api = Str(o, "api");
                a.NeedsAdmin = Bool(o, "needsAdmin");
                a.Mode = Str(o, "mode");
                a.Rights = StrArray(o, "rights") ?? new string[0];
                a.Sid = Str(o, "sid");
                a.Condition = Str(o, "condition");
                plan.Api.Add(a);
            }

            foreach (object e in Arr(root, "events"))
            {
                var o = (JObject)e;
                var ev = new PlanEvent();
                ev.Key = Str(o, "key");
                ev.Log = Str(o, "log");
                ev.Provider = Str(o, "provider");
                ev.EventId = (int)Num(o, "eventId");
                ev.Xpath = Str(o, "xpath");
                ev.AfterLastBoot = Bool(o, "afterLastBoot");
                ev.Mode = Str(o, "mode");
                plan.Events.Add(ev);
            }

            foreach (object e in Arr(root, "raw"))
            {
                var o = (JObject)e;
                var r = new PlanRaw();
                r.Key = Str(o, "key");
                r.Kind = Str(o, "kind");
                r.Tool = Str(o, "tool");
                r.Arguments = StrArray(o, "arguments") ?? new string[0];
                r.File = Str(o, "file");
                r.MaxBytes = Num(o, "maxBytes");
                r.NeedsAdmin = Bool(o, "needsAdmin");
                r.Mode = Str(o, "mode");
                plan.Raw.Add(r);
            }

            foreach (object m in Arr(root, "allowedWmiMethods")) plan.AllowedWmiMethods.Add((string)m);
            return plan;
        }

        private static JObject Obj(JObject o, string name)
        {
            var value = o.Get(name) as JObject;
            if (value == null) throw new FormatException(name + " fehlt");
            return value;
        }

        private static IList Arr(JObject o, string name)
        {
            return o.Get(name) as IList ?? new List<object>();
        }

        private static string Str(JObject o, string name)
        {
            return o.Get(name) as string;
        }

        private static long Num(JObject o, string name)
        {
            object v = o.Get(name);
            if (v is long) return (long)v;
            throw new FormatException(name + " ist keine Ganzzahl");
        }

        private static bool Bool(JObject o, string name)
        {
            object v = o.Get(name);
            return v is bool && (bool)v;
        }

        private static string[] StrArray(JObject o, string name)
        {
            var list = o.Get(name) as IList;
            if (list == null) return null;
            var result = new string[list.Count];
            for (int i = 0; i < list.Count; i++) result[i] = (string)list[i];
            return result;
        }
    }

    /// <summary>Ein Registry-Eintrag des Plans: recursive (bis MaxDepth), key (genau dieser Schlüssel), children (eine Ebene).</summary>
    internal sealed class PlanRegistry
    {
        public string Path;
        public string Kind;
        public int MaxDepth;
        public string[] Values;
        public bool NeedsAdmin;
        public string Mode;
    }

    /// <summary>WMI-Klasse mit Eigenschaften und erlaubten Methoden.</summary>
    internal sealed class PlanWmi
    {
        public string Key;
        public string Namespace;
        public string Class;
        public string Where;
        public string[] Properties;
        public string[] AdminOnlyProperties;
        public readonly List<PlanWmiMethod> Methods = new List<PlanWmiMethod>();
        public bool NeedsAdmin;
        public string Mode;
    }

    /// <summary>Methode eines WMI-Eintrags: static (Ergebnis unter „out“) oder perRow (je Zeile).</summary>
    internal sealed class PlanWmiMethod
    {
        public string Name;
        public string Kind;
        public string ViaClass;
        public string KeyProperty;
        public string[] OutProperties;
    }

    /// <summary>Abfrage über eine dokumentierte Windows-API.</summary>
    internal sealed class PlanApi
    {
        public string Key;
        public string Api;
        public bool NeedsAdmin;
        public string Mode;
        public string[] Rights;
        public string Sid;
        public string Condition;
    }

    /// <summary>Ereignis aus einem Protokoll mit fester XPath-Abfrage.</summary>
    internal sealed class PlanEvent
    {
        public string Key;
        public string Log;
        public string Provider;
        public int EventId;
        public string Xpath;
        public bool AfterLastBoot;
        public string Mode;
    }

    /// <summary>Rohdaten: Werkzeug mit festen Argumenten oder eine Datei.</summary>
    internal sealed class PlanRaw
    {
        public string Key;
        public string Kind;
        public string Tool;
        public string[] Arguments;
        public string File;
        public long MaxBytes;
        public bool NeedsAdmin;
        public string Mode;
    }

    /// <summary>Wann ein Planeintrag erfasst wird (spec 7.4).</summary>
    internal static class PlanModes
    {
        public const string Always = "always";
        public const string SkipInMinimal = "skipInMinimal";
        public const string DeepOnly = "deepOnly";
    }
}
