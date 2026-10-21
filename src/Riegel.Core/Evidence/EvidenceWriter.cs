// SPDX-License-Identifier: MIT
using System.IO.Compression;
using Riegel.Core.About;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;

namespace Riegel.Core.Evidence;

/// <summary>
/// Schreibt das Nachweispaket (spec 5 O9): Berichtsdateien, der neueste Lauf je Rechner mit seinem Export (+ .sha256),
/// die verwendeten Register (auch frühere Fassungen aus history/), die Anlage, der ganze Katalog, Baselines (nur
/// Metadaten, Inhalt nur auf Wunsch mit Lizenzwarnung), Vergleiche, Importnachweise, README.txt, manifest.json und
/// SHA256SUMS.txt. Nie die Rohdaten eines Bordmittel-Pakets, nie Dateien außerhalb des Anlagenordners.
/// </summary>
internal sealed class EvidenceWriter(RiegelServices services, Func<ICatalogue> catalogueSource)
{
    private const string PlantFileName = "riegel-plant.json";

    /// <summary>Was das README und das Manifest über den Inhalt wissen müssen.</summary>
    internal sealed record PackContent(EvidenceRequest Request, ICatalogue Catalogue, IReadOnlyList<PackedRun> Runs,
        IReadOnlyList<EvidenceBaseline> Baselines, IReadOnlyList<string> Notes, IReadOnlyList<string> Documents,
        IReadOnlyCollection<string> Paths);

    internal sealed record PackedRun(Run Run, string ResultsPath, string SnapshotPath);

    public string Write(EvidenceRequest r)
    {
        var limits = ReadLimits.Default;
        var plant = r.Plant;
        var now = services.Clock.GetUtcNow();
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var notes = new List<string>();
        var t = OutputText.For(r.Language);

        void Add(string path, byte[] bytes)
        {
            if (!EvidenceArchive.IsSafeName(path, out _) || !files.TryAdd(path, bytes))
                throw RglCodesDataOutputs.Error(RglCodesDataOutputs.DocumentNotPackable, path);
        }

        // Berichtsdateien (PDF, Excel, CSV) in die Wurzel des Pakets. Läufe aus --format json (runs/<host>/<runId>.results.json)
        // stehen schon unter runs/ im Paket und kämen sonst ein zweites Mal flach in die Wurzel.
        var documents = new List<string>();
        foreach (var document in r.Documents)
        {
            var name = Path.GetFileName(document);
            if (name.EndsWith(".results.json", StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(document) || !EvidenceLayout.DocumentName().IsMatch(name))
                throw RglCodesDataOutputs.Error(RglCodesDataOutputs.DocumentNotPackable, document);
            Add(name, ReadLimited(document, limits.MaxEvidenceEntryBytes, RglCodesDataOutputs.DocumentNotPackable));
            documents.Add(name);
        }

        // Der ganze Katalog als ein riegel.catalogue/1-Dokument (docs/file-formats.md)
        var catalogue = catalogueSource();
        Add(EvidenceLayout.Catalogue(catalogue.Version), RiegelJson.SerializeToUtf8Bytes(new CatalogueDocument(SchemaIds.Catalogue,
            catalogue.Id, catalogue.Version, catalogue.ReleasedOn, catalogue.Rules, catalogue.Lifecycle, catalogue.Builds,
            catalogue.Capabilities, catalogue.Data)));

        // Die Anlage (Gruppen, ESU, Einstellungen): verify braucht sie, um die Register anzuwenden
        var folder = plant.Folder;
        var plantPath = folder is null ? null : Path.Combine(folder, PlantFileName);
        Add(EvidenceLayout.PlantFile, plantPath is not null && File.Exists(plantPath)
            ? ReadLimited(plantPath, limits.MaxPlantFileBytes, RglCodesDataOutputs.RunNotPackable)
            : RiegelJson.SerializeToUtf8Bytes(plant));

        // Läufe und Exporte: der neueste Lauf je Rechner, wie im Bericht
        var runs = new List<PackedRun>();
        var currentRegisters = RegisterHashes.Empty;
        if (folder is not null)
        {
            foreach (var host in Hosts(plant, r.Hosts))
            {
                var latest = services.Plants.ListRuns(plant, host).LastOrDefault();
                if (latest is null) continue;
                runs.Add(PackRun(folder, latest.Id, host, limits, Add));
            }
            currentRegisters = services.Registers.Load(plant).Hashes;
            PackRegisters(folder, runs.Select(x => x.Run.Registers).Append(currentRegisters), limits, Add, notes, t);
            PackComparisons(plant, folder, runs, limits, Add, notes, t);
            PackImports(folder, runs, limits, Add);
        }

        // Baselines: Metadaten immer, Inhalt nur auf ausdrücklichen Wunsch (spec 2.15)
        var baselines = BaselinePacker.Pack(services, folder, runs.Select(x => x.Run).ToList(), r.IncludeBaselineContent, limits, Add, notes, t);
        if (baselines.Any(b => b.ContentIncluded))
            Add(EvidenceLayout.BaselineLicenseNote, EvidenceReadme.LicenseWarningFile(t));

        var content = new PackContent(r, catalogue, runs, baselines, notes, documents, files.Keys);
        Add(EvidenceLayout.Readme, EvidenceReadme.Build(content, t));

        var manifest = new EvidenceManifest(SchemaIds.Evidence, plant.Id, plant.Name, now, r.EvaluationDate, AppInfo.Tool(),
            new CatalogueRef(catalogue.Id, catalogue.Version, catalogue.ReleasedOn, catalogue.Sha256),
            Ref(files, EvidenceLayout.Catalogue(catalogue.Version)), baselines, currentRegisters,
            [.. runs.Select(x => new EvidenceRun(x.Run.Id, x.Run.HostName, Ref(files, x.ResultsPath), Ref(files, x.SnapshotPath)))],
            r.OperatorRole ?? plant.Settings.OperatorRole, [.. files.Select(f => new FileRef(f.Key, Hashes.Sha256Hex(f.Value)))]);
        files[EvidenceLayout.Manifest] = RiegelJson.SerializeToUtf8Bytes(manifest);
        files[EvidenceLayout.Checksums] = EvidenceLayout.ChecksumFile(files.Select(f => KeyValuePair.Create(f.Key, Hashes.Sha256Hex(f.Value))));

        Directory.CreateDirectory(r.OutDir);
        var path = Path.Combine(r.OutDir, ReportFileNames.Evidence(plant.Name, r.EvaluationDate));
        WriteZip(path, files, now);
        return path;
    }

    private static FileRef Ref(IReadOnlyDictionary<string, byte[]> files, string path) => new(path, Hashes.Sha256Hex(files[path]));

    /// <summary>Rechner der Anlage: aus riegel-plant.json und aus den Laufordnern (unbekannte Rechner, spec 7.6).</summary>
    private static IEnumerable<string> Hosts(Plant plant, IReadOnlyList<string>? only)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in plant.Machines) names.Add(FileNames.Host(m.HostName));
        var runs = Path.Combine(plant.Folder!, "runs");
        if (Directory.Exists(runs))
            foreach (var dir in Directory.EnumerateDirectories(runs)) names.Add(Path.GetFileName(dir));
        return only is null ? names : names.Where(h => only.Any(o => string.Equals(FileNames.Host(o), h, StringComparison.OrdinalIgnoreCase)));
    }

    private static PackedRun PackRun(string folder, string runId, string host, ReadLimits limits, Action<string, byte[]> add)
    {
        if (!RunIds.IsValid(runId)) throw RglCodesDataOutputs.Error(RglCodesDataOutputs.RunNotPackable, runId);
        var resultsFile = InPlant(folder, ReportFileNames.RunFile(host, runId));
        var resultsBytes = ReadLimited(resultsFile, limits.MaxSnapshotBytes, RglCodesDataOutputs.RunNotPackable);
        var run = RiegelJson.Deserialize<Run>(resultsBytes);
        if (run.Id != runId || run.Schema != SchemaIds.Run) throw RglCodesDataOutputs.Error(RglCodesDataOutputs.RunNotPackable, runId);

        // Export: nur eine Exportdatei im Anlagenordner, deren SHA-256 zum Lauf passt; nie ein Bordmittel-Rohpaket
        var snapshotFile = InPlant(folder, run.Snapshot.Path);
        var name = Path.GetFileName(snapshotFile);
        if (!EvidenceLayout.SnapshotName().IsMatch(name)) throw RglCodesDataOutputs.Error(RglCodesDataOutputs.RunNotPackable, run.Snapshot.Path);
        var snapshotBytes = ReadLimited(snapshotFile, limits.MaxSnapshotBytes, RglCodesDataOutputs.RunNotPackable);
        var sha = Hashes.Sha256Hex(snapshotBytes);
        if (!string.Equals(sha, run.Snapshot.Sha256, StringComparison.OrdinalIgnoreCase))
            throw RglCodesDataOutputs.Error(RglCodesDataOutputs.RunNotPackable, run.Snapshot.Path);

        var resultsPath = EvidenceLayout.RunFile(run.HostName, run.Id);
        var snapshotPath = EvidenceLayout.SnapshotFile(run.HostName, name);
        add(resultsPath, resultsBytes);
        add(snapshotPath, snapshotBytes);
        add(snapshotPath + SnapshotFile.SidecarExtension, System.Text.Encoding.UTF8.GetBytes(SnapshotFile.SidecarLine(sha, name)));
        return new PackedRun(run, resultsPath, snapshotPath);
    }

    /// <summary>
    /// Register „wie verwendet“: jede Fassung, auf die ein Lauf oder der Bericht verweist – die aktuelle Datei oder die
    /// Kopie in history/&lt;art&gt;/&lt;ts&gt;_&lt;sha8&gt;.json, geprüft über den vollen SHA-256.
    /// </summary>
    private static void PackRegisters(string folder, IEnumerable<RegisterHashes> used, ReadLimits limits, Action<string, byte[]> add,
        List<string> notes, OutputText t)
    {
        var wanted = new SortedSet<(RegisterKind, string)>();
        foreach (var h in used)
        {
            if (h.Exceptions.Length > 0) wanted.Add((RegisterKind.Exceptions, h.Exceptions.ToLowerInvariant()));
            if (h.Tailoring.Length > 0) wanted.Add((RegisterKind.Tailoring, h.Tailoring.ToLowerInvariant()));
            if (h.Measures.Length > 0) wanted.Add((RegisterKind.Measures, h.Measures.ToLowerInvariant()));
        }
        foreach (var (kind, sha) in wanted)
        {
            if (!Hashes.IsSha256(sha)) continue;
            var bytes = FindRegister(folder, kind, sha, limits);
            if (bytes is null)
            {
                notes.Add(t.Get(nameof(Resources.DataOutputStrings.EvidenceReadme_RegisterMissing), OutputText.Camel(kind), Hashes.Short(sha)));
                continue;
            }
            add(EvidenceLayout.Register(kind, sha), bytes);
        }
    }

    private static byte[]? FindRegister(string folder, RegisterKind kind, string sha, ReadLimits limits)
    {
        var name = OutputText.Camel(kind);
        var candidates = new List<string> { Path.Combine(folder, name + ".json") };
        var history = Path.Combine(folder, "history", name);
        if (Directory.Exists(history))
            candidates.AddRange(Directory.EnumerateFiles(history, "*_" + Hashes.Short(sha) + ".json").Order(StringComparer.Ordinal));
        foreach (var file in candidates)
        {
            if (!File.Exists(file)) continue;
            var info = new FileInfo(file);
            if (info.Length > limits.MaxRegisterBytes || (info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            var bytes = File.ReadAllBytes(file);
            if (Hashes.Sha256Hex(bytes) == sha) return bytes;
        }
        return null;
    }

    /// <summary>Vergleich mit dem vorigen Lauf und dem freigegebenen Stand je Rechner (comparison/).</summary>
    private void PackComparisons(Plant plant, string folder, IReadOnlyList<PackedRun> runs, ReadLimits limits, Action<string, byte[]> add,
        List<string> notes, OutputText t)
    {
        foreach (var packed in runs)
        {
            var run = packed.Run;
            Snapshot? current = null;
            Snapshot Current() => current ??= services.Snapshots.Read(InPlant(folder, run.Snapshot.Path), limits);
            void Compare(string? fromId, ComparisonBasis basis)
            {
                if (fromId is null) return;
                try
                {
                    var from = services.Plants.LoadRun(plant, fromId);
                    var fromSnapshot = services.Snapshots.Read(InPlant(folder, from.Snapshot.Path), limits);
                    var comparison = services.Comparison.Compare(from, fromSnapshot, run, Current(), basis);
                    add(EvidenceLayout.Comparison(run.HostName, basis), RiegelJson.SerializeToUtf8Bytes(comparison));
                }
                catch (RiegelException e)
                {
                    notes.Add(t.Get(nameof(Resources.DataOutputStrings.EvidenceReadme_ComparisonSkipped), run.HostName, e.Code));
                }
            }
            Compare(run.PreviousRunId, ComparisonBasis.Previous);
            string? approvedRun = null;
            try { approvedRun = services.Plants.LoadApproved(plant, run.HostName)?.RunId; }
            catch (RiegelException e) { notes.Add(t.Get(nameof(Resources.DataOutputStrings.EvidenceReadme_ComparisonSkipped), run.HostName, e.Code)); }
            Compare(approvedRun ?? run.ApprovedRunId, ComparisonBasis.Approved);
        }
    }

    /// <summary>Import- und Erhebungsnachweise, die einen gepackten Lauf oder Export betreffen.</summary>
    private static void PackImports(string folder, IReadOnlyList<PackedRun> runs, ReadLimits limits, Action<string, byte[]> add)
    {
        var imports = Path.Combine(folder, "imports");
        if (!Directory.Exists(imports)) return;
        var runIds = runs.Select(x => x.Run.Id).ToHashSet(StringComparer.Ordinal);
        var shas = runs.Select(x => x.Run.Snapshot.Sha256.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(imports, "*.import.json").Order(StringComparer.Ordinal))
        {
            var id = Path.GetFileName(file)[..^".import.json".Length];
            if (!EvidenceLayout.ImportId().IsMatch(id)) continue;
            var info = new FileInfo(file);
            if (info.Length > limits.MaxPlantFileBytes || (info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            var bytes = File.ReadAllBytes(file);
            ImportRecord record;
            try { record = RiegelJson.Deserialize<ImportRecord>(bytes); }
            catch (RiegelException) { continue; }
            if (record.Files.Any(f => (f.RunId is { } rid && runIds.Contains(rid)) || shas.Contains(f.Sha256.ToLowerInvariant())))
                add(EvidenceLayout.Import(id), bytes);
        }
    }

    /// <summary>
    /// Vollständiger Pfad einer Datei im Anlagenordner. Abgelehnt: absolute Pfade, Wege aus dem Ordner hinaus,
    /// Reparse-Punkte (Verknüpfungen) und Bordmittel-Rohpakete (<c>*.riegel-builtin</c>, Ordner).
    /// </summary>
    internal static string InPlant(string folder, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw RglCodesDataOutputs.Error(RglCodesDataOutputs.RunNotPackable, relative);
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw RglCodesDataOutputs.Error(RglCodesDataOutputs.RunNotPackable, relative);
        var parts = full[(root.Length + 1)..].Split(Path.DirectorySeparatorChar);
        if (parts.Any(p => p.EndsWith(".riegel-builtin", StringComparison.OrdinalIgnoreCase)) || Directory.Exists(full))
            throw RglCodesDataOutputs.Error(RglCodesDataOutputs.RunNotPackable, relative);
        // Keine Verknüpfungen auf dem Weg von der Wurzel zur Datei
        var current = root;
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw RglCodesDataOutputs.Error(RglCodesDataOutputs.RunNotPackable, relative);
        }
        return full;
    }

    internal static byte[] ReadLimited(string path, long max, string code)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw RglCodesDataOutputs.Error(code, path);
        if (info.Length > max) throw RiegelException.Common(RglCodesCommon.LimitExceeded, path, max);
        return File.ReadAllBytes(path);
    }

    private static void WriteZip(string path, IReadOnlyDictionary<string, byte[]> files, DateTimeOffset stamp)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                // ZIP speichert Ortszeit im DOS-Format; die feste UTC-Zeit des Pakets hält die Einträge gleich
                var time = new DateTimeOffset(DateTime.SpecifyKind(stamp.UtcDateTime, DateTimeKind.Unspecified), TimeSpan.Zero);
                foreach (var (name, bytes) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
                {
                    var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                    entry.LastWriteTime = time;
                    using var target = entry.Open();
                    target.Write(bytes);
                }
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
