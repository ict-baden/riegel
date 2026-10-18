// SPDX-License-Identifier: MIT
using System.Text;

namespace Riegel.Tools.RuleTool;

/// <summary>
/// Entwicklerwerkzeug für den Katalog (Paket rules-a). Nicht ausgeliefert; Ausgabe bewusst nur Englisch.
/// <list type="bullet">
/// <item><c>rules-doc [--repo &lt;dir&gt;] [--out &lt;file&gt;] [--check]</c>: docs/rules.md erzeugen bzw. prüfen</item>
/// <item><c>verify-sources &lt;folder&gt;… [--repo &lt;dir&gt;]</c>: Verweise gegen lokale Quellkopien prüfen</item>
/// <item><c>coverage [--repo &lt;dir&gt;] [--plan &lt;file&gt;]</c>: Belegschlüssel gegen den Erfassungsplan</item>
/// </list>
/// Exitcodes: 0 in Ordnung, 1 Befund (veraltet, fehlende Verweise, Planlücken), 3 Aufruf- oder Lesefehler.
/// </summary>
public static class Program
{
    public const int Ok = 0;
    public const int Findings = 1;
    public const int Error = 3;

    private const string UsageText =
        "Usage:\n" +
        "  RuleTool rules-doc [--repo <dir>] [--out <file>] [--check]\n" +
        "  RuleTool verify-sources <folder> [<folder>...] [--repo <dir>]\n" +
        "  RuleTool coverage [--repo <dir>] [--plan <file>]\n";

    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Einstieg für Tests: Ausgabe in eigene Writer.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "/?")
            {
                (args.Length == 0 ? stderr : stdout).Write(UsageText);
                return args.Length == 0 ? Error : Ok;
            }
            var (options, positional) = Parse(args.Skip(1).ToArray(), args[0]);
            return args[0].ToLowerInvariant() switch
            {
                "rules-doc" => RulesDoc(options, positional, stdout, stderr),
                "verify-sources" => VerifySources(options, positional, stdout),
                "coverage" => Coverage(options, positional, stdout),
                _ => throw new RuleToolException(RglCodesCatalogue.Usage, $"unknown command '{args[0]}'"),
            };
        }
        catch (RuleToolException e)
        {
            stderr.WriteLine(e.Message);
            if (e.Code == RglCodesCatalogue.Usage) stderr.Write(UsageText);
            return Error;
        }
    }

    private static (Dictionary<string, string?> Options, List<string> Positional) Parse(string[] args, string command)
    {
        var withValue = new HashSet<string>(StringComparer.Ordinal) { "--repo", "--out", "--plan" };
        var flags = new HashSet<string>(StringComparer.Ordinal) { "--check" };
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (withValue.Contains(a))
            {
                if (i + 1 >= args.Length) throw new RuleToolException(RglCodesCatalogue.Usage, $"{a} needs a value");
                options[a] = args[++i];
            }
            else if (flags.Contains(a)) options[a] = null;
            else if (a.StartsWith("--", StringComparison.Ordinal)) throw new RuleToolException(RglCodesCatalogue.Usage, $"unknown option '{a}'");
            else positional.Add(a);
        }
        var allowed = command.ToLowerInvariant() switch
        {
            "rules-doc" => new[] { "--repo", "--out", "--check" },
            "verify-sources" => ["--repo"],
            "coverage" => ["--repo", "--plan"],
            _ => [],
        };
        foreach (var key in options.Keys)
            if (!allowed.Contains(key)) throw new RuleToolException(RglCodesCatalogue.Usage, $"option '{key}' is not valid for {command}");
        if (command is "rules-doc" or "coverage" && positional.Count > 0)
            throw new RuleToolException(RglCodesCatalogue.Usage, $"unexpected argument '{positional[0]}'");
        return (options, positional);
    }

    private static int RulesDoc(Dictionary<string, string?> options, List<string> positional, TextWriter stdout, TextWriter stderr)
    {
        var root = RuleRepository.FindRoot(options.GetValueOrDefault("--repo"));
        var repo = RuleRepository.Load(root);
        var markdown = RulesDocGenerator.Generate(repo);
        var target = options.GetValueOrDefault("--out") ?? Path.Combine(root, "docs", "rules.md");
        var bytes = new UTF8Encoding(false).GetBytes(markdown);
        if (options.ContainsKey("--check"))
        {
            var current = File.Exists(target) ? File.ReadAllBytes(target) : [];
            if (current.AsSpan().SequenceEqual(bytes))
            {
                stdout.WriteLine($"{Path.GetFileName(target)} is up to date ({repo.Rules.Count()} rules).");
                return Ok;
            }
            stderr.WriteLine($"{RglCodesCatalogue.RulesDocOutdated}: {RglCodesCatalogue.Message(RglCodesCatalogue.RulesDocOutdated)} ({target})");
            return Findings;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        File.WriteAllBytes(target, bytes);
        stdout.WriteLine($"Wrote {target} ({repo.Rules.Count()} rules in {repo.Files.Count} files).");
        return Ok;
    }

    private static int VerifySources(Dictionary<string, string?> options, List<string> positional, TextWriter stdout)
    {
        if (positional.Count == 0) throw new RuleToolException(RglCodesCatalogue.Usage, "verify-sources needs at least one folder");
        var repo = RuleRepository.Load(RuleRepository.FindRoot(options.GetValueOrDefault("--repo")));
        var corpus = SourceVerifier.Corpus.Load(positional.Select(Path.GetFullPath));
        foreach (var skipped in corpus.Skipped)
            stdout.WriteLine($"{RglCodesCatalogue.SourceFileSkipped}: {skipped}");
        var results = SourceVerifier.Verify(repo, corpus);
        var missing = 0;
        foreach (var rule in results.GroupBy(r => r.RuleId))
        {
            var checkable = rule.Where(r => r.Found is not null).ToList();
            var notFound = checkable.Where(r => r.Found == false).ToList();
            missing += notFound.Count;
            stdout.WriteLine($"{rule.Key}: {checkable.Count - notFound.Count}/{checkable.Count} found" +
                             (rule.Any(r => r.Found is null) ? $", {rule.Count(r => r.Found is null)} URL-only reference(s) not checked" : ""));
            foreach (var r in notFound)
                stdout.WriteLine($"  {RglCodesCatalogue.ReferenceNotFound}: {r.Source} '{r.Token}'");
        }
        stdout.WriteLine($"{results.Select(r => r.RuleId).Distinct().Count()} rules, {results.Count(r => r.Found is not null)} references checked in {corpus.Files.Distinct().Count()} files, {missing} not found.");
        return missing == 0 ? Ok : Findings;
    }

    private static int Coverage(Dictionary<string, string?> options, List<string> positional, TextWriter stdout)
    {
        var root = RuleRepository.FindRoot(options.GetValueOrDefault("--repo"));
        var repo = RuleRepository.Load(root);
        var plan = CoverageAnalyzer.LoadPlan(options.GetValueOrDefault("--plan") ?? Path.Combine(root, "contracts", "collection-plan.json"));
        var evidence = CoverageAnalyzer.EvidenceKeys(repo.Rules);
        var gaps = CoverageAnalyzer.Gaps(repo.Rules, plan);
        foreach (var g in gaps)
            stdout.WriteLine($"{RglCodesCatalogue.EvidenceNotInPlan}: {g.RuleId} {g.CheckType} {g.Key}{(g.Fallback ? " (fallback source)" : "")}");
        stdout.WriteLine($"{evidence.Select(e => e.RuleId).Distinct().Count()} of {repo.Rules.Count()} rules read evidence keys; {evidence.Count} keys, {gaps.Count} not in the plan ({gaps.Count(g => g.Fallback)} of them fallback sources).");
        return gaps.Count == 0 ? Ok : Findings;
    }
}
