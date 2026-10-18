// SPDX-License-Identifier: MIT
using System.Text;
using Riegel.Core.Evidence;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Samples;
using Riegel.Core.Services;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Evidence;

/// <summary>Das Nachweispaket (O9): Aufbau, Manifest, SHA256SUMS.txt, README, Register wie verwendet, Baselines, Grenzen.</summary>
public sealed class EvidenceWriteTests
{
    [Fact]
    public void Paket_hat_den_Aufbau_aus_spec_5_O9()
    {
        using var f = new EvidenceFixture();
        var runs = f.Standard();
        var zip = f.Write();
        Assert.Equal("Riegel-Evidence_Testanlage_2026-10-18.zip", Path.GetFileName(zip));
        var entries = EvidenceFixture.ReadAll(zip);
        foreach (var name in new[] { "manifest.json", "SHA256SUMS.txt", "README.txt", "plant/riegel-plant.json",
                     "catalogue/riegel-catalogue-2026.10.json", "Riegel-Report_Testanlage_2026-10-18.pdf", "results.csv" })
            Assert.Contains(name, entries.Keys);
        foreach (var run in runs)
        {
            Assert.Contains($"runs/{run.HostName}/{run.Id}.results.json", entries.Keys);
            var snapshot = $"snapshots/{run.HostName}/{Path.GetFileName(run.Snapshot.Path)}";
            Assert.Contains(snapshot, entries.Keys);
            var sidecar = Encoding.UTF8.GetString(entries[snapshot + ".sha256"]);
            Assert.Equal(SnapshotFile.SidecarLine(run.Snapshot.Sha256, Path.GetFileName(run.Snapshot.Path)), sidecar);
            Assert.Equal(run.Snapshot.Sha256, Hashes.Sha256Hex(entries[snapshot]));
        }
        Assert.Contains(entries.Keys, k => k.StartsWith("registers/exceptions_", StringComparison.Ordinal));
        Assert.Contains(entries.Keys, k => k.StartsWith("registers/tailoring_", StringComparison.Ordinal));
        Assert.Contains(entries.Keys, k => k.StartsWith("registers/measures_", StringComparison.Ordinal));
        // Der Katalog ist ein vollständiges riegel.catalogue/1-Dokument
        var catalogue = RiegelJson.Deserialize<CatalogueDocument>(entries["catalogue/riegel-catalogue-2026.10.json"]);
        Assert.Equal(SchemaIds.Catalogue, catalogue.Schema);
        Assert.Equal(f.Catalogue.Rules.Count, catalogue.Rules!.Count);
    }

    [Fact]
    public void Manifest_ist_schema_gueltig_und_nennt_jede_Datei_mit_Pruefsumme()
    {
        using var f = new EvidenceFixture();
        var runs = f.Standard();
        var zip = f.Write();
        var entries = EvidenceFixture.ReadAll(zip);
        var json = Encoding.UTF8.GetString(entries["manifest.json"]);
        SchemaValidator.AssertValid(json, "manifest.json");
        var m = RiegelJson.Deserialize<EvidenceManifest>(entries["manifest.json"]);
        Assert.Equal(SchemaIds.Evidence, m.Schema);
        Assert.Equal("testanlage", m.PlantId);
        Assert.Equal(new DateOnly(2026, 10, 18), m.EvaluationDate);
        Assert.Equal(FixedClock.Default.AddMinutes(1), m.CreatedAtUtc);
        Assert.Equal(f.Catalogue.Sha256, m.Catalogue.Sha256);
        Assert.Equal("Anlagenverantwortlicher", m.OperatorRole);
        Assert.Equal(runs.Select(r => r.Id).Order(), m.Runs.Select(r => r.RunId).Order());
        Assert.Equal(runs[0].Registers, m.Registers);
        var listed = m.Files.ToDictionary(x => x.Path, x => x.Sha256);
        Assert.Equal(entries.Keys.Where(k => k is not "manifest.json" and not "SHA256SUMS.txt").Order(StringComparer.Ordinal),
            listed.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, sha) in listed) Assert.Equal(Hashes.Sha256Hex(entries[path]), sha);
        Assert.Equal(Hashes.Sha256Hex(entries[m.CatalogueFile.Path]), m.CatalogueFile.Sha256);
    }

    [Fact]
    public void SHA256SUMS_hat_LF_ist_sortiert_und_deckt_alle_Dateien_ab()
    {
        using var f = new EvidenceFixture();
        f.Standard();
        var entries = EvidenceFixture.ReadAll(f.Write());
        var bytes = entries["SHA256SUMS.txt"];
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        Assert.False(bytes[0] == 0xEF, "kein BOM");
        var lines = text.TrimEnd('\n').Split('\n');
        Assert.Equal(lines.OrderBy(l => l[66..], StringComparer.Ordinal), lines);
        var parsed = EvidenceLayout.ParseChecksums(bytes)!;
        Assert.Equal(entries.Keys.Where(k => k != "SHA256SUMS.txt").Order(StringComparer.Ordinal), parsed.Keys);
        foreach (var (path, sha) in parsed) Assert.Equal(Hashes.Sha256Hex(entries[path]), sha);
        Assert.Matches("^[0-9a-f]{64}  manifest\\.json$", lines.Single(l => l.EndsWith("  manifest.json", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("de", "Die SHA-256-Prüfsummen zeigen", "verify")]
    [InlineData("en", "The SHA-256 checksums show", "verify")]
    [InlineData("fr", "Les sommes de contrôle SHA-256", "verify")]
    public void README_erklaert_Pruefsummen_verify_compare_snapshot_und_die_Integritaetsaussage(string lang, string integrity, string verify)
    {
        using var f = new EvidenceFixture();
        f.Standard();
        var readme = Encoding.UTF8.GetString(EvidenceFixture.ReadAll(f.Write(language: lang))["README.txt"]);
        Assert.Contains(integrity, readme, StringComparison.Ordinal);
        // Integritätsaussage immer auch auf Deutsch und Englisch (spec 5)
        Assert.Contains("Sie beweisen nicht, dass ein Export unverändert vom Zielrechner stammt", readme, StringComparison.Ordinal);
        Assert.Contains("They do not prove that an export came unchanged from the target machine", readme, StringComparison.Ordinal);
        Assert.Contains("sha256sum -c SHA256SUMS.txt", readme, StringComparison.Ordinal);
        Assert.Contains("Riegel.Cli.exe " + verify + " Riegel-Evidence_Testanlage_2026-10-18.zip", readme, StringComparison.Ordinal);
        Assert.Contains("compare-snapshot", readme, StringComparison.Ordinal);
        Assert.Contains("\r\n", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_wie_verwendet_auch_die_fruehere_Fassung_aus_dem_Verlauf()
    {
        using var f = new EvidenceFixture();
        var old = f.WriteRegisters();
        var run = f.Evaluate(SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").Build());
        // Danach ändert jemand das Register; die alte Fassung liegt im Verlauf (spec 2.11)
        var history = Path.Combine(f.Plant.Folder, "history", "exceptions");
        Directory.CreateDirectory(history);
        File.Copy(f.Plant.PathOf("exceptions.json"), Path.Combine(history, "20261018T090000Z_" + Hashes.Short(old.Exceptions) + ".json"));
        var current = f.WriteRegisters(" Ergänzt nach dem Lauf.");
        Assert.NotEqual(old.Exceptions, current.Exceptions);

        var zip = f.Write();
        var entries = EvidenceFixture.ReadAll(zip);
        Assert.Equal(old.Exceptions, Hashes.Sha256Hex(entries["registers/exceptions_" + Hashes.Short(old.Exceptions) + ".json"]));
        Assert.Equal(current.Exceptions, Hashes.Sha256Hex(entries["registers/exceptions_" + Hashes.Short(current.Exceptions) + ".json"]));
        Assert.Equal(current, EvidenceFixture.Manifest(zip).Registers);
        Assert.Equal(old, run.Registers);
        // verify wertet mit der Fassung aus, die der Lauf benutzt hat
        var result = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.Equal(VerifyStatus.Reproduced, result.Status);
    }

    [Fact]
    public void Nie_die_Rohdaten_eines_Bordmittel_Pakets()
    {
        using var f = new EvidenceFixture();
        f.Standard();
        // Ein Bordmittel-Paket liegt (wie nach einem Import) neben den Exporten im Anlagenordner
        var source = Path.Combine(RepoPaths.SyntheticFixtures, "PG-ALT-01_20261010.riegel-builtin");
        var target = Path.Combine(f.Plant.Folder, "snapshots", "HMI-TEST", "PG-ALT-01_20261010.riegel-builtin");
        CopyDirectory(source, target);
        var entries = EvidenceFixture.ReadAll(f.Write());
        Assert.DoesNotContain(entries.Keys, k => k.Contains("riegel-builtin", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries.Keys, k => k.EndsWith("secedit.inf", StringComparison.OrdinalIgnoreCase) || k.EndsWith(".reg", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Bordmittel", Encoding.UTF8.GetString(entries["README.txt"]), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("snapshots/HMI-TEST/PG-ALT-01_20261010.riegel-builtin")]
    [InlineData("../ausserhalb.snapshot.json")]
    [InlineData("snapshots/HMI-TEST/../../../ausserhalb.snapshot.json")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("riegel-plant.json")]
    public void Lauf_mit_unzulaessigem_Exportpfad_wird_nicht_gepackt(string path)
    {
        using var f = new EvidenceFixture();
        var run = f.Evaluate(SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").Build());
        Directory.CreateDirectory(Path.Combine(f.Plant.Folder, "snapshots", "HMI-TEST", "PG-ALT-01_20261010.riegel-builtin"));
        var file = f.Plant.PathOf(ReportFileNames.RunFile(run.HostName, run.Id));
        RiegelJson.WriteFile(file, run with { Snapshot = run.Snapshot with { Path = path } });
        var e = Assert.Throws<RiegelException>(() => f.Write());
        Assert.Equal(RglCodesDataOutputs.RunNotPackable, e.Code);
    }

    [Fact]
    public void Veraenderter_Export_im_Anlagenordner_wird_nicht_gepackt()
    {
        using var f = new EvidenceFixture();
        var run = f.Evaluate(SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").Build());
        File.AppendAllText(f.Plant.PathOf(run.Snapshot.Path), " ");
        Assert.Equal(RglCodesDataOutputs.RunNotPackable, Assert.Throws<RiegelException>(() => f.Write()).Code);
    }

    [Fact]
    public void Berichtsdatei_die_fehlt_bricht_mit_RGL_Code_ab()
    {
        using var f = new EvidenceFixture();
        var request = new EvidenceRequest(f.Plant.Plant, null, new DateOnly(2026, 10, 18), [Path.Combine(f.Out.Path, "fehlt.pdf")], false, null, "de", f.Out.Path);
        Assert.Equal(RglCodesDataOutputs.DocumentNotPackable, Assert.Throws<RiegelException>(() => f.Pack.Write(request)).Code);
    }

    [Fact]
    public void Nur_die_gewaehlten_Rechner()
    {
        using var f = new EvidenceFixture();
        f.Standard();
        var m = EvidenceFixture.Manifest(f.Write(hosts: ["w11-test"]));
        Assert.Equal(["W11-TEST"], m.Runs.Select(r => r.HostName));
    }

    [Fact]
    public void Anlage_ohne_Ordner_ergibt_ein_Paket_ohne_Laeufe()
    {
        using var temp = new TempFolder();
        var services = RiegelServices.Create(new NullPlatform(), new FixedClock());
        var pack = EvidenceFactory.Create(services);
        var zip = pack.Write(new EvidenceRequest(SampleData.Plant(), null, SampleData.Date, [], false, null, "de", temp.Path));
        var m = EvidenceFixture.Manifest(zip);
        Assert.Empty(m.Runs);
        SchemaValidator.AssertValid(Encoding.UTF8.GetString(EvidenceFixture.ReadAll(zip)["manifest.json"]), "manifest.json");
        Assert.Equal(VerifyStatus.Reproduced, pack.Verify(zip, ReadLimits.Default).Status);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
