// SPDX-License-Identifier: MIT
using System.Collections;
using System.Management;
using System.Runtime.InteropServices;
using Riegel.Collector.Collection;

namespace Riegel.Tests.Windows.Collector;

/// <summary>CIM-Umwandlung (spec 7.3), Fehlerstatus aus WMI-Codes, Pseudonyme und die Übersetzung von Namen in SIDs.</summary>
public sealed class ConversionAndPrivacyTests
{
    [Fact]
    public void CIM_Werte_nach_dem_Vertrag()
    {
        Assert.Equal("18446744073709551615", WmiCollector.Convert(ulong.MaxValue, CimType.UInt64, false));
        Assert.Equal(4294967295u, WmiCollector.Convert(4294967295u, CimType.UInt32, false));
        Assert.Equal(-5L, WmiCollector.Convert((short)-5, CimType.SInt16, false));
        Assert.Equal(200L, WmiCollector.Convert((byte)200, CimType.UInt8, false));
        Assert.Equal(true, WmiCollector.Convert(true, CimType.Boolean, false));
        Assert.Equal(1.5, WmiCollector.Convert(1.5f, CimType.Real32, false));
        Assert.Equal("text", WmiCollector.Convert("text", CimType.String, false));
        Assert.Null(WmiCollector.Convert(null, CimType.String, false));
        var array = (IList)WmiCollector.Convert(new uint[] { 2, 7 }, CimType.UInt32, true);
        Assert.Equal(new object[] { 2u, 7u }, array.Cast<object>());
        var strings = (IList)WmiCollector.Convert(new[] { "a", "b" }, CimType.String, true);
        Assert.Equal(new object[] { "a", "b" }, strings.Cast<object>());
    }

    [Theory]
    [InlineData("20261016042152.500569+120", "2026-10-16T02:21:52.5005690Z")]
    [InlineData("20261016042152.000000-060", "2026-10-16T05:21:52.0000000Z")]
    [InlineData("20261016042152.000000+000", "2026-10-16T04:21:52.0000000Z")]
    public void CIM_Datum_wird_UTC(string dmtf, string expected)
    {
        Assert.True(WmiCollector.TryParseDmtf(dmtf, out var utc));
        Assert.Equal(expected, utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("00000001000000.000000:000")] // Intervall
    [InlineData("2026101604****.******+***")] // Platzhalter
    [InlineData("kein Datum")]
    [InlineData(null)]
    public void Ungueltige_CIM_Daten_werden_nicht_geraten(string? dmtf) => Assert.False(WmiCollector.TryParseDmtf(dmtf!, out _));

    [Theory]
    [InlineData("10/16/2026", "2026-10-16T00:00:00Z")]
    [InlineData("3/2/2025", "2025-03-02T00:00:00Z")]
    [InlineData("20261016", "2026-10-16T00:00:00Z")]
    [InlineData("01dc3d8e7a2b4000", null)]
    public void InstalledOn_der_Hotfixes(string text, string? expected)
    {
        var row = new JObject();
        WmiCollector.AddInstalledOn(row, text);
        if (expected is null)
        {
            // FILETIME in Hex (alte Systeme): ein gültiges Datum, kein Rohtext
            Assert.IsType<string>(row.Get("InstalledOn"));
            Assert.False(row.Has("InstalledOn@raw"));
        }
        else Assert.Equal(expected, row.Get("InstalledOn"));
    }

    [Fact]
    public void Unlesbares_InstalledOn_bleibt_roh()
    {
        var row = new JObject();
        WmiCollector.AddInstalledOn(row, "16.10.2026");
        Assert.Null(row.Get("InstalledOn"));
        Assert.Equal("16.10.2026", row.Get("InstalledOn@raw"));
    }

    [Theory]
    [InlineData(unchecked((int)0x8004100E), "namespaceMissing")]
    [InlineData(unchecked((int)0x80041010), "namespaceMissing")]
    [InlineData(unchecked((int)0x80041003), "denied")]
    [InlineData(unchecked((int)0x80070005), "denied")]
    [InlineData(unchecked((int)0x80041013), "error")]
    public void WMI_Fehler_werden_Status(int code, string status) => Assert.Equal(status, WmiCollector.StatusFor(code));

    [Fact]
    public void Ausnahmen_werden_Status_ohne_Text_des_Rechners()
    {
        var denied = WmiCollector.Failure("wmi:x", new UnauthorizedAccessException(@"C:\Users\someone"));
        Assert.Equal(Statuses.Denied, denied.Status);
        Assert.Equal("Access denied", denied.Error);
        var com = WmiCollector.Failure("wmi:x", new COMException("x", unchecked((int)0x8004100E)));
        Assert.Equal(Statuses.NamespaceMissing, com.Status);
        var other = WmiCollector.Failure("wmi:x", new InvalidOperationException(@"Pfad C:\geheim"));
        Assert.Equal(Statuses.Error, other.Status);
        Assert.DoesNotContain("geheim", other.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Pseudonyme_sind_stabil_und_haengen_an_der_MachineGuid()
    {
        var a = new Pseudonymizer("6df513cb-37f0-4965-9a56-aeea475327d3", "PC");
        var b = new Pseudonymizer("6DF513CB-37F0-4965-9A56-AEEA475327D3", "anders");
        var c = new Pseudonymizer("00000000-0000-4000-8000-000000000001", "PC");
        Assert.Equal(a.Name("operator"), b.Name("Operator"));
        Assert.NotEqual(a.Name("operator"), c.Name("operator"));
        Assert.Matches("^user-[0-9a-f]{8}$", a.Name("operator"));
        Assert.Equal(a.Name("operator"), a.Name("operator"));
        Assert.Equal("", a.Name(""));
        Assert.Equal(@".\" + a.Name("svc"), a.Account(@".\svc"));
        Assert.Equal(@"PC\" + a.Name("svc"), a.Account(@"PC\svc"));
    }

    [Fact]
    public void Profilpfade_werden_pseudonymisiert_gemeinsame_Profile_nicht()
    {
        var p = new Pseudonymizer("6df513cb-37f0-4965-9a56-aeea475327d3", "PC");
        string name = p.Name("operator");
        Assert.Equal($@"C:\Users\{name}\AppData\x.exe", p.Paths(@"C:\Users\operator\AppData\x.exe"));
        Assert.Equal($@"""C:\Users\{name}"";C:\Users\Public\y", p.Paths(@"""C:\Users\operator"";C:\Users\Public\y"));
        Assert.Equal(@"C:\Program Files\x.exe", p.Paths(@"C:\Program Files\x.exe"));
        Assert.Equal(p.Paths(p.Paths(@"C:\Users\operator\a")), p.Paths(@"C:\Users\operator\a"));
    }

    [Fact]
    public void Ohne_MachineGuid_gilt_der_Hostname_als_Schluessel()
    {
        Assert.Equal(new Pseudonymizer(null!, "PC-01").Name("x"), new Pseudonymizer("", "pc-01").Name("x"));
        Assert.Equal(16, Pseudonymizer.KeyBytes("6df513cb-37f0-4965-9a56-aeea475327d3", "x").Length);
    }

    [Fact]
    public void Benutzerrechte_im_INF_werden_in_SIDs_uebersetzt()
    {
        string inf = "[Unicode]\r\nUnicode=yes\r\n[System Access]\r\nNewAdministratorName = \"Administrator\"\r\n[Privilege Rights]\r\n"
                     + "SeDenyNetworkLogonRight = *S-1-5-32-546,Gibt-Es-Nicht-12345\r\nSeBackupPrivilege = *S-1-5-32-544\r\n";
        string plain = RawCollector.TranslatePrivilegeRights(inf, null);
        Assert.Contains("SeDenyNetworkLogonRight = *S-1-5-32-546,Gibt-Es-Nicht-12345", plain, StringComparison.Ordinal);
        Assert.Contains("NewAdministratorName = \"Administrator\"", plain, StringComparison.Ordinal);

        var p = new Pseudonymizer("6df513cb-37f0-4965-9a56-aeea475327d3", "PC");
        string pseudo = RawCollector.TranslatePrivilegeRights(inf, p);
        Assert.Contains("*S-1-5-32-546," + p.Name("Gibt-Es-Nicht-12345"), pseudo, StringComparison.Ordinal);
        Assert.Contains($"NewAdministratorName = \"{p.Name("Administrator")}\"", pseudo, StringComparison.Ordinal);
        Assert.DoesNotContain("Gibt-Es-Nicht", pseudo, StringComparison.Ordinal);
    }

    [Fact]
    public void Bekannte_Namen_werden_beim_Erfassen_zu_SIDs()
    {
        // „NT AUTHORITY\SYSTEM“ ist sprachunabhängig auflösbar (S-1-5-18); nur lesend
        string inf = "[Privilege Rights]\r\nSeTcbPrivilege = NT AUTHORITY\\SYSTEM\r\n";
        Assert.Contains("SeTcbPrivilege = *S-1-5-18", RawCollector.TranslatePrivilegeRights(inf, null), StringComparison.Ordinal);
    }

    [Fact]
    public void Schutzstufe_von_LSASS()
    {
        var ppl = SystemCollector.Protection(0x41);
        Assert.Equal("light", ppl.Get("type"));
        Assert.Equal(4L, ppl.Get("signer"));
        Assert.Equal(65L, ppl.Get("protectionByte"));
        Assert.Equal("none", SystemCollector.Protection(0).Get("type"));
        Assert.Equal("full", SystemCollector.Protection(0x62).Get("type"));
    }

    [Fact]
    public void Wininit_Ereignis_nur_nach_dem_letzten_Start()
    {
        string xml = "<Event><System><TimeCreated SystemTime='2026-10-16T02:22:06.2702438Z'/></System><EventData><Data>4</Data><Data Name='x'>a&amp;b</Data></EventData></Event>";
        var boot = new DateTime(2026, 10, 16, 2, 21, 52, DateTimeKind.Utc);
        var found = SystemCollector.EventValue(xml, boot);
        Assert.Equal(true, found.Get("found"));
        Assert.Equal(new object[] { "4", "a&b" }, ((IList)found.Get("eventData")).Cast<object>());
        var before = SystemCollector.EventValue(xml, boot.AddDays(1));
        Assert.Equal(false, before.Get("found"));
        Assert.Null(before.Get("timeCreatedUtc"));
        Assert.Equal(false, SystemCollector.EventValue(null!, boot).Get("found"));
    }

    [Fact]
    public void Netzwerk_Firewall_Aktionen()
    {
        Assert.Equal("block", ComCollector.Action(0));
        Assert.Equal("allow", ComCollector.Action(1));
        Assert.Null(ComCollector.Action(null));
        Assert.Equal("on", Wsc.State(0));
        Assert.Equal("expired", Wsc.State(3));
    }

    [Fact]
    public void Kennwortrichtlinie_in_Tagen_und_Minuten()
    {
        var modals = AccountCollector.Modals(
            new Riegel.Collector.Collection.Native.USER_MODALS_INFO_0 { MinPasswordLength = 12, MaxPasswordAge = -1, MinPasswordAge = 86400, PasswordHistoryLength = 24 },
            new Riegel.Collector.Collection.Native.USER_MODALS_INFO_3 { LockoutDuration = 900, LockoutObservationWindow = 600, LockoutThreshold = 10 });
        Assert.Null(modals.Get("maxPwAgeDays")); // TIMEQ_FOREVER = unbegrenzt
        Assert.Equal(1L, modals.Get("minPwAgeDays"));
        Assert.Equal(15L, modals.Get("lockoutDurationMin"));
        Assert.Equal(10L, modals.Get("lockoutWindowMin"));
        Assert.Equal(24L, modals.Get("pwHistLen"));
        Assert.Equal("user", AccountCollector.Kind(1));
        Assert.Equal("group", AccountCollector.Kind(4));
        Assert.Equal("wellKnown", AccountCollector.Kind(5));
        Assert.Equal(500L, AccountCollector.Rid("S-1-5-21-1-2-3-500"));
    }
}
