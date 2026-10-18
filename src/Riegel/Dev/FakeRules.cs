// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Resources;
using static Riegel.Dev.FakeView;

namespace Riegel.Dev;

/// <summary>Ergebnis einer nachgebildeten Regel (nur Entwicklung).</summary>
internal sealed record FakeOutcome(ResultStatus Status, string? Expected = null, string? Actual = null,
    ValueSource Source = ValueSource.Unknown, string? SourceDetail = null, ResultFlags Flags = ResultFlags.None,
    IReadOnlyList<string>? Details = null, IReadOnlyList<string>? Evidence = null);

/// <summary>Eine nachgebildete Regel: Katalogdaten und ein einfacher Auswerter.</summary>
internal sealed record FakeRule(string Id, string Category, Severity Severity, RuleKind Kind, (string En, string De) Title,
    (string En, string De) Rationale, (string En, string De)? OtNote, (string En, string De)? Remedy, ReasonCategory? Suggested,
    string[] Os, DomainScope Domain, bool NeedsAdmin, CheckSpec Check, string[] Attack, Func<FakeView, FakeOutcome> Evaluate);

/// <summary>
/// Nur Entwicklung (RIEGEL_FAKE_SERVICES=1, nur Demo-Anlage): rund 40 Regeln mit IDs aus spec 2.1 und einfachen
/// Auswertern, damit Seiten und Screenshots vor der Integration echte Inhalte zeigen. Das ist <b>nicht</b> der Katalog
/// 2026.10 – Sollwerte, Texte und Belegregeln liefern die Pakete rules-a/b/c und engine; die Integration erzeugt die
/// Screenshots mit der echten Engine neu. Verweise nur auf MITRE-ATT&amp;CK-Kennungen der Abhilfe.
/// </summary>
internal static class FakeRules
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string Ccs = @"SYSTEM\CurrentControlSet";
    private const string Cvp = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies";
    private const string Lsa = @"SYSTEM\CurrentControlSet\Control\Lsa";
    private const string Defender = @"wmi:root\Microsoft\Windows\Defender";
    private const string MpStatus = Defender + ":MSFT_MpComputerStatus";
    private const string MpPreference = Defender + ":MSFT_MpPreference";
    private const string SmbServer = @"wmi:root\Microsoft\Windows\SMB:MSFT_SmbServerConfiguration.GetConfiguration";
    private const string Services = @"wmi:root\cimv2:Win32_Service";
    private const string Features = @"wmi:root\cimv2:Win32_OptionalFeature";

    private static JsonElement N(long n) => JsonValues.Number(n);
    private static JsonElement S(string s) => JsonValues.Text(s);

    private static RegistryCheck RegCheck(string key, string value, long expected, WhenMissing missing = WhenMissing.Fail, CheckOperator op = CheckOperator.Eq) =>
        new(@"HKLM\" + key, value, RegValueType.Dword, null, op, N(expected), missing, null, null, null);

    private static readonly string[] Both = ["client", "server"];
    private static readonly string[] ServerOnly = ["server"];

    private static FakeRule R(string id, string category, Severity severity, (string, string) title, (string, string) rationale,
        CheckSpec check, Func<FakeView, FakeOutcome> evaluate, (string, string)? ot = null, (string, string)? remedy = null,
        ReasonCategory? suggested = null, string[]? os = null, bool admin = false, RuleKind kind = RuleKind.Check, params string[] attack) =>
        new(id, category, severity, kind, title, rationale, ot, remedy, suggested, os ?? Both, DomainScope.Any, admin, check, attack, evaluate);

    /// <summary>Alle nachgebildeten Regeln in Katalogreihenfolge.</summary>
    public static IReadOnlyList<FakeRule> All { get; } =
    [
        // ---------------------------------------------------------------- Lebenszyklus
        R("LIFE-01", "lifecycle", Severity.High, ("Supported operating system", "Unterstütztes Betriebssystem"),
            ("An operating system without security updates stays open to every flaw found after its end of support.",
             "Ein Betriebssystem ohne Sicherheitsupdates bleibt für jede Lücke angreifbar, die nach dem Supportende bekannt wird."),
            new LifecycleCheck(), Lifecycle,
            remedy: ("Plan the upgrade to a supported version; until then document compensating measures.",
                     "Umstieg auf eine unterstützte Version planen; bis dahin kompensierende Maßnahmen dokumentieren."),
            suggested: ReasonCategory.CompensatingControl, attack: "M1051"),
        R("LIFE-02", "lifecycle", Severity.High, ("Days since last cumulative update", "Tage seit dem letzten kumulativen Update"),
            ("Cumulative updates close known vulnerabilities; long gaps leave published flaws open.",
             "Kumulative Updates schließen bekannte Lücken; lange Pausen lassen veröffentlichte Schwachstellen offen."),
            new UpdateAgeCheck(null), UpdateAge,
            remedy: ("Install the latest cumulative update in the next maintenance window.",
                     "Das neueste kumulative Update im nächsten Wartungsfenster einspielen."), attack: "M1051"),
        R("LIFE-04", "lifecycle", Severity.Medium, ("Defender signatures current", "Defender-Signaturen aktuell"),
            ("Outdated signatures miss current malware.", "Veraltete Signaturen erkennen aktuelle Schadsoftware nicht."),
            new DefenderSettingCheck(DefenderSource.Status, "AntivirusSignatureAge", CheckOperator.Le, N(7)),
            v => Gate(v) ?? SignatureAge(v), attack: "M1049"),

        // ---------------------------------------------------------------- Netzwerk
        R("NET-01", "network", Severity.High, ("SMBv1 server disabled", "SMBv1-Server deaktiviert"),
            ("SMBv1 is an outdated file-sharing protocol without modern protections and a common route for worms.",
             "SMBv1 ist ein veraltetes Freigabeprotokoll ohne moderne Schutzmechanismen und ein häufiger Weg für Würmer."),
            new WmiPropertyCheck(SmbServer, "EnableSMB1Protocol", null, RowsMode.First, CheckOperator.Eq, JsonValues.False, NoRowsOutcome.NotCheckable),
            Smb1,
            ot: ("Old operator panels and NAS devices sometimes still need SMBv1; check with the vendor before switching it off.",
                 "Alte Bediengeräte und NAS brauchen teils noch SMBv1; vor dem Abschalten mit dem Hersteller klären."),
            remedy: ("Disable SMBv1 (feature SMB1Protocol) after the vendor has confirmed that nothing needs it.",
                     "SMBv1 (Feature SMB1Protocol) abschalten, nachdem der Hersteller bestätigt hat, dass nichts es braucht."),
            suggested: ReasonCategory.VendorRequirement, attack: "M1042"),
        R("NET-03", "network", Severity.Medium, ("LLMNR disabled", "LLMNR deaktiviert"),
            ("LLMNR answers name queries from anyone on the local network, which allows credentials to be captured and relayed.",
             "LLMNR beantwortet Namensanfragen von jedem im lokalen Netz – das ermöglicht, Anmeldedaten abzugreifen und weiterzuleiten."),
            RegCheck(@"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast", 0),
            v => RegEq(v, @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast", 0, WhenMissing.Fail),
            remedy: ("Set the policy \"Turn off multicast name resolution\".", "Richtlinie „Multicastnamensauflösung deaktivieren“ setzen."),
            attack: "M1042"),
        R("NET-04", "network", Severity.Medium, ("NetBIOS over TCP/IP disabled", "NetBIOS über TCP/IP deaktiviert"),
            ("The NetBIOS name service answers broadcast queries and can be abused like LLMNR.",
             "Der NetBIOS-Namensdienst beantwortet Broadcast-Anfragen und lässt sich wie LLMNR missbrauchen."),
            new WmiPropertyCheck(@"wmi:root\cimv2:Win32_NetworkAdapterConfiguration", "TcpipNetbiosOptions", null, RowsMode.All, CheckOperator.Eq, N(2), NoRowsOutcome.NotCheckable),
            NetBios, attack: "M1042"),
        R("NET-08", "network", Severity.Medium, ("SMB server requires signing", "SMB-Server verlangt Signierung"),
            ("Without signing, SMB sessions can be tampered with or relayed.", "Ohne Signierung lassen sich SMB-Sitzungen manipulieren oder weiterleiten."),
            new FirstOfCheck([
                new WmiPropertyCheck(SmbServer, "RequireSecuritySignature", null, RowsMode.First, CheckOperator.Eq, JsonValues.True, NoRowsOutcome.NotCheckable),
                RegCheck(Ccs + @"\Services\LanmanServer\Parameters", "RequireSecuritySignature", 1, WhenMissing.NotCheckable)]),
            SmbSigning,
            ot: ("Old engineering tools and NAS devices sometimes fail with signing.", "Alte Engineering-Werkzeuge und NAS-Geräte scheitern teils an der Signierung."),
            suggested: ReasonCategory.VendorRequirement),

        // ---------------------------------------------------------------- Anmeldedaten
        R("CRED-01", "credentials", Severity.High, ("No LM hash stored", "Kein LM-Hash gespeichert"),
            ("LM hashes are weak and can be cracked quickly.", "LM-Hashes sind schwach und schnell zu knacken."),
            RegCheck(Lsa, "NoLMHash", 1, WhenMissing.NotCheckable), v => RegEq(v, Lsa, "NoLMHash", 1, WhenMissing.NotCheckable), attack: "M1027"),
        R("CRED-02", "credentials", Severity.High, ("NTLMv2 only (LmCompatibilityLevel 5)", "Nur NTLMv2 (LmCompatibilityLevel 5)"),
            ("Older LM and NTLMv1 responses can be cracked or relayed easily.", "Ältere LM- und NTLMv1-Antworten lassen sich leicht knacken oder weiterleiten."),
            RegCheck(Lsa, "LmCompatibilityLevel", 5, WhenMissing.NotCheckable), v => RegEq(v, Lsa, "LmCompatibilityLevel", 5, WhenMissing.NotCheckable)),
        R("CRED-05", "credentials", Severity.High, ("WDigest does not store credentials", "WDigest speichert keine Anmeldedaten"),
            ("When WDigest stores credentials, plain-text passwords can be read from memory.",
             "Speichert WDigest Anmeldedaten, lassen sich Kennwörter im Klartext aus dem Speicher lesen."),
            RegCheck(Ccs + @"\Control\SecurityProviders\WDigest", "UseLogonCredential", 0, WhenMissing.OsDefault), WDigest, attack: "M1043"),
        R("CRED-06", "credentials", Severity.High, ("LSA protection running", "LSA-Schutz aktiv"),
            ("LSA protection makes it harder to read credentials from the LSASS process.",
             "Der LSA-Schutz erschwert das Auslesen von Anmeldedaten aus dem LSASS-Prozess."),
            new LsaProtectionCheck(), LsaProtection, attack: "M1043"),
        R("CRED-08", "credentials", Severity.Medium, ("Virtualisation-based security running", "Virtualisierungsbasierte Sicherheit läuft"),
            ("VBS isolates sensitive parts of the system from the rest of the operating system.",
             "VBS trennt sensible Teile des Systems vom übrigen Betriebssystem."),
            new DeviceGuardCheck(DeviceGuardService.Vbs), Vbs, attack: "M1043"),
        R("CRED-18", "credentials", Severity.High, ("Remote UAC filtering for local accounts", "UAC-Remotefilter für lokale Konten"),
            ("With LocalAccountTokenFilterPolicy = 1, local administrator accounts get full rights over the network, which eases lateral movement.",
             "Mit LocalAccountTokenFilterPolicy = 1 erhalten lokale Administratorkonten über das Netz volle Rechte – das erleichtert die Ausbreitung im Netz."),
            RegCheck(Cvp + @"\System", "LocalAccountTokenFilterPolicy", 1, WhenMissing.Pass, CheckOperator.Ne),
            v => RegNot(v, Cvp + @"\System", "LocalAccountTokenFilterPolicy", 1),
            ot: ("Remote maintenance in workgroups sometimes relies on this setting; a dedicated maintenance account is the better way.",
                 "Fernwartung in Arbeitsgruppen setzt teils auf diese Einstellung; besser ist ein eigenes Wartungskonto."),
            suggested: ReasonCategory.TechnicalNecessity, attack: "M1026"),

        // ---------------------------------------------------------------- Anmeldung
        R("LOGON-09", "logon", Severity.Medium, ("Screen lock after inactivity", "Sperre nach Inaktivität"),
            ("An unlocked, unattended session can be used by anyone at the machine.", "Eine offene, unbeaufsichtigte Sitzung kann jeder am Rechner benutzen."),
            RegCheck(Cvp + @"\System", "InactivityTimeoutSecs", 900, WhenMissing.Fail, CheckOperator.Le), Inactivity),
        R("LOGON-10", "logon", Severity.High, ("No automatic logon", "Keine automatische Anmeldung"),
            ("Automatic logon gives anyone at the machine a logged-on session, and the password is kept on the machine.",
             "Bei automatischer Anmeldung hat jeder am Rechner eine angemeldete Sitzung, und das Kennwort liegt auf dem Rechner."),
            new RegistryCheck(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "AutoAdminLogon", RegValueType.Sz, null, CheckOperator.Ne, S("1"),
                WhenMissing.Pass, null, null, null),
            AutoLogon,
            ot: ("Many operator stations log on automatically so the line restarts after a power failure; document this as a vendor requirement.",
                 "Viele Bedienstationen melden sich automatisch an, damit die Linie nach einem Stromausfall wieder anläuft; als Herstellervorgabe begründen."),
            suggested: ReasonCategory.VendorRequirement),

        // ---------------------------------------------------------------- Konten
        R("ACCT-10", "accounts", Severity.Medium, ("Guest account disabled", "Gastkonto deaktiviert"),
            ("The guest account allows access without a personal account.", "Das Gastkonto erlaubt Zugriff ohne persönliches Konto."),
            new LocalAccountCheck(501, LocalAccountExpectation.Disabled), v => AccountDisabled(v, 501), attack: "M1026"),
        R("ACCT-11", "accounts", Severity.Medium, ("Built-in Administrator disabled", "Integriertes Administratorkonto deaktiviert"),
            ("The built-in Administrator is a well-known target for password guessing.",
             "Das integrierte Administratorkonto ist ein bekanntes Ziel für das Erraten von Kennwörtern."),
            new LocalAccountCheck(500, LocalAccountExpectation.Disabled), v => AccountDisabled(v, 500), attack: "M1026"),
        R("ACCT-15", "accounts", Severity.Low, ("Local administrators (inventory)", "Lokale Administratoren (Bestand)"),
            ("Lists who has administrator rights on the machine.", "Zeigt, wer auf dem Rechner Administratorrechte hat."),
            new InventoryCheck(InventoryRenderer.LocalAdmins, [ItemKeys.LocalAdministrators]), LocalAdmins, kind: RuleKind.Inventory),
        R("UAC-01", "accounts", Severity.High, ("User Account Control enabled", "Benutzerkontensteuerung aktiv"),
            ("Without UAC every program of an administrator runs with full rights.",
             "Ohne Benutzerkontensteuerung läuft jedes Programm eines Administrators mit vollen Rechten."),
            RegCheck(Cvp + @"\System", "EnableLUA", 1), v => RegEq(v, Cvp + @"\System", "EnableLUA", 1, WhenMissing.NotCheckable), attack: "M1052"),

        // ---------------------------------------------------------------- Benutzerrechte
        R("URA-05", "userRights", Severity.High, ("Debug programs: Administrators only", "Programme debuggen: nur Administratoren"),
            ("The debug privilege allows reading the memory of any process, including credentials.",
             "Das Debug-Recht erlaubt, den Speicher jedes Prozesses zu lesen – auch Anmeldedaten."),
            new UserRightCheck("SeDebugPrivilege", UserRightMode.OnlyFrom, ["S-1-5-32-544"]), DebugPrivilege, admin: true, attack: "M1026"),

        // ---------------------------------------------------------------- Defender
        R("DEF-01", "defender", Severity.High, ("Real-time protection on", "Echtzeitschutz an"),
            ("Without real-time protection malware is only found by scheduled scans.",
             "Ohne Echtzeitschutz wird Schadsoftware erst bei geplanten Scans gefunden."),
            new DefenderSettingCheck(DefenderSource.Status, "RealTimeProtectionEnabled", CheckOperator.Eq, JsonValues.True),
            v => Gate(v) ?? MpBool(v, MpStatus, "RealTimeProtectionEnabled"), attack: "M1049"),
        R("DEF-03", "defender", Severity.Medium, ("Cloud-delivered protection", "Cloudbasierter Schutz"),
            ("Cloud-delivered protection lets Defender recognise new threats faster.", "Cloudbasierter Schutz lässt Defender neue Bedrohungen schneller erkennen."),
            new DefenderSettingCheck(DefenderSource.Preference, "MAPSReporting", CheckOperator.Ge, N(1)),
            v => Gate(v) ?? MapsReporting(v),
            ot: ("Air-gapped operator stations cannot reach the cloud service; a tailoring decision for the role fits better than an exception.",
                 "Bedienstationen ohne Internetzugang erreichen den Clouddienst nicht; ein Zuschnitt der Rolle passt besser als eine Ausnahme."),
            suggested: ReasonCategory.NotRelevantHere, attack: "M1049"),
        R("DEF-12", "defender", Severity.Medium, ("Tamper protection on", "Manipulationsschutz an"),
            ("Tamper protection prevents malware from switching off Defender.", "Der Manipulationsschutz verhindert, dass Schadsoftware Defender abschaltet."),
            new DefenderSettingCheck(DefenderSource.Status, "IsTamperProtected", CheckOperator.Eq, JsonValues.True),
            v => Gate(v) ?? MpBool(v, MpStatus, "IsTamperProtected"), attack: "M1049"),
        R("DEF-18", "defender", Severity.High, ("An antivirus is active and current", "Ein Virenschutz ist aktiv und aktuell"),
            ("Some antivirus product must be active and up to date.", "Ein Virenschutz muss aktiv und aktuell sein."),
            new AvStatusCheck(null), AvActive, attack: "M1049"),
        R("ASR-02", "defender", Severity.Medium, ("ASR: block credential stealing from LSASS", "ASR: Diebstahl von Anmeldedaten aus LSASS blockieren"),
            ("This attack surface reduction rule blocks programs that try to read credentials from LSASS.",
             "Diese Regel zur Verringerung der Angriffsfläche blockiert Programme, die Anmeldedaten aus LSASS lesen wollen."),
            new AsrRuleCheck("9e6c4e1f-7d60-472f-ba1a-a39ef669e4b2"), v => Gate(v, asr: true) ?? Asr(v, "9e6c4e1f-7d60-472f-ba1a-a39ef669e4b2"),
            suggested: ReasonCategory.CompensatingControl, attack: "M1040"),

        // ---------------------------------------------------------------- Anwendungssteuerung
        R("APP-01", "appControl", Severity.Medium, ("Application control enforced", "Anwendungssteuerung erzwungen"),
            ("Application control allows only approved programs to run.", "Anwendungssteuerung lässt nur freigegebene Programme laufen."),
            new WmiPropertyCheck(@"wmi:root\Microsoft\Windows\DeviceGuard:Win32_DeviceGuard", "UsermodeCodeIntegrityPolicyEnforcementStatus", null,
                RowsMode.First, CheckOperator.Eq, N(2), NoRowsOutcome.NotCheckable),
            AppControl, attack: "M1038"),

        // ---------------------------------------------------------------- Verschlüsselung
        R("ENC-01", "encryption", Severity.Medium, ("BitLocker on the OS volume", "BitLocker auf dem Systemlaufwerk"),
            ("Without encryption, data on the disk can be read when the device or disk is taken away.",
             "Ohne Verschlüsselung lassen sich die Daten lesen, wenn Gerät oder Festplatte entwendet werden."),
            new WmiPropertyCheck(@"wmi:root\cimv2\Security\MicrosoftVolumeEncryption:Win32_EncryptableVolume", "ProtectionStatus",
                new RowFilter("VolumeType", CheckOperator.Eq, N(0)), RowsMode.First, CheckOperator.Eq, N(1), NoRowsOutcome.Fail),
            BitLocker,
            ot: ("Agree the handling of recovery keys with operations before enabling BitLocker on line PCs.",
                 "Vor dem Einschalten auf Linienrechnern den Umgang mit Wiederherstellungsschlüsseln mit dem Betrieb abstimmen."),
            admin: true, attack: "M1041"),
        R("ENC-05", "encryption", Severity.Medium, ("Secure Boot on", "Sicherer Start aktiv"),
            ("Secure Boot prevents unsigned boot loaders and rootkits from starting.",
             "Sicherer Start verhindert, dass nicht signierte Startprogramme und Rootkits geladen werden."),
            RegCheck(Ccs + @"\Control\SecureBoot\State", "UEFISecureBootEnabled", 1), SecureBoot),

        // ---------------------------------------------------------------- Firewall
        R("FW-01", "firewall", Severity.High, ("Firewall on (domain profile)", "Firewall an (Domänenprofil)"),
            ("The firewall limits which services can be reached over the network.", "Die Firewall begrenzt, welche Dienste über das Netz erreichbar sind."),
            new FirewallProfileCheck(FirewallProfileName.Domain, FirewallProfileProperty.Enabled, true), v => Firewall(v, "domain", "DomainProfile"), attack: "M1037"),
        R("FW-02", "firewall", Severity.High, ("Firewall on (private profile)", "Firewall an (privates Profil)"),
            ("The firewall limits which services can be reached over the network.", "Die Firewall begrenzt, welche Dienste über das Netz erreichbar sind."),
            new FirewallProfileCheck(FirewallProfileName.Private, FirewallProfileProperty.Enabled, true), v => Firewall(v, "private", "StandardProfile"), attack: "M1037"),
        R("FW-03", "firewall", Severity.High, ("Firewall on (public profile)", "Firewall an (öffentliches Profil)"),
            ("The firewall limits which services can be reached over the network.", "Die Firewall begrenzt, welche Dienste über das Netz erreichbar sind."),
            new FirewallProfileCheck(FirewallProfileName.Public, FirewallProfileProperty.Enabled, true), v => Firewall(v, "public", "PublicProfile"), attack: "M1037"),
        R("FW-11", "firewall", Severity.High, ("No inbound RDP/SMB/WinRM from any address", "Kein eingehendes RDP/SMB/WinRM von beliebigen Adressen"),
            ("Remote access ports open to any address make the machine reachable from every network segment.",
             "Fernzugriffsports, die für jede Adresse offen sind, machen den Rechner aus jedem Netzsegment erreichbar."),
            new FirewallExposureCheck([new(NetProtocol.Tcp, 3389), new(NetProtocol.Tcp, 445), new(NetProtocol.Tcp, 5985), new(NetProtocol.Tcp, 5986)], ProfileScope.Active),
            Exposure, attack: "M1037"),

        // ---------------------------------------------------------------- Dienste und System
        R("SVC-01", "services", Severity.High, ("Print Spooler not running on servers", "Druckwarteschlange auf Servern aus"),
            ("The Print Spooler has had several serious vulnerabilities; servers rarely need it.",
             "Die Druckwarteschlange hatte mehrere schwere Schwachstellen; Server brauchen sie selten."),
            new ServiceCheck("Spooler", null, ["Stopped"], AbsentOutcome.Pass), v => ServiceState(v, "Spooler"),
            ot: ("Servers that print labels for the line need the spooler.", "Server, die Etiketten für die Linie drucken, brauchen die Druckwarteschlange."),
            suggested: ReasonCategory.TechnicalNecessity, os: ServerOnly, attack: "M1042"),
        R("SVC-04", "services", Severity.Medium, ("Remote Registry disabled", "Remoteregistrierung deaktiviert"),
            ("Remote Registry allows the registry to be read and changed over the network.",
             "Die Remoteregistrierung erlaubt, die Registry über das Netz zu lesen und zu ändern."),
            new ServiceCheck("RemoteRegistry", ["Disabled", "Manual"], ["Stopped"], AbsentOutcome.Pass), RemoteRegistry, attack: "M1042"),
        R("SYS-07", "system", Severity.Low, ("Administrative shares (inventory)", "Administrative Freigaben (Bestand)"),
            ("Lists the administrative shares of the machine.", "Zeigt die administrativen Freigaben des Rechners."),
            new InventoryCheck(InventoryRenderer.AdminShares, [@"wmi:root\cimv2:Win32_Share"]), v => Shares(v, admin: true), kind: RuleKind.Inventory),
        R("SYS-08", "system", Severity.Low, ("Shares open to everyone (inventory)", "Freigaben für jeden (Bestand)"),
            ("Lists shares that grant access to everyone.", "Zeigt Freigaben, auf die jeder zugreifen darf."),
            new InventoryCheck(InventoryRenderer.Shares, [@"wmi:root\cimv2:Win32_Share"]), v => Shares(v, admin: false), kind: RuleKind.Inventory),

        // ---------------------------------------------------------------- Überwachung, PowerShell, RDP, Medien, TLS
        R("AUD-01", "audit", Severity.Medium, ("Audit credential validation", "Überwachung der Anmeldeinformationsprüfung"),
            ("Records successful and failed credential checks, the basis for detecting password attacks.",
             "Protokolliert erfolgreiche und fehlgeschlagene Anmeldeprüfungen – Grundlage, um Kennwortangriffe zu erkennen."),
            new AuditSubcategoryCheck("{0CCE923F-69AE-11D9-BED3-505054503030}", AuditExpectation.SuccessAndFailure),
            v => Audit(v, "{0CCE923F-69AE-11D9-BED3-505054503030}"), admin: true, attack: "M1047"),
        R("PS-01", "powershell", Severity.Medium, ("PowerShell 2.0 engine absent", "PowerShell-2.0-Modul nicht vorhanden"),
            ("PowerShell 2.0 bypasses newer logging and protection features.", "PowerShell 2.0 umgeht neuere Protokollierungs- und Schutzfunktionen."),
            new FeatureCheck("MicrosoftWindowsPowerShellV2Root", [2, 3], AbsentOutcome.Pass), PowerShell2, attack: "M1042"),
        R("RDP-01", "rdp", Severity.High, ("Network Level Authentication required", "Authentifizierung auf Netzwerkebene verlangt"),
            ("With Network Level Authentication, users must sign in before a remote session is created.",
             "Mit Authentifizierung auf Netzwerkebene müssen sich Benutzer anmelden, bevor eine Remotesitzung entsteht."),
            new FirstOfCheck([
                RegCheck(@"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services", "UserAuthentication", 1, WhenMissing.NotCheckable),
                RegCheck(Ccs + @"\Control\Terminal Server\WinStations\RDP-Tcp", "UserAuthentication", 1, WhenMissing.Fail)]),
            Nla,
            ot: ("Some remote maintenance gateways do not support NLA.", "Manche Fernwartungsgeräte unterstützen keine Authentifizierung auf Netzwerkebene."),
            remedy: ("Require NLA by policy once the remote maintenance path supports it.",
                     "NLA per Richtlinie verlangen, sobald der Fernwartungsweg es unterstützt."),
            suggested: ReasonCategory.VendorRequirement, attack: "M1035"),
        R("RDP-09", "rdp", Severity.Low, ("Remote Desktop state (inventory)", "Remotedesktop (Bestand)"),
            ("Shows whether Remote Desktop is switched on.", "Zeigt, ob Remotedesktop eingeschaltet ist."),
            new InventoryCheck(InventoryRenderer.RdpState, [ItemKeys.Registry(Ccs + @"\Control\Terminal Server")]), RdpState, kind: RuleKind.Inventory),
        R("MED-01", "media", Severity.Medium, ("AutoRun disabled", "AutoRun deaktiviert"),
            ("AutoRun starts programs from inserted media without asking.", "AutoRun startet Programme von eingelegten Datenträgern ohne Rückfrage."),
            RegCheck(Cvp + @"\Explorer", "NoDriveTypeAutoRun", 255), v => RegEq(v, Cvp + @"\Explorer", "NoDriveTypeAutoRun", 255, WhenMissing.Fail),
            attack: "M1042"),
        R("TLS-10", "tls", Severity.Medium, (".NET uses strong cryptography", ".NET nutzt starke Kryptografie"),
            ("Without strong cryptography, .NET programs may still negotiate outdated protocols.",
             "Ohne starke Kryptografie handeln .NET-Programme womöglich noch veraltete Protokolle aus."),
            RegCheck(@"SOFTWARE\Microsoft\.NETFramework\v4.0.30319", "SchUseStrongCrypto", 1), StrongCrypto),
    ];

    // ================================================================ Auswerter

    private static string Str(long n) => n.ToString(Inv);

    private static string Item(FakeView v, ItemStatus status) =>
        CommonStrings.ResourceManager.GetString("Item_" + status, CultureInfo.GetCultureInfo(v.Context.Language)) ?? status.ToString();

    private static FakeOutcome NotCheckable(FakeView v, string key, ItemStatus status, string? expected = null) =>
        new(ResultStatus.NotCheckable, expected, null, ValueSource.Unknown, Details: [$"{key}: {Item(v, status)}"], Evidence: [key]);

    /// <summary>DWORD-Wert gleich <paramref name="expected"/>.</summary>
    private static FakeOutcome RegEq(FakeView v, string path, string name, long expected, WhenMissing missing)
    {
        var r = v.Reg(path, name);
        var (source, detail) = v.SourceOf(path, name);
        var exp = Str(expected);
        switch (r.Evidence)
        {
            case Evidence.Present:
                if (r.Type is not ("dword" or null))
                    return new(ResultStatus.Fail, exp, Canon(r.Value), source, detail, Details: [v.T("wrong value type", "falscher Werttyp")], Evidence: [r.Key]);
                return new(Number(r.Value) == expected ? ResultStatus.Pass : ResultStatus.Fail, exp, Canon(r.Value), source, detail, Evidence: [r.Key]);
            case Evidence.NotSet:
                return missing switch
                {
                    WhenMissing.Fail => new(ResultStatus.Fail, exp, null, ValueSource.NotSet, Evidence: [r.Key]),
                    WhenMissing.Pass => new(ResultStatus.Pass, exp, null, ValueSource.OsDefault, Flags: ResultFlags.FromDefault, Evidence: [r.Key]),
                    _ => new(ResultStatus.NotCheckable, exp, null, ValueSource.NotSet, Details: [v.T("Default value unknown", "Standardwert unbekannt")], Evidence: [r.Key]),
                };
            default:
                return NotCheckable(v, r.Key, r.Status, exp);
        }
    }

    /// <summary>DWORD-Wert ungleich <paramref name="forbidden"/>; nicht gesetzt besteht (Standardwert).</summary>
    private static FakeOutcome RegNot(FakeView v, string path, string name, long forbidden)
    {
        var r = v.Reg(path, name);
        var (source, detail) = v.SourceOf(path, name);
        var exp = "≠ " + Str(forbidden);
        return r.Evidence switch
        {
            Evidence.Present => new(Number(r.Value) == forbidden ? ResultStatus.Fail : ResultStatus.Pass, exp, Canon(r.Value), source, detail, Evidence: [r.Key]),
            Evidence.NotSet => new(ResultStatus.Pass, exp, null, ValueSource.OsDefault, Flags: ResultFlags.FromDefault, Evidence: [r.Key]),
            _ => NotCheckable(v, r.Key, r.Status, exp),
        };
    }

    private sealed record Product(int Build, string[]? Editions, bool Server, string Name, DateOnly End);

    // Supportenden der Demo-Betriebssysteme (nur Entwicklung; die echte Tabelle rules/lifecycle/windows.json bringt rules-a)
    private static readonly Product[] Products =
    [
        new(7601, null, false, "Windows 7 SP1", new DateOnly(2020, 1, 14)),
        new(14393, null, true, "Windows Server 2016", new DateOnly(2027, 1, 12)),
        new(17763, ["EnterpriseS", "IoTEnterpriseS"], false, "Windows 10 LTSC 2019", new DateOnly(2029, 1, 9)),
        new(20348, null, true, "Windows Server 2022", new DateOnly(2031, 10, 14)),
        new(26100, ["Enterprise", "Education"], false, "Windows 11 Enterprise 24H2", new DateOnly(2027, 10, 12)),
    ];

    private static FakeOutcome Lifecycle(FakeView v)
    {
        var m = v.Snapshot.Machine;
        var product = Products.FirstOrDefault(p => p.Build == m.Build && p.Server == v.IsServer
                                                   && (p.Editions is null || p.Editions.Contains(m.EditionId, StringComparer.OrdinalIgnoreCase)));
        if (product is null)
            return new(ResultStatus.NotCheckable, "supported", null, ValueSource.Runtime, Details: [v.T("Not in the lifecycle table", "Nicht in der Lebenszyklus-Tabelle")]);
        var end = product.End.ToString("yyyy-MM-dd", Inv);
        int days = product.End.DayNumber - v.Context.EvaluationDate.DayNumber;
        var actual = $"{product.Name} ({v.T("end of support", "Supportende")} {end})";
        if (days >= 0)
        {
            var details = days <= 183 ? new[] { v.T($"End of support in {days} days", $"Supportende in {days} Tagen") } : null;
            return new(ResultStatus.Pass, "supported", actual, ValueSource.Runtime, Details: details);
        }
        if (v.Context.EsuCovered)
            return new(ResultStatus.Pass, "supported", actual, ValueSource.Runtime, Details: [v.T("Extended Security Updates recorded for this machine", "ESU für diesen Rechner eingetragen")]);
        return new(ResultStatus.Fail, "supported", actual, ValueSource.Runtime, Details: [v.T($"Unsupported since {end}", $"Ohne Support seit {end}")]);
    }

    private static FakeOutcome UpdateAge(FakeView v)
    {
        const string key = @"wmi:root\cimv2:Win32_QuickFixEngineering";
        var rows = v.Rows(key);
        var max = Str(v.Context.UpdateMaxDays);
        if (rows is null) return NotCheckable(v, key, v.StatusOf(key) ?? ItemStatus.NotCollected, max);
        var newest = rows.Select(r => Date(r, "InstalledOn")).Where(d => d is not null).Max();
        if (newest is null) return new(ResultStatus.NotCheckable, max, null, ValueSource.Runtime, Evidence: [key]);
        int days = (int)(v.Snapshot.CollectedAtUtc - newest.Value).TotalDays;
        return new(days <= v.Context.UpdateMaxDays ? ResultStatus.Pass : ResultStatus.Fail, max, Str(days), ValueSource.Runtime,
            Details: [v.T($"Newest update installed {newest.Value:yyyy-MM-dd}", $"Neuestes Update installiert am {newest.Value:yyyy-MM-dd}")], Evidence: [key]);
    }

    /// <summary>Sperre nach defender.mode (spec 2.5); null = auswerten.</summary>
    private static FakeOutcome? Gate(FakeView v, bool asr = false)
    {
        var mode = v.Fact("defender.mode");
        bool third = v.Fact("av.thirdPartyActive") == "true";
        string thirdText = v.T("Third-party antivirus active", "Drittanbieter-Virenschutz aktiv");
        string noneText = v.T("No active antivirus", "Kein aktiver Virenschutz");
        return mode switch
        {
            "active" => null,
            "passive" or "edrBlock" => third
                ? new(ResultStatus.NotApplicable, null, mode, ValueSource.Runtime, Flags: ResultFlags.Derived, Details: [thirdText], Evidence: [MpStatus])
                : new(ResultStatus.Fail, null, mode, ValueSource.Runtime, Details: [noneText], Evidence: [MpStatus]),
            "off" => new(ResultStatus.Fail, null, mode, ValueSource.Runtime, Details: [noneText], Evidence: [MpStatus]),
            "absent" => third || asr
                ? new(ResultStatus.NotApplicable, null, mode, ValueSource.Runtime, Details: [third ? thirdText : v.T("Defender not present", "Defender nicht vorhanden")], Evidence: [MpStatus])
                : new(ResultStatus.Fail, null, mode, ValueSource.Runtime, Details: [noneText], Evidence: [MpStatus]),
            _ => new(ResultStatus.NotCheckable, null, null, ValueSource.Unknown, Details: [v.T("Defender mode unknown", "Defender-Modus unbekannt")], Evidence: [MpStatus]),
        };
    }

    private static FakeOutcome MpBool(FakeView v, string key, string property)
    {
        var rows = v.Rows(key);
        if (rows is not [var row, ..]) return NotCheckable(v, key, v.StatusOf(key) ?? ItemStatus.NotCollected, "true");
        var value = Bool(row, property);
        if (value is null) return new(ResultStatus.NotCheckable, "true", null, ValueSource.Runtime, Evidence: [key]);
        return new(value.Value ? ResultStatus.Pass : ResultStatus.Fail, "true", value.Value ? "true" : "false", ValueSource.Runtime, Evidence: [key]);
    }

    private static FakeOutcome SignatureAge(FakeView v)
    {
        var rows = v.Rows(MpStatus);
        if (rows is not [var row, ..]) return NotCheckable(v, MpStatus, v.StatusOf(MpStatus) ?? ItemStatus.NotCollected, "7");
        var age = Num(row, "AntivirusSignatureAge");
        if (age is null) return new(ResultStatus.NotCheckable, "7", null, ValueSource.Runtime, Evidence: [MpStatus]);
        return new(age <= 7 ? ResultStatus.Pass : ResultStatus.Fail, "7", Str(age.Value), ValueSource.Runtime, Evidence: [MpStatus]);
    }

    private static FakeOutcome MapsReporting(FakeView v)
    {
        var rows = v.Rows(MpPreference);
        if (rows is not [var row, ..]) return NotCheckable(v, MpPreference, v.StatusOf(MpPreference) ?? ItemStatus.NotCollected, "1");
        var maps = Num(row, "MAPSReporting");
        if (maps is null) return new(ResultStatus.NotCheckable, "1", null, ValueSource.Runtime, Evidence: [MpPreference]);
        var (source, detail) = v.RegHas(@"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "SpynetReporting")
            ? v.SourceOf(@"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "SpynetReporting")
            : (ValueSource.Runtime, null);
        return new(maps >= 1 ? ResultStatus.Pass : ResultStatus.Fail, "1", Str(maps.Value), source, detail, Evidence: [MpPreference]);
    }

    private static FakeOutcome Asr(FakeView v, string guid)
    {
        var rows = v.Rows(MpPreference);
        if (rows is not [var row, ..]) return NotCheckable(v, MpPreference, v.StatusOf(MpPreference) ?? ItemStatus.NotCollected, "block");
        if (row.TryGetProperty("AttackSurfaceReductionRules_Ids", out var ids) && ids.ValueKind == JsonValueKind.Array)
        {
            var list = ids.EnumerateArray().Select(i => i.GetString() ?? "").ToList();
            int index = list.FindIndex(i => i.Equals(guid, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && row.TryGetProperty("AttackSurfaceReductionRules_Actions", out var actions) && actions.ValueKind == JsonValueKind.Array
                && actions.GetArrayLength() > index && Number(actions[index]) is { } mode)
                return new(mode == 1 ? ResultStatus.Pass : ResultStatus.Fail, "block", mode switch { 1 => "block", 2 => "audit", 6 => "warn", _ => "off" },
                    ValueSource.Runtime, Evidence: [MpPreference]);
        }
        return new(ResultStatus.Fail, "block", null, ValueSource.NotSet, Details: [v.T("Not configured", "Nicht konfiguriert")], Evidence: [MpPreference]);
    }

    private static FakeOutcome AvActive(FakeView v)
    {
        if (v.Fact("defender.mode") == "active" && v.Rows(MpStatus) is [var row, ..] && Bool(row, "RealTimeProtectionEnabled") == true
            && Num(row, "AntivirusSignatureAge") is <= 7)
            return new(ResultStatus.Pass, "active, current", "Microsoft Defender Antivirus", ValueSource.Runtime, Evidence: [MpStatus]);
        if (v.TryValue(ItemKeys.WscAntivirus, out var wsc) && wsc.ValueKind == JsonValueKind.Array)
        {
            var active = wsc.EnumerateArray().FirstOrDefault(p => Text(p, "state") == "on" && Text(p, "signature") == "upToDate");
            if (active.ValueKind == JsonValueKind.Object)
                return new(ResultStatus.Pass, "active, current", Text(active, "name"), ValueSource.Runtime, Evidence: [ItemKeys.WscAntivirus]);
        }
        const string sc = @"wmi:root\SecurityCenter2:AntivirusProduct";
        if (v.Rows(sc) is { Count: > 0 } products)
            return new(ResultStatus.NotCheckable, "active, current", Text(products[0], "displayName"), ValueSource.Runtime, Flags: ResultFlags.Heuristic,
                Details: [v.T("Only the Security Center heuristic is available", "Nur die Heuristik des Sicherheitscenters liegt vor")], Evidence: [sc]);
        if (v.IsBuiltin) return NotCheckable(v, ItemKeys.WscAntivirus, ItemStatus.NotCollected, "active, current");
        return new(ResultStatus.Fail, "active, current", null, ValueSource.Runtime, Details: [v.T("No active antivirus found", "Kein aktiver Virenschutz gefunden")], Evidence: [MpStatus]);
    }

    private static FakeOutcome Smb1(FakeView v)
    {
        if (v.Out(SmbServer) is { } o && o.TryGetProperty("EnableSMB1Protocol", out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return new(e.ValueKind == JsonValueKind.False ? ResultStatus.Pass : ResultStatus.Fail, "false", e.ValueKind == JsonValueKind.True ? "true" : "false",
                ValueSource.Runtime, Evidence: [SmbServer]);
        if (v.Rows(Features) is { } features)
        {
            var smb = features.FirstOrDefault(f => Text(f, "Name") is "SMB1Protocol-Server" or "SMB1Protocol");
            if (smb.ValueKind == JsonValueKind.Object && Num(smb, "InstallState") is { } state)
                return new(state == 1 ? ResultStatus.Fail : ResultStatus.Pass, "false", state == 1 ? "true" : "false", ValueSource.Runtime, Evidence: [Features]);
        }
        var r = v.Reg(Ccs + @"\Services\LanmanServer\Parameters", "SMB1");
        if (r.Evidence == Evidence.Present)
            return new(Number(r.Value) == 0 ? ResultStatus.Pass : ResultStatus.Fail, "0", Canon(r.Value), ValueSource.DirectlySet, Evidence: [r.Key]);
        return new(ResultStatus.NotCheckable, "false", null, ValueSource.Unknown,
            Details: [v.T("No runtime state, feature or registry value (no build default for SMBv1)", "Kein Laufzeitzustand, Feature oder Registry-Wert (kein Standardwert für SMBv1)")],
            Evidence: [SmbServer, r.Key]);
    }

    private static FakeOutcome SmbSigning(FakeView v)
    {
        if (v.Out(SmbServer) is { } o && o.TryGetProperty("RequireSecuritySignature", out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return new(e.ValueKind == JsonValueKind.True ? ResultStatus.Pass : ResultStatus.Fail, "true", e.ValueKind == JsonValueKind.True ? "true" : "false",
                ValueSource.Runtime, Evidence: [SmbServer]);
        return RegEq(v, Ccs + @"\Services\LanmanServer\Parameters", "RequireSecuritySignature", 1, WhenMissing.NotCheckable);
    }

    private static FakeOutcome NetBios(FakeView v)
    {
        const string key = @"wmi:root\cimv2:Win32_NetworkAdapterConfiguration";
        if (v.Rows(key) is { Count: > 0 } rows)
        {
            bool all = rows.All(r => Num(r, "TcpipNetbiosOptions") == 2);
            return new(all ? ResultStatus.Pass : ResultStatus.Fail, "2", all ? "2" : string.Join(", ", rows.Select(r => Num(r, "TcpipNetbiosOptions")?.ToString(Inv) ?? "?")),
                ValueSource.Runtime, Evidence: [key]);
        }
        return NotCheckable(v, key, v.StatusOf(key) ?? ItemStatus.NotCollected, "2");
    }

    private static FakeOutcome WDigest(FakeView v)
    {
        var path = Ccs + @"\Control\SecurityProviders\WDigest";
        var r = v.Reg(path, "UseLogonCredential");
        if (r.Evidence == Evidence.NotSet && v.Build >= 9600)
            return new(ResultStatus.Pass, "0", null, ValueSource.OsDefault, Flags: ResultFlags.FromDefault, Evidence: [r.Key]);
        return RegEq(v, path, "UseLogonCredential", 0, WhenMissing.NotCheckable);
    }

    private static FakeOutcome LsaProtection(FakeView v)
    {
        if (v.TryValue(ItemKeys.WininitEvent12, out var evt) && Bool(evt, "found") == true)
            return new(ResultStatus.Pass, "running", "running", ValueSource.Runtime,
                Details: [v.T("Proven by Wininit event 12 since the last boot", "Nachgewiesen durch Wininit-Ereignis 12 seit dem letzten Start")], Evidence: [ItemKeys.WininitEvent12]);
        var r = v.Reg(Lsa, "RunAsPPL");
        var (source, detail) = v.SourceOf(Lsa, "RunAsPPL");
        if (r.Evidence == Evidence.Present && Number(r.Value) is 1 or 2)
            return new(ResultStatus.NotCheckable, "running", Canon(r.Value), source, detail, ResultFlags.ConfiguredOnly,
                [v.T("Configured, effect not proven", "Konfiguriert, Wirkung nicht nachgewiesen")], [r.Key, ItemKeys.WininitEvent12]);
        if (r.Evidence == Evidence.Unavailable) return NotCheckable(v, r.Key, r.Status, "running");
        return new(ResultStatus.Fail, "running", r.Evidence == Evidence.Present ? Canon(r.Value) : null,
            r.Evidence == Evidence.Present ? source : ValueSource.NotSet, Evidence: [r.Key, ItemKeys.WininitEvent12]);
    }

    private static FakeOutcome Vbs(FakeView v)
    {
        const string key = @"wmi:root\Microsoft\Windows\DeviceGuard:Win32_DeviceGuard";
        if (v.Rows(key) is [var row, ..])
        {
            var status = Num(row, "VirtualizationBasedSecurityStatus");
            return new(status == 2 ? ResultStatus.Pass : ResultStatus.Fail, "running", status == 2 ? "running" : status == 1 ? "configured" : "off", ValueSource.Runtime,
                Details: status == 1 ? [v.T("Configured, not running", "Konfiguriert, läuft nicht")] : null, Evidence: [key]);
        }
        if (v.StatusOf(key) == ItemStatus.NamespaceMissing)
            return new(ResultStatus.Fail, "running", null, ValueSource.Runtime,
                Details: [v.T("This operating system has no VBS", "Dieses Betriebssystem hat keine VBS")], Evidence: [key]);
        return NotCheckable(v, key, v.StatusOf(key) ?? ItemStatus.NotCollected, "running");
    }

    private static FakeOutcome Inactivity(FakeView v)
    {
        var path = Cvp + @"\System";
        var r = v.Reg(path, "InactivityTimeoutSecs");
        var (source, detail) = v.SourceOf(path, "InactivityTimeoutSecs");
        return r.Evidence switch
        {
            Evidence.Present => new(Number(r.Value) is > 0 and <= 900 ? ResultStatus.Pass : ResultStatus.Fail, "≤ 900", Canon(r.Value), source, detail, Evidence: [r.Key]),
            Evidence.NotSet => new(ResultStatus.Fail, "≤ 900", null, ValueSource.NotSet, Evidence: [r.Key]),
            _ => NotCheckable(v, r.Key, r.Status, "≤ 900"),
        };
    }

    private static FakeOutcome AutoLogon(FakeView v)
    {
        const string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
        var r = v.Reg(path, "AutoAdminLogon");
        if (r.Evidence == Evidence.Unavailable) return NotCheckable(v, r.Key, r.Status, "≠ 1");
        if (r.Evidence == Evidence.Present && Canon(r.Value) == "1")
        {
            bool password = v.RegHas(path, "DefaultPassword");
            return new(ResultStatus.Fail, "≠ 1", "1", ValueSource.DirectlySet,
                Details: [password ? v.T("Password in the registry", "Kennwort in Registry") : v.T("Where the password is kept cannot be checked", "Ablage des Kennworts nicht prüfbar")],
                Evidence: [r.Key]);
        }
        return new(ResultStatus.Pass, "≠ 1", r.Evidence == Evidence.Present ? Canon(r.Value) : null,
            r.Evidence == Evidence.Present ? ValueSource.DirectlySet : ValueSource.NotSet, Evidence: [r.Key]);
    }

    private static FakeOutcome AccountDisabled(FakeView v, int rid)
    {
        const string key = @"wmi:root\cimv2:Win32_UserAccount";
        var rows = v.Rows(key);
        var row = rows?.FirstOrDefault(r => Text(r, "SID")?.EndsWith("-" + rid.ToString(Inv), StringComparison.Ordinal) == true);
        if (row is { ValueKind: JsonValueKind.Object } found && Bool(found, "Disabled") is { } disabled)
            return new(disabled ? ResultStatus.Pass : ResultStatus.Fail, "disabled", disabled ? "disabled" : "enabled", ValueSource.Runtime, Evidence: [key]);
        if (v.TryValue(ItemKeys.NetUserEnum, out var users) && users.ValueKind == JsonValueKind.Array)
        {
            var user = users.EnumerateArray().FirstOrDefault(u => Num(u, "rid") == rid);
            if (user.ValueKind == JsonValueKind.Object && Bool(user, "disabled") is { } d)
                return new(d ? ResultStatus.Pass : ResultStatus.Fail, "disabled", d ? "disabled" : "enabled", ValueSource.Runtime, Evidence: [ItemKeys.NetUserEnum]);
        }
        if (v.IsBuiltin && rid == 500 && v.TryValue(ItemKeys.RawSeceditInf, out var inf) && Text(inf, "text") is { } text
            && text.Contains("EnableAdminAccount = 1", StringComparison.Ordinal))
            return new(ResultStatus.Fail, "disabled", "enabled", ValueSource.LocalPolicy, Evidence: [ItemKeys.RawSeceditInf]);
        return NotCheckable(v, key, v.StatusOf(key) ?? ItemStatus.NotCollected, "disabled");
    }

    private static FakeOutcome LocalAdmins(FakeView v)
    {
        if (!v.TryValue(ItemKeys.LocalAdministrators, out var members) || members.ValueKind != JsonValueKind.Array)
            return NotCheckable(v, ItemKeys.LocalAdministrators, v.StatusOf(ItemKeys.LocalAdministrators) ?? ItemStatus.NotCollected);
        var lines = members.EnumerateArray().Select(m => $"{Text(m, "name")} ({Text(m, "sid")})").ToList();
        return new(ResultStatus.Info, null, Str(lines.Count), ValueSource.Runtime, Details: lines, Evidence: [ItemKeys.LocalAdministrators]);
    }

    private static FakeOutcome DebugPrivilege(FakeView v)
    {
        if (v.TryValue(ItemKeys.LsaRights, out var rights) && rights.TryGetProperty("SeDebugPrivilege", out var sids))
        {
            var list = sids.EnumerateArray().Select(s => s.GetString() ?? "").ToList();
            bool ok = list.All(s => s == "S-1-5-32-544");
            return new(ok ? ResultStatus.Pass : ResultStatus.Fail, "S-1-5-32-544", string.Join(", ", list), ValueSource.Runtime, Evidence: [ItemKeys.LsaRights]);
        }
        if (v.TryValue(ItemKeys.ParsedSecedit, out var secedit) && secedit.TryGetProperty("sections", out var sections)
            && sections.TryGetProperty("Privilege Rights", out var pr) && pr.TryGetProperty("SeDebugPrivilege", out var entries))
        {
            var list = entries.EnumerateArray().Select(s => (s.GetString() ?? "").TrimStart('*')).ToList();
            bool ok = list.All(s => s == "S-1-5-32-544");
            return new(ok ? ResultStatus.Pass : ResultStatus.Fail, "S-1-5-32-544", string.Join(", ", list), ValueSource.LocalPolicy, Evidence: [ItemKeys.ParsedSecedit]);
        }
        if (v.TryValue(ItemKeys.RawSeceditInf, out var inf) && Text(inf, "text") is { } text)
        {
            var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("SeDebugPrivilege", StringComparison.Ordinal));
            if (line is not null)
            {
                var list = line[(line.IndexOf('=') + 1)..].Split(',', StringSplitOptions.TrimEntries).Select(s => s.TrimStart('*')).ToList();
                bool ok = list.All(s => s == "S-1-5-32-544");
                return new(ok ? ResultStatus.Pass : ResultStatus.Fail, "S-1-5-32-544", string.Join(", ", list), ValueSource.LocalPolicy, Evidence: [ItemKeys.RawSeceditInf]);
            }
        }
        return NotCheckable(v, ItemKeys.LsaRights, v.StatusOf(ItemKeys.LsaRights) ?? ItemStatus.NotCollected, "S-1-5-32-544");
    }

    private static FakeOutcome AppControl(FakeView v)
    {
        const string key = @"wmi:root\Microsoft\Windows\DeviceGuard:Win32_DeviceGuard";
        if (v.Rows(key) is [var row, ..])
        {
            bool enforced = Num(row, "UsermodeCodeIntegrityPolicyEnforcementStatus") == 2;
            var appId = v.Rows(Services)?.FirstOrDefault(s => Text(s, "Name") == "AppIDSvc");
            bool appLocker = appId is { ValueKind: JsonValueKind.Object } a && Text(a, "State") == "Running";
            return new(enforced || appLocker ? ResultStatus.Pass : ResultStatus.Fail, "enforced", enforced ? "App Control" : appLocker ? "AppLocker" : "off",
                ValueSource.Runtime, Evidence: [key, Services]);
        }
        if (v.StatusOf(key) == ItemStatus.NamespaceMissing)
            return new(ResultStatus.Fail, "enforced", "off", ValueSource.Runtime, Details: [v.T("No application control found", "Keine Anwendungssteuerung gefunden")], Evidence: [key]);
        return NotCheckable(v, key, v.StatusOf(key) ?? ItemStatus.NotCollected, "enforced");
    }

    private static FakeOutcome BitLocker(FakeView v)
    {
        const string key = @"wmi:root\cimv2\Security\MicrosoftVolumeEncryption:Win32_EncryptableVolume";
        if (v.Rows(key) is { } rows)
        {
            var os = rows.FirstOrDefault(r => Num(r, "VolumeType") == 0);
            if (os.ValueKind != JsonValueKind.Object) return new(ResultStatus.Fail, "1", null, ValueSource.Runtime, Evidence: [key]);
            var status = Num(os, "ProtectionStatus");
            return new(status == 1 ? ResultStatus.Pass : ResultStatus.Fail, "1", status?.ToString(Inv), ValueSource.Runtime,
                Details: status == 1 ? null : [v.T("BitLocker is off on C:", "BitLocker ist auf C: aus")], Evidence: [key]);
        }
        return NotCheckable(v, key, v.StatusOf(key) ?? ItemStatus.NotCollected, "1");
    }

    private static FakeOutcome SecureBoot(FakeView v)
    {
        var path = Ccs + @"\Control\SecureBoot\State";
        var r = v.Reg(path, "UEFISecureBootEnabled");
        return r.Evidence switch
        {
            Evidence.Present => new(Number(r.Value) == 1 ? ResultStatus.Pass : ResultStatus.Fail, "1", Canon(r.Value), ValueSource.Runtime, Evidence: [r.Key]),
            Evidence.NotSet => new(ResultStatus.Fail, "1", null, ValueSource.NotSet, Details: [v.T("No UEFI Secure Boot state (legacy BIOS?)", "Kein Zustand für UEFI-Secure-Boot (BIOS-Start?)")], Evidence: [r.Key]),
            _ => NotCheckable(v, r.Key, r.Status, "1"),
        };
    }

    private static FakeOutcome Firewall(FakeView v, string profile, string registryProfile)
    {
        if (v.TryValue(ItemKeys.Firewall, out var fw) && fw.TryGetProperty("profiles", out var profiles) && profiles.TryGetProperty(profile, out var p)
            && Bool(p, "enabled") is { } enabled)
            return new(enabled ? ResultStatus.Pass : ResultStatus.Fail, "true", enabled ? "true" : "false", ValueSource.Runtime, Evidence: [ItemKeys.Firewall]);
        var path = Ccs + @"\Services\SharedAccess\Parameters\FirewallPolicy\" + registryProfile;
        var r = v.Reg(path, "EnableFirewall");
        if (r.Evidence == Evidence.Present)
            return new(Number(r.Value) == 1 ? ResultStatus.Pass : ResultStatus.Fail, "1", Canon(r.Value), ValueSource.DirectlySet,
                Details: [v.T("From the registry, no runtime state", "Aus der Registry, kein Laufzeitzustand")], Evidence: [r.Key]);
        return NotCheckable(v, ItemKeys.Firewall, v.StatusOf(ItemKeys.Firewall) ?? ItemStatus.NotCollected, "true");
    }

    private static FakeOutcome Exposure(FakeView v)
    {
        if (!v.TryValue(ItemKeys.Firewall, out var fw) || Bool(fw, "rulesCollected") != true || !fw.TryGetProperty("rules", out var rules))
            return NotCheckable(v, ItemKeys.Firewall, v.StatusOf(ItemKeys.Firewall) ?? ItemStatus.NotCollected);
        string[] ports = ["3389", "445", "5985", "5986"];
        var open = rules.EnumerateArray().Where(r => Bool(r, "enabled") == true && Text(r, "direction") == "in" && Text(r, "action") == "allow"
                                                     && ports.Contains(Text(r, "localPorts")) && Text(r, "remoteAddresses") is "*" or "Any" or "")
            .Select(r => $"{Text(r, "name")} (TCP {Text(r, "localPorts")})").ToList();
        return new(open.Count == 0 ? ResultStatus.Pass : ResultStatus.Fail, null, open.Count == 0 ? null : string.Join(", ", open), ValueSource.Runtime,
            Details: open.Count == 0 ? null : open, Evidence: [ItemKeys.Firewall]);
    }

    private static FakeOutcome ServiceState(FakeView v, string name)
    {
        if (v.Rows(Services) is { } rows)
        {
            var row = rows.FirstOrDefault(r => string.Equals(Text(r, "Name"), name, StringComparison.OrdinalIgnoreCase));
            if (row.ValueKind != JsonValueKind.Object) return new(ResultStatus.Pass, "Stopped", null, ValueSource.Runtime, Evidence: [Services]);
            var state = Text(row, "State");
            return new(state == "Running" ? ResultStatus.Fail : ResultStatus.Pass, "Stopped", state, ValueSource.Runtime,
                Details: [$"{Text(row, "StartMode")}"], Evidence: [Services]);
        }
        return new(ResultStatus.NotCheckable, "Stopped", null, ValueSource.Unknown,
            Details: [v.T("The built-in bundle has no running state", "Das Bordmittel-Paket kennt keinen Laufzustand")], Evidence: [Services]);
    }

    private static FakeOutcome RemoteRegistry(FakeView v)
    {
        if (v.Rows(Services) is { } rows)
        {
            var row = rows.FirstOrDefault(r => Text(r, "Name") == "RemoteRegistry");
            if (row.ValueKind != JsonValueKind.Object) return new(ResultStatus.Pass, "Disabled", null, ValueSource.Runtime, Evidence: [Services]);
            var mode = Text(row, "StartMode");
            var state = Text(row, "State");
            bool ok = mode == "Disabled" || (mode == "Manual" && state == "Stopped");
            return new(ok ? ResultStatus.Pass : ResultStatus.Fail, "Disabled", $"{mode}, {state}", ValueSource.Runtime, Evidence: [Services]);
        }
        var r = v.Reg(Ccs + @"\Services\RemoteRegistry", "Start");
        if (r.Evidence == Evidence.Present)
            return new(Number(r.Value) is 3 or 4 ? ResultStatus.Pass : ResultStatus.Fail, "4", Canon(r.Value), ValueSource.DirectlySet, Evidence: [r.Key]);
        return NotCheckable(v, Services, v.StatusOf(Services) ?? ItemStatus.NotCollected, "Disabled");
    }

    private static FakeOutcome Shares(FakeView v, bool admin)
    {
        const string key = @"wmi:root\cimv2:Win32_Share";
        if (v.Rows(key) is not { } rows) return NotCheckable(v, key, v.StatusOf(key) ?? ItemStatus.NotCollected);
        var lines = rows.Where(r => admin
                ? (Num(r, "Type") ?? 0) >= 2147483648
                : r.TryGetProperty("GetSecurityDescriptor", out var sd) && sd.TryGetProperty("Aces", out var aces)
                  && aces.EnumerateArray().Any(a => Text(a, "Sid") == "S-1-1-0"))
            .Select(r => admin ? Text(r, "Name") ?? "" : $"{Text(r, "Name")} ({Text(r, "Path")}): {v.T("Everyone", "Jeder")}")
            .ToList();
        return new(ResultStatus.Info, null, Str(lines.Count), ValueSource.Runtime, Details: lines, Evidence: [key]);
    }

    private static FakeOutcome Audit(FakeView v, string guid)
    {
        if (v.TryValue(ItemKeys.AuditPolicy, out var policy) && policy.TryGetProperty(guid, out var value) && Number(value) is { } n)
            return new(n == 3 ? ResultStatus.Pass : ResultStatus.Fail, "successAndFailure",
                n switch { 0 => "noAuditing", 1 => "success", 2 => "failure", _ => "successAndFailure" }, ValueSource.Runtime, Evidence: [ItemKeys.AuditPolicy]);
        return NotCheckable(v, ItemKeys.AuditPolicy, v.StatusOf(ItemKeys.AuditPolicy) ?? ItemStatus.NotCollected, "successAndFailure");
    }

    private static FakeOutcome PowerShell2(FakeView v)
    {
        if (v.Build is > 0 and < 9200)
            return new(ResultStatus.Fail, "absent", "present", ValueSource.Runtime,
                Details: [v.T("Part of this operating system", "Bestandteil dieses Betriebssystems")]);
        if (v.Rows(Features) is { } rows)
        {
            var row = rows.FirstOrDefault(r => Text(r, "Name") == "MicrosoftWindowsPowerShellV2Root");
            bool present = row.ValueKind == JsonValueKind.Object && Num(row, "InstallState") == 1;
            return new(present ? ResultStatus.Fail : ResultStatus.Pass, "absent", present ? "present" : "absent", ValueSource.Runtime, Evidence: [Features]);
        }
        return NotCheckable(v, Features, v.StatusOf(Features) ?? ItemStatus.NotCollected, "absent");
    }

    private static FakeOutcome Nla(FakeView v)
    {
        var ts = v.Reg(Ccs + @"\Control\Terminal Server", "fDenyTSConnections");
        if (ts.Evidence == Evidence.Present && Number(ts.Value) == 1)
            return new(ResultStatus.NotApplicable, "1", null, ValueSource.DirectlySet, Details: [v.T("Remote Desktop is off", "Remotedesktop ist aus")], Evidence: [ts.Key]);
        var policy = v.Reg(@"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services", "UserAuthentication");
        if (policy.Evidence == Evidence.Present)
        {
            var (source, detail) = v.SourceOf(@"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services", "UserAuthentication");
            return new(Number(policy.Value) == 1 ? ResultStatus.Pass : ResultStatus.Fail, "1", Canon(policy.Value), source, detail, Evidence: [policy.Key]);
        }
        return RegEq(v, Ccs + @"\Control\Terminal Server\WinStations\RDP-Tcp", "UserAuthentication", 1, WhenMissing.Fail);
    }

    private static FakeOutcome RdpState(FakeView v)
    {
        var ts = v.Reg(Ccs + @"\Control\Terminal Server", "fDenyTSConnections");
        if (ts.Evidence == Evidence.Unavailable) return NotCheckable(v, ts.Key, ts.Status);
        bool on = ts.Evidence == Evidence.Present && Number(ts.Value) == 0;
        var nla = v.Reg(Ccs + @"\Control\Terminal Server\WinStations\RDP-Tcp", "UserAuthentication");
        var lines = new List<string> { on ? v.T("Remote Desktop on", "Remotedesktop an") : v.T("Remote Desktop off", "Remotedesktop aus") };
        if (on) lines.Add(nla.Evidence == Evidence.Present && Number(nla.Value) == 1 ? v.T("NLA required", "NLA verlangt") : v.T("NLA not required", "NLA nicht verlangt"));
        return new(ResultStatus.Info, null, on ? "on" : "off", ValueSource.Runtime, Details: lines, Evidence: [ts.Key]);
    }

    private static FakeOutcome StrongCrypto(FakeView v)
    {
        var path = @"SOFTWARE\Microsoft\.NETFramework\v4.0.30319";
        var r = v.Reg(path, "SchUseStrongCrypto");
        if (r.Status == ItemStatus.KeyMissing && v.Item(r.Key) is not null)
            return new(ResultStatus.NotApplicable, "1", null, ValueSource.NotSet, Details: [v.T(".NET Framework 4 not installed", ".NET Framework 4 nicht installiert")], Evidence: [r.Key]);
        return RegEq(v, path, "SchUseStrongCrypto", 1, WhenMissing.Fail);
    }
}
