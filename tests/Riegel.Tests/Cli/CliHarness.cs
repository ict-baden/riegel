// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Commands;
using Riegel.Core.About;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Cli;

/// <summary>Ergebnis eines Aufrufs: Exitcode, stdout, stderr.</summary>
internal sealed record CliResult(int Exit, string Out, string Err)
{
    public JsonElement Json() => JsonDocument.Parse(Out).RootElement.Clone();

    public override string ToString() => $"exit {Exit}\n--- out ---\n{Out}\n--- err ---\n{Err}";
}

/// <summary>Ruft die Befehle im Prozess auf (CommandApp), mit Stubs aus RiegelServices und austauschbaren Fälschungen.</summary>
internal static class Cli
{
    /// <summary>Dienste mit fester Uhr (2026-10-18T09:00:00Z) und ohne Erfassung.</summary>
    public static CommandServices Services(IPlatform? platform = null, TimeProvider? clock = null) =>
        new(RiegelServices.Create(platform ?? new NullPlatform(), clock ?? new FixedClock()));

    /// <summary>Stub-Dienste mit fester Uhr; für Fälschungen, die den Stub umhüllen.</summary>
    public static RiegelServices Inner(IPlatform? platform = null) => RiegelServices.Create(platform ?? new NullPlatform(), new FixedClock());

    /// <summary>Dienste mit einzelnen Fälschungen, alles andere Stubs.</summary>
    public static CommandServices With(IRunService? runs = null, IPlatform? platform = null, IEvidencePack? evidence = null,
        ICatalogueProvider? catalogue = null)
    {
        var inner = Inner(platform);
        return new CommandServices(inner)
        {
            Runs = runs ?? inner.Runs,
            Evidence = evidence ?? inner.Evidence,
            Catalogue = catalogue ?? inner.Catalogue,
        };
    }

    public static CliResult Run(CommandServices services, params string[] args)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        int exit = CommandApp.Run(services, args, output, error, CancellationToken.None);
        return new CliResult(exit, output.ToString(), error.ToString());
    }

    public static CliResult Run(params string[] args) => Run(Services(), args);
}

/// <summary>Stellt Oberflächen- und Formatkultur für die Dauer eines Tests um.</summary>
internal sealed class UiCulture : IDisposable
{
    private readonly CultureInfo _ui = CultureInfo.CurrentUICulture;
    private readonly CultureInfo _format = CultureInfo.CurrentCulture;

    public UiCulture(string language)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language == "en" ? "en-US" : language);
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _ui;
        CultureInfo.CurrentCulture = _format;
    }
}

/// <summary>Gebaute Läufe und Ergebnisse für die Tests der Tokens und der Icinga-Zeile.</summary>
internal static class TestRuns
{
    public static readonly DateTimeOffset Collected = new(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);

    public static RuleResult Result(string ruleId, ResultStatus status, Severity severity = Severity.Medium, ResultFlags flags = ResultFlags.None) =>
        new("riegel", ruleId, status, severity, null, null, ValueSource.Unknown, null, [], null, null, null, flags, null, null);

    /// <summary>Lauf mit einer aus den Ergebnissen gezählten Zusammenfassung; <paramref name="summary"/> ändert sie.</summary>
    public static Run Make(string host, IReadOnlyList<RuleResult> results, Func<RunSummary, RunSummary>? summary = null,
        RunFlags flags = RunFlags.None, string? previousRunId = null, DateTimeOffset? collected = null, int? score = 80)
    {
        int Count(ResultStatus s) => results.Count(r => r.Status == s);
        int Open(Severity v) => results.Count(r => r.Status == ResultStatus.Fail && r.Severity == v);
        var s = new RunSummary(Count(ResultStatus.Pass), Count(ResultStatus.Fail), Count(ResultStatus.Exception), Count(ResultStatus.OutOfScope),
            Count(ResultStatus.NotApplicable), Count(ResultStatus.NotCheckable), Count(ResultStatus.Info), Open(Severity.High), Open(Severity.Medium),
            Open(Severity.Low), 0, 0, 0, 0, 0, 0, score, score, 95);
        if (summary is not null) s = summary(s);
        var when = collected ?? Collected;
        var snapshotSha = Hashes.Sha256Hex(host + when.ToString("O", CultureInfo.InvariantCulture));
        var evaluated = FixedClock.Default;
        var id = RunIds.Create(when, snapshotSha, "00", RegisterHashes.Empty);
        return new Run(SchemaIds.Run, id, host, evaluated, DateOnly.FromDateTime(evaluated.UtcDateTime), when, null,
            new FileRef($"snapshots/{host}/missing.snapshot.json", snapshotSha), AppInfo.Tool(),
            new CatalogueRef("riegel", "2026.10", new DateOnly(2026, 10, 1), "00"), [], RegisterHashes.Empty, true,
            new CollectionOptions(false, false, false), results, s, [], new SortedDictionary<string, JsonElement>(StringComparer.Ordinal),
            previousRunId, null, null, flags);
    }

    /// <summary>Lauf in den Anlagenordner legen (runs/&lt;host&gt;/&lt;id&gt;.results.json), wie der Anlagenspeicher ihn liest.</summary>
    public static Run Store(TempPlant plant, Run run)
    {
        RiegelJson.WriteFile(plant.PathOf(ReportFileNames.RunFile(run.HostName, run.Id)), run);
        return run;
    }
}

/// <summary>Auswertung, die vorbereitete Läufe zurückgibt (statt der Stub-Auswertung, die alles „nicht prüfbar“ meldet).</summary>
internal sealed class FakeRunService : IRunService
{
    private readonly Queue<Run> _runs = new();

    public FakeRunService(params Run[] runs)
    {
        foreach (var run in runs) _runs.Enqueue(run);
    }

    public List<(string Path, RunOptions Options)> Stored { get; } = [];

    public Run EvaluateAndStore(Plant p, string snapshotPath, RunOptions o)
    {
        Stored.Add((snapshotPath, o));
        return _runs.Dequeue();
    }

    public Run EvaluateOnly(Snapshot s, RunOptions o) => _runs.Dequeue();
}

/// <summary>Plattform, die „erfassen“ kann: liefert einen vorbereiteten Export und merkt sich die Aufrufe.</summary>
internal sealed class FakePlatform(Snapshot snapshot, string? handoffRoot = null) : IPlatform
{
    public bool CanCollect => true;

    public bool IsElevated => snapshot.IsAdmin;

    public List<CollectOptions> Collected { get; } = [];

    public List<string> Handoffs { get; } = [];

    public Snapshot CollectThisMachine(CollectOptions o, CancellationToken ct)
    {
        Collected.Add(o);
        return snapshot;
    }

    public string CreateHandoffDirectory(string token)
    {
        Handoffs.Add(token);
        var dir = Path.Combine(handoffRoot ?? throw new InvalidOperationException("kein Übergabeordner"), token);
        if (Directory.Exists(dir)) throw RiegelException.Common(RglCodesCommon.AlreadyExists, dir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public HandoffResult RunElevatedCollect(string token, CancellationToken ct) => throw new NotSupportedException();

    public void OpenWithShell(string path) => throw new NotSupportedException();

    public string ExportCollectorBundle(string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(targetDir, ReportFileNames.CollectorHandover), "test\n");
        return targetDir;
    }
}

/// <summary>Nachweispaket mit festem Ergebnis für verify und compare-snapshot.</summary>
internal sealed class FakeEvidence(VerifyResult? verify = null, SampleResult? sample = null) : IEvidencePack
{
    public string Write(EvidenceRequest r) => throw new NotSupportedException();

    public VerifyResult Verify(string zipPath, ReadLimits l) => verify ?? throw new NotSupportedException();

    public SampleResult CompareSample(string freshSnapshotPath, string zipPath, string? host, ReadLimits l) => sample ?? throw new NotSupportedException();
}

/// <summary>Registerdienst des Stubs, aber mit vorgegebenen Prüfbefunden.</summary>
internal sealed class FakeValidation(IRegisterService inner, params ValidationIssue[] issues) : IRegisterService
{
    public RegisterSet Load(Plant p) => inner.Load(p);
    public void Save(Plant p, RegisterSet r, RegisterHashes expected) => inner.Save(p, r, expected);
    public IReadOnlyList<RuleResult> Apply(IReadOnlyList<RuleResult> raw, RegisterSet r, Plant p, string host, DateOnly today) => raw;
    public IReadOnlyList<ValidationIssue> Validate(RegisterSet r, ICatalogue c, Plant p, DateOnly today) => issues;
    public RegisterSet Approve(RegisterSet r, RegisterKind kind, IReadOnlyList<string> ids, Approval a) => inner.Approve(r, kind, ids, a);
    public RegisterSet ImportTemplate(RegisterSet r, string templatePath, ReadLimits l) => inner.ImportTemplate(r, templatePath, l);
}

/// <summary>Katalog des Stubs plus Testregeln (der eingebettete Katalog des Gerüsts hat noch keine Regeln).</summary>
internal sealed class CatalogueWith(ICatalogueProvider inner, params Rule[] rules) : ICatalogueProvider
{
    // Nur die Testregeln, mit den Tabellen des echten Katalogs (seit der Integration enthält der eingebettete Katalog
    // die 153 Regeln, und Combine nimmt nur Kunden-Baselines mit IMP-Kennungen an)
    public ICatalogue LoadEmbedded()
    {
        var core = inner.LoadEmbedded();
        return new Riegel.Tests.Plants.TestCatalogue(rules, core.ReleasedOn, core.Builds) { Lifecycle = core.Lifecycle };
    }

    public ICatalogue Load(Stream catalogueJson, ReadLimits l) => inner.Load(catalogueJson, l);

    public ICatalogue Combine(ICatalogue core, IEnumerable<ImportedBaseline> b) => inner.Combine(core, b);
}
