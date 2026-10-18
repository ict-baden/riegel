// SPDX-License-Identifier: MIT
using System.Reflection;
using Riegel.Core.Model;

namespace Riegel.Demo;

/// <summary>
/// Inhalte der Demo-Anlage „Musterwerk – Linie 3“ (spec 7.9): Gruppen, Rechner mit offensichtlich erfundener
/// Maschinenidentität, Register mit Rollen statt Namen und erfundenen Nachweisen („Herstellerdokument HD-4711“,
/// Hersteller „Beispiel-Anlagenbau“, „CHG-DEMO-0001“). Keine echten Kunden, Hersteller, Personen oder Rechner.
/// </summary>
internal static class DemoData
{
    public const string PlantName = "Musterwerk – Linie 3";

    /// <summary>Stand der Demo: Auswertung und „heute“ der Oberfläche.</summary>
    public static DateTimeOffset Now { get; } = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Erster Import (Erhebungsrunde September, Freigabe von HMI-L3-02 nach der Inbetriebnahme).</summary>
    public static DateTimeOffset FirstImport { get; } = new(2026, 9, 18, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Zweiter Import (Erhebungsrunde Oktober).</summary>
    public static DateTimeOffset SecondImport { get; } = new(2026, 10, 17, 14, 0, 0, TimeSpan.Zero);

    // Rollen, nie Namen (spec 7.9)
    public const string RoleOwner = "Anlagenverantwortlicher";
    public const string RoleIsb = "Informationssicherheitsbeauftragter";
    public const string RoleQa = "Qualitätssicherung";
    public const string RoleTechnician = "Servicetechniker";

    /// <summary>Erfundener Verweis auf ein Herstellerdokument (spec 7.9).</summary>
    public const string VendorDocument = "Herstellerdokument HD-4711";

    public const string HmiGroup = "g-hmi";

    public static IReadOnlyList<MachineGroup> Groups { get; } =
    [
        new("g-hmi", "Bedienstationen", MachineRole.Hmi),
        new("g-eng", "Engineering", MachineRole.EngineeringStation),
        new("g-hist", "Historian", MachineRole.Historian),
        new("g-srv", "Betriebsserver", MachineRole.Server),
        new("g-alt", "Altbestand", MachineRole.Other),
    ];

    private static MachineIdentity Identity(string serial) => new("Abfüllmaschine Typ X-100 (fiktiv)", serial, "AUF-DEMO-0001",
        new DateOnly(2026, 3, 2), "Beispiel-Leitsystem 4.2", "Beispiel-Anlagenbau", "Halle 3, Linie 3");

    public static IReadOnlyList<PlantMachine> Machines { get; } =
    [
        new("HMI-L3-01", "g-hmi", "Bedienstation 1", false, null, Identity("SN-DEMO-0001")),
        new("HMI-L3-02", "g-hmi", "Bedienstation 2", false, null, Identity("SN-DEMO-0002")),
        new("ENG-L3-01", "g-eng", "Engineering-Station", false, null, null),
        new("HIST-01", "g-hist", "Historian", false, "Ablösung durch Server 2022 geplant", null),
        new("PG-ALT-01", "g-alt", "Programmiergerät (Altbestand)", false, "Nur für Altprojekte, ohne Netzwerk", null),
        new("OPS-01", "g-srv", "Betriebsserver", false, "Zugleich Druckserver der Linie", null),
    ];

    public static PlantSettings Settings { get; } = new(30, 30, 35, 90, null,
        [new(SignOffSteps.Created, RoleOwner), new(SignOffSteps.Reviewed, RoleIsb), new(SignOffSteps.Approved, RoleQa)],
        null, ReportPreset.Audit, null, null, [], RoleOwner);

    public const string Description = "Fiktive Demo-Anlage – alle Rechner, Werte und Nachweise sind erfunden.";

    private static RegisterScope Machine(string host) => new([host], [], false);

    private static ExceptionEntry Approve(ExceptionEntry e, string role, DateOnly on, DateOnly reviewBy, string record) =>
        e with { Approval = new Approval(role, null, on, reviewBy, record, ContentHashes.Of(e)) };

    private static TailoringEntry Approve(TailoringEntry t, string role, DateOnly on, DateOnly reviewBy, string record) =>
        t with { Approval = new Approval(role, null, on, reviewBy, record, ContentHashes.Of(t)) };

    /// <summary>Ausnahmen: gültig, läuft bald ab, beantragt, mit kompensierender Maßnahme (akzeptiert jeden Wert).</summary>
    public static IReadOnlyList<ExceptionEntry> Exceptions() =>
    [
        Approve(new ExceptionEntry("EX-0001", "LOGON-10", Machine("HMI-L3-01"), ReasonCategory.VendorRequirement,
                "Die Bedienstation meldet sich laut Herstellervorgabe automatisch an, damit die Linie nach einem Stromausfall ohne Eingriff anläuft.",
                null, VendorDocument, "1", Approval.None, new DateOnly(2026, 4, 1), RoleOwner, null, "2026.10", null),
            RoleOwner, new DateOnly(2026, 4, 20), new DateOnly(2027, 4, 20), "CHG-DEMO-0001"),
        Approve(new ExceptionEntry("EX-0002", "SVC-01", Machine("OPS-01"), ReasonCategory.TechnicalNecessity,
                "Der Betriebsserver ist zugleich Druckserver für die Etikettendrucker der Linie 3.",
                null, null, "{\"startMode\":\"Auto\",\"state\":\"Running\"}", Approval.None, new DateOnly(2025, 10, 20), RoleOwner, null, "2026.10", null),
            RoleIsb, new DateOnly(2025, 11, 5), new DateOnly(2026, 11, 5), "CHG-DEMO-0002"),
        new ExceptionEntry("EX-0003", "RDP-01", Machine("HMI-L3-02"), ReasonCategory.VendorRequirement,
            "Das Fernwartungsgerät von Beispiel-Anlagenbau unterstützt keine Authentifizierung auf Netzwerkebene.",
            null, "Herstellerdokument HD-4711, Abschnitt 3", "0", Approval.None, new DateOnly(2026, 10, 17), RoleOwner, null, "2026.10", null),
        Approve(new ExceptionEntry("EX-0004", "LIFE-01", Machine("PG-ALT-01"), ReasonCategory.CompensatingControl,
                "Die Programmierstation wird nur noch für Altprojekte der Linie gebraucht; die Ablösung ist geplant.",
                "Rechner ohne Netzwerkverbindung, Datenaustausch nur über geprüfte USB-Datenträger", null, "*", Approval.None,
                new DateOnly(2026, 2, 20), RoleOwner, null, "2026.10", null),
            RoleOwner, new DateOnly(2026, 3, 2), new DateOnly(2027, 3, 1), "CHG-DEMO-0004"),
    ];

    /// <summary>Zuschnitt: Cloud-Schutz ist für die Bedienstationen (ohne Internetzugang) nicht vorgesehen.</summary>
    public static IReadOnlyList<TailoringEntry> Tailoring() =>
    [
        Approve(new TailoringEntry("TL-0001", "DEF-03", new RegisterScope([], [HmiGroup], false),
                "Bedienstationen haben keinen Internetzugang; cloudbasierter Schutz ist in der Soll-Konfiguration dieser Rolle nicht vorgesehen.",
                Approval.None, new DateOnly(2026, 4, 1), RoleOwner, null, null),
            RoleIsb, new DateOnly(2026, 4, 20), new DateOnly(2027, 4, 20), "CHG-DEMO-0001"),
    ];

    /// <summary>Maßnahmen: eine überfällig (BitLocker), zwei geplant.</summary>
    public static IReadOnlyList<MeasureEntry> Measures() =>
    [
        new("M-0001", "ENC-01", Machine("ENG-L3-01"), "BitLocker auf der Engineering-Station aktivieren", RoleIsb, new DateOnly(2026, 9, 30),
            "TICKET-DEMO-12", MeasureState.Planned, new DateOnly(2026, 6, 1), RoleOwner, null, null),
        new("M-0002", "LIFE-02", Machine("HIST-01"), "Kumulatives Update im nächsten Wartungsfenster einspielen", RoleOwner, new DateOnly(2026, 11, 30),
            "TICKET-DEMO-13", MeasureState.Planned, new DateOnly(2026, 8, 20), RoleOwner, null, null),
        new("M-0003", "RDP-01", Machine("HMI-L3-01"), "Authentifizierung auf Netzwerkebene nach Freigabe durch den Hersteller einschalten", RoleOwner,
            new DateOnly(2026, 12, 15), "TICKET-DEMO-14", MeasureState.Planned, new DateOnly(2026, 9, 18), RoleOwner, null, null),
    ];

    /// <summary>Freigegebener Stand von HMI-L3-02 nach der Inbetriebnahme (SAT).</summary>
    public static Approval SatApproval { get; } = new(RoleQa, null, new DateOnly(2026, 9, 18), null, "CHG-DEMO-0003", null);

    public const string ApprovedHost = "HMI-L3-02";

    // ---------------------------------------------------------------- eingebettete Exporte

    private const string SnapshotPrefix = "demo/snapshots/";

    /// <summary>Namen der eingebetteten Exporte (z. B. „HMI-L3-01_20260917T073000Z.snapshot.json“), sortiert.</summary>
    public static IReadOnlyList<string> SnapshotNames() =>
        [.. typeof(DemoData).Assembly.GetManifestResourceNames().Where(n => n.StartsWith(SnapshotPrefix, StringComparison.Ordinal))
            .Select(n => n[SnapshotPrefix.Length..]).Order(StringComparer.Ordinal)];

    public static Stream OpenSnapshot(string name) =>
        typeof(DemoData).Assembly.GetManifestResourceStream(SnapshotPrefix + name)
        ?? throw new FileNotFoundException(name);

    /// <summary>Je Rechner die erste Erhebung in Runde 1, die zweite in Runde 2 (nach Zeitstempel im Namen).</summary>
    public static (IReadOnlyList<string> First, IReadOnlyList<string> Second) Rounds()
    {
        var first = new List<string>();
        var second = new List<string>();
        foreach (var host in SnapshotNames().GroupBy(n => n[..n.IndexOf('_')]))
        {
            var ordered = host.Order(StringComparer.Ordinal).ToList();
            first.Add(ordered[0]);
            second.AddRange(ordered.Skip(1));
        }
        return (first, second);
    }

    internal static Assembly Assembly => typeof(DemoData).Assembly;
}
