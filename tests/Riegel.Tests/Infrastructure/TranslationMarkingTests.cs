// SPDX-License-Identifier: MIT
using System.Xml.Linq;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Infrastructure;

/// <summary>
/// Eine Kennzeichnung der Vorübersetzung (Review docs-ui-10, Richtlinie 13): jede es/fr/zh-Hans-Sprachdatei trägt
/// oben „Maschinell vorübersetzt – TODO-übersetzen …“ (wie messages.&lt;Sprache&gt;.txt des Sammlers), einzelne Einträge
/// nur noch „TODO-übersetzen“, und die unsicheren Einträge sind in allen drei Sprachen dieselben. Vorher gab es drei
/// Schreibweisen, und die Suche nach TODO-übersetzen übersah die 154 vorübersetzten Texte der Auswertung.
/// </summary>
public sealed class TranslationMarkingTests
{
    private const string Mark = "Maschinell vorübersetzt – TODO-übersetzen";
    private static readonly string[] PreTranslated = ["es", "fr", "zh-Hans"];

    private static IEnumerable<string> Bases() =>
        Directory.EnumerateFiles(RepoPaths.Src, "*.resx", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => Path.GetFileName(f).Count(c => c == '.') == 1)
            .Order(StringComparer.Ordinal);

    private static string Variant(string baseFile, string language) => Path.ChangeExtension(baseFile, null) + "." + language + ".resx";

    [Fact]
    public void Vorübersetzte_Sprachdateien_sind_oben_gekennzeichnet_und_geprüfte_nicht()
    {
        var bases = Bases().ToList();
        Assert.True(bases.Count >= 9, string.Join(", ", bases));
        foreach (var file in bases)
        {
            foreach (var language in PreTranslated)
            {
                var doc = XDocument.Load(Variant(file, language));
                var first = doc.Root!.Nodes().First();
                Assert.True(first is XComment c && c.Value.Trim().StartsWith(Mark, StringComparison.Ordinal),
                    $"{Path.GetFileName(Variant(file, language))}: Kennzeichnung oben fehlt");
            }
            foreach (var reviewed in new[] { file, Variant(file, "de") })
                Assert.DoesNotContain(Mark, File.ReadAllText(reviewed));
        }
    }

    [Fact]
    public void Eintraege_tragen_nur_TODO_uebersetzen_und_in_allen_drei_Sprachen_dieselben()
    {
        foreach (var file in Bases())
        {
            var todo = new Dictionary<string, HashSet<string>>();
            foreach (var language in PreTranslated)
            {
                var name = Path.GetFileName(Variant(file, language));
                var data = XDocument.Load(Variant(file, language)).Root!.Elements("data").ToList();
                foreach (var d in data)
                {
                    var comment = (string?)d.Element("comment");
                    Assert.True(comment is null or "TODO-übersetzen", $"{name} {(string?)d.Attribute("name")}: Kommentar „{comment}“");
                }
                todo[language] = data.Where(d => d.Element("comment") is not null).Select(d => (string)d.Attribute("name")!).ToHashSet();
            }
            foreach (var language in PreTranslated.Skip(1))
                Assert.True(todo["es"].SetEquals(todo[language]),
                    $"{Path.GetFileNameWithoutExtension(file)}: unsichere Einträge es ≠ {language}: {string.Join(", ", todo["es"].SymmetricExcept(todo[language]))}");
        }
    }

    [Fact]
    public void Sammlertexte_folgen_derselben_Kennzeichnung()
    {
        foreach (var language in PreTranslated)
            Assert.Contains(Mark, File.ReadLines(Path.Combine(RepoPaths.Src, "Riegel.Collector", $"messages.{language}.txt")).Take(3).First(l => l.Contains("TODO", StringComparison.Ordinal)));
        foreach (var language in new[] { "en", "de" })
            Assert.DoesNotContain("TODO-übersetzen", File.ReadAllText(Path.Combine(RepoPaths.Src, "Riegel.Collector", $"messages.{language}.txt")));
    }
}

file static class SetExtensions
{
    public static IEnumerable<string> SymmetricExcept(this HashSet<string> a, HashSet<string> b) => a.Except(b).Concat(b.Except(a)).Order(StringComparer.Ordinal);
}
