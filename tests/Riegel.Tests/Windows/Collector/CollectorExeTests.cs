// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using Riegel.Collector.Collection;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Windows.Collector;

/// <summary>
/// Der gebaute net35-Sammler als Programm: Exitcodes ohne Erhebung (Ziel nicht beschreibbar → 3 mit RGL-0102,
/// unbekannte Option → 3, --version → 0), Größe, Version, Konfiguration, Meldungstabellen und Codepages. Diese Tests
/// lesen nichts vom System: der Sammler bricht vor der Erhebung ab.
/// </summary>
public sealed class CollectorExeTests
{
    private static string Exe => CollectorTestFiles.CollectorExe;

    // Unter .NET 10 kennt Encoding die OEM-Codepages erst mit diesem Anbieter (das .NET Framework des Sammlers immer)
    static CollectorExeTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void Nicht_beschreibbares_Ziel_ergibt_Exitcode_3_und_nichts_wird_erhoben()
    {
        using var temp = new TempFolder();
        // Ein Ordnername, unter dem eine Datei liegt: dort lässt sich keine Datei anlegen
        string file = Path.Combine(temp.Path, "eine-datei");
        File.WriteAllText(file, "x");
        var (exit, output, error) = CollectorTestFiles.Run("--out", Path.Combine(file, "unterordner"));
        Assert.Equal(3, exit);
        Assert.Contains(RglCodesCollector.TargetNotWritable, error, StringComparison.Ordinal);
        Assert.DoesNotContain("Export:", output, StringComparison.Ordinal);
        Assert.Equal(["eine-datei"], Directory.GetFileSystemEntries(temp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Nicht_vorhandenes_Laufwerk_ergibt_Exitcode_3()
    {
        string? free = Enumerable.Range('G', 20).Select(c => $"{(char)c}:\\").FirstOrDefault(d => !Directory.Exists(d));
        if (free is null) return; // kein freier Laufwerksbuchstabe
        var (exit, _, error) = CollectorTestFiles.Run("--out", free + "riegel");
        Assert.Equal(3, exit);
        Assert.Contains("RGL-0102", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Unbekannte_Option_und_fehlender_Wert_ergeben_Exitcode_3()
    {
        var (exit, _, error) = CollectorTestFiles.Run("--gibt-es-nicht");
        Assert.Equal(3, exit);
        Assert.Contains("RGL-0101", error, StringComparison.Ordinal);
        Assert.Equal(3, CollectorTestFiles.Run("--out").Exit);
    }

    [Fact]
    public void Version_und_Hilfe_mit_Exitcode_0()
    {
        var (exit, output, _) = CollectorTestFiles.Run("--version");
        Assert.Equal(0, exit);
        string version = File.ReadAllText(Path.Combine(RepoPaths.Src, "Riegel.Collector", "collector-version.txt")).Trim();
        Assert.Contains(version, output, StringComparison.Ordinal);
        var help = CollectorTestFiles.Run("--help");
        Assert.Equal(0, help.Exit);
        Assert.Contains("--minimal", help.Output, StringComparison.Ordinal);
        Assert.Contains("--nur-lesen", help.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Exe_ist_klein_hat_die_eigene_Version_und_beide_Laufzeiten_in_der_Konfiguration()
    {
        Assert.True(new FileInfo(Exe).Length <= 150 * 1024, $"{new FileInfo(Exe).Length} Bytes");
        string version = File.ReadAllText(Path.Combine(RepoPaths.Src, "Riegel.Collector", "collector-version.txt")).Trim();
        var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(Exe);
        Assert.Equal(version, info.ProductVersion);
        string config = File.ReadAllText(Exe + ".config");
        int v4 = config.IndexOf("v4.0", StringComparison.Ordinal), v2 = config.IndexOf("v2.0.50727", StringComparison.Ordinal);
        Assert.True(v4 > 0 && v2 > v4, "supportedRuntime v4.0 vor v2.0.50727");
        using var pe = new PEReader(File.OpenRead(Exe));
        var reader = pe.GetMetadataReader();
        var resources = reader.ManifestResources.Select(h => reader.GetString(reader.GetManifestResource(h).Name)).ToList();
        Assert.Contains(Plan.ResourceName, resources);
        foreach (var code in new[] { "en", "de", "es", "fr", "zh-Hans" }) Assert.Contains($"messages.{code}.txt", resources);
        Assert.Equal(Subsystem.WindowsCui, pe.PEHeaders.PEHeader!.Subsystem);
        Assert.False(pe.PEHeaders.CorHeader!.Flags.HasFlag(CorFlags.Requires32Bit) || pe.PEHeaders.CorHeader.Flags.HasFlag(CorFlags.Prefers32Bit));
    }

    [Fact]
    public void Manifest_verlangt_keine_Rechteerhoehung()
    {
        string manifest = File.ReadAllText(Path.Combine(RepoPaths.Src, "Riegel.Collector", "app.manifest"));
        Assert.Contains("level=\"asInvoker\"", manifest, StringComparison.Ordinal);
    }

    public static TheoryData<string> Languages => new() { "en", "de", "es", "fr", "zh-Hans" };

    private static Dictionary<string, string> Table(string code)
    {
        using var reader = new StreamReader(Path.Combine(RepoPaths.Src, "Riegel.Collector", $"messages.{code}.txt"), Encoding.UTF8);
        return ConsoleMessages.Parse(reader);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Meldungstabellen_haben_dieselben_Schluessel(string code)
    {
        var english = Table("en");
        var other = Table(code);
        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), other.Keys.Order(StringComparer.Ordinal));
        foreach (var key in new[] { "TargetNotWritable", "UnknownOption", "WriteFailed", "PlanError", "Fatal", "SidecarFailed", "MissingValue" })
            Assert.StartsWith("RGL-01", other[key], StringComparison.Ordinal);
    }

    [Fact]
    public void Westeuropaeische_Meldungen_passen_in_die_OEM_Codepages_850_und_437()
    {
        // Deutsche, französische und spanische Konsolen nutzen meist Codepage 850: dort darf kein Text auf Englisch zurückfallen
        foreach (var code in new[] { "en", "de", "es", "fr" })
            Assert.True(ConsoleMessages.CanShow(Table(code), 850), $"messages.{code}.txt in Codepage 850");
        Assert.True(ConsoleMessages.CanShow(Table("de"), 437));
    }

    [Fact]
    public void Chinesisch_nur_wenn_die_Codepage_es_kann_sonst_Englisch()
    {
        Assert.False(ConsoleMessages.CanShow(Table("zh-Hans"), 850));
        Assert.True(ConsoleMessages.CanShow(Table("zh-Hans"), 65001));
        Assert.True(ConsoleMessages.CanShow(Table("zh-Hans"), 936));
    }

    [Theory]
    [InlineData("de-DE", "de")]
    [InlineData("de-CH", "de")]
    [InlineData("fr-CA", "fr")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("ja-JP", "en")]
    [InlineData("", "en")]
    public void Sprache_der_Meldungen(string culture, string expected) =>
        Assert.Equal(expected, ConsoleMessages.Language(CultureInfo.GetCultureInfo(culture)));
}
