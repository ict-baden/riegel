// SPDX-License-Identifier: MIT
namespace Riegel.Tests.TestSupport;

/// <summary>Pfade im Repo (vom Testordner aus nach oben bis zur Riegel.sln) und im Ausgabeordner der Tests.</summary>
public static class RepoPaths
{
    private static readonly Lazy<string> RootPath = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Riegel.sln"))) return dir.FullName;
        throw new InvalidOperationException("Riegel.sln nicht gefunden – der Test läuft außerhalb des Repos.");
    });

    /// <summary>Wurzel des Repos.</summary>
    public static string Root => RootPath.Value;

    public static string Rules => Path.Combine(Root, "rules");
    public static string Templates => Path.Combine(Root, "templates");
    public static string Schemas => Path.Combine(Root, "schemas");
    public static string Contracts => Path.Combine(Root, "contracts");
    public static string CollectionPlan => Path.Combine(Contracts, "collection-plan.json");
    public static string Src => Path.Combine(Root, "src");
    public static string TestsProject => Path.Combine(Root, "tests", "Riegel.Tests");

    /// <summary>Synthetische Fixtures, wie sie neben die Tests kopiert werden.</summary>
    public static string SyntheticFixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic");

    /// <summary>Aufgezeichnete Runner-Fixtures (Paket collector); der Ordner fehlt, bis es sie gibt.</summary>
    public static string RecordedFixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures", "recorded");

    /// <summary>Datei eines synthetischen Exports, z. B. „win11-24h2-workgroup-admin“.</summary>
    public static string Synthetic(string name) => Path.Combine(SyntheticFixtures, name + ".snapshot.json");

    /// <summary>Regeldateien unter rules/ (ohne catalogue.json), sortiert.</summary>
    public static IReadOnlyList<string> RuleFiles() =>
        Directory.Exists(Rules)
            ? Directory.EnumerateFiles(Rules, "*.json").Where(f => !Path.GetFileName(f).Equals("catalogue.json", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal).ToList()
            : [];
}
