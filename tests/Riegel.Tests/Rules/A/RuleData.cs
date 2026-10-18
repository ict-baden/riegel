// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Tests.Infrastructure;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Rules.A;

/// <summary>Regeln und Tabellen des Pakets rules-a, direkt aus den Dateien unter rules/ gelesen.</summary>
internal static class RuleData
{
    /// <summary>Präfixe des Pakets rules-a (spec 2.1, Spalte Pkg).</summary>
    public static readonly string[] PackageAPrefixes = ["LIFE", "NET", "NETX", "CRED", "LOGON", "ACCT", "UAC", "URA"];

    private static readonly Lazy<IReadOnlyList<Rule>> AllRules = new(() =>
        PackageAPrefixes.SelectMany(p => RiegelJson.Deserialize<RuleFile>(File.ReadAllBytes(Path.Combine(RepoPaths.Rules, p + ".json"))).Rules).ToList());

    /// <summary>Alle Regeln der acht Dateien.</summary>
    public static IReadOnlyList<Rule> Rules => AllRules.Value;

    public static Rule Get(string id) => Rules.Single(r => r.Id == id);

    /// <summary>Soll-IDs des Pakets aus der gemeinsamen Katalogliste.</summary>
    public static IEnumerable<string> ExpectedIds => PackageAPrefixes.SelectMany(p => CatalogueInvariantTests.Catalogue202610[p]);

    public static LifecycleTable Lifecycle => RiegelJson.ReadFile<LifecycleTable>(Path.Combine(RepoPaths.Rules, "lifecycle", "windows.json"), 16 * 1024 * 1024);

    public static BuildTable Builds => RiegelJson.ReadFile<BuildTable>(Path.Combine(RepoPaths.Rules, "lifecycle", "builds.json"), 16 * 1024 * 1024);

    public static CapabilityFile Capabilities => RiegelJson.ReadFile<CapabilityFile>(Path.Combine(RepoPaths.Rules, "capabilities", "system.json"), 1024 * 1024);
}
