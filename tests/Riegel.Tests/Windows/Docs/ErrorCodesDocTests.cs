// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Windows.Docs;

/// <summary>
/// docs/error-codes.md wird aus den Klassen RglCodes* erzeugt (spec 7.13): je Bereich die Konstanten mit ihrem
/// englischen Text aus der Sprachdatei des Pakets. Der Test vergleicht die Datei mit der erzeugten Fassung; mit
/// <c>RIEGEL_UPDATE_DOCS=1</c> schreibt er sie neu. Nur unter Windows, weil Sammler, Windows-Anbindung und App dazugehören.
/// </summary>
public sealed class ErrorCodesDocTests
{
    private sealed record Area(string Range, string Owner, Type Codes, Func<string, string?> Text);

    private static ResourceManager Resources(Assembly assembly, string baseName) => new(baseName, assembly);

    private static string? Res(ResourceManager rm, string code) =>
        rm.GetString("Error_" + code.Replace("-", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);

    private static IReadOnlyList<Area> Areas()
    {
        var core = typeof(Riegel.Core.Model.RglCodesCommon).Assembly;
        var commands = typeof(Riegel.Commands.RglCodesCli).Assembly;
        var platform = typeof(Riegel.Platform.Windows.PlatformFactory).Assembly;
        var app = typeof(Riegel.RglCodesApp).Assembly;
        var ruleTool = Assembly.LoadFrom(Path.Combine(RepoPaths.Root, "tools", "RuleTool", "bin", Configuration, "net10.0", "RuleTool.dll"));
        var catalogue = ruleTool.GetType("Riegel.Tools.RuleTool.RglCodesCatalogue", throwOnError: false)
            ?? ruleTool.GetTypes().Single(t => t.Name == "RglCodesCatalogue");
        var catalogueMessage = catalogue.GetMethod("Message", [typeof(string)])!;
        return
        [
            new("RGL-0001–0099", "common", typeof(Riegel.Core.Model.RglCodesCommon), c => Res(Resources(core, "Riegel.Core.Resources.CommonStrings"), c)),
            new("RGL-0100–0199", "collector and Windows platform", platform.GetTypes().Single(t => t.Name == "RglCodesCollector"),
                c => Res(Resources(platform, "Riegel.Platform.Windows.Resources.PlatformStrings"), c) ?? CollectorText(c)),
            new("RGL-0200–0249", "engine (catalogue, facts, evaluation)", typeof(Riegel.Core.Evaluation.RglCodesEngine), c => Res(Resources(core, "Riegel.Core.Resources.EngineStrings"), c)),
            new("RGL-0250–0299", "parsing (snapshots, built-in bundles, baselines)", typeof(Riegel.Core.Parsing.RglCodesParsing), c => Res(Resources(core, "Riegel.Core.Resources.ParsingStrings"), c)),
            new("RGL-0300–0399", "catalogue content and RuleTool (developer tool)", catalogue, c => (string?)catalogueMessage.Invoke(null, [c])),
            new("RGL-0400–0449", "workflow (plant folder, runs, registers)", typeof(Riegel.Core.Plants.RglCodesWorkflow), c => Res(Resources(core, "Riegel.Core.Resources.WorkflowStrings"), c)),
            new("RGL-0450–0499", "command line", typeof(Riegel.Commands.RglCodesCli), c => Res(Resources(commands, "Riegel.Commands.Resources.CliStrings"), c)),
            new("RGL-0500–0549", "PDF documents", typeof(Riegel.Core.Reports.Pdf.RglCodesPdf), c => Res(Resources(core, "Riegel.Core.Resources.PdfStrings"), c)),
            new("RGL-0550–0599", "data outputs (Excel, CSV, evidence pack, remediation)", typeof(Riegel.Core.Evidence.RglCodesDataOutputs), c => Res(Resources(core, "Riegel.Core.Resources.DataOutputStrings"), c)),
            new("RGL-0600–0699", "app (user interface)", typeof(Riegel.RglCodesApp), c => Res(Resources(app, "Riegel.Resources.UiStrings"), c)),
        ];
    }

    /// <summary>
    /// Texte des portablen Sammlers stehen in messages.en.txt („Schlüssel=RGL-nnnn: Text“), die der Bordmittel in
    /// collect-builtin.cmd („echo RGL-nnnn: englisch / deutsch“).
    /// </summary>
    private static string? CollectorText(string code)
    {
        var prefix = code + ": ";
        foreach (var line in File.ReadLines(Path.Combine(RepoPaths.Src, "Riegel.Collector", "messages.en.txt")))
        {
            int eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && line[(eq + 1)..].StartsWith(prefix, StringComparison.Ordinal)) return line[(eq + 1 + prefix.Length)..];
        }
        foreach (var line in File.ReadLines(Path.Combine(RepoPaths.Root, "tools", "collect-builtin", "collect-builtin.cmd")))
        {
            int at = line.IndexOf("echo " + prefix, StringComparison.Ordinal);
            if (at >= 0) return line[(at + 5 + prefix.Length)..].Split(" / ")[0].Trim() + " (collect-builtin.cmd)";
        }
        return null;
    }

    private static string Configuration =>
        typeof(ErrorCodesDocTests).Assembly.Location.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ? "Debug" : "Release";

    /// <summary>Alle Codes eines Bereichs mit dem Namen ihrer Konstante (Reihenfolge der Liste All).</summary>
    private static List<(string Code, string Name)> Codes(Type type)
    {
        var names = type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .GroupBy(f => (string)f.GetRawConstantValue()!).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        var member = (MemberInfo?)type.GetProperty("All", BindingFlags.Public | BindingFlags.Static) ?? type.GetField("All", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(type.Name + ": All fehlt");
        var all = (IEnumerable<string>)(member is PropertyInfo p ? p.GetValue(null)! : ((FieldInfo)member).GetValue(null)!);
        return [.. all.Select(c => (c, names.TryGetValue(c, out var n) ? n : "?"))];
    }

    internal static string Generate()
    {
        var sb = new StringBuilder();
        sb.Append("[Riegel](../README.md) › Error codes\n\n# Error codes\n\n");
        var areas = Areas();
        sb.Append("**Contents:** ").Append(string.Join(" · ", areas.Select(a => $"[{a.Range}](#{Anchor(a.Range)})"))).Append("\n\n");
        sb.Append("Every message Riegel shows for an error or a reason starts with a code `RGL-nnnn`. The code is the same in every\n")
          .Append("language; the text below is the English one (the program shows it in the language of the user interface).\n")
          .Append("`{0}`, `{1}` … are filled in with details such as a file name. Reason codes of results (for example\n")
          .Append("\"not checkable\" because an item was denied) are in the engine range. This page is generated from the source code\n")
          .Append("(`tests/Riegel.Tests/Windows/Docs/ErrorCodesDocTests.cs`, `RIEGEL_UPDATE_DOCS=1 dotnet test`); a test keeps it current.\n\n");
        foreach (var area in areas)
        {
            sb.Append("## ").Append(area.Range).Append("\n\n").Append(char.ToUpperInvariant(area.Owner[0])).Append(area.Owner[1..]).Append(".\n\n");
            sb.Append("| Code | Name | Meaning |\n|---|---|---|\n");
            foreach (var (code, name) in Codes(area.Codes))
            {
                var text = (area.Text(code) ?? "").Replace("\r\n", " ").Replace('\n', ' ').Replace("|", "\\|", StringComparison.Ordinal).Trim();
                sb.Append("| `").Append(code).Append("` | ").Append(name).Append(" | ").Append(text).Append(" |\n");
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string Anchor(string heading) =>
        new string([.. heading.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is '-' or ' ')]).Replace(' ', '-');

    [Fact]
    public void Jeder_Code_hat_einen_englischen_Text_und_liegt_in_seinem_Bereich()
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var area in Areas())
        {
            var bounds = area.Range[4..].Split('–').Select(s => int.Parse(s.Replace("RGL-", ""), CultureInfo.InvariantCulture)).ToArray();
            foreach (var (code, _) in Codes(area.Codes))
            {
                if (!seen.Add(code)) problems.Add($"{code}: doppelt");
                int n = int.Parse(code[4..], CultureInfo.InvariantCulture);
                if (n < bounds[0] || n > bounds[1]) problems.Add($"{code}: außerhalb {area.Range}");
                if (string.IsNullOrWhiteSpace(area.Text(code))) problems.Add($"{code}: kein englischer Text");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Fehlercode_Seite_ist_aktuell()
    {
        var path = Path.Combine(RepoPaths.Root, "docs", "error-codes.md");
        var generated = Generate();
        if (Environment.GetEnvironmentVariable("RIEGEL_UPDATE_DOCS") == "1")
            File.WriteAllText(path, generated, new UTF8Encoding(false));
        Assert.True(File.Exists(path), "docs/error-codes.md fehlt – mit RIEGEL_UPDATE_DOCS=1 erzeugen.");
        Assert.True(File.ReadAllText(path).Replace("\r\n", "\n") == generated,
            "docs/error-codes.md weicht von den RglCodes-Klassen ab – mit RIEGEL_UPDATE_DOCS=1 neu erzeugen.");
    }
}
