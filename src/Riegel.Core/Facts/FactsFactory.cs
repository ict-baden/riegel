// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

namespace Riegel.Core.Facts;

/// <summary>
/// Factory der Fakten (Paket engine; Signatur fest): alle Fakten aus spec 2.5, unbekannt = JSON null.
/// </summary>
public static class FactsFactory
{
    public static IFactsDeriver Create(RiegelServices services) => new FactsDeriver();
}
