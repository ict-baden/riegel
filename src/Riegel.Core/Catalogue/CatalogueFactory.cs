// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

namespace Riegel.Core.Catalogue;

/// <summary>
/// Factory des Katalogs (Paket engine; Signatur fest): lädt rules/** aus Riegel.Core oder einen Katalog aus einem
/// Nachweispaket, prüft ihn inhaltlich (RGL-0200 bis RGL-0212) und verbindet ihn mit Kunden-Baselines.
/// </summary>
public static class CatalogueFactory
{
    public static ICatalogueProvider Create(RiegelServices services) => new CatalogueProvider();
}
