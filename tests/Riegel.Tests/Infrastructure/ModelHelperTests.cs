// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Samples;
using Riegel.Core.Settings;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Infrastructure;

/// <summary>Die Helfer des Vertrags (Model): kanonische Werte, Texte, Dateinamen, Exporte, Lauf-IDs, Inhalts-Hashes.</summary>
public sealed class ModelHelperTests
{
    [Theory]
    [InlineData("{\"t\":\"dword\",\"v\":0}", "0")]
    [InlineData("{\"t\":\"dword\",\"v\":4294967295}", "4294967295")]
    [InlineData("{\"t\":\"qword\",\"v\":\"18446744073709551615\"}", "18446744073709551615")]
    [InlineData("{\"t\":\"sz\",\"v\":\"Wert mit Umlaut ä\"}", "Wert mit Umlaut ä")]
    [InlineData("{\"t\":\"multi_sz\",\"v\":[\"a\",\"b\"]}", "[\"a\",\"b\"]")]
    [InlineData("{\"t\":\"binary\",\"v\":\"AQID\"}", "hex:010203")]
    [InlineData("{\"t\":\"sz\",\"redacted\":true,\"empty\":false}", "redacted")]
    [InlineData("{\"t\":\"sz\",\"redacted\":true,\"empty\":true}", "redacted:empty")]
    public void Registry_Werte_werden_kanonisch_dargestellt(string json, string expected) =>
        Assert.Equal(expected, CanonicalValue.RenderRegistry(JsonValues.Parse(json)));

    [Fact]
    public void Kanonische_Darstellung_und_Vergleich()
    {
        Assert.Equal("true", CanonicalValue.Render(JsonValues.True));
        Assert.Null(CanonicalValue.Render(JsonValues.Null));
        Assert.Null(CanonicalValue.Render(null));
        Assert.Equal("{\"a\":1,\"b\":[true]}", CanonicalValue.Render(JsonValues.Parse("{\"b\":[true],\"a\":1}")));
        Assert.True(CanonicalValue.AreEqual("0", "0x0"));
        Assert.True(CanonicalValue.AreEqual("255", "0xFF"));
        Assert.False(CanonicalValue.AreEqual("1", null));
        Assert.True(CanonicalValue.AreEqual(null, null));
        Assert.False(CanonicalValue.AreEqual("abc", "ABC"));
        var bytes = Enumerable.Range(0, 40).Select(i => (byte)i).ToArray();
        Assert.Equal("hex:" + Convert.ToHexString(bytes[..32]).ToLowerInvariant() + "…", CanonicalValue.Binary(bytes));
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("(nicht gesetzt)", CanonicalValue.Display(null));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            Assert.Equal("(not set)", CanonicalValue.Display(null));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Texte_fallen_auf_Englisch_zurueck()
    {
        var text = LocalizedText.Of(("en", "Pass"), ("de", "Erfüllt"));
        Assert.Equal("Erfüllt", text.Get("de"));
        Assert.Equal("Erfüllt", text.Get("de-AT"));
        Assert.Equal("Pass", text.Get("fr"));
        Assert.Equal("Pass", text.Get("zh-Hans"));
        Assert.Equal("Pass", text.Get(null));
        Assert.Equal(text, LocalizedText.Of(("de", "Erfüllt"), ("en", "Pass")));
        Assert.Equal("{\n  \"de\": \"Erfüllt\",\n  \"en\": \"Pass\"\n}\n", RiegelJson.Serialize(text));
    }

    [Fact]
    public void Exportdateien_tragen_den_kanonischen_Namen_und_eine_Pruefsumme()
    {
        using var temp = new TempFolder();
        var snapshot = SnapshotBuilder.Windows11Workgroup("hmi-l3-01").CollectedAt(new DateTimeOffset(2026, 10, 15, 8, 0, 5, TimeSpan.Zero)).Build();
        var path = SnapshotFile.Write(snapshot, temp.Path);
        Assert.Equal("HMI-L3-01_20261015T080005Z.snapshot.json", Path.GetFileName(path));
        var sidecar = File.ReadAllText(SnapshotFile.SidecarPath(path));
        Assert.Equal($"{Hashes.Sha256File(path)}  HMI-L3-01_20261015T080005Z.snapshot.json\n", sidecar);
        Assert.Equal(HashCheckStatus.Ok, SnapshotFile.Verify(path).Status);
        File.AppendAllText(path, " ");
        Assert.Equal(HashCheckStatus.Mismatch, SnapshotFile.Verify(path).Status);
        Assert.Throws<IOException>(() => SnapshotFile.Write(snapshot, temp.Path));
        var gz = SnapshotFile.Write(snapshot, temp.Path, gzip: true);
        Assert.EndsWith(".snapshot.json.gz", gz, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HMI-L3-01", "HMI-L3-01")]
    [InlineData("Musterwerk – Linie 3", "Musterwerk-Linie-3")]
    [InlineData("Härtung/Prüfung", "Haertung-Pruefung")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("..\\..\\böse", "boese")]
    [InlineData("", "unknown")]
    public void Dateinamen_werden_bereinigt(string input, string expected) => Assert.Equal(expected, FileNames.Host(input));

    [Fact]
    public void Lauf_IDs_sind_deterministisch()
    {
        var a = RunIds.Create(FixedClock.Default, new string('a', 64), new string('b', 64), RegisterHashes.Empty);
        var b = RunIds.Create(FixedClock.Default, new string('a', 64), new string('b', 64), RegisterHashes.Empty);
        var c = RunIds.Create(FixedClock.Default, new string('c', 64), new string('b', 64), RegisterHashes.Empty);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.StartsWith("20261018T090000Z-", a, StringComparison.Ordinal);
        Assert.True(RunIds.IsValid(a));
        Assert.False(RunIds.IsValid("20261018T090000Z-XYZ"));
    }

    [Fact]
    public void Inhalts_Hash_ignoriert_die_Freigabe_und_erkennt_Aenderungen()
    {
        var entry = SampleData.Exceptions()[0];
        var approved = entry with { Approval = entry.Approval with { ApprovalRecord = "CHG-ANDERS" } };
        Assert.Equal(ContentHashes.Of(entry), ContentHashes.Of(approved));
        Assert.NotEqual(ContentHashes.Of(entry), ContentHashes.Of(entry with { Justification = entry.Justification + "!" }));
        Assert.Equal(entry.Approval.ApprovedContentSha256, ContentHashes.Of(entry));
    }

    [Fact]
    public void Zeitpunkte_brauchen_eine_Zeitzone()
    {
        Assert.True(UtcDateTimeOffsetConverter.TryParse("2026-10-18T11:00:00+02:00", out var t));
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero), t);
        Assert.False(UtcDateTimeOffsetConverter.TryParse("2026-10-18T11:00:00", out _));
        Assert.False(UtcDateTimeOffsetConverter.TryParse("18.10.2026 11:00", out _));
        Assert.Equal("2026-10-18T09:00:00.5Z", UtcDateTimeOffsetConverter.Format(FixedClock.Default.AddMilliseconds(500)));
    }

    [Fact]
    public void Einstellungen_Benutzer_vor_Vorgabe_vor_Standard()
    {
        var effective = SettingsStore.Resolve(new RiegelSettings(Language: "de", WarnDays: 14), new RiegelSettings(Language: "en", StaleDays: 60, UpdateMaxDays: 21));
        Assert.Equal("de", effective.Language);
        Assert.Equal(14, effective.WarnDays);
        Assert.Equal(60, effective.StaleDays);
        Assert.Equal(21, effective.UpdateMaxDays);
        Assert.Equal(90, effective.UnusedAccountDays);
        Assert.True(effective.IsPreset("staleDays"));
        Assert.False(effective.IsPreset("warnDays"));
        Assert.Equal("/x/riegel-daten", AppPaths.ResolveDataFolder(n => n == AppPaths.DataDirVariable ? "/x/riegel-daten" : null, windows: false, macOS: false));
        Assert.Equal(Path.Combine("/home/test", ".local", "share", "riegel"), AppPaths.ResolveDataFolder(n => n == "HOME" ? "/home/test" : null, windows: false, macOS: false));
    }

    [Fact]
    public void Kaputte_Einstellungen_fuehren_zu_Standardwerten()
    {
        using var temp = new TempFolder();
        var file = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(file, "{ kaputt");
        Assert.Equal(RiegelSettings.Empty, SettingsStore.Load(file));
        SettingsStore.Save(new RiegelSettings(Language: "fr"), file);
        Assert.Equal("fr", SettingsStore.Load(file).Language);
    }

    [Fact]
    public void Diagnose_ersetzt_Namen_Adressen_SIDs_und_Hosts()
    {
        var anonymizer = new Core.Diagnostics.Anonymizer("PC-LABOR-01", "techniker", "WERK", ["HMI-L3-01", "HIST-01"]);
        var text = anonymizer.Apply(@"PC-LABOR-01 an HMI-L3-01 und HIST-01: \\fileserver\freigabe\x, //nas01/share, S-1-5-21-1-2-3-1001, 192.0.2.7, max@example.com, C:\Users\techniker\a.txt, WERK\techniker, S-1-5-32-544");
        Assert.DoesNotContain("PC-LABOR-01", text);
        Assert.DoesNotContain("HMI-L3-01", text);
        Assert.DoesNotContain("HIST-01", text);
        Assert.DoesNotContain("fileserver", text);
        Assert.DoesNotContain("nas01", text);
        Assert.DoesNotContain("1001", text);
        Assert.DoesNotContain("192.0.2.7", text);
        Assert.DoesNotContain("example.com", text);
        Assert.DoesNotContain("techniker", text);
        Assert.Contains("S-1-5-32-544", text);
        Assert.Contains("HOST-1", text);
    }
}
