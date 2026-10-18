// SPDX-License-Identifier: MIT
using System.Text;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Demo;
using Riegel.Platform.Windows;
using Riegel.Session;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Windows.App;

/// <summary>
/// Demo-Anlage „Musterwerk – Linie 3“ (spec 7.9): eingebettete synthetische Exporte, nur Rollen und erfundene Nachweise,
/// Aufbau über die echten Dienste. Mit den Entwicklungsdiensten zeigt sie Verschlechterung, ablaufende und beantragte
/// Ausnahme, Zuschnitt, überfällige Maßnahme und einen Rechner mit passivem Defender.
/// </summary>
[Collection(AppCollection.Name)]
public sealed class DemoPlantTests
{
    private static readonly string[] Hosts = ["HMI-L3-01", "HMI-L3-02", "ENG-L3-01", "HIST-01", "PG-ALT-01", "OPS-01"];

    [Fact]
    public void Zwei_Exporte_je_Rechner_und_jeder_passt_zum_Schema()
    {
        var names = DemoData.SnapshotNames();
        Assert.Equal(12, names.Count);
        foreach (var host in Hosts) Assert.Equal(2, names.Count(n => n.StartsWith(host + "_", StringComparison.Ordinal)));
        foreach (var name in names)
        {
            using var stream = DemoData.OpenSnapshot(name);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var json = Encoding.UTF8.GetString(buffer.ToArray());
            SchemaValidator.AssertValid(json, name);
            var snapshot = RiegelJson.Deserialize<Snapshot>(buffer.ToArray());
            Assert.Contains(snapshot.Machine.HostName, Hosts);
            Assert.StartsWith(snapshot.Machine.HostName + "_", name);
        }
    }

    [Fact]
    public void Runden_teilen_die_Exporte_nach_Zeit()
    {
        var (first, second) = DemoData.Rounds();
        Assert.Equal(6, first.Count);
        Assert.Equal(6, second.Count);
        foreach (var host in Hosts)
        {
            var a = first.Single(n => n.StartsWith(host + "_", StringComparison.Ordinal));
            var b = second.Single(n => n.StartsWith(host + "_", StringComparison.Ordinal));
            Assert.True(string.CompareOrdinal(a, b) < 0);
        }
    }

    [Fact]
    public void Register_nennen_Rollen_und_erfundene_Nachweise()
    {
        string[] roles = [DemoData.RoleOwner, DemoData.RoleIsb, DemoData.RoleQa, DemoData.RoleTechnician];
        foreach (var e in DemoData.Exceptions())
        {
            Assert.Null(e.Approval.ApprovedByName);
            if (e.CreatedByRole is { } c) Assert.Contains(c, roles);
            if (e.Approval.ApprovedByRole is { } r) Assert.Contains(r, roles);
            if (e.Approval.ApprovalRecord is { } rec) Assert.Contains("DEMO", rec);
        }
        foreach (var t in DemoData.Tailoring()) Assert.Null(t.Approval.ApprovedByName);
        foreach (var m in DemoData.Measures())
        {
            Assert.Contains(m.OwnerRole, roles);
            if (m.Ticket is { } ticket) Assert.Contains("DEMO", ticket);
        }
        Assert.Contains(DemoData.Exceptions(), e => e.Reference?.Contains(DemoData.VendorDocument, StringComparison.Ordinal) == true);
        Assert.All(DemoData.Machines.Where(m => m.Identity is not null), m => Assert.Contains("DEMO", m.Identity!.SerialNumber));
    }

    [Fact]
    public void Aufbau_ueber_die_echten_Dienste()
    {
        using var temp = new TempFolder();
        var plant = DemoPlant.Build(temp.Path, PlatformFactory.Create(), fake: false);
        var services = DemoPlant.Services(PlatformFactory.Create(), DemoData.Now, fake: false);
        var context = PlantContext.Load(services, services.Plants.Open(plant.Folder!, readOnly: false), isDemo: true);
        Assert.Equal("Musterwerk – Linie 3", context.Plant.Name);
        Assert.Equal(6, context.Machines.Count);
        Assert.All(context.Machines, m => Assert.Equal(2, m.Runs.Count));
        Assert.Equal(2, context.Imports.Count);
        Assert.Equal(4, context.Registers.Exceptions.Exceptions.Count);
        Assert.Single(context.Registers.Tailoring.Entries);
        Assert.Equal(3, context.Registers.Measures.Measures.Count);
        Assert.NotNull(context.Machines.Single(m => m.HostName == DemoData.ApprovedHost).Approved);
        Assert.True(File.Exists(Path.Combine(plant.Folder!, "riegel-plant.json")));
    }

    [Fact]
    public void Mit_Entwicklungsdiensten_zeigt_die_Demo_jeden_Fall()
    {
        using var temp = new TempFolder();
        var plant = DemoPlant.Build(temp.Path, PlatformFactory.Create(), fake: true);
        var services = DemoPlant.Services(PlatformFactory.Create(), DemoData.Now, fake: true);
        var context = PlantContext.Load(services, services.Plants.Open(plant.Folder!, readOnly: false), isDemo: true);
        RuleResult Result(string host, string rule) =>
            context.Machines.Single(m => m.HostName == host).Latest!.Results.Single(r => r.RuleId == rule);

        // Verschlechterung gegenüber dem freigegebenen Stand
        Assert.True((Result("HMI-L3-02", "NET-01").Flags & ResultFlags.Regression) != 0);
        // beantragte Ausnahme, ablaufende Ausnahme, gültige Ausnahme
        Assert.True((Result("HMI-L3-02", "RDP-01").Flags & ResultFlags.ExceptionPending) != 0);
        Assert.True((Result("OPS-01", "SVC-01").Flags & ResultFlags.ExceptionExpiring) != 0);
        Assert.Equal(ResultStatus.Exception, Result("HMI-L3-01", "LOGON-10").Status);
        // Zuschnitt des Cloud-Schutzes für die Bedienstationen
        Assert.Equal(ResultStatus.OutOfScope, Result("HMI-L3-01", "DEF-03").Status);
        // überfällige Maßnahme auf der Engineering-Station
        Assert.True((Result("ENG-L3-01", "ENC-01").Flags & ResultFlags.MeasureOverdue) != 0);
        // Defender passiv (Virenschutz eines Drittanbieters): Defender-Regeln nicht anwendbar
        Assert.Equal(ResultStatus.NotApplicable, Result("ENG-L3-01", "DEF-01").Status);
        // veraltete Erfassung und viele nicht prüfbare Regeln beim Bordmittel-Paket
        Assert.True((context.Machines.Single(m => m.HostName == "HIST-01").Latest!.Flags & RunFlags.StaleSnapshot) != 0);
        Assert.True(context.Machines.Single(m => m.HostName == "PG-ALT-01").Latest!.Results.Count(r => r.Status == ResultStatus.NotCheckable) >= 10);
    }

    [Fact]
    public void Demo_Ordner_werden_wieder_geloescht()
    {
        var folder = DemoPlant.NewFolder();
        Assert.True(Directory.Exists(folder));
        DemoPlant.Delete(folder);
        Assert.False(Directory.Exists(folder));
        // Ordner ohne Kennzeichen löscht Delete nie
        using var temp = new TempFolder();
        DemoPlant.Delete(temp.Path);
        Assert.True(Directory.Exists(temp.Path));
    }
}
