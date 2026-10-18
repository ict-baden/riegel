// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Platform.Windows;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Windows.Collector;

/// <summary>
/// Der echte Sammler auf diesem Rechner – nur lesend (spec 8, „LocalReadOnly“): schreibt nur Export und .sha256 in
/// einen Temp-Ordner, räumt seinen Arbeitsordner ab, ist schema-gültig, und net35-Exe und net10-Anbindung liefern
/// dieselben Einträge. Mit --minimal, damit kein secedit/gpresult läuft; der volle Lauf steht im CI-Nachweis.
/// </summary>
[Trait("Category", "LocalReadOnly")]
public sealed class LocalReadOnlyTests
{
    private static string[] RiegelTempFolders() =>
        Directory.GetDirectories(Path.GetTempPath(), "riegel-*").Where(d => !Path.GetFileName(d).StartsWith("riegel-test-", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void Sammler_schreibt_nur_Export_und_Pruefsumme_und_raeumt_auf()
    {
        using var temp = new TempFolder();
        var before = RiegelTempFolders();
        var (exit, output, error) = CollectorTestFiles.Run("--out", temp.Path, "--minimal", "--quiet");
        Assert.True(exit is 0 or 1, $"Exitcode {exit}: {error}");
        var files = Directory.GetFiles(temp.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(2, files.Count);
        Assert.EndsWith(SnapshotFile.Extension, files[0]);
        Assert.Equal(files[0] + SnapshotFile.SidecarExtension, files[1]);
        Assert.Empty(RiegelTempFolders().Except(before));

        string path = Path.Combine(temp.Path, files[0]!);
        Assert.Equal(HashCheckStatus.Ok, SnapshotFile.Verify(path).Status);
        Assert.Contains(Hashes.Sha256File(path), output, StringComparison.Ordinal);
        string json = File.ReadAllText(path);
        SchemaValidator.AssertValid(json, files[0]!);
        var snapshot = RiegelJson.Deserialize<Snapshot>(File.ReadAllBytes(path));
        Assert.Equal(SnapshotOrigin.Collector, snapshot.Origin);
        Assert.True(snapshot.Options.Minimal);
        Assert.Equal(exit == 1, !snapshot.IsAdmin || snapshot.Items.Any(i => i.Status is ItemStatus.Denied or ItemStatus.Error));
        // --minimal: kein secedit, kein gpresult, keine Update-Abfrage, keine Firewall-Regeln
        foreach (var key in new[] { ItemKeys.RawSeceditInf, ItemKeys.RawGpresultXml, ItemKeys.WindowsUpdateHistory })
            Assert.Equal(ItemStatus.NotCollected, snapshot.Items.Single(i => ItemKeys.Same(i.Key, key)).Status);
        var firewall = snapshot.Items.Single(i => i.Key == ItemKeys.Firewall);
        if (firewall.Status == ItemStatus.Ok) Assert.False(firewall.Value!.Value.GetProperty("rulesCollected").GetBoolean());
        Assert.Equal(Hashes.Sha256File(CollectorTestFiles.CollectorExe), snapshot.Collector.ExeSha256);
    }

    [Fact]
    public void Net35_und_net10_liefern_dieselben_Eintraege()
    {
        using var temp = new TempFolder();
        var (exit, _, error) = CollectorTestFiles.Run("--out", temp.Path, "--minimal", "--quiet");
        Assert.True(exit is 0 or 1, error);
        var exe = RiegelJson.Deserialize<Snapshot>(File.ReadAllBytes(Directory.GetFiles(temp.Path, "*" + SnapshotFile.Extension).Single()));
        var app = new WindowsPlatform().CollectThisMachine(new CollectOptions(Minimal: true), CancellationToken.None);
        Assert.Equal(SnapshotOrigin.App, app.Origin);
        // Ob ein Dienst gerade läuft, kann sich zwischen den Läufen ändern; die Schlüssel und Status nicht
        var a = exe.Items.Select(i => $"{i.Key.ToLowerInvariant()}|{i.Status}").Order(StringComparer.Ordinal).ToList();
        var b = app.Items.Select(i => $"{i.Key.ToLowerInvariant()}|{i.Status}").Order(StringComparer.Ordinal).ToList();
        Assert.Equal(a, b);
        Assert.Equal(exe.Coverage.Select(c => c.Root), app.Coverage.Select(c => c.Root));
        string json = RiegelJson.Serialize(app);
        SchemaValidator.AssertValid(json, "CollectThisMachine");
    }

    [Fact]
    public void Voller_Lauf_raeumt_den_Arbeitsordner_von_secedit_und_gpresult_ab()
    {
        using var temp = new TempFolder();
        var before = RiegelTempFolders();
        var (exit, _, error) = CollectorTestFiles.Run("--out", temp.Path, "--quiet");
        Assert.True(exit is 0 or 1, error);
        Assert.Empty(RiegelTempFolders().Except(before));
        Assert.Equal(2, Directory.GetFiles(temp.Path).Length);
        var snapshot = RiegelJson.Deserialize<Snapshot>(File.ReadAllBytes(Directory.GetFiles(temp.Path, "*" + SnapshotFile.Extension).Single()));
        var secedit = snapshot.Items.Single(i => i.Key == ItemKeys.RawSeceditInf);
        if (snapshot.IsAdmin)
        {
            Assert.Equal(ItemStatus.Ok, secedit.Status);
            string inf = secedit.Value!.Value.GetProperty("text").GetString()!;
            Assert.Contains("[Privilege Rights]", inf, StringComparison.Ordinal);
            // Namen sind beim Erfassen in SIDs übersetzt
            foreach (var line in inf.Split('\n').SkipWhile(l => !l.StartsWith("[Privilege Rights]", StringComparison.Ordinal)).Skip(1).TakeWhile(l => !l.StartsWith('[')))
                if (line.Contains('=')) Assert.All(line[(line.IndexOf('=') + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), m => Assert.StartsWith("*S-", m, StringComparison.Ordinal));
        }
        else Assert.Equal(ItemStatus.Denied, secedit.Status);
    }

    [Fact]
    public void Abbruch_wird_beachtet()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => new WindowsPlatform().CollectThisMachine(new CollectOptions(Minimal: true), cts.Token));
    }

    [Fact]
    public void Pseudonymisiert_ohne_lokale_Benutzernamen()
    {
        using var temp = new TempFolder();
        var (exit, _, error) = CollectorTestFiles.Run("--out", temp.Path, "--minimal", "--quiet", "--pseudonymize");
        Assert.True(exit is 0 or 1, error);
        var snapshot = RiegelJson.Deserialize<Snapshot>(File.ReadAllBytes(Directory.GetFiles(temp.Path, "*" + SnapshotFile.Extension).Single()));
        Assert.True(snapshot.Options.Pseudonymized);
        var users = snapshot.Items.Single(i => i.Key == ItemKeys.NetUserEnum);
        if (users.Status != ItemStatus.Ok) return;
        foreach (var user in users.Value!.Value.EnumerateArray())
        {
            Assert.Matches("^user-[0-9a-f]{8}$", user.GetProperty("name").GetString()!);
            Assert.StartsWith("S-1-5-", user.GetProperty("sid").GetString()!, StringComparison.Ordinal);
        }
        string current = Environment.UserName;
        var accounts = snapshot.Items.Single(i => i.Key == @"wmi:root\cimv2:Win32_UserAccount");
        if (accounts.Status == ItemStatus.Ok)
            Assert.DoesNotContain(accounts.Value!.Value.GetProperty("rows").EnumerateArray(),
                r => string.Equals(r.GetProperty("Name").GetString(), current, StringComparison.OrdinalIgnoreCase));
    }
}
