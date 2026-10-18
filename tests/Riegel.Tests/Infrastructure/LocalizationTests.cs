// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Riegel.Core.Localization;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Resources;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Infrastructure;

/// <summary>
/// Sprachdateien (Richtlinie 13, spec 7.10): jede .resx in fünf Sprachen mit denselben Schlüsseln und Platzhaltern
/// und ohne leere Texte; Regeltexte en+de; Meldungstabellen des Sammlers; Sprachwahl.
/// </summary>
public sealed partial class LocalizationTests
{
    /// <summary>Die Sprachdateien je Besitzer (spec 7.10).</summary>
    public static TheoryData<string, string> Owners => new()
    {
        { "src/Riegel.Core/Resources", "CommonStrings" },
        { "src/Riegel.Core/Resources", "EngineStrings" },
        { "src/Riegel.Core/Resources", "ParsingStrings" },
        { "src/Riegel.Core/Resources", "WorkflowStrings" },
        { "src/Riegel.Core/Resources", "PdfStrings" },
        { "src/Riegel.Core/Resources", "DataOutputStrings" },
        { "src/Riegel.Commands/Resources", "CliStrings" },
        { "src/Riegel.Platform.Windows/Resources", "PlatformStrings" },
        { "src/Riegel/Resources", "UiStrings" },
    };

    [Theory]
    [MemberData(nameof(Owners))]
    public void Jede_Sprachdatei_gibt_es_in_fuenf_Sprachen_mit_denselben_Schluesseln(string folder, string name)
    {
        var english = Load(Path.Combine(RepoPaths.Root, folder, name + ".resx"));
        Assert.NotEmpty(english);
        foreach (var code in UiLanguage.Supported.Where(c => c != UiLanguage.Fallback))
        {
            var other = Load(Path.Combine(RepoPaths.Root, folder, $"{name}.{code}.resx"));
            Assert.Equal(english.Keys.Order(StringComparer.Ordinal), other.Keys.Order(StringComparer.Ordinal));
            foreach (var (key, text) in other)
                Assert.True(Placeholders(english[key]).SetEquals(Placeholders(text)), $"{name}.{code}: {key} – „{english[key]}“ vs. „{text}“");
        }
    }

    [Theory]
    [MemberData(nameof(Owners))]
    public void Keine_leeren_Texte(string folder, string name)
    {
        foreach (var code in UiLanguage.Supported)
        {
            var file = Path.Combine(RepoPaths.Root, folder, code == UiLanguage.Fallback ? name + ".resx" : $"{name}.{code}.resx");
            foreach (var (key, text) in Load(file))
                Assert.False(string.IsNullOrWhiteSpace(text), $"{Path.GetFileName(file)}: {key} ist leer");
        }
    }

    [Fact]
    public void Jede_resx_im_Repo_gehoert_zu_einem_bekannten_Besitzer_und_hat_fuenf_Sprachen()
    {
        var known = Owners.Select(o => Path.GetFullPath(Path.Combine(RepoPaths.Root, (string)o[0], (string)o[1] + ".resx"))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var neutral = Directory.EnumerateFiles(RepoPaths.Src, "*.resx", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => Path.GetFileNameWithoutExtension(f).Count(c => c == '.') == 0)
            .ToList();
        foreach (var file in neutral)
        {
            Assert.True(known.Contains(Path.GetFullPath(file)), $"{file} ist keinem Besitzer zugeordnet (spec 7.10) – in Owners eintragen.");
            foreach (var code in UiLanguage.Supported.Where(c => c != UiLanguage.Fallback))
                Assert.True(File.Exists(Path.ChangeExtension(file, null) + $".{code}.resx"), $"{file}: Sprache {code} fehlt");
        }
    }

    [Fact]
    public void Regeltexte_gibt_es_in_Englisch_und_Deutsch()
    {
        var problems = new List<string>();
        foreach (var file in RepoPaths.RuleFiles())
        {
            var rules = RiegelJson.Deserialize<RuleFile>(File.ReadAllBytes(file)).Rules;
            foreach (var rule in rules)
            {
                void Need(string field, LocalizedText? text, bool required)
                {
                    if (text is null) { if (required) problems.Add($"{rule.Id}: {field} fehlt"); return; }
                    foreach (var code in new[] { "en", "de" })
                        if (!text.Has(code)) problems.Add($"{rule.Id}: {field} ohne {code}");
                }
                Need("title", rule.Title, true);
                Need("rationale", rule.Rationale, true);
                Need("otNote", rule.OtNote, false);
                Need("remedy", rule.Remedy, false);
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Meldungstabellen_des_Sammlers_sind_vollstaendig()
    {
        var folder = Path.Combine(RepoPaths.Src, "Riegel.Collector");
        var english = Messages(Path.Combine(folder, "messages.en.txt"));
        if (english is null) return; // noch keine Tabellen (Paket collector)
        Assert.NotEmpty(english);
        foreach (var code in UiLanguage.Supported.Where(c => c != UiLanguage.Fallback))
        {
            var other = Messages(Path.Combine(folder, $"messages.{code}.txt"));
            Assert.True(other is not null, $"messages.{code}.txt fehlt");
            Assert.Equal(english.Keys.Order(StringComparer.Ordinal), other!.Keys.Order(StringComparer.Ordinal));
            foreach (var (key, text) in other)
            {
                Assert.False(string.IsNullOrWhiteSpace(text), $"messages.{code}.txt: {key} ist leer");
                Assert.True(Placeholders(english[key]).SetEquals(Placeholders(text)), $"messages.{code}.txt: {key}");
            }
        }
    }

    [Theory]
    [InlineData("de-DE", "de")]
    [InlineData("de-AT", "de")]
    [InlineData("en-US", "en")]
    [InlineData("fr-FR", "fr")]
    [InlineData("es-MX", "es")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("it-IT", "en")]
    public void Sprache_folgt_dem_System_wenn_nichts_gewaehlt_ist(string system, string expected) =>
        Assert.Equal(expected, UiLanguage.Resolve(CultureInfo.GetCultureInfo(system)));

    [Fact]
    public void Reihenfolge_Umgebung_Benutzer_Vorgabe_System()
    {
        var system = CultureInfo.GetCultureInfo("fr-FR");
        Assert.Equal("es", UiLanguage.Resolve(system, "es", "de", "en"));
        Assert.Equal("de", UiLanguage.Resolve(system, null, "de", "en"));
        Assert.Equal("en", UiLanguage.Resolve(system, "", " ", "EN"));
        Assert.Equal("fr", UiLanguage.Resolve(system, "klingonisch", null, null));
    }

    [Fact]
    public void Fuenf_Sprachen_und_zwei_gepruefte_Berichtssprachen()
    {
        Assert.Equal(["en", "de", "es", "fr", "zh-Hans"], UiLanguage.Supported);
        Assert.Equal(["en", "de"], UiLanguage.ReviewedReportLanguages);
        Assert.True(UiLanguage.IsReviewedReportLanguage("de-DE"));
        Assert.False(UiLanguage.IsReviewedReportLanguage("zh-Hans"));
    }

    [Fact]
    public void Texte_kommen_in_der_eingestellten_Sprache_sonst_Englisch()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("Nicht prüfbar", CommonStrings.Status_NotCheckable);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            Assert.Equal("Not checkable", CommonStrings.Status_NotCheckable);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("it");
            Assert.Equal("Not checkable", CommonStrings.Status_NotCheckable);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Jede_Kategorie_jeder_Status_und_jedes_Flag_hat_einen_Text()
    {
        foreach (var category in RuleCategories.All)
            Assert.False(string.IsNullOrEmpty(CommonStrings.ResourceManager.GetString("Category_" + category, CultureInfo.InvariantCulture)), category);
        foreach (var status in Enum.GetNames<ResultStatus>())
            Assert.False(string.IsNullOrEmpty(CommonStrings.ResourceManager.GetString("Status_" + status, CultureInfo.InvariantCulture)), status);
        foreach (var flag in Enum.GetNames<ResultFlags>().Where(f => f != nameof(ResultFlags.None)))
            Assert.False(string.IsNullOrEmpty(CommonStrings.ResourceManager.GetString("Flag_" + flag, CultureInfo.InvariantCulture)), flag);
        foreach (var flag in Enum.GetNames<RunFlags>().Where(f => f != nameof(RunFlags.None)))
            Assert.False(string.IsNullOrEmpty(CommonStrings.ResourceManager.GetString("RunFlag_" + flag, CultureInfo.InvariantCulture)), flag);
        foreach (var reason in Enum.GetNames<ReasonCategory>())
            Assert.False(string.IsNullOrEmpty(CommonStrings.ResourceManager.GetString("Reason_" + reason, CultureInfo.InvariantCulture)), reason);
        foreach (var status in Enum.GetNames<ItemStatus>())
            Assert.False(string.IsNullOrEmpty(CommonStrings.ResourceManager.GetString("Item_" + status, CultureInfo.InvariantCulture)), status);
        foreach (var code in RglCodesCommon.All)
            Assert.False(string.IsNullOrEmpty(CommonStrings.ResourceManager.GetString("Error_" + code.Replace("-", ""), CultureInfo.InvariantCulture)), code);
    }

    internal static Dictionary<string, string> Load(string file)
    {
        Assert.True(File.Exists(file), $"{file} fehlt");
        return XDocument.Load(file).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
    }

    private static Dictionary<string, string>? Messages(string file)
    {
        if (!File.Exists(file)) return null;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(file))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            int equals = line.IndexOf('=');
            Assert.True(equals > 0, $"{Path.GetFileName(file)}: Zeile ohne Schlüssel=Text: {line}");
            Assert.True(result.TryAdd(line[..equals], line[(equals + 1)..]), $"{Path.GetFileName(file)}: Schlüssel {line[..equals]} doppelt");
        }
        return result;
    }

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    private static SortedSet<int> Placeholders(string text) =>
        new(Placeholder().Matches(text.Replace("{{", "").Replace("}}", "")).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
}
