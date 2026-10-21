// SPDX-License-Identifier: MIT
using Riegel.Core.Model;

namespace Riegel.Commands.Commands;

/// <summary>
/// Rechnernamen aus <c>--machine</c> prüfen (Prüfung vom 2026-10-20: ein Tippfehler in <c>report --machine</c> ergab
/// einen leeren Bericht mit Exitcode 0, <c>measures add</c> nahm unbekannte Rechner an). Bekannt ist ein Rechner der
/// Anlage oder einer mit Läufen (Ordner unter runs/); verglichen wird wie überall ohne Groß/klein und bereinigt.
/// </summary>
internal static class PlantHosts
{
    /// <summary>Ordner der Läufe im Anlagenordner (wie PlantFiles.Runs in Riegel.Core).</summary>
    private const string RunsFolder = "runs";

    /// <summary>Bekannte Rechner in Anzeigeform: Einträge der Anlage, dann Ordner unter runs/ ohne Eintrag.</summary>
    public static IReadOnlyList<string> Known(Plant plant)
    {
        var result = plant.Machines.Select(m => m.HostName).ToList();
        var seen = new HashSet<string>(result.Select(FileNames.Host), StringComparer.OrdinalIgnoreCase);
        if (plant.Folder is { } folder && Directory.Exists(Path.Combine(folder, RunsFolder)))
            foreach (var dir in Directory.EnumerateDirectories(Path.Combine(folder, RunsFolder)).Order(StringComparer.Ordinal))
                if (seen.Add(Path.GetFileName(dir))) result.Add(Path.GetFileName(dir));
        return result;
    }

    /// <summary>Jeder Name in <paramref name="hosts"/> muss bekannt sein, sonst RGL-0452 mit der Liste der bekannten Rechner.</summary>
    public static void Require(Plant plant, IEnumerable<string> hosts, string option)
    {
        var known = Known(plant);
        var set = new HashSet<string>(known.Select(FileNames.Host), StringComparer.OrdinalIgnoreCase);
        foreach (var host in hosts)
            if (!set.Contains(FileNames.Host(host.Trim())))
                throw RglCodesCli.Error(RglCodesCli.InvalidValue, option, host, known.Count == 0 ? "–" : string.Join(", ", known));
    }
}
