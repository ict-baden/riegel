// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using Riegel.Core.Model;
using Riegel.Core.Remediation;
using Riegel.Core.Resources;
using Riegel.Core.Services;
using Riegel.Tests.Infrastructure;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Remediation;

/// <summary>
/// Abhilfe nur als Dateien (spec 5 O10, 7.12): .reg und Rücknahme mit strenger Grammatik, GptTmpl.inf nach [MS-GPSB],
/// audit.csv nach [MS-GPAC], README; weggelassene Regeln erscheinen nie in den Dateien; feindliche Exporte ergeben
/// sichere Dateien.
/// </summary>
public sealed partial class RemediationTests : IDisposable
{
    private const string H = RemediationFixture.Host;

    private sealed record Output(RemediationOutput Result, string Folder, byte[] Reg, byte[] Rollback, byte[] Inf, byte[] Audit, string Readme);

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private Output Write(string lang = "de")
    {
        var writer = RemediationFactory.Create(RiegelServices.Create(new NullPlatform(), new FixedClock()));
        var result = writer.Write(RemediationFixture.Run(), RemediationFixture.Snapshot(), RemediationFixture.Catalogue(), RemediationFixture.Registers(),
            Path.Combine(_temp.Path, lang), lang);
        byte[] F(string name) => File.ReadAllBytes(Path.Combine(result.Directory, name));
        return new Output(result, result.Directory, F("riegel-remediation.reg"), F("riegel-rollback.reg"), F("GptTmpl.inf"), F("audit.csv"),
            new UTF8Encoding(false, true).GetString(F("README.txt")));
    }

    private static string Header(string lang) =>
        CommonStrings.ResourceManager.GetString("Remediation_Header", CultureInfo.GetCultureInfo(lang))!;

    private static string T(string key, string lang = "de") =>
        DataOutputStrings.ResourceManager.GetString(key, CultureInfo.GetCultureInfo(lang))!;

    private static Dictionary<(string, string), RegEntry> Map(IEnumerable<RegEntry> entries) =>
        entries.ToDictionary(e => (e.Key, e.Name.ToLowerInvariant()));

    private static string K(string hklm) => @"HKEY_LOCAL_MACHINE\" + hklm[5..];

    [Fact]
    public void Fixture_ist_ein_gueltiger_synthetischer_Export()
    {
        SchemaValidator.AssertValid(File.ReadAllText(RemediationFixture.FixturePath), "hostile-registry.snapshot.json");
        Assert.Equal(SnapshotOrigin.Synthetic, RemediationFixture.Snapshot().Origin);
    }

    [Fact]
    public void Genau_die_fuenf_Dateien_im_Ordner_des_Laufs_ohne_Skript()
    {
        var o = Write();
        Assert.EndsWith(Path.Combine("remediation", H, "20261018T090000Z-fe1d01"), o.Folder, StringComparison.Ordinal);
        Assert.Equal(new[] { "GptTmpl.inf", "README.txt", "audit.csv", "riegel-remediation.reg", "riegel-rollback.reg" },
            Directory.EnumerateFiles(o.Folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(5, o.Result.Files.Count);
        Assert.DoesNotContain(Directory.EnumerateFiles(o.Folder, "*", SearchOption.AllDirectories), f => f.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("zh-Hans")]
    public void Jede_Datei_beginnt_mit_dem_Pflichthinweis_in_der_Berichtssprache(string lang)
    {
        var o = Write(lang);
        var header = Header(lang);
        RegFileValidator.Validate(o.Reg, header);
        RegFileValidator.Validate(o.Rollback, header);
        var inf = Encoding.Unicode.GetString(o.Inf, 2, o.Inf.Length - 2);
        Assert.StartsWith("; " + header + "\r\n", inf, StringComparison.Ordinal);
        Assert.StartsWith("# " + header + "\r\n", Encoding.UTF8.GetString(o.Audit), StringComparison.Ordinal);
        Assert.StartsWith(header + "\r\n", o.Readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Abhilfe_reg_setzt_die_Werte_aus_dem_Katalog()
    {
        var o = Write();
        var reg = Map(RegFileValidator.Validate(o.Reg, Header("de")));
        Assert.Equal(1u, reg[(K(RemediationFixture.Lanman), "requiresecuritysignature")].Data);
        Assert.Equal("0", reg[(K(RemediationFixture.Winlogon), "autoadminlogon")].Data);
        Assert.Equal("delete", reg[(K(RemediationFixture.Winlogon), "defaultpassword")].Kind);
        // Anführungszeichen und Backslash korrekt maskiert und zurückgelesen
        Assert.Equal("Nur für Befugte \"Werk 3\" C:\\Pfad\\", reg[(K(RemediationFixture.Winlogon), "legalnoticecaption")].Data);
        Assert.Contains("\"LegalNoticeCaption\"=\"Nur für Befugte \\\"Werk 3\\\" C:\\\\Pfad\\\\\"", Encoding.Unicode.GetString(o.Reg), StringComparison.Ordinal);
        // Typen: qword, expand_sz als hex(2), binary, multi_sz als hex(7)
        var sys = K(RemediationFixture.SystemPolicies);
        Assert.Equal(2ul, reg[(sys, "consentpromptbehavioradmin")].Data);
        Assert.Equal("expand_sz", reg[(sys, "legalnotice")].Kind);
        Assert.Equal("%SystemRoot%\\notice.txt", reg[(sys, "legalnotice")].Data);
        Assert.Equal(new byte[] { 0x00, 0x01 }, reg[(sys, "disablecad")].Data);
        Assert.Equal(new[] { "kerberos", "msv1_0" }, (List<string>)reg[(K(RemediationFixture.Lsa), "security packages")].Data!);
        // Standardwert als @
        Assert.Equal("Standardwert", reg[(K(RemediationFixture.PoliciesTest), "")].Data);
        Assert.Contains("\r\n@=\"Standardwert\"\r\n", Encoding.Unicode.GetString(o.Reg), StringComparison.Ordinal);
        Assert.Equal("delete", reg[(K(RemediationFixture.PoliciesTest), "existing")].Kind);
        Assert.Contains("NET-08", o.Result.IncludedRuleIds);
        Assert.Contains("TEST-01", o.Result.IncludedRuleIds);
    }

    [Fact]
    public void Ruecknahme_stellt_die_typgepruefen_alten_Werte_wieder_her()
    {
        var o = Write();
        var rollback = Map(RegFileValidator.Validate(o.Rollback, Header("de")));
        var lanman = K(RemediationFixture.Lanman);
        var winlogon = K(RemediationFixture.Winlogon);
        var sys = K(RemediationFixture.SystemPolicies);
        var lsa = K(RemediationFixture.Lsa);
        Assert.Equal(0u, rollback[(lanman, "requiresecuritysignature")].Data);
        // $() und Backslash/Anführungszeichen bleiben reine Daten und kommen exakt zurück
        Assert.Equal("C:\\Temp\\\"q\" $(Invoke-Expression 'calc') `whoami` %PATH%", rollback[(winlogon, "legalnoticecaption")].Data);
        Assert.Equal("Grüße – 中文 ✓", rollback[(winlogon, "powerdownaftershutdown")].Data);
        Assert.Equal(0u, rollback[(sys, "enablelua")].Data);
        Assert.Equal(ulong.MaxValue, rollback[(sys, "consentpromptbehavioradmin")].Data);
        Assert.Equal("%SystemRoot%\\system32\\\"x\".txt", rollback[(sys, "legalnotice")].Data);
        Assert.Equal(Convert.FromBase64String("AAECAwQFBgcICQ=="), rollback[(sys, "disablecad")].Data);
        Assert.Equal(new[] { "kerberos", "msv1_0", "schannel", "wdigest", "tspkg", "pku2u" }, (List<string>)rollback[(lsa, "security packages")].Data!);
        Assert.Equal(3u, rollback[(lsa, "lmcompatibilitylevel")].Data);
        // Nachweislich nicht gesetzt (Schlüssel fehlt) → Rücknahme löscht den Wert wieder; ebenso der neue Standardwert
        Assert.Equal("delete", rollback[(K(RemediationFixture.DnsClient), "enablemulticast")].Kind);
        Assert.Equal("delete", rollback[(K(RemediationFixture.PoliciesTest), "")].Kind);
        // Gelöschter Wert kommt mit altem Inhalt zurück
        Assert.Equal(7u, rollback[(K(RemediationFixture.PoliciesTest), "existing")].Data);
    }

    [Theory]
    [InlineData(RemediationFixture.Lanman, "EnableSecuritySignature", "OmitReason_WrongType")]
    [InlineData(RemediationFixture.Winlogon, "AutoAdminLogon", "OmitReason_ControlCharacters")]
    [InlineData(RemediationFixture.Winlogon, "LegalNoticeText", "OmitReason_ControlCharacters")]
    [InlineData(RemediationFixture.Winlogon, "ShutdownWithoutLogon", "OmitReason_ControlCharacters")]
    [InlineData(RemediationFixture.Winlogon, "Shell", "OmitReason_TooLong")]
    [InlineData(RemediationFixture.Winlogon, "Userinit", "OmitReason_Truncated")]
    [InlineData(RemediationFixture.Winlogon, "DefaultPassword", "OmitReason_Redacted")]
    [InlineData(RemediationFixture.SystemPolicies, "NoConnectedUser", "OmitReason_UnsupportedType")]
    [InlineData(RemediationFixture.Lsa, "Notification Packages", "OmitReason_ControlCharacters")]
    [InlineData(RemediationFixture.Lsa, "Authentication Packages", "OmitReason_EmptyListEntry")]
    [InlineData(RemediationFixture.PoliciesTest + @"\Gesperrt", "Value", "OmitReason_NotCollected")]
    public void Unsichere_alte_Werte_fehlen_in_der_Ruecknahme_und_stehen_im_README(string key, string value, string reason)
    {
        var o = Write();
        var rollback = Map(RegFileValidator.Validate(o.Rollback, Header("de")));
        Assert.False(rollback.ContainsKey((K(key), value.ToLowerInvariant())), $"{key}\\{value} darf nicht in der Rücknahme stehen");
        // Die Abhilfe selbst steht trotzdem in der .reg (der neue Wert kommt aus dem Katalog)
        Assert.True(Map(RegFileValidator.Validate(o.Reg, Header("de"))).ContainsKey((K(key), value.ToLowerInvariant())));
        var note = T(nameof(DataOutputStrings.Remediation_RollbackOmitted)).Split('{')[0];
        Assert.Contains(o.Readme.Split("\r\n"), l => l.Contains(note, StringComparison.Ordinal) && l.Contains(value, StringComparison.Ordinal)
            && l.Contains(T(reason), StringComparison.Ordinal) && l.Contains("RGL-0570", StringComparison.Ordinal));
    }

    [Fact]
    public void Feindliche_Werte_und_Wertnamen_aus_dem_Export_gelangen_nie_in_eine_reg_Datei()
    {
        var o = Write();
        foreach (var bytes in new[] { o.Reg, o.Rollback })
        {
            var entries = RegFileValidator.Validate(bytes, Header("de"));
            var text = Encoding.Unicode.GetString(bytes);
            Assert.DoesNotContain("SOFTWARE\\Evil", text, StringComparison.Ordinal);
            Assert.DoesNotContain("reg add", text, StringComparison.Ordinal);
            Assert.DoesNotContain("HKEY_CURRENT_USER", text, StringComparison.Ordinal);
            Assert.DoesNotContain("a]b", text, StringComparison.Ordinal);
            Assert.DoesNotContain(entries, e => e.Name is "BadText" or "BadDword" or "BadMulti" or "NoType" or "X");
            Assert.All(entries, e => Assert.DoesNotContain('\0', e.Data as string ?? ""));
        }
        // Schlüssel und Wertnamen stammen nur aus dem Katalog
        var catalogue = RemediationFixture.Catalogue().Rules.SelectMany(r => r.Remediation?.Registry ?? [])
            .Select(a => (K(a.Key.StartsWith("HKLM", StringComparison.Ordinal) ? a.Key : @"HKLM\x"), a.Value.ToLowerInvariant())).ToHashSet();
        foreach (var e in RegFileValidator.Validate(o.Reg, Header("de")).Concat(RegFileValidator.Validate(o.Rollback, Header("de"))))
            Assert.Contains((e.Key, e.Name.ToLowerInvariant()), catalogue);
        // Die unzulässigen Katalog-Einträge sind im README genannt
        Assert.True(o.Readme.Split("\r\n").Count(l => l.Contains("RGL-0569", StringComparison.Ordinal)) >= 7, o.Readme);
    }

    [Fact]
    public void Weggelassene_Regeln_erscheinen_nie_in_den_Dateien()
    {
        var o = Write();
        var files = Encoding.Unicode.GetString(o.Reg) + Encoding.Unicode.GetString(o.Rollback) + Encoding.Unicode.GetString(o.Inf)
                    + Encoding.UTF8.GetString(o.Audit);
        foreach (var name in RemediationFixture.ExcludedValueNames)
            Assert.DoesNotContain(name, files, StringComparison.OrdinalIgnoreCase);
        var excluded = o.Result.Excluded.ToDictionary(e => e.RuleId);
        Assert.Equal(RemediationExclusionReason.ValidException, excluded["PS-02"].Reason);
        Assert.Equal("EX-0007", excluded["PS-02"].EntryId);
        Assert.Equal(RemediationExclusionReason.PendingException, excluded["CRED-18"].Reason);
        Assert.Equal(RemediationExclusionReason.Tailored, excluded["DEF-03"].Reason);
        Assert.Equal(RemediationExclusionReason.Tailored, excluded["ENC-04"].Reason);
        // Nur im Register (Ergebnis ohne Ausnahme-ID): gültige Ausnahme für genau diesen Rechner
        Assert.Equal(RemediationExclusionReason.ValidException, excluded["RDP-01"].Reason);
        Assert.Equal("EX-0010", excluded["RDP-01"].EntryId);
        Assert.Equal(RemediationExclusionReason.NoRemediation, excluded["NOREM-01"].Reason);
        Assert.Equal(RemediationExclusionReason.NoRemediation, excluded["UNBEKANNT-01"].Reason);
        Assert.DoesNotContain("MED-01", excluded.Keys);
        Assert.DoesNotContain("MED-01", o.Result.IncludedRuleIds);
        // Abgelaufene Ausnahme: Vorschlag enthalten, mit Hinweis
        Assert.Contains("NET-01", o.Result.IncludedRuleIds);
        Assert.Contains("SMB1", files, StringComparison.Ordinal);
        Assert.Contains(T(nameof(DataOutputStrings.Remediation_ExceptionExpired)).Replace("{0}", "EX-0005", StringComparison.Ordinal), o.Readme, StringComparison.Ordinal);
        // Das README nennt die weggelassenen Regeln mit Grund
        Assert.Contains("PS-02 – " + T("Exclusion_ValidException").Replace("{0}", "EX-0007", StringComparison.Ordinal), o.Readme, StringComparison.Ordinal);
        Assert.Contains("RDP-01 – " + T("Exclusion_ValidException").Replace("{0}", "EX-0010", StringComparison.Ordinal), o.Readme, StringComparison.Ordinal);
    }

    [Fact]
    public void GptTmpl_inf_nach_MS_GPSB()
    {
        var o = Write();
        Assert.True(o.Inf[0] == 0xFF && o.Inf[1] == 0xFE, "UTF-16 LE mit BOM");
        var text = new UnicodeEncoding(false, true, true).GetString(o.Inf, 2, o.Inf.Length - 2);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        var lines = text.TrimEnd('\r', '\n').Split("\r\n");
        Assert.All(lines, l => Assert.True(l.All(c => !char.IsControl(c)), l));
        var body = lines.SkipWhile(l => l.StartsWith(';')).ToList();
        Assert.Equal(["[Unicode]", "Unicode=yes", "[Version]", "signature=\"$CHICAGO$\"", "Revision=1"], body.Take(5));
        Assert.Contains("[System Access]", body);
        Assert.Contains("MinimumPasswordLength = 14", body);
        Assert.Contains("[Registry Values]", body);
        Assert.Contains(@"MACHINE\System\CurrentControlSet\Control\Lsa\NoLMHash=4,1", body);
        Assert.Contains("[Privilege Rights]", body);
        Assert.Contains("SeDebugPrivilege = *S-1-5-32-544", body);
        // *-501 aufgelöst mit dem SID-Präfix des Rechners aus den lokalen Konten
        Assert.Contains("SeDenyNetworkLogonRight = *S-1-5-21-1000-2000-3000-501,*S-1-5-32-546", body);
        // Unzulässig oder nicht als Datei ausdrückbar
        foreach (var bad in new[] { "SeTrustedCredManAccessPrivilege", "File Security", "Min;Len", "LockoutBadCount", "evil", "SeBackupPrivilege", "Administratoren" })
            Assert.DoesNotContain(bad, string.Join("\n", body), StringComparison.Ordinal);
        // Jede Zeile im Abschnittsteil passt zur Grammatik
        foreach (var line in body.Skip(5))
            Assert.Matches(@"^(\[[A-Za-z ]+\]|[A-Za-z][A-Za-z0-9_]* = (\d+|""[^""]*""|\*S-[0-9-]+(,\*S-[0-9-]+)*)|MACHINE\\[^=;""]+=[12347],[^;]*)$", line);
        Assert.Contains(T(nameof(DataOutputStrings.Remediation_TemplateManual)).Replace("{0}", "[Privilege Rights] SeTrustedCredManAccessPrivilege", StringComparison.Ordinal),
            o.Readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Audit_csv_nach_MS_GPAC_mit_der_Spaltenfolge_von_auditpol_backup()
    {
        var o = Write();
        Assert.False(o.Audit[0] == 0xEF, "kein BOM");
        var text = new UTF8Encoding(false, true).GetString(o.Audit);
        var lines = text.Split("\r\n");
        Assert.Equal("", lines[^1]);
        Assert.Equal("Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value", lines[1]);
        var rows = lines.Skip(2).Where(l => l.Length > 0).ToList();
        Assert.Equal(
        [
            ",System,Credential Validation,{0CCE923F-69AE-11D9-BED3-505054503030},Success and Failure,,3",
            ",System,Logon,{0CCE9215-69AE-11D9-BED3-505054503030},Success,,1",
            ",System,Account Lockout,{0CCE9217-69AE-11D9-BED3-505054503030},Failure,,2",
            ",System,Filtering Platform Connection,{0CCE9226-69AE-11D9-BED3-505054503030},No Auditing,,4",
        ], rows);
        Assert.All(rows, r => Assert.Equal(7, r.Split(',').Length));
        Assert.DoesNotContain("kaputt", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Böse", text, StringComparison.Ordinal);
    }

    [Fact]
    public void README_je_Einstellung_mit_alt_neu_OT_Hinweis_Massnahme_und_manuellen_Schritten()
    {
        var o = Write();
        var readme = o.Readme;
        Assert.Contains("[NET-08] Testregel NET-08", readme, StringComparison.Ordinal);
        Assert.Contains("RequireSecuritySignature: alt 0 (dword) → neu 1 (dword)", readme, StringComparison.Ordinal);
        Assert.Contains("Old engineering tools may fail with SMB signing (de)", readme, StringComparison.Ordinal);
        Assert.Contains("M-0001: SMB-Signierung im Wartungsfenster einschalten – verantwortlich: Anlagenverantwortlicher, fällig 2026-12-15", readme, StringComparison.Ordinal);
        Assert.Contains("Den Dienst Druckwarteschlange deaktivieren.", readme, StringComparison.Ordinal);
        Assert.Contains("SVC-01", o.Result.ManualRuleIds);
        Assert.Contains(T(nameof(DataOutputStrings.Remediation_NoScript)), readme, StringComparison.Ordinal);
        Assert.Contains(T(nameof(DataOutputStrings.Remediation_HowToAudit)), readme, StringComparison.Ordinal);
        // Mehrere Vorlagen- oder Überwachungs-Einträge: der Istwert der Prüfung steht einmal davor
        Assert.Contains("Istwert laut Prüfung: 7", readme, StringComparison.Ordinal);
        Assert.Contains("Sicherheitsvorlage [System Access] MinimumPasswordLength: neu 14", readme, StringComparison.Ordinal);
        Assert.Contains("Istwert laut Prüfung: noAuditing", readme, StringComparison.Ordinal);
        Assert.Contains("Überwachungsrichtlinie Credential Validation {0CCE923F-69AE-11D9-BED3-505054503030}: neu successAndFailure", readme, StringComparison.Ordinal);
        // Steuerzeichen aus dem Export erscheinen im README nicht roh
        Assert.All(readme.Split("\r\n"), l => Assert.True(l.All(c => !char.IsControl(c)), l));
    }

    [Fact]
    public void Ohne_offene_Befunde_entstehen_leere_aber_gueltige_Dateien()
    {
        using var temp = new TempFolder();
        var run = RemediationFixture.Run() with { Results = [] };
        var result = RemediationFactory.Create(RiegelServices.Create(new NullPlatform(), new FixedClock()))
            .Write(run, RemediationFixture.Snapshot(), RemediationFixture.Catalogue(), RemediationFixture.Registers(), temp.Path, "en");
        Assert.Empty(RegFileValidator.Validate(File.ReadAllBytes(Path.Combine(result.Directory, "riegel-remediation.reg")), Header("en")));
        Assert.Empty(result.IncludedRuleIds);
        Assert.Contains(T(nameof(DataOutputStrings.Remediation_NoSettings), "en"), File.ReadAllText(Path.Combine(result.Directory, "README.txt")), StringComparison.Ordinal);
    }

    [Fact]
    public void Nichts_startet_einen_Prozess_oder_schreibt_in_die_Registry()
    {
        foreach (var folder in new[] { "Remediation", "Evidence", "Reports/Tabular" })
            foreach (var (file, code) in SourceScanTests.Sources("Riegel.Core/" + folder))
                Assert.False(Forbidden().IsMatch(code), $"{Path.GetFileName(file)}: {Forbidden().Match(code).Value}");
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\bProcess\b|ProcessStartInfo|Microsoft\.Win32|\bRegistryKey\b|Registry\.(LocalMachine|CurrentUser|SetValue|GetValue)|\.ps1""")]
    private static partial System.Text.RegularExpressions.Regex Forbidden();
}
