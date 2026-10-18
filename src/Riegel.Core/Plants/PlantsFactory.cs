// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

namespace Riegel.Core.Plants;

/// <summary>
/// Factory des Anlagenspeichers (Paket workflow; Signatur fest): Anlagenordner nach spec 7.6 mit Sperrdatei,
/// optimistischer Nebenläufigkeit, Verlauf, Import-Nachweisen und Prüfermodus (<see cref="PlantStore"/>).
/// </summary>
public static class PlantsFactory
{
    public static IPlantStore Create(RiegelServices services) => new PlantStore(services);
}
