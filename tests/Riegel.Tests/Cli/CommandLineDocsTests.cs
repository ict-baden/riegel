// SPDX-License-Identifier: MIT
using System.Text.RegularExpressions;
using Riegel.Commands;
using Riegel.Commands.Alerting;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Cli;

/// <summary>
/// docs/command-line.md bleibt vollständig (Richtlinie 11): Brotkrumen und Inhaltszeile, jeder Befehl, jedes Token,
/// jeder Fehlercode der Befehlszeile, die Beispiele aus dem Paketauftrag, und jeder Link der Inhaltszeile trifft eine
/// Überschrift.
/// </summary>
public sealed partial class CommandLineDocsTests
{
    private static string Doc() => File.ReadAllText(Path.Combine(RepoPaths.Root, "docs", "command-line.md"));

    [Fact]
    public void Brotkrumen_und_Inhaltszeile()
    {
        var lines = Doc().Split('\n');
        Assert.StartsWith("[Riegel](../README.md) › Command line", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, l => l.StartsWith("**Contents:**", StringComparison.Ordinal));
    }

    [Fact]
    public void Jeder_Befehl_jedes_Token_und_jeder_Code_steht_drin()
    {
        var doc = Doc();
        foreach (var command in CommandApp.CommandNames)
            Assert.Contains("Riegel.Cli.exe " + command, doc, StringComparison.Ordinal);
        foreach (var token in AlertTokens.All.Select(AlertTokens.Name))
            Assert.Contains($"`{token}`", doc, StringComparison.Ordinal);
        foreach (var code in RglCodesCli.All)
            Assert.Contains($"`{code}`", doc, StringComparison.Ordinal);
        foreach (var option in new[] { "--help", "--hilfe", "--version", "--info", "--licenses", "--lizenzen", "--nur-lesen", "--read-only" })
            Assert.Contains($"`{option}`", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Beispiele_fuer_Aufgabenplanung_Icinga_FAT_Linux_und_Freigabe()
    {
        var doc = Doc();
        Assert.Contains("schtasks /Create", doc, StringComparison.Ordinal);
        Assert.Contains("object CheckCommand \"riegel\"", doc, StringComparison.Ordinal);
        Assert.Contains("## FAT pipelines", doc, StringComparison.Ordinal);
        Assert.Contains("## Auditors on Linux", doc, StringComparison.Ordinal);
        Assert.Contains("start /wait", doc, StringComparison.Ordinal);
        Assert.Contains("Out-Default", doc, StringComparison.Ordinal);
        Assert.Contains("## SYSTEM and a plant share", doc, StringComparison.Ordinal);
        Assert.Contains("Workgroup", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Die_O11_Zeile_steht_woertlich_drin()
    {
        Assert.Contains(
            "RIEGEL WARNING - HMI-01: 0 worse, 3 open (1 high), 1 exception expiring, 1 measure overdue | score=72%;;;0;100 open=3 high=1 regressions=0 expiring=1 overdue=1 notcheckable=4",
            Doc(), StringComparison.Ordinal);
    }

    [Fact]
    public void Jeder_Link_der_Inhaltszeile_trifft_eine_Ueberschrift()
    {
        var doc = Doc();
        var slugs = Heading().Matches(doc).Select(m => Slug(m.Groups[1].Value)).ToHashSet(StringComparer.Ordinal);
        var start = doc.IndexOf("**Contents:**", StringComparison.Ordinal);
        var end = doc.IndexOf("\n\n", start, StringComparison.Ordinal);
        foreach (Match link in Anchor().Matches(doc[start..end]))
            Assert.True(slugs.Contains(link.Groups[1].Value), $"#{link.Groups[1].Value} hat keine Überschrift");
    }

    /// <summary>Anker wie GitHub: klein, ohne Satzzeichen außer „-“, Leerzeichen zu „-“.</summary>
    private static string Slug(string heading) =>
        new string(heading.Trim().ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-').ToArray()).Replace(' ', '-');

    [GeneratedRegex(@"^#{2,3} (.+)$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"\]\(#([a-z0-9-]+)\)")]
    private static partial Regex Anchor();
}
