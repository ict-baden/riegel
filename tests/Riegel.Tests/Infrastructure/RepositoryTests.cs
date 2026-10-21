// SPDX-License-Identifier: MIT
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Infrastructure;

/// <summary>Regeln für das Repo als Ganzes (Richtlinien 03, 08, 11; spec 4.3, 9).</summary>
public sealed partial class RepositoryTests
{
    [Fact]
    public void Keine_Platzhalter_der_Vorlagen_uebrig()
    {
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(RepoPaths.Root, "*", SearchOption.AllDirectories))
        {
            var parts = Path.GetRelativePath(RepoPaths.Root, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(p => p is ".git" or "bin" or "obj" or "node_modules" or "out" or "dist" or "TestResults")) continue;
            if (Path.GetExtension(file) is ".png" or ".ico" or ".exe" or ".dll" or ".zip" or ".msi" or ".pdf" or ".xlsx" or ".gz") continue;
            string text;
            try { text = File.ReadAllText(file); }
            catch (IOException) { continue; }
            foreach (Match m in Placeholder().Matches(text)) hits.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}: {m.Value}");
        }
        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }

    [Fact]
    public void Pflichtdateien_sind_da()
    {
        foreach (var file in new[] { "LICENSE", "README.md", "CHANGELOG.md", "SECURITY.md", "SUPPORT.md", "CONTRIBUTING.md", "THIRD-PARTY-NOTICES.txt",
                     "CLAUDE.md", "Directory.Build.props", "global.json", ".editorconfig", ".gitattributes", ".gitignore", "Riegel.sln",
                     ".github/workflows/build.yml", ".github/scripts/release-latest.ps1", ".github/scripts/sbom-shipped.ps1", ".github/dependabot.yml",
                     ".github/ISSUE_TEMPLATE/fehler.yml", ".github/ISSUE_TEMPLATE/wunsch.yml", ".github/ISSUE_TEMPLATE/config.yml",
                     "installer/Riegel.Installer.wixproj", "installer/Package.wxs", "docs/file-formats.md", "docs/development.md", "docs/assets/logo.svg",
                     "src/Riegel/Assets/riegel.ico", "src/Riegel/Assets/riegel.png", "contracts/collection-plan.json", "rules/catalogue.json" })
            Assert.True(File.Exists(Path.Combine(RepoPaths.Root, file)), file);
        Assert.Contains("Copyright (c) 2026 ICT-Baden", File.ReadAllText(Path.Combine(RepoPaths.Root, "LICENSE")), StringComparison.Ordinal);
    }

    [Fact]
    public void Sammler_hat_eigene_Version_und_ignoriert_die_CI_Version()
    {
        var project = XDocument.Load(Path.Combine(RepoPaths.Src, "Riegel.Collector", "Riegel.Collector.csproj"));
        var local = ((string?)project.Root!.Attribute("TreatAsLocalProperty") ?? "").Split(';');
        foreach (var property in new[] { "Version", "FileVersion", "AssemblyVersion", "InformationalVersion" }) Assert.Contains(property, local);
        Assert.Matches(@"^\d+\.\d+\.\d+$", File.ReadAllText(Path.Combine(RepoPaths.Src, "Riegel.Collector", "collector-version.txt")).Trim());
        var hash = File.ReadAllText(Path.Combine(RepoPaths.Src, "Riegel.Collector", "collector.sha256")).Trim();
        Assert.True(hash == "unset" || Core.Model.Hashes.IsSha256(hash), "collector.sha256: „unset“ oder ein SHA-256");
        var tfm = project.Descendants("TargetFramework").Single().Value;
        Assert.Equal("net35", tfm);
    }

    [Fact]
    public void Dateiformate_nennen_jeden_oeffentlichen_Typ_von_Core()
    {
        // docs/file-formats.md ist die Referenz der Verträge: ein neuer öffentlicher Typ gehört dort hinein
        var doc = File.ReadAllText(Path.Combine(RepoPaths.Root, "docs", "file-formats.md"));
        var missing = typeof(Core.Model.Snapshot).Assembly.GetExportedTypes()
            .Where(t => !t.IsNested && !doc.Contains($"**{t.Name}**", StringComparison.Ordinal))
            .Select(t => t.FullName).ToList();
        Assert.True(missing.Count == 0, "In docs/file-formats.md fehlen: " + string.Join(", ", missing));
    }

    [Fact]
    public void Workflow_bricht_ueberholte_Laeufe_ab_und_kennt_die_festen_Release_Namen()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoPaths.Root, ".github", "workflows", "build.yml"));
        Assert.Contains("concurrency:", workflow, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress:", workflow, StringComparison.Ordinal);
        foreach (var name in new[] { "Riegel-x64.msi", "Riegel.exe", "Riegel-win-x64-framework.zip", "riegel-win-x64.exe", "riegel-linux-x64",
                     "riegel-linux-arm64", "Riegel-Collector.zip", "Riegel-Collector-allowlist.zip", "Riegel-Collector-ReadOnlyProof.txt",
                     "Riegel-sbom.cdx.json", "SHA256SUMS.txt" })
            Assert.Contains($"'{name}'", workflow, StringComparison.Ordinal);
        // Artefakte dürfen kein Release tragen (Speicher der Organisation): jeder Upload ist klein und blockiert nicht
        var uploads = Regex.Matches(workflow, @"uses: actions/upload-artifact@[0-9a-f]{40}");
        Assert.Equal(uploads.Count, Regex.Matches(workflow, @"retention-days: 7").Count);
    }

    /// <summary>
    /// Regressionstest (Review legal-3): die Stückliste entsteht aus den deps.json aller ausgelieferten Programme – auch
    /// Riegel.Cli.exe für Windows, das System.CodeDom mitbringt – und nennt die WiX-Custom-Action der MSI.
    /// </summary>
    [Fact]
    public void Stueckliste_kennt_alle_ausgelieferten_Programme_und_die_MSI_Erweiterung()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoPaths.Root, ".github", "workflows", "build.yml"));
        foreach (var deps in new[] { "src/Riegel.Cli/obj/Release/net10.0-windows/win-x64/Riegel.Cli.deps.json",
                     "src/Riegel.Cli/obj/Release/net10.0/linux-x64/Riegel.Cli.deps.json", "src/Riegel.Cli/obj/Release/net10.0/linux-arm64/Riegel.Cli.deps.json" })
            Assert.Contains(deps, workflow, StringComparison.Ordinal);
        Assert.Contains("$deps.Count -ne 4", workflow, StringComparison.Ordinal);
        Assert.Contains("-WixProject installer/Riegel.Installer.wixproj", workflow, StringComparison.Ordinal);
        var script = File.ReadAllText(Path.Combine(RepoPaths.Root, ".github", "scripts", "sbom-shipped.ps1"));
        Assert.Contains("'WixToolset.Util.wixext'", script, StringComparison.Ordinal);
        Assert.Contains("MS-RL", script, StringComparison.Ordinal);
        Assert.Contains("WixToolset.Util.wixext", File.ReadAllText(Path.Combine(RepoPaths.Root, "installer", "Riegel.Installer.wixproj")), StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\{\{[A-Z_]+\}\}")]
    private static partial Regex Placeholder();
}
