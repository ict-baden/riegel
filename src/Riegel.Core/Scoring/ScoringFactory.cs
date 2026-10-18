// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

namespace Riegel.Core.Scoring;

/// <summary>
/// Factory der Wertung (Paket workflow; Signatur fest): Zählungen, Härtungsgrad, Dokumentiert, Deckel der Kategorie
/// audit, Kategoriewerte und Prüfumfang nach spec 2.6 (<see cref="ScoreCalculator"/>).
/// </summary>
public static class ScoringFactory
{
    public static IScoring Create(RiegelServices services) => new ScoreCalculator();
}
