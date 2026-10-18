// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

namespace Riegel.Session;

/// <summary>
/// Die Dienste, die die Oberfläche benutzt – dieselben Schnittstellen wie <see cref="RiegelServices"/> (spec 7.2).
/// Normal kommen alle aus <see cref="RiegelServices.CreateDefault"/>. Nur für die Entwicklung kann die Demo-Anlage
/// über <c>RIEGEL_FAKE_SERVICES=1</c> mit nachgebildeten Diensten laufen (<see cref="Dev.FakeServices"/>), solange die
/// Engine ein Platzhalter ist – die Seiten und ihr Code bleiben dabei dieselben.
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

    /// <summary>Nachgebildete Dienste aktiv (nur Demo-Anlage, nur mit RIEGEL_FAKE_SERVICES=1).</summary>
    public bool IsFake { get; init; }

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

    /// <summary>Umgebungsvariable für die nachgebildeten Dienste der Demo (nur Entwicklung, nie in der Oberfläche genannt).</summary>
    public const string FakeServicesVariable = "RIEGEL_FAKE_SERVICES";

    /// <summary>Sind die nachgebildeten Dienste für die Demo verlangt?</summary>
    public static bool FakeRequested(Func<string, string?>? env = null) =>
        (env ?? Environment.GetEnvironmentVariable)(FakeServicesVariable) is "1";

    /// <summary>
    /// Dienste für die Demo-Anlage: echte, oder mit <c>RIEGEL_FAKE_SERVICES=1</c> die nachgebildeten. Für Anlagen der
    /// Benutzer gilt die Variable nie (<see cref="From"/>).
    /// </summary>
    public static AppServices ForDemo(IPlatform platform, TimeProvider clock, bool fake)
    {
        var real = RiegelServices.Create(platform, clock);
        return fake ? Dev.FakeServices.Create(real) : From(real);
    }
}
