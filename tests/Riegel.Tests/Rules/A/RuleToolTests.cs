// SPDX-License-Identifier: MIT
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Rules.A;

/// <summary>
/// RuleTool (Paket rules-a): rules-doc erzeugt docs/rules.md deterministisch, verify-sources findet Verweise in lokalen
/// Quellkopien, coverage meldet Belegschlüssel außerhalb des Erfassungsplans. Alle Läufe arbeiten auf dem Repo
/// (nur lesend) oder auf Temp-Ordnern.
/// </summary>
public sealed partial class RuleToolTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "riegel-ruletool-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }

    [RuleToolFact]
    public void Regeldoku_im_Repo_ist_aktuell()
    {
        var (exit, stdout, stderr) = RuleToolRunner.Run(null, "rules-doc", "--check", "--repo", RepoPaths.Root);
        Assert.True(exit == 0, $"docs/rules.md ist veraltet – „dotnet tools/RuleTool/bin/<Konfiguration>/net10.0/RuleTool.dll rules-doc“ ausführen.\n{stdout}{stderr}");
    }

    [RuleToolFact]
    public void Regeldoku_ist_bytegleich_bei_zwei_Laeufen_und_nennt_jede_Regel()
    {
        Directory.CreateDirectory(_temp);
        var first = Path.Combine(_temp, "a.md");
        var second = Path.Combine(_temp, "b.md");
        Assert.Equal(0, RuleToolRunner.Run(null, "rules-doc", "--repo", RepoPaths.Root, "--out", first).ExitCode);
        Assert.Equal(0, RuleToolRunner.Run(null, "rules-doc", "--repo", RepoPaths.Root, "--out", second).ExitCode);
        var bytes = File.ReadAllBytes(first);
        Assert.Equal(bytes, File.ReadAllBytes(second));
        Assert.DoesNotContain((byte)'\r', bytes);
        var text = File.ReadAllText(first);
        Assert.StartsWith("[Riegel](../README.md) › Rule catalogue\n", text, StringComparison.Ordinal);
        Assert.Contains("**Contents:**", text, StringComparison.Ordinal);
        foreach (var id in RuleIdsInRepo())
            Assert.Contains($"| {id} |", text, StringComparison.Ordinal);
    }

    [RuleToolFact]
    public void Regeldoku_check_erkennt_eine_veraltete_Seite()
    {
        var repo = MiniRepo("NET-03");
        File.WriteAllText(Path.Combine(repo, "docs", "rules.md"), "veraltet\n");
        var (exit, _, stderr) = RuleToolRunner.Run(null, "rules-doc", "--check", "--repo", repo);
        Assert.Equal(1, exit);
        Assert.Contains("RGL-0303", stderr, StringComparison.Ordinal);
        Assert.Equal(0, RuleToolRunner.Run(null, "rules-doc", "--repo", repo).ExitCode);
        Assert.Equal(0, RuleToolRunner.Run(null, "rules-doc", "--check", "--repo", repo).ExitCode);
    }

    [RuleToolFact]
    public void VerifySources_meldet_fehlende_und_gefundene_Verweise()
    {
        var repo = MiniRepo("NET-03");
        var sources = Path.Combine(_temp, "sources");
        Directory.CreateDirectory(sources);
        // Nur ein Teil der Verweise von NET-03 in der „Quelle“: ACSC-Abschnitt und ATT&CK-Kennungen fehlen noch
        File.WriteAllText(Path.Combine(sources, "msb.txt"), "Network\\DNS Client | Turn off multicast name resolution || configured\n");
        File.WriteAllText(Path.Combine(sources, "gspp.json"), "{ \"id\": \"KONF.2.4\", \"title\": \"x\" } { \"id\": \"KONF.2.4.1\" }\n");
        var (exit, stdout, _) = RuleToolRunner.Run(null, "verify-sources", sources, "--repo", repo);
        Assert.Equal(1, exit);
        Assert.Contains("RGL-0305: ACSC 'Security policies'", stdout, StringComparison.Ordinal);
        Assert.Contains("RGL-0305: ATTACK 'M1042'", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("'Turn off multicast name resolution'", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("'KONF.2.4'", stdout, StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(sources, "acsc.html"), "<h2>Security policies</h2><p>…</p>");
        File.WriteAllText(Path.Combine(sources, "attack.txt"), "M1042 Disable or Remove Feature or Program; M1037; M0942; M0937");
        (exit, stdout, _) = RuleToolRunner.Run(null, "verify-sources", sources, "--repo", repo);
        Assert.True(exit == 0, stdout);
        Assert.Contains("NET-03: 7/7 found", stdout, StringComparison.Ordinal);
    }

    [RuleToolFact]
    public void VerifySources_liest_Kennungen_nur_an_Wortgrenzen()
    {
        var repo = MiniRepo("NET-03");
        var sources = Path.Combine(_temp, "sources");
        Directory.CreateDirectory(sources);
        File.WriteAllText(Path.Combine(sources, "a.txt"), "Security policies; Turn off multicast name resolution; KONF.2.41; XM1042; M1037; M0942; M0937");
        var (exit, stdout, _) = RuleToolRunner.Run(null, "verify-sources", sources, "--repo", repo);
        Assert.Equal(1, exit);
        Assert.Contains("RGL-0305: GSPP 'KONF.2.4'", stdout, StringComparison.Ordinal);
        Assert.Contains("RGL-0305: ATTACK 'M1042'", stdout, StringComparison.Ordinal);
    }

    [RuleToolFact]
    public void VerifySources_ohne_Ordner_ist_ein_Aufruffehler()
    {
        var (exit, _, stderr) = RuleToolRunner.Run(null, "verify-sources", Path.Combine(_temp, "gibt-es-nicht"), "--repo", RepoPaths.Root);
        Assert.Equal(3, exit);
        Assert.Contains("RGL-0304", stderr, StringComparison.Ordinal);
    }

    [RuleToolFact]
    public void Coverage_meldet_nur_bekannte_Luecken_der_Regeln_dieses_Pakets()
    {
        var (_, stdout, stderr) = RuleToolRunner.Run(null, "coverage", "--repo", RepoPaths.Root);
        Assert.True(stderr.Length == 0, stderr);
        Assert.Matches(@"\d+ of \d+ rules read evidence keys; \d+ keys, \d+ not in the plan", stdout);
        var known = KnownGaps();
        var unknown = new List<string>();
        foreach (Match m in GapLine().Matches(stdout))
        {
            var (rule, key) = (m.Groups["rule"].Value, m.Groups["key"].Value);
            if (!RuleData.PackageAPrefixes.Contains(rule.Split('-')[0])) continue;
            if (!known.Contains($"{rule} {key}")) unknown.Add($"{rule} {key}");
        }
        Assert.True(unknown.Count == 0, "Neue Planlücken (Integrationsanfrage nötig oder known-coverage-gaps.txt ergänzen):\n" + string.Join("\n", unknown));
    }

    [RuleToolFact]
    public void Coverage_erkennt_einen_Schluessel_ausserhalb_des_Plans()
    {
        var repo = MiniRepo("NET-03");
        var file = Path.Combine(repo, "rules", "NET.json");
        var node = JsonNode.Parse(File.ReadAllText(file))!;
        node["rules"]![0]!["check"]!["key"] = @"HKLM\SOFTWARE\Beispiel\NichtImPlan";
        File.WriteAllText(file, node.ToJsonString());
        var (exit, stdout, _) = RuleToolRunner.Run(null, "coverage", "--repo", repo);
        Assert.Equal(1, exit);
        Assert.Contains(@"RGL-0306: NET-03 registry reg:HKLM\SOFTWARE\Beispiel\NichtImPlan", stdout, StringComparison.Ordinal);

        node["rules"]![0]!["check"]!["key"] = @"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient";
        File.WriteAllText(file, node.ToJsonString());
        Assert.Equal(0, RuleToolRunner.Run(null, "coverage", "--repo", repo).ExitCode);
    }

    [RuleToolFact]
    public void Unbekannter_Befehl_und_unbekannte_Option_geben_RGL_0300()
    {
        var (exit, _, stderr) = RuleToolRunner.Run(null, "erfinde-regeln");
        Assert.Equal(3, exit);
        Assert.Contains("RGL-0300", stderr, StringComparison.Ordinal);
        (exit, _, stderr) = RuleToolRunner.Run(null, "coverage", "--schnell");
        Assert.Equal(3, exit);
        Assert.Contains("RGL-0300", stderr, StringComparison.Ordinal);
        Assert.Equal(3, RuleToolRunner.Run(null).ExitCode);
    }

    [RuleToolFact]
    public void Ohne_Repository_gibt_es_RGL_0301()
    {
        Directory.CreateDirectory(_temp);
        var (exit, _, stderr) = RuleToolRunner.Run(_temp, "rules-doc", "--check", "--repo", _temp);
        Assert.Equal(3, exit);
        Assert.Contains("RGL-0301", stderr, StringComparison.Ordinal);
    }

    /// <summary>Kleines Repository im Temp-Ordner: Katalogkopf, Erfassungsplan und eine Regel aus rules/.</summary>
    private string MiniRepo(string ruleId)
    {
        var root = Path.Combine(_temp, "repo");
        Directory.CreateDirectory(Path.Combine(root, "rules"));
        Directory.CreateDirectory(Path.Combine(root, "contracts"));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        File.Copy(Path.Combine(RepoPaths.Rules, "catalogue.json"), Path.Combine(root, "rules", "catalogue.json"));
        File.Copy(RepoPaths.CollectionPlan, Path.Combine(root, "contracts", "collection-plan.json"));
        var prefix = ruleId.Split('-')[0];
        var source = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Rules, prefix + ".json")))!;
        var rule = source["rules"]!.AsArray().First(r => (string?)r!["id"] == ruleId)!.DeepClone();
        var doc = new JsonObject { ["schema"] = "riegel.rules/1", ["prefix"] = prefix, ["rules"] = new JsonArray(rule) };
        File.WriteAllText(Path.Combine(root, "rules", prefix + ".json"), doc.ToJsonString());
        return root;
    }

    private static IEnumerable<string> RuleIdsInRepo() =>
        RepoPaths.RuleFiles().SelectMany(f => JsonNode.Parse(File.ReadAllText(f))!["rules"]!.AsArray().Select(r => (string)r!["id"]!));

    private static HashSet<string> KnownGaps()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(Path.Combine(RepoPaths.TestsProject, "Rules", "A", "known-coverage-gaps.txt")))
        {
            var text = line.Split('#')[0].Trim();
            if (text.Length > 0) set.Add(text);
        }
        return set;
    }

    [GeneratedRegex(@"^RGL-0306: (?<rule>[A-Z]+-\d{2}) \w+ (?<key>.+?)( \(fallback source\))?\r?$", RegexOptions.Multiline)]
    private static partial Regex GapLine();
}
