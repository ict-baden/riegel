// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

namespace Riegel.Session;

/// <summary>
/// Die Dienste, die die Oberfläche benutzt – dieselben Schnittstellen wie <see cref="RiegelServices"/> (spec 7.2).
/// Alle kommen aus <see cref="RiegelServices.CreateDefault"/>, auch für die Demo-Anlage (echte Engine und echter
/// Katalog).
/// </summary>
internal sealed class AppServices
{
    public required ICatalogueProvider Catalogue { get; init; }
    public required ISnapshotReader Snapshots { get; init; }
    public required IBaselineImporter Baselines { get; init; }
    public required IPlantStore Plants { get; init; }
    public required IRegisterService Registers { get; init; }
    public required IRunComparer Comparison { get; init; }
    public required IRunService Runs { get; init; }
    public required IScoring Scoring { get; init; }
    public required IReportDataBuilder ReportData { get; init; }
    public required IPdfWriter Pdf { get; init; }
    public required ITabularWriter Tabular { get; init; }
    public required IEvidencePack Evidence { get; init; }
    public required IRemediationWriter Remediation { get; init; }
    public required IPlatform Platform { get; init; }
    public required TimeProvider Clock { get; init; }

    /// <summary>Heute (UTC) laut Uhr der Dienste.</summary>
    public DateOnly Today => DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);

    /// <summary>Die echten Dienste.</summary>
    public static AppServices From(RiegelServices s) => new()
    {
        Catalogue = s.Catalogue,
        Snapshots = s.Snapshots,
        Baselines = s.Baselines,
        Plants = s.Plants,
        Registers = s.Registers,
        Comparison = s.Comparison,
        Runs = s.Runs,
        Scoring = s.Scoring,
        ReportData = s.ReportData,
        Pdf = s.Pdf,
        Tabular = s.Tabular,
        Evidence = s.Evidence,
        Remediation = s.Remediation,
        Platform = s.Platform,
        Clock = s.Clock,
    };

    /// <summary>Dienste für die Demo-Anlage: die echten, mit der Uhr der Demo.</summary>
    public static AppServices ForDemo(IPlatform platform, TimeProvider clock) => From(RiegelServices.Create(platform, clock));
}
