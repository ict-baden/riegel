// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt
// (keine LINQ, kein HashSet, kein System.Text.Json).
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Riegel.Collector.Collection
{
    /// <summary>
    /// JSON-Objekt mit fester Reihenfolge der Eigenschaften (wie geschrieben, so ausgegeben). Werte: null, bool,
    /// Ganzzahlen, double, string, <see cref="JObject"/>, <see cref="IList"/> (Arrays).
    /// </summary>
    internal sealed class JObject
    {
        private readonly List<string> _names = new List<string>();
        private readonly List<object> _values = new List<object>();

        public int Count { get { return _names.Count; } }

        public string NameAt(int index) { return _names[index]; }

        public object ValueAt(int index) { return _values[index]; }

        /// <summary>Setzt den Wert (ersetzt einen vorhandenen gleichen Namens, sonst hinten anhängen).</summary>
        public JObject Add(string name, object value)
        {
            int i = IndexOf(name);
            if (i >= 0) _values[i] = value;
            else
            {
                _names.Add(name);
                _values.Add(value);
            }
            return this;
        }

        /// <summary>Index des Namens (ordinal), -1 wenn nicht vorhanden.</summary>
        public int IndexOf(string name)
        {
            for (int i = 0; i < _names.Count; i++)
                if (string.Equals(_names[i], name, StringComparison.Ordinal)) return i;
            return -1;
        }

        public bool Has(string name) { return IndexOf(name) >= 0; }

        public object Get(string name)
        {
            int i = IndexOf(name);
            return i >= 0 ? _values[i] : null;
        }

        public void Remove(string name)
        {
            int i = IndexOf(name);
            if (i < 0) return;
            _names.RemoveAt(i);
            _values.RemoveAt(i);
        }
    }

    /// <summary>
    /// Schreibt JSON wie RiegelJson: UTF-8-taugliche Zeichenkette, eingerückt mit 2 Leerzeichen, LF, Umlaute lesbar,
    /// Zahlen invariant. Einzelne Surrogate werden zu U+FFFD, damit jeder JSON-Leser die Datei annimmt.
    /// </summary>
    internal static class JsonWriter
    {
        public static string Write(object value)
        {
            var sb = new StringBuilder(64 * 1024);
            WriteValue(sb, value, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        private static void Indent(StringBuilder sb, int depth)
        {
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        internal static void WriteValue(StringBuilder sb, object value, int depth)
        {
            if (value == null) { sb.Append("null"); return; }
            var s = value as string;
            if (s != null) { WriteString(sb, s); return; }
            if (value is bool) { sb.Append((bool)value ? "true" : "false"); return; }
            var o = value as JObject;
            if (o != null)
            {
                if (o.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                for (int i = 0; i < o.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Indent(sb, depth + 1);
                    WriteString(sb, o.NameAt(i));
                    sb.Append(": ");
                    WriteValue(sb, o.ValueAt(i), depth + 1);
                }
                Indent(sb, depth);
                sb.Append('}');
                return;
            }
            if (value is double || value is float)
            {
                double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (value is int || value is long || value is uint || value is ulong || value is short || value is ushort
                || value is byte || value is sbyte || value is decimal)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }
            var list = value as IList;
            if (list != null)
            {
                if (list.Count == 0) { sb.Append("[]"); return; }
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Indent(sb, depth + 1);
                    WriteValue(sb, list[i], depth + 1);
                }
                Indent(sb, depth);
                sb.Append(']');
                return;
            }
            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        internal static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); continue;
                    case '\\': sb.Append("\\\\"); continue;
                    case '\n': sb.Append("\\n"); continue;
                    case '\r': sb.Append("\\r"); continue;
                    case '\t': sb.Append("\\t"); continue;
                    case '\b': sb.Append("\\b"); continue;
                    case '\f': sb.Append("\\f"); continue;
                }
                if (c < 0x20 || c == 0x7F)
                {
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[i + 1]); i++; }
                    else sb.Append('�');
                }
                else if (char.IsLowSurrogate(c)) sb.Append('�');
                else sb.Append(c);
            }
            sb.Append('"');
        }
    }

    /// <summary>
    /// Kleiner JSON-Leser für den eingebetteten Erfassungsplan: Objekte als <see cref="JObject"/>, Arrays als
    /// <see cref="List{T}"/>, Ganzzahlen als long, andere Zahlen als double. Tiefe höchstens 64.
    /// </summary>
    internal sealed class JsonReader
    {
        private readonly string _text;
        private int _pos;

        private JsonReader(string text) { _text = text; }

        public static object Parse(string text)
        {
            var reader = new JsonReader(text ?? "");
            if (reader._text.Length > 0 && reader._text[0] == '﻿') reader._pos = 1;
            object value = reader.ReadValue(0);
            reader.SkipWhitespace();
            if (reader._pos != reader._text.Length) throw reader.Fail("Text nach dem Ende");
            return value;
        }

        private FormatException Fail(string what)
        {
            return new FormatException("JSON: " + what + " an Position " + _pos.ToString(CultureInfo.InvariantCulture));
        }

        private void SkipWhitespace()
        {
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') _pos++;
                else break;
            }
        }

        private object ReadValue(int depth)
        {
            if (depth > 64) throw Fail("zu tief geschachtelt");
            SkipWhitespace();
            if (_pos >= _text.Length) throw Fail("unerwartetes Ende");
            char c = _text[_pos];
            if (c == '{') return ReadObject(depth);
            if (c == '[') return ReadArray(depth);
            if (c == '"') return ReadString();
            if (c == 't') { Expect("true"); return true; }
            if (c == 'f') { Expect("false"); return false; }
            if (c == 'n') { Expect("null"); return null; }
            if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
            throw Fail("unerwartetes Zeichen");
        }

        private void Expect(string word)
        {
            if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0) throw Fail("erwartet " + word);
            _pos += word.Length;
        }

        private JObject ReadObject(int depth)
        {
            var o = new JObject();
            _pos++;
            SkipWhitespace();
            if (_pos < _text.Length && _text[_pos] == '}') { _pos++; return o; }
            while (true)
            {
                SkipWhitespace();
                if (_pos >= _text.Length || _text[_pos] != '"') throw Fail("Name erwartet");
                string name = ReadString();
                SkipWhitespace();
                if (_pos >= _text.Length || _text[_pos] != ':') throw Fail("':' erwartet");
                _pos++;
                o.Add(name, ReadValue(depth + 1));
                SkipWhitespace();
                if (_pos >= _text.Length) throw Fail("unerwartetes Ende");
                if (_text[_pos] == ',') { _pos++; continue; }
                if (_text[_pos] == '}') { _pos++; return o; }
                throw Fail("',' oder '}' erwartet");
            }
        }

        private List<object> ReadArray(int depth)
        {
            var list = new List<object>();
            _pos++;
            SkipWhitespace();
            if (_pos < _text.Length && _text[_pos] == ']') { _pos++; return list; }
            while (true)
            {
                list.Add(ReadValue(depth + 1));
                SkipWhitespace();
                if (_pos >= _text.Length) throw Fail("unerwartetes Ende");
                if (_text[_pos] == ',') { _pos++; continue; }
                if (_text[_pos] == ']') { _pos++; return list; }
                throw Fail("',' oder ']' erwartet");
            }
        }

        private string ReadString()
        {
            var sb = new StringBuilder();
            _pos++;
            while (true)
            {
                if (_pos >= _text.Length) throw Fail("Zeichenkette nicht beendet");
                char c = _text[_pos++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (_pos >= _text.Length) throw Fail("Escape nicht beendet");
                char e = _text[_pos++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_pos + 4 > _text.Length) throw Fail("\\u unvollständig");
                        sb.Append((char)int.Parse(_text.Substring(_pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        _pos += 4;
                        break;
                    default: throw Fail("unbekanntes Escape");
                }
            }
        }

        private object ReadNumber()
        {
            int start = _pos;
            bool integer = true;
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+') { _pos++; continue; }
                if (c == '.' || c == 'e' || c == 'E') { integer = false; _pos++; continue; }
                break;
            }
            string s = _text.Substring(start, _pos - start);
            long l;
            if (integer && long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l)) return l;
            double d;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            throw Fail("ungültige Zahl");
        }
    }
}
