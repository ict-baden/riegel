// SPDX-License-Identifier: MIT
using System.Text;
using System.Text.RegularExpressions;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Windows.Collector;

/// <summary>
/// Doku und Bordmittel im Gleichlauf: die Liste der erfassten Einträge in docs/collector-change-request.md wird aus
/// dem Plan erzeugt (mit RIEGEL_UPDATE_DOCS=1 schreibt der Test sie neu), die Befehle von collect-builtin.cmd stehen
/// auch in docs/collector.md, und die Bordmittel geben DefaultPassword nie aus.
/// </summary>
public sealed partial class CollectorDocsTests
{
    private const string Start = "<!-- plan-items:start (generated from contracts/collection-plan.json by CollectorDocsTests; do not edit) -->";
    private const string End = "<!-- plan-items:end -->";

    private static string Docs(string name) => Path.Combine(RepoPaths.Root, "docs", name);

    private static string Cmd => File.ReadAllText(Path.Combine(RepoPaths.Root, "tools", "collect-builtin", "collect-builtin.cmd"));

    /// <summary>Markdown der Planeinträge (Abschnitt „Every item read“).</summary>
    internal static string PlanSection(CollectionPlan plan)
    {
        var sb = new StringBuilder();
        string Mode(PlanMode m) => m switch { PlanMode.SkipInMinimal => "not with `--minimal`", PlanMode.DeepOnly => "only with `--deep`", _ => "always" };
        sb.AppendLine(FormattableString.Invariant($"Collection plan `{plan.Schema}` version {plan.Version}. Limits: at most {plan.Limits.MaxValuesTotal:N0} registry values in total, recursion depth {plan.Limits.RecursiveMaxDepth}, binary values up to {plan.Limits.MaxBinaryBytes:N0} bytes, tool timeout {plan.Limits.ToolTimeoutSeconds} s, raw texts up to {plan.Limits.MaxRawTextBytes / 1024 / 1024} MB, the newest {plan.Limits.WindowsUpdateHistoryEntries} Windows Update history entries."));
        sb.AppendLine();
        sb.AppendLine("**Registry (read through the 64-bit view, `KEY_READ` only)**");
        sb.AppendLine();
        sb.AppendLine("| Key | Scope | Values | Admin |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var r in plan.Registry.Roots.Concat(plan.Registry.Keys))
        {
            string scope = r.Kind switch { RegistryPlanKind.Recursive => $"key and subkeys, depth {r.MaxDepth}", RegistryPlanKind.Children => "key and one level of subkeys", _ => "this key only" };
            string values = r.Values is { Count: > 0 } v ? string.Join(", ", v.Select(x => $"`{x}`")) : "all";
            sb.AppendLine($"| `{r.Path}` | {scope} | {values} | {(r.NeedsAdmin ? "yes" : "no")} |");
        }
        sb.AppendLine();
        sb.AppendLine("**WMI (queries and the allowed read-only methods)**");
        sb.AppendLine();
        sb.AppendLine("| Namespace and class | Filter | Properties | Methods | Admin |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var w in plan.Wmi)
        {
            string methods = w.Methods.Count == 0 ? "–" : string.Join(", ", w.Methods.Select(m => $"`{m.Name}`" + (m.ViaClass is null ? "" : $" (on `{m.ViaClass}`)")));
            sb.AppendLine($"| `{w.Namespace}:{w.Class}` | {(w.Where is null ? "–" : $"`{w.Where}`")} | {string.Join(", ", w.Properties)} | {methods} | {(w.NeedsAdmin ? "yes" : "no")} |");
        }
        sb.AppendLine();
        sb.AppendLine("**Windows APIs**");
        sb.AppendLine();
        sb.AppendLine("| Item | Source | When | Admin |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var a in plan.Api)
        {
            string source = a.Api switch
            {
                "netusermodals" => "`NetUserModalsGet` levels 0 and 3",
                "netuserenum" => "`NetUserEnum` level 2 (names, SIDs, flags, last logon as a date only)",
                "lsa-rights" => $"`LsaOpenPolicy` + `LsaEnumerateAccountsWithUserRight` for {a.Rights?.Count} rights (SIDs only)",
                "localgroup" => $"`NetLocalGroupGetMembers` of `{a.Sid}` (found by SID)",
                "audit-policy" => "`AuditQuerySystemPolicy` (after enabling `SeSecurityPrivilege` in the collector's own token)",
                "lsass-protection" => "`OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `NtQueryInformationProcess` (protection level of LSASS)",
                "firewall" => "`HNetCfg.FwPolicy2` (profiles; the rule list not with `--minimal`)",
                "windows-update-history" => "`Microsoft.Update.Session` `QueryHistory`, only if `wuauserv` is already running",
                "winhttp-proxy" => "`WinHttpGetDefaultProxyConfiguration`",
                "wsc-av" => "Windows Security Center API `IWSCProductList` (Windows 8 and later clients)",
                _ => $"`{a.Api}`",
            };
            sb.AppendLine($"| `{a.Key}` | {source} | {Mode(a.Mode)} | {(a.NeedsAdmin ? "yes" : "no")} |");
        }
        sb.AppendLine();
        sb.AppendLine("**Event log**");
        sb.AppendLine();
        foreach (var e in plan.Events)
            sb.AppendLine($"- `{e.Key}`: newest event {e.EventId} of `{e.Provider}` in the {e.Log} log after the last boot, `EvtQuery` with `{e.Xpath}`");
        sb.AppendLine();
        sb.AppendLine("**Raw data**");
        sb.AppendLine();
        foreach (var r in plan.Raw)
        {
            string what = r.Kind == RawKind.Tool ? $"`{r.Tool} {string.Join(' ', r.Arguments ?? [])}` in the collector's temporary folder" : $"the file `{r.File}` (read, never changed)";
            sb.AppendLine(FormattableString.Invariant($"- `{r.Key}`: {what}; {Mode(r.Mode)}; up to {r.MaxBytes / 1024 / 1024} MB"));
        }
        sb.AppendLine();
        sb.AppendLine($"Allowed WMI methods (all read-only): {string.Join(", ", plan.AllowedWmiMethods.Select(m => $"`{m}`"))}.");
        return sb.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public void Aenderungsantrag_listet_jeden_Planeintrag()
    {
        var plan = RiegelJson.ReadFile<CollectionPlan>(RepoPaths.CollectionPlan, 10 * 1024 * 1024);
        string expected = PlanSection(plan);
        string path = Docs("collector-change-request.md");
        string doc = File.ReadAllText(path).Replace("\r\n", "\n");
        int s = doc.IndexOf(Start, StringComparison.Ordinal), e = doc.IndexOf(End, StringComparison.Ordinal);
        Assert.True(s >= 0 && e > s, "Markierungen der Planliste fehlen in docs/collector-change-request.md");
        string actual = doc[(s + Start.Length)..e].Trim('\n') + "\n";
        if (actual != expected && Environment.GetEnvironmentVariable("RIEGEL_UPDATE_DOCS") == "1")
        {
            File.WriteAllText(path, doc[..(s + Start.Length)] + "\n" + expected + doc[e..], new UTF8Encoding(false));
            return;
        }
        Assert.True(actual == expected, "docs/collector-change-request.md weicht vom Plan ab – mit RIEGEL_UPDATE_DOCS=1 neu erzeugen.\n" + expected);
        foreach (var w in plan.Wmi) Assert.Contains(w.Class, actual, StringComparison.Ordinal);
    }

    [Fact]
    public void Jeder_Befehl_der_Bordmittel_steht_auch_in_der_Doku()
    {
        string doc = File.ReadAllText(Docs("collector.md"));
        var commands = BuiltinCommand().Matches(Cmd).Select(m => Normalize(m.Value)).Distinct().ToList();
        Assert.True(commands.Count >= 18, $"nur {commands.Count} Befehle gefunden");
        string normalizedDoc = Normalize(doc);
        foreach (var command in commands) Assert.True(normalizedDoc.Contains(command, StringComparison.OrdinalIgnoreCase), $"docs/collector.md nennt nicht: {command}");
    }

    [Fact]
    public void Bordmittel_lesen_nur_und_geben_kein_Kennwort_aus()
    {
        string cmd = Cmd;
        Assert.Contains("\r\n", cmd, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", cmd.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.All(cmd, c => Assert.True(c < 128, "nur ASCII (Codepages von XP bis 11)"));
        // DefaultPassword nur als vorhanden/nicht vorhanden – die Abfrage geht nach nul
        foreach (Match m in Regex.Matches(cmd, @"^.*DefaultPassword.*$", RegexOptions.Multiline))
            if (m.Value.Contains("reg query", StringComparison.OrdinalIgnoreCase)) Assert.Contains(">nul 2>&1", m.Value, StringComparison.Ordinal);
        // Dienste nur mit /v Start; kein rekursiver Export von CurrentVersion (enthielte Winlogon)
        Assert.Contains(@"reg query ""HKLM\SYSTEM\CurrentControlSet\Services"" /s /v Start", cmd, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"reg export ""HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion""", cmd);
        Assert.DoesNotMatch(@"reg export ""HKLM\\SYSTEM\\CurrentControlSet\\Services""\s", cmd);
        foreach (Match m in Regex.Matches(cmd, @"^(?!rem )\s*(\w+(\.exe)?)\s", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            string program = m.Groups[1].Value.ToLowerInvariant();
            Assert.True(program is "secedit" or "auditpol" or "reg" or "hostname" or "ver" or "set" or "if" or "for" or "call" or "goto" or "echo" or "mkdir" or "endlocal" or "setlocal" or "exit" or "rem",
                $"unerwartetes Programm in collect-builtin.cmd: {program}");
        }
    }

    [Fact]
    public void Doku_Seiten_haben_Brotkrumen_und_Inhalt()
    {
        foreach (var name in new[] { "collector.md", "collector-change-request.md" })
        {
            string[] lines = File.ReadAllLines(Docs(name));
            Assert.StartsWith("[Riegel](../README.md) › ", lines[0], StringComparison.Ordinal);
            Assert.Contains(lines.Take(8), l => l.StartsWith("**Contents:**", StringComparison.Ordinal));
        }
    }

    private static string Normalize(string s) => Regex.Replace(s.Replace("%BUNDLE%", "<bundle>").Replace("%WL%", @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"), @"\s+", " ").Trim();

    // secedit/auditpol/reg-Zeilen (ohne Umleitungen), so wie man sie abtippt
    [GeneratedRegex(@"^(secedit /export[^\r\n>]*|auditpol /backup[^\r\n>]*|reg (export|query) ""[^""]+""( /s)?( /v \w+)?)", RegexOptions.Multiline)]
    private static partial Regex BuiltinCommand();
}
