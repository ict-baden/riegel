// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Parsing;
using Riegel.Core.Services;

namespace Riegel.Core.Attribution;

/// <summary>
/// Woher ein Registry-Wert kommt (spec 2.10, research-technik §3.2). Je Wert eines <c>reg:</c>-Eintrags, in dieser
/// Reihenfolge:
/// <list type="number">
/// <item><b>domainGpo</b> (experimentell): <c>parsed:gpresult</c> nennt den Wert (RegistrySetting, oder als
/// Sicherheitsoption mit <c>KeyName</c> „MACHINE\…“) aus einem anderen GPO als der lokalen Gruppenrichtlinie; Detail =
/// GPO-Name.</item>
/// <item><b>localPolicy</b>: <c>parsed:registry-pol</c> (lokale Registry.pol) setzt genau diesen Wert mit diesem Typ und
/// Inhalt, oder gpresult nennt die lokale Gruppenrichtlinie. Auf einem Domänenmitglied ohne gpresult ist nicht
/// auszuschließen, dass eine Domänen-GPO denselben Wert setzt – dann keine Zuordnung (unknown).</item>
/// <item><b>mdm</b>: der Wert liegt unter <c>SOFTWARE\Microsoft\PolicyManager\current\device</c>; Detail = Bereich.</item>
/// <item><b>directlySet</b>: unter <c>SOFTWARE\Policies</c> oder <c>…\CurrentVersion\Policies</c>, in keiner der Quellen
/// – nur entscheidbar mit Admin-Rechten und gpresult (<see cref="AttributionMap.Determinable"/>), und nur, wenn
/// gpresult keine administrativen Vorlagen eines anderen GPOs nennt (die XML-Ausgabe gibt für sie keinen
/// Registry-Pfad an; ein so gesetzter Wert sähe sonst „direkt gesetzt“ aus).</item>
/// </list>
/// Alles andere bekommt keinen Eintrag (Herkunft unknown); osDefault, runtime und notSet setzt die Auswertung.
/// </summary>
internal sealed class ValueSourceAttributor : IValueSourceAttributor
{
    private static readonly string[] PolicyRoots = [@"HKLM\SOFTWARE\Policies", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies"];
    private const string MdmRoot = @"HKLM\SOFTWARE\Microsoft\PolicyManager\current\device";

    /// <summary>Detail für Werte aus der lokalen Registry.pol.</summary>
    internal const string RegistryPolDetail = "Registry.pol";

    public AttributionMap Build(Snapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var gpresult = OkValue(s, ItemKeys.ParsedGpresult) is { } g ? GpresultParser.FromParsedItem(g) : null;
        var registryPol = OkValue(s, ItemKeys.ParsedRegistryPol) is { } p ? ReadRegistryPol(p) : null;
        bool determinable = s.IsAdmin && gpresult is not null;
        // DomainRole 1/3/4/5 = Domänenmitglied oder DC (0/2 = eigenständig); null = unbekannt
        bool? domainJoined = s.Machine.DomainRole is { } role ? role is 1 or 3 or 4 or 5 : null;

        var domain = new Dictionary<string, string>(StringComparer.Ordinal);
        var localGpo = new Dictionary<string, string>(StringComparer.Ordinal);
        bool unmappedDomainPolicies = false;
        if (gpresult is not null)
        {
            foreach (var r in gpresult.Registry)
                (gpresult.IsLocal(r.Gpo) ? localGpo : domain).TryAdd(AttributionMap.KeyOf(r.Key, r.Value), r.Gpo);
            foreach (var sec in gpresult.Security)
            {
                if (!sec.Name.StartsWith(@"MACHINE\", StringComparison.OrdinalIgnoreCase)) continue;
                var path = @"HKLM\" + sec.Name[8..];
                int cut = path.LastIndexOf('\\');
                if (cut <= 5) continue;
                (gpresult.IsLocal(sec.Gpo) ? localGpo : domain).TryAdd(AttributionMap.KeyOf(path[..cut], path[(cut + 1)..]), sec.Gpo);
            }
            unmappedDomainPolicies = gpresult.Policies.Any(x => !gpresult.IsLocal(x.Gpo));
        }

        var entries = new SortedDictionary<string, ValueAttribution>(StringComparer.Ordinal);
        foreach (var item in s.Items)
        {
            if (!item.Key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase) || item.Status != ItemStatus.Ok || item.Value is not { } value) continue;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object) continue;
            var keyPath = item.Key[4..].TrimEnd('\\');
            foreach (var property in values.EnumerateObject())
            {
                var k = AttributionMap.KeyOf(keyPath, property.Name);
                if (domain.TryGetValue(k, out var gpoName))
                {
                    entries[k] = new ValueAttribution(ValueSource.DomainGpo, gpoName, Experimental: true);
                    continue;
                }
                if (localGpo.TryGetValue(k, out var localName))
                {
                    entries[k] = new ValueAttribution(ValueSource.LocalPolicy, localName, false);
                    continue;
                }
                if (registryPol is not null && registryPol.TryGetValue(k, out var polValue) && SameValue(property.Value, polValue))
                {
                    if (domainJoined == false || gpresult is not null) entries[k] = new ValueAttribution(ValueSource.LocalPolicy, RegistryPolDetail, false);
                    continue;
                }
                if (Under(keyPath, MdmRoot))
                {
                    var area = keyPath.Length > MdmRoot.Length ? keyPath[(MdmRoot.Length + 1)..].Split('\\')[0] : null;
                    entries[k] = new ValueAttribution(ValueSource.Mdm, area, false);
                    continue;
                }
                if (PolicyRoots.Any(r => Under(keyPath, r)) && determinable && !unmappedDomainPolicies)
                    entries[k] = new ValueAttribution(ValueSource.DirectlySet, null, false);
            }
        }
        return new AttributionMap(entries, determinable);
    }

    private static JsonElement? OkValue(Snapshot s, string key)
    {
        var item = s.Items.FirstOrDefault(i => ItemKeys.Same(i.Key, key));
        return item is { Status: ItemStatus.Ok, Truncated: false, Value: { } v } ? v : null;
    }

    /// <summary><c>parsed:registry-pol</c> → Schlüssel|Wert → <c>{ t, v }</c>; der letzte Eintrag gilt (wie beim Anwenden).</summary>
    private static Dictionary<string, JsonElement> ReadRegistryPol(JsonElement value)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (value.ValueKind != JsonValueKind.Array) return map;
        foreach (var e in value.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String
                || !e.TryGetProperty("value", out var name) || name.ValueKind != JsonValueKind.String) continue;
            map[AttributionMap.KeyOf(key.GetString()!, name.GetString()!)] = e;
        }
        return map;
    }

    /// <summary>Gleicher Typ und gleicher Inhalt (Registry-Kodierung, spec 7.3); geschwärzte Werte zählen nie als gleich.</summary>
    private static bool SameValue(JsonElement registry, JsonElement pol)
    {
        if (Flag(registry, "redacted") || Flag(pol, "redacted") || Flag(registry, "truncated") || Flag(pol, "truncated")) return false;
        if (!Str(registry, "t").Equals(Str(pol, "t"), StringComparison.Ordinal)) return false;
        bool hasA = registry.TryGetProperty("v", out var a), hasB = pol.TryGetProperty("v", out var b);
        if (!hasA || !hasB) return hasA == hasB;
        return CanonicalValue.RenderRegistry(Str(registry, "t"), a) == CanonicalValue.RenderRegistry(Str(pol, "t"), b);
    }

    private static bool Flag(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var f) && f.ValueKind == JsonValueKind.True;

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";

    private static bool Under(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
}
