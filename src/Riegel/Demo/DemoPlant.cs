// SPDX-License-Identifier: MIT
using Riegel.Core.Diagnostics;
using Riegel.Core.Model;
using Riegel.Core.Services;
using Riegel.Session;

namespace Riegel.Demo;

/// <summary>Feste Uhr der Demo (Stand der Demo-Anlage, unabhängig vom heutigen Datum).</summary>
internal sealed class DemoClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// Baut die Demo-Anlage „Musterwerk – Linie 3“ beim Start über die echten Dienste auf (spec 7.9): Anlagenordner im
/// Temp-Ordner, Register, zwei Erhebungsrunden je Rechner als Import (mit Erhebungsprotokoll-Angaben) und der
/// freigegebene Stand von HMI-L3-02 nach der ersten Runde. Jede Runde läuft mit der Uhr ihres Tages, damit Fristen,
/// veraltete Erfassungen und Vergleiche stimmen. Mit <c>RIEGEL_FAKE_SERVICES=1</c> (nur Entwicklung) werten
/// nachgebildete Dienste aus, sonst die echten.
/// </summary>
internal static class DemoPlant
{
    /// <summary>Markierung im Demo-Ordner; nur solche Ordner räumt Riegel wieder weg.</summary>
    public const string Marker = ".riegel-demo";

    public const string FolderPrefix = "Riegel-Demo-";

    /// <summary>Neuer, leerer Ordner für die Demo im Temp-Ordner des Benutzers.</summary>
    public static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), FolderPrefix + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Marker), "Riegel demo plant – deleted when Riegel closes.\n");
        return folder;
    }

    /// <summary>Demo-Ordner löschen (nur mit Markierung, Fehler werden ignoriert).</summary>
    public static void Delete(string? folder)
    {
        if (folder is null || !File.Exists(Path.Combine(folder, Marker))) return;
        try { Directory.Delete(folder, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warning($"Demo-Ordner nicht gelöscht: {e.GetType().Name}"); }
    }

    /// <summary>Übrig gebliebene Demo-Ordner früherer Starts (älter als einen Tag) aufräumen.</summary>
    public static void CleanupOld()
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(Path.GetTempPath(), FolderPrefix + "*"))
                if (File.Exists(Path.Combine(dir, Marker)) && Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddDays(-1)) Delete(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Dienste der Demo mit der Uhr <paramref name="now"/>.</summary>
    public static AppServices Services(IPlatform platform, DateTimeOffset now, bool fake) => AppServices.ForDemo(platform, new DemoClock(now), fake);

    /// <summary>Die Demo-Anlage in <paramref name="folder"/> aufbauen und für die Oberfläche öffnen.</summary>
    public static Plant Build(string folder, IPlatform platform, bool fake)
    {
        Log.Info(fake ? "Demo-Anlage wird aufgebaut (nachgebildete Dienste)" : "Demo-Anlage wird aufgebaut");
        var first = Services(platform, DemoData.FirstImport, fake);
        var second = Services(platform, DemoData.SecondImport, fake);
        var plantFolder = Path.Combine(folder, "Musterwerk-Linie-3");

        var plant = first.Plants.Create(plantFolder, DemoData.PlantName);
        var configured = plant with { Description = DemoData.Description, Groups = DemoData.Groups, Machines = DemoData.Machines, Settings = DemoData.Settings };
        first.Plants.SavePlant(configured, plant.FileSha256 ?? "");
        plant = first.Plants.Open(plantFolder, readOnly: false);

        // Register so, wie sie am Tag der jeweiligen Runde standen (EX-0003 wurde erst im Oktober beantragt)
        SaveRegisters(first, plant, DateOnly.FromDateTime(DemoData.FirstImport.UtcDateTime));

        var incoming = Path.Combine(folder, "eingang");
        var (round1, round2) = DemoData.Rounds();
        var options = new ImportOptions(DemoData.RoleTechnician, DemoData.RoleOwner, DemoData.RoleOwner);

        var record1 = first.Plants.Import(plant, Extract(incoming, round1), options);
        // Freigegebener Stand nach der Inbetriebnahme (SAT) – ab hier zählt jede Verschlechterung
        var approvedFile = record1.Files.FirstOrDefault(f => string.Equals(f.HostName, DemoData.ApprovedHost, StringComparison.OrdinalIgnoreCase));
        if (approvedFile?.RunId is { } runId)
        {
            var run = first.Plants.LoadRun(plant, runId);
            var resultsFile = Path.Combine(plantFolder, ReportFileNames.RunFile(run.HostName, run.Id).Replace('/', Path.DirectorySeparatorChar));
            var current = first.Registers.Load(plant);
            first.Plants.SaveApproved(plant, new ApprovedState(SchemaIds.Approved, run.HostName, run.Id, run.Snapshot.Sha256,
                Hashes.Sha256File(resultsFile), run.Catalogue.Version, current.Hashes, DemoData.SatApproval));
        }

        plant = second.Plants.Open(plantFolder, readOnly: false);
        SaveRegisters(second, plant, DateOnly.FromDateTime(DemoData.SecondImport.UtcDateTime));
        second.Plants.Import(plant, Extract(incoming, round2), options);
        try { Directory.Delete(incoming, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Log.Info("Demo-Anlage aufgebaut");
        return plant;
    }

    private static void SaveRegisters(AppServices services, Plant plant, DateOnly asOf)
    {
        var registers = services.Registers.Load(plant);
        services.Registers.Save(plant, registers with
        {
            Exceptions = registers.Exceptions with { Exceptions = [.. DemoData.Exceptions().Where(e => e.CreatedOn <= asOf)] },
            Tailoring = registers.Tailoring with { Entries = [.. DemoData.Tailoring().Where(t => t.CreatedOn <= asOf)] },
            Measures = registers.Measures with { Measures = [.. DemoData.Measures().Where(m => m.CreatedOn <= asOf)] },
        }, registers.Hashes);
    }

    /// <summary>Eingebettete Exporte mit Prüfsummendatei ablegen, wie sie vom USB-Stick kämen.</summary>
    private static List<string> Extract(string folder, IReadOnlyList<string> names)
    {
        Directory.CreateDirectory(folder);
        var paths = new List<string>();
        foreach (var name in names)
        {
            var path = Path.Combine(folder, name);
            using (var source = DemoData.OpenSnapshot(name))
            using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                source.CopyTo(target);
            File.WriteAllText(SnapshotFile.SidecarPath(path), SnapshotFile.SidecarLine(Hashes.Sha256File(path), name));
            paths.Add(path);
        }
        return paths;
    }
}
