// SPDX-License-Identifier: MIT
using Riegel.Core.Catalogue;
using Riegel.Core.Model;

namespace Riegel.Tests.Catalogue;

/// <summary>
/// Sperre des Katalogs (unabhängige Prüfung vom 2026-10-20): Eine Katalogversion gehört zu genau einem Inhalt. Wer
/// Regeln, Tabellen oder den Kopf ändert, hebt <c>version</c> in rules/catalogue.json an und trägt den neuen SHA-256 in
/// <see cref="ReleasedCatalogues"/> ein – sonst meldete verify echte Pakete früherer Stände derselben Version als
/// gefälscht (RGL-0572), und ältere Riegel-Versionen könnten neue Pakete nicht einordnen.
/// </summary>
public sealed class CatalogueLockTests
{
    [Fact]
    public void Eingebauter_Katalog_steht_mit_seinem_SHA256_in_der_Liste_der_veroeffentlichten()
    {
        var c = CatalogueProvider.LoadEmbeddedCore();
        Assert.Equal("riegel", c.Id);
        Assert.True(ReleasedCatalogues.Riegel.TryGetValue(c.Version, out var sha),
            $"Katalogversion {c.Version} fehlt in src/Riegel.Core/Catalogue/ReleasedCatalogues.cs: [\"{c.Version}\"] = \"{c.Sha256}\" eintragen.");
        Assert.True(sha == c.Sha256,
            $"Der Katalog {c.Version} wurde geändert (SHA-256 {c.Sha256}, veröffentlicht {sha}). Eine veröffentlichte Version nie ändern: " +
            "in rules/catalogue.json eine neue version (JJJJ.MM oder JJJJ.MM.N) und releasedOn setzen und die neue Zeile in " +
            "ReleasedCatalogues.cs eintragen (docs/development.md, Releases).");
        Assert.Equal(c.Sha256, ReleasedCatalogues.Sha256("riegel", c.Version));
    }

    [Fact]
    public void Liste_hat_gueltige_Versionen_und_Pruefsummen()
    {
        Assert.NotEmpty(ReleasedCatalogues.Riegel);
        foreach (var (version, sha) in ReleasedCatalogues.Riegel)
        {
            Assert.Matches(ReleasedCatalogues.VersionPattern(), version);
            Assert.True(Hashes.IsSha256(sha) && sha == sha.ToLowerInvariant(), version + ": " + sha);
        }
        Assert.Equal(ReleasedCatalogues.Riegel.Count, ReleasedCatalogues.Riegel.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Matches(ReleasedCatalogues.VersionPattern(), CatalogueProvider.LoadEmbeddedCore().Version);
        Assert.Null(ReleasedCatalogues.Sha256("kunde", "2026.10"));
        Assert.Null(ReleasedCatalogues.Sha256("riegel", "1999.01"));
    }
}
