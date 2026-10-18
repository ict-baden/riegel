// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

namespace Riegel.Core.Baselines;

/// <summary>
/// Factory des Baseline-Imports (Paket parsing; Signatur fest): <see cref="BaselineImporter"/> für GPO-Sicherungen,
/// <c>riegel.baseline/1</c> (JSON) und die CSV-Vorlage (spec 2.15). Die Importzeit kommt von der Uhr der Dienste.
/// </summary>
public static class BaselinesFactory
{
    public static IBaselineImporter Create(RiegelServices services) => new BaselineImporter(services.Clock);
}
