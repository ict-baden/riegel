// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Security.Cryptography;
using Riegel.Core.Diagnostics;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Model.Reports;
using Riegel.Core.Registers;
using Riegel.Core.Settings;
using Riegel.UI;

namespace Riegel.Session;

/// <summary>Welche Dokumente die Berichte-Seite erzeugt (spec 5).</summary>
internal enum DocumentKind { PlantReport, Summary, TargetConfig, ApprovalSheet, CollectionRecord, Evidence, Remediation, CollectorHandover }

/// <summary>Angaben der Berichte-Seite für ein Dokument.</summary>
internal sealed record DocumentRequest(DocumentKind Kind, string OutDir, string Language, ReportPreset Preset = ReportPreset.Audit,
    bool PerMachine = false, bool IncludeAppendix = true, IReadOnlyList<ReportFormat>? Formats = null, bool Evidence = false,
    bool IncludeBaselineContent = false, IReadOnlyList<string>? Hosts = null, string? GroupId = null, string? ImportId = null,
    IReadOnlyList<string>? PlantFolders = null, PlantSettings? Settings = null, IReadOnlyList<string>? Ids = null);

/// <summary>Alles, was schreibt (Register, Anlage, Importe, Läufe, Freigaben, Dokumente) – mit den Regeln für Prüfermodus und Demo.</summary>
internal sealed partial class AppSession
{
    private PlantContext RequirePlant() => _plant ?? throw new RiegelException(RglCodesApp.NoPlant, RglCodesApp.Text(RglCodesApp.NoPlant));

    private PlantContext RequireWritable()
    {
        var plant = RequirePlant();
        if (plant.ReadOnly) throw RiegelException.Common(RglCodesCommon.ReadOnlyMode);
        return plant;
    }

    // ---------------------------------------------------------------- Register

    /// <summary>Register ändern und speichern (optimistische Nebenläufigkeit über die geladenen Hashes, spec 7.6).</summary>
    public async Task SaveRegistersAsync(Func<RegisterSet, RegisterSet> change, string success)
    {
        var plant = RequireWritable();
        await Work(Loc.T("Busy_Saving"), () =>
        {
            var current = plant.Registers;
            plant.Services.Registers.Save(plant.Plant, change(current), current.Hashes);
        });
        Log.Info("Register gespeichert");
        await ReloadAsync();
        Report(NoticeKind.Success, success);
    }

    public Task SaveExceptionAsync(ExceptionEntry entry) => SaveRegistersAsync(r => r with
    {
        Exceptions = r.Exceptions with { Exceptions = Replace(r.Exceptions.Exceptions, entry, e => e.Id) },
    }, Loc.F("Notice_ExceptionSaved", entry.Id));

    public Task SaveTailoringAsync(TailoringEntry entry) => SaveRegistersAsync(r => r with
    {
        Tailoring = r.Tailoring with { Entries = Replace(r.Tailoring.Entries, entry, e => e.Id) },
    }, Loc.F("Notice_TailoringSaved", entry.Id));

    public Task SaveMeasureAsync(MeasureEntry entry) => SaveRegistersAsync(r => r with
    {
        Measures = r.Measures with { Measures = Replace(r.Measures.Measures, entry, e => e.Id) },
    }, Loc.F("Notice_MeasureSaved", entry.Id));

    /// <summary>Eintrag zurückziehen: aus dem Register entfernen (der Verlauf behält ihn, spec 2.11).</summary>
    public Task RetireAsync(RegisterKind kind, string id) => SaveRegistersAsync(r => kind switch
    {
        RegisterKind.Exceptions => r with { Exceptions = r.Exceptions with { Exceptions = [.. r.Exceptions.Exceptions.Where(e => e.Id != id)] } },
        RegisterKind.Tailoring => r with { Tailoring = r.Tailoring with { Entries = [.. r.Tailoring.Entries.Where(e => e.Id != id)] } },
        _ => r with { Measures = r.Measures with { Measures = [.. r.Measures.Measures.Where(e => e.Id != id)] } },
    }, Loc.F("Notice_Retired", id));

    /// <summary>„Genehmigung eintragen“ für ausgewählte Einträge (Rolle, Datum, Prüftermin, Nachweis, optional Name).</summary>
    public Task ApproveEntriesAsync(RegisterKind kind, IReadOnlyList<string> ids, Approval approval)
    {
        var plant = RequireWritable();
        return SaveRegistersAsync(r =>
        {
            var approved = plant.Services.Registers.Approve(r, kind, ids, approval);
            // Nur gültige Einträge freigeben (wie die Befehlszeile): sonst RGL-0448 und nichts gespeichert
            RegisterApproval.ThrowIfInvalid(plant.Services.Registers.Validate(approved, plant.Catalogue, plant.Plant, plant.Today), kind, ids);
            return approved;
        }, Loc.P("Notice_Approved", ids.Count, ids.Count, approval.ApprovalRecord));
    }

    /// <summary>Vorlage importieren: neue Einträge sind immer „beantragt“ (spec 2.11, 2.12).</summary>
    public Task ImportTemplateAsync(string path)
    {
        var plant = RequireWritable();
        return SaveRegistersAsync(r => plant.Services.Registers.ImportTemplate(r, path, ReadLimits.Default), Loc.T("Notice_TemplateImported"));
    }

    private static IReadOnlyList<T> Replace<T>(IReadOnlyList<T> list, T entry, Func<T, string> id)
    {
        var result = list.Where(e => !string.Equals(id(e), id(entry), StringComparison.OrdinalIgnoreCase)).ToList();
        result.Add(entry);
        return [.. result.OrderBy(id, StringComparer.Ordinal)];
    }

    // ---------------------------------------------------------------- Anlage

    /// <summary>Anlage ändern (Gruppen, Rechner, Identität, Einstellungen) und speichern.</summary>
    public async Task SavePlantAsync(Func<Plant, Plant> change, string success)
    {
        var plant = RequireWritable();
        await Work(Loc.T("Busy_Saving"), () => plant.Services.Plants.SavePlant(change(plant.Plant), plant.Plant.FileSha256 ?? ""));
        await ReloadAsync();
        Report(NoticeKind.Success, success);
    }

    // ---------------------------------------------------------------- Importe und Auswertung

    /// <summary>Exporte oder Bordmittel-Pakete importieren (mit erhebender Rolle und Zeuge, spec 5 O14).</summary>
    public async Task<ImportRecord> ImportAsync(IReadOnlyList<string> paths, string? collectedByRole, string? witnessRole, bool writeRecord)
    {
        var plant = RequireWritable();
        if (paths.Count == 0) throw new RiegelException(RglCodesApp.NothingToImport, RglCodesApp.Text(RglCodesApp.NothingToImport));
        var language = ReportLanguage;
        var (record, recordPath) = await Work(Loc.T("Busy_Importing"), () =>
        {
            // Exporte, die schon mit Lauf in der Anlage liegen, nicht noch einmal (Prüfung vom 2026-10-20); neu auswerten
            // geht über „Neu auswerten“
            var r = plant.Services.Plants.Import(plant.Plant, paths,
                new ImportOptions(Blank(collectedByRole), Blank(witnessRole), plant.Plant.Settings.OperatorRole, SkipKnown: true));
            string? pdf = null;
            if (writeRecord && r.Files.Count > 0)
            {
                var out1 = ReportsFolder(plant);
                pdf = plant.Services.Pdf.WriteCollectionRecord(plant.Services.ReportData.BuildCollectionRecord(plant.Plant, r.Id, language), new PdfOptions(out1));
            }
            return (r, pdf);
        });
        Log.Info($"Import: {record.Files.Count} Datei(en)");
        await ReloadAsync();
        int skipped = Math.Max(0, paths.Count - record.Files.Count);
        if (record.Files.Count == 0) Report(NoticeKind.Info, Loc.P("Notice_AlreadyImported", skipped, skipped));
        else Report(NoticeKind.Success, (recordPath is null
            ? Loc.P("Notice_Imported", record.Files.Count, record.Files.Count)
            : Loc.P("Notice_ImportedWithRecord", record.Files.Count, record.Files.Count, Path.GetFileName(recordPath)))
            + (skipped > 0 ? " " + Loc.P("Notice_AlreadyImported", skipped, skipped) : ""));
        return record;
    }

    /// <summary>Neueste Exporte der Rechner neu auswerten (nach Registeränderungen; spec 2.8 „reEvaluationOf“).</summary>
    public async Task ReEvaluateAsync(IReadOnlyList<string>? hosts = null)
    {
        var plant = RequireWritable();
        var language = ReportLanguage;
        var skipped = new List<string>();
        int count = await Work(Loc.T("Busy_Evaluating"), () =>
        {
            int n = 0;
            foreach (var m in plant.Machines)
            {
                if (hosts is not null && !hosts.Contains(m.HostName, StringComparer.OrdinalIgnoreCase)) continue;
                if (m.Latest is not { } run || string.IsNullOrEmpty(run.Snapshot.Path)) continue;
                // Nur der geprüfte Export im Anlagenordner (RGL-0415/0416): sonst würde ein fremder Pfad gelesen und
                // als Export in die Anlage übernommen; dieser Rechner wird dann übersprungen
                string? path;
                try { path = plant.SnapshotPath(run); }
                catch (RiegelException e)
                {
                    Log.Warning($"Neu auswerten: Export übersprungen ({e.Code})");
                    skipped.Add($"{m.HostName} ({e.Code})");
                    continue;
                }
                if (path is null) continue;
                plant.Services.Runs.EvaluateAndStore(plant.Plant, path, new RunOptions(ImportedAtUtc: run.ImportedAtUtc, Language: language));
                n++;
            }
            return n;
        });
        await ReloadAsync();
        Report(NoticeKind.Success, Loc.P("Notice_Reevaluated", count, count));
        if (skipped.Count > 0) Report(NoticeKind.Warning, Loc.F("Notice_ReevaluateSkipped", string.Join(", ", skipped)));
    }

    /// <summary>„Diesen Lauf freigeben“: freigegebener Stand mit Rolle und Nachweis (spec 2.14).</summary>
    public async Task ApproveRunAsync(string host, string runId, Approval approval)
    {
        var plant = RequireWritable();
        await Work(Loc.T("Busy_Saving"), () =>
        {
            var run = plant.Services.Plants.LoadRun(plant.Plant, runId);
            var results = Path.Combine(plant.Folder, ReportFileNames.RunFile(run.HostName, run.Id).Replace('/', Path.DirectorySeparatorChar));
            plant.Services.Plants.SaveApproved(plant.Plant, new ApprovedState(SchemaIds.Approved, run.HostName, run.Id, run.Snapshot.Sha256,
                Hashes.Sha256File(results), run.Catalogue.Version, plant.Registers.Hashes, approval));
        });
        await ReloadAsync();
        Report(NoticeKind.Success, Loc.F("Notice_RunApproved", host, approval.ApprovalRecord));
    }

    /// <summary>
    /// „Diesen PC prüfen“: erhöht direkt, sonst über den Übergabeordner mit UAC (spec 7.6). Nie in der Demo und nie
    /// beim Erzeugen der Screenshots; die Tests rufen das nie auf.
    /// </summary>
    public async Task CheckThisPcAsync(bool elevate)
    {
        if (!CollectionAllowed) throw new RiegelException(RglCodesApp.NotInDemo, RglCodesApp.Text(RglCodesApp.NotInDemo));
        var plant = RequireWritable();
        var platform = Platform;
        // Die Zwischenablage (%TEMP%\Riegel-Erfassung-… oder handoff\<token>\) mit Konten, SIDs, Defender-Ausnahmen und
        // Proxy nie liegen lassen – auch nicht, wenn Erfassung, Übergabe oder Import scheitern (Prüfung vom 2026-10-20).
        // Der Pfad steht fest, bevor etwas geschrieben wird; die Zuweisung im Hintergrund ist nach dem await sichtbar.
        string? transfer = null;
        try
        {
            string path = await Work(Loc.T("Busy_Collecting"), () =>
            {
                if (!elevate || IsElevated)
                {
                    var folder = Path.Combine(Path.GetTempPath(), "Riegel-Erfassung-" + Guid.NewGuid().ToString("N")[..8]);
                    transfer = folder;
                    var snapshot = platform.CollectThisMachine(new CollectOptions(), CancellationToken.None);
                    return SnapshotFile.Write(snapshot, folder);
                }
                var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                transfer = Path.Combine(AppPaths.HandoffFolder, token);
                var result = platform.RunElevatedCollect(token, CancellationToken.None);
                if (result.DifferentUserProfile)
                    throw new RiegelException(RglCodesApp.ElevationOtherProfile, RglCodesApp.Text(RglCodesApp.ElevationOtherProfile));
                if (!result.Success || result.SnapshotPath is null)
                    throw new RiegelException(result.ErrorCode ?? RglCodesApp.ElevationFailed, result.Message ?? RglCodesApp.Text(RglCodesApp.ElevationFailed));
                return result.SnapshotPath;
            });
            // Der Anlagenordner hält danach die importierte Kopie
            await ImportAsync([path], Loc.T("Role_ThisPc"), null, writeRecord: false);
        }
        finally
        {
            DeleteTransferFolder(transfer);
        }
    }

    /// <summary>
    /// Beim Start: liegengebliebene Übergabeordner (älter als ein Tag) löschen, etwa nach einem Absturz. Ein laufender
    /// erhöhter Lauf einer anderen Instanz ist jünger und bleibt.
    /// </summary>
    internal static void CleanupOldTransferFolders()
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        try
        {
            var candidates = Directory.EnumerateDirectories(Path.GetTempPath(), "Riegel-Erfassung-*");
            if (Directory.Exists(AppPaths.HandoffFolder)) candidates = candidates.Concat(Directory.EnumerateDirectories(AppPaths.HandoffFolder));
            foreach (var folder in candidates.ToList())
                if (Directory.GetLastWriteTimeUtc(folder) < cutoff) DeleteTransferFolder(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warning($"Alte Übergabeordner nicht aufgeräumt ({e.GetType().Name})");
        }
    }

    /// <summary>
    /// Löscht den Ordner, über den „Diesen PC prüfen“ den Export übergab – nur einen eigenen (Riegel-Erfassung-* im
    /// Temp-Ordner oder unter handoff\), und ohne Fehler, wenn das nicht geht (die Datei gehört dann z. B. dem Administrator).
    /// </summary>
    internal static void DeleteTransferFolder(string? folder)
    {
        if (folder is null) return;
        var full = Path.GetFullPath(folder);
        bool own = Path.GetFileName(full).StartsWith("Riegel-Erfassung-", StringComparison.Ordinal) &&
                   string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                   || string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(AppPaths.HandoffFolder), StringComparison.OrdinalIgnoreCase);
        if (!own || !Directory.Exists(full)) return;
        try { Directory.Delete(full, recursive: true); }
        catch (IOException e) { Log.Warning($"Übergabeordner nicht gelöscht ({e.GetType().Name})"); }
        catch (UnauthorizedAccessException e) { Log.Warning($"Übergabeordner nicht gelöscht ({e.GetType().Name})"); }
    }

    /// <summary>„Sammler auf Datenträger kopieren“ (O15).</summary>
    public async Task<string> ExportCollectorAsync(string targetDir)
    {
        var platform = Platform;
        var path = await Work(Loc.T("Busy_Copying"), () => platform.ExportCollectorBundle(targetDir));
        Report(NoticeKind.Success, Loc.F("Notice_CollectorCopied", path));
        return path;
    }

    // ---------------------------------------------------------------- Baselines

    /// <summary>Kunden-Baseline importieren und als baselines/&lt;id&gt;.json ablegen (spec 2.15).</summary>
    public async Task ImportBaselineAsync(string path, string name)
    {
        var plant = RequireWritable();
        await Work(Loc.T("Busy_Importing"), () =>
        {
            var imported = plant.Services.Baselines.Import(path, name, ReadLimits.Default);
            var prefix = $"IMP-{imported.Baseline.Id}-";
            // Gespeichert wird die Form riegel.baseline/1 mit den IDs des Autors (ohne „IMP-<baseline>-“)
            var file = new BaselineFile(SchemaIds.Baseline, imported.Baseline.Id, imported.Baseline.Name, null, imported.Baseline.SourceFileName,
                imported.Baseline.SourceSha256, imported.Baseline.ImportedAtUtc,
                [.. imported.Rules.Select(r => new BaselineRule(r.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? r.Id[prefix.Length..] : r.Id,
                    r.Title.Get(Language), r.Severity, r.Category, r.Check, r.Rationale.Get(Language), null))]);
            RiegelJson.WriteFile(Path.Combine(plant.Folder, "baselines", FileNames.Token(imported.Baseline.Id) + ".json"), file);
        });
        await ReloadAsync();
        Report(NoticeKind.Success, Loc.F("Notice_BaselineImported", name));
    }

    /// <summary>Baseline entfernen (Datei baselines/&lt;id&gt;.json löschen; bestätigt vorher die Seite).</summary>
    public async Task RemoveBaselineAsync(string id)
    {
        var plant = RequireWritable();
        await Work(Loc.T("Busy_Saving"), () =>
        {
            var file = Path.Combine(plant.Folder, "baselines", FileNames.Token(id) + ".json");
            if (File.Exists(file)) File.Delete(file);
        });
        await ReloadAsync();
        Report(NoticeKind.Success, Loc.F("Notice_BaselineRemoved", id));
    }

    // ---------------------------------------------------------------- Dokumente

    /// <summary>Standard-Ausgabeordner: reports/&lt;Zeitpunkt&gt;/ im Anlagenordner (spec 7.6); im Prüfermodus keiner.</summary>
    public string? DefaultOutputFolder => _plant is { ReadOnly: false } p ? Path.Combine(p.Folder, "reports") : null;

    private string ReportsFolder(PlantContext plant) => NewOutputFolder(Path.Combine(plant.Folder, "reports"), plant.Services.Clock);

    /// <summary>Neuer Unterordner mit Zeitstempel unter <paramref name="baseFolder"/>.</summary>
    public string NewOutputFolder(string baseFolder) => NewOutputFolder(baseFolder, Services.Clock);

    /// <summary>
    /// Neuer Unterordner je Erzeugung (spec 7.6). Der Name kommt bewusst von der echten Uhr, nicht von der Uhr der
    /// Anlage: in der Demo steht diese fest, und alle Dokumente landeten sonst im selben Ordner (nur beim Erzeugen der
    /// Screenshots bleibt die feste Uhr, damit die Bilder gleich bleiben). Gibt es den Ordner schon (dieselbe Sekunde),
    /// bekommt der neue „-2“, „-3“ … angehängt.
    /// </summary>
    private string NewOutputFolder(string baseFolder, TimeProvider plantClock)
    {
        var clock = Mode == AppMode.Screenshots ? plantClock : RealServices.Clock;
        var folder = Path.Combine(baseFolder, clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HHmmss'Z'", CultureInfo.InvariantCulture));
        if (Mode == AppMode.Screenshots) return folder;
        var candidate = folder;
        for (int n = 2; Directory.Exists(candidate) || File.Exists(candidate); n++) candidate = folder + "-" + n.ToString(CultureInfo.InvariantCulture);
        return candidate;
    }

    /// <summary>
    /// Liegt <paramref name="path"/> im Ordner <paramref name="folder"/> (oder ist er es)? Mit Trennzeichen verglichen, damit
    /// ein Nachbarordner wie „Linie3-Berichte“ neben „Linie3“ nicht als innen gilt (Prüfung vom 2026-10-20).
    /// </summary>
    internal static bool IsInside(string folder, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ein Dokument erzeugen; gibt die geschriebenen Dateien zurück.</summary>
    public async Task<IReadOnlyList<string>> GenerateAsync(DocumentRequest d)
    {
        var plant = RequirePlant();
        if (string.IsNullOrWhiteSpace(d.OutDir)) throw new RiegelException(RglCodesApp.OutputFolderRequired, RglCodesApp.Text(RglCodesApp.OutputFolderRequired));
        if (plant.ReadOnly && IsInside(plant.Folder, d.OutDir)) throw RiegelException.Common(RglCodesCommon.ReadOnlyMode);
        var services = plant.Services;
        var p = d.Settings is null ? plant.Plant : plant.Plant with { Settings = d.Settings };
        var today = services.Today;
        var files = await Work(Loc.T("Busy_Report"), () =>
        {
            Directory.CreateDirectory(d.OutDir);
            var written = new List<string>();
            switch (d.Kind)
            {
                case DocumentKind.PlantReport:
                {
                    var formats = d.Formats ?? [ReportFormat.Pdf, ReportFormat.Xlsx];
                    var request = new ReportRequest(p, d.Hosts, today, d.Language, d.Preset, d.PerMachine, d.IncludeAppendix, formats,
                        d.Evidence, d.IncludeBaselineContent, false, d.OutDir);
                    var data = services.ReportData.Build(request);
                    if (formats.Contains(ReportFormat.Pdf))
                    {
                        written.Add(services.Pdf.WriteReport(data, new PdfOptions(d.OutDir, PerMachine: d.PerMachine, IncludeAppendix: d.IncludeAppendix)));
                        if (d.PerMachine)
                            written.AddRange(data.Machines.Select(m => Path.Combine(d.OutDir, ReportFileNames.MachineReport(data.Header.PlantName, data.Header.EvaluationDate, m.HostName)))
                                .Where(File.Exists));
                    }
                    if (formats.Contains(ReportFormat.Xlsx)) written.Add(services.Tabular.WriteWorkbook(data, d.OutDir));
                    if (formats.Contains(ReportFormat.Csv)) written.Add(services.Tabular.WriteCsv(data, d.OutDir));
                    if (formats.Contains(ReportFormat.Json)) written.AddRange(CopyRuns(plant, d.OutDir, d.Hosts));
                    if (d.Evidence)
                        written.Add(services.Evidence.Write(new EvidenceRequest(p, d.Hosts, today, [.. written], d.IncludeBaselineContent, p.Settings.OperatorRole, d.Language, d.OutDir)));
                    break;
                }
                case DocumentKind.Summary:
                {
                    var plants = new List<Plant> { p };
                    foreach (var folder in d.PlantFolders ?? [])
                    {
                        if (string.Equals(Path.GetFullPath(folder), Path.GetFullPath(plant.Folder), StringComparison.OrdinalIgnoreCase)) continue;
                        try { plants.Add(services.Plants.Open(folder, readOnly: true)); }
                        catch (RiegelException e) { Log.Warning($"Übersicht: Anlage übersprungen ({e.Code})"); }
                    }
                    written.Add(services.Pdf.WriteSummary(services.ReportData.BuildSummary(plants, today, d.Language), new PdfOptions(d.OutDir)));
                    break;
                }
                case DocumentKind.TargetConfig:
                {
                    var data = services.ReportData.BuildTargetConfig(p, d.GroupId, d.Language);
                    written.Add(services.Pdf.WriteTargetConfig(data, new PdfOptions(d.OutDir)));
                    written.Add(services.Tabular.WriteTargetConfigWorkbook(data, d.OutDir));
                    break;
                }
                case DocumentKind.ApprovalSheet:
                    written.Add(services.Pdf.WriteApprovalSheet(services.ReportData.BuildApprovalSheet(p, d.Ids, d.Language), new PdfOptions(d.OutDir)));
                    break;
                case DocumentKind.CollectionRecord:
                    written.Add(services.Pdf.WriteCollectionRecord(services.ReportData.BuildCollectionRecord(p, d.ImportId ?? "latest", d.Language), new PdfOptions(d.OutDir)));
                    break;
                case DocumentKind.Evidence:
                    written.Add(services.Evidence.Write(new EvidenceRequest(p, d.Hosts, today, [], d.IncludeBaselineContent, p.Settings.OperatorRole, d.Language, d.OutDir)));
                    break;
                case DocumentKind.Remediation:
                {
                    var catalogue = plant.Catalogue;
                    foreach (var m in plant.Machines)
                    {
                        if (d.Hosts is not null && !d.Hosts.Contains(m.HostName, StringComparer.OrdinalIgnoreCase)) continue;
                        if (m.Latest is not { } run || plant.ReadSnapshot(run) is not { } snapshot) continue;
                        written.Add(services.Remediation.Write(run, snapshot, catalogue, plant.Registers, d.OutDir, d.Language).Directory);
                    }
                    break;
                }
                case DocumentKind.CollectorHandover:
                    written.Add(Platform.ExportCollectorBundle(d.OutDir));
                    break;
            }
            return written;
        });
        Log.Info($"Dokument erzeugt: {d.Kind}");
        Report(NoticeKind.Success, Loc.P("Notice_DocumentWritten", files.Count, files.Count, d.OutDir));
        return files;
    }

    private static IEnumerable<string> CopyRuns(PlantContext plant, string outDir, IReadOnlyList<string>? hosts)
    {
        foreach (var m in plant.Machines)
        {
            if (hosts is not null && !hosts.Contains(m.HostName, StringComparer.OrdinalIgnoreCase)) continue;
            if (m.Latest is not { } run) continue;
            var relative = ReportFileNames.RunFile(run.HostName, run.Id).Replace('/', Path.DirectorySeparatorChar);
            var source = Path.Combine(plant.Folder, relative);
            if (!File.Exists(source)) continue;
            var target = Path.Combine(outDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
            yield return target;
        }
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
