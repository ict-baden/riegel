// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Infrastructure;

/// <summary>
/// Quelltext-Prüfungen (spec 7.4, 7.12, 8.13): Sammler und Windows-Anbindung schreiben nichts und starten nichts außer
/// über ToolRunner; kein ausgeliefertes Projekt öffnet Netzwerkverbindungen; Core startet keine Prozesse; der
/// gebaute net35-Sammler referenziert nur Assemblys der 2.0-Schicht; „Win32_Product“ kommt nirgends vor.
/// Geprüft wird der Code ohne Kommentare, Texte in Zeichenketten zählen mit.
/// </summary>
public sealed partial class SourceScanTests
{
    private static readonly string[] Shipped = ["Riegel.Core", "Riegel.Commands", "Riegel.Cli", "Riegel.Platform.Windows", "Riegel.Collector", "Riegel"];

    /// <summary>Verbotene Aufrufe und Verben im Sammler und in der Windows-Anbindung (spec 7.4).</summary>
    private static readonly (string Pattern, string Why)[] CollectorForbidden =
    [
        (@"\bCreateSubKey\b", "Registry schreiben"), (@"\bSetValue\b", "Registry schreiben"), (@"\bDeleteValue\b", "Registry löschen"),
        (@"\bDeleteSubKey(Tree)?\b", "Registry löschen"), (@"\bRegSetValue\w*", "Registry schreiben"), (@"\bRegCreateKey\w*", "Registry anlegen"),
        (@"\bRegDelete\w*", "Registry löschen"), (@"Win32_Product", "löst MSI-Reparaturen aus"), (@"\bServiceController\b", "Dienste steuern"),
        (@"\bXmlSerializer\b", "erzeugt Code zur Laufzeit"), (@"\bCSharpCodeProvider\b|System\.CodeDom", "Codeerzeugung"),
        (@"Reflection\.Emit", "Codeerzeugung"), (@"RegexOptions\.Compiled", "Codeerzeugung"),
        (@"\bSet-[A-Z]\w*", "ändernde PowerShell-Verben"), (@"\bReset-[A-Z]\w*", "ändernde PowerShell-Verben"),
        (@"(?<![\w-])/set\b", "auditpol/secedit setzen"), (@"/configure\b", "secedit /configure"), (@"(?i)\breg(\.exe)?\s+add\b", "reg add"),
        (@"(?i)\bgpupdate\b", "gpupdate"),
    ];

    /// <summary>Netzwerk-APIs, die kein ausgeliefertes Projekt benutzen darf (spec 7.12).</summary>
    private const string NetworkApis =
        @"\b(HttpClient|HttpClientHandler|SocketsHttpHandler|WebRequest|HttpWebRequest|WebClient|TcpClient|TcpListener|UdpClient|Socket|ClientWebSocket|Dns)\b|System\.Net\.(Http|Sockets|WebSockets)";

    [Fact]
    public void Sammler_und_Windows_Anbindung_enthalten_keine_aendernden_Aufrufe()
    {
        var findings = new List<string>();
        foreach (var (file, code) in Sources("Riegel.Collector", "Riegel.Platform.Windows"))
        {
            foreach (var (pattern, why) in CollectorForbidden)
                foreach (Match m in Regex.Matches(code, pattern))
                    findings.Add($"{Relative(file)}: „{m.Value}“ ({why})");
            var name = Path.GetFileName(file);
            if (!name.Equals("ToolRunner.cs", StringComparison.OrdinalIgnoreCase) && ProcessStart().IsMatch(code))
                findings.Add($"{Relative(file)}: Prozessstart außerhalb von ToolRunner.cs");
            if (!name.Contains("Workspace", StringComparison.OrdinalIgnoreCase) && !name.Equals("ToolRunner.cs", StringComparison.OrdinalIgnoreCase)
                && FileDelete().IsMatch(code))
                findings.Add($"{Relative(file)}: Löschen außerhalb des eigenen Arbeitsordners (nur in *Workspace*.cs oder ToolRunner.cs)");
        }
        foreach (var file in Files(Path.Combine(RepoPaths.Root, "tools", "collect-builtin"), "*.cmd"))
        {
            var text = File.ReadAllText(file);
            foreach (var (pattern, why) in CollectorForbidden.Where(p => p.Why is "auditpol/secedit setzen" or "secedit /configure" or "reg add" or "gpupdate"))
                foreach (Match m in Regex.Matches(text, pattern))
                    findings.Add($"{Relative(file)}: „{m.Value}“ ({why})");
        }
        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }

    [Fact]
    public void WMI_Methoden_nur_aus_der_Erlaubnisliste()
    {
        var plan = RiegelJson.ReadFile<CollectionPlan>(RepoPaths.CollectionPlan, 10 * 1024 * 1024);
        string[] allowed = ["GetConfiguration", "GetConversionStatus", "GetEncryptionMethod", "GetKeyProtectors", "GetProtectionStatus", "GetSecurityDescriptor"];
        Assert.Equal(allowed.Order(StringComparer.Ordinal), plan.AllowedWmiMethods.Order(StringComparer.Ordinal));
        foreach (var method in plan.Wmi.SelectMany(w => w.Methods))
            Assert.Contains(method.Name, allowed);
        var findings = new List<string>();
        foreach (var (file, code) in Sources("Riegel.Collector", "Riegel.Platform.Windows"))
            foreach (Match m in InvokeMethod().Matches(code))
                if (!allowed.Contains(m.Groups[1].Value)) findings.Add($"{Relative(file)}: WMI-Methode {m.Groups[1].Value}");
        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }

    [Fact]
    public void Kein_ausgeliefertes_Projekt_benutzt_Netzwerk_APIs()
    {
        var findings = new List<string>();
        foreach (var (file, code) in Sources(Shipped))
            foreach (Match m in Regex.Matches(code, NetworkApis))
                findings.Add($"{Relative(file)}: {m.Value}");
        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }

    [Fact]
    public void Core_startet_keine_Prozesse()
    {
        var findings = Sources("Riegel.Core").Where(s => ProcessStart().IsMatch(s.Code)).Select(s => Relative(s.File)).ToList();
        Assert.True(findings.Count == 0, "Prozessstart in Riegel.Core: " + string.Join(", ", findings));
    }

    [Fact]
    public void Win32_Product_kommt_nirgends_vor()
    {
        var hits = new List<string>();
        foreach (var root in new[] { RepoPaths.Src, RepoPaths.Contracts, Path.Combine(RepoPaths.Root, "tools"), RepoPaths.Rules })
            foreach (var file in Files(root, "*.*"))
                if (File.ReadAllText(file).Contains("Win32_Product", StringComparison.OrdinalIgnoreCase)) hits.Add(Relative(file));
        Assert.True(hits.Count == 0, "Win32_Product löst MSI-Selbstreparaturen aus: " + string.Join(", ", hits));
    }

    [Fact]
    public void Jede_cs_Datei_beginnt_mit_der_SPDX_Zeile()
    {
        var missing = new List<string>();
        foreach (var root in new[] { RepoPaths.Src, Path.Combine(RepoPaths.Root, "tests"), Path.Combine(RepoPaths.Root, "tools") })
            foreach (var file in Files(root, "*.cs"))
            {
                using var reader = new StreamReader(file);
                if (reader.ReadLine() != "// SPDX-License-Identifier: MIT") missing.Add(Relative(file));
            }
        Assert.True(missing.Count == 0, "Ohne „// SPDX-License-Identifier: MIT“ in Zeile 1: " + string.Join(", ", missing));
    }

    [Fact]
    public void Der_net35_Sammler_referenziert_nur_Assemblys_der_2_0_Schicht()
    {
        var exe = typeof(SourceScanTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RiegelCollectorExe").Value!;
        if (!File.Exists(exe))
        {
            // Unter Linux baut die CI den Sammler nicht (nur das net10.0-Ziel der Tests)
            Assert.False(OperatingSystem.IsWindows(), $"{exe} fehlt – das Windows-Ziel der Tests baut den Sammler mit.");
            return;
        }
        using var stream = File.OpenRead(exe);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        string[] allowed = ["mscorlib", "System", "System.Management", "System.Xml"];
        var references = reader.AssemblyReferences.Select(h => reader.GetAssemblyReference(h))
            .Select(r => (Name: reader.GetString(r.Name), r.Version)).ToList();
        Assert.NotEmpty(references);
        foreach (var (name, version) in references)
        {
            Assert.True(allowed.Contains(name), $"Der Sammler referenziert {name} – nur {string.Join(", ", allowed)} sind erlaubt (kein System.Core).");
            Assert.True(version.Major == 2, $"{name} {version}: erwartet ist die 2.0-Schicht (CLR 2 und CLR 4).");
        }
        Assert.True(new FileInfo(exe).Length <= 150 * 1024, "Sammler größer als 150 KB (spec 9).");
    }

    // ---------------------------------------------------------------- Helfer

    [GeneratedRegex(@"\bProcess\s*\.\s*Start\b|\bnew\s+Process\s*\(|\bProcessStartInfo\b")]
    private static partial Regex ProcessStart();

    [GeneratedRegex(@"\bFile\s*\.\s*Delete\b|\bDirectory\s*\.\s*Delete\b")]
    private static partial Regex FileDelete();

    [GeneratedRegex(@"InvokeMethod\s*\(\s*""(\w+)""")]
    private static partial Regex InvokeMethod();

    private static IEnumerable<string> Files(string root, string pattern)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
        {
            var parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(p => p is "bin" or "obj" or "node_modules" or ".git")) continue;
            yield return file;
        }
    }

    /// <summary>Quellen der Projekte (src/&lt;Projekt&gt;/**/*.cs) ohne Kommentare.</summary>
    internal static IReadOnlyList<(string File, string Code)> Sources(params string[] projects) =>
        [.. projects.SelectMany(p => Files(Path.Combine(RepoPaths.Src, p), "*.cs")).Select(f => (f, StripComments(File.ReadAllText(f))))];

    private static string Relative(string file) => Path.GetRelativePath(RepoPaths.Root, file).Replace('\\', '/');

    /// <summary>Entfernt // und /* */-Kommentare, lässt Zeichenketten (auch @"…", $"…" und """…""") stehen.</summary>
    internal static string StripComments(string code)
    {
        var sb = new StringBuilder(code.Length);
        int i = 0;
        while (i < code.Length)
        {
            char c = code[i];
            char next = i + 1 < code.Length ? code[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                while (i < code.Length && code[i] != '\n') i++;
                continue;
            }
            if (c == '/' && next == '*')
            {
                int end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? code.Length : end + 2;
                continue;
            }
            if (c == '"' && i + 2 < code.Length && code[i + 1] == '"' && code[i + 2] == '"')
            {
                int end = code.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                end = end < 0 ? code.Length : end + 3;
                sb.Append(code, i, end - i);
                i = end;
                continue;
            }
            if (c == '"' || c == '\'')
            {
                bool verbatim = i > 0 && (code[i - 1] == '@' || (i > 1 && code[i - 1] == '$' && code[i - 2] == '@') || (i > 1 && code[i - 1] == '@' && code[i - 2] == '$'));
                int j = i + 1;
                while (j < code.Length)
                {
                    if (verbatim && c == '"' && code[j] == '"')
                    {
                        if (j + 1 < code.Length && code[j + 1] == '"') { j += 2; continue; }
                        break;
                    }
                    if (!verbatim && code[j] == '\\') { j += 2; continue; }
                    if (code[j] == c || (!verbatim && code[j] == '\n')) break;
                    j++;
                }
                j = Math.Min(j + 1, code.Length);
                sb.Append(code, i, j - i);
                i = j;
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }
}
