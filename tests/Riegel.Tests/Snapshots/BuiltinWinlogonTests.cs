// SPDX-License-Identifier: MIT
using System.Text;
using Riegel.Core.Model;
using Riegel.Core.Services;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Snapshots;

/// <summary>
/// Regressionstest (Review evidence-3): ein fehlendes AutoAdminLogon in <c>winlogon.txt</c> ist nur dann „nicht
/// gesetzt“, wenn <c>exitcodes.txt</c> den Lauf in der 64-Bit-Ansicht belegt (<c>wow64=0</c>) und die Abfrage
/// errorlevel 1 meldet. Ein 32-Bit-cmd auf 64-Bit-Windows liest WOW6432Node, wo der Wert fehlt; vorher ergab das
/// LOGON-10 (hoch) „erfüllt“ mit „Standardwert angenommen“.
/// </summary>
public sealed class BuiltinWinlogonTests
{
    private static RuleResult Logon10(string bundle)
    {
        var services = RiegelServices.Create(new NullPlatform(), new FixedClock());
        var snapshot = services.Snapshots.FromBuiltinBundle(bundle, ReadLimits.Default);
        var run = services.Runs.EvaluateOnly(snapshot, new RunOptions(Language: "en"));
        return run.Results.Single(r => r.RuleId == "LOGON-10");
    }

    /// <summary>Kopie des Windows-7-Pakets ohne AutoAdminLogon in winlogon.txt und mit den angegebenen Zeilen in exitcodes.txt.</summary>
    private static string BundleWithoutAutoAdminLogon(TempFolder temp, params string[] codes)
    {
        var source = Path.Combine(RepoPaths.SyntheticFixtures, "PG-ALT-01_20261010.riegel-builtin");
        var target = Path.Combine(temp.Path, "PG-ALT-01_20261010.riegel-builtin");
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
        // Aufbau wie collect-builtin.cmd heute: CurrentVersion nur per reg query /v (kein rekursiver Export, der Winlogon
        // mit abdecken würde), Winlogon nur aus winlogon.txt
        File.Delete(Path.Combine(target, "reg", "10-currentversion.reg"));
        File.WriteAllText(Path.Combine(target, "currentversion.txt"),
            "\r\nHKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\r\n    CurrentBuild    REG_SZ    7601\r\n\r\n" +
            "\r\nHKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\r\n    EditionID    REG_SZ    Professional\r\n\r\n" +
            "\r\nHKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\r\n    InstallationType    REG_SZ    Client\r\n\r\n", Encoding.ASCII);
        var winlogon = Path.Combine(target, "winlogon.txt");
        var lines = File.ReadAllText(winlogon, Encoding.ASCII).Split("\r\n").Where(l => !l.Contains("AutoAdminLogon", StringComparison.Ordinal));
        File.WriteAllText(winlogon, string.Join("\r\n", lines), Encoding.ASCII);
        var exitCodes = Path.Combine(target, "exitcodes.txt");
        var kept = File.ReadAllLines(exitCodes).Where(l => !l.StartsWith("winlogon", StringComparison.Ordinal) && !l.StartsWith("wow64", StringComparison.Ordinal));
        File.WriteAllText(exitCodes, string.Join("\r\n", kept.Concat(codes)) + "\r\n", Encoding.ASCII);
        return target;
    }

    [Fact]
    public void Fehlender_Wert_aus_einem_32_Bit_Lauf_ist_nicht_pruefbar()
    {
        using var temp = new TempFolder();
        var result = Logon10(BundleWithoutAutoAdminLogon(temp, "wow64=1", "winlogon-autoadminlogon=1"));
        Assert.Equal(ResultStatus.NotCheckable, result.Status);
    }

    [Fact]
    public void Fehlender_Wert_ohne_Angabe_der_Ansicht_ist_nicht_pruefbar()
    {
        using var temp = new TempFolder();
        // Fall aus dem Review: winlogon-autoadminlogon=1, keine Angabe zur 32/64-Bit-Ansicht
        var result = Logon10(BundleWithoutAutoAdminLogon(temp, "winlogon-autoadminlogon=1"));
        Assert.Equal(ResultStatus.NotCheckable, result.Status);
        Assert.Equal(ResultFlags.None, result.Flags & ResultFlags.FromDefault);
    }

    [Fact]
    public void Fehlender_Wert_aus_der_64_Bit_Ansicht_ist_nicht_gesetzt()
    {
        using var temp = new TempFolder();
        var result = Logon10(BundleWithoutAutoAdminLogon(temp, "wow64=0", "winlogon-autoadminlogon=1"));
        Assert.Equal(ResultStatus.Pass, result.Status);
        Assert.NotEqual(ResultFlags.None, result.Flags & ResultFlags.FromDefault);
    }

    [Fact]
    public void Andere_Exitcodes_der_Abfrage_sind_nicht_pruefbar()
    {
        using var temp = new TempFolder();
        var result = Logon10(BundleWithoutAutoAdminLogon(temp, "wow64=0", "winlogon-autoadminlogon=2"));
        Assert.Equal(ResultStatus.NotCheckable, result.Status);
    }

    [Fact]
    public void Collect_builtin_startet_sich_als_64_Bit_cmd_neu_und_vermerkt_die_Ansicht()
    {
        var cmd = File.ReadAllText(Path.Combine(RepoPaths.Root, "tools", "collect-builtin", "collect-builtin.cmd"));
        Assert.Contains("if not defined PROCESSOR_ARCHITEW6432 goto native", cmd, StringComparison.Ordinal);
        Assert.Contains(@"""%SystemRoot%\Sysnative\cmd.exe"" /d /c call ""%~f0"" %*", cmd, StringComparison.Ordinal);
        Assert.Contains(@">>""%CODES%"" echo wow64=%WOW64%", cmd, StringComparison.Ordinal);
    }
}
