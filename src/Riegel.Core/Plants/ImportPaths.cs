// SPDX-License-Identifier: MIT
using Riegel.Core.Model;

namespace Riegel.Core.Plants;

/// <summary>
/// Was ein Ordner beim Import bedeutet – für Befehlszeile und Oberfläche gleich (Prüfung vom 2026-10-20: die Oberfläche
/// hielt einen Ordner mit Exporten für ein Bordmittel-Paket und scheiterte mit RGL-0260, die Befehlszeile nahm ihn an).
/// Ein Ordner, dessen Name auf .riegel-builtin endet oder der hostname.txt bzw. secedit.inf enthält, ist ein
/// Bordmittel-Paket; jeder andere steht für die Exporte darin (*.snapshot.json, *.snapshot.json.gz, nicht rekursiv,
/// nach Namen sortiert).
/// </summary>
public static class ImportPaths
{
    /// <summary>Bordmittel-Paket statt Ordner mit Exporten.</summary>
    public static bool IsBuiltinBundle(string folder) =>
        Path.TrimEndingDirectorySeparator(folder).EndsWith(".riegel-builtin", StringComparison.OrdinalIgnoreCase)
        || File.Exists(Path.Combine(folder, "hostname.txt")) || File.Exists(Path.Combine(folder, "secedit.inf"));

    /// <summary>Exporte direkt in <paramref name="folder"/> (nicht rekursiv), nach Namen sortiert.</summary>
    public static IReadOnlyList<string> ExportsIn(string folder) =>
    [
        .. Directory.EnumerateFiles(folder).Where(IsExport).Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// Liegt mindestens ein Export direkt in <paramref name="folder"/>? Bricht beim ersten Treffer ab und gibt bei einem
    /// unlesbaren Ordner false zurück – für die Prüfung beim Ziehen über das Fenster, die oft und auf dem UI-Thread läuft
    /// (unabhängige Prüfung vom 2026-10-20).
    /// </summary>
    public static bool ContainsExports(string folder)
    {
        try { return Directory.EnumerateFiles(folder).Any(IsExport); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }

    private static bool IsExport(string file) =>
        file.EndsWith(SnapshotFile.Extension, StringComparison.OrdinalIgnoreCase) || file.EndsWith(SnapshotFile.GzipExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Pfade zum Import: Dateien und Bordmittel-Pakete bleiben, jeder andere Ordner wird durch seine Exporte ersetzt (ein
    /// Ordner ohne Exporte fällt weg – die Befehlszeile meldet ihn vorher mit RGL-0470).
    /// </summary>
    public static IReadOnlyList<string> Expand(IEnumerable<string> paths)
    {
        var result = new List<string>();
        foreach (var argument in paths)
        {
            var path = Path.GetFullPath(argument);
            if (!Directory.Exists(path) || IsBuiltinBundle(path)) result.Add(path);
            else result.AddRange(ExportsIn(path));
        }
        return result;
    }
}
