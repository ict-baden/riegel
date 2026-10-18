// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Riegel.Core.Model;

namespace Riegel.Core.Parsing;

/// <summary>Inhalt von <c>parsed:gpresult</c> (spec 7.3) plus die Namen der administrativen Vorlagen.</summary>
/// <param name="Policies">Einstellungen administrativer Vorlagen (ADMX). Das XML nennt für sie nur den Namen, keinen
/// Registry-Pfad; sie lassen sich deshalb keinem Wert zuordnen (siehe Zuordnung „directlySet“).</param>
internal sealed record GpresultData(
    IReadOnlyList<GpresultData.Gpo> Gpos,
    IReadOnlyList<GpresultData.RegistryEntry> Registry,
    IReadOnlyList<GpresultData.Named> Security,
    IReadOnlyList<GpresultData.Named> Policies)
{
    internal sealed record Gpo(string Name, string Guid);

    internal sealed record RegistryEntry(string Key, string Value, string Gpo);

    internal sealed record Named(string Name, string Gpo);

    /// <summary>Kennung des lokalen Gruppenrichtlinienobjekts im gpresult-XML (auf dem Testrechner gemessen).</summary>
    internal const string LocalGpoId = "LocalGPO";

    /// <summary>Ist die GPO mit diesem Namen die lokale Gruppenrichtlinie?</summary>
    internal bool IsLocal(string gpoName) =>
        Gpos.Any(g => string.Equals(g.Name, gpoName, StringComparison.Ordinal) && string.Equals(g.Guid, LocalGpoId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Leser für <c>gpresult /scope computer /x</c> (RSoP als XML, Namensraum http://www.microsoft.com/GroupPolicy/Rsop).
/// Gelesen werden die GPO-Liste unter <c>ComputerResults</c> (Name, <c>Path/Identifier</c>; nicht angewendete GPOs mit
/// <c>AccessDenied</c> true, <c>FilterAllowed</c>, <c>IsValid</c> oder <c>Enabled</c> false fallen weg) und je
/// Erweiterung die gewinnenden Einstellungen (<c>Precedence</c> 1): <c>RegistrySetting</c> mit <c>KeyPath</c> und
/// <c>Value/Name</c>, <c>Policy</c> (nur Name), und aus den Erweiterungen SecuritySettings und AuditSettings der Name
/// der Einstellung (<c>KeyName</c>, <c>SystemAccessPolicyName</c>, <c>Name</c> oder <c>SubcategoryGuid</c>). Die
/// Elementnamen stammen aus der Ausgabe des Testrechners und den GPO-Berichten der Microsoft-Baselines (nur gelesen);
/// in einer echten Domäne ist das nicht getestet – die Zuordnung „Domänen-GPO“ ist deshalb experimentell (spec 2.10).
/// Sicher gegen feindliches XML: keine DTD, keine externen Ressourcen, Größen- und Tiefengrenze.
/// </summary>
internal static class GpresultParser
{
    private const int MaxXmlDepth = 64;

    internal static GpresultData Parse(string xml, string source, long maxChars)
    {
        var document = Load(xml, source, maxChars);
        var computer = document.Root?.Descendants().FirstOrDefault(e => e.Name.LocalName == "ComputerResults")
            ?? throw RglCodesParsing.Error(RglCodesParsing.GpresultInvalid, source, "ComputerResults");
        var gpos = new List<GpresultData.Gpo>();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var gpo in Children(computer, "GPO"))
        {
            var name = Text(Child(gpo, "Name"));
            var id = Text(Child(Child(gpo, "Path"), "Identifier"));
            if (name is null || id is null) continue;
            if (Flag(gpo, "AccessDenied") == true || Flag(gpo, "FilterAllowed") == false || Flag(gpo, "IsValid") == false
                || Flag(gpo, "Enabled") == false) continue;
            if (names.TryAdd(id, name)) gpos.Add(new(name, id));
        }
        var registry = new List<GpresultData.RegistryEntry>();
        var security = new List<GpresultData.Named>();
        var policies = new List<GpresultData.Named>();
        var seenRegistry = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var data in Children(computer, "ExtensionData"))
        {
            foreach (var extension in Children(data, "Extension"))
            {
                var kind = ExtensionKind(extension);
                foreach (var setting in extension.Elements())
                {
                    var gpoId = Text(Child(Child(setting, "GPO"), "Identifier"));
                    if (gpoId is null) continue;
                    var precedence = Text(Child(setting, "Precedence"));
                    if (precedence is not null && precedence != "1") continue;
                    var gpoName = names.TryGetValue(gpoId, out var n) ? n : gpoId;
                    switch (setting.Name.LocalName)
                    {
                        case "RegistrySetting":
                            var keyPath = Text(Child(setting, "KeyPath"));
                            var valueName = Text(Child(Child(setting, "Value"), "Name"));
                            if (keyPath is null || valueName is null) continue; // nur Schlüssel angelegt
                            var key = @"HKLM\" + keyPath.Trim('\\');
                            if (seenRegistry.Add(AttributionMap.KeyOf(key, valueName))) registry.Add(new(key, valueName, gpoName));
                            break;
                        case "Policy":
                            var policy = Text(Child(setting, "Name"));
                            if (policy is not null) policies.Add(new(policy, gpoName));
                            break;
                        default:
                            if (kind is not ("SecuritySettings" or "AuditSettings")) continue;
                            var settingName = Text(Child(setting, "KeyName")) ?? Text(Child(setting, "SystemAccessPolicyName"))
                                ?? Text(Child(setting, "Name")) ?? Text(Child(setting, "SubcategoryGuid"));
                            if (settingName is not null) security.Add(new(settingName, gpoName));
                            break;
                    }
                }
            }
        }
        return new GpresultData(gpos, registry, security, policies);
    }

    /// <summary>XML sicher laden: keine DTD, keine Auflösung externer Ressourcen, höchstens <paramref name="maxChars"/> Zeichen und Tiefe 64.</summary>
    internal static XDocument Load(string xml, string source, long maxChars)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maxChars,
            MaxCharactersFromEntities = 1024,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CloseInput = true,
        };
        try
        {
            using (var scan = XmlReader.Create(new StringReader(xml), settings))
                while (scan.Read())
                    if (scan.Depth > MaxXmlDepth) throw RglCodesParsing.Error(RglCodesParsing.GpresultInvalid, source, "depth");
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException e)
        {
            throw new RiegelException(RglCodesParsing.GpresultInvalid,
                RglCodesParsing.Message(RglCodesParsing.GpresultInvalid, source, e.LineNumber.ToString(CultureInfo.InvariantCulture)), e);
        }
    }

    private static string? ExtensionKind(XElement extension)
    {
        var type = extension.Attributes().FirstOrDefault(a => a.Name.LocalName == "type" && a.Name.NamespaceName == "http://www.w3.org/2001/XMLSchema-instance")?.Value;
        if (type is null) return null;
        int colon = type.IndexOf(':');
        return colon >= 0 ? type[(colon + 1)..] : type;
    }

    private static IEnumerable<XElement> Children(XElement? parent, string localName) =>
        parent is null ? [] : parent.Elements().Where(e => e.Name.LocalName == localName);

    private static XElement? Child(XElement? parent, string localName) => Children(parent, localName).FirstOrDefault();

    private static string? Text(XElement? element)
    {
        var value = element?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static bool? Flag(XElement gpo, string localName) => Text(Child(gpo, localName)) switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    /// <summary>Wert von <c>parsed:gpresult</c>; <c>policies</c> ergänzt die Form aus spec 7.3.</summary>
    internal static JsonElement ToParsedItem(GpresultData data) => JsonValues.Build(w =>
    {
        w.WriteStartObject();
        w.WritePropertyName("gpos");
        w.WriteStartArray();
        foreach (var g in data.Gpos)
        {
            w.WriteStartObject();
            w.WriteString("name", g.Name);
            w.WriteString("guid", g.Guid);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WritePropertyName("registry");
        w.WriteStartArray();
        foreach (var r in data.Registry)
        {
            w.WriteStartObject();
            w.WriteString("key", r.Key);
            w.WriteString("value", r.Value);
            w.WriteString("gpo", r.Gpo);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        WriteNamed(w, "security", data.Security);
        if (data.Policies.Count > 0) WriteNamed(w, "policies", data.Policies);
        w.WriteEndObject();
    });

    private static void WriteNamed(Utf8JsonWriter w, string property, IReadOnlyList<GpresultData.Named> list)
    {
        w.WritePropertyName(property);
        w.WriteStartArray();
        foreach (var s in list)
        {
            w.WriteStartObject();
            w.WriteString("name", s.Name);
            w.WriteString("gpo", s.Gpo);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    /// <summary>
    /// <c>parsed:gpresult</c> zurück in die Datenform (für die Zuordnung; synthetische Exporte bringen den Eintrag mit).
    /// Unbekannte oder falsch geformte Teile werden übergangen.
    /// </summary>
    internal static GpresultData FromParsedItem(JsonElement value)
    {
        var gpos = new List<GpresultData.Gpo>();
        var registry = new List<GpresultData.RegistryEntry>();
        var security = new List<GpresultData.Named>();
        var policies = new List<GpresultData.Named>();
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var g in Array(value, "gpos"))
                if (Str(g, "name") is { } name && Str(g, "guid") is { } guid) gpos.Add(new(name, guid));
            foreach (var r in Array(value, "registry"))
                if (Str(r, "key") is { } key && Str(r, "value") is { } v && Str(r, "gpo") is { } gpo) registry.Add(new(key, v, gpo));
            foreach (var s in Array(value, "security"))
                if (Str(s, "name") is { } name && Str(s, "gpo") is { } gpo) security.Add(new(name, gpo));
            foreach (var p in Array(value, "policies"))
                if (Str(p, "name") is { } name && Str(p, "gpo") is { } gpo) policies.Add(new(name, gpo));
        }
        return new GpresultData(gpos, registry, security, policies);
    }

    private static IEnumerable<JsonElement> Array(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray() : [];

    private static string? Str(JsonElement e, string property) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    /// <summary>
    /// Schwärzt im XML die Daten von <c>RegistrySetting</c>-Werten mit geheimem Namen (spec 7.3). Gibt null zurück, wenn
    /// nichts zu tun war (der Text bleibt dann unverändert).
    /// </summary>
    internal static string? Redact(string xml, string source, long maxChars)
    {
        var document = Load(xml, source, maxChars);
        bool changed = false;
        foreach (var value in document.Descendants().Where(e => e.Name.LocalName == "Value" && e.Parent?.Name.LocalName == "RegistrySetting"))
        {
            var name = Text(Child(value, "Name"));
            if (name is null || !Secrets.IsSecretName(name)) continue;
            // Zahlen (Number) sind Schalter, keine Geheimnisse (siehe Secrets)
            foreach (var data in value.Elements().Where(e => e.Name.LocalName is not ("Name" or "Number")).ToList())
            {
                if (data.IsEmpty || data.Value.Length == 0) continue;
                data.RemoveNodes();
                changed = true;
            }
        }
        if (!changed) return null;
        return (document.Declaration is { } declaration ? declaration + "\n" : "") + document.ToString(SaveOptions.DisableFormatting);
    }
}
