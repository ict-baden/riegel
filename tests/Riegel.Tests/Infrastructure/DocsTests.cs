// SPDX-License-Identifier: MIT
using System.Text.RegularExpressions;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Infrastructure;

/// <summary>
/// Doku (Richtlinie 11, spec 9): jede Seite unter docs/ beginnt mit der Brotkrumen-Zeile zum README und hat eine
/// Inhaltszeile, die Pflichtseiten sind da, und relative Links in README und docs/ zeigen auf vorhandene Dateien.
/// </summary>
public sealed partial class DocsTests
{
    private static readonly string Docs = Path.Combine(RepoPaths.Root, "docs");

    public static TheoryData<string> Pages()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(Docs, "*.md").Order(StringComparer.Ordinal)) data.Add(Path.GetFileName(file));
        return data;
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Seite_hat_Brotkrumen_und_Inhaltszeile(string page)
    {
        var lines = File.ReadAllLines(Path.Combine(Docs, page));
        Assert.StartsWith("[Riegel](../README.md) › ", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines.Take(12), l => l.StartsWith("**Contents:**", StringComparison.Ordinal));
    }

    [Fact]
    public void Pflichtseiten_aus_spec_9_sind_da()
    {
        string[] required =
        [
            "installation", "collector", "collector-change-request", "usage", "command-line", "exceptions", "evaluation",
            "scoring-and-comparison", "baselines", "file-formats", "reports", "evidence", "remediation", "rules", "rule-sources",
            "frameworks", "settings", "data-and-privacy", "limitations", "development", "error-codes",
        ];
        foreach (var page in required) Assert.True(File.Exists(Path.Combine(Docs, page + ".md")), page + ".md fehlt");
    }

    [Fact]
    public void Relative_Links_zeigen_auf_vorhandene_Dateien()
    {
        var problems = new List<string>();
        var files = Directory.EnumerateFiles(Docs, "*.md").Append(Path.Combine(RepoPaths.Root, "README.md"));
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var folder = Path.GetDirectoryName(file)!;
            foreach (Match m in Link().Matches(text))
            {
                var target = m.Groups["t"].Value.Trim();
                if (target.Length == 0 || target.StartsWith('#') || target.Contains("://", StringComparison.Ordinal) || target.StartsWith("mailto:", StringComparison.Ordinal))
                    continue;
                var path = target.Split('#')[0];
                if (path.Length == 0) continue;
                var full = Path.GetFullPath(Path.Combine(folder, Uri.UnescapeDataString(path)));
                if (!File.Exists(full) && !Directory.Exists(full)) problems.Add($"{Path.GetFileName(file)}: {target}");
            }
        }
        Assert.True(problems.Count == 0, "Links ins Leere:\n" + string.Join("\n", problems));
    }

    // Markdown-Links ](ziel) und HTML-Attribute href="ziel" / src="ziel" / srcset="ziel"
    [GeneratedRegex(@"(?:\]\((?<t>[^)\s]+)\)|(?:href|src|srcset)=""(?<t>[^""]+)"")")]
    private static partial Regex Link();
}
