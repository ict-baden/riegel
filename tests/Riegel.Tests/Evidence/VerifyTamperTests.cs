// SPDX-License-Identifier: MIT
using Riegel.Core.Evidence;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Evidence;

/// <summary>
/// Gefälschte Nachweispakete aus der Prüfung vom 2026-10-20 und der unabhängigen Prüfung danach: ein Katalog, der
/// zusammen mit den Ergebnissen geändert wurde (alle Prüfsummen passend), eine geänderte Zusammenfassung in einem Paket
/// mit einer Kunden-Baseline ohne Inhalt, eine angebliche Baseline „riegel“ und eigene Ergebnisse unter einer Baseline.
/// verify darf keines davon als reproduziert melden.
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

    /// <summary>
    /// Unabhängige Prüfung: Ein Katalog mit einer Version, die diese Riegel-Version nicht kennt (neuer oder erfunden), ist
    /// in sich stimmig, aber nicht nachgeprüft – eigener Status, damit Skripte, die nur „status“ lesen, ihn nicht für
    /// „reproduced“ halten.
    /// </summary>
    [Fact]
    public void Katalog_einer_unbekannten_Version_ist_nicht_nachgeprueft_mit_Exitcode_1()
    {
        using var f = new EvidenceFixture();
        var builtIn = f.Catalogue;
        f.Catalogue = EvidenceFixture.LoadCatalogue(f.Services, ForgedRules(), version: "2026.07");
        f.Standard();
        var result = new EvidencePack(f.Services, () => builtIn, NoneReleased).Verify(f.Write(), ReadLimits.Default);
        Assert.True(result.Status == VerifyStatus.CatalogueUnknown, Codes(result));
        Assert.Equal(1, result.ExitCode);
        Assert.False(result.CatalogueBuiltIn);
        Assert.False(result.CatalogueReleased);
        Assert.Equal("2026.07", result.Catalogue!.Version);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(RglCodesDataOutputs.CatalogueNotReference, finding.Code);
        Assert.Contains("2026.07", finding.Message, StringComparison.Ordinal);
        Assert.Contains("2026.10", finding.Message, StringComparison.Ordinal);
    }

    private static string? NoneReleased(string id, string version) => null;

    /// <summary>
    /// Unabhängige Prüfung: Eine neuere Riegel-Version mit geändertem Katalog (neue Version) prüft ein echtes Paket einer
    /// früheren Version gegen deren veröffentlichten SHA-256 – reproduziert, Exitcode 0. Vorher meldete jede Änderung am
    /// Katalog, die nur releasedOn anhob, alle älteren Pakete als gefälscht (RGL-0572).
    /// </summary>
    [Fact]
    public void Paket_eines_frueheren_veroeffentlichten_Katalogs_wird_reproduziert()
    {
        using var f = new EvidenceFixture();
        var earlier = f.Catalogue;
        f.Standard();
        var zip = f.Write();

        // Nur das Datum ändern ändert den Inhalt: dieselbe Version wäre nicht mehr eindeutig (CatalogueLockTests)
        var sameVersion = EvidenceFixture.LoadCatalogue(f.Services, EvidenceFixture.Rules(), releasedOn: new DateOnly(2026, 11, 2));
        Assert.Equal(earlier.Version, sameVersion.Version);
        Assert.NotEqual(earlier.Sha256, sameVersion.Sha256);

        var newer = EvidenceFixture.LoadCatalogue(f.Services, ForgedRules(), version: "2026.11", releasedOn: new DateOnly(2026, 11, 2));
        string? Released(string id, string version) => id == "riegel" && version == "2026.10" ? earlier.Sha256 : null;
        var result = new EvidencePack(f.Services, () => newer, Released).Verify(zip, ReadLimits.Default);
        Assert.True(result.Status == VerifyStatus.Reproduced, Codes(result));
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.CatalogueBuiltIn);
        Assert.True(result.CatalogueReleased);
        Assert.Empty(result.Findings);

        // Ein Katalog, der sich als diese veröffentlichte Version ausgibt, aber andere Regeln hat, ist abweichend
        using var g = new EvidenceFixture();
        g.Catalogue = EvidenceFixture.LoadCatalogue(g.Services, ForgedRules());
        g.Standard();
        var forged = new EvidencePack(g.Services, () => newer, Released).Verify(g.Write(), ReadLimits.Default);
        Assert.Equal(VerifyStatus.Mismatch, forged.Status);
        Assert.Equal(2, forged.ExitCode);
        Assert.False(forged.CatalogueReleased);
        var finding = Assert.Single(forged.Findings);
        Assert.Equal(RglCodesDataOutputs.CatalogueNotBuiltIn, finding.Code);
        Assert.Contains(Hashes.Short(earlier.Sha256), finding.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Unabhängige Prüfung: alle „fail“ zu „pass“, Zusammenfassung geschönt und im Manifest eine Baseline „riegel“ ohne
    /// Inhalt – vorher nahm verify damit alle eigenen Ergebnisse aus dem Vergleich und meldete „reproduziert“.
    /// </summary>
    [Fact]
    public void Angebliche_Baseline_riegel_ohne_Inhalt_macht_das_Manifest_ungueltig()
    {
        using var f = new EvidenceFixture();
        var runs = f.Standard();
        Assert.Contains(runs.SelectMany(r => r.Results), r => r.Status == ResultStatus.Fail);
        var zip = f.Write();
        foreach (var id in new[] { "riegel", "Riegel", "../x", "" })
        {
            var copy = Path.Combine(f.Out.Path, "copy.zip");
            File.Copy(zip, copy, overwrite: true);
            EvidenceFixture.Rewrite(copy, e =>
            {
                Beautify(e);
                var m = RiegelJson.Deserialize<EvidenceManifest>(e[EvidenceLayout.Manifest]);
                e[EvidenceLayout.Manifest] = RiegelJson.SerializeToUtf8Bytes(m with { Baselines = [.. m.Baselines, FakeBaseline(id, f.Clock.Now)] });
            }, rehash: true);
            var result = f.Pack.Verify(copy, ReadLimits.Default);
            Assert.True(result.Status == VerifyStatus.Error, id + ": " + Codes(result));
            Assert.Equal(3, result.ExitCode);
            Assert.Equal(RglCodesDataOutputs.ManifestInvalid, Assert.Single(result.Findings).Code);
        }

        // Dieselbe Fälschung ohne die Baseline fällt bei der erneuten Auswertung auf
        EvidenceFixture.Rewrite(zip, Beautify, rehash: true);
        var plain = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.Equal(VerifyStatus.Mismatch, plain.Status);
        Assert.Contains(plain.Findings, x => x.Code == RglCodesDataOutputs.ResultMismatch);
        Assert.Contains(plain.Findings, x => x.Code == RglCodesDataOutputs.SummaryMismatch);
    }

    /// <summary>
    /// Unabhängige Prüfung, zweite Form: eine gültige Baseline-ID ohne Inhalt, und die eigenen Ergebnisse tragen deren
    /// RuleSetId. Ergebnisse einer Baseline haben Regel-IDs „IMP-&lt;id&gt;-…“ – alles andere ist abweichend.
    /// </summary>
    [Fact]
    public void Eigene_Ergebnisse_als_Ergebnisse_einer_Baseline_ohne_Inhalt_sind_abweichend()
    {
        using var f = new EvidenceFixture();
        f.Standard();
        var zip = f.Write();
        var fake = FakeBaseline("fremd", f.Clock.Now);
        EvidenceFixture.Rewrite(zip, e =>
        {
            foreach (var path in e.Keys.Where(k => k.EndsWith(".results.json", StringComparison.Ordinal)).ToList())
            {
                var run = RiegelJson.Deserialize<Run>(e[path]);
                e[path] = RiegelJson.SerializeToUtf8Bytes(run with
                {
                    Results = [.. run.Results.Select(r => r with { RuleSetId = "fremd", Status = r.Status == ResultStatus.Fail ? ResultStatus.Pass : r.Status })],
                    Baselines = [.. run.Baselines, fake.Baseline],
                });
            }
            var m = RiegelJson.Deserialize<EvidenceManifest>(e[EvidenceLayout.Manifest]);
            e[EvidenceLayout.Manifest] = RiegelJson.SerializeToUtf8Bytes(m with { Baselines = [.. m.Baselines, fake] });
        }, rehash: true);
        var result = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.Equal(VerifyStatus.Mismatch, result.Status);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains(result.Findings, x => x.Code == RglCodesDataOutputs.ResultMismatch && x.Message.Contains("not a rule of this baseline", StringComparison.Ordinal));
    }

    private static EvidenceBaseline FakeBaseline(string id, DateTimeOffset now) =>
        new(new BaselineRef(id, "Riegel", BaselineSourceKind.RiegelJson, new string('0', 64), null, now, 0), false, null);

    /// <summary>Alle „fail“ zu „pass“ und eine Zusammenfassung ohne offene Punkte.</summary>
    private static void Beautify(SortedDictionary<string, byte[]> e)
    {
        foreach (var path in e.Keys.Where(k => k.EndsWith(".results.json", StringComparison.Ordinal)).ToList())
        {
            var run = RiegelJson.Deserialize<Run>(e[path]);
            int fails = run.Results.Count(r => r.Status == ResultStatus.Fail);
            e[path] = RiegelJson.SerializeToUtf8Bytes(run with
            {
                Results = [.. run.Results.Select(r => r.Status == ResultStatus.Fail ? r with { Status = ResultStatus.Pass } : r)],
                Summary = run.Summary with
                {
                    Pass = run.Summary.Pass + fails, Fail = 0, OpenHigh = 0, OpenMedium = 0, OpenLow = 0, Score = 100, Documented = 100,
                },
            });
        }
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
