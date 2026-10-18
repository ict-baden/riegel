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
    /// <summary>
    /// Meldungstabellen <c>messages.&lt;sprache&gt;.txt</c> (eingebettet, spec 7.10): je Zeile <c>Schlüssel=Text</c>,
    /// <c>\n</c> = Zeilenumbruch, Zeilen mit <c>#</c> sind Kommentare. Fehlt ein Schlüssel, gilt Englisch; kann die
    /// Codepage der Konsole die Sprache nicht darstellen, gilt ganz Englisch.
    /// </summary>
    internal sealed class ConsoleMessages
    {
        private readonly Dictionary<string, string> _texts;
        private readonly Dictionary<string, string> _english;

        private ConsoleMessages(Dictionary<string, string> texts, Dictionary<string, string> english)
        {
            _texts = texts;
            _english = english;
        }

        /// <summary>Sprache der Oberfläche; Englisch, wenn <paramref name="console"/> deren Zeichen nicht kodieren kann.</summary>
        public static ConsoleMessages Load(CultureInfo culture, Encoding console)
        {
            var english = Read("en");
            string code = Language(culture);
            if (code == "en") return new ConsoleMessages(english, english);
            var texts = Read(code);
            if (console != null && !CanShow(texts, console.CodePage)) return new ConsoleMessages(english, english);
            return new ConsoleMessages(texts, english);
        }

        /// <summary>Lassen sich alle Texte in der Codepage darstellen (ohne Ersatzzeichen)?</summary>
        internal static bool CanShow(Dictionary<string, string> texts, int codePage)
        {
            if (codePage == 65001 || codePage == 1200 || codePage == 1201) return true;
            Encoding strict;
            try { strict = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            foreach (string text in texts.Values)
            {
                try { strict.GetBytes(text); }
                catch (EncoderFallbackException) { return false; }
            }
            return true;
        }

        public string Get(string key)
        {
            string text;
            if (_texts.TryGetValue(key, out text) && text.Length > 0) return text;
            return _english.TryGetValue(key, out text) ? text : key;
        }

        public string Format(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, Get(key), args);
        }

        internal static string Language(CultureInfo culture)
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

        internal static Dictionary<string, string> Read(string code)
        {
            using (var stream = typeof(ConsoleMessages).Assembly.GetManifestResourceStream("messages." + code + ".txt"))
            {
                if (stream == null) return new Dictionary<string, string>(StringComparer.Ordinal);
                using (var reader = new StreamReader(stream, Encoding.UTF8)) return Parse(reader);
            }
        }

        /// <summary>Eine Tabelle lesen: Schlüssel=Text, „\n“ wird Zeilenumbruch, „#“-Zeilen sind Kommentare.</summary>
        internal static Dictionary<string, string> Parse(TextReader reader)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                result[line.Substring(0, equals)] = line.Substring(equals + 1).Replace("\\n", Environment.NewLine);
            }
            return result;
        }
    }
}
