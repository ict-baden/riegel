// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Xml.Linq;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Infrastructure;

/// <summary>
/// Versionen mit Lizenzgrenze (spec 7.1): PDFsharp-MigraDoc 6.2.x (ab 6.5 AGPL), SixLabors.Fonts 1.x (ab 2.0 Split
/// License), ClosedXML 0.105.1, WiX 5.x (ab 6 OSMF-EULA), JsonSchema.Net unter 9 (ab 9 OSMF-EULA, nur Tests). Gelesen
/// aus dem aufgelösten Paketgraphen (obj/project.assets.json), also auch für transitive Pakete.
/// </summary>
public sealed class DependencyPinTests
{
    [Theory]
    [InlineData("src/Riegel.Core")]
    [InlineData("src/Riegel")]
    [InlineData("src/Riegel.Cli")]
    public void Ausgelieferte_Pakete_bleiben_in_den_erlaubten_Versionen(string project)
    {
        var assets = Path.Combine(RepoPaths.Root, project, "obj", "project.assets.json");
        if (!File.Exists(assets))
        {
            // Unter Linux baut die CI nur das net10.0-Ziel der Tests; die WPF-App wird dort nicht wiederhergestellt
            Assert.False(project == "src/Riegel.Core", $"{assets} fehlt – wurde wiederhergestellt?");
            return;
        }
        var libraries = Libraries(assets);
        Assert.Equal("0.105.1", libraries["ClosedXML"]);
        Assert.StartsWith("6.2.", libraries["PDFsharp-MigraDoc"], StringComparison.Ordinal);
        Assert.StartsWith("6.2.", libraries["PDFsharp"], StringComparison.Ordinal);
        Assert.StartsWith("1.", libraries["SixLabors.Fonts"], StringComparison.Ordinal);
    }

    [Fact]
    public void Testpakete_bleiben_unter_der_OSMF_Grenze()
    {
        var assets = Path.Combine(RepoPaths.TestsProject, "obj", "project.assets.json");
        Assert.True(File.Exists(assets), $"{assets} fehlt");
        var version = Version.Parse(Libraries(assets)["JsonSchema.Net"]);
        Assert.True(version.Major < 9, $"JsonSchema.Net {version}: ab 9.0 gilt die OSMF-EULA statt MIT.");
    }

    [Fact]
    public void Installer_nutzt_WiX_5()
    {
        var doc = XDocument.Load(Path.Combine(RepoPaths.Root, "installer", "Riegel.Installer.wixproj"));
        var sdk = (string?)doc.Root!.Attribute("Sdk") ?? "";
        Assert.StartsWith("WixToolset.Sdk/5.", sdk, StringComparison.Ordinal);
        foreach (var reference in doc.Descendants("PackageReference").Where(r => ((string?)r.Attribute("Include") ?? "").StartsWith("WixToolset.", StringComparison.Ordinal)))
            Assert.StartsWith("5.", (string?)reference.Attribute("Version") ?? "", StringComparison.Ordinal);
    }

    private static Dictionary<string, string> Libraries(string assetsFile)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(assetsFile));
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lib in doc.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (lib.Value.GetProperty("type").GetString() != "package") continue;
            var parts = lib.Name.Split('/');
            result[parts[0]] = parts[1];
        }
        return result;
    }
}
