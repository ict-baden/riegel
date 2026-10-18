// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Riegel.Collector
{
    /// <summary>
    /// Portabler Sammler (spec 7.4). Gerüst: nur <c>--version</c> und <c>--help</c>; er erfasst nichts, schreibt keine
    /// Datei und endet mit 0. Die Erfassung bringt das Paket collector (Ordner Collection/).
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var messages = Messages.Load(CultureInfo.CurrentUICulture);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, messages.Get("Version"), Version()));
            if (args.Length > 0 && Is(args[0], "--version")) return 0;
            Console.WriteLine(messages.Get("Help"));
            if (args.Length == 0 || !(Is(args[0], "--help") || Is(args[0], "--hilfe") || Is(args[0], "/?")))
                Console.WriteLine(messages.Get("Stub"));
            return 0;
        }

        private static bool Is(string arg, string name)
        {
            return string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);
        }

        private static string Version()
        {
            var attributes = typeof(Program).Assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
            return attributes.Length > 0 ? ((AssemblyInformationalVersionAttribute)attributes[0]).InformationalVersion : "unknown";
        }
    }

    /// <summary>
    /// Meldungstabellen <c>messages.&lt;sprache&gt;.txt</c> (eingebettet, spec 7.10): je Zeile <c>Schlüssel=Text</c>,
    /// <c>\n</c> = Zeilenumbruch, Zeilen mit <c>#</c> sind Kommentare. Fehlt ein Schlüssel, gilt Englisch.
    /// </summary>
    internal sealed class Messages
    {
        private readonly Dictionary<string, string> _texts;
        private readonly Dictionary<string, string> _english;

        private Messages(Dictionary<string, string> texts, Dictionary<string, string> english)
        {
            _texts = texts;
            _english = english;
        }

        public static Messages Load(CultureInfo culture)
        {
            var english = Read("en");
            string code = Language(culture);
            return new Messages(code == "en" ? english : Read(code), english);
        }

        public string Get(string key)
        {
            string text;
            if (_texts.TryGetValue(key, out text) && text.Length > 0) return text;
            return _english.TryGetValue(key, out text) ? text : key;
        }

        private static string Language(CultureInfo culture)
        {
            for (var c = culture; c != null && c.Name.Length > 0; c = c.Parent)
            {
                string name = c.Name;
                if (name == "zh-Hans" || name == "zh-CN" || name == "zh-SG" || name == "zh-CHS" || name == "zh") return "zh-Hans";
                if (name == "de" || name == "es" || name == "fr" || name == "en") return name;
                if (c.Parent == null || c.Parent.Name == c.Name) break;
            }
            return "en";
        }

        private static Dictionary<string, string> Read(string code)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var stream = typeof(Messages).Assembly.GetManifestResourceStream("messages." + code + ".txt"))
            {
                if (stream == null) return result;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0 || line[0] == '#') continue;
                        int equals = line.IndexOf('=');
                        if (equals <= 0) continue;
                        result[line.Substring(0, equals)] = line.Substring(equals + 1).Replace("\\n", Environment.NewLine);
                    }
                }
            }
            return result;
        }
    }
}
