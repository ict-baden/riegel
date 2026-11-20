// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using Riegel.Collector.Collection;
using Riegel.Core.Model;
using Riegel.Core.Services;
using Riegel.Platform.Windows;
using Riegel.Tests.Cli;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Windows.Collector;

/// <summary>
/// Windows-Anbindung ohne Rechteerhöhung und ohne Shell-Aufruf: Übergabeordner (Kennzeichen, vorhandener Ordner,
/// Reparse-Punkt), Auswertung nach dem erhöhten Lauf, welche Ziele die Shell öffnen darf, Sammler-Paket mit
/// UEBERGABE.txt. Nie UAC, nie die echte Übergabe – nur Temp-Ordner.
/// </summary>
public sealed class WindowsPlatformTests
{
    [Fact]
    public void Factory_liefert_die_Windows_Anbindung()
    {
        IPlatform platform = PlatformFactory.Create();
        Assert.True(platform.CanCollect);
        Assert.IsType<WindowsPlatform>(platform);
        Assert.Equal(MachineFacts.IsAdmin(), platform.IsElevated);
    }

    [Theory]
    [InlineData("abcdef12")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("a-b-c-d-e")]
    public void Gueltige_Kennzeichen(string token) => Handoff.ValidateToken(token);

    [Theory]
    [InlineData("")]
    [InlineData("kurz")]
    [InlineData(@"..\..\Windows")]
    [InlineData("abc/defgh")]
    [InlineData("abcdefgh:x")]
    [InlineData("abcdefgh ")]
    public void Ungueltige_Kennzeichen_werden_abgelehnt(string token)
    {
        var e = Assert.Throws<RiegelException>(() => Handoff.ValidateToken(token));
        Assert.Equal(RglCodesCollector.HandoffInvalidToken, e.Code);
    }

    [Fact]
    public void Uebergabeordner_wird_angelegt_und_nie_wiederverwendet()
    {
        using var temp = new TempFolder();
        string root = Path.Combine(temp.Path, "Riegel", "handoff");
        string dir = Handoff.CreateDirectory(root, "token-0001");
        Assert.True(Directory.Exists(dir));
        Assert.Equal(Path.Combine(root, "token-0001"), dir);
        var e = Assert.Throws<RiegelException>(() => Handoff.CreateDirectory(root, "token-0001"));
        Assert.Equal(RglCodesCollector.HandoffExists, e.Code);
    }

    [Fact]
    public void Reparse_Punkt_im_Pfad_wird_abgelehnt()
    {
        using var temp = new TempFolder();
        string target = temp.Sub("ziel");
        string link = Path.Combine(temp.Path, "verknuepfung");
        try
        {
            // Symbolische Verknüpfung nur im eigenen Temp-Ordner (keine Einstellung des Rechners); ohne Recht überspringen
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        var e = Assert.Throws<RiegelException>(() => Handoff.CreateDirectory(link, "token-0002"));
        Assert.Equal(RglCodesCollector.HandoffReparsePoint, e.Code);
        Assert.False(Directory.Exists(Path.Combine(target, "token-0002")));
    }

    [Fact]
    public void Auswertung_nach_dem_erhoehten_Lauf()
    {
        using var temp = new TempFolder();
        string root = temp.Sub("handoff");

        var missing = Handoff.Evaluate(root, "token-0003", 0);
        Assert.False(missing.Success);
        Assert.True(missing.DifferentUserProfile);
        Assert.Equal(RglCodesCollector.HandoffDifferentProfile, missing.ErrorCode);

        var failed = Handoff.Evaluate(root, "token-0004", 3);
        Assert.False(failed.DifferentUserProfile);
        Assert.Equal(RglCodesCollector.HandoffNoSnapshot, failed.ErrorCode);

        string dir = Handoff.CreateDirectory(root, "token-0005");
        Assert.Equal(RglCodesCollector.HandoffNoSnapshot, Handoff.Evaluate(root, "token-0005", 0).ErrorCode);
        string snapshot = Path.Combine(dir, "PC-01_20261018T090000Z.snapshot.json");
        File.WriteAllText(snapshot, "{}");
        Assert.False(Handoff.Evaluate(root, "token-0005", 0).Success); // ohne Begleitdatei
        File.WriteAllText(snapshot + ".sha256", new string('0', 64) + "  PC-01_20261018T090000Z.snapshot.json\n");
        var ok = Handoff.Evaluate(root, "token-0005", 1);
        Assert.True(ok.Success);
        Assert.Equal(snapshot, ok.SnapshotPath);
        Assert.Equal(1, ok.ExitCode);
    }

    [Fact]
    public void Ohne_Export_nennt_die_Meldung_Exitcode_und_hinterlegte_Ursache()
    {
        using var _ = new UiCulture("en");
        using var temp = new TempFolder();
        string root = temp.Sub("handoff");
        Assert.Equal(Riegel.Commands.Commands.CollectCommand.HandoffErrorFile, Handoff.ErrorFile);

        string dir = Handoff.CreateDirectory(root, "token-0006");
        var plain = Handoff.Evaluate(root, "token-0006", 3);
        Assert.Equal(RglCodesCollector.HandoffNoSnapshot, plain.ErrorCode);
        Assert.Contains("(exit code 3)", plain.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Cause", plain.Message, StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(dir, Handoff.ErrorFile), "RGL-0140" + Environment.NewLine);
        var withCause = Handoff.Evaluate(root, "token-0006", 3);
        Assert.Equal(RglCodesCollector.HandoffNoSnapshot, withCause.ErrorCode);
        Assert.EndsWith("Cause reported by the elevated process: RGL-0140.", withCause.Message, StringComparison.Ordinal);

        // nur genau ein Code, sonst nichts aus der Datei
        foreach (var bad in new[] { "RGL-0140 und mehr", "C:/Users/x", "RGL-14", "RGL-01a0", new string('x', 100) })
        {
            File.WriteAllText(Path.Combine(dir, Handoff.ErrorFile), bad);
            Assert.Null(Handoff.ReadCause(dir));
        }
        // kein Ordner und ein Fehlschlag: ebenfalls mit Exitcode
        Assert.Contains("(exit code 5)", Handoff.Evaluate(root, "token-0007", 5).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_oeffnet_nur_Links_Ordner_und_erzeugte_Dateiarten()
    {
        using var temp = new TempFolder();
        string Make(string name)
        {
            string path = Path.Combine(temp.Path, name);
            File.WriteAllText(path, "x");
            return path;
        }
        Assert.True(WindowsPlatform.CanOpen("https://github.com/ict-baden/riegel"));
        Assert.True(WindowsPlatform.CanOpen("http://localhost/"));
        Assert.True(WindowsPlatform.CanOpen(temp.Path));
        Assert.True(WindowsPlatform.CanOpen(Make("Bericht.pdf")));
        Assert.True(WindowsPlatform.CanOpen(Make("Uebersicht.xlsx")));
        Assert.True(WindowsPlatform.CanOpen(Make("results.csv")));
        foreach (var name in new[] { "riegel-remediation.reg", "x.exe", "x.cmd", "x.bat", "x.ps1", "x.lnk", "x.url", "x.js", "x.vbs", "x.msi", "x.hta", "x.scr" })
            Assert.False(WindowsPlatform.CanOpen(Make(name)), name);
        Assert.False(WindowsPlatform.CanOpen(Path.Combine(temp.Path, "fehlt.pdf")));
        Assert.False(WindowsPlatform.CanOpen("file:///C:/Windows/System32/calc.exe"));
        Assert.False(WindowsPlatform.CanOpen("ms-settings:windowsupdate"));
        Assert.False(WindowsPlatform.CanOpen(""));
        var e = Assert.Throws<RiegelException>(() => new WindowsPlatform().OpenWithShell(Make("y.reg")));
        Assert.Equal(RglCodesCollector.ShellOpenRefused, e.Code);
    }

    [Fact]
    public void Sammler_Paket_mit_Uebergabeblatt()
    {
        using var temp = new TempFolder();
        string source = temp.Sub("collector");
        File.Copy(CollectorTestFiles.CollectorExe, Path.Combine(source, "Riegel.Collector.exe"));
        File.WriteAllText(Path.Combine(source, "README.txt"), "readme");
        string target = Path.Combine(temp.Path, "usb");
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            string result = CollectorBundle.Export(source, target, new DateTime(2026, 10, 18, 9, 30, 0, DateTimeKind.Utc));
            Assert.Equal(Path.GetFullPath(target), result);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
        Assert.True(File.Exists(Path.Combine(target, "Riegel.Collector.exe")));
        byte[] bytes = File.ReadAllBytes(Path.Combine(target, CollectorBundle.HandoverFile));
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        string text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("Übergabeblatt", text, StringComparison.Ordinal);
        Assert.Contains(Hashes.Sha256File(Path.Combine(target, "Riegel.Collector.exe")) + "  Riegel.Collector.exe", text, StringComparison.Ordinal);
        Assert.Contains("2026-10-18 09:30 UTC", text, StringComparison.Ordinal);
        Assert.Contains("\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\r", text, StringComparison.Ordinal);

        var exists = Assert.Throws<RiegelException>(() => CollectorBundle.Export(source, target, DateTime.UtcNow));
        Assert.Equal(RglCodesCollector.CollectorBundleTargetExists, exists.Code);
        var missing = Assert.Throws<RiegelException>(() => CollectorBundle.Export(Path.Combine(temp.Path, "fehlt"), temp.Sub("leer"), DateTime.UtcNow));
        Assert.Equal(RglCodesCollector.CollectorBundleMissing, missing.Code);
    }

    [Fact]
    public void Uebergabeblatt_in_allen_Sprachen()
    {
        foreach (var code in new[] { "en", "de", "es", "fr", "zh-Hans" })
        {
            string text = CollectorBundle.Handover("0.1.0", [("Riegel.Collector.exe", new string('a', 64))], DateTime.UtcNow, CultureInfo.GetCultureInfo(code));
            Assert.Contains("0.1.0", text, StringComparison.Ordinal);
            Assert.Contains("Riegel.Collector.exe --out", text, StringComparison.Ordinal);
            Assert.Contains("%TEMP%", text, StringComparison.Ordinal);
            Assert.Contains("SOC", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Fehlertexte_fuer_jeden_Code_der_Anbindung()
    {
        foreach (var code in RglCodesCollector.All.Where(c => string.CompareOrdinal(c, "RGL-0110") >= 0))
            Assert.NotEqual(code, WindowsPlatform.Message(code, "x", 1));
        Assert.Equal(RglCodesCollector.All.Length, RglCodesCollector.All.Distinct().Count());
        Assert.All(RglCodesCollector.All, c => Assert.InRange(int.Parse(c[4..], CultureInfo.InvariantCulture), RglCodesCollector.First, RglCodesCollector.Last));
    }

    [Fact]
    public void Erfassungsdaten_werden_zum_Vertragstyp()
    {
        var data = new SnapshotData { CollectorName = "Riegel", CollectorVersion = "0.1.0", Clr = "10.0.0", Origin = "app", IsAdmin = false };
        data.Machine.HostName = "PC-01";
        data.Items.Add(CollectedItem.Ok("api:netusermodals", new JObject().Add("minPwLen", 12L)));
        var snapshot = WindowsPlatform.ToSnapshot(data);
        Assert.Equal(SnapshotOrigin.App, snapshot.Origin);
        Assert.Equal(12, snapshot.Items[0].Value!.Value.GetProperty("minPwLen").GetInt32());
        Assert.False(snapshot.IsAdmin);
    }
}
