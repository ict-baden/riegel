// SPDX-License-Identifier: MIT
using System.Text;
using Riegel.Core.Evidence;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Evidence;

/// <summary>Stichprobe des Prüfers (spec 2.14, compare-snapshot): konsistent, abweichend, Rechner fehlt, Paket verändert.</summary>
public sealed class CompareSampleTests
{
    [Fact]
    public void Gleicher_Export_ist_konsistent()
    {
        using var f = new EvidenceFixture();
        var runs = f.Standard();
        var zip = f.Write();
        var fresh = f.Plant.PathOf(runs[0].Snapshot.Path);
        var result = f.Pack.CompareSample(fresh, zip, null, ReadLimits.Default);
        Assert.Equal(SampleStatus.Consistent, result.Status);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Comparison);
        Assert.True(result.Comparison!.Consistent);
        Assert.Equal(runs[0].HostName, result.Comparison.HostName);
    }

    [Fact]
    public void Geaenderter_Export_weicht_ab_mit_der_Liste()
    {
        using var f = new EvidenceFixture();
        f.Standard();
        var zip = f.Write();
        using var temp = new TempFolder();
        var fresh = SnapshotBuilder.Windows10Ltsc2019("HMI-TEST")
            .RegistryValue(@"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "RequireSecuritySignature", RegValueType.Dword, 1)
            .CollectedAt(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero))
            .WriteTo(temp.Path);
        var result = f.Pack.CompareSample(fresh, zip, null, ReadLimits.Default);
        Assert.Equal(SampleStatus.Different, result.Status);
        Assert.Equal(1, result.ExitCode);
        Assert.NotEmpty(result.Comparison!.Differences);
        Assert.Contains(result.Comparison.Differences, d => d.Key.Contains("LanmanServer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rechner_nicht_im_Paket_ist_ein_Fehler()
    {
        using var f = new EvidenceFixture();
        f.Standard();
        var zip = f.Write();
        using var temp = new TempFolder();
        var fresh = SnapshotBuilder.Windows11Workgroup("FREMD-01").WriteTo(temp.Path);
        var result = f.Pack.CompareSample(fresh, zip, null, ReadLimits.Default);
        Assert.Equal(SampleStatus.Error, result.Status);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal(RglCodesDataOutputs.HostNotInPack, result.ErrorCode);
        // Mit --host wird der Rechner im Paket ausdrücklich gewählt
        var chosen = f.Pack.CompareSample(fresh, zip, "w11-test", ReadLimits.Default);
        Assert.NotEqual(SampleStatus.Error, chosen.Status);
    }

    [Fact]
    public void Verandertes_Paket_taugt_nicht_als_Vergleichsgrundlage()
    {
        using var f = new EvidenceFixture();
        var runs = f.Standard();
        var zip = f.Write();
        var snapshot = $"snapshots/{runs[0].HostName}/{Path.GetFileName(runs[0].Snapshot.Path)}";
        EvidenceFixture.Rewrite(zip, e => e[snapshot] = [.. e[snapshot], (byte)'\n'], rehash: false);
        var result = f.Pack.CompareSample(f.Plant.PathOf(runs[0].Snapshot.Path), zip, null, ReadLimits.Default);
        Assert.Equal(SampleStatus.Error, result.Status);
        Assert.Equal(RglCodesDataOutputs.FileHashMismatch, result.ErrorCode);
    }

    [Fact]
    public void Unlesbarer_frischer_Export_ist_ein_Fehler()
    {
        using var f = new EvidenceFixture();
        f.Standard();
        var zip = f.Write();
        using var temp = new TempFolder();
        var bad = Path.Combine(temp.Path, "kaputt.snapshot.json");
        File.WriteAllText(bad, "{ nicht json", new UTF8Encoding(false));
        var result = f.Pack.CompareSample(bad, zip, null, ReadLimits.Default);
        Assert.Equal(3, result.ExitCode);
        Assert.Matches("^RGL-\\d{4}$", result.ErrorCode!);
    }
}

/// <summary>Kunden-Baselines im Paket (spec 2.15): nur Metadaten (nicht nachprüfbar) oder mit Inhalt und Lizenzwarnung.</summary>
public sealed class EvidenceBaselineTests
{
    private const string Id = "kunde";

    /// <summary>Gespeicherte Baseline (baselines/kunde.json), daraus Regeln, damit ausgewertet; der Lauf nennt die Baseline.</summary>
    private static (Run Run, BaselineRef Baseline) EvaluateWithBaseline(EvidenceFixture f)
    {
        f.WriteRegisters();
        var stored = Path.Combine(f.Plant.Folder, "baselines", Id + ".json");
        RiegelJson.WriteFile(stored, new BaselineFile(SchemaIds.Baseline, Id, "Kundenvorgaben (Test)", null, "kunde.json", null, null,
        [
            new BaselineRule("R1", "OPC UA nur auf Port 4840", Severity.Medium, "network",
                new RegistryCheck(@"HKLM\SOFTWARE\Policies\Beispiel\OpcUa", "Port", RegValueType.Dword, null, CheckOperator.Eq, JsonValues.Number(4840),
                    WhenMissing.Fail, null, null, null), null, null),
        ]));
        var imported = f.Services.Baselines.Import(stored, "Kundenvorgaben (Test)", ReadLimits.Default);
        var combined = f.Services.Catalogue.Combine(f.Catalogue, [imported]);
        var run = f.Evaluate(SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").Build(), combined);
        // Der Lauf nennt die Baseline (der Platzhalter des Arbeitsablaufs trägt sie noch nicht ein)
        var withBaseline = run with { Baselines = [imported.Baseline] };
        RiegelJson.WriteFile(f.Plant.PathOf(ReportFileNames.RunFile(run.HostName, run.Id)), withBaseline);
        return (withBaseline, imported.Baseline);
    }

    [Fact]
    public void Standard_nur_Metadaten_und_Ergebnisse_nicht_nachpruefbar()
    {
        using var f = new EvidenceFixture();
        var (run, baseline) = EvaluateWithBaseline(f);
        var zip = f.Write();
        var entries = EvidenceFixture.ReadAll(zip);
        Assert.Contains("baselines/kunde.metadata.json", entries.Keys);
        Assert.DoesNotContain("baselines/kunde.json", entries.Keys);
        Assert.DoesNotContain("baselines/kunde.rules.json", entries.Keys);
        Assert.DoesNotContain(EvidenceLayout.BaselineLicenseNote, entries.Keys);
        var metadata = Encoding.UTF8.GetString(entries["baselines/kunde.metadata.json"]);
        Assert.Contains("\"schema\": \"riegel.evidence-baseline/1\"", metadata, StringComparison.Ordinal);
        Assert.Contains(baseline.SourceSha256, metadata, StringComparison.Ordinal);
        Assert.Contains("hklm\\\\software\\\\policies\\\\beispiel\\\\opcua|port", metadata, StringComparison.Ordinal);
        // Titel und Texte der Vorgaben gehören nicht in die Metadaten
        Assert.DoesNotContain("OPC UA nur auf Port 4840", metadata, StringComparison.Ordinal);
        var m = EvidenceFixture.Manifest(zip);
        var b = Assert.Single(m.Baselines);
        Assert.False(b.ContentIncluded);
        Assert.Equal(Id, b.Baseline.Id);

        var result = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.True(result.Status == VerifyStatus.Reproduced, string.Join("\n", result.Findings.Select(x => $"{x.Code} {x.Message}")));
        var imp = run.Results.Where(r => r.RuleSetId == Id).Select(r => "HMI-TEST: " + r.RuleId).ToList();
        Assert.NotEmpty(imp);
        Assert.Equal(imp, result.NotVerifiable);
        Assert.Contains(result.Findings, x => x.Code == RglCodesDataOutputs.BaselineNotVerifiable);
        Assert.Equal(run.Results.Count - imp.Count, result.ResultsCompared);
        Assert.Contains("nicht nachprüfbar", Encoding.UTF8.GetString(entries["README.txt"]), StringComparison.Ordinal);
    }

    [Fact]
    public void Mit_Inhalt_und_Lizenzwarnung_vollstaendig_nachpruefbar()
    {
        using var f = new EvidenceFixture();
        var (run, _) = EvaluateWithBaseline(f);
        var zip = f.Write(includeBaselineContent: true);
        var entries = EvidenceFixture.ReadAll(zip);
        Assert.Contains("baselines/kunde.json", entries.Keys);
        Assert.Contains("baselines/kunde.rules.json", entries.Keys);
        Assert.Contains(EvidenceLayout.BaselineLicenseNote, entries.Keys);
        Assert.Contains("WARNUNG", Encoding.UTF8.GetString(entries["README.txt"]), StringComparison.Ordinal);
        Assert.Contains("WARNING", Encoding.UTF8.GetString(entries[EvidenceLayout.BaselineLicenseNote]), StringComparison.Ordinal);
        var b = Assert.Single(EvidenceFixture.Manifest(zip).Baselines);
        Assert.True(b.ContentIncluded);
        Assert.Equal("baselines/kunde.json", b.File!.Path);

        var result = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.True(result.Status == VerifyStatus.Reproduced, string.Join("\n", result.Findings.Select(x => $"{x.Code} {x.Message}")));
        Assert.Empty(result.NotVerifiable);
        Assert.Equal(run.Results.Count, result.ResultsCompared);
    }
}
