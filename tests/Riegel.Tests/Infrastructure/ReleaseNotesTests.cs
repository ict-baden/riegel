// SPDX-License-Identifier: MIT
using System.Text.RegularExpressions;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Infrastructure;

/// <summary>
/// Release-Texte und Beispiele in der Doku: Jede Version im CHANGELOG hat einen englischen Teil („### English“), den
/// .github/scripts/release-notes.ps1 als Text des Tag-Releases nimmt; Dateinamen mit Version in README und docs/ nennen
/// die neueste veröffentlichte Version, damit Beispiele nicht veralten.
/// </summary>
public sealed partial class ReleaseNotesTests
{
    private static readonly string[] Changelog = File.ReadAllLines(Path.Combine(RepoPaths.Root, "CHANGELOG.md"));

    [Fact]
    public void Jede_Version_hat_einen_englischen_Release_Text()
    {
        var sections = Sections();
        Assert.NotEmpty(sections);
        var missing = new List<string>();
        foreach (var (version, lines) in sections)
        {
            var english = lines.FindIndex(l => Regex.IsMatch(l, @"^### English\s*$"));
            if (english < 0) { missing.Add($"{version}: kein '### English'"); continue; }
            // Wie release-notes.ps1: HTML-Kommentarzeilen zählen nicht als Text
            if (lines.Skip(english + 1).All(l => string.IsNullOrWhiteSpace(l) || Regex.IsMatch(l, @"^\s*<!--.*-->\s*$")))
                missing.Add($"{version}: '### English' ist leer");
            if (lines.Skip(english + 1).Any(l => l.StartsWith("### ", StringComparison.Ordinal)))
                missing.Add($"{version}: '### English' muss der letzte Teil des Abschnitts sein (Unterüberschriften mit ####)");
        }
        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }

    [Fact]
    public void Beispiele_nennen_die_neueste_veroeffentlichte_Version()
    {
        var newest = Sections()[0].Version;
        var files = new[] { Path.Combine(RepoPaths.Root, "README.md") }
            .Concat(Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, "docs"), "*.md"));
        var stale = new List<string>();
        foreach (var file in files)
            foreach (Match m in VersionedName().Matches(File.ReadAllText(file)))
                if (m.Groups["v"].Value != newest) stale.Add($"{Path.GetFileName(file)}: {m.Value}");
        Assert.True(stale.Count == 0, $"Neueste Version im CHANGELOG ist {newest}; veraltet:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void Basisversion_ist_nicht_aelter_als_der_neueste_Abschnitt()
    {
        // Vor dem Tag gleich dem neuesten Abschnitt, direkt nach dem Tag angehoben (docs/development.md, Releases); dann
        // sammelt „Unveröffentlicht“ die Änderungen der nächsten Version. Älter hieße: latest-Builds sortierten vor das Release.
        var props = System.Xml.Linq.XDocument.Load(Path.Combine(RepoPaths.Root, "Directory.Build.props"));
        var baseText = props.Descendants("Version").Select(e => e.Value.Trim()).First(v => v.Length > 0);
        var newestText = Sections()[0].Version;
        Assert.Matches(@"^\d+\.\d+\.\d+$", baseText);
        var newest = Version.Parse(newestText.Split('-')[0]);
        var current = Version.Parse(baseText);
        Assert.True(current >= newest, $"Directory.Build.props nennt {baseText}, der neueste CHANGELOG-Abschnitt ist {newestText}.");
        if (current > newest)
            Assert.Contains(Changelog, l => Regex.IsMatch(l, @"^## \[Unveröffentlicht\]\s*$"));
    }

    /// <summary>Versionsabschnitte „## [x.y.z] – Datum“ in der Reihenfolge der Datei (neueste zuerst), ohne „Unveröffentlicht“.</summary>
    private static List<(string Version, List<string> Lines)> Sections()
    {
        var result = new List<(string, List<string>)>();
        List<string>? current = null;
        foreach (var line in Changelog)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                current = null;
                var m = Regex.Match(line, @"^## \[(?<v>\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?)\]");
                if (m.Success) { current = []; result.Add((m.Groups["v"].Value, current)); }
                continue;
            }
            current?.Add(line);
        }
        return result;
    }

    // Riegel-0.1.3-x64.msi, Riegel-Collector-0.1.3.zip, riegel-0.1.3-linux-x64 …
    [GeneratedRegex(@"\b(?:Riegel|riegel|Riegel-Collector(?:-allowlist|-ReadOnlyProof)?)-(?<v>\d+\.\d+\.\d+)(?=[-.])")]
    private static partial Regex VersionedName();
}
