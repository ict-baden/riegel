// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Windows.Collector;

/// <summary>
/// Aufgezeichnete Runner-Fixtures (spec 8.1): schema-gültig, Herkunft recorded, anonymisiert (keine echten Rechner-
/// SIDs, Hostnamen, Benutzer oder Gateways), mit den Rohdaten als Parser-Fixtures. Und der Anonymisierer selbst an
/// einer synthetischen Fixture.
/// </summary>
public sealed partial class RecordedFixtureTests
{
    private static readonly string[] Runners = ["windows-2022", "windows-2025", "windows-11-arm"];

    public static TheoryData<string> Snapshots()
    {
        var data = new TheoryData<string>();
        if (Directory.Exists(RepoPaths.RecordedFixtures))
            foreach (var file in Directory.EnumerateFiles(RepoPaths.RecordedFixtures, "*.snapshot.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                data.Add(Path.GetRelativePath(RepoPaths.RecordedFixtures, file));
        if (data.Count == 0) data.Add("");
        return data;
    }

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void Aufgezeichnete_Exporte_sind_gueltig_und_anonymisiert(string relative)
    {
        if (relative.Length == 0) return; // noch keine Aufzeichnung
        string path = Path.Combine(RepoPaths.RecordedFixtures, relative);
        string json = File.ReadAllText(path);
        SnapshotSchema.AssertValid(json, relative);
        var snapshot = RiegelJson.Deserialize<Snapshot>(File.ReadAllBytes(path));
        Assert.Equal(SnapshotOrigin.Recorded, snapshot.Origin);
        Assert.StartsWith("RUNNER-", snapshot.Machine.HostName, StringComparison.Ordinal);
        if (snapshot.Machine.MachineGuid is not null) Assert.Equal("00000000-0000-4000-8000-0000000000a1", snapshot.Machine.MachineGuid);
        AssertAnonymous(json, relative);
        Assert.Equal(relative.Contains("-nonadmin", StringComparison.Ordinal), !snapshot.IsAdmin);
        Assert.Equal(SnapshotOrigin.Recorded, snapshot.Origin);
        Assert.DoesNotMatch(@"WIN-[A-Z0-9]{11}", json);
    }

    [Fact]
    public void Jeder_Runner_hat_seine_Aufzeichnung_und_Rohdaten()
    {
        if (!Directory.Exists(RepoPaths.RecordedFixtures)) return; // noch keine Aufzeichnung
        foreach (var runner in Runners)
        {
            string admin = Path.Combine(RepoPaths.RecordedFixtures, $"{runner}-admin.snapshot.json");
            Assert.True(File.Exists(admin), $"{runner}-admin.snapshot.json fehlt");
            var snapshot = RiegelJson.Deserialize<Snapshot>(File.ReadAllBytes(admin));
            string raw = Path.Combine(RepoPaths.RecordedFixtures, "raw", runner);
            foreach (var name in new[] { "secedit.inf", "auditpol.csv" })
                Assert.True(File.Exists(Path.Combine(raw, name)), $"raw/{runner}/{name} fehlt");
            // gpresult nur, wo der Runner es erlaubt (auf windows-11-arm verweigert es auch Administratoren)
            bool gpresult = snapshot.Items.Single(i => i.Key == ItemKeys.RawGpresultXml).Status == ItemStatus.Ok;
            Assert.Equal(gpresult, File.Exists(Path.Combine(raw, "gpresult.xml")));
            foreach (var file in Directory.GetFiles(raw)) AssertAnonymous(File.ReadAllText(file), $"raw/{runner}/{Path.GetFileName(file)}");
        }
    }

    /// <summary>Keine Rechner-SIDs außer den Ersatz-SIDs, keine Profilnamen außer Pseudonymen, Gateways nur aus Dokumentationsnetzen.</summary>
    private static void AssertAnonymous(string text, string label)
    {
        foreach (Match m in DomainSid().Matches(text))
            Assert.True(m.Value is "S-1-5-21-1000-2000-3000" or "S-1-5-21-1100-2100-3100" or "S-1-5-21-1200-2200-3200", $"{label}: echte Domänen-SID {m.Value}");
        foreach (Match m in ProfilePath().Matches(text))
        {
            string name = m.Groups[1].Value;
            Assert.True(name is "Public" or "Default" or "Default User" or "All Users" || name.StartsWith("user-", StringComparison.Ordinal) || name.Contains('%'),
                $"{label}: Profilpfad mit Namen {name}");
        }
        Assert.DoesNotContain("runneradmin", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Anonymisierer_ersetzt_Namen_SIDs_und_Herkunft()
    {
        string dll = Path.Combine(RepoPaths.Root, "tools", "SnapshotAnonymizer", "bin", "Release", "net10.0", "SnapshotAnonymizer.dll");
        if (!File.Exists(dll)) dll = dll.Replace(@"\Release\", @"\Debug\", StringComparison.Ordinal);
        if (!File.Exists(dll)) return; // nur gebaut, wenn die ganze Lösung gebaut wurde
        using var temp = new TempFolder();
        var node = JsonNode.Parse(File.ReadAllText(RepoPaths.Synthetic("win11-24h2-workgroup-admin")))!.AsObject();
        node["origin"] = "collector";
        var items = node["items"]!.AsArray();
        // Ein Windows-Dienst, dessen Anzeigename wie ein Konto lautet („installer“), und gpresult mit dem Rechnernamen des Images
        var services = items.First(i => (string?)i!["key"] == @"wmi:root\cimv2:Win32_Service")!["value"]!["rows"]!.AsArray();
        services.Add(new JsonObject { ["Name"] = "msiserver", ["DisplayName"] = "Windows Installer", ["State"] = "Stopped", ["StartMode"] = "Manual",
            ["StartName"] = "LocalSystem", ["PathName"] = @"C:\Windows\system32\msiexec.exe /V" });
        items.Add(new JsonObject { ["key"] = "raw:gpresult-xml", ["status"] = "ok", ["durationMs"] = 1,
            ["value"] = new JsonObject { ["text"] = "<Rsop><ComputerResults><Name>WORKGROUP\\IMAGE-PC01$</Name></ComputerResults></Rsop>" } });
        // Eine fremde Rechner-SID, ein Benutzer mit Profilpfad und ein Fremddienst
        string json = node.ToJsonString().Replace("S-1-5-21-1000-2000-3000", "S-1-5-21-111-222-333", StringComparison.Ordinal)
            .Replace("\"operator\"", "\"installer\"", StringComparison.Ordinal);
        string input = Path.Combine(temp.Path, "in.snapshot.json");
        File.WriteAllText(input, json);
        string output = Path.Combine(temp.Path, "out.snapshot.json");
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { dll, input, output, "--host", "RUNNER-TEST" }) info.ArgumentList.Add(a);
        using (var process = Process.Start(info)!)
        {
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(60_000);
            Assert.True(process.ExitCode == 0, error);
        }
        string result = File.ReadAllText(output);
        var snapshot = RiegelJson.Deserialize<Snapshot>(File.ReadAllBytes(output));
        Assert.Equal(SnapshotOrigin.Recorded, snapshot.Origin);
        Assert.Equal("RUNNER-TEST", snapshot.Machine.HostName);
        Assert.DoesNotContain("W11-WG-01", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("S-1-5-21-111-222-333", result, StringComparison.Ordinal);
        Assert.Contains("S-1-5-21-1000-2000-3000-500", result, StringComparison.Ordinal);
        Assert.DoesNotContain("\"installer\"", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Windows Installer\"", result, StringComparison.Ordinal); // Wörter im Text bleiben
        Assert.DoesNotContain("IMAGE-PC01", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Beispiel-Leitsystem", result, StringComparison.Ordinal);
        Assert.Contains("Administrator", result, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"S-1-5-21-\d+-\d+-\d+")]
    private static partial Regex DomainSid();

    // im JSON-Text sind „\“ verdoppelt, in den Rohdateien nicht
    [GeneratedRegex(@"\\{1,2}Users\\{1,2}([^\\""\r\n;,|]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ProfilePath();
}
