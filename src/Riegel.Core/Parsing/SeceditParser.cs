// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Core.Parsing;

/// <summary>Eine Sektion einer Sicherheitsvorlage (secedit /export, GptTmpl.inf).</summary>
internal sealed class InfSection(string name)
{
    internal string Name { get; } = name;

    /// <summary>Schlüssel → Rohwert (getrimmt), Reihenfolge wie gelesen; doppelte Schlüssel: der letzte gilt.</summary>
    internal List<KeyValuePair<string, string>> Entries { get; } = [];

    internal void Set(string key, string value)
    {
        int index = Entries.FindIndex(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) Entries[index] = new(Entries[index].Key, value);
        else Entries.Add(new(key, value));
    }
}

/// <summary>
/// Sicherheitsvorlagen im INF-Format, wie <c>secedit /export</c> und GPO-Sicherungen (<c>GptTmpl.inf</c>) sie schreiben
/// (UTF-16 LE mit BOM, CRLF). Zeilen „Schlüssel = Wert“ in Sektionen „[Name]“; Zeilen ohne „=“ (z. B. in
/// [Service General Setting]) werden übergangen, Kommentare beginnen mit „;“. Ergebnis für <c>parsed:secedit</c>
/// (spec 7.3): <c>{ "sections": { "&lt;Sektion&gt;": { "&lt;Schlüssel&gt;": "&lt;Rohwert&gt;" } } }</c>, [Privilege Rights]
/// als Listen (an „,“ getrennt), [Registry Values] als <c>{ "type": n, "value": "&lt;roh&gt;" }</c>.
/// </summary>
internal static class SeceditParser
{
    internal const string SystemAccess = "System Access";
    internal const string EventAudit = "Event Audit";
    internal const string RegistryValues = "Registry Values";
    internal const string PrivilegeRights = "Privilege Rights";

    /// <summary>Liest eine Vorlage. Text außerhalb einer Sektion oder eine kaputte Sektionszeile → RGL-0264.</summary>
    internal static List<InfSection> Parse(string text, string source, int maxStringChars)
    {
        var sections = new List<InfSection>();
        InfSection? current = null;
        int lineNumber = 0;
        foreach (var raw in TextFiles.Lines(text))
        {
            lineNumber++;
            var line = raw.Trim();
            if (lineNumber == 1) line = line.TrimStart('﻿');
            if (line.Length == 0 || line[0] == ';') continue;
            if (line.Length > maxStringChars) throw RglCodesParsing.Error(RglCodesParsing.InfInvalid, source, lineNumber);
            if (line[0] == '[')
            {
                if (line[^1] != ']' || line.Length < 3) throw RglCodesParsing.Error(RglCodesParsing.InfInvalid, source, lineNumber);
                var name = line[1..^1].Trim();
                current = sections.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                if (current is null)
                {
                    current = new InfSection(name);
                    sections.Add(current);
                }
                continue;
            }
            if (current is null) throw RglCodesParsing.Error(RglCodesParsing.InfInvalid, source, lineNumber);
            int equals = line.IndexOf('=');
            if (equals <= 0) continue;
            var key = line[..equals].Trim();
            if (key.Length == 0) continue;
            current.Set(key, line[(equals + 1)..].Trim());
        }
        if (sections.Count == 0) throw RglCodesParsing.Error(RglCodesParsing.InfInvalid, source, 0);
        return sections;
    }

    /// <summary>„typ,wert“ eines [Registry Values]-Eintrags; null, wenn die Form nicht stimmt.</summary>
    internal static (int Type, string Value)? SplitRegistryValue(string raw)
    {
        int comma = raw.IndexOf(',');
        var typeText = (comma < 0 ? raw : raw[..comma]).Trim();
        if (!int.TryParse(typeText, NumberStyles.None, CultureInfo.InvariantCulture, out var type)) return null;
        return (type, comma < 0 ? "" : raw[(comma + 1)..]);
    }

    /// <summary>Wertname am Ende eines [Registry Values]-Pfads (<c>MACHINE\…\Lsa\NoLMHash</c> → NoLMHash).</summary>
    internal static string ValueNameOf(string registryValuePath)
    {
        int cut = registryValuePath.LastIndexOf('\\');
        return cut < 0 ? registryValuePath : registryValuePath[(cut + 1)..];
    }

    /// <summary>Wert von <c>parsed:secedit</c> aus den Sektionen. Geheime Wertnamen in [Registry Values] werden geschwärzt.</summary>
    internal static JsonElement ToParsedItem(IReadOnlyList<InfSection> sections) => JsonValues.Build(w =>
    {
        w.WriteStartObject();
        w.WritePropertyName("sections");
        w.WriteStartObject();
        foreach (var section in sections)
        {
            w.WritePropertyName(section.Name);
            w.WriteStartObject();
            bool rights = string.Equals(section.Name, PrivilegeRights, StringComparison.OrdinalIgnoreCase);
            bool registry = string.Equals(section.Name, RegistryValues, StringComparison.OrdinalIgnoreCase);
            foreach (var (key, value) in section.Entries)
            {
                w.WritePropertyName(key);
                if (rights)
                {
                    w.WriteStartArray();
                    foreach (var part in value.Split(','))
                    {
                        var trimmed = part.Trim();
                        if (trimmed.Length > 0) w.WriteStringValue(trimmed);
                    }
                    w.WriteEndArray();
                }
                else if (registry && SplitRegistryValue(value) is { } rv)
                {
                    w.WriteStartObject();
                    w.WriteNumber("type", rv.Type);
                    if (Secrets.ShouldRedact(ValueNameOf(key), Parsing.RegTypes.Name((uint)Math.Max(0, rv.Type))))
                    {
                        w.WriteString("value", CanonicalValue.Redacted);
                        w.WriteBoolean("redacted", true);
                    }
                    else
                    {
                        w.WriteString("value", rv.Value);
                    }
                    w.WriteEndObject();
                }
                else
                {
                    w.WriteStringValue(value);
                }
            }
            w.WriteEndObject();
        }
        w.WriteEndObject();
        w.WriteEndObject();
    });

    /// <summary>
    /// Schwärzt in einem INF-Text die Daten geheimer Wertnamen in [Registry Values] („…\DefaultPassword=1,"x"“ →
    /// „…\DefaultPassword=1,“). Gibt null zurück, wenn nichts zu schwärzen war.
    /// </summary>
    internal static string? RedactText(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool inRegistry = false, changed = false;
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != '\n') continue;
            var line = text[start..i];
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                inRegistry = string.Equals(trimmed[1..^1].Trim(), RegistryValues, StringComparison.OrdinalIgnoreCase);
            else if (inRegistry && trimmed.IndexOf('=') is var eq and > 0
                     && SplitRegistryValue(trimmed[(eq + 1)..]) is { } rv && rv.Value.Trim().Length > 0
                     && Secrets.ShouldRedact(ValueNameOf(trimmed[..eq].Trim()), RegTypes.Name((uint)Math.Max(0, rv.Type))))
            {
                line = trimmed[..eq] + "=" + rv.Type.ToString(CultureInfo.InvariantCulture) + "," + (line.EndsWith('\r') ? "\r" : "");
                changed = true;
            }
            builder.Append(line);
            if (i < text.Length) builder.Append('\n');
            start = i + 1;
        }
        return changed ? builder.ToString() : null;
    }
}
