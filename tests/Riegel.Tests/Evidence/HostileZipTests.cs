// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Text;
using Riegel.Core.Evidence;
using Riegel.Core.Model;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Evidence;

/// <summary>
/// Feindliche Nachweispakete (spec 7.12, 8.8): ZIP-Bomben, „..“, absolute Namen, riesige Einträge, zu viele Einträge,
/// falsches Schema. Ergebnis ist immer ein RGL-Code mit Exitcode 3 – nie eine Ausnahme und nie eine Datei außerhalb.
/// </summary>
public sealed class HostileZipTests
{
    private static string Zip(TempFolder temp, Action<ZipArchive> fill, string name = "feindlich.zip")
    {
        var path = Path.Combine(temp.Path, name);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) fill(zip);
        return path;
    }

    private static void Entry(ZipArchive zip, string name, byte[] bytes, CompressionLevel level = CompressionLevel.Optimal)
    {
        var e = zip.CreateEntry(name, level);
        using var s = e.Open();
        s.Write(bytes);
    }

    private static VerifyResult Verify(string zip, ReadLimits? limits = null)
    {
        using var f = new EvidenceFixture();
        return f.Pack.Verify(zip, limits ?? ReadLimits.Default);
    }

    [Theory]
    [InlineData("../ausserhalb.txt")]
    [InlineData("runs/../../ausserhalb.txt")]
    [InlineData("/absolut.txt")]
    [InlineData("C:/Windows/absolut.txt")]
    [InlineData("runs\\backslash.txt")]
    [InlineData("runs/./punkt.txt")]
    [InlineData("runs//doppelt.txt")]
    [InlineData("steuer\u0001zeichen.txt")]
    [InlineData("datei.txt:stream")]
    public void Unsichere_Eintragsnamen_werden_abgelehnt(string name)
    {
        using var temp = new TempFolder();
        var zip = Zip(temp, z => { Entry(z, "manifest.json", "{}"u8.ToArray()); Entry(z, name, "x"u8.ToArray()); });
        var result = Verify(zip);
        Assert.Equal(VerifyStatus.Error, result.Status);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal(RglCodesDataOutputs.UnsafeEntryName, Assert.Single(result.Findings).Code);
        // Nichts wurde neben das Paket oder darüber geschrieben
        Assert.Equal(["feindlich.zip"], Directory.EnumerateFileSystemEntries(temp.Path).Select(Path.GetFileName));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(temp.Path)!, "ausserhalb.txt")));
    }

    [Fact]
    public void Doppelte_Namen_ohne_Gross_klein_werden_abgelehnt()
    {
        using var temp = new TempFolder();
        var zip = Zip(temp, z => { Entry(z, "README.txt", "a"u8.ToArray()); Entry(z, "readme.TXT", "b"u8.ToArray()); });
        Assert.Equal(RglCodesDataOutputs.UnsafeEntryName, Assert.Single(Verify(zip).Findings).Code);
    }

    [Fact]
    public void ZIP_Bombe_ueber_der_Grenze_je_Eintrag_wird_abgelehnt()
    {
        using var temp = new TempFolder();
        // 16 MB Nullen packen sich auf wenige KB
        var zip = Zip(temp, z => Entry(z, "snapshots/bombe.snapshot.json", new byte[16 * 1024 * 1024]));
        Assert.True(new FileInfo(zip).Length < 200_000);
        var result = Verify(zip, ReadLimits.Default with { MaxEvidenceEntryBytes = 4 * 1024 * 1024 });
        Assert.Equal(VerifyStatus.Error, result.Status);
        Assert.Equal(RglCodesDataOutputs.EntryTooLarge, Assert.Single(result.Findings).Code);
    }

    [Fact]
    public void Summe_der_entpackten_Eintraege_ist_begrenzt()
    {
        using var temp = new TempFolder();
        var zip = Zip(temp, z =>
        {
            for (int i = 0; i < 8; i++) Entry(z, $"runs/teil{i}.json", new byte[1024 * 1024]);
        });
        var result = Verify(zip, ReadLimits.Default with { MaxEvidenceZipBytes = 4 * 1024 * 1024, MaxEvidenceEntryBytes = 2 * 1024 * 1024 });
        Assert.Equal(RglCodesDataOutputs.EntryTooLarge, Assert.Single(result.Findings).Code);
    }

    [Fact]
    public void Riesiger_unkomprimierter_Eintrag_wird_vor_dem_Lesen_abgelehnt()
    {
        using var temp = new TempFolder();
        var zip = Zip(temp, z => Entry(z, "groß.bin", new byte[3 * 1024 * 1024], CompressionLevel.NoCompression));
        var result = Verify(zip, ReadLimits.Default with { MaxEvidenceEntryBytes = 1024 * 1024 });
        Assert.Equal(RglCodesDataOutputs.EntryTooLarge, Assert.Single(result.Findings).Code);
    }

    [Fact]
    public void Datei_ueber_der_Paketgrenze_wird_nicht_geoeffnet()
    {
        using var temp = new TempFolder();
        var zip = Zip(temp, z => Entry(z, "daten.bin", new byte[256 * 1024], CompressionLevel.NoCompression));
        var result = Verify(zip, ReadLimits.Default with { MaxEvidenceZipBytes = 64 * 1024 });
        Assert.Equal(RglCodesDataOutputs.ZipTooLarge, Assert.Single(result.Findings).Code);
    }

    [Fact]
    public void Zu_viele_Eintraege_werden_abgelehnt()
    {
        using var temp = new TempFolder();
        var zip = Zip(temp, z =>
        {
            for (int i = 0; i <= EvidenceArchive.MaxEntries; i++) z.CreateEntry("e/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), CompressionLevel.NoCompression);
        });
        Assert.Equal(RglCodesDataOutputs.TooManyEntries, Assert.Single(Verify(zip).Findings).Code);
    }

    [Fact]
    public void Paket_ohne_Pruefsummen_und_ohne_Manifest_ist_ein_Fehler()
    {
        using var temp = new TempFolder();
        var zip = Zip(temp, z => Entry(z, "README.txt", "nur Text"u8.ToArray()));
        var result = Verify(zip);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal(RglCodesDataOutputs.ChecksumFileInvalid, Assert.Single(result.Findings).Code);
    }

    [Fact]
    public void Manifest_mit_unbekannten_Feldern_oder_kaputtem_JSON_ist_ein_Fehler()
    {
        using var temp = new TempFolder();
        foreach (var manifest in new[] { "{\"schema\":\"riegel.evidence/1\",\"boese\":true}", "{ kaputt", "[]" })
        {
            var bytes = Encoding.UTF8.GetBytes(manifest);
            var sums = EvidenceLayout.ChecksumFile([KeyValuePair.Create("manifest.json", Hashes.Sha256Hex(bytes))]);
            var zip = Zip(temp, z => { Entry(z, "manifest.json", bytes); Entry(z, "SHA256SUMS.txt", sums); }, Guid.NewGuid().ToString("N") + ".zip");
            var result = Verify(zip);
            Assert.Equal(VerifyStatus.Error, result.Status);
            Assert.Equal(RglCodesDataOutputs.ManifestInvalid, Assert.Single(result.Findings).Code);
        }
    }

    [Fact]
    public void Feindlicher_Pfad_im_Manifest_wird_abgelehnt()
    {
        using var f = new EvidenceFixture();
        f.Standard();
        var zip = f.Write();
        EvidenceFixture.Rewrite(zip, e =>
        {
            var m = Core.Model.Json.RiegelJson.Deserialize<EvidenceManifest>(e["manifest.json"]);
            e["manifest.json"] = Core.Model.Json.RiegelJson.SerializeToUtf8Bytes(m with { Files = [.. m.Files, new FileRef("../../etc/passwd", new string('0', 64))] });
            e.Remove("SHA256SUMS.txt");
            e["SHA256SUMS.txt"] = EvidenceLayout.ChecksumFile(e.Select(x => KeyValuePair.Create(x.Key, Hashes.Sha256Hex(x.Value))));
        }, rehash: false);
        var result = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.Equal(VerifyStatus.Error, result.Status);
        Assert.Equal(RglCodesDataOutputs.ManifestInvalid, Assert.Single(result.Findings).Code);
    }

    [Fact]
    public void Feindlicher_Export_im_Paket_ist_ein_Befund_kein_Absturz()
    {
        using var f = new EvidenceFixture();
        var runs = f.Standard();
        var zip = f.Write();
        var snapshot = $"snapshots/{runs[0].HostName}/{Path.GetFileName(runs[0].Snapshot.Path)}";
        // Tiefe Verschachtelung, falsches Schema – mit passenden Prüfsummen
        var resultsPath = $"runs/{runs[0].HostName}/{runs[0].Id}.results.json";
        EvidenceFixture.Rewrite(zip, e =>
        {
            e[snapshot] = Encoding.UTF8.GetBytes(new string('[', 100_000));
            var run = Core.Model.Json.RiegelJson.Deserialize<Run>(e[resultsPath]);
            e[resultsPath] = Core.Model.Json.RiegelJson.SerializeToUtf8Bytes(run with { Snapshot = run.Snapshot with { Sha256 = Hashes.Sha256Hex(e[snapshot]) } });
        }, rehash: true);
        var result = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.Equal(VerifyStatus.Mismatch, result.Status);
        Assert.Contains(result.Findings, x => x.Code == RglCodesDataOutputs.PackedFileUnreadable && x.File == snapshot);
    }
}
