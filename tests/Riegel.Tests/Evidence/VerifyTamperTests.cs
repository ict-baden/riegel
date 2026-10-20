// SPDX-License-Identifier: MIT
using Riegel.Core.Evidence;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Evidence;

/// <summary>
/// Gefälschte Nachweispakete aus der Prüfung vom 2026-10-20: ein Katalog, der zusammen mit den Ergebnissen geändert
/// wurde (alle Prüfsummen passend), und eine geänderte Zusammenfassung in einem Paket mit einer Kunden-Baseline ohne
/// Inhalt. verify muss beide als abweichend melden.
/// </summary>
public sealed class VerifyTamperTests
{
    /// <summary>NET-08 erwartet 0 statt 1 – dieselbe ID und Version wie der „eingebaute“ Testkatalog, anderer Inhalt.</summary>
    private static IReadOnlyList<Rule> ForgedRules() =>
    [
        .. EvidenceFixture.Rules().Select(r => r.Id == "NET-08" && r.Check is RegistryCheck c
            ? r with { Check = c with { Expected = JsonValues.Number(0) } }
            : r),
    ];

    private static string Codes(VerifyResult result) => string.Join("\n", result.Findings.Select(x => $"{x.Code} {x.Message}"));

    [Fact]
    public void Paket_mit_gefaelschtem_Katalog_und_passenden_Ergebnissen_ist_abweichend()
    {
        using var f = new EvidenceFixture();
        var builtIn = f.Catalogue;
        // Fälscher: Katalog geändert, Ergebnisse, Katalog-Prüfsummen in Läufen und Manifest und SHA256SUMS.txt passend dazu
        f.Catalogue = EvidenceFixture.LoadCatalogue(f.Services, ForgedRules());
        Assert.Equal(builtIn.Version, f.Catalogue.Version);
        Assert.NotEqual(builtIn.Sha256, f.Catalogue.Sha256);
        var runs = f.Standard();
        Assert.Contains(runs.SelectMany(r => r.Results), r => r.RuleId == "NET-08");
        var zip = f.Write();

        // In sich stimmig: mit dem gefälschten Katalog als Referenz wird es reproduziert …
        var selfConsistent = new EvidencePack(f.Services, () => f.Catalogue).Verify(zip, ReadLimits.Default);
        Assert.True(selfConsistent.Status == VerifyStatus.Reproduced, Codes(selfConsistent));
        Assert.True(selfConsistent.CatalogueBuiltIn);
        Assert.Equal(0, selfConsistent.ExitCode);

        // … gegen den eingebauten Katalog derselben Version nicht
        var result = new EvidencePack(f.Services, () => builtIn).Verify(zip, ReadLimits.Default);
        Assert.Equal(VerifyStatus.Mismatch, result.Status);
        Assert.Equal(2, result.ExitCode);
        Assert.False(result.CatalogueBuiltIn);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(RglCodesDataOutputs.CatalogueNotBuiltIn, finding.Code);
        Assert.Equal(f.Catalogue.Sha256, result.Catalogue!.Sha256);
        Assert.Contains(Hashes.Short(builtIn.Sha256), finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Katalog_einer_anderen_Version_wird_reproduziert_aber_mit_Hinweis_und_Exitcode_1()
    {
        using var f = new EvidenceFixture();
        var builtIn = f.Catalogue;
        f.Catalogue = EvidenceFixture.LoadCatalogue(f.Services, EvidenceFixture.Rules(), version: "2026.07");
        f.Standard();
        var result = new EvidencePack(f.Services, () => builtIn).Verify(f.Write(), ReadLimits.Default);
        Assert.True(result.Status == VerifyStatus.Reproduced, Codes(result));
        Assert.Equal(1, result.ExitCode);
        Assert.False(result.CatalogueBuiltIn);
        Assert.Equal("2026.07", result.Catalogue!.Version);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(RglCodesDataOutputs.CatalogueNotReference, finding.Code);
        Assert.Contains("2026.07", finding.Message, StringComparison.Ordinal);
        Assert.Contains("2026.10", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eingebauter_Katalog_wird_gemeldet()
    {
        using var f = new EvidenceFixture();
        f.Standard();
        var result = f.Pack.Verify(f.Write(), ReadLimits.Default);
        Assert.True(result.Status == VerifyStatus.Reproduced, Codes(result));
        Assert.True(result.CatalogueBuiltIn);
        Assert.Equal(new CatalogueRef("riegel", "2026.10", new DateOnly(2026, 10, 18), f.Catalogue.Sha256), result.Catalogue);
    }

    [Fact]
    public void Geaenderte_Zusammenfassung_faellt_auch_mit_Baseline_ohne_Inhalt_auf()
    {
        using var f = new EvidenceFixture();
        var (run, _) = EvidenceBaselineTests.EvaluateWithBaseline(f);
        Assert.Contains(run.Results, r => r.RuleSetId == EvidenceBaselineTests.Id);
        var zip = f.Write();
        Assert.True(f.Pack.Verify(zip, ReadLimits.Default).Status == VerifyStatus.Reproduced);

        // Nur die Zusammenfassung schöner machen und alle Prüfsummen neu berechnen
        var path = $"runs/{run.HostName}/{run.Id}.results.json";
        EvidenceFixture.Rewrite(zip, e =>
        {
            var packed = RiegelJson.Deserialize<Run>(e[path]);
            e[path] = RiegelJson.SerializeToUtf8Bytes(packed with
            {
                Summary = packed.Summary with { Score = 97, OpenHigh = 0, OpenMedium = 0, OpenLow = 0 },
            });
        }, rehash: true);
        var result = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.Equal(VerifyStatus.Mismatch, result.Status);
        Assert.Equal(2, result.ExitCode);
        var finding = Assert.Single(result.Findings, x => x.Code == RglCodesDataOutputs.SummaryMismatch);
        Assert.Contains("score: 97", finding.Message, StringComparison.Ordinal);
        Assert.Contains(result.Findings, x => x.Code == RglCodesDataOutputs.BaselineNotVerifiable);
    }

    [Fact]
    public void Geaenderter_Katalog_faellt_auch_mit_Baseline_ohne_Inhalt_auf()
    {
        using var f = new EvidenceFixture();
        EvidenceBaselineTests.EvaluateWithBaseline(f);
        var zip = f.Write();
        // Katalog ändern, seine Prüfsumme im Manifest und alle Dateiprüfsummen passend setzen; die Prüfsumme des
        // verbundenen Katalogs im Lauf lässt sich ohne Baseline-Inhalt nicht nachrechnen
        EvidenceFixture.Rewrite(zip, e =>
        {
            var file = "catalogue/riegel-catalogue-2026.10.json";
            var doc = RiegelJson.Deserialize<CatalogueDocument>(e[file]);
            var forged = EvidenceFixture.LoadCatalogue(f.Services, ForgedRules());
            e[file] = RiegelJson.SerializeToUtf8Bytes(doc with { Rules = [.. forged.Rules] });
            var m = RiegelJson.Deserialize<EvidenceManifest>(e[EvidenceLayout.Manifest]);
            e[EvidenceLayout.Manifest] = RiegelJson.SerializeToUtf8Bytes(m with { Catalogue = m.Catalogue with { Sha256 = forged.Sha256 } });
        }, rehash: true);
        var result = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.Equal(VerifyStatus.Mismatch, result.Status);
        Assert.Contains(result.Findings, x => x.Code == RglCodesDataOutputs.CatalogueNotBuiltIn);
        Assert.DoesNotContain(result.Findings, x => x.Code == RglCodesDataOutputs.CatalogueMismatch);
    }
}
