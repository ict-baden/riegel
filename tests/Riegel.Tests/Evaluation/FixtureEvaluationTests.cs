// SPDX-License-Identifier: MIT
using Riegel.Core.Evaluation;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Tests.RuleVectors;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Evaluation;

/// <summary>
/// Die synthetischen Fixtures (spec 8.1) gegen Testregeln, die echte Einträge lesen: Domänen- und
/// Arbeitsgruppen-Anwendbarkeit, Domänencontroller, das deutschsprachige System über SIDs, und alte Windows-Versionen
/// ohne falsches „nicht erfüllt“ und ohne falsches „erfüllt“. Jede Erwartung ist aus den Werten der Fixture
/// begründet (siehe Fixtures/synthetic/generate-fixtures.py), nicht aus der Ausgabe der Engine abgeschrieben.
/// </summary>
public sealed class FixtureEvaluationTests
{
    private static readonly Lazy<ICatalogue> Catalogue = new(VectorRunner.TestCatalogue);

    private static Snapshot Fixture(string name) => RiegelJson.ReadFile<Snapshot>(RepoPaths.Synthetic(name), 64L * 1024 * 1024);

    private static RuleResult Eval(string fixture, string ruleId) =>
        new Evaluator(null).Evaluate(Fixture(fixture), Catalogue.Value, VectorRunner.Context, null, [Catalogue.Value.Find(ruleId)!]).Single();

    // Fixture, Regel, Erwartung, Begründung
    public static TheoryData<string, string, ResultStatus, string> Expectations => new()
    {
        // Gast nach RID 501, auch mit deutschem Namen „Gast“; DC ohne lokale Konten
        { "win11-24h2-workgroup-admin", "TACC-01", ResultStatus.Pass, "Guest -501 Disabled=true" },
        { "win11-24h2-workgroup-nonadmin", "TACC-01", ResultStatus.Pass, "Win32_UserAccount ohne Admin lesbar" },
        { "win10-german-locale", "TACC-01", ResultStatus.Pass, "„Gast“ -501 deaktiviert – SID entscheidet" },
        { "server2022-dc", "TACC-01", ResultStatus.NotCheckable, "keine lokalen Konten, netuserenum ohne RID 501" },
        // Benutzerrecht über SIDs: deutsches System liefert die API mit SIDs, secedit mit Namen
        { "win10-german-locale", "TSEC-09", ResultStatus.Pass, "api:lsa-rights SeDebugPrivilege = [S-1-5-32-544]" },
        { "win11-24h2-workgroup-admin", "TSEC-09", ResultStatus.Pass, "api:lsa-rights SeDebugPrivilege = [S-1-5-32-544]" },
        { "win11-24h2-workgroup-nonadmin", "TSEC-09", ResultStatus.NotCheckable, "lsa-rights verweigert, kein secedit ohne Admin" },
        // Nur Domänenmitglieder (wie URA-02/03)
        { "win11-24h2-workgroup-admin", "TSEC-08", ResultStatus.NotApplicable, "Arbeitsgruppe" },
        { "win10-german-locale", "TSEC-08", ResultStatus.NotApplicable, "Arbeitsgruppe" },
        { "server2022-domain-member", "TSEC-08", ResultStatus.Pass, "Mitglied, lokaler Gast -501 im Recht" },
        { "server2022-domain-member", "TCMP-07", ResultStatus.Fail, "Mitglied: Testwert nicht gesetzt (whenMissing fail)" },
        { "win11-24h2-workgroup-admin", "TCMP-07", ResultStatus.NotApplicable, "Arbeitsgruppe" },
        { "server2022-dc", "TCMP-07", ResultStatus.NotApplicable, "excludeDc" },
        { "server2022-dc", "TREG-13", ResultStatus.NotApplicable, "excludeDc" },
        // Laufzeit vor Konfiguration (SMB-Signierung)
        { "win11-24h2-workgroup-admin", "TCMP-01", ResultStatus.Pass, "GetConfiguration RequireSecuritySignature=true" },
        { "win7-sp1", "TCMP-01", ResultStatus.Fail, "SMB-Namespace fehlt, LanmanServer RequireSecuritySignature=0 ausdrücklich gesetzt" },
        { "server2022-domain-member", "TSYS-04", ResultStatus.Pass, "Server: GetConfiguration RequireSecuritySignature=true" },
        { "win7-sp1", "TSYS-04", ResultStatus.NotApplicable, "nur Server" },
        // Dienste und Features
        { "win11-24h2-workgroup-admin", "TSYS-01", ResultStatus.Pass, "RemoteRegistry Disabled" },
        { "server2016-historian", "TSYS-01", ResultStatus.NotApplicable, "nur Client" },
        { "win7-sp1", "TSYS-03", ResultStatus.NotApplicable, "minBuild 9200, Windows 7 ist 7601" },
        { "win10-ltsc2019-hmi", "TSYS-03", ResultStatus.Pass, "SMB1Protocol InstallState 2" },
        { "server2012r2", "TSYS-03", ResultStatus.Pass, "SMB1Protocol InstallState 2" },
        // Defender und Virenschutz auf alten Systemen: kein falsches fail, kein falsches pass
        { "win11-24h2-workgroup-admin", "TDEF-01", ResultStatus.Pass, "Normal, Echtzeitschutz an" },
        { "win11-defender-passive-thirdparty-av", "TDEF-01", ResultStatus.NotApplicable, "passiv, Drittanbieter laut WSC an" },
        { "win7-sp1", "TDEF-01", ResultStatus.NotApplicable, "Defender-Namespace fehlt, Drittanbieter laut SecurityCenter2 (Heuristik) an" },
        { "server2012r2", "TDEF-01", ResultStatus.NotCheckable, "kein Defender, kein Sicherheitscenter: Drittanbieter unbekannt" },
        { "win11-24h2-workgroup-admin", "TDEF-06", ResultStatus.Pass, "Defender aktiv, Signaturen von 2026-10-15" },
        { "win11-defender-passive-thirdparty-av", "TDEF-06", ResultStatus.Pass, "Drittanbieter an und aktuell (WSC)" },
        { "win7-sp1", "TDEF-06", ResultStatus.NotCheckable, "nur SecurityCenter2-Heuristik" },
        { "server2012r2", "TDEF-06", ResultStatus.NotCheckable, "Server ohne Defender und ohne Sicherheitscenter" },
        { "server2016-historian", "TDEF-06", ResultStatus.NotCheckable, "Signaturzeit liegt nach der Erfassung (2026-08-03)" },
        // LSA-Schutz und virtualisierungsbasierte Sicherheit
        { "win11-24h2-workgroup-admin", "TISO-01", ResultStatus.Pass, "Wininit-Ereignis 12 nach dem Start" },
        { "win10-ltsc2019-hmi", "TISO-01", ResultStatus.NotCheckable, "kein Ereignis, RunAsPPL 0 – ohne Laufzeitbeleg nicht prüfbar" },
        { "win7-sp1", "TISO-01", ResultStatus.NotApplicable, "LSA-Schutz erst ab Build 9600 (minBuild der Regel)" },
        { "server2012r2", "TISO-01", ResultStatus.NotCheckable, "Build 9600: kein Ereignis, RunAsPPL 0 – ohne Laufzeitbeleg nicht prüfbar" },
        { "win11-24h2-workgroup-admin", "TISO-02", ResultStatus.Fail, "Credential Guard nicht in SecurityServicesRunning [2]" },
        { "win7-sp1", "TISO-02", ResultStatus.NotApplicable, "Fähigkeit testFeature fehlt (Build 7601)" },
        { "server2012r2", "TISO-02", ResultStatus.NotApplicable, "Fähigkeit testFeature fehlt (Build 9600)" },
        { "win11-24h2-workgroup-admin", "TISO-04", ResultStatus.Pass, "Speicherintegrität läuft [2]" },
        { "win7-sp1", "TISO-04", ResultStatus.Fail, "Schutzfunktion fehlt: Lücke (ifCapabilityAbsent fail)" },
        // Automatische Anmeldung (Falle LOGON-10)
        { "win11-24h2-workgroup-admin", "TREG-12", ResultStatus.Pass, "AutoAdminLogon 0" },
        { "win10-ltsc2019-hmi", "TREG-12", ResultStatus.Fail, "AutoAdminLogon 1" },
        // Firewall: Laufzeit an; Freigaben ohne zerlegte Registry-Regeln nicht prüfbar
        { "win7-sp1", "TNET-01", ResultStatus.Pass, "öffentliches Profil an (COM)" },
        { "win11-24h2-workgroup-admin", "TNET-05", ResultStatus.NotCheckable, "parsed:firewall-rules-registry fehlt (vom Leser erzeugt)" },
        // Überwachungsrichtlinie: ohne Admin nicht prüfbar
        { "win11-24h2-workgroup-admin", "TSEC-06", ResultStatus.Pass, "Logon = 3" },
        { "win11-24h2-workgroup-nonadmin", "TSEC-06", ResultStatus.NotCheckable, "audit-policy Fehler 1314" },
    };

    [Theory]
    [MemberData(nameof(Expectations))]
    public void Fixture_gibt_den_begruendeten_Status(string fixture, string ruleId, ResultStatus expected, string why)
    {
        var result = Eval(fixture, ruleId);
        Assert.True(expected == result.Status, $"{fixture}/{ruleId}: erwartet {expected} ({why}), ist {result.Status} {result.ReasonCode} {string.Join(" | ", result.Details ?? [])}");
    }

    [Fact]
    public void Heuristik_und_Kennwort_Hinweis_stehen_am_Ergebnis()
    {
        Assert.True(Eval("win7-sp1", "TDEF-01").Flags.HasFlag(ResultFlags.Heuristic));
        Assert.True(Eval("win7-sp1", "TDEF-06").Flags.HasFlag(ResultFlags.Heuristic));
        var logon = Eval("win10-ltsc2019-hmi", "TREG-12");
        Assert.Contains(logon.Details!, d => d.Contains("DefaultPassword", StringComparison.Ordinal));
    }

    [Fact]
    public void Jede_Fixture_liefert_fuer_jede_Testregel_ein_Ergebnis_ohne_Fehler()
    {
        foreach (var file in Directory.EnumerateFiles(RepoPaths.SyntheticFixtures, "*.snapshot.json"))
        {
            var snapshot = RiegelJson.ReadFile<Snapshot>(file, 64L * 1024 * 1024);
            var results = new Evaluator(null).Evaluate(snapshot, Catalogue.Value, VectorRunner.Context);
            Assert.Equal(Catalogue.Value.Rules.Count, results.Count);
            Assert.DoesNotContain(results, r => r.ReasonCode == RglCodesEngine.EvaluationFailed);
            Assert.All(results, r => Assert.True(r.Status != ResultStatus.Pass || r.EvidenceKeys.Count > 0 || r.RuleId.StartsWith("TTIM", StringComparison.Ordinal),
                $"{Path.GetFileName(file)} {r.RuleId}: pass ohne Beleg"));
        }
    }
}
