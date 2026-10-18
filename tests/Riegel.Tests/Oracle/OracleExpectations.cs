// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Services;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Oracle;

/// <summary>
/// Erwartung des Orakels für eine Regel und einen Export (<c>expected-results.json</c>). <see cref="Status"/> ist ein
/// Ergebnisstatus oder „any“ (Ausgang weder durch die Recherche noch durch die Spezifikation festgelegt); bei „any“
/// schränkt <see cref="Allowed"/> die zulässigen Status ein, wenn angegeben.
/// </summary>
internal sealed record OracleExpectation(string RuleId, string Status, IReadOnlyList<string> Allowed, IReadOnlyList<string> Flags,
    IReadOnlyList<string> AbsentFlags, bool ResearchGap, string Why)
{
    public bool IsAny => Status == OracleData.Any;
}

/// <summary>Die Dateien des Orakels und die feste Auswertungsumgebung.</summary>
internal static class OracleData
{
    public const string Any = "any";

    /// <summary>Die Rollen der beiden Exporte (Schlüssel in <c>expected-results.json</c>).</summary>
    public static readonly string[] Roles = ["admin", "nonadmin"];

    /// <summary>Gültige Status der Erwartungen (camelCase wie im Ergebnis-JSON) plus „any“.</summary>
    public static readonly string[] Statuses = ["pass", "fail", "notApplicable", "notCheckable", "info", Any];

    /// <summary>Die 153 Regel-IDs des Katalogs 2026.10 aus spec 2.1, in der Reihenfolge der Tabelle.</summary>
    public static readonly string[] SpecRuleIds =
    [
        "LIFE-01", "LIFE-02", "LIFE-03", "LIFE-04", "LIFE-05",
        "NET-01", "NET-02", "NET-03", "NET-04", "NET-05", "NET-08", "NET-09", "NET-10", "NET-11", "NET-17", "NET-18", "NET-19",
        "NET-23", "NET-25", "NET-29", "NETX-01",
        "CRED-01", "CRED-02", "CRED-03", "CRED-04", "CRED-05", "CRED-06", "CRED-07", "CRED-08", "CRED-09", "CRED-10", "CRED-12",
        "CRED-13", "CRED-15", "CRED-17", "CRED-18", "CRED-20", "CRED-21",
        "LOGON-09", "LOGON-10",
        "ACCT-01", "ACCT-05", "ACCT-06", "ACCT-10", "ACCT-11", "ACCT-12", "ACCT-14", "ACCT-15", "ACCT-16", "ACCT-17",
        "UAC-01", "UAC-02", "UAC-04",
        "URA-02", "URA-03", "URA-05",
        "DEF-01", "DEF-02", "DEF-03", "DEF-09", "DEF-11", "DEF-12", "DEF-14", "DEF-17", "DEF-18",
        "ASR-01", "ASR-02", "ASR-05", "ASR-08", "ASR-09", "ASR-13", "ASR-15",
        "APP-01", "APP-02", "APP-06",
        "ENC-01", "ENC-02", "ENC-03", "ENC-04", "ENC-05", "ENC-06", "ENC-07",
        "FW-01", "FW-02", "FW-03", "FW-04", "FW-05", "FW-06", "FW-07", "FW-08", "FW-09", "FW-10", "FW-11",
        "SVC-01", "SVC-02", "SVC-04",
        "SYS-07", "SYS-08",
        "AUD-01", "AUD-02", "AUD-03", "AUD-04", "AUD-05", "AUD-06", "AUD-07", "AUD-08", "AUD-09", "AUD-10", "AUD-11", "AUD-12",
        "AUD-13", "AUD-14", "AUD-15", "AUD-16", "AUD-17", "AUD-18", "AUD-19", "AUD-20", "AUD-21", "AUD-22", "AUD-23", "AUD-26",
        "AUD-27", "AUD-28", "AUD-30",
        "PS-01", "PS-02",
        "RDP-01", "RDP-02", "RDP-03", "RDP-04", "RDP-05", "RDP-06", "RDP-07", "RDP-08", "RDP-09",
        "MED-01", "MED-02", "MED-03", "MED-04", "MED-05", "MED-06",
        "TLS-01", "TLS-02", "TLS-03", "TLS-04", "TLS-05", "TLS-06", "TLS-07", "TLS-08", "TLS-09", "TLS-10", "TLS-11",
    ];

    /// <summary>Feste Auswertungsumgebung: Tag 2026-10-18, Uhr 09:00 UTC, Werkseinstellungen der Anlage (35/90 Tage).</summary>
    public static EvaluationContext Context { get; } = new(new DateOnly(2026, 10, 18), FixedClock.Default, null, 35, 90, false, "en");

    /// <summary>Ordner des Orakels im Repo (die Dateien werden nicht in die Ausgabe kopiert, sie werden dort gelesen).</summary>
    public static string Folder => Path.Combine(RepoPaths.TestsProject, "Oracle");

    public static string ExpectedPath => Path.Combine(Folder, "expected-results.json");

    public static string ScriptPath => Path.Combine(Folder, "Compare-LocalRecording.ps1");

    public static string SnapshotPath(string role) => Path.Combine(Folder, $"oracle-{role}.snapshot.json");

    /// <summary>Liest die Erwartungen einer Rolle (Reihenfolge wie in der Datei).</summary>
    public static IReadOnlyList<OracleExpectation> Expectations(string role)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(ExpectedPath));
        var results = doc.RootElement.GetProperty("snapshots").GetProperty(role).GetProperty("results");
        var list = new List<OracleExpectation>();
        foreach (var entry in results.EnumerateObject())
        {
            var v = entry.Value;
            list.Add(new OracleExpectation(entry.Name, v.GetProperty("status").GetString()!, Strings(v, "allowed"), Strings(v, "flags"),
                Strings(v, "absentFlags"), v.TryGetProperty("researchGap", out var gap) && gap.GetBoolean(),
                v.TryGetProperty("why", out var why) ? why.GetString() ?? "" : ""));
        }
        return list;
    }

    /// <summary>Flags, die kein Ergebnis dieser frischen Exporte tragen darf (<c>globalAbsentFlags</c>).</summary>
    public static IReadOnlyList<string> GlobalAbsentFlags()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(ExpectedPath));
        return Strings(doc.RootElement, "globalAbsentFlags");
    }

    private static List<string> Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.GetString()!).ToList() : [];

    /// <summary>Status aus der camelCase-Schreibweise („notCheckable“ → <see cref="ResultStatus.NotCheckable"/>).</summary>
    public static ResultStatus ParseStatus(string s) =>
        Enum.TryParse<ResultStatus>(s, ignoreCase: true, out var status) && char.IsLower(s[0])
            ? status
            : throw new FormatException($"Unbekannter Status „{s}“.");

    /// <summary>Flag aus der camelCase-Schreibweise („fromDefault“ → <see cref="ResultFlags.FromDefault"/>).</summary>
    public static ResultFlags ParseFlag(string s) =>
        Enum.TryParse<ResultFlags>(s, ignoreCase: true, out var flag) && flag != ResultFlags.None && char.IsLower(s[0]) && !s.Contains(',')
            ? flag
            : throw new FormatException($"Unbekanntes Flag „{s}“.");

    /// <summary>Wie das Ergebnis-JSON den Status schreibt (camelCase).</summary>
    public static string Name(ResultStatus s) => char.ToLowerInvariant(s.ToString()[0]) + s.ToString()[1..];

    /// <summary>Wie das Ergebnis-JSON ein einzelnes Flag schreibt (camelCase).</summary>
    public static string Name(ResultFlags f) => char.ToLowerInvariant(f.ToString()[0]) + f.ToString()[1..];
}

/// <summary>Vergleich von Ergebnissen mit den Erwartungen (Status, verlangte und verbotene Flags).</summary>
internal static class OracleComparer
{
    /// <summary>Alle Abweichungen, je eine Zeile; leer = Ergebnisse passen zu den Erwartungen.</summary>
    /// <param name="inCatalogue">Gibt es die Regel im Katalog?</param>
    /// <param name="globalAbsentFlags">Flags, die kein Ergebnis tragen darf.</param>
    public static List<string> Differences(IReadOnlyList<OracleExpectation> expectations, IReadOnlyList<RuleResult> results,
        Func<string, bool> inCatalogue, IReadOnlyList<string> globalAbsentFlags)
    {
        var byId = new Dictionary<string, RuleResult>(StringComparer.Ordinal);
        foreach (var r in results.Where(r => r.RuleSetId == "riegel")) byId[r.RuleId] = r;
        var global = globalAbsentFlags.Select(OracleData.ParseFlag).ToList();
        var differences = new List<string>();
        foreach (var x in expectations)
        {
            if (!inCatalogue(x.RuleId)) { differences.Add($"{x.RuleId}: fehlt im Katalog"); continue; }
            if (!byId.TryGetValue(x.RuleId, out var result)) { differences.Add($"{x.RuleId}: kein Ergebnis"); continue; }
            var actual = OracleData.Name(result.Status);
            bool statusFits = x.IsAny ? x.Allowed.Count == 0 || x.Allowed.Contains(actual) : actual == x.Status;
            if (!statusFits)
            {
                var wanted = x.IsAny ? "erlaubt " + string.Join("|", x.Allowed) : "erwartet " + x.Status;
                differences.Add($"{x.RuleId}: {actual}, {wanted} ({result.ReasonCode}) – {x.Why}");
            }
            else
            {
                // Flags nur prüfen, wenn der Status passt (sonst wäre es dieselbe Abweichung zweimal)
                foreach (var flag in x.Flags.Where(f => (result.Flags & OracleData.ParseFlag(f)) == 0))
                    differences.Add($"{x.RuleId}: Flag {flag} fehlt – {x.Why}");
                foreach (var flag in x.AbsentFlags.Where(f => (result.Flags & OracleData.ParseFlag(f)) != 0))
                    differences.Add($"{x.RuleId}: Flag {flag} unerwartet – {x.Why}");
            }
            foreach (var flag in global.Where(f => (result.Flags & f) != 0))
                differences.Add($"{x.RuleId}: Flag {OracleData.Name(flag)} unerwartet (frischer Export, stimmige Zeiten)");
        }
        return differences;
    }
}

/// <summary>
/// Integrationsgate: Solange die Auswertung der Platzhalter ist (jede Regel „nicht prüfbar“) oder eine Regel-ID aus
/// spec 2.1 im eingebetteten Katalog fehlt, werden die Vergleichstests übersprungen. Der Integrationsschritt setzt
/// <see cref="Entfernt"/> auf true – danach wird nie mehr übersprungen, ein Platzhalter oder fehlende Regeln lassen
/// die Tests dann scheitern.
/// </summary>
internal static class OracleGate
{
    public const string Reason = "Oracle wartet auf Katalog und Engine (Integrationsgate)";

    /// <summary>Im Integrationsschritt auf true gesetzt (spec 8.2: grün mit der echten Engine ist ein Integrationsgate).</summary>
    public static readonly bool Entfernt = true;

    private static readonly Lazy<string?> Computed = new(Compute);

    /// <summary>Grund zum Überspringen oder null.</summary>
    public static string? SkipReason => Entfernt ? null : Computed.Value;

    private static string? Compute()
    {
        try
        {
            var services = RiegelServices.CreateDefault(new NullPlatform());
            var catalogue = services.Catalogue.LoadEmbedded();
            if (OracleData.SpecRuleIds.Any(id => catalogue.Find(id) is null)) return Reason;
            var snapshot = services.Snapshots.Read(OracleData.SnapshotPath("admin"), ReadLimits.Default);
            var results = services.Evaluator.Evaluate(snapshot, catalogue, OracleData.Context);
            return results.All(r => r.Status == ResultStatus.NotCheckable) ? Reason : null;
        }
        catch (Exception)
        {
            // Nicht überspringen: der Test selbst zeigt den Fehler
            return null;
        }
    }
}

/// <summary>
/// Die Orakel-Tests laufen nicht parallel zu anderen Klassen: <c>SchemaValidator</c> (TestSupport) registriert ein
/// Schema beim ersten Zugriff global, und zwei gleichzeitige erste Zugriffe auf dasselbe Schema scheitern mit
/// „Overwriting registered schemas is not permitted“ (gemeldet als Integrationsanfrage).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OracleCollection
{
    public const string Name = "Orakel";
}

/// <summary>Vergleichstest des Orakels; übersprungen, solange das Integrationsgate geschlossen ist.</summary>
public sealed class OracleFactAttribute : FactAttribute
{
    public OracleFactAttribute()
    {
        if (OracleGate.SkipReason is { } reason) Skip = reason;
    }
}
