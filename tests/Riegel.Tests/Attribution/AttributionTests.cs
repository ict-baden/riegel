// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Services;
using Riegel.Core.Snapshots;
using Riegel.Tests.Parsing;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Parsing.ParsingSamples;

namespace Riegel.Tests.Attribution;

/// <summary>Herkunft der Werte (spec 2.10) mit handgeschriebenen gpresult- und Registry.pol-Beispielen.</summary>
public sealed class AttributionTests
{
    private const string Dns = @"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient";
    private const string Local = @"HKLM\SOFTWARE\Policies\Beispiel";
    private const string Explorer = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private const string Mdm = @"HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Update";
    private const string Lsa = @"HKLM\SYSTEM\CurrentControlSet\Control\Lsa";

    private static IValueSourceAttributor Attributor() => RiegelServices.Create(new NullPlatform(), new FixedClock()).Attribution;

    /// <summary>Rohdaten wie vom Sammler; der Leser berechnet daraus parsed:*.</summary>
    private static Snapshot Prepare(SnapshotBuilder builder) => DerivedItems.Process(builder.Build(), ReadLimits.Default);

    private static SnapshotBuilder Machine(int domainRole, bool admin = true) => SnapshotBuilder.Windows11Workgroup()
        .Origin(SnapshotOrigin.Collector)
        .Header(m => m with { DomainRole = domainRole })
        .Admin(admin)
        .RegistryValue(Dns, "EnableMulticast", RegValueType.Dword, 0)
        .RegistryValue(Local, "LokalerWert", RegValueType.Dword, 1)
        .RegistryValue(Local, "Abweichend", RegValueType.Dword, 0)
        .RegistryValue(Explorer, "NoAutorun", RegValueType.Dword, 1)
        .RegistryValue(Mdm, "AllowAutoUpdate", RegValueType.Dword, 1)
        .RegistryValue(Lsa, "NoLMHash", RegValueType.Dword, 1);

    private static byte[] LocalPol() => Pol(
        (@"Software\Policies\Beispiel", "LokalerWert", 4, Dword(1)),
        (@"Software\Policies\Beispiel", "Abweichend", 4, Dword(1)),
        (@"Software\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast", 4, Dword(0)));

    private static SnapshotBuilder WithPol(SnapshotBuilder b) =>
        b.Api(ItemKeys.RawRegistryPol, new { base64 = Convert.ToBase64String(LocalPol()), sha256 = Hashes.Sha256Hex(LocalPol()) });

    private static SnapshotBuilder WithGpresult(SnapshotBuilder b, bool admx = false) =>
        b.Api(ItemKeys.RawGpresultXml, new { text = GpresultAndFirewallTests.Sample(admx) });

    private static ValueSource? Source(AttributionMap map, string key, string value) => map.Find(key, value)?.Source;

    [Fact]
    public void Arbeitsgruppe_mit_Admin_und_gpresult()
    {
        var workgroupGpresult = Gpresult([("Local Group Policy", "LocalGPO")],
            Extension("RegistrySettings", RegistryNs, RegistrySetting("LocalGPO", @"Software\Policies\Beispiel", "LokalerWert")));
        var map = Attributor().Build(Prepare(WithPol(Machine(0)).Api(ItemKeys.RawGpresultXml, new { text = workgroupGpresult })));
        Assert.True(map.Determinable);
        Assert.Equal(ValueSource.LocalPolicy, Source(map, Local, "LokalerWert"));
        Assert.Equal("Local Group Policy", map.Find(Local, "LokalerWert")!.Detail);
        // Registry.pol trifft genau (Typ und Wert) – auch ohne Eintrag in gpresult
        Assert.Equal(ValueSource.LocalPolicy, Source(map, Dns, "EnableMulticast"));
        Assert.Equal("Registry.pol", map.Find(Dns, "EnableMulticast")!.Detail);
        // Registry.pol sagt 1, gesetzt ist 0: kommt nicht aus der lokalen Richtlinie
        Assert.Equal(ValueSource.DirectlySet, Source(map, Local, "Abweichend"));
        Assert.Equal(ValueSource.DirectlySet, Source(map, Explorer, "NoAutorun"));
        Assert.Equal(ValueSource.Mdm, Source(map, Mdm, "AllowAutoUpdate"));
        Assert.Equal("Update", map.Find(Mdm, "AllowAutoUpdate")!.Detail);
        // Außerhalb der Richtlinienbereiche ohne Quelle: keine Zuordnung (unknown)
        Assert.Null(map.Find(Lsa, "NoLMHash"));
        Assert.All(map.Entries.Values, v => Assert.False(v.Experimental));
    }

    [Fact]
    public void Domaenenmitglied_mit_gpresult_Domaenen_GPO_ist_experimentell_und_gewinnt()
    {
        var map = Attributor().Build(Prepare(WithGpresult(WithPol(Machine(3)))));
        Assert.True(map.Determinable);
        var dns = map.Find(Dns, "EnableMulticast")!;
        Assert.Equal((ValueSource.DomainGpo, "Linie-3-Basis", true), (dns.Source, dns.Detail, dns.Experimental));
        // Sicherheitsoption mit KeyName MACHINE\…
        Assert.Equal(ValueSource.DomainGpo, Source(map, Lsa, "NoLMHash"));
        Assert.Equal(ValueSource.LocalPolicy, Source(map, Local, "LokalerWert"));
        Assert.Equal(ValueSource.DirectlySet, Source(map, Explorer, "NoAutorun"));
    }

    [Fact]
    public void Administrative_Vorlagen_einer_Domaenen_GPO_verhindern_directlySet()
    {
        var map = Attributor().Build(Prepare(WithGpresult(Machine(3), admx: true)));
        Assert.True(map.Determinable);
        Assert.Null(map.Find(Explorer, "NoAutorun"));
        Assert.Null(map.Find(Local, "Abweichend"));
        Assert.Equal(ValueSource.DomainGpo, Source(map, Dns, "EnableMulticast"));
    }

    [Fact]
    public void Ohne_Admin_ist_die_Herkunft_nicht_bestimmbar()
    {
        // gpresult braucht Admin-Rechte; ohne liefert der Sammler „denied“
        var builder = WithPol(Machine(3, admin: false)).Denied(ItemKeys.RawGpresultXml);
        var map = Attributor().Build(Prepare(builder));
        Assert.False(map.Determinable);
        // Domänenmitglied ohne RSoP: Registry.pol könnte von einer Domänen-GPO überdeckt sein
        Assert.Null(map.Find(Local, "LokalerWert"));
        Assert.Null(map.Find(Explorer, "NoAutorun"));
        Assert.Equal(ValueSource.Mdm, Source(map, Mdm, "AllowAutoUpdate"));

        // In der Arbeitsgruppe gibt es keine Domänen-GPO: Registry.pol genügt
        var workgroup = Attributor().Build(Prepare(WithPol(Machine(0, admin: false)).Denied(ItemKeys.RawGpresultXml)));
        Assert.False(workgroup.Determinable);
        Assert.Equal(ValueSource.LocalPolicy, Source(workgroup, Local, "LokalerWert"));
        Assert.Null(workgroup.Find(Explorer, "NoAutorun"));
    }

    [Fact]
    public void Synthetische_Fixture_mit_Domaenen_GPO()
    {
        var snapshot = RiegelServices.Create(new NullPlatform(), new FixedClock()).Snapshots.Read(RepoPaths.Synthetic("server2022-domain-member"), ReadLimits.Default);
        var map = Attributor().Build(snapshot);
        var dns = map.Find(Dns, "EnableMulticast")!;
        Assert.Equal((ValueSource.DomainGpo, "Linie-3-Basis", true), (dns.Source, dns.Detail, dns.Experimental));
        Assert.Equal(ValueSource.DomainGpo, Source(map, @"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services", "UserAuthentication"));
    }

    [Fact]
    public void Bordmittel_Paket_hat_keine_bestimmbare_Herkunft()
    {
        var snapshot = RiegelServices.Create(new NullPlatform(), new FixedClock()).Snapshots
            .FromBuiltinBundle(Path.Combine(RepoPaths.SyntheticFixtures, "PG-ALT-01_20261010.riegel-builtin"), ReadLimits.Default);
        var map = Attributor().Build(snapshot);
        Assert.False(map.Determinable);
        Assert.Empty(map.Entries);
    }

    [Fact]
    public void Geschwaerzte_Werte_gelten_nie_als_gleich()
    {
        var pol = Pol((@"Software\Policies\Beispiel", "Secret", 1, Sz("x")));
        var builder = Machine(0).RegistryRedacted(Local, "Secret")
            .Api(ItemKeys.RawRegistryPol, new { base64 = Convert.ToBase64String(pol), sha256 = Hashes.Sha256Hex(pol) });
        var map = Attributor().Build(Prepare(builder));
        Assert.Null(map.Find(Local, "Secret"));
    }
}
