// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Plants;
using Riegel.Core.Runs;
using Riegel.Core.Services;

namespace Riegel.Core.ReportBuilding;

/// <summary>
/// Was die Berichte über eine Anlage brauchen, einmal gelesen: Katalog mit Baselines, Register, alle Läufe, je Rechner
/// der maßgebliche Lauf bis zum Stichtag (neuester Export nach CollectedAtUtc, davon die neueste Auswertung), Exporte
/// und Import-Nachweise.
/// </summary>
internal sealed class PlantView
{
    private readonly RiegelServices _services;
    private readonly Dictionary<string, Run?> _runCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Snapshot?> _snapshotCache = new(StringComparer.Ordinal);
    private readonly ReadLimits _limits;

    private PlantView(RiegelServices services, Plant plant, ReadLimits limits)
    {
        _services = services;
        Plant = plant;
        _limits = limits;
    }

    public Plant Plant { get; }
    public ICatalogue Catalogue { get; private set; } = null!;
    public IReadOnlyList<BaselineRef> Baselines { get; private set; } = [];
    public RegisterSet Registers { get; private set; } = null!;
    public bool HasRegisterFiles { get; private set; }

    /// <summary>Alle Läufe aller Rechner (Kurzinfo), sortiert nach Rechner, Erfassung, Auswertung.</summary>
    public IReadOnlyList<RunInfo> AllRuns { get; private set; } = [];

    /// <summary>Der maßgebliche Lauf je Rechner bis zum Stichtag, nach Rechnername sortiert.</summary>
    public IReadOnlyList<Run> Runs { get; private set; } = [];

    public IReadOnlyList<ImportRecord> Imports { get; private set; } = [];

    /// <summary>Alle bekannten Rechner: Einträge der Anlage und Rechner mit Läufen, sortiert.</summary>
    public IReadOnlyList<string> Hosts { get; private set; } = [];

    /// <summary>Anlage laden. <paramref name="catalogue"/> null = eingebetteter Katalog plus Baselines der Anlage.</summary>
    public static PlantView Load(RiegelServices services, Plant plant, DateOnly date, IReadOnlyList<string>? hosts, ICatalogue? catalogue = null,
        ReadLimits? limits = null)
    {
        var view = new PlantView(services, plant, limits ?? ReadLimits.Default);
        var (c, baselines) = PlantCatalogue.For(services, plant, catalogue, view._limits);
        view.Catalogue = c;
        view.Baselines = baselines;
        if (plant.Folder is null)
        {
            view.Registers = Empty(plant);
            view.Hosts = [.. plant.Machines.Select(m => m.HostName).Order(StringComparer.OrdinalIgnoreCase)];
            return view;
        }
        view.Registers = services.Registers.Load(plant);
        view.HasRegisterFiles = view.Registers.Hashes != RegisterHashes.Empty;
        view.AllRuns = ListAll(services, plant);
        view.Runs = [.. Latest(view.AllRuns, date, hosts).Select(info => view.Run(info.Id)).OfType<Run>()
            .OrderBy(r => r.HostName, StringComparer.OrdinalIgnoreCase)];
        view.Imports = ReadImports(plant.Folder);
        view.Hosts = [.. plant.Machines.Select(m => m.HostName).Concat(view.AllRuns.Select(r => r.HostName))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        return view;
    }

    public static RegisterSet Empty(Plant plant) => new(new ExceptionFile(SchemaIds.Exceptions, plant.Id, []),
        new TailoringFile(SchemaIds.Tailoring, plant.Id, []), new MeasureFile(SchemaIds.Measures, plant.Id, []), RegisterHashes.Empty);

    /// <summary>Je Rechner der maßgebliche Lauf bis <paramref name="date"/> (Auswertungstag ≤ Stichtag).</summary>
    public static IEnumerable<RunInfo> Latest(IEnumerable<RunInfo> runs, DateOnly date, IReadOnlyList<string>? hosts) =>
        runs.Where(r => r.EvaluationDate <= date)
            .Where(r => hosts is null || hosts.Any(h => string.Equals(h, r.HostName, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(r => r.HostName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(r => r.CollectedAtUtc).ThenBy(r => r.EvaluatedAtUtc).ThenBy(r => r.Id, StringComparer.Ordinal).Last());

    /// <summary>Lauf nach ID (zwischengespeichert), null wenn es ihn nicht gibt.</summary>
    public Run? Run(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId) || Plant.Folder is null) return null;
        if (_runCache.TryGetValue(runId, out var cached)) return cached;
        Run? run;
        try { run = _services.Plants.LoadRun(Plant, runId); }
        catch (RiegelException) { run = null; }
        _runCache[runId] = run;
        return run;
    }

    /// <summary>Export eines Laufs aus dem Anlagenordner, null wenn er fehlt oder nicht lesbar ist.</summary>
    public Snapshot? Snapshot(Run run)
    {
        if (Plant.Folder is not { } folder || string.IsNullOrWhiteSpace(run.Snapshot.Path)) return null;
        if (_snapshotCache.TryGetValue(run.Id, out var cached)) return cached;
        Snapshot? snapshot = null;
        var path = PlantFiles.Full(folder, run.Snapshot.Path);
        if (PlantFiles.Relative(folder, path) is not null && File.Exists(path))
        {
            try { snapshot = _services.Snapshots.Read(path, _limits); }
            catch (RiegelException) { snapshot = null; }
        }
        _snapshotCache[run.Id] = snapshot;
        return snapshot;
    }

    /// <summary>Aktuell freigegebener Stand eines Rechners und dessen Lauf.</summary>
    public (ApprovedState? State, Run? Run) Approved(string host)
    {
        if (Plant.Folder is null) return (null, null);
        ApprovedState? state;
        try { state = _services.Plants.LoadApproved(Plant, host); }
        catch (RiegelException) { state = null; }
        return (state, state is null ? null : Run(state.RunId));
    }

    /// <summary>Import-Nachweis, in dem ein Lauf entstand (oder der seinen Export brachte).</summary>
    public string? ImportOf(Run run) =>
        Imports.FirstOrDefault(i => i.Files.Any(f => f.RunId == run.Id || (run.ReEvaluationOf is not null && f.RunId == run.ReEvaluationOf)))?.Id
        ?? Imports.FirstOrDefault(i => i.Files.Any(f => string.Equals(f.Sha256, run.Snapshot.Sha256, StringComparison.OrdinalIgnoreCase)))?.Id;

    /// <summary>Gruppe eines Rechners laut Anlage.</summary>
    public MachineGroup? GroupOf(string host)
    {
        var entry = Machine(host);
        return entry?.GroupId is { } id ? Plant.Groups.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase)) : null;
    }

    public PlantMachine? Machine(string host) =>
        Plant.Machines.FirstOrDefault(m => string.Equals(m.HostName, host, StringComparison.OrdinalIgnoreCase));

    private static List<RunInfo> ListAll(RiegelServices services, Plant plant)
    {
        var runs = Path.Combine(plant.Folder!, PlantFiles.Runs);
        if (!Directory.Exists(runs)) return [];
        return [.. Directory.EnumerateDirectories(runs).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(host => services.Plants.ListRuns(plant, host))
            .OrderBy(r => r.HostName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.CollectedAtUtc).ThenBy(r => r.EvaluatedAtUtc)
            .ThenBy(r => r.Id, StringComparer.Ordinal)];
    }

    private static List<ImportRecord> ReadImports(string folder)
    {
        var directory = Path.Combine(folder, PlantFiles.Imports);
        if (!Directory.Exists(directory)) return [];
        var list = new List<ImportRecord>();
        foreach (var file in Directory.EnumerateFiles(directory, "*" + PlantFiles.ImportExtension).Order(StringComparer.Ordinal))
        {
            try { list.Add(RiegelJson.ReadFile<ImportRecord>(file, ReadLimits.Default.MaxPlantFileBytes)); }
            catch (RiegelException) { /* unlesbarer Nachweis: im Bericht ohne Import-ID, das Erhebungsprotokoll meldet den Fehler */ }
        }
        return list;
    }
}
