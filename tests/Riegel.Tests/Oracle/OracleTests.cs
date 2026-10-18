// SPDX-License-Identifier: MIT
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Oracle;

/// <summary>
/// Unabhängiges Orakel (spec 8.4): zwei Exporte desselben Rechners (erhöht und eingeschränktes Token), geschrieben
/// nur aus den Messungen in research-technik.md §2/§3/§3.1–3.3/§4.3, und je Regel-ID das erwartete Ergebnis. Die
/// Erwartungen stammen nicht aus Regel- oder Engine-Code. Nur der Autor des Orakels oder die Prüfung der Integration
/// ändern sie, mit Begründung in der Commit-Nachricht (README.md).
/// </summary>
[Collection(OracleCollection.Name)]
public sealed partial class OracleTests
{
    public static TheoryData<string> Roles()
    {
        var data = new TheoryData<string>();
        foreach (var role in OracleData.Roles) data.Add(role);
        return data;
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void Export_ist_schemagueltig_und_ueber_den_Leser_lesbar(string role)
    {
        var path = OracleData.SnapshotPath(role);
        var json = File.ReadAllText(path);
        SchemaValidator.AssertValid(json, Path.GetFileName(path));

        var snapshot = RiegelServices.CreateDefault(new NullPlatform()).Snapshots.Read(path, ReadLimits.Default);
        Assert.Equal(SnapshotOrigin.Synthetic, snapshot.Origin);
        Assert.Equal("ORACLE-PC", snapshot.Machine.HostName);
        Assert.Equal(role == "admin", snapshot.IsAdmin);
        Assert.Equal((0, 1, 26200, 9711, "Professional"),
            (snapshot.Machine.DomainRole, snapshot.Machine.ProductType, snapshot.Machine.Build, snapshot.Machine.Ubr, snapshot.Machine.EditionId));
        Assert.Equal(snapshot.Items.Count, snapshot.Items.Select(i => i.Key.ToLowerInvariant()).Distinct().Count());
        // Kanonische Schreibweise wie die synthetischen Fixtures (Rundreise ohne Änderung)
        Assert.Equal(json.Replace("\r\n", "\n"), RiegelJson.Serialize(snapshot));
    }

    [Fact]
    public void Nichtadmin_unterscheidet_sich_nur_in_den_verweigerten_Eintraegen()
    {
        // research-technik §1.2/§3/§4.3: ohne Admin fehlen Benutzerrechte, Überwachungsrichtlinie, secedit, BitLocker,
        // TPM, gpresult, LSASS-Laufzeit und Defender-Ausschlüsse; alles andere ist gleich
        var services = RiegelServices.CreateDefault(new NullPlatform());
        var admin = services.Snapshots.Read(OracleData.SnapshotPath("admin"), ReadLimits.Default);
        var user = services.Snapshots.Read(OracleData.SnapshotPath("nonadmin"), ReadLimits.Default);
        string[] withheld =
        [
            ItemKeys.LsaRights, ItemKeys.AuditPolicy, ItemKeys.RawSeceditInf, ItemKeys.RawGpresultXml, ItemKeys.LsassProtection,
            @"wmi:root\cimv2\Security\MicrosoftVolumeEncryption:Win32_EncryptableVolume", @"wmi:root\cimv2\Security\MicrosoftTpm:Win32_Tpm",
        ];
        foreach (var key in withheld)
        {
            var item = Assert.Single(user.Items, i => ItemKeys.Same(i.Key, key));
            Assert.True(item.Status is ItemStatus.Denied or ItemStatus.Error, $"{key}: {item.Status}");
            Assert.Null(item.Value);
            // Als Admin: Rohtexte nicht nachgebildet (die Auszüge stehen in parsed:*), alles andere gemessen
            var expected = key.StartsWith("raw:", StringComparison.Ordinal) ? ItemStatus.NotCollected : ItemStatus.Ok;
            Assert.Equal(expected, Assert.Single(admin.Items, i => ItemKeys.Same(i.Key, key)).Status);
        }
        Assert.DoesNotContain(user.Items, i => i.Key.StartsWith("parsed:", StringComparison.Ordinal));
        var preference = Assert.Single(user.Items, i => i.Key.EndsWith(":MSFT_MpPreference", StringComparison.Ordinal));
        Assert.DoesNotContain("Exclusion", preference.Value!.Value.GetRawText(), StringComparison.Ordinal);

        // Alles Übrige ist Zeichen für Zeichen gleich
        using var adminDoc = JsonDocument.Parse(File.ReadAllBytes(OracleData.SnapshotPath("admin")));
        using var userDoc = JsonDocument.Parse(File.ReadAllBytes(OracleData.SnapshotPath("nonadmin")));
        List<string> Rest(JsonDocument d) => d.RootElement.GetProperty("items").EnumerateArray()
            .Where(i => !Withheld(i.GetProperty("key").GetString()!, withheld)).Select(i => i.GetRawText()).ToList();
        Assert.Equal(Rest(adminDoc), Rest(userDoc));
        foreach (var part in new[] { "coverage", "machine", "options", "origin" })
            Assert.Equal(adminDoc.RootElement.GetProperty(part).GetRawText(), userDoc.RootElement.GetProperty(part).GetRawText());
    }

    private static bool Withheld(string key, string[] withheld) =>
        withheld.Any(w => ItemKeys.Same(w, key)) || key.StartsWith("parsed:", StringComparison.Ordinal) || key.EndsWith(":MSFT_MpPreference", StringComparison.Ordinal);

    [Fact]
    public void Erwartungen_decken_genau_die_153_Regeln_ab_und_sind_begruendet()
    {
        Assert.Equal(153, OracleData.SpecRuleIds.Length);
        Assert.Equal(153, OracleData.SpecRuleIds.Distinct().Count());
        List<string> compared;
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(OracleData.ExpectedPath)))
        {
            var root = doc.RootElement;
            compared = root.GetProperty("comparedFlags").EnumerateArray().Select(f => f.GetString()!).ToList();
            Assert.Equal("2026.10", root.GetProperty("catalogue").GetProperty("version").GetString());
            Assert.Equal("2026-10-18", root.GetProperty("evaluationDate").GetString());
            foreach (var role in OracleData.Roles)
                Assert.Equal(Path.GetFileName(OracleData.SnapshotPath(role)),
                    root.GetProperty("snapshots").GetProperty(role).GetProperty("file").GetString());
        }
        foreach (var flag in compared) OracleData.ParseFlag(flag);
        Assert.All(OracleData.GlobalAbsentFlags(), f => Assert.Contains(f, compared));

        foreach (var role in OracleData.Roles)
        {
            var expectations = OracleData.Expectations(role);
            Assert.Equal(OracleData.SpecRuleIds, expectations.Select(x => x.RuleId).ToArray());
            foreach (var x in expectations)
            {
                Assert.Contains(x.Status, OracleData.Statuses);
                Assert.True(x.Why.Length >= 20 && !x.Why.Contains('\n'), $"{role} {x.RuleId}: einzeilige Begründung fehlt");
                Assert.True(x.IsAny || x.Allowed.Count == 0, $"{role} {x.RuleId}: allowed nur bei any");
                foreach (var allowed in x.Allowed) OracleData.ParseStatus(allowed);
                Assert.True(!x.IsAny || x.Allowed.Count != 1, $"{role} {x.RuleId}: any mit nur einem Status – dann den Status eintragen");
                // Nur Flags der Auswertung (comparedFlags); Register- und Vergleichsflags setzt die Engine nicht
                foreach (var flag in x.Flags.Concat(x.AbsentFlags)) Assert.Contains(flag, compared);
                Assert.Empty(x.Flags.Intersect(x.AbsentFlags));
                Assert.True(!x.ResearchGap || x.Status == "notCheckable" || x.IsAny, $"{role} {x.RuleId}: researchGap nur bei notCheckable/any");
            }
            // „any“ unter 20 % (Auftrag des Orakels)
            var any = expectations.Count(x => x.IsAny);
            Assert.True(any * 5 < expectations.Count, $"{role}: {any} von {expectations.Count} Erwartungen sind „any“");
        }
    }

    [Fact]
    public void Erwartungen_enthalten_die_Beispiele_aus_spec_8_4()
    {
        var admin = OracleData.Expectations("admin").ToDictionary(x => x.RuleId);
        var user = OracleData.Expectations("nonadmin").ToDictionary(x => x.RuleId);
        (string Rule, string Status)[] spec84 =
        [
            ("RDP-01", "fail"), ("CRED-18", "fail"), ("NET-08", "pass"), ("CRED-07", "fail"), ("CRED-08", "pass"), ("CRED-06", "pass"),
            ("ENC-01", "fail"), ("ACCT-01", "fail"), ("ACCT-14", "notApplicable"), ("DEF-09", "fail"),
            ("ASR-01", "fail"), ("ASR-02", "fail"), ("ASR-08", "fail"), ("ASR-09", "fail"), ("ASR-13", "fail"), ("ASR-15", "fail"),
        ];
        foreach (var (rule, status) in spec84) Assert.Equal((rule, status), (rule, admin[rule].Status));
        Assert.Contains("fromDefault", admin["NET-08"].AbsentFlags);
        // LSASS-Schutz ohne Admin: Laufzeit verweigert, Ereignis 12 beweist ihn (spec 8.4)
        Assert.Equal("pass", user["CRED-06"].Status);
    }

    [Fact]
    public void Exporte_enthalten_keine_echten_Namen_oder_SIDs()
    {
        var allowedSid = PlaceholderSid();
        foreach (var file in Directory.EnumerateFiles(OracleData.Folder))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in MachineSid().Matches(text))
                Assert.True(allowedSid.IsMatch(m.Value), $"{Path.GetFileName(file)}: SID {m.Value} ist kein Platzhalter S-1-5-21-1000-2000-3000-<rid>");
        }
        foreach (var role in OracleData.Roles)
        {
            var snapshot = RiegelJson.Deserialize<Snapshot>(File.ReadAllBytes(OracleData.SnapshotPath(role)));
            Assert.Null(snapshot.Machine.MachineGuid);
            Assert.Null(snapshot.Machine.DnsDomain);
            // Kontonamen: eingebaute Konten der Recherche oder Platzhalter
            string[] names = ["Administrator", "Guest", "DefaultAccount", "WDAGUtilityAccount", "local-user-1001", "local-user-1003"];
            var accounts = snapshot.Items.Single(i => i.Key == @"wmi:root\cimv2:Win32_UserAccount").Value!.Value.GetProperty("rows");
            foreach (var row in accounts.EnumerateArray()) Assert.Contains(row.GetProperty("Name").GetString(), names);
            var admins = snapshot.Items.Single(i => i.Key == ItemKeys.LocalAdministrators).Value!.Value;
            foreach (var member in admins.EnumerateArray())
            {
                Assert.Contains(member.GetProperty("name").GetString(), names);
                Assert.Equal("ORACLE-PC", member.GetProperty("domain").GetString());
            }
            // Pfade nur als Platzhalter (Defender-Ausschlüsse)
            Assert.DoesNotContain(@"\\Users\\", File.ReadAllText(OracleData.SnapshotPath(role)), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Vergleichsskript_ruft_nur_check_auf_und_schreibt_nichts()
    {
        var script = File.ReadAllText(OracleData.ScriptPath, Encoding.UTF8);
        // Der einzige Aufruf von Riegel: check mit Export, JSON-Ausgabe, ohne Ablage
        Assert.Matches(@"&\s*\$cli\s+check\s+--snapshot\s+\$snapshotFull\s+--format\s+json\s+--no-store", script);
        // Code ohne Kommentare und Hilfetext prüfen
        var code = Regex.Replace(Regex.Replace(script, @"<#.*?#>", "", RegexOptions.Singleline), @"(?m)#.*$", "");
        string[] forbidden =
        [
            @"\bSet-[A-Za-z]", @"\bNew-Item\b", @"\bRemove-Item\b", @"\bCopy-Item\b", @"\bMove-Item\b", @"\bRename-Item\b",
            @"\bOut-File\b", @"\bAdd-Content\b", @"\bClear-Content\b", @"\bExport-[A-Za-z]", @"\bStart-Process\b", @"\bInvoke-Expression\b",
            @"\bregedit\b", @"\breg(\.exe)?\s+(add|delete|import|copy|save|restore|load)\b", @"\bsecedit\b", @"\bauditpol\b", @"\bgpupdate\b",
            @"\bnetsh\b", @"\brunas\b", @"--this-pc", @"\bcollect\b", @"WriteAll", @"\[System\.IO\.File\]::(Write|Append|Create|Delete|Copy|Move)",
            @"StreamWriter", @"FileMode\]::Create", @"\bmsiexec\b",
        ];
        foreach (var pattern in forbidden)
            Assert.False(Regex.IsMatch(code, pattern, RegexOptions.IgnoreCase), $"Compare-LocalRecording.ps1 enthält „{pattern}“");
        // Export außerhalb des Repos verlangt
        Assert.Contains("außerhalb des Repos", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Vergleich_findet_jede_Art_von_Abweichung()
    {
        // Der Vergleich selbst läuft schon vor dem Integrationsgate: mit Ergebnissen, die genau den Erwartungen entsprechen
        var expectations = OracleData.Expectations("admin");
        var global = OracleData.GlobalAbsentFlags();
        var matching = expectations.Select(x => Result(x.RuleId, x.IsAny ? (x.Allowed.Count > 0 ? x.Allowed[0] : "pass") : x.Status,
            x.Flags.Select(OracleData.ParseFlag).Aggregate(ResultFlags.None, (a, b) => a | b))).ToList();
        Assert.Empty(OracleComparer.Differences(expectations, matching, _ => true, global));

        List<RuleResult> Changed(string id, string status, ResultFlags flags) =>
            matching.Select(r => r.RuleId == id ? Result(id, status, flags) : r).ToList();
        int Count(List<RuleResult> results) => OracleComparer.Differences(expectations, results, _ => true, global).Count;
        Assert.Equal(1, Count(Changed("RDP-01", "pass", ResultFlags.None)));                 // anderer Status
        Assert.Equal(1, Count(Changed("TLS-03", "pass", ResultFlags.None)));                 // any, aber nicht erlaubt
        Assert.Equal(0, Count(Changed("ACCT-06", "fail", ResultFlags.None)));                // any, erlaubt
        Assert.Equal(1, Count(Changed("NET-08", "pass", ResultFlags.FromDefault)));          // verbotenes Flag
        Assert.Equal(1, Count(Changed("ACCT-01", "fail", ResultFlags.StaleSnapshot)));       // Flag, das kein Ergebnis tragen darf
        Assert.Equal(1, Count(matching.Where(r => r.RuleId != "LIFE-01").ToList()));         // kein Ergebnis
        Assert.Single(OracleComparer.Differences(expectations, matching, id => id != "LIFE-01", global)); // fehlt im Katalog
        // Ergebnisse anderer Regelwerke (importierte Baselines) zählen nicht
        Assert.Equal(0, Count([.. matching, Result("IMP-x-1", "fail", ResultFlags.None) with { RuleSetId = "x" }]));
    }

    private static RuleResult Result(string id, string status, ResultFlags flags) =>
        new("riegel", id, OracleData.ParseStatus(status), Severity.Medium, null, null, ValueSource.Unknown, null, [], null, null, null, flags, null, null);

    [OracleFact]
    public void Admin_Export_ergibt_die_erwarteten_Ergebnisse() => AssertMatchesExpectations("admin");

    [OracleFact]
    public void Nichtadmin_Export_ergibt_die_erwarteten_Ergebnisse() => AssertMatchesExpectations("nonadmin");

    /// <summary>Wertet den Export mit dem eingebetteten Katalog am 2026-10-18 aus und vergleicht Status und Flags je Regel.</summary>
    private static void AssertMatchesExpectations(string role)
    {
        var services = RiegelServices.CreateDefault(new NullPlatform());
        var snapshot = services.Snapshots.Read(OracleData.SnapshotPath(role), ReadLimits.Default);
        var catalogue = services.Catalogue.LoadEmbedded();
        var results = services.Evaluator.Evaluate(snapshot, catalogue, OracleData.Context);
        var differences = OracleComparer.Differences(OracleData.Expectations(role), results, id => catalogue.Find(id) is not null,
            OracleData.GlobalAbsentFlags());
        Assert.True(differences.Count == 0,
            $"Orakel {role}: {differences.Count} Abweichung(en) – Erwartungen ändern nur Autor oder Prüfung der Integration (README.md):\n  " +
            string.Join("\n  ", differences));
    }

    [GeneratedRegex(@"S-1-5-21-\d+-\d+-\d+(-\d+)?")]
    private static partial Regex MachineSid();

    [GeneratedRegex(@"^S-1-5-21-1000-2000-3000(-\d+)?$")]
    private static partial Regex PlaceholderSid();
}
