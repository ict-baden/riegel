// SPDX-License-Identifier: MIT
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Riegel.Core.Model;

namespace Riegel.Core.Remediation;

/// <summary>
/// <c>GptTmpl.inf</c>-Ausschnitt (Sicherheitsvorlage) nach [MS-GPSB] 2.2 „Message Syntax“
/// (https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpsb/fa15485d-ae9f-456e-a08f-81f2e5725a7e, gesehen
/// 2026-10-18): UTF-16 LE mit BOM, CRLF, <c>[Unicode] Unicode=yes</c>, <c>[Version] signature="$CHICAGO$" Revision=1</c>,
/// dann die Abschnitte. Benutzerrechte nach 2.2.6 (<c>Recht = *SID,*SID</c>), Registry-Werte nach 2.2.5
/// (<c>MACHINE\…=Typ,Wert</c>). Kommentare mit „;“ nach den allgemeinen INF-Regeln
/// (https://learn.microsoft.com/en-us/windows-hardware/drivers/install/general-syntax-rules-for-inf-files).
/// </summary>
internal static partial class TemplateFile
{
    /// <summary>Unterstützte Abschnitte in der Reihenfolge der Tabelle in [MS-GPSB] 2.2 (ohne ACL-Abschnitte).</summary>
    public static IReadOnlyList<string> Sections { get; } =
        ["System Access", "Kerberos Policy", "System Log", "Security Log", "Application Log", "Event Audit", "Registry Values", "Privilege Rights"];

    public const string PrivilegeRights = "Privilege Rights";
    public const string RegistryValues = "Registry Values";

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SettingKey();

    [GeneratedRegex(@"^(\d{1,10}|""[\x20\x21\x23-\x7E]{0,1024}"")$", RegexOptions.CultureInvariant)]
    private static partial Regex SettingValue();

    /// <summary>[MS-GPSB] 2.2.5: KeyName aus IdCharacter (%x20-21, %x23-5B, %x5D-7E), mit „\“ getrennt, beginnend mit MACHINE.</summary>
    [GeneratedRegex(@"^MACHINE(\\[\x20\x21\x23-\x5B\x5D-\x7E]{1,255}){2,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex RegistryValueName();

    /// <summary>[MS-GPSB] 2.2.5: Typ 1, 2, 3, 4 oder 7, dann der Wert (druckbares ASCII ohne „;“, außer in Anführungszeichen).</summary>
    [GeneratedRegex(@"^[12347],([\x20\x21\x23-\x3A\x3C-\x7E]{0,1024}|""[\x20\x21\x23-\x7E]{0,1024}"")$", RegexOptions.CultureInvariant)]
    private static partial Regex RegistryValueData();

    [GeneratedRegex(@"^Se[A-Za-z]{1,60}(Privilege|Right)$", RegexOptions.CultureInvariant)]
    private static partial Regex RightName();

    [GeneratedRegex(@"^S-1-\d{1,14}(-\d{1,10}){1,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sid();

    [GeneratedRegex(@"^\*-(\d{1,10})$", RegexOptions.CultureInvariant)]
    private static partial Regex LocalRid();

    /// <summary>Eine geprüfte Zeile für einen Abschnitt.</summary>
    internal sealed record Line(string Section, string Key, string Text, string NewValue);

    /// <summary>
    /// Prüft eine Katalog-Aktion und bildet die Zeile. null = unzulässig (Abschnitt, Schlüssel oder Wert) oder bei
    /// leerer SID-Liste nicht als Datei ausdrückbar (dann manueller Schritt).
    /// </summary>
    public static Line? Build(TemplateAction a, string? machineSidPrefix, out bool needsManual)
    {
        needsManual = false;
        var section = Sections.FirstOrDefault(s => string.Equals(s, a.Section?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (section is null || a.Key is null) return null;
        if (section == PrivilegeRights)
        {
            if (!RightName().IsMatch(a.Key)) return null;
            if (a.Sids is not { Count: > 0 } sids) { needsManual = true; return null; }
            var resolved = new List<string>();
            foreach (var raw in sids)
            {
                var sid = raw.StartsWith('*') && raw.Length > 1 && raw[1] == 'S' ? raw[1..] : raw;
                if (LocalRid().Match(sid) is { Success: true } rid)
                {
                    if (machineSidPrefix is null) { needsManual = true; return null; }
                    sid = machineSidPrefix + "-" + rid.Groups[1].Value;
                }
                if (!Sid().IsMatch(sid)) return null;
                resolved.Add("*" + sid);
            }
            var value = string.Join(",", resolved);
            return new Line(section, a.Key, a.Key + " = " + value, value);
        }
        if (a.Value is null) return null;
        if (section == RegistryValues)
        {
            if (!RegistryValueName().IsMatch(a.Key) || !RegistryValueData().IsMatch(a.Value)) return null;
            return new Line(section, a.Key, a.Key + "=" + a.Value, a.Value);
        }
        if (!SettingKey().IsMatch(a.Key) || !SettingValue().IsMatch(a.Value)) return null;
        return new Line(section, a.Key, a.Key + " = " + a.Value, a.Value);
    }

    /// <summary>
    /// SID-Präfix des Rechners (S-1-5-21-a-b-c) aus den lokalen Konten des Exports (<c>api:netuserenum</c>), für
    /// <c>*-&lt;RID&gt;</c> im Katalog. null, wenn nicht bestimmbar.
    /// </summary>
    public static string? MachineSidPrefix(Snapshot s)
    {
        var item = s.Items.FirstOrDefault(i => ItemKeys.Same(i.Key, ItemKeys.NetUserEnum) && i.Status == ItemStatus.Ok);
        if (item?.Value is not { ValueKind: JsonValueKind.Array } accounts) return null;
        var prefixes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var account in accounts.EnumerateArray())
        {
            if (account.ValueKind != JsonValueKind.Object || !account.TryGetProperty("sid", out var sid) || sid.ValueKind != JsonValueKind.String) continue;
            var text = sid.GetString() ?? "";
            if (!Sid().IsMatch(text) || !text.StartsWith("S-1-5-21-", StringComparison.Ordinal)) continue;
            var parts = text.Split('-');
            if (parts.Length != 8) continue;
            prefixes.Add(string.Join('-', parts[..7]));
        }
        return prefixes.Count == 1 ? prefixes.Min : null;
    }

    /// <summary>Die Datei: Hinweis, Präambel, Abschnitte in fester Reihenfolge.</summary>
    public static byte[] Encode(IReadOnlyList<string> comments, IReadOnlyList<Line> lines)
    {
        var all = new List<string>();
        all.AddRange(comments.Select(c => "; " + Values.Printable(c, 2000)));
        all.Add("[Unicode]");
        all.Add("Unicode=yes");
        all.Add("[Version]");
        all.Add("signature=\"$CHICAGO$\"");
        all.Add("Revision=1");
        foreach (var section in Sections)
        {
            var inSection = lines.Where(l => l.Section == section).ToList();
            if (inSection.Count == 0) continue;
            all.Add("[" + section + "]");
            all.AddRange(inSection.Select(l => l.Text));
        }
        return [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(string.Join("\r\n", all) + "\r\n")];
    }
}

/// <summary>
/// <c>audit.csv</c> der erweiterten Überwachungsrichtlinie nach [MS-GPAC] 2.2 „Message Syntax“
/// (https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpac/6494a0f2-8a16-40e2-b87d-328be7d732e0) und
/// 2.2.1.3.1 (https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpac/21ad2e80-3011-48ef-be13-cc11ff7bfeb1),
/// gesehen 2026-10-18: UTF-8, CRLF, Kopfzeile
/// <c>Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value</c>,
/// je Unterkategorie <c>,System,&lt;Name&gt;,{GUID},&lt;Text&gt;,,&lt;0–4&gt;</c> (1 Erfolg, 2 Fehler, 3 beides, 4 keine).
/// Die Spaltenfolge stimmt mit einer aufgezeichneten <c>auditpol /backup</c>-Datei überein. Erste Zeile: der
/// Pflichthinweis mit „#“ – [MS-GPAC] kennt keine Kommentare, deshalb vor einem Import entfernen (README).
/// </summary>
internal static partial class AuditCsv
{
    public const string Header = "Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value";

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$", RegexOptions.CultureInvariant)]
    private static partial Regex Guid();

    /// <summary>[MS-GPAC] StringWithSpaces: ALPHANUM, „/“, „-“, „:“, „;“ und Leerraum.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9/\-:; ]{1,200}$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainName();

    /// <summary>[MS-GPAC] QuotedString: druckbares ASCII ohne „"“.</summary>
    [GeneratedRegex(@"^[\x20\x21\x23-\x7E]{1,200}$", RegexOptions.CultureInvariant)]
    private static partial Regex QuotableName();

    internal sealed record Row(string Guid, string Subcategory, AuditSetting Setting, string Text);

    public static Row? Build(AuditAction a)
    {
        if (a.Guid is null || !Guid().IsMatch(a.Guid.Trim()) || string.IsNullOrWhiteSpace(a.Subcategory)) return null;
        var name = a.Subcategory.Trim();
        string field;
        if (PlainName().IsMatch(name)) field = name;
        else if (QuotableName().IsMatch(name)) field = "\"" + name + "\"";
        else return null;
        var (inclusion, value) = a.Setting switch
        {
            AuditSetting.Success => ("Success", 1),
            AuditSetting.Failure => ("Failure", 2),
            AuditSetting.SuccessAndFailure => ("Success and Failure", 3),
            _ => ("No Auditing", 4),
        };
        var guid = a.Guid.Trim().ToUpperInvariant();
        return new Row(guid, name, a.Setting, $",System,{field},{guid},{inclusion},,{value}");
    }

    public static byte[] Encode(string notice, IReadOnlyList<Row> rows)
    {
        var lines = new List<string> { "# " + Values.Printable(notice, 2000), Header };
        lines.AddRange(rows.Select(r => r.Text));
        return new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines) + "\r\n");
    }
}
