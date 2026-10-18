// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.RegularExpressions;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Tests.TestSupport;
using Riegel.UI;

namespace Riegel.Tests.Windows.App;

/// <summary>
/// Jeder Text der Oberfläche steht in UiStrings (Richtlinie 13): Schlüssel aus XAML (<c>{ui:T …}</c>) und Code
/// (<c>Loc.T/F("…")</c>) sowie die zusammengesetzten Schlüssel je Aufzählungswert – in allen fünf Sprachen.
/// </summary>
public sealed partial class UiStringsTests
{
    private static readonly string[] Languages = ["en", "de", "es", "fr", "zh-Hans"];
    private static string AppSource => Path.Combine(RepoPaths.Src, "Riegel");

    [GeneratedRegex(@"\{ui:T ([A-Za-z0-9_]+)\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"Loc\.[TF]\(""([A-Za-z0-9_]+)""[,)]")]
    private static partial Regex CodeKey();

    private static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(AppSource, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static void AssertKey(string key, string where)
    {
        foreach (var language in Languages)
        {
            var text = Loc.Strings.GetString(key, new CultureInfo(language));
            Assert.False(string.IsNullOrWhiteSpace(text), $"Schlüssel {key} fehlt ({language}) – {where}");
        }
    }

    [Fact]
    public void Schluessel_aus_XAML_und_Code()
    {
        int count = 0;
        foreach (var file in Files("*.xaml"))
            foreach (Match m in XamlKey().Matches(File.ReadAllText(file))) { AssertKey(m.Groups[1].Value, Path.GetFileName(file)); count++; }
        foreach (var file in Files("*.cs"))
            foreach (Match m in CodeKey().Matches(File.ReadAllText(file))) { AssertKey(m.Groups[1].Value, Path.GetFileName(file)); count++; }
        Assert.True(count > 300, $"nur {count} Schlüssel gefunden – stimmt das Muster noch?");
    }

    [Fact]
    public void Zusammengesetzte_Schluessel()
    {
        foreach (var v in Enum.GetValues<MachineRole>()) AssertKey("MachineRole_" + v, nameof(MachineRole));
        foreach (var v in Enum.GetValues<ValueSource>()) AssertKey("Source_" + v, nameof(ValueSource));
        foreach (var v in Enum.GetValues<RegisterEntryState>()) AssertKey("EntryState_" + v, nameof(RegisterEntryState));
        foreach (var v in Enum.GetValues<MeasureState>()) AssertKey("MeasureState_" + v, nameof(MeasureState));
        foreach (var v in Enum.GetValues<StatusChangeKind>()) AssertKey("Change_" + v, nameof(StatusChangeKind));
        foreach (var v in Enum.GetValues<DriftKind>()) AssertKey("Drift_" + v, nameof(DriftKind));
        foreach (var v in Enum.GetValues<SnapshotOrigin>()) AssertKey("Origin_" + v, nameof(SnapshotOrigin));
        foreach (var v in new[] { DomainScope.Joined, DomainScope.Standalone }) AssertKey("Catalogue_Domain_" + v, nameof(DomainScope));
        foreach (var v in new[] { "client", "server" }) AssertKey("OsKind_" + v, "os.kind");
        foreach (var slug in UI.Screenshots.Pages)
        {
            AssertKey("Page_" + slug, "Seite");
            AssertKey("Page_" + slug + "_Subtitle", "Seite");
        }
        foreach (var code in RglCodesApp.All) AssertKey("Error_" + code.Replace("-", ""), "RglCodesApp");
    }

    [Fact]
    public void Kein_Text_fest_in_den_Seiten()
    {
        // Text="…", Content="…", Header="…" und ToolTip="…" nur mit Bindung oder {ui:T}; Glyphen (Segoe Fluent Icons) erlaubt.
        // Ausnahmen: Produkt- und Formatnamen sowie die Zustandsnamen der Exitcodes (Nagios-Protokoll), in jeder Sprache gleich.
        string[] allowed = ["Riegel", "PDF", "Excel", "CSV", "JSON", "0 OK", "1 WARNING", "2 CRITICAL", "3 UNKNOWN"];
        var literal = new Regex(@"\b(Text|Content|Header|ToolTip)=""(?!\{|&#x)([^""]*[A-Za-zÄÖÜäöü]{2}[^""]*)""");
        foreach (var file in Files("*.xaml"))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in literal.Matches(text))
                if (!allowed.Contains(m.Groups[2].Value)) Assert.Fail($"{Path.GetFileName(file)}: fester Text {m.Value}");
        }
    }

    [Fact]
    public void Englisch_mit_englischen_Anfuehrungszeichen()
    {
        var set = Loc.Strings.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!;
        foreach (System.Collections.DictionaryEntry e in set)
            Assert.DoesNotContain("„", (string)e.Value!);
    }
}
