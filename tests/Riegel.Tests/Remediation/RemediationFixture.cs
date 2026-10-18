// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Samples;
using Riegel.Tests.Reports.Tabular;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Remediation;

/// <summary>
/// Testkatalog mit Abhilfe-Angaben, Lauf und Register zur feindlichen Fixture
/// <c>Remediation/Fixtures/hostile-registry.snapshot.json</c> (erzeugt mit <c>generate-hostile.py</c>).
/// </summary>
internal static class RemediationFixture
{
    public const string Host = "HMI-FEIND-01";
    public const string Lanman = @"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters";
    public const string Winlogon = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
    public const string SystemPolicies = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    public const string Lsa = @"HKLM\SYSTEM\CurrentControlSet\Control\Lsa";
    public const string PoliciesTest = @"HKLM\SOFTWARE\Policies\Riegel-Test";
    public const string DnsClient = @"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient";

    /// <summary>Wertnamen der weggelassenen Regeln: dürfen in keiner Datei außer dem README stehen.</summary>
    public static IReadOnlyList<string> ExcludedValueNames { get; } =
        ["ExecutionPolicy", "LocalAccountTokenFilterPolicy", "SpynetReporting", "UseTPMPIN", "UserAuthentication", "NoDriveTypeAutoRun"];

    public static string FixturePath => Path.Combine(RepoPaths.TestsProject, "Remediation", "Fixtures", "hostile-registry.snapshot.json");

    public static Snapshot Snapshot() => RiegelJson.Deserialize<Snapshot>(File.ReadAllBytes(FixturePath));

    private static RegistryAction Set(string key, string value, RegValueType type, object data) =>
        new(key, value, type, JsonSerializer.SerializeToElement(data));

    private static RegistryAction Delete(string key, string value) => new(key, value, null, null, Delete: true);

    private static Rule Rule(string id, string category, RemediationSpec spec, string? otNote = null)
    {
        var b = RuleBuilder.Check(id, category).Title($"Test rule {id}", $"Testregel {id}").Remediation(spec);
        if (otNote is not null) b.OtNote(otNote, otNote + " (de)");
        return b.Build();
    }

    public static MiniCatalogue Catalogue() => new(
    [
        Rule("NET-08", "network", new([Set(Lanman, "RequireSecuritySignature", RegValueType.Dword, 1)], null, null, null),
            otNote: "Old engineering tools may fail with SMB signing"),
        Rule("NET-09", "network", new([Set(Lanman, "EnableSecuritySignature", RegValueType.Dword, 1)], null, null, null)),
        Rule("LOGON-10", "logon", new([Set(Winlogon, "AutoAdminLogon", RegValueType.Sz, "0"), Delete(Winlogon, "DefaultPassword")], null, null, null)),
        Rule("LOGON-11", "logon", new([Set(Winlogon, "LegalNoticeCaption", RegValueType.Sz, "Nur für Befugte \"Werk 3\" C:\\Pfad\\")], null, null, null)),
        Rule("LOGON-12", "logon", new(
        [
            Set(Winlogon, "LegalNoticeText", RegValueType.Sz, "Hinweis"), Set(Winlogon, "Shell", RegValueType.Sz, "explorer.exe"),
            Set(Winlogon, "Userinit", RegValueType.Sz, "C:\\Windows\\system32\\userinit.exe,"), Set(Winlogon, "ShutdownWithoutLogon", RegValueType.Dword, 0),
            Set(Winlogon, "PowerdownAfterShutdown", RegValueType.Sz, "1"),
        ], null, null, null)),
        Rule("UAC-01", "accounts", new(
        [
            Set(SystemPolicies, "EnableLUA", RegValueType.Dword, 1), Set(SystemPolicies, "ConsentPromptBehaviorAdmin", RegValueType.Qword, 2),
            Set(SystemPolicies, "LegalNotice", RegValueType.ExpandSz, "%SystemRoot%\\notice.txt"),
            Set(SystemPolicies, "DisableCAD", RegValueType.Binary, "AAE="), Set(SystemPolicies, "NoConnectedUser", RegValueType.Dword, 3),
        ], null, null, null)),
        Rule("CRED-01", "credentials", new(
        [
            Set(Lsa, "Security Packages", RegValueType.MultiSz, new[] { "kerberos", "msv1_0" }),
            Set(Lsa, "Notification Packages", RegValueType.MultiSz, new[] { "scecli" }),
            Set(Lsa, "Authentication Packages", RegValueType.MultiSz, new[] { "msv1_0" }),
            Set(Lsa, "LmCompatibilityLevel", RegValueType.Dword, 5),
        ], null, null, null)),
        Rule("NET-03", "network", new([Set(DnsClient, "EnableMulticast", RegValueType.Dword, 0)], null, null, null)),
        Rule("TEST-01", "system", new([Set(PoliciesTest + @"\Gesperrt", "Value", RegValueType.Dword, 1), Delete(PoliciesTest, "Existing"),
            Set(PoliciesTest, "", RegValueType.Sz, "Standardwert")], null, null, null)),
        // Unzulässige Katalog-Einträge: nichts davon darf in eine Datei
        Rule("BAD-01", "system", new(
        [
            Set(@"HKCU\Software\Riegel-Test", "X", RegValueType.Dword, 1),
            Set(@"HKLM\SOFTWARE\Policies\a]b", "X", RegValueType.Dword, 1),
            Set(PoliciesTest, "zeile\r\numbruch", RegValueType.Dword, 1),
            Set(PoliciesTest, "BadText", RegValueType.Sz, "a\rb"),
            Set(PoliciesTest, "BadDword", RegValueType.Dword, "abc"),
            Set(PoliciesTest, "BadMulti", RegValueType.MultiSz, new[] { "ok", "" }),
            new RegistryAction(PoliciesTest, "NoType", null, JsonSerializer.SerializeToElement(1)),
        ], null, null, null)),
        Rule("ACCT-01", "accounts", new(null,
        [
            new TemplateAction("System Access", "MinimumPasswordLength", "14", null),
            new TemplateAction("Registry Values", @"MACHINE\System\CurrentControlSet\Control\Lsa\NoLMHash", "4,1", null),
            new TemplateAction("Privilege Rights", "SeDebugPrivilege", null, ["S-1-5-32-544"]),
            new TemplateAction("Privilege Rights", "SeDenyNetworkLogonRight", null, ["*-501", "*S-1-5-32-546"]),
            new TemplateAction("Privilege Rights", "SeTrustedCredManAccessPrivilege", null, []),
            new TemplateAction("File Security", "%SystemRoot%", "2,\"D:P\"", null),
            new TemplateAction("System Access", "Min;Len", "1", null),
            new TemplateAction("System Access", "LockoutBadCount", "1\r\n[Evil]", null),
            new TemplateAction("Registry Values", @"MACHINE\System\X\Y", "4,1;evil", null),
            new TemplateAction("Privilege Rights", "SeBackupPrivilege", null, ["Administratoren"]),
        ], null, null)),
        Rule("AUD-01", "audit", new(null, null,
        [
            new AuditAction("{0CCE923F-69AE-11D9-BED3-505054503030}", "Credential Validation", AuditSetting.SuccessAndFailure),
            new AuditAction("{0cce9215-69ae-11d9-bed3-505054503030}", "Logon", AuditSetting.Success),
            new AuditAction("{0CCE9217-69AE-11D9-BED3-505054503030}", "Account Lockout", AuditSetting.Failure),
            new AuditAction("{0CCE9226-69AE-11D9-BED3-505054503030}", "Filtering Platform Connection", AuditSetting.None),
            new AuditAction("{kaputt}", "Kaputt", AuditSetting.Success),
            new AuditAction("{0CCE9213-69AE-11D9-BED3-505054503030}", "Böse,\"Name\"", AuditSetting.Success),
        ], null)),
        Rule("SVC-01", "services", new(null, null, null, LocalizedText.Of(("en", "Disable the Print Spooler service."), ("de", "Den Dienst Druckwarteschlange deaktivieren.")))),
        // Weggelassene Regeln
        Rule("PS-02", "powershell", new([Set(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\PowerShell", "ExecutionPolicy", RegValueType.Sz, "AllSigned")], null, null, null)),
        Rule("CRED-18", "credentials", new([Set(SystemPolicies, "LocalAccountTokenFilterPolicy", RegValueType.Dword, 0)], null, null, null)),
        Rule("DEF-03", "defender", new([Set(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "SpynetReporting", RegValueType.Dword, 2)], null, null, null)),
        Rule("ENC-04", "encryption", new([Set(@"HKLM\SOFTWARE\Policies\Microsoft\FVE", "UseTPMPIN", RegValueType.Dword, 1)], null, null, null)),
        Rule("RDP-01", "rdp", new([Set(@"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services", "UserAuthentication", RegValueType.Dword, 1)], null, null, null)),
        Rule("NET-01", "network", new([Set(Lanman, "SMB1", RegValueType.Dword, 0)], null, null, null)),
        Rule("MED-01", "media", new([Set(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun", RegValueType.Dword, 255)], null, null, null)),
        Rule("NOREM-01", "system", new(null, null, null, null)),
    ]);

    private static RuleResult Result(string rule, ResultStatus status, ResultFlags flags = ResultFlags.None, string? exception = null,
        string? tailoring = null, string? measure = null, string? actual = "0") =>
        new("riegel", rule, status, Severity.Medium, "1", actual, ValueSource.LocalPolicy, null, [], exception, tailoring, measure, flags, null, null);

    public static Run Run()
    {
        var results = new List<RuleResult>
        {
            Result("NET-08", ResultStatus.Fail, ResultFlags.MeasurePlanned, measure: "M-0001"),
            Result("NET-09", ResultStatus.Fail),
            Result("LOGON-10", ResultStatus.Fail),
            Result("LOGON-11", ResultStatus.Fail),
            Result("LOGON-12", ResultStatus.Fail),
            Result("UAC-01", ResultStatus.Fail),
            Result("CRED-01", ResultStatus.Fail),
            Result("NET-03", ResultStatus.Fail, actual: null),
            Result("TEST-01", ResultStatus.Fail),
            Result("BAD-01", ResultStatus.Fail),
            Result("ACCT-01", ResultStatus.Fail, actual: "7"),
            Result("AUD-01", ResultStatus.Fail, actual: "noAuditing"),
            Result("SVC-01", ResultStatus.Fail, actual: "Running"),
            Result("PS-02", ResultStatus.Exception, exception: "EX-0007"),
            Result("CRED-18", ResultStatus.Fail, ResultFlags.ExceptionPending, exception: "EX-0002"),
            Result("DEF-03", ResultStatus.OutOfScope, tailoring: "TL-0001"),
            Result("ENC-04", ResultStatus.Fail, ResultFlags.TailoringPending, tailoring: "TL-0002"),
            Result("RDP-01", ResultStatus.Fail),
            Result("NET-01", ResultStatus.Fail, ResultFlags.ExceptionExpired, exception: "EX-0005"),
            Result("MED-01", ResultStatus.Pass, actual: "255"),
            Result("NOREM-01", ResultStatus.Fail),
            Result("UNBEKANNT-01", ResultStatus.Fail),
        };
        return SampleData.Run(SampleData.Hmi) with { HostName = Host, Id = "20261018T090000Z-fe1d01", Results = results, EvaluationDate = new DateOnly(2026, 10, 18) };
    }

    /// <summary>Register: eine gültige, nur im Register stehende Ausnahme für RDP-01 auf diesem Rechner und eine Maßnahme.</summary>
    public static RegisterSet Registers()
    {
        var rdp = new ExceptionEntry("EX-0010", "RDP-01", new RegisterScope([Host], [], false), ReasonCategory.VendorRequirement,
            "Fernwartungsgerät des Herstellers kann keine NLA.", null, "Herstellerdokument HD-4711", "0", Approval.None,
            new DateOnly(2026, 4, 1), "Anlagenverantwortlicher", null, "2026.10", null);
        rdp = rdp with { Approval = new Approval("Informationssicherheitsbeauftragter", null, new DateOnly(2026, 4, 20), new DateOnly(2027, 4, 20), "CHG-DEMO-0010", ContentHashes.Of(rdp)) };
        var registers = SampleData.Registers();
        return registers with
        {
            Exceptions = registers.Exceptions with { Exceptions = [.. registers.Exceptions.Exceptions, rdp] },
            Measures = registers.Measures with
            {
                Measures =
                [
                    new MeasureEntry("M-0001", "NET-08", new RegisterScope([], [], true), "SMB-Signierung im Wartungsfenster einschalten",
                        "Anlagenverantwortlicher", new DateOnly(2026, 12, 15), "TICKET-DEMO-11", MeasureState.Planned, new DateOnly(2026, 9, 1),
                        "Anlagenverantwortlicher", null, null),
                ],
            },
        };
    }
}
