// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Model.Reports;

namespace Riegel.Session;

/// <summary>Ein Rechner der geöffneten Anlage mit seinen Läufen.</summary>
/// <param name="Runs">Gespeicherte Läufe, älteste zuerst (nach Erfassung, dann Auswertung).</param>
/// <param name="Latest">Neuester Lauf (neueste Auswertung des neuesten Exports).</param>
/// <param name="Previous">Neuester Lauf eines anderen, älteren Exports (Vergleich, Trend).</param>
internal sealed record MachineState(string HostName, PlantMachine? Entry, MachineGroup? Group, IReadOnlyList<RunInfo> Runs,
    Run? Latest, Run? Previous, ApprovedState? Approved)
{
    public string? GroupId => Entry?.GroupId;
}

/// <summary>Zustand eines Registereintrags für die Seiten (spec 2.11–2.13, Berichtsbegriffe aus <see cref="RegisterEntryState"/>).</summary>
internal sealed record EntryInfo(RegisterEntryState State, RegisterFlags Flags, IReadOnlyList<string> Hosts, IReadOnlyList<ValidationIssue> Issues);

/// <summary>
/// Eine geöffnete Anlage mit allem, was die Seiten zeigen: Anlage, Katalog, Register samt Prüfung, Rechner mit Läufen,
/// Importnachweise und Baselines. Wird nach jeder Änderung neu geladen (die Daten sind klein, der Ordner bleibt die
/// Wahrheit). Liest nur; geschrieben wird über <see cref="AppSession"/>.
/// </summary>
internal sealed class PlantContext
{
    private PlantContext(AppServices services, Plant plant, ICatalogue catalogue, RegisterSet registers,
        IReadOnlyList<ValidationIssue> issues, IReadOnlyList<MachineState> machines, IReadOnlyList<ImportRecord> imports,
        IReadOnlyList<BaselineFile> baselines, bool isDemo)
    {
        Services = services;
        Plant = plant;
        Catalogue = catalogue;
        Registers = registers;
        Issues = issues;
        Machines = machines;
        Imports = imports;
        Baselines = baselines;
        IsDemo = isDemo;
        Today = services.Today;
    }

    public AppServices Services { get; }
    public Plant Plant { get; }
    public ICatalogue Catalogue { get; }
    public RegisterSet Registers { get; }
    public IReadOnlyList<ValidationIssue> Issues { get; }
    public IReadOnlyList<MachineState> Machines { get; }
    public IReadOnlyList<ImportRecord> Imports { get; }
    public IReadOnlyList<BaselineFile> Baselines { get; }
    public bool IsDemo { get; }
    public DateOnly Today { get; }

    public string Folder => Plant.Folder ?? "";
    public bool ReadOnly => Plant.ReadOnly;

    /// <summary>Anlage laden (Hintergrund-Thread).</summary>
    public static PlantContext Load(AppServices services, Plant plant, bool isDemo)
    {
        var catalogue = services.Catalogue.LoadEmbedded();
        var registers = services.Registers.Load(plant);
        var issues = services.Registers.Validate(registers, catalogue, plant, services.Today);

        var hosts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in plant.Machines) hosts.Add(m.HostName);
        var runsFolder = Path.Combine(plant.Folder!, "runs");
        if (Directory.Exists(runsFolder))
            foreach (var dir in Directory.EnumerateDirectories(runsFolder)) hosts.Add(Path.GetFileName(dir));

        var machines = new List<MachineState>();
        foreach (var host in hosts)
        {
            var entry = plant.Machines.FirstOrDefault(m => string.Equals(m.HostName, host, StringComparison.OrdinalIgnoreCase));
            var group = plant.Groups.FirstOrDefault(g => g.Id == entry?.GroupId);
            var runs = services.Plants.ListRuns(plant, host);
            Run? latest = runs.Count > 0 ? services.Plants.LoadRun(plant, runs[^1].Id) : null;
            Run? previous = null;
            if (latest is not null)
            {
                var prev = runs.Where(r => r.SnapshotSha256 != latest.Snapshot.Sha256 && r.CollectedAtUtc < latest.CollectedAtUtc).LastOrDefault();
                if (prev is not null) previous = services.Plants.LoadRun(plant, prev.Id);
            }
            machines.Add(new MachineState(entry?.HostName ?? latest?.HostName ?? host, entry, group, runs, latest, previous, services.Plants.LoadApproved(plant, host)));
        }
        // Reihenfolge wie in der Anlage (Gruppen, dann Rechner), Unbekannte am Ende
        var order = plant.Machines.Select((m, i) => (m.HostName, i)).ToDictionary(x => x.HostName, x => x.i, StringComparer.OrdinalIgnoreCase);
        machines = [.. machines.OrderBy(m => order.TryGetValue(m.HostName, out var i) ? i : int.MaxValue).ThenBy(m => m.HostName, StringComparer.OrdinalIgnoreCase)];

        return new PlantContext(services, plant, catalogue, registers, issues, machines, LoadImports(plant), LoadBaselines(plant), isDemo);
    }

    private static List<ImportRecord> LoadImports(Plant plant)
    {
        var folder = Path.Combine(plant.Folder!, "imports");
        var list = new List<ImportRecord>();
        if (!Directory.Exists(folder)) return list;
        foreach (var file in Directory.EnumerateFiles(folder, "*.import.json").Order(StringComparer.Ordinal))
        {
            try { list.Add(RiegelJson.ReadFile<ImportRecord>(file, ReadLimits.Default.MaxPlantFileBytes)); }
            catch (RiegelException) { }
        }
        return list;
    }

    private static List<BaselineFile> LoadBaselines(Plant plant)
    {
        var folder = Path.Combine(plant.Folder!, "baselines");
        var list = new List<BaselineFile>();
        if (!Directory.Exists(folder)) return list;
        foreach (var file in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            try { list.Add(RiegelJson.ReadFile<BaselineFile>(file, ReadLimits.Default.MaxPlantFileBytes)); }
            catch (RiegelException) { }
        }
        return list;
    }

    // ---------------------------------------------------------------- Nachschlagen

    public MachineState? Machine(string? host) =>
        host is null ? null : Machines.FirstOrDefault(m => string.Equals(m.HostName, host, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<string> Hosts => Machines.Select(m => m.HostName);

    public Rule? Rule(string ruleId) => Catalogue.Find(ruleId);

    /// <summary>Titel einer Regel in der Sprache der Oberfläche; importierte Regeln aus ihrer Baseline, sonst die ID.</summary>
    public string Title(string ruleId, string language)
    {
        if (Catalogue.Find(ruleId) is { } rule) return rule.Title.Get(language);
        foreach (var b in Baselines)
            foreach (var r in b.Rules)
                if (r.Id is { } id && (string.Equals(id, ruleId, StringComparison.OrdinalIgnoreCase) || ruleId.EndsWith("-" + id, StringComparison.OrdinalIgnoreCase)))
                    return r.Title;
        return ruleId;
    }

    public string Category(string ruleId) => Catalogue.Find(ruleId)?.Category ?? "system";

    /// <summary>Snapshot eines Laufs lesen (Pfad relativ zum Anlagenordner).</summary>
    public Snapshot? ReadSnapshot(Run run)
    {
        if (string.IsNullOrEmpty(run.Snapshot.Path)) return null;
        var path = Path.Combine(Folder, run.Snapshot.Path.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? Services.Snapshots.Read(path, ReadLimits.Default) : null;
    }

    // ---------------------------------------------------------------- Register

    private IEnumerable<(string Host, RuleResult Result)> LatestResults(string ruleId)
    {
        foreach (var m in Machines)
            if (m.Latest?.Results.FirstOrDefault(r => string.Equals(r.RuleId, ruleId, StringComparison.OrdinalIgnoreCase)) is { } r)
                yield return (m.HostName, r);
    }

    private IReadOnlyList<ValidationIssue> IssuesOf(RegisterKind kind, string id) =>
        [.. Issues.Where(i => i.Register == kind && string.Equals(i.EntryId, id, StringComparison.OrdinalIgnoreCase))];

    public EntryInfo Info(ExceptionEntry e)
    {
        var issues = IssuesOf(RegisterKind.Exceptions, e.Id);
        var state = issues.Any(i => i.Severity == IssueSeverity.Error) ? RegisterEntryState.Invalid
            : RegisterRules.StateOf(e.Approval, ContentHashes.Of(e), Today, Plant.Settings.WarnDays);
        var hosts = RegisterRules.Hosts(e.Scope, Plant, Hosts);
        var flags = RegisterFlags.None;
        var results = LatestResults(e.RuleId).Where(x => hosts.Contains(x.Host, StringComparer.OrdinalIgnoreCase)).ToList();
        if (results.Any(x => x.Result.ExceptionId == e.Id && (x.Result.Flags & ResultFlags.ExceptionValueMismatch) != 0)) flags |= RegisterFlags.ValueMismatch;
        if (results.Count > 0 && results.All(x => x.Result.Status == ResultStatus.Pass)) flags |= RegisterFlags.Unused;
        if (Catalogue.Find(e.RuleId)?.State == RuleState.Retired) flags |= RegisterFlags.RuleRetired;
        if (Registers.Exceptions.Exceptions.Any(o => o.Id != e.Id && string.Equals(o.RuleId, e.RuleId, StringComparison.OrdinalIgnoreCase)
                                                     && RegisterRules.Hosts(o.Scope, Plant, Hosts).Intersect(hosts, StringComparer.OrdinalIgnoreCase).Any()))
            flags |= RegisterFlags.Overlap;
        return new EntryInfo(state, flags, hosts, issues);
    }

    public EntryInfo Info(TailoringEntry t)
    {
        var issues = IssuesOf(RegisterKind.Tailoring, t.Id);
        var state = issues.Any(i => i.Severity == IssueSeverity.Error) ? RegisterEntryState.Invalid
            : RegisterRules.StateOf(t.Approval, ContentHashes.Of(t), Today, Plant.Settings.WarnDays);
        var hosts = RegisterRules.Hosts(t.Scope, Plant, Hosts);
        var flags = Catalogue.Find(t.RuleId)?.State == RuleState.Retired ? RegisterFlags.RuleRetired : RegisterFlags.None;
        if (Registers.Tailoring.Entries.Any(o => o.Id != t.Id && string.Equals(o.RuleId, t.RuleId, StringComparison.OrdinalIgnoreCase)
                                                 && RegisterRules.Hosts(o.Scope, Plant, Hosts).Intersect(hosts, StringComparer.OrdinalIgnoreCase).Any()))
            flags |= RegisterFlags.Overlap;
        return new EntryInfo(state, flags, hosts, issues);
    }

    /// <summary>Maßnahme: überfällig, wenn geplant, Termin vorbei und die Regel auf einem Rechner weiter nicht erfüllt ist.</summary>
    public (bool Overdue, RegisterFlags Flags, IReadOnlyList<string> Hosts, int OpenCount, IReadOnlyList<ValidationIssue> Issues) Info(MeasureEntry m)
    {
        var hosts = RegisterRules.Hosts(m.Scope, Plant, Hosts);
        int open = LatestResults(m.RuleId).Count(x => hosts.Contains(x.Host, StringComparer.OrdinalIgnoreCase) && x.Result.Status == ResultStatus.Fail);
        bool overdue = m.State == MeasureState.Planned && m.DueDate < Today && open > 0;
        var flags = m.State == MeasureState.Done && open > 0 ? RegisterFlags.MeasureDoneButFailing : RegisterFlags.None;
        return (overdue, flags, hosts, open, IssuesOf(RegisterKind.Measures, m.Id));
    }

    private (RunSummary, IReadOnlyList<CategoryScore>)? _score;

    /// <summary>Werte der Anlage über die neuesten Läufe aller Rechner (ohne Domänencontroller, spec 2.6).</summary>
    public (RunSummary Summary, IReadOnlyList<CategoryScore> Categories) PlantScore() => _score ??= Services.Scoring.Score(
        [.. Machines.Where(m => m.Latest is { } r && (r.Flags & RunFlags.DomainController) == 0).SelectMany(m => m.Latest!.Results)], Catalogue, null, false);

    /// <summary>Zählungen zuerst (spec 2.6): offene Befunde je Schwere und die Register-Hinweise über die Anlage.</summary>
    public (int High, int Medium, int Low, int Regressions, int Expiring, int Pending, int Overdue, int NotCheckable) Counts()
    {
        int high = 0, medium = 0, low = 0, regressions = 0, notCheckable = 0;
        foreach (var m in Machines)
        {
            if (m.Latest is not { } run) continue;
            high += run.Summary.OpenHigh;
            medium += run.Summary.OpenMedium;
            low += run.Summary.OpenLow;
            regressions += run.Results.Count(r => (r.Flags & ResultFlags.Regression) != 0);
            notCheckable += run.Summary.NotCheckable;
        }
        int expiring = Registers.Exceptions.Exceptions.Count(e => Info(e).State == RegisterEntryState.Expiring);
        int pending = Registers.Exceptions.Exceptions.Count(e => Info(e).State is RegisterEntryState.Pending or RegisterEntryState.ChangedAfterApproval);
        int overdue = Registers.Measures.Measures.Count(x => Info(x).Overdue);
        return (high, medium, low, regressions, expiring, pending, overdue, notCheckable);
    }
}
