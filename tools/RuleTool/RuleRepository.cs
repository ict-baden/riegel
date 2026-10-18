// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;

namespace Riegel.Tools.RuleTool;

/// <summary>Regeldateien und Katalogkopf eines Repository-Stands (rules/*.json ohne catalogue.json).</summary>
public sealed class RuleRepository
{
    private const long MaxRuleFileBytes = 16 * 1024 * 1024;

    private RuleRepository(string root, CatalogueDocument header, IReadOnlyList<(string File, RuleFile Rules)> files)
    {
        Root = root;
        Header = header;
        Files = files;
    }

    /// <summary>Wurzel des Repositorys.</summary>
    public string Root { get; }

    /// <summary>Kopf aus rules/catalogue.json.</summary>
    public CatalogueDocument Header { get; }

    /// <summary>Regeldateien in ordinaler Reihenfolge der Dateinamen.</summary>
    public IReadOnlyList<(string File, RuleFile Rules)> Files { get; }

    /// <summary>Alle Regeln aller Dateien.</summary>
    public IEnumerable<Rule> Rules => Files.SelectMany(f => f.Rules.Rules);

    /// <summary>Liest rules/ unter <paramref name="root"/>.</summary>
    public static RuleRepository Load(string root)
    {
        var rules = Path.Combine(root, "rules");
        var headerPath = Path.Combine(rules, "catalogue.json");
        if (!File.Exists(headerPath)) throw new RuleToolException(RglCodesCatalogue.RepositoryNotFound, headerPath);
        CatalogueDocument header;
        try { header = RiegelJson.ReadFile<CatalogueDocument>(headerPath, MaxRuleFileBytes); }
        catch (Exception e) when (e is not RuleToolException) { throw new RuleToolException(RglCodesCatalogue.RuleFileInvalid, $"{headerPath}: {e.Message}"); }
        var files = new List<(string, RuleFile)>();
        foreach (var file in Directory.EnumerateFiles(rules, "*.json").Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(file).Equals("catalogue.json", StringComparison.OrdinalIgnoreCase)) continue;
            try { files.Add((file, RiegelJson.ReadFile<RuleFile>(file, MaxRuleFileBytes))); }
            catch (Exception e) { throw new RuleToolException(RglCodesCatalogue.RuleFileInvalid, $"{Path.GetFileName(file)}: {e.Message}"); }
        }
        return new RuleRepository(root, header, files);
    }

    /// <summary>Repository-Wurzel: <paramref name="explicitRoot"/> oder der erste Ordner oberhalb des Arbeitsordners mit Riegel.sln.</summary>
    public static string FindRoot(string? explicitRoot)
    {
        if (explicitRoot is not null)
        {
            var full = Path.GetFullPath(explicitRoot);
            if (!Directory.Exists(Path.Combine(full, "rules"))) throw new RuleToolException(RglCodesCatalogue.RepositoryNotFound, full);
            return full;
        }
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Riegel.sln"))) return dir.FullName;
        throw new RuleToolException(RglCodesCatalogue.RepositoryNotFound, Directory.GetCurrentDirectory());
    }

    /// <summary>Alle Prüfungen einer Regel, auch die verschachtelten.</summary>
    public static IEnumerable<CheckSpec> Flatten(CheckSpec check)
    {
        yield return check;
        IEnumerable<CheckSpec> children = check switch
        {
            FirstOfCheck f => f.Checks,
            AllOfCheck a => a.Checks,
            AnyOfCheck a => a.Checks,
            NoneOfCheck n => n.Checks,
            NotCheck n => [n.Check],
            _ => [],
        };
        foreach (var child in children)
            foreach (var nested in Flatten(child)) yield return nested;
    }
}
