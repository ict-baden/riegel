// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;

namespace Riegel.Tests.TestSupport;

/// <summary>
/// Anlagenordner in einem Temp-Ordner (spec 7.6), wird beim Dispose samt Inhalt gelöscht. Schreibt die Dateien direkt
/// nach dem Vertrag, unabhängig von der Umsetzung des Anlagenspeichers.
/// </summary>
public sealed class TempPlant : IDisposable
{
    public TempPlant(Plant? plant = null)
    {
        Folder = Path.Combine(Path.GetTempPath(), "riegel-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Folder);
        var content = (plant ?? Default()) with { Folder = null, ReadOnly = false, FileSha256 = null };
        var sha = RiegelJson.WriteFile(Path.Combine(Folder, PlantFile), content);
        Plant = content with { Folder = Folder, FileSha256 = sha };
    }

    public const string PlantFile = "riegel-plant.json";

    /// <summary>Vollständiger Pfad des Anlagenordners.</summary>
    public string Folder { get; }

    /// <summary>Die Anlage mit gesetztem <see cref="Plant.Folder"/>.</summary>
    public Plant Plant { get; }

    /// <summary>Kleine Anlage „Testanlage“ mit einer Gruppe und zwei Rechnern.</summary>
    public static Plant Default() => new(SchemaIds.Plant, "testanlage", "Testanlage", null,
        [new MachineGroup("g-hmi", "Bedienstationen", MachineRole.Hmi)],
        [new PlantMachine("HMI-TEST", "g-hmi", null, false, null, null), new PlantMachine("W11-TEST", null, null, false, null, null)],
        PlantSettings.Default);

    /// <summary>Export nach snapshots/&lt;host&gt;/ legen (mit .sha256); gibt den Pfad zurück.</summary>
    public string AddSnapshot(Snapshot snapshot) =>
        SnapshotFile.Write(snapshot, Path.Combine(Folder, "snapshots", FileNames.Host(snapshot.Machine.HostName)));

    /// <summary>Register als exceptions.json, tailoring.json und measures.json schreiben; gibt deren SHA-256 zurück.</summary>
    public RegisterHashes WriteRegisters(RegisterSet registers) => new(
        RiegelJson.WriteFile(Path.Combine(Folder, "exceptions.json"), registers.Exceptions),
        RiegelJson.WriteFile(Path.Combine(Folder, "tailoring.json"), registers.Tailoring),
        RiegelJson.WriteFile(Path.Combine(Folder, "measures.json"), registers.Measures));

    /// <summary>Pfad im Anlagenordner („runs/HMI/…“ mit „/“ oder „\“).</summary>
    public string PathOf(string relative) => Path.Combine(Folder, relative.Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        try { Directory.Delete(Folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Temporärer Ordner für Tests (aus Zutritt), wird beim Dispose gelöscht.</summary>
public sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "riegel-test-" + Guid.NewGuid().ToString("N")[..8]);

    public TempFolder() => Directory.CreateDirectory(Path);

    public string Sub(params string[] parts)
    {
        var p = System.IO.Path.Combine([Path, .. parts]);
        Directory.CreateDirectory(p);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
