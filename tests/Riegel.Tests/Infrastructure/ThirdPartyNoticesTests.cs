// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Infrastructure;

/// <summary>
/// THIRD-PARTY-NOTICES.txt nennt jedes ausgelieferte Paket mit Version (Richtlinien 02/06, spec 7.1). Bringt Dependabot
/// ein neues Paket oder eine neue Version, schlägt der Test fehl, bis die Datei nachgezogen ist.
/// </summary>
public sealed partial class ThirdPartyNoticesTests
{
    /// <summary>Projekte, deren Pakete ausgeliefert werden (MSI, Exe, ZIP, Linux-Programme, Sammler-Paket).</summary>
    public static readonly string[] ShippedProjects =
    [
        "src/Riegel.Core/Riegel.Core.csproj", "src/Riegel.Commands/Riegel.Commands.csproj", "src/Riegel.Cli/Riegel.Cli.csproj",
        "src/Riegel.Platform.Windows/Riegel.Platform.Windows.csproj", "src/Riegel/Riegel.csproj", "src/Riegel.Collector/Riegel.Collector.csproj",
    ];

    [Fact]
    public void Jedes_direkt_referenzierte_ausgelieferte_Paket_steht_mit_Version_in_den_Hinweisen()
    {
        var entries = Entries(File.ReadAllText(Path.Combine(RepoPaths.Root, "THIRD-PARTY-NOTICES.txt")));
        var missing = new List<string>();
        foreach (var (id, version, project) in DirectPackages())
        {
            var entry = entries.FirstOrDefault(e => MentionsId(e, id));
            if (entry is null) missing.Add($"{id} {version} ({project}) fehlt");
            else if (!entry.Contains(version, StringComparison.Ordinal)) missing.Add($"{id}: Version {version} fehlt in seinem Eintrag");
        }
        Assert.True(missing.Count == 0, "THIRD-PARTY-NOTICES.txt ist nicht aktuell:\n  " + string.Join("\n  ", missing));
    }

#if WINDOWS
    [WindowsFact("Riegel.deps.json gibt es nur mit dem Windows-Ziel (WPF-App referenziert)")]
#else
    [Fact(Skip = "Riegel.deps.json gibt es nur mit dem Windows-Ziel (WPF-App referenziert)")]
#endif
    public void Jedes_Paket_der_App_steht_mit_Version_in_den_Hinweisen()
    {
        var depsFile = Path.Combine(AppContext.BaseDirectory, "Riegel.deps.json");
        Assert.True(File.Exists(depsFile), "Riegel.deps.json fehlt neben den Tests – ist die App referenziert?");
        using var doc = JsonDocument.Parse(File.ReadAllText(depsFile));
        var packages = doc.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(lib => lib.Value.GetProperty("type").GetString() == "package")
            .Select(lib => (Id: lib.Name.Split('/')[0], Version: lib.Name.Split('/')[1]))
            .ToList();
        Assert.NotEmpty(packages);
        var entries = Entries(File.ReadAllText(Path.Combine(RepoPaths.Root, "THIRD-PARTY-NOTICES.txt")));
        var missing = new List<string>();
        foreach (var (id, version) in packages)
        {
            var entry = entries.FirstOrDefault(e => MentionsId(e, id));
            if (entry is null) missing.Add($"{id} {version} fehlt");
            else if (!entry.Contains(version, StringComparison.Ordinal)) missing.Add($"{id}: Version {version} fehlt in seinem Eintrag");
        }
        Assert.True(missing.Count == 0, "THIRD-PARTY-NOTICES.txt ist nicht aktuell:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// Regressionstest (Review legal-1): PdfSharp.WPFonts.dll (Segoe-WP-Schriften von Microsoft, „All Rights Reserved“),
    /// PdfSharp.Snippets.dll und PdfSharp.Quality.dll kommen in keine Ausgabe und in keine deps.json
    /// (Directory.Build.targets). Die Testausgabe enthält die deps.json der App, des Konsolenprogramms und der Tests.
    /// </summary>
    [Fact]
    public void PDFsharp_Zusatzbibliotheken_mit_Microsoft_Schriften_werden_nicht_ausgeliefert()
    {
        string[] names = ["PdfSharp.WPFonts", "PdfSharp.Snippets", "PdfSharp.Quality"];
        var folder = AppContext.BaseDirectory;
        foreach (var name in names)
            Assert.False(File.Exists(Path.Combine(folder, name + ".dll")), $"{name}.dll liegt in der Ausgabe");
        var depsFiles = Directory.GetFiles(folder, "*.deps.json");
        Assert.NotEmpty(depsFiles);
        foreach (var deps in depsFiles)
        {
            var text = File.ReadAllText(deps);
            foreach (var name in names) Assert.DoesNotContain(name + ".dll", text, StringComparison.OrdinalIgnoreCase);
        }
        var targets = File.ReadAllText(Path.Combine(RepoPaths.Root, "Directory.Build.targets"));
        foreach (var name in names) Assert.Contains($"'{name}'", targets, StringComparison.Ordinal);
        var notices = File.ReadAllText(Path.Combine(RepoPaths.Root, "THIRD-PARTY-NOTICES.txt"));
        Assert.DoesNotContain("under its licence (MIT)", notices, StringComparison.Ordinal);
        Assert.Contains("Not shipped: the PDFsharp package also contains PdfSharp.WPFonts.dll", notices, StringComparison.Ordinal);
    }

    /// <summary>
    /// Regressionstest (Review legal-2): die Hinweise der mitgelieferten .NET-Laufzeiten liegen versionsgenau im Repo,
    /// werden mitgeliefert und in den einzelnen Programmen angehängt; THIRD-PARTY-NOTICES.txt verweist nicht mehr auf
    /// den beweglichen Zweig main.
    /// </summary>
    [Fact]
    public void Hinweise_der_Laufzeiten_sind_versionsgenau_und_eingebettet()
    {
        var notices = File.ReadAllText(Path.Combine(RepoPaths.Root, "THIRD-PARTY-NOTICES.txt"));
        Assert.DoesNotContain("/blob/main/THIRD-PARTY-NOTICES.TXT", notices, StringComparison.Ordinal);
        Assert.Contains("https://github.com/dotnet/runtime/blob/v10.0.13/THIRD-PARTY-NOTICES.TXT", notices, StringComparison.Ordinal);
        Assert.Contains("https://github.com/dotnet/wpf/blob/v10.0.13/THIRD-PARTY-NOTICES.TXT", notices, StringComparison.Ordinal);
        var all = Core.About.LicenseTexts.ThirdPartyNotices()!;
        foreach (var file in Core.About.LicenseTexts.RuntimeNoticesFiles)
        {
            var text = File.ReadAllText(Path.Combine(RepoPaths.Root, "notices", file));
            Assert.Contains(file, notices, StringComparison.Ordinal);
            Assert.Contains("===== " + file, all, StringComparison.Ordinal);
            Assert.Contains(text.TrimEnd().ReplaceLineEndings("\n"), all.ReplaceLineEndings("\n"), StringComparison.Ordinal);
            foreach (var project in new[] { "src/Riegel/Riegel.csproj", "src/Riegel.Cli/Riegel.Cli.csproj" })
                Assert.Contains(@"notices\THIRD-PARTY-NOTICES-dotnet-*.txt", File.ReadAllText(Path.Combine(RepoPaths.Root, project)), StringComparison.Ordinal);
        }
        var runtime = File.ReadAllText(Path.Combine(RepoPaths.Root, "notices", "THIRD-PARTY-NOTICES-dotnet-runtime.txt"));
        Assert.StartsWith(".NET Runtime uses third-party libraries", runtime, StringComparison.Ordinal);
        Assert.Contains("License notice for zlib-ng", runtime, StringComparison.Ordinal);
        // Liegt das Runtime-Paket 10.0.13 im NuGet-Cache, muss der Text gleich sein (die CI prüft das an jedem Publish)
        var cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        foreach (var rid in new[] { "win-x64", "linux-x64", "linux-arm64" })
        {
            var pack = Path.Combine(cache, "microsoft.netcore.app.runtime." + rid, "10.0.13", "THIRD-PARTY-NOTICES.TXT");
            if (File.Exists(pack)) Assert.Equal(File.ReadAllText(pack).ReplaceLineEndings("\n"), runtime.ReplaceLineEndings("\n"));
        }
    }

    [Fact]
    public void Hinweise_enthalten_die_Namensnennungen_der_Regelquellen_und_Laufzeiten()
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Root, "THIRD-PARTY-NOTICES.txt"));
        string[] required =
        [
            "© Commonwealth of Australia 2026", "CC BY 4.0", "windowsserverdocs",
            "© 2026 The MITRE Corporation. This work is reproduced and distributed with the permission of The MITRE Corporation.",
            "Grundschutz++", "CC BY-SA 4.0", "endoflife.date", "Copyright 2020 endoflife.date contributors", "DISA",
            "Microsoft.NETCore.App.Runtime.linux-x64", "Microsoft.NETCore.App.Runtime.linux-arm64", "Windows Desktop", "Microsoft.Windows.SDK.NET",
            "Microsoft Reciprocal License", "Apache License", "SixLabors.Fonts",
        ];
        var missing = required.Where(r => !text.Contains(r, StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "Es fehlt in THIRD-PARTY-NOTICES.txt: " + string.Join(", ", missing));
    }

    /// <summary>Direkte PackageReferences ausgelieferter Projekte, ohne reine Bau-Pakete (PrivateAssets="all").</summary>
    internal static IReadOnlyList<(string Id, string Version, string Project)> DirectPackages()
    {
        var list = new List<(string, string, string)>();
        foreach (var project in ShippedProjects)
        {
            var doc = XDocument.Load(Path.Combine(RepoPaths.Root, project));
            foreach (var reference in doc.Descendants("PackageReference"))
            {
                if (string.Equals((string?)reference.Attribute("PrivateAssets"), "all", StringComparison.OrdinalIgnoreCase)) continue;
                var id = (string?)reference.Attribute("Include");
                var version = (string?)reference.Attribute("Version");
                Assert.False(string.IsNullOrEmpty(id) || string.IsNullOrEmpty(version), $"{project}: PackageReference ohne Include oder feste Version");
                list.Add((id!, version!, project));
            }
        }
        return list;
    }

    /// <summary>Nummerierte Einträge („1. Name Version – …“) bis zum ersten Lizenztext (Trennlinie aus Bindestrichen).</summary>
    internal static List<string> Entries(string notices)
    {
        var head = notices.Replace("\r\n", "\n").Split(["\n---"], StringSplitOptions.None)[0];
        var starts = EntryStart().Matches(head).Select(m => m.Index).Append(head.Length).ToList();
        var list = new List<string>();
        for (int i = 0; i + 1 < starts.Count; i++) list.Add(head[starts[i]..starts[i + 1]]);
        return list;
    }

    /// <summary>Paket-Id als ganzes Wort (nicht „PDFsharp“ in „PDFsharp-MigraDoc“).</summary>
    private static bool MentionsId(string entry, string id) =>
        Regex.IsMatch(entry, $@"(?<![\w.-]){Regex.Escape(id)}(?![\w-]|\.\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"^\s*\d+\.\s", RegexOptions.Multiline)]
    private static partial Regex EntryStart();
}
