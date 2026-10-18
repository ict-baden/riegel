// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Plants;
using Riegel.Core.Services;

namespace Riegel.Core.Runs;

/// <summary>
/// Der Katalog, mit dem eine Anlage ausgewertet wird: eingebetteter Katalog plus die importierten Kunden-Baselines
/// unter <c>baselines/&lt;id&gt;.json</c> (spec 2.15), verbunden über <see cref="ICatalogueProvider.Combine"/>.
/// </summary>
internal static class PlantCatalogue
{
    public static (ICatalogue Catalogue, IReadOnlyList<BaselineRef> Baselines) For(RiegelServices services, Plant? plant, ICatalogue? given,
        ReadLimits limits)
    {
        if (given is not null) return (given, []);
        var core = services.Catalogue.LoadEmbedded();
        var imported = Baselines(services, plant, limits);
        if (imported.Count == 0) return (core, []);
        return (services.Catalogue.Combine(core, imported), [.. imported.Select(b => b.Baseline)]);
    }

    /// <summary>Gespeicherte Baselines der Anlage, nach ID sortiert; Name, Quell-Prüfsumme und Importzeit aus der Datei.</summary>
    public static IReadOnlyList<ImportedBaseline> Baselines(RiegelServices services, Plant? plant, ReadLimits limits)
    {
        if (plant?.Folder is not { } folder) return [];
        var directory = Path.Combine(folder, PlantFiles.Baselines);
        if (!Directory.Exists(directory)) return [];
        var list = new List<ImportedBaseline>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            var stored = RiegelJson.ReadFile<BaselineFile>(path, limits.MaxBaselineCsvBytes);
            var imported = services.Baselines.Import(path, stored.Name, limits);
            var reference = imported.Baseline with
            {
                Name = stored.Name,
                SourceSha256 = string.IsNullOrWhiteSpace(stored.SourceSha256) ? imported.Baseline.SourceSha256 : stored.SourceSha256,
                SourceFileName = stored.SourceFileName ?? imported.Baseline.SourceFileName,
                ImportedAtUtc = stored.ImportedAtUtc ?? imported.Baseline.ImportedAtUtc,
            };
            list.Add(imported with { Baseline = reference });
        }
        return [.. list.OrderBy(b => b.Baseline.Id, StringComparer.Ordinal)];
    }
}
