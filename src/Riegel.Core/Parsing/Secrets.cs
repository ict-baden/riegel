// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;

namespace Riegel.Core.Parsing;

/// <summary>
/// Schwärzen von Geheimnissen (spec 7.3): Wertnamen, die (ohne Groß/klein) password, passwd, pwd, secret, token,
/// credential, passphrase oder community enthalten, dazu DefaultPassword und AltDefaultPassword, werden als
/// <c>{ "t": …, "redacted": true, "empty": … }</c> geschrieben. Unter jedem Schlüssel mit
/// <c>\SNMP\Parameters\ValidCommunities</c> im Pfad werden die Wertnamen (die Community-Zeichenketten) zu
/// <c>redacted-&lt;n&gt;</c>. Der Sammler schwärzt beim Erfassen, der Leser beim Import noch einmal.
/// <para>
/// Abweichung vom Wortlaut von spec 7.3 (Integrationsanfrage): Zahlenwerte (dword, qword) bleiben stehen. Eine 32- oder
/// 64-Bit-Zahl unter einem solchen Namen ist ein Schalter, kein Geheimnis – z. B. <c>LimitBlankPasswordUse</c> (Lsa),
/// <c>EnablePlainTextPassword</c> (LanmanWorkstation), <c>DisablePasswordSaving</c> und <c>fPromptForPassword</c>
/// (Terminal Services), die Regeln des Katalogs prüfen. Die synthetischen Fixtures des Gerüsts führen sie ebenso
/// ungeschwärzt.
/// </para>
/// </summary>
internal static class Secrets
{
    private static readonly string[] Fragments = ["password", "passwd", "pwd", "secret", "token", "credential", "passphrase", "community"];

    /// <summary>Ist das ein Wertname, dessen Inhalt nie im Export stehen darf?</summary>
    internal static bool IsSecretName(string valueName)
    {
        foreach (var fragment in Fragments)
            if (valueName.Contains(fragment, StringComparison.OrdinalIgnoreCase)) return true;
        // DefaultPassword und AltDefaultPassword enthalten „password“ und sind damit schon abgedeckt
        return false;
    }

    /// <summary>Wird ein Wert dieses Namens und Typs (Snapshot-Kodierung) geschwärzt? Zahlenwerte nie.</summary>
    internal static bool ShouldRedact(string valueName, string? type) => IsSecretName(valueName) && !IsNumericType(type);

    /// <summary>dword und qword (auch als Zahl 4 bzw. 11 in Sicherheitsvorlagen und Registry.pol).</summary>
    internal static bool IsNumericType(string? type) => type is "dword" or "qword";

    /// <summary>Schlüssel, unter dem die Wertnamen selbst geheim sind (SNMP-Communities).</summary>
    internal static bool IsSnmpCommunitiesKey(string keyPath) =>
        keyPath.Contains(@"\SNMP\Parameters\ValidCommunities", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Schwärzt den Wert eines <c>reg:</c>-Eintrags erneut. Gibt null zurück, wenn nichts zu tun war (der Eintrag bleibt
    /// dann bytegleich), sonst den neuen Wert.
    /// </summary>
    internal static JsonElement? RedactRegistryItem(string keyPath, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object)
            return null;
        bool snmp = IsSnmpCommunitiesKey(keyPath);
        bool needed = false;
        int index = 0;
        foreach (var property in values.EnumerateObject())
        {
            index++;
            if (snmp && property.Name != "redacted-" + index.ToString(CultureInfo.InvariantCulture)) needed = true;
            if (ShouldRedact(property.Name, TypeOf(property.Value)) && !IsRedacted(property.Value)) needed = true;
        }
        if (!needed) return null;
        return Model.JsonValues.Build(w =>
        {
            w.WriteStartObject();
            foreach (var top in value.EnumerateObject())
            {
                if (top.NameEquals("values"))
                {
                    w.WritePropertyName("values");
                    w.WriteStartObject();
                    int n = 0;
                    foreach (var property in values.EnumerateObject())
                    {
                        n++;
                        var name = snmp ? "redacted-" + n.ToString(CultureInfo.InvariantCulture) : property.Name;
                        w.WritePropertyName(name);
                        if (ShouldRedact(property.Name, TypeOf(property.Value)) && !IsRedacted(property.Value)) WriteRedacted(w, property.Value);
                        else property.Value.WriteTo(w);
                    }
                    w.WriteEndObject();
                }
                else
                {
                    top.WriteTo(w);
                }
            }
            w.WriteEndObject();
        });
    }

    private static string? TypeOf(JsonElement registryValue) =>
        registryValue.ValueKind == JsonValueKind.Object && registryValue.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;

    private static bool IsRedacted(JsonElement registryValue) =>
        registryValue.ValueKind == JsonValueKind.Object && registryValue.TryGetProperty("redacted", out var r) && r.ValueKind == JsonValueKind.True;

    private static void WriteRedacted(Utf8JsonWriter w, JsonElement original)
    {
        string type = original.ValueKind == JsonValueKind.Object && original.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()!
            : "unknown";
        bool empty = true;
        if (original.ValueKind == JsonValueKind.Object && original.TryGetProperty("v", out var v))
        {
            empty = v.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => true,
                JsonValueKind.String => v.GetString()!.Length == 0,
                JsonValueKind.Array => v.GetArrayLength() == 0,
                _ => false,
            };
        }
        w.WriteStartObject();
        w.WriteString("t", type);
        w.WriteBoolean("redacted", true);
        w.WriteBoolean("empty", empty);
        w.WriteEndObject();
    }
}
