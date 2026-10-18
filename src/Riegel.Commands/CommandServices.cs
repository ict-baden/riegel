// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

namespace Riegel.Commands;

/// <summary>
/// Die Dienste, die die Befehle benutzen – standardmäßig die aus <see cref="RiegelServices"/> (spec 7.2), erst beim
/// ersten Zugriff erzeugt. Tests ersetzen einzelne Dienste über die init-Eigenschaften durch Fälschungen, ohne dass ein
/// Vertrag oder eine Factory geändert werden muss.
/// </summary>
internal sealed class CommandServices
{
    private readonly RiegelServices _inner;
    private ICatalogueProvider? _catalogue;
    private ISnapshotReader? _snapshots;
    private IBaselineImporter? _baselines;
    private IPlantStore? _plants;
    private IRegisterService? _registers;
    private IRunComparer? _comparison;
    private IRunService? _runs;
    private IReportDataBuilder? _reportData;
    private IPdfWriter? _pdf;
    private ITabularWriter? _tabular;
    private IEvidencePack? _evidence;
    private IRemediationWriter? _remediation;
    private IPlatform? _platform;

    public CommandServices(RiegelServices inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public TimeProvider Clock => _inner.Clock;

    /// <summary>Heute (UTC) laut Uhr der Dienste.</summary>
    public DateOnly Today => DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);

    public IPlatform Platform { get => _platform ?? _inner.Platform; init => _platform = value; }
    public ICatalogueProvider Catalogue { get => _catalogue ?? _inner.Catalogue; init => _catalogue = value; }
    public ISnapshotReader Snapshots { get => _snapshots ?? _inner.Snapshots; init => _snapshots = value; }
    public IBaselineImporter Baselines { get => _baselines ?? _inner.Baselines; init => _baselines = value; }
    public IPlantStore Plants { get => _plants ?? _inner.Plants; init => _plants = value; }
    public IRegisterService Registers { get => _registers ?? _inner.Registers; init => _registers = value; }
    public IRunComparer Comparison { get => _comparison ?? _inner.Comparison; init => _comparison = value; }
    public IRunService Runs { get => _runs ?? _inner.Runs; init => _runs = value; }
    public IReportDataBuilder ReportData { get => _reportData ?? _inner.ReportData; init => _reportData = value; }
    public IPdfWriter Pdf { get => _pdf ?? _inner.Pdf; init => _pdf = value; }
    public ITabularWriter Tabular { get => _tabular ?? _inner.Tabular; init => _tabular = value; }
    public IEvidencePack Evidence { get => _evidence ?? _inner.Evidence; init => _evidence = value; }
    public IRemediationWriter Remediation { get => _remediation ?? _inner.Remediation; init => _remediation = value; }
}
