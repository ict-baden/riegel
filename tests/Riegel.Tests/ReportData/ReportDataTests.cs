// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.ReportBuilding;
using Riegel.Tests.Plants;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Plants.WorkflowKit;

// Namensraum ReportBuilding wie in Core: ein Namensraum „ReportData“ verdeckte den Typ ReportData (auch in fremden Tests)
namespace Riegel.Tests.ReportBuilding;

/// <summary>
/// Daten des Anlagenberichts (spec 5 O4/O7, 2.4 Punkt 9, 2.8, 2.11): Befunde je Regel, Änderungen gegenüber vorigem
/// Lauf und Freigabe, Registersichten mit Kennzeichen, Rechner, Versicherer-Fakten, Banner, Freigabeblock, Methode.
/// </summary>
public sealed class ReportDataTests : IClassFixture<ReportDataTests.Fixture>
{
    public sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Rig = new ReportRig().Scenario();
            Data = Rig.Build();
        }

        internal ReportRig Rig { get; }
        public Core.Model.Reports.ReportData Data { get; }

        public void Dispose() => Rig.Dispose();
    }

    private readonly Fixture _f;

    public ReportDataTests(Fixture f) => _f = f;

    private Core.Model.Reports.ReportData Data => _f.Data;

    [Fact]
    public void Befunde_je_Regel_mit_offen_von_anwendbar_und_hoechstens_zehn_Rechnern()
    {
        Assert.Equal(["NET-01", "ENC-01"], Data.Findings.Select(f => f.RuleId));
        var net01 = Data.Findings[0];
        Assert.Equal(Severity.High, net01.Severity);
        Assert.Equal(12, net01.OpenCount);
        Assert.Equal(14, net01.ApplicableCount);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => $"HMI-{i:00}"), net01.Hosts);
        Assert.True(net01.MoreHosts);
        Assert.Equal("Testregel NET-01", net01.Title);
        Assert.True((net01.Flags & ResultFlags.Regression) != 0);
        Assert.True((net01.Flags & ResultFlags.ExceptionPending) != 0);
        Assert.Equal("M-0002", net01.Measure?.MeasureId);
        var enc = Data.Findings[1];
        Assert.Equal(new MeasureSummary("M-0001", "Anlagenverantwortlicher", Today.AddDays(-1), true), enc.Measure);
        Assert.True((enc.Flags & ResultFlags.ExceptionExpired) != 0);
    }

    [Fact]
    public void Zaehlungen_zuerst()
    {
        var c = Data.Counts;
        Assert.Equal((14, 13), (c.Machines, c.MachinesScored));
        Assert.Equal((12, 12, 0), (c.OpenHigh, c.OpenMedium, c.OpenLow));
        Assert.Equal(2, c.Regressions);
        Assert.Equal((4, 0, 1, 1), (c.ExceptionsValid, c.ExceptionsExpiring, c.ExceptionsExpired, c.ExceptionsPending));
        Assert.Equal((2, 2, 1), (c.TailoringEntries, c.MeasuresPlanned, c.MeasuresOverdue));
        Assert.Equal((1, 1, 1), (c.UnsupportedOs, c.StaleSnapshots, c.DomainControllers));
        Assert.True(Data.Score.Greyed);
        Assert.Equal(96, Data.Score.CheckedShare);
        Assert.NotNull(Data.Score.Score);
    }

    [Fact]
    public void Verschlechtert_seit_dem_letzten_Lauf_und_seit_der_Freigabe()
    {
        var worse = Data.Changes.Where(c => ReportDataBuilder.IsWorse(c.Change)).ToList();
        Assert.Equal(3, worse.Count);
        Assert.Contains(worse, c => c.HostName == "HMI-01" && c.Basis == ComparisonBasis.Previous && c.Change.RuleId == "NET-01");
        Assert.Contains(worse, c => c.HostName == "HMI-02" && c.Basis == ComparisonBasis.Previous);
        var approved = Assert.Single(worse, c => c.Basis == ComparisonBasis.Approved);
        Assert.Equal("HMI-02", approved.HostName);
        Assert.Equal(Severity.High, approved.Severity);
        Assert.Equal(StatusChangeKind.Worse, approved.Change.Kind);
        // zuerst gegenüber dem vorigen Lauf, dann gegenüber der Freigabe
        Assert.Equal(ComparisonBasis.Previous, Data.Changes[0].Basis);
        Assert.Equal(ComparisonBasis.Approved, Data.Changes[^1].Basis);

        var hmi01 = Data.Machines.Single(m => m.HostName == "HMI-01");
        Assert.Equal("NET-01", Assert.Single(hmi01.Worse).Change.RuleId);
        Assert.True(hmi01.Drift.Total >= 1);
        Assert.Contains(hmi01.Drift.Top, d => d.Path == "NET-01" && d.From == "pass|1" && d.To == "fail|0");
    }

    [Fact]
    public void Registersichten_mit_Zustand_und_Kennzeichen()
    {
        ExceptionView E(string id) => Data.Exceptions.Concat(Data.ExpiredExceptions).Single(e => e.Entry.Id == id);
        Assert.Equal(RegisterEntryState.Valid, E("EX-0001").State);
        Assert.Equal(Enumerable.Range(1, 12).Select(i => $"HMI-{i:00}"), E("EX-0001").AffectedHosts);
        Assert.Equal(RegisterFlags.None, E("EX-0002").Flags & RegisterFlags.Unused); // auf SRV-01 nötig
        Assert.Equal(RegisterFlags.Unused, E("EX-0003").Flags);
        Assert.Equal(RegisterFlags.RuleRetired, E("EX-0004").Flags);
        Assert.Equal(RegisterEntryState.Pending, E("EX-0006").State);
        Assert.Equal("EX-0005", Assert.Single(Data.ExpiredExceptions).Entry.Id);
        Assert.DoesNotContain(Data.Exceptions, e => e.State == RegisterEntryState.Expired);
        Assert.Equal(ContentHashes.Of(E("EX-0001").Entry), E("EX-0001").ContentSha256);

        Assert.Equal(RegisterEntryState.Valid, Data.Tailoring.Single(t => t.Entry.Id == "TL-0001").State);
        Assert.Equal(RegisterEntryState.Pending, Data.Tailoring.Single(t => t.Entry.Id == "TL-0002").State);
        Assert.Equal(RegisterFlags.None, Data.Tailoring.Single(t => t.Entry.Id == "TL-0002").Flags);

        MeasureView M(string id) => Data.Measures.Single(m => m.Entry.Id == id);
        Assert.True(M("M-0001").Overdue);
        Assert.Equal(1, M("M-0001").OpenCount);
        Assert.Equal(RegisterFlags.MeasureDoneButFailing, M("M-0002").Flags);
        Assert.False(M("M-0003").Overdue);
        Assert.Equal(0, M("M-0003").OpenCount);
    }

    [Fact]
    public void Banner_nach_spec_2_4_und_2_8()
    {
        Banner B(BannerKind kind) => Assert.Single(Data.Banners, b => b.Kind == kind);
        var incomplete = B(BannerKind.IncompleteCheck);
        Assert.Equal(["DC-01", "SRV-01"], incomplete.Hosts.Order(StringComparer.Ordinal));
        Assert.Equal(96, incomplete.CheckedShare);
        Assert.Equal(["audit", "accounts"], incomplete.Areas);
        Assert.Equal(["HMI-12"], B(BannerKind.StaleSnapshot).Hosts);
        Assert.Equal(40, B(BannerKind.StaleSnapshot).AgeDays);
        Assert.Equal(231, B(BannerKind.StaleCatalogue).AgeDays);
        Assert.Equal(["SRV-01"], B(BannerKind.ExperimentalGpoAttribution).Hosts);
        Assert.Equal(["DC-01"], B(BannerKind.ClockSuspect).Hosts);
        Assert.Equal(["DC-01"], B(BannerKind.DomainController).Hosts);
        Assert.DoesNotContain(Data.Banners, b => b.Kind == BannerKind.PreTranslation);
        Assert.True(Data.Method.ExperimentalGpoAttribution);

        var spanish = _f.Rig.Build("es");
        Assert.Contains(spanish.Banners, b => b.Kind == BannerKind.PreTranslation);
        Assert.True(spanish.Header.PreTranslated);
    }

    [Fact]
    public void Freigabeblock_bindet_die_Register()
    {
        var s = Data.SignOff;
        var hashes = _f.Rig.Services.Registers.Load(_f.Rig.Plant.Plant).Hashes;
        Assert.Equal(hashes.Exceptions, s.ExceptionsSha256);
        Assert.Equal(hashes.Tailoring, s.TailoringSha256);
        Assert.Equal(hashes.Measures, s.MeasuresSha256);
        Assert.Equal((6, 4, 2, 3), (s.ExceptionCount, s.ExceptionValidCount, s.TailoringCount, s.MeasureCount));
        Assert.Equal(14, s.RunIds.Count);
        Assert.Equal(Data.Header.ReportId, s.ReportId);
        Assert.Equal("RPT-20261018T090100Z", s.ReportId);
        Assert.Equal(hashes, Data.Header.Registers);
        Assert.Equal(PlantSettings.Default.SignOff.Select(r => r.Step), s.Roles.Select(r => r.Step));

        var delivered = _f.Rig.Build("de", ReportPreset.AsDelivered).SignOff.Roles;
        Assert.Equal([("created", "Hersteller"), ("reviewed", "Hersteller"), ("accepted", "Betreiber")], delivered.Select(r => (r.Step, r.Role)));
        Assert.Equal("Machine builder", _f.Rig.Build("en", ReportPreset.AsDelivered).SignOff.Roles[0].Role);
    }

    [Fact]
    public void Rechner_mit_Erhebung_Fakten_Bestand_und_Lebenszyklus()
    {
        var hmi = Data.Machines.Single(m => m.HostName == "HMI-03");
        Assert.Equal("Bedienstation 3", hmi.Label);
        Assert.Equal(("g-hmi", "Bedienstationen", MachineRole.Hmi), (hmi.GroupId, hmi.GroupName, hmi.Role));
        Assert.Equal(17763, hmi.Machine.Build);
        Assert.Equal(2, hmi.Collection.AgeDays);
        Assert.True(hmi.Collection.IsAdmin);
        Assert.Equal("Testsystem Client", hmi.Lifecycle?.ProductName);
        Assert.True(hmi.Lifecycle?.Supported);
        Assert.Contains(hmi.Facts, f => f.Name == "os.build" && f.Value == "17763");
        Assert.DoesNotContain(hmi.Facts, f => f.Name.StartsWith("net.", StringComparison.Ordinal));
        var inventory = Assert.Single(hmi.Inventory);
        Assert.Equal(("ACCT-15", InventoryRenderer.LocalAdmins), (inventory.RuleId, inventory.Renderer));
        var view = hmi.Results.Single(r => r.Result.RuleId == "NET-01");
        Assert.Equal(("Testregel NET-01", "network", RuleKind.Check), (view.Title, view.Category, view.Kind));
        Assert.Empty(view.References);

        var dc = Data.Machines.Single(m => m.HostName == "DC-01");
        Assert.True(dc.IsDomainController);
        Assert.Null(dc.Summary.Score);
        Assert.False(dc.Lifecycle?.Supported);
        Assert.Equal(Data.Machines.Select(m => m.HostName).Order(StringComparer.OrdinalIgnoreCase), Data.Machines.Select(m => m.HostName));
    }

    [Fact]
    public void Versicherer_Fakten_aus_Ergebnissen_und_Rohdaten()
    {
        var srv = Data.InsurerFacts.Single(r => r.HostName == "SRV-01");
        Assert.Equal(("Testsystem Server", 20348), (srv.OsName, srv.Build));
        Assert.Equal(new DateOnly(2027, 1, 1), srv.SupportEnd);
        Assert.True(srv.Supported);
        Assert.Equal(2, srv.LocalAdminCount);
        Assert.True(srv.FirewallOn);
        Assert.True(srv.RdpEnabled);
        Assert.Null(srv.RdpNla);
        Assert.False(srv.Smb1);
        Assert.True(srv.BitLocker);
        Assert.Equal("Beispiel-Virenschutz (fiktiv)", srv.AvProduct);
        Assert.Null(srv.AvState);
        Assert.Equal(new NetworkFacts(null, null, null, null, null), srv.Network);

        var hmi = Data.InsurerFacts.Single(r => r.HostName == "HMI-01");
        Assert.True(hmi.Smb1); // NET-01 offen
        Assert.False(hmi.BitLocker);
        Assert.Null(hmi.LocalAdminCount);
        Assert.Equal(Data.Machines.Count, Data.InsurerFacts.Count);
    }

    [Fact]
    public void Kopf_Methode_und_Rechnerauswahl()
    {
        Assert.Equal(("musteranlage", "Musteranlage", "de"), (Data.Header.PlantId, Data.Header.PlantName, Data.Header.Language));
        Assert.Equal(_f.Rig.Cat.Sha256, Data.Header.Catalogue.Sha256);
        Assert.Equal("Vertraulich – enthält Sicherheitskonfiguration", Data.Header.Confidentiality);
        Assert.Null(Data.Header.LogoPath);
        Assert.Equal(7, Data.Method.RulesTotal);
        Assert.Equal((6, 1), (Data.Method.CheckRules, Data.Method.InventoryRules));
        Assert.Equal(new ProvenanceCounts(6, 6, 0, 0, 0, 0), Data.Method.Provenance);
        Assert.Equal(RuleSources.All.Count, Data.Method.Sources.Count);
        Assert.Contains(Frameworks.SupportedEvidence, f => Data.Method.Frameworks.Contains(f));

        var two = _f.Rig.Build(hosts: ["hmi-01", "SRV-01"]);
        Assert.Equal(["HMI-01", "SRV-01"], two.Machines.Select(m => m.HostName));
        Assert.Equal(1, two.Findings.Single(f => f.RuleId == "NET-01").OpenCount);
    }

    [Fact]
    public void Gleiche_Daten_und_Uhr_ergeben_dieselben_Berichtsdaten()
    {
        Assert.Equal(ToJson(_f.Rig.Build()), ToJson(_f.Rig.Build()));
    }

    [Fact]
    public void Logo_nur_aus_dem_Anlagenordner_als_PNG_oder_JPEG_bis_1_MB()
    {
        using var temp = new TempFolder();
        var folder = temp.Sub("anlage");
        File.WriteAllBytes(Path.Combine(folder, "logo.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);
        File.WriteAllBytes(Path.Combine(folder, "logo.txt"), "kein Bild"u8.ToArray());
        File.WriteAllBytes(Path.Combine(temp.Path, "draussen.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Plant With(string? file) => ReportRig.Definition with { Folder = folder, Settings = ReportRig.Definition.Settings with { CoverLogoFile = file } };
        Assert.Equal(Path.Combine(folder, "logo.png"), ReportDataBuilder.LogoPath(With("logo.png")));
        Assert.Null(ReportDataBuilder.LogoPath(With("logo.txt")));
        Assert.Null(ReportDataBuilder.LogoPath(With("../draussen.png")));
        Assert.Null(ReportDataBuilder.LogoPath(With("fehlt.png")));
        File.WriteAllBytes(Path.Combine(folder, "gross.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[1024 * 1024]]);
        Assert.Null(ReportDataBuilder.LogoPath(With("gross.png")));
    }

    [Fact]
    public void Fehlt_der_Export_bleiben_die_Statusaenderungen()
    {
        using var rig = new ReportRig();
        rig.Add(ReportRig.Hmi(1, new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), net01: "pass|1"));
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        var latest = rig.Add(ReportRig.Hmi(1, new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero)));
        File.Delete(rig.Plant.PathOf(latest.Snapshot.Path));
        var data = rig.Build();
        Assert.Contains(data.Changes, c => c.Change.RuleId == "NET-01" && c.Change.Kind == StatusChangeKind.Worse);
        var machine = Assert.Single(data.Machines);
        Assert.Equal(0, machine.Drift.Total);
        Assert.Empty(machine.Drift.Top);
        Assert.Equal(17763, machine.Machine.Build); // Kopf aus den Fakten des Laufs
        Assert.Equal("", machine.Collection.CollectorName);
    }

    [Fact]
    public void Leere_Anlage_ergibt_leere_Berichtsdaten()
    {
        using var rig = new ReportRig();
        var data = rig.Build();
        Assert.Empty(data.Machines);
        Assert.Empty(data.Findings);
        Assert.Null(data.Score.Score);
        Assert.False(data.Score.Greyed);
        Assert.Equal(("", "", ""), (data.SignOff.ExceptionsSha256, data.SignOff.TailoringSha256, data.SignOff.MeasuresSha256));
    }
}
