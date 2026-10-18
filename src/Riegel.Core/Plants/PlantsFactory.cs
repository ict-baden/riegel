// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Resources;
using Riegel.Core.Services;

namespace Riegel.Core.Plants;

/// <summary>
/// Factory des Anlagenspeichers (gehört ab dem Gerüst dem Paket workflow; Signatur fest). Der Platzhalter liest und
/// schreibt die JSON-Dateien des Anlagenordners (spec 7.6) – ohne Sperrdatei und ohne Verlauf.
/// </summary>
public static class PlantsFactory
{
    public static IPlantStore Create(RiegelServices services) => new StubPlantStore(services);
}

/// <summary>Platzhalter (Gerüst).</summary>
internal sealed class StubPlantStore(RiegelServices services) : IPlantStore
{
    public const string PlantFile = "riegel-plant.json";

    public Plant Create(string dir, string name)
    {
        var folder = Path.GetFullPath(dir);
        var file = Path.Combine(folder, PlantFile);
        if (File.Exists(file)) throw RiegelException.Common(RglCodesCommon.AlreadyExists, file);
        Directory.CreateDirectory(folder);
        var plant = new Plant(SchemaIds.Plant, FileNames.Token(name, "plant").ToLowerInvariant(), name, null, [], [], PlantSettings.Default);
        var sha = RiegelJson.WriteFile(file, plant);
        return plant with { Folder = folder, ReadOnly = false, FileSha256 = sha };
    }

    public Plant Open(string dir, bool readOnly)
    {
        var folder = Path.GetFullPath(dir);
        var file = Path.Combine(folder, PlantFile);
        var plant = RiegelJson.ReadFile<Plant>(file, ReadLimits.Default.MaxPlantFileBytes);
        if (plant.Schema != SchemaIds.Plant) throw RiegelException.Common(RglCodesCommon.UnsupportedSchema, plant.Schema);
        return plant with { Folder = folder, ReadOnly = readOnly, FileSha256 = Hashes.Sha256File(file) };
    }

    public void SavePlant(Plant p, string expectedSha256)
    {
        var file = Path.Combine(Writable(p), PlantFile);
        if (File.Exists(file) && !string.Equals(Hashes.Sha256File(file), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new RiegelException(RglCodesWorkflow.ChangedByOthers, WorkflowStrings.Error_RGL0403);
        RiegelJson.WriteFile(file, p);
    }

    public ImportRecord Import(Plant p, IReadOnlyList<string> paths, ImportOptions o)
    {
        var folder = Writable(p);
        var limits = o.Limits ?? ReadLimits.Default;
        var now = services.Clock.GetUtcNow();
        var files = new List<ImportedFile>();
        foreach (var source in paths)
        {
            var snapshot = services.Snapshots.Read(source, limits);
            var check = services.Snapshots.VerifySidecar(source);
            var hostFolder = Path.Combine(folder, "snapshots", FileNames.Host(snapshot.Machine.HostName));
            Directory.CreateDirectory(hostFolder);
            var target = Path.Combine(hostFolder, Path.GetFileName(source));
            if (!File.Exists(target))
            {
                File.Copy(source, target);
                File.WriteAllText(SnapshotFile.SidecarPath(target), SnapshotFile.SidecarLine(check.Actual, Path.GetFileName(target)));
            }
            string? runId = null;
            if (o.Evaluate)
                runId = services.Runs.EvaluateAndStore(p, target, new RunOptions(ImportedAtUtc: now, Limits: limits)).Id;
            files.Add(new ImportedFile(Path.GetFileName(source), check.Actual, check.Status == HashCheckStatus.Ok,
                snapshot.Machine.HostName, snapshot.CollectedAtUtc, snapshot.Collector.Name, snapshot.Collector.Version,
                snapshot.Collector.ExeSha256, snapshot.IsAdmin, snapshot.Options, snapshot.Origin, runId));
        }
        var id = FileNames.Timestamp(now) + "-" + Hashes.Sha256Hex(string.Join("\n", files.Select(f => f.Sha256)))[..6];
        var record = new ImportRecord(SchemaIds.Import, id, now, o.CollectedByRole, o.WitnessRole, o.OperatorRole, files);
        RiegelJson.WriteFile(Path.Combine(folder, "imports", id + ".import.json"), record);
        return record;
    }

    public void SaveRun(Plant p, Run r) =>
        RiegelJson.WriteFile(Path.Combine(Writable(p), ReportFileNames.RunFile(r.HostName, r.Id)), r);

    public IReadOnlyList<RunInfo> ListRuns(Plant p, string host)
    {
        var folder = Path.Combine(Folder(p), "runs", FileNames.Host(host));
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder, "*.results.json")
            .Select(f => RiegelJson.ReadFile<Run>(f, ReadLimits.Default.MaxSnapshotBytes))
            .Select(r => new RunInfo(r.Id, r.HostName, r.EvaluatedAtUtc, r.EvaluationDate, r.CollectedAtUtc, r.Snapshot.Sha256,
                r.Catalogue.Version, r.ReEvaluationOf, r.Flags, r.Summary.Score, r.Summary.OpenHigh))
            .OrderBy(r => r.CollectedAtUtc).ThenBy(r => r.EvaluatedAtUtc).ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
    }

    public Run LoadRun(Plant p, string runId)
    {
        var runs = Path.Combine(Folder(p), "runs");
        if (RunIds.IsValid(runId) && Directory.Exists(runs))
        {
            foreach (var hostFolder in Directory.EnumerateDirectories(runs))
            {
                var file = Path.Combine(hostFolder, runId + ".results.json");
                if (File.Exists(file)) return RiegelJson.ReadFile<Run>(file, ReadLimits.Default.MaxSnapshotBytes);
            }
        }
        throw RiegelException.Common(RglCodesCommon.NotFound, runId);
    }

    public ApprovedState? LoadApproved(Plant p, string host)
    {
        var file = Path.Combine(Folder(p), "approved", FileNames.Host(host) + ".json");
        return File.Exists(file) ? RiegelJson.ReadFile<ApprovedState>(file, ReadLimits.Default.MaxPlantFileBytes) : null;
    }

    public void SaveApproved(Plant p, ApprovedState a) =>
        RiegelJson.WriteFile(Path.Combine(Writable(p), "approved", FileNames.Host(a.HostName) + ".json"), a);

    private static string Folder(Plant p) => p.Folder ?? throw RiegelException.Common(RglCodesCommon.NotFound, p.Id);

    private static string Writable(Plant p) =>
        p.ReadOnly ? throw RiegelException.Common(RglCodesCommon.ReadOnlyMode) : Folder(p);

    internal static string Stamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
}
