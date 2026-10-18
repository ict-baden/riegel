// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Core.Parsing;

/// <summary>
/// CSV der erweiterten Überwachungsrichtlinie: <c>auditpol /backup</c> (Bordmittel-Paket) und <c>audit.csv</c> in
/// GPO-Sicherungen. Kopf <c>Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion
/// Setting,Setting Value</c>. Ausgewertet werden nur die GUID und die Zahl in <c>Setting Value</c> (1 = Erfolg,
/// 2 = Fehler, 3 = beides, 0 = keine) – nie die übersetzten Texte (research-technik §3.3). Zeilen „Option:…“ und Zeilen
/// ohne GUID werden übergangen. Die GUID wird über ihr Muster gefunden, damit ein Komma in einem übersetzten
/// Unterkategorienamen die Spalten nicht verschiebt.
/// </summary>
internal static class AuditCsvParser
{
    /// <summary>Eine Unterkategorie.</summary>
    /// <param name="Guid">In Großbuchstaben mit geschweiften Klammern, wie in <c>api:audit-policy</c>.</param>
    /// <param name="Name">Unterkategoriename aus der Datei (nur zur Anzeige; kann übersetzt sein).</param>
    internal sealed record AuditRow(string Guid, string Name, int Setting);

    /// <param name="strict">true (auditpol /backup): ein Wert außerhalb 0–3 ist ein Fehler; false (GPO-Sicherung): die
    /// Zeile wird übergangen.</param>
    internal static List<AuditRow> Parse(string text, string source, int maxRows, int maxFieldChars, bool strict = true)
    {
        List<CsvText.Row> rows;
        try
        {
            rows = CsvText.Parse(text, ',', maxRows + 1, maxFieldChars);
        }
        catch (Exception e) when (e is FormatException or InvalidDataException)
        {
            throw RglCodesParsing.Error(RglCodesParsing.AuditCsvInvalid, source, 0);
        }
        if (rows.Count == 0) throw RglCodesParsing.Error(RglCodesParsing.AuditCsvInvalid, source, 1);
        var header = rows[0].Fields;
        if (header.Count < 7 || !header[3].Trim().Equals("Subcategory GUID", StringComparison.OrdinalIgnoreCase)
            || !header[^1].Trim().Equals("Setting Value", StringComparison.OrdinalIgnoreCase))
            throw RglCodesParsing.Error(RglCodesParsing.AuditCsvInvalid, source, rows[0].Line);
        var result = new List<AuditRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Skip(1))
        {
            var fields = row.Fields;
            int guidIndex = -1;
            for (int i = 0; i < fields.Count - 1; i++)
                if (NormalizeGuid(fields[i]) is not null) { guidIndex = i; break; }
            // Optionszeilen (Option:CrashOnAuditFail …) und andere Zeilen ohne GUID (z. B. globale SACL) zählen nicht
            if (guidIndex < 0) continue;
            var guid = NormalizeGuid(fields[guidIndex])!;
            if (!int.TryParse(fields[^1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var setting) || setting is < 0 or > 3)
            {
                if (!strict) continue;
                throw RglCodesParsing.Error(RglCodesParsing.AuditCsvInvalid, source, row.Line);
            }
            if (!seen.Add(guid)) throw RglCodesParsing.Error(RglCodesParsing.AuditCsvInvalid, source, row.Line);
            var name = string.Join(",", fields.Skip(2).Take(Math.Max(0, guidIndex - 2))).Trim();
            result.Add(new AuditRow(guid, name, setting));
        }
        return result;
    }

    /// <summary>„{0cce9215-69ae-11d9-bed3-505054503030}“ → „{0CCE9215-69AE-11D9-BED3-505054503030}“; null, wenn keine GUID.</summary>
    internal static string? NormalizeGuid(string text)
    {
        var t = text.Trim();
        if (t.Length != 38 || t[0] != '{' || t[^1] != '}') return null;
        return Guid.TryParseExact(t, "B", out var g) ? "{" + g.ToString("D").ToUpperInvariant() + "}" : null;
    }

    /// <summary>Wert von <c>api:audit-policy</c>: <c>{ "{GUID}": 0..3 }</c>.</summary>
    internal static JsonElement ToItem(IEnumerable<AuditRow> rows) => JsonValues.Build(w =>
    {
        w.WriteStartObject();
        foreach (var row in rows.OrderBy(r => r.Guid, StringComparer.Ordinal)) w.WriteNumber(row.Guid, row.Setting);
        w.WriteEndObject();
    });
}
