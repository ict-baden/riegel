// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;

namespace Riegel.Core.Samples;

/// <summary>
/// Feste, reichhaltige Beispieldaten (spec 7.2): Berichtsdaten aller Dokumente und Läufe für die Pakete pdf,
/// data-outputs und app, solange Engine und Arbeitsablauf noch Platzhalter sind. Enthält jeden Status und jedes Flag,
/// drei Rechner plus einen Domänencontroller und einen mit veraltetem Export, jede Registerart und jeden
/// Eintragszustand, Banner und Maschinenidentität. Alle Werte sind erfunden: keine echten Namen, Rechner,
/// Hersteller oder Kunden (spec 7.9, Rollen statt Personen). Jeder Aufruf liefert gleiche Daten.
/// </summary>
public static class SampleData
{
    /// <summary>Auswertungstag der Beispiele.</summary>
    public static DateOnly Date { get; } = new(2026, 10, 18);

    /// <summary>Auswertungszeit der Beispiele.</summary>
    public static DateTimeOffset Now { get; } = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    public const string PlantId = "musterwerk-linie-3";
    public const string PlantName = "Musterwerk – Linie 3";

    // Drei Rechner, ein Domänencontroller, einer mit veraltetem Export (HIST-01)
    public const string Hmi = "HMI-L3-01";
    public const string Engineering = "ENG-L3-01";
    public const string Operations = "OPS-01";
    public const string DomainController = "DC-01";
    public const string Historian = "HIST-01";

    public static IReadOnlyList<string> Hosts { get; } = [Hmi, Engineering, Operations, DomainController, Historian];

    private const string RoleOwner = "Anlagenverantwortlicher";
    private const string RoleIsb = "Informationssicherheitsbeauftragter";
    private const string RoleQa = "Qualitätssicherung";
    private const string RoleBuilder = "Hersteller";

    private static readonly string Sha0 = new('0', 64);

    /// <summary>Werkzeug der Beispiele.</summary>
    public static ToolInfo Tool { get; } = new("Riegel", "0.1.0", "abc1234", "1111111111111111111111111111111111111111111111111111111111111111");

    /// <summary>Katalog der Beispiele.</summary>
    public static CatalogueRef Catalogue { get; } = new("riegel", "2026.10", new DateOnly(2026, 10, 18),
        "2222222222222222222222222222222222222222222222222222222222222222");

    /// <summary>SHA-256 der Beispielregister.</summary>
    public static RegisterHashes RegisterHashes { get; } = new(
        "3333333333333333333333333333333333333333333333333333333333333333",
        "4444444444444444444444444444444444444444444444444444444444444444",
        "5555555555555555555555555555555555555555555555555555555555555555");

    /// <summary>Eine importierte Kunden-Baseline (Inhalt nicht im Paket).</summary>
    public static BaselineRef Baseline { get; } = new("kunde-vorgaben", "Kundenvorgaben Linie 3 (fiktiv)", BaselineSourceKind.Csv,
        "6666666666666666666666666666666666666666666666666666666666666666", "kundenvorgaben.csv", new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), 4);

    // ---------------------------------------------------------------- Anlage

    /// <summary>Die Beispielanlage mit Gruppen, Rechnern (einer mit Maschinenidentität) und Einstellungen.</summary>
    public static Plant Plant() => new(SchemaIds.Plant, PlantId, PlantName, "Beispielanlage (fiktiv) für Entwicklung und Tests",
        [
            new MachineGroup("g-hmi", "Bedienstationen", MachineRole.Hmi),
            new MachineGroup("g-eng", "Engineering", MachineRole.EngineeringStation),
            new MachineGroup("g-srv", "Server", MachineRole.Historian),
        ],
        [
            new PlantMachine(Hmi, "g-hmi", "Bedienstation Linie 3", false, null, Identity()),
            new PlantMachine(Engineering, "g-eng", "Engineering-Station", false, null, null),
            new PlantMachine(Operations, "g-srv", "Betriebsserver", false, null, null),
            new PlantMachine(DomainController, null, "Domänencontroller", false, "nicht bewertet", null),
            new PlantMachine(Historian, "g-srv", "Historian", true, "ESU vorhanden", null),
        ],
        new PlantSettings(30, 30, 35, 90, "Vertraulich – enthält Sicherheitskonfiguration",
            [new(SignOffSteps.Created, RoleOwner), new(SignOffSteps.Reviewed, RoleIsb), new(SignOffSteps.Approved, RoleQa)],
            "de", ReportPreset.Audit, "Beispiel-Dienstleister (fiktiv)", null, [RoleIsb], RoleOwner))
    {
        Folder = null,
    };

    /// <summary>Maschinenidentität für den Auslieferungszustand (offensichtlich erfunden).</summary>
    public static MachineIdentity Identity() => new("Abfüllmaschine Typ X-100 (fiktiv)", "SN-0000-0001", "AUF-2026-0001",
        new DateOnly(2026, 3, 2), "Leitsystem Demo 4.2", "Beispiel-Anlagenbau", "Halle 3, Linie 3");

    // ---------------------------------------------------------------- Register

    /// <summary>Alle drei Register mit jedem Eintragszustand.</summary>
    public static RegisterSet Registers() => new(
        new ExceptionFile(SchemaIds.Exceptions, PlantId, Exceptions()),
        new TailoringFile(SchemaIds.Tailoring, PlantId, Tailoring()),
        new MeasureFile(SchemaIds.Measures, PlantId, Measures()),
        RegisterHashes);

    private static Approval Approved(DateOnly on, DateOnly reviewBy, string record, ExceptionEntry? forHash = null) =>
        new(RoleIsb, null, on, reviewBy, record, forHash is null ? null : ContentHashes.Of(forHash));

    private static RegisterScope Machine(string host) => new([host], [], false);

    /// <summary>EX-0001 gültig, -0002 beantragt, -0003 nach Freigabe geändert, -0004 läuft bald ab, -0005 abgelaufen,
    /// -0006 Wert weicht ab, -0007 nicht mehr nötig.</summary>
    public static IReadOnlyList<ExceptionEntry> Exceptions()
    {
        var created = new DateOnly(2026, 4, 1);
        ExceptionEntry Entry(string id, string rule, RegisterScope scope, ReasonCategory category, string justification,
            string? measure, string? reference, string? accepted) =>
            new(id, rule, scope, category, justification, measure, reference, accepted, Approval.None, created, RoleOwner, null, "2026.10", null);

        var valid = Entry("EX-0001", "LOGON-10", Machine(Hmi), ReasonCategory.VendorRequirement,
            "Die Bedienstation meldet sich laut Hersteller automatisch an, damit die Linie nach Stromausfall anläuft.",
            null, "Herstellerdokument HD-4711", "1");
        var changed = Entry("EX-0003", "NET-08", new RegisterScope([], ["g-hmi"], false), ReasonCategory.CompensatingControl,
            "Altes Engineering-Werkzeug scheitert an SMB-Signierung; Zugriff nur aus dem Leitstandnetz.",
            "Zugriff auf Port 445 nur aus dem Leitstandnetz (Firewall-Regel FW-L3-07)", null, "0");
        var changedApproved = changed with { Approval = Approved(new DateOnly(2026, 5, 4), new DateOnly(2027, 5, 4), "CHG-DEMO-0003", changed) };
        return
        [
            valid with { Approval = Approved(new DateOnly(2026, 4, 20), new DateOnly(2027, 4, 20), "CHG-DEMO-0001", valid) },
            Entry("EX-0002", "CRED-18", Machine(Engineering), ReasonCategory.TechnicalNecessity,
                "Fernwartung des Anlagenbauers braucht Administratorfreigaben im Arbeitsgruppenbetrieb.", null, null, "1"),
            // Inhalt nach der Freigabe geändert: der Hash passt nicht mehr
            changedApproved with { Justification = changed.Justification + " Ergänzt nach Freigabe.", ModifiedOn = new DateOnly(2026, 9, 1) },
            Entry("EX-0004", "SVC-01", Machine(Operations), ReasonCategory.TechnicalNecessity,
                "Der Betriebsserver ist zugleich Druckserver für die Etikettendrucker der Linie.", null, null, "Running") with
            {
                Approval = new Approval(RoleIsb, null, new DateOnly(2025, 11, 5), new DateOnly(2026, 11, 5), "CHG-DEMO-0004", null),
            },
            Entry("EX-0005", "NET-03", new RegisterScope([], [], true), ReasonCategory.RiskAccepted,
                "Namensauflösung über LLMNR wird für ein altes Bediengerät noch gebraucht; Ablösung geplant.", null, null, "*") with
            {
                Approval = new Approval(RoleIsb, null, new DateOnly(2025, 9, 30), new DateOnly(2026, 9, 30), "CHG-DEMO-0005", null),
            },
            Entry("EX-0006", "RDP-01", Machine(Engineering), ReasonCategory.VendorRequirement,
                "Das Fernwartungsgerät des Herstellers unterstützt keine Authentifizierung auf Netzwerkebene.", null,
                "Herstellerdokument HD-4711, Abschnitt 3", "1") with
            {
                Approval = new Approval(RoleIsb, null, new DateOnly(2026, 6, 1), new DateOnly(2027, 6, 1), "CHG-DEMO-0006", null),
            },
            Entry("EX-0007", "PS-02", Machine(Hmi), ReasonCategory.NotRelevantHere,
                "Auf der Bedienstation laufen keine Skripte; die Ausführungsrichtlinie spielt hier keine Rolle.", null, null, "Unrestricted") with
            {
                Approval = new Approval(RoleIsb, null, new DateOnly(2026, 4, 2), new DateOnly(2027, 4, 2), "CHG-DEMO-0007", null),
            },
        ];
    }

    /// <summary>TL-0001 gültig, TL-0002 beantragt, TL-0003 abgelaufen.</summary>
    public static IReadOnlyList<TailoringEntry> Tailoring() =>
    [
        new("TL-0001", "DEF-03", new RegisterScope([], ["g-hmi"], false),
            "Bedienstationen sind ohne Internetzugang; Cloud-Schutz ist in der Soll-Konfiguration dieser Rolle nicht vorgesehen.",
            new Approval(RoleIsb, null, new DateOnly(2026, 4, 20), new DateOnly(2027, 4, 20), "CHG-DEMO-0002", null),
            new DateOnly(2026, 4, 1), RoleOwner, null, "Vorschlag – vor Übernahme prüfen"),
        new("TL-0002", "ENC-04", new RegisterScope([], ["g-hmi"], false),
            "Fest eingebaute Bedienpulte: ein Kennwort vor dem Start ist für diese Rolle nicht vorgesehen.",
            Approval.None, new DateOnly(2026, 9, 15), RoleOwner, null, null),
        new("TL-0003", "RDP-08", Machine(Historian),
            "Historian-Sitzungen laufen dauerhaft für die Datenübernahme; Leerlaufgrenze nicht im Prüfumfang.",
            new Approval(RoleIsb, null, new DateOnly(2023, 9, 1), new DateOnly(2026, 9, 1), "CHG-DEMO-0008", null),
            new DateOnly(2023, 8, 15), RoleOwner, null, null),
    ];

    /// <summary>M-0001 geplant, M-0002 überfällig, M-0003 erledigt (Regel weiter offen), M-0004 verworfen.</summary>
    public static IReadOnlyList<MeasureEntry> Measures() =>
    [
        new("M-0001", "LIFE-02", new RegisterScope([], [], true), "Monatliche Updates im Wartungsfenster einspielen",
            RoleOwner, new DateOnly(2026, 12, 15), "TICKET-DEMO-11", MeasureState.Planned, new DateOnly(2026, 9, 1), RoleOwner, null, null),
        new("M-0002", "ENC-01", Machine(Engineering), "BitLocker auf der Engineering-Station aktivieren",
            RoleIsb, new DateOnly(2026, 9, 30), "TICKET-DEMO-12", MeasureState.Planned, new DateOnly(2026, 6, 1), RoleOwner, null, null),
        new("M-0003", "NET-01", Machine(Hmi), "SMBv1 abschalten (Herstellerfreigabe liegt vor)",
            RoleOwner, new DateOnly(2026, 10, 1), null, MeasureState.Done, new DateOnly(2026, 7, 1), RoleOwner, new DateOnly(2026, 10, 2), null),
        new("M-0004", "FW-07", new RegisterScope([], ["g-srv"], false), "Protokollierung verworfener Pakete einschalten",
            RoleIsb, new DateOnly(2026, 11, 30), null, MeasureState.Dropped, new DateOnly(2026, 5, 1), RoleOwner, new DateOnly(2026, 8, 1),
            "Ersetzt durch zentrale Protokollierung"),
    ];

    // ---------------------------------------------------------------- Ergebnisse und Läufe

    private sealed record Line(string Rule, ResultStatus Status, ResultFlags Flags = ResultFlags.None,
        string? Expected = null, string? Actual = null, ValueSource Source = ValueSource.Unknown, string? Detail = null,
        string? Exception = null, string? Tailoring = null, string? Measure = null, string? Reason = null, string[]? Details = null);

    private static readonly Dictionary<string, (string Title, string Category, Severity Severity)> RuleInfo = new()
    {
        ["NET-01"] = ("SMBv1 server disabled", "network", Severity.High),
        ["NET-03"] = ("LLMNR disabled", "network", Severity.Medium),
        ["NET-08"] = ("SMB server requires signing", "network", Severity.Medium),
        ["CRED-05"] = ("WDigest does not store credentials", "credentials", Severity.High),
        ["CRED-06"] = ("LSA protection running", "credentials", Severity.High),
        ["CRED-18"] = ("Remote UAC filtering for local accounts", "credentials", Severity.High),
        ["LOGON-10"] = ("No automatic logon", "logon", Severity.High),
        ["ACCT-14"] = ("Windows LAPS configured", "accounts", Severity.Medium),
        ["ACCT-15"] = ("Local administrators (inventory)", "accounts", Severity.Low),
        ["LIFE-01"] = ("Supported operating system", "lifecycle", Severity.High),
        ["LIFE-02"] = ("Days since last cumulative update", "lifecycle", Severity.High),
        ["DEF-03"] = ("Cloud-delivered protection", "defender", Severity.Medium),
        ["DEF-18"] = ("An antivirus is active and current", "defender", Severity.High),
        ["ENC-01"] = ("BitLocker on the OS volume", "encryption", Severity.Medium),
        ["ENC-04"] = ("Pre-boot PIN on mobile devices", "encryption", Severity.Low),
        ["SVC-01"] = ("Print Spooler not running on servers", "services", Severity.High),
        ["RDP-01"] = ("Network Level Authentication required", "rdp", Severity.High),
        ["RDP-08"] = ("Idle session limit", "rdp", Severity.Low),
        ["PS-02"] = ("PowerShell execution policy", "powershell", Severity.Low),
        ["AUD-01"] = ("Audit credential validation", "audit", Severity.Medium),
        ["FW-01"] = ("Firewall on (domain profile)", "firewall", Severity.High),
        ["FW-07"] = ("Log dropped packets (domain profile)", "firewall", Severity.Low),
        ["TLS-03"] = ("TLS 1.0 server disabled", "tls", Severity.Medium),
        ["MED-01"] = ("AutoRun disabled", "media", Severity.Medium),
        ["URA-05"] = ("Debug programs only for administrators", "userRights", Severity.High),
        ["APP-01"] = ("Application control enforced", "appControl", Severity.Medium),
        ["SYS-07"] = ("Administrative shares (inventory)", "system", Severity.Low),
        ["IMP-kunde-vorgaben-0001"] = ("Customer requirement: OPC UA port only", "network", Severity.Medium),
    };

    private static RuleResult Result(Line l)
    {
        var (_, category, severity) = RuleInfo[l.Rule];
        return new RuleResult(l.Rule.StartsWith("IMP-", StringComparison.Ordinal) ? Baseline.Id : "riegel", l.Rule, l.Status, severity,
            l.Expected, l.Actual, l.Source, l.Detail, [EvidenceFor(l.Rule)], l.Exception, l.Tailoring, l.Measure, l.Flags,
            l.Reason, l.Details);
    }

    private static string EvidenceFor(string rule) => rule switch
    {
        "NET-01" => "wmi:root\\Microsoft\\Windows\\SMB:MSFT_SmbServerConfiguration.GetConfiguration",
        "NET-08" => @"reg:HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters",
        "LOGON-10" => @"reg:HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon",
        "CRED-06" => @"reg:HKLM\SYSTEM\CurrentControlSet\Control\Lsa",
        "ACCT-15" => ItemKeys.LocalAdministrators,
        "DEF-18" => "wmi:root\\SecurityCenter2:AntivirusProduct",
        "URA-05" => ItemKeys.LsaRights,
        "AUD-01" => ItemKeys.AuditPolicy,
        _ => @"reg:HKLM\SOFTWARE\Policies",
    };

    /// <summary>Ergebnisse je Rechner. Zusammen enthalten sie jeden Status, jedes Flag und jede Wertherkunft.</summary>
    public static IReadOnlyList<RuleResult> Results(string host) => host switch
    {
        Hmi =>
        [
            Result(new("NET-01", ResultStatus.Fail, ResultFlags.Regression, "false", "true", ValueSource.Runtime, null, null, null, "M-0003")),
            Result(new("NET-08", ResultStatus.Pass, ResultFlags.FromDefault, "1", null, ValueSource.OsDefault)),
            Result(new("LOGON-10", ResultStatus.Exception, ResultFlags.None, "0", "1", ValueSource.LocalPolicy, null, "EX-0001",
                Details: ["Kennwort in Registry"])),
            Result(new("DEF-03", ResultStatus.OutOfScope, ResultFlags.None, "2", "0", ValueSource.DirectlySet, null, null, "TL-0001")),
            Result(new("ENC-04", ResultStatus.NotApplicable, ResultFlags.TailoringPending, null, null, ValueSource.NotSet, null, null, "TL-0002",
                Reason: "RGL-0220")),
            Result(new("CRED-06", ResultStatus.NotCheckable, ResultFlags.ConfiguredOnly, "2", "2", ValueSource.LocalPolicy, Reason: "RGL-0230")),
            Result(new("ACCT-15", ResultStatus.Info, ResultFlags.None, null, null, ValueSource.Runtime,
                Details: ["S-1-5-21-1000-2000-3000-500 (deaktiviert)", "S-1-5-21-1000-2000-3000-1001"])),
            Result(new("PS-02", ResultStatus.Pass, ResultFlags.None, "RemoteSigned", "RemoteSigned", ValueSource.Mdm)),
            Result(new("LIFE-02", ResultStatus.Fail, ResultFlags.MeasurePlanned, "35", "41", ValueSource.Runtime, null, null, null, "M-0001",
                Details: ["mindestens 41 Tage"])),
            Result(new("AUD-01", ResultStatus.Fail, ResultFlags.NewRule, "successAndFailure", "noAuditing", ValueSource.Unknown)),
        ],
        Engineering =>
        [
            Result(new("CRED-18", ResultStatus.Fail, ResultFlags.ExceptionPending, "0", "1", ValueSource.LocalPolicy, null, "EX-0002")),
            Result(new("RDP-01", ResultStatus.Fail, ResultFlags.ExceptionValueMismatch, "1", "0", ValueSource.DomainGpo, "GPO Linie-3-Basis",
                "EX-0006")),
            Result(new("NET-08", ResultStatus.Fail, ResultFlags.ExceptionChangedAfterApproval, "1", "0", ValueSource.LocalPolicy, null, "EX-0003")),
            Result(new("ENC-01", ResultStatus.Fail, ResultFlags.MeasureOverdue, "1", "0", ValueSource.Runtime, null, null, null, "M-0002")),
            Result(new("DEF-18", ResultStatus.NotCheckable, ResultFlags.Heuristic, null, "397568", ValueSource.Runtime, Reason: "RGL-0231",
                Details: ["Drittanbieter-Virenschutz laut SecurityCenter2 (Heuristik)"])),
            Result(new("DEF-03", ResultStatus.NotApplicable, ResultFlags.Derived, null, null, ValueSource.Runtime, Reason: "RGL-0221",
                Details: ["Drittanbieter-Virenschutz aktiv"])),
            Result(new("URA-05", ResultStatus.Pass, ResultFlags.None, "S-1-5-32-544", "S-1-5-32-544", ValueSource.Runtime)),
            Result(new("IMP-kunde-vorgaben-0001", ResultStatus.Pass, ResultFlags.None, "4840", "4840", ValueSource.Runtime)),
        ],
        Operations =>
        [
            Result(new("SVC-01", ResultStatus.Exception, ResultFlags.ExceptionExpiring, "Stopped", "Running", ValueSource.Runtime, null, "EX-0004")),
            Result(new("NET-03", ResultStatus.Fail, ResultFlags.ExceptionExpired, "0", null, ValueSource.NotSet, null, "EX-0005")),
            Result(new("LIFE-02", ResultStatus.NotCheckable, ResultFlags.ClockSuspect, "35", null, ValueSource.Runtime, null, null, null, "M-0001",
                Reason: "RGL-0232")),
            Result(new("FW-01", ResultStatus.Pass, ResultFlags.None, "true", "true", ValueSource.Runtime)),
            Result(new("TLS-03", ResultStatus.Fail, ResultFlags.None, "0", "1", ValueSource.DirectlySet)),
            Result(new("MED-01", ResultStatus.Pass, ResultFlags.None, "255", "255", ValueSource.DomainGpo, "GPO Linie-3-Basis")),
        ],
        DomainController =>
        [
            Result(new("ACCT-14", ResultStatus.NotApplicable, ResultFlags.None, null, null, ValueSource.Unknown, Reason: "RGL-0222")),
            Result(new("FW-01", ResultStatus.Pass, ResultFlags.None, "true", "true", ValueSource.Runtime)),
            Result(new("SYS-07", ResultStatus.Info, ResultFlags.None, null, null, ValueSource.Runtime, Details: ["ADMIN$", "C$", "IPC$"])),
        ],
        Historian =>
        [
            Result(new("LIFE-01", ResultStatus.Pass, ResultFlags.StaleSnapshot, "supported", "2027-01-12", ValueSource.Runtime)),
            Result(new("RDP-08", ResultStatus.Fail, ResultFlags.TailoringPending, "900000", null, ValueSource.NotSet, null, null, "TL-0003")),
            Result(new("URA-05", ResultStatus.NotCheckable, ResultFlags.None, null, null, ValueSource.Unknown, Reason: "RGL-0203")),
            Result(new("APP-01", ResultStatus.Fail, ResultFlags.None, "enforced", "off", ValueSource.Runtime)),
            Result(new("CRED-05", ResultStatus.Pass, ResultFlags.FromDefault, "0", null, ValueSource.OsDefault)),
        ],
        _ => [],
    };

    private static RunSummary Summarize(IReadOnlyList<RuleResult> results, int? score, int? documented, int share, int regressions = 0,
        int newlyOpen = 0, int expiring = 0, int expired = 0, int pending = 0, int overdue = 0)
    {
        int C(ResultStatus s) => results.Count(r => r.Status == s);
        int O(Severity s) => results.Count(r => r.Status == ResultStatus.Fail && r.Severity == s);
        return new RunSummary(C(ResultStatus.Pass), C(ResultStatus.Fail), C(ResultStatus.Exception), C(ResultStatus.OutOfScope),
            C(ResultStatus.NotApplicable), C(ResultStatus.NotCheckable), C(ResultStatus.Info), O(Severity.High), O(Severity.Medium),
            O(Severity.Low), regressions, newlyOpen, expiring, expired, pending, overdue, score, documented, share);
    }

    private static readonly Dictionary<string, (string RunId, DateTimeOffset Collected, bool Admin, RunFlags Flags, int? Score, int? Documented, int Share, string? Previous)> RunInfoByHost = new()
    {
        [Hmi] = ("20261018T090000Z-a1b2c3", new DateTimeOffset(2026, 10, 16, 7, 30, 0, TimeSpan.Zero), true, RunFlags.None, 58, 81, 92, "20260918T090000Z-0a1b2c"),
        [Engineering] = ("20261018T090100Z-b2c3d4", new DateTimeOffset(2026, 10, 15, 13, 5, 0, TimeSpan.Zero), true, RunFlags.MachineIdentityChanged, 37, 55, 94, null),
        [Operations] = ("20261018T090200Z-c3d4e5", new DateTimeOffset(2026, 10, 17, 22, 40, 0, TimeSpan.Zero), true, RunFlags.ClockSuspect, 72, 88, 95, null),
        [DomainController] = ("20261018T090300Z-d4e5f6", new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero), true, RunFlags.DomainController, null, null, 90, null),
        [Historian] = ("20261018T090400Z-e5f6a7", new DateTimeOffset(2026, 8, 3, 6, 15, 0, TimeSpan.Zero), false, RunFlags.StaleSnapshot | RunFlags.Incomplete, 61, 61, 62, null),
    };

    /// <summary>Ein Lauf je Rechner (neuester zuerst nach Rechnerliste) plus ein älterer Lauf von HMI-L3-01 für den Vergleich.</summary>
    public static IReadOnlyList<Run> Runs()
    {
        var runs = Hosts.Select(Run).ToList();
        var hmi = runs[0];
        var previousResults = hmi.Results.Select(r => r.RuleId == "NET-01" ? r with { Status = ResultStatus.Pass, Flags = ResultFlags.None, Actual = "false" } : r)
            .Where(r => r.RuleId != "AUD-01").ToList();
        runs.Add(hmi with
        {
            Id = "20260918T090000Z-0a1b2c",
            EvaluatedAtUtc = new DateTimeOffset(2026, 9, 18, 9, 0, 0, TimeSpan.Zero),
            EvaluationDate = new DateOnly(2026, 9, 18),
            CollectedAtUtc = new DateTimeOffset(2026, 9, 17, 7, 30, 0, TimeSpan.Zero),
            Snapshot = new FileRef("snapshots/HMI-L3-01/HMI-L3-01_20260917T073000Z.snapshot.json", "7777777777777777777777777777777777777777777777777777777777777777"),
            Results = previousResults,
            Summary = Summarize(previousResults, 63, 84, 91),
            PreviousRunId = null,
            Flags = RunFlags.None,
        });
        return runs;
    }

    /// <summary>Der neueste Beispiellauf eines Rechners.</summary>
    public static Run Run(string host)
    {
        var info = RunInfoByHost[host];
        var results = Results(host);
        int regressions = results.Count(r => (r.Flags & ResultFlags.Regression) != 0);
        var summary = Summarize(results, info.Score, info.Documented, info.Share, regressions, newlyOpen: results.Count(r => (r.Flags & ResultFlags.NewRule) != 0),
            expiring: results.Count(r => (r.Flags & ResultFlags.ExceptionExpiring) != 0), expired: results.Count(r => (r.Flags & ResultFlags.ExceptionExpired) != 0),
            pending: results.Count(r => (r.Flags & ResultFlags.ExceptionPending) != 0), overdue: results.Count(r => (r.Flags & ResultFlags.MeasureOverdue) != 0));
        var stamp = FileNames.Timestamp(info.Collected);
        return new Run(SchemaIds.Run, info.RunId, host, Now, Date, info.Collected, info.Collected.AddHours(1),
            new FileRef($"snapshots/{host}/{host}_{stamp}.snapshot.json", Hashes.Sha256Hex(host + stamp)), Tool, Catalogue,
            host == Engineering ? [Baseline] : [], RegisterHashes, info.Admin, new CollectionOptions(host == Historian, false, false),
            results, summary, Categories(results, host == DomainController), Facts(host), info.Previous, host == Hmi ? "20260801T090000Z-9f8e7d" : null,
            null, info.Flags);
    }

    private static IReadOnlyList<CategoryScore> Categories(IReadOnlyList<RuleResult> results, bool isDc) =>
        RuleCategories.All.Select(category =>
        {
            var inCategory = results.Where(r => RuleInfo.TryGetValue(r.RuleId, out var i) && i.Category == category).ToList();
            int pass = inCategory.Count(r => r.Status == ResultStatus.Pass);
            int fail = inCategory.Count(r => r.Status == ResultStatus.Fail);
            int exception = inCategory.Count(r => r.Status == ResultStatus.Exception);
            int? score = isDc || pass + fail + exception == 0 ? null : (int)Math.Round(100.0 * pass / (pass + fail + exception), MidpointRounding.AwayFromZero);
            return new CategoryScore(category, pass, fail, exception, score, pass + fail + exception, category == RuleCategories.Audit && fail > 0);
        }).ToList();

    /// <summary>Fakten (spec 2.5) eines Beispielrechners; unbekannte als JSON null.</summary>
    public static IReadOnlyDictionary<string, JsonElement> Facts(string host)
    {
        bool dc = host == DomainController;
        bool server = host is Operations or DomainController or Historian;
        return new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["os.kind"] = JsonValues.Text(server ? "server" : "client"),
            ["os.build"] = JsonValues.Number(host switch { Hmi => 17763, Engineering => 26100, Operations => 20348, DomainController => 20348, _ => 14393 }),
            ["domain.joined"] = JsonValues.Bool(host is Operations or DomainController),
            ["domain.isDc"] = JsonValues.Bool(dc),
            ["defender.mode"] = JsonValues.Text(host == Engineering ? "passive" : "active"),
            ["av.thirdPartyActive"] = JsonValues.Bool(host == Engineering),
            ["office.installed"] = host == Historian ? JsonValues.Null : JsonValues.False,
            ["rdp.enabled"] = JsonValues.Bool(host is Engineering or Operations),
            ["machine.mobile"] = JsonValues.False,
            ["net.gatewayConfigured"] = JsonValues.Bool(host != Hmi),
            ["net.wireless"] = JsonValues.Bool(host == Engineering),
        };
    }

    // ---------------------------------------------------------------- Exporte

    /// <summary>Ein kleiner Beispielexport (Herkunft synthetic) mit jedem Eintragsstatus.</summary>
    public static Snapshot Snapshot(string host)
    {
        var info = RunInfoByHost.TryGetValue(host, out var i) ? i : RunInfoByHost[Hmi];
        var origin = host switch { Engineering => SnapshotOrigin.App, Historian => SnapshotOrigin.Builtin, _ => SnapshotOrigin.Synthetic };
        return new Snapshot(SchemaIds.Snapshot, new CollectorInfo("Riegel.Collector", "0.1.0", Sha0, "4.0.30319.42000", true), info.Collected,
            info.Admin, origin, new CollectionOptions(host == Historian, false, false),
            new MachineInfo(host, host is Operations or DomainController ? "corp.example" : null, host == DomainController ? 5 : host == Operations ? 3 : 0,
                host is Operations or DomainController or Historian ? 3 : 1, host == Historian ? 14393 : 17763, 1234, "EnterpriseS", "Client",
                "1809", "AMD64", "00000000-0000-4000-8000-000000000001", "de-DE", info.Collected.AddDays(-3)),
            [
                new CoverageEntry(@"HKLM\SOFTWARE\Policies", true, 8, true, [], null),
                new CoverageEntry(@"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", false, 0, true, [], null),
            ],
            [
                new SnapshotItem(@"reg:HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", ItemStatus.Ok,
                    JsonValues.Parse("""{"values":{"RequireSecuritySignature":{"t":"dword","v":0}},"subkeys":[]}"""), null, null, 2),
                new SnapshotItem(@"reg:HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", ItemStatus.KeyMissing, null, null, null, 0),
                new SnapshotItem(@"reg:HKLM\SYSTEM\CurrentControlSet\Control\Lsa", ItemStatus.ValueMissing,
                    JsonValues.Parse("""{"values":{},"subkeys":[]}"""), null, null, 1),
                new SnapshotItem(ItemKeys.LsaRights, ItemStatus.Denied, null, 5, "Access denied", 3),
                new SnapshotItem("wmi:root\\Microsoft\\Windows\\DeviceGuard:Win32_DeviceGuard", ItemStatus.NamespaceMissing, null, unchecked((int)0x8004100E), "Invalid namespace", 4),
                new SnapshotItem(ItemKeys.WindowsUpdateHistory, ItemStatus.NotCollected, null, null, "wuauserv not running", 0),
                new SnapshotItem(ItemKeys.LsassProtection, ItemStatus.NotApplicable, null, null, null, 0),
                new SnapshotItem(ItemKeys.AuditPolicy, ItemStatus.Error, null, 1314, "Privilege not held", 2),
            ]);
    }

    // ---------------------------------------------------------------- Berichtsdaten

    /// <summary>Daten des Anlagenberichts (O4/O5/O7/O3) mit allen Bannern, Registersichten und Rechnern.</summary>
    public static ReportData Report()
    {
        var plant = Plant();
        var runs = Hosts.Select(Run).ToList();
        var exceptions = Exceptions();
        var header = new ReportHeader(PlantId, PlantName, plant.Description, "RPT-20261018T090500Z", ReportPreset.Audit, "de", Date, Now, Tool,
            Catalogue, [Baseline], RegisterHashes, plant.Settings.PerformedBy, null, plant.Settings.Confidentiality!, [.. Hosts], false);
        var banners = new List<Banner>
        {
            new(BannerKind.IncompleteCheck, 62, [Historian], null, ["audit", "userRights"]),
            new(BannerKind.StaleSnapshot, null, [Historian], 76, []),
            new(BannerKind.StaleCatalogue, null, [], 190, []),
            new(BannerKind.PreTranslation, null, [], null, []),
            new(BannerKind.ExperimentalGpoAttribution, null, [Engineering, Operations], null, []),
            new(BannerKind.ClockSuspect, null, [Operations], null, []),
            new(BannerKind.DomainController, null, [DomainController], null, []),
        };
        var counts = new ReportCounts(5, 4, 6, 4, 1, 1, 1, 4, 2, 1, 1, 1, 3, 1, 1, 0, 1, 1);
        var findings = new List<AggregatedFinding>
        {
            new("riegel", "NET-01", "SMBv1-Server deaktiviert", "network", Severity.High, 1, 4, [Hmi], false,
                new MeasureSummary("M-0003", RoleOwner, new DateOnly(2026, 10, 1), true), ResultFlags.Regression),
            new("riegel", "LIFE-02", "Tage seit dem letzten kumulativen Update", "lifecycle", Severity.High, 1, 4, [Hmi], false,
                new MeasureSummary("M-0001", RoleOwner, new DateOnly(2026, 12, 15), false), ResultFlags.MeasurePlanned),
            new("riegel", "CRED-18", "UAC-Remotefilter für lokale Konten", "credentials", Severity.High, 1, 4, [Engineering], false, null, ResultFlags.ExceptionPending),
            new("riegel", "RDP-01", "Authentifizierung auf Netzwerkebene verlangt", "rdp", Severity.High, 1, 3, [Engineering], false, null, ResultFlags.ExceptionValueMismatch),
            new("riegel", "ENC-01", "BitLocker auf dem Systemlaufwerk", "encryption", Severity.Medium, 1, 4, [Engineering], false,
                new MeasureSummary("M-0002", RoleIsb, new DateOnly(2026, 9, 30), true), ResultFlags.MeasureOverdue),
            new("riegel", "TLS-03", "TLS 1.0 (Server) deaktiviert", "tls", Severity.Medium, 12, 14,
                ["SRV-A01", "SRV-A02", "SRV-A03", "SRV-A04", "SRV-A05", "SRV-A06", "SRV-A07", "SRV-A08", "SRV-A09", Operations], true, null, ResultFlags.None),
        };
        var changes = new List<HostChange>
        {
            new(Hmi, ComparisonBasis.Previous, "20260918T090000Z-0a1b2c", runs[0].Id, "SMBv1-Server deaktiviert", Severity.High,
                new StatusChange("riegel", "NET-01", StatusChangeKind.Worse, ResultStatus.Pass, ResultStatus.Fail, ResultFlags.None, ResultFlags.Regression, null)),
            new(Hmi, ComparisonBasis.Approved, "20260801T090000Z-9f8e7d", runs[0].Id, "Überwachung der Anmeldeinformationsprüfung", Severity.Medium,
                new StatusChange("riegel", "AUD-01", StatusChangeKind.NewRule, null, ResultStatus.Fail, ResultFlags.None, ResultFlags.NewRule, null)),
            new(Operations, ComparisonBasis.Previous, null, runs[2].Id, "LLMNR deaktiviert", Severity.Medium,
                new StatusChange("riegel", "NET-03", StatusChangeKind.ExceptionExpired, ResultStatus.Exception, ResultStatus.Fail, ResultFlags.None, ResultFlags.ExceptionExpired, "EX-0005")),
            new(Engineering, ComparisonBasis.Previous, null, runs[1].Id, "Authentifizierung auf Netzwerkebene verlangt", Severity.High,
                new StatusChange("riegel", "RDP-01", StatusChangeKind.ScopeChanged, ResultStatus.NotCheckable, ResultStatus.Fail, ResultFlags.None, ResultFlags.ExceptionValueMismatch, null)),
        };
        ExceptionView View(ExceptionEntry e, RegisterEntryState state, RegisterFlags flags, string title, Severity severity, params string[] hosts) =>
            new(e, state, flags, title, severity, hosts, ContentHashes.Of(e));
        var exceptionViews = new List<ExceptionView>
        {
            View(exceptions[0], RegisterEntryState.Valid, RegisterFlags.None, "Keine automatische Anmeldung", Severity.High, Hmi),
            View(exceptions[1], RegisterEntryState.Pending, RegisterFlags.None, "UAC-Remotefilter für lokale Konten", Severity.High, Engineering),
            View(exceptions[2], RegisterEntryState.ChangedAfterApproval, RegisterFlags.Overlap, "SMB-Server verlangt Signierung", Severity.Medium, Hmi, Engineering),
            View(exceptions[3], RegisterEntryState.Expiring, RegisterFlags.None, "Druckwarteschlange auf Servern aus", Severity.High, Operations),
            View(exceptions[5], RegisterEntryState.Valid, RegisterFlags.ValueMismatch, "Authentifizierung auf Netzwerkebene verlangt", Severity.High, Engineering),
            View(exceptions[6], RegisterEntryState.Valid, RegisterFlags.Unused | RegisterFlags.RuleRetired, "Ausführungsrichtlinie von PowerShell", Severity.Low, Hmi),
            new(exceptions[1] with { Id = "EX-0008", Justification = "zu kurz" }, RegisterEntryState.Invalid, RegisterFlags.None,
                "UAC-Remotefilter für lokale Konten", Severity.High, [Engineering], ContentHashes.Of(exceptions[1] with { Id = "EX-0008", Justification = "zu kurz" })),
        };
        var expired = new List<ExceptionView> { View(exceptions[4], RegisterEntryState.Expired, RegisterFlags.None, "LLMNR deaktiviert", Severity.Medium, Hmi, Engineering, Operations, DomainController, Historian) };
        var tailoring = Tailoring();
        var tailoringViews = new List<TailoringView>
        {
            new(tailoring[0], RegisterEntryState.Valid, RegisterFlags.None, "Cloudbasierter Schutz", Severity.Medium, [Hmi], ContentHashes.Of(tailoring[0])),
            new(tailoring[1], RegisterEntryState.Pending, RegisterFlags.None, "Kennwort vor dem Start auf Mobilgeräten", Severity.Low, [Hmi], ContentHashes.Of(tailoring[1])),
            new(tailoring[2], RegisterEntryState.Expired, RegisterFlags.None, "Leerlaufgrenze für Sitzungen", Severity.Low, [Historian], ContentHashes.Of(tailoring[2])),
        };
        var measures = Measures();
        var measureViews = new List<MeasureView>
        {
            new(measures[0], false, RegisterFlags.None, "Tage seit dem letzten kumulativen Update", Severity.High, [Hmi, Operations], 1),
            new(measures[1], true, RegisterFlags.None, "BitLocker auf dem Systemlaufwerk", Severity.Medium, [Engineering], 1),
            new(measures[2], false, RegisterFlags.MeasureDoneButFailing, "SMBv1-Server deaktiviert", Severity.High, [Hmi], 1),
            new(measures[3], false, RegisterFlags.None, "Verworfene Pakete protokollieren (Domäne)", Severity.Low, [Operations, Historian], 0),
        };
        var machines = runs.Select(r => MachineReportFor(plant, r)).ToList();
        var insurer = Hosts.Select(Insurer).ToList();
        var allResults = runs.SelectMany(r => r.Results).ToList();
        var categories = Categories(allResults, false);
        var signOff = new SignOffData(plant.Settings.SignOff, RegisterHashes.Exceptions, exceptions.Count, 3, RegisterHashes.Tailoring,
            tailoring.Count, RegisterHashes.Measures, measures.Count, [.. runs.Select(r => r.Id)], header.ReportId);
        var method = new MethodInfo(RuleSources.All, 153, 146, 7, new ProvenanceCounts(146, 61, 52, 28, 5, 97), Frameworks.SupportedEvidence, true);
        var baselines = new List<BaselineSection> { new(Baseline, 1, 0, 0, 3, 100, false) };
        return new ReportData(header, banners, counts, new ScoreView(61, 79, 87, true, true), categories, findings, changes, expired,
            exceptionViews, tailoringViews, measureViews, machines, insurer, baselines, signOff, method);
    }

    private static MachineReport MachineReportFor(Plant plant, Run run)
    {
        var entry = plant.Machines.First(m => m.HostName == run.HostName);
        var group = plant.Groups.FirstOrDefault(g => g.Id == entry.GroupId);
        var snapshot = Snapshot(run.HostName);
        var views = run.Results.Select(r => new RuleResultView(r, RuleInfo[r.RuleId].Title, RuleInfo[r.RuleId].Category,
            r.Status == ResultStatus.Info ? RuleKind.Inventory : RuleKind.Check, "Begründung der Regel (Beispiel).",
            r.RuleId == "NET-08" ? "Alte Engineering-Werkzeuge scheitern teils an der Signierung." : null, "Abhilfe (Beispiel).",
            ["ACSC: Hardening Microsoft Windows 11 workstations", "ATTACK: M1037"])).ToList();
        var worse = run.HostName == Hmi
            ? new List<HostChange>
            {
                new(Hmi, ComparisonBasis.Previous, "20260918T090000Z-0a1b2c", run.Id, "SMBv1-Server deaktiviert", Severity.High,
                    new StatusChange("riegel", "NET-01", StatusChangeKind.Worse, ResultStatus.Pass, ResultStatus.Fail, ResultFlags.None, ResultFlags.Regression, null)),
            }
            : [];
        var drift = run.HostName == Hmi
            ? new DriftSummary(3,
            [
                new(@"reg:HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "SMB1", "0", "1", DriftKind.Changed),
                new("wmi:root\\cimv2:Win32_Service", "Spooler/StartMode", "Disabled", "Auto", DriftKind.Changed),
                new(ItemKeys.LocalAdministrators, "S-1-5-21-1000-2000-3000-1003", null, "S-1-5-21-1000-2000-3000-1003", DriftKind.Added),
            ])
            : DriftSummary.None;
        var facts = Facts(run.HostName).Where(f => !f.Key.StartsWith("net.", StringComparison.Ordinal))
            .Select(f => new FactView(f.Key, CanonicalValue.Render(f.Value))).ToList();
        var network = Facts(run.HostName).Where(f => f.Key.StartsWith("net.", StringComparison.Ordinal))
            .Select(f => new FactView(f.Key, CanonicalValue.Render(f.Value))).ToList();
        var inventory = run.Results.Where(r => r.Status == ResultStatus.Info)
            .Select(r => new InventoryView(r.RuleId, RuleInfo[r.RuleId].Title, r.RuleId == "SYS-07" ? InventoryRenderer.AdminShares : InventoryRenderer.LocalAdmins,
                r.Details ?? [])).ToList();
        var lifecycle = run.HostName == Historian
            ? new LifecycleView("Windows Server 2016", new DateOnly(2027, 1, 12), null, true, true)
            : new LifecycleView("Windows 10 Enterprise LTSC 2019", new DateOnly(2029, 1, 9), null, false, true);
        var collection = new CollectionInfo(run.CollectedAtUtc, Date.DayNumber - DateOnly.FromDateTime(run.CollectedAtUtc.UtcDateTime).DayNumber,
            run.ImportedAtUtc, run.IsAdmin, run.Options, snapshot.Origin, snapshot.Collector.Name, snapshot.Collector.Version,
            snapshot.Collector.ExeSha256, run.Snapshot.Sha256, "20261017T120000Z-1a2b3c");
        return new MachineReport(run.HostName, entry.Label, entry.GroupId, group?.Name, group?.Role, entry.Identity, snapshot.Machine,
            collection, run.Id, run.Summary, run.Categories, views, worse, drift, facts, network, inventory, lifecycle, run.Flags);
    }

    private static InsurerFactsRow Insurer(string host) => host switch
    {
        Hmi => new(host, "Windows 10 Enterprise LTSC 2019", 17763, new DateOnly(2029, 1, 9), true, "Microsoft Defender Antivirus", "on", 0, 41,
            "2026-09", 2, false, null, true, false, true, new NetworkFacts(false, 1, false, false, false)),
        Engineering => new(host, "Windows 11 Enterprise 24H2", 26100, new DateOnly(2027, 10, 12), true, "Beispiel-Virenschutz (fiktiv)", "on", 1, 3,
            "2026-10", 3, true, false, false, false, true, new NetworkFacts(true, 2, true, false, true)),
        Operations => new(host, "Windows Server 2022", 20348, new DateOnly(2031, 10, 14), true, "Microsoft Defender Antivirus", "on", 0, null,
            null, 4, true, true, false, null, true, new NetworkFacts(true, 1, false, false, false)),
        DomainController => new(host, "Windows Server 2022", 20348, new DateOnly(2031, 10, 14), true, "Microsoft Defender Antivirus", "on", 0, 9,
            "2026-10", null, false, null, false, true, true, new NetworkFacts(true, 1, false, false, false)),
        _ => new(host, "Windows Server 2016", 14393, new DateOnly(2027, 1, 12), true, null, null, null, null, null, null, null, null, null, null, null,
            new NetworkFacts(null, null, null, null, null)),
    };

    /// <summary>Übersicht für die Geschäftsführung (O6) über drei Anlagen.</summary>
    public static SummaryData Summary()
    {
        static IReadOnlyList<TrendPoint> Trend(params int?[] values) =>
        [
            new(new DateOnly(2026, 6, 30), values[0]), new(new DateOnly(2026, 7, 31), values[1]),
            new(new DateOnly(2026, 8, 31), values[2]), new(new DateOnly(2026, 9, 30), values[3]),
        ];
        var rows = new List<SummaryPlantRow>
        {
            new(PlantId, PlantName, 5, 100, 6, Trend(9, 8, 7, 5), 1, 1, 1, 61, 79, 87, true),
            new("musterwerk-linie-1", "Musterwerk – Linie 1", 12, 92, 2, Trend(4, 3, 3, 2), 0, 2, 0, 84, 91, 96, false),
            new("labor-nord", "Labor Nord (fiktiv)", 7, 71, 11, Trend(null, 14, 12, 11), 2, 0, 3, 47, 66, 90, false),
        };
        var report = Report();
        return new SummaryData(Date, "de", Now, Tool, rows, [.. report.Findings.Where(f => f.Severity == Severity.High).Take(5)],
            [new Banner(BannerKind.IncompleteCheck, 62, [Historian], null, ["audit"]), new Banner(BannerKind.StaleSnapshot, null, [Historian], 76, [])],
            "Vertraulich – enthält Sicherheitskonfiguration", false);
    }

    /// <summary>Soll-Konfiguration (O13) mit zwei Gruppen.</summary>
    public static TargetConfigData TargetConfig()
    {
        var tailoring = Tailoring();
        TargetConfigRule Rule(string id, string expectation, string[] sources, bool msb, string? applies = null) =>
            new(id.StartsWith("IMP-", StringComparison.Ordinal) ? Baseline.Id : "riegel", id, RuleInfo[id].Title, RuleInfo[id].Category,
                RuleInfo[id].Severity, id is "ACCT-15" or "SYS-07" ? RuleKind.Inventory : RuleKind.Check, expectation, sources, msb, applies);
        var common = new List<TargetConfigRule>
        {
            Rule("NET-01", "SMBv1 server disabled (runtime)", ["MSDOCS-CCBY"], true),
            Rule("NET-08", "RequireSecuritySignature = 1", ["MSDOCS-CCBY", "ACSC"], true),
            Rule("LOGON-10", "AutoAdminLogon ≠ 1", ["ACSC"], true),
            Rule("ACCT-14", "Windows LAPS configured", ["MSDOCS-CCBY"], false, "domain-joined machines only"),
            Rule("ACCT-15", "list of local administrators", [], false),
        };
        var groups = new List<TargetConfigGroup>
        {
            new("g-hmi", "Bedienstationen", MachineRole.Hmi, [Hmi], common,
                [new(tailoring[0], RegisterEntryState.Valid, RegisterFlags.None, "Cloudbasierter Schutz", Severity.Medium, [Hmi], ContentHashes.Of(tailoring[0]))]),
            new("g-eng", "Engineering", MachineRole.EngineeringStation, [Engineering],
                [.. common, Rule("IMP-kunde-vorgaben-0001", "TCP 4840 only (customer requirement)", [], false)], []),
        };
        return new TargetConfigData(PlantId, PlantName, "de", Date, Now, Tool, Catalogue, groups, [Baseline],
            new ProvenanceCounts(146, 61, 52, 28, 5, 97), Plant().Settings.SignOff, RegisterHashes.Tailoring,
            "Vertraulich – enthält Sicherheitskonfiguration", false);
    }

    /// <summary>„Ausnahmen zur Genehmigung“ (O12) mit einer beantragten, einer geänderten Ausnahme und einem Zuschnitt.</summary>
    public static ApprovalSheetData ApprovalSheet()
    {
        var exceptions = Exceptions();
        var tailoring = Tailoring();
        var entries = new List<ApprovalSheetEntry>
        {
            new(RegisterKind.Exceptions, exceptions[1].Id, exceptions[1].RuleId, "UAC-Remotefilter für lokale Konten", Severity.High,
                exceptions[1].Scope, exceptions[1].Category, exceptions[1].Justification, null, null, exceptions[1].AcceptedValue, null,
                ContentHashes.Of(exceptions[1]), RegisterEntryState.Pending),
            new(RegisterKind.Exceptions, exceptions[2].Id, exceptions[2].RuleId, "SMB-Server verlangt Signierung", Severity.Medium,
                exceptions[2].Scope, exceptions[2].Category, exceptions[2].Justification, exceptions[2].CompensatingMeasure, null,
                exceptions[2].AcceptedValue, "M-0003", ContentHashes.Of(exceptions[2]), RegisterEntryState.ChangedAfterApproval),
            new(RegisterKind.Tailoring, tailoring[1].Id, tailoring[1].RuleId, "Kennwort vor dem Start auf Mobilgeräten", Severity.Low,
                tailoring[1].Scope, null, tailoring[1].Reason, null, null, null, null, ContentHashes.Of(tailoring[1]), RegisterEntryState.Pending),
        };
        return new ApprovalSheetData(PlantId, PlantName, "de", Date, Now, Tool, "GB-12", RegisterHashes.Exceptions, RegisterHashes.Tailoring,
            entries, [new(SignOffSteps.Approved, RoleIsb), new(SignOffSteps.Approved, RoleQa)], "Vertraulich – enthält Sicherheitskonfiguration", false);
    }

    /// <summary>Erhebungsprotokoll (O14) eines Imports mit zwei Exporten.</summary>
    public static CollectionRecordData CollectionRecord()
    {
        var record = new ImportRecord(SchemaIds.Import, "20261017T120000Z-1a2b3c", new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero),
            "Servicetechniker", "Anlagenverantwortlicher (Zeuge)", RoleOwner,
            [
                new($"{Hmi}_20261016T073000Z.snapshot.json", "8888888888888888888888888888888888888888888888888888888888888888", true, Hmi,
                    new DateTimeOffset(2026, 10, 16, 7, 30, 0, TimeSpan.Zero), "Riegel.Collector", "0.1.0", Sha0, true,
                    new CollectionOptions(false, false, false), SnapshotOrigin.Collector, "20261018T090000Z-a1b2c3"),
                new($"{Historian}_20260803T061500Z.riegel-builtin", "9999999999999999999999999999999999999999999999999999999999999999", false, Historian,
                    new DateTimeOffset(2026, 8, 3, 6, 15, 0, TimeSpan.Zero), "collect-builtin.cmd", "0.1.0", null, false,
                    new CollectionOptions(true, false, true), SnapshotOrigin.Builtin, "20261018T090400Z-e5f6a7"),
            ]);
        return new CollectionRecordData(PlantId, PlantName, "de", Now, Tool, record, "Vertraulich – enthält Sicherheitskonfiguration", false);
    }
}
