// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

namespace Riegel.Core.Comparison;

/// <summary>
/// Factory des Vergleichs (Paket workflow; Signatur fest): Statusänderungen, Rohwert-Abweichungen ohne flüchtige
/// Liste und Stichprobe des Prüfers nach spec 2.14 (<see cref="RunComparer"/>).
/// </summary>
public static class ComparisonFactory
{
    public static IRunComparer Create(RiegelServices services) => new RunComparer();
}
