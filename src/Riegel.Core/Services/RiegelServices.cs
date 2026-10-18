// SPDX-License-Identifier: MIT
using Riegel.Core.Attribution;
using Riegel.Core.Baselines;
using Riegel.Core.Catalogue;
using Riegel.Core.Comparison;
using Riegel.Core.Evaluation;
using Riegel.Core.Evidence;
using Riegel.Core.Facts;
using Riegel.Core.Model;
using Riegel.Core.Plants;
using Riegel.Core.Registers;
using Riegel.Core.Remediation;
using Riegel.Core.ReportBuilding;
using Riegel.Core.Reports.Pdf;
using Riegel.Core.Reports.Tabular;
using Riegel.Core.Runs;
using Riegel.Core.Scoring;
using Riegel.Core.Snapshots;

namespace Riegel.Core.Services;

/// <summary>
/// Alle Dienste von Riegel an einer Stelle (spec 7.2). Jeder Dienst entsteht beim ersten Zugriff über die Factory
/// seines Ordners (<c>CatalogueFactory</c>, <c>EvaluationFactory</c> …); die Factory bekommt diese Sammlung, damit
/// ein Dienst andere benutzen kann, ohne dass ein Paket eine gemeinsame Datei ändert.
/// </summary>
public sealed class RiegelServices
{
    private readonly Lazy<ICatalogueProvider> _catalogue;
    private readonly Lazy<IFactsDeriver> _facts;
    private readonly Lazy<IEvaluator> _evaluator;
    private readonly Lazy<ISnapshotReader> _snapshots;
    private readonly Lazy<IValueSourceAttributor> _attribution;
    private readonly Lazy<IBaselineImporter> _baselines;
    private readonly Lazy<IPlantStore> _plants;
    private readonly Lazy<IRegisterService> _registers;
    private readonly Lazy<IRunComparer> _comparison;
    private readonly Lazy<IScoring> _scoring;
    private readonly Lazy<IRunService> _runs;
    private readonly Lazy<IReportDataBuilder> _reportData;
    private readonly Lazy<IPdfWriter> _pdf;
    private readonly Lazy<ITabularWriter> _tabular;
    private readonly Lazy<IEvidencePack> _evidence;
    private readonly Lazy<IRemediationWriter> _remediation;

    private RiegelServices(IPlatform platform, TimeProvider clock)
    {
        Platform = platform;
        Clock = clock;
        _catalogue = new(() => CatalogueFactory.Create(this));
        _facts = new(() => FactsFactory.Create(this));
        _evaluator = new(() => EvaluationFactory.Create(this));
        _snapshots = new(() => SnapshotsFactory.Create(this));
        _attribution = new(() => AttributionFactory.Create(this));
        _baselines = new(() => BaselinesFactory.Create(this));
        _plants = new(() => PlantsFactory.Create(this));
        _registers = new(() => RegistersFactory.Create(this));
        _comparison = new(() => ComparisonFactory.Create(this));
        _scoring = new(() => ScoringFactory.Create(this));
        _runs = new(() => RunsFactory.Create(this));
        _reportData = new(() => ReportDataFactory.Create(this));
        _pdf = new(() => PdfFactory.Create(this));
        _tabular = new(() => TabularFactory.Create(this));
        _evidence = new(() => EvidenceFactory.Create(this));
        _remediation = new(() => RemediationFactory.Create(this));
    }

    /// <summary>Alle Dienste mit der Systemuhr.</summary>
    /// <param name="platform">Windows: aus <c>Riegel.Platform.Windows.PlatformFactory</c>; Linux und Tests: <see cref="NullPlatform"/>.</param>
    public static RiegelServices CreateDefault(IPlatform platform) => Create(platform, TimeProvider.System);

    /// <summary>Alle Dienste mit einer eigenen Uhr (Tests: feste Zeit, Determinismus).</summary>
    public static RiegelServices Create(IPlatform platform, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(clock);
        return new RiegelServices(platform, clock);
    }

    public IPlatform Platform { get; }

    /// <summary>Uhr für Auswertungszeit und „heute“ (UTC).</summary>
    public TimeProvider Clock { get; }

    /// <summary>Heutiges Datum (UTC) laut <see cref="Clock"/>.</summary>
    public DateOnly Today => DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);

    public ICatalogueProvider Catalogue => _catalogue.Value;
    public IFactsDeriver Facts => _facts.Value;
    public IEvaluator Evaluator => _evaluator.Value;
    public ISnapshotReader Snapshots => _snapshots.Value;
    public IValueSourceAttributor Attribution => _attribution.Value;
    public IBaselineImporter Baselines => _baselines.Value;
    public IPlantStore Plants => _plants.Value;
    public IRegisterService Registers => _registers.Value;
    public IRunComparer Comparison => _comparison.Value;
    public IScoring Scoring => _scoring.Value;
    public IRunService Runs => _runs.Value;
    public IReportDataBuilder ReportData => _reportData.Value;
    public IPdfWriter Pdf => _pdf.Value;
    public ITabularWriter Tabular => _tabular.Value;
    public IEvidencePack Evidence => _evidence.Value;
    public IRemediationWriter Remediation => _remediation.Value;
}

/// <summary>Plattform ohne Erfassung (Linux, Tests): <see cref="CanCollect"/> ist false, alles Windows-Eigene schlägt mit RGL-0012 fehl.</summary>
public sealed class NullPlatform : IPlatform
{
    public bool CanCollect => false;

    public bool IsElevated => false;

    public Snapshot CollectThisMachine(CollectOptions o, CancellationToken ct) => throw NotSupported();

    public string CreateHandoffDirectory(string token) => throw NotSupported();

    public HandoffResult RunElevatedCollect(string token, CancellationToken ct) => throw NotSupported();

    public void OpenWithShell(string path) => throw NotSupported();

    public string ExportCollectorBundle(string targetDir) => throw NotSupported();

    private static RiegelException NotSupported() => RiegelException.Common(RglCodesCommon.PlatformNotSupported);
}
