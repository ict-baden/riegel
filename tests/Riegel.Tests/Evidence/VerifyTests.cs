// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using Riegel.Core.About;
using Riegel.Core.Evidence;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Resources;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Evidence;

/// <summary>
/// verify (spec 5 O9, 7.7): reproduziert ein im selben Test gebautes Paket mit der Auswertung der Dienste (bis zur
/// Integration der Platzhalter) und erkennt veränderte, fehlende und zusätzliche Dateien sowie geänderte Ergebnisse.
/// </summary>
public sealed class VerifyTests
{
    [Fact]
    public void Ein_frisch_geschriebenes_Paket_wird_reproduziert()
    {
        using var f = new EvidenceFixture();
        var runs = f.Standard();
        var result = f.Pack.Verify(f.Write(), ReadLimits.Default);
        Assert.True(result.Status == VerifyStatus.Reproduced, string.Join("\n", result.Findings.Select(x => $"{x.Code} {x.Message}")));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(AppInfo.Version, result.EngineVersion);
        Assert.Equal(AppInfo.Version, result.PackedEngineVersion);
        Assert.Equal(runs.Count, result.SnapshotsChecked);
        Assert.Equal(runs.Sum(r => r.Results.Count), result.ResultsCompared);
        Assert.Empty(result.Findings);
        Assert.Empty(result.NotVerifiable);
        var text = string.Format(CultureInfo.GetCultureInfo("de"), DataOutputStrings.ResourceManager.GetString(nameof(DataOutputStrings.Verify_Reproduced),
            CultureInfo.GetCultureInfo("de"))!, result.EngineVersion, result.PackedEngineVersion);
        Assert.StartsWith("reproduziert mit Engine ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Veraenderte_Datei_wird_erkannt()
    {
        using var f = new EvidenceFixture();
        var runs = f.Standard();
        var zip = f.Write();
        var snapshot = $"snapshots/{runs[0].HostName}/{Path.GetFileName(runs[0].Snapshot.Path)}";
        EvidenceFixture.Rewrite(zip, e => e[snapshot] = [.. e[snapshot], (byte)' '], rehash: false);
        var result = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.Equal(VerifyStatus.Mismatch, result.Status);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains(result.Findings, x => x.Code == RglCodesDataOutputs.FileHashMismatch && x.File == snapshot);
    }

    [Fact]
    public void Fehlende_Datei_wird_erkannt()
    {
        using var f = new EvidenceFixture();
        f.Standard();
        var zip = f.Write();
        string? removed = null;
        EvidenceFixture.Rewrite(zip, e => { removed = e.Keys.First(k => k.StartsWith("registers/", StringComparison.Ordinal)); e.Remove(removed); }, rehash: false);
        var result = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.Equal(VerifyStatus.Mismatch, result.Status);
        Assert.Contains(result.Findings, x => x.Code == RglCodesDataOutputs.FileMissing && x.File == removed);
    }

    [Fact]
    public void Zusaetzliche_Datei_wird_erkannt()
    {
        using var f = new EvidenceFixture();
        f.Standard();
        var zip = f.Write();
        EvidenceFixture.Rewrite(zip, e => e["runs/nachgeschoben.txt"] = Encoding.UTF8.GetBytes("x"), rehash: false);
        var result = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.Equal(VerifyStatus.Mismatch, result.Status);
        Assert.Contains(result.Findings, x => x.Code == RglCodesDataOutputs.FileNotListed && x.File == "runs/nachgeschoben.txt");
    }

    [Fact]
    public void Geaendertes_Ergebnis_faellt_bei_der_erneuten_Auswertung_auf_auch_mit_passenden_Pruefsummen()
    {
        using var f = new EvidenceFixture();
        var runs = f.Standard();
        var zip = f.Write();
        var path = $"runs/{runs[0].HostName}/{runs[0].Id}.results.json";
        string? ruleId = null;
        EvidenceFixture.Rewrite(zip, e =>
        {
            var run = RiegelJson.Deserialize<Run>(e[path]);
            var first = run.Results[0];
            ruleId = first.RuleId;
            var changed = first with { Status = first.Status == ResultStatus.Pass ? ResultStatus.Fail : ResultStatus.Pass };
            e[path] = RiegelJson.SerializeToUtf8Bytes(run with { Results = [changed, .. run.Results.Skip(1)] });
        }, rehash: true);
        var result = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.Equal(VerifyStatus.Mismatch, result.Status);
        Assert.Equal(2, result.ExitCode);
        // Die Prüfsummen passen – nur die erneute Auswertung findet die Änderung
        Assert.DoesNotContain(result.Findings, x => x.Code is RglCodesDataOutputs.FileHashMismatch or RglCodesDataOutputs.FileMissing or RglCodesDataOutputs.FileNotListed);
        var finding = Assert.Single(result.Findings, x => x.Code == RglCodesDataOutputs.ResultMismatch);
        Assert.Equal(ruleId, finding.RuleId);
        Assert.Equal(runs[0].HostName, finding.HostName);
        Assert.Contains("status:", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Veraenderter_Katalog_mit_passenden_Pruefsummen_wird_erkannt()
    {
        using var f = new EvidenceFixture();
        f.Standard();
        var zip = f.Write();
        EvidenceFixture.Rewrite(zip, e =>
        {
            var path = "catalogue/riegel-catalogue-2026.10.json";
            var doc = RiegelJson.Deserialize<CatalogueDocument>(e[path]);
            e[path] = RiegelJson.SerializeToUtf8Bytes(doc with { Rules = [.. doc.Rules!.Select(r => r with { Severity = Severity.Low })] });
        }, rehash: true);
        var result = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.Equal(VerifyStatus.Mismatch, result.Status);
        Assert.Contains(result.Findings, x => x.Code == RglCodesDataOutputs.CatalogueMismatch);
    }

    [Fact]
    public void Lauf_mit_fremdem_Katalog_ist_nicht_nachpruefbar_und_damit_abweichend()
    {
        using var f = new EvidenceFixture();
        f.WriteRegisters();
        var other = EvidenceFixture.LoadCatalogue(f.Services, [.. EvidenceFixture.Rules().Take(1)]);
        f.Evaluate(SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").Build(), other);
        var result = f.Pack.Verify(f.Write(), ReadLimits.Default);
        Assert.Equal(VerifyStatus.Mismatch, result.Status);
        Assert.Contains(result.Findings, x => x.Code == RglCodesDataOutputs.RunNotVerifiable && x.HostName == "HMI-TEST");
    }

    [Fact]
    public void Manifest_mit_falschem_Schema_ist_ein_Fehler()
    {
        using var f = new EvidenceFixture();
        f.Standard();
        var zip = f.Write();
        EvidenceFixture.Rewrite(zip, e =>
        {
            var m = RiegelJson.Deserialize<EvidenceManifest>(e["manifest.json"]);
            e["manifest.json"] = RiegelJson.SerializeToUtf8Bytes(m with { Schema = "riegel.evidence/2" });
        }, rehash: false);
        // SHA256SUMS.txt neu, damit nur das Schema falsch ist
        EvidenceFixture.Rewrite(zip, e => e["SHA256SUMS.txt"] = EvidenceLayout.ChecksumFile(e.Where(x => x.Key != "SHA256SUMS.txt")
            .Select(x => KeyValuePair.Create(x.Key, Hashes.Sha256Hex(x.Value)))), rehash: false);
        var result = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.Equal(VerifyStatus.Error, result.Status);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal(RglCodesDataOutputs.ManifestInvalid, Assert.Single(result.Findings).Code);
    }

    [Fact]
    public void Fehlende_oder_kaputte_SHA256SUMS_sind_ein_Fehler()
    {
        using var f = new EvidenceFixture();
        f.Standard();
        var zip = f.Write();
        EvidenceFixture.Rewrite(zip, e => e["SHA256SUMS.txt"] = Encoding.UTF8.GetBytes("kein sha256sum-Format\r\n"), rehash: false);
        var result = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.Equal(VerifyStatus.Error, result.Status);
        Assert.Equal(RglCodesDataOutputs.ChecksumFileInvalid, Assert.Single(result.Findings).Code);
    }

    [Fact]
    public void Keine_Datei_oder_keine_ZIP_ist_ein_Fehler()
    {
        using var temp = new TempFolder();
        using var f = new EvidenceFixture();
        var missing = f.Pack.Verify(Path.Combine(temp.Path, "gibt-es-nicht.zip"), ReadLimits.Default);
        Assert.Equal(3, missing.ExitCode);
        Assert.Equal(RglCodesCommon.NotFound, Assert.Single(missing.Findings).Code);
        var noZip = Path.Combine(temp.Path, "kein.zip");
        File.WriteAllText(noZip, "das ist kein ZIP");
        var result = f.Pack.Verify(noZip, ReadLimits.Default);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal(RglCodesDataOutputs.ZipUnreadable, Assert.Single(result.Findings).Code);
    }
}
