// SPDX-License-Identifier: MIT
#if WINDOWS
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Cli;

/// <summary>
/// Das gebaute Riegel.Cli.exe als eigener Prozess (Paket cli, Abnahme; spec 4.2): Exitcodes 0, 1, 2 und 3 kommen in
/// PowerShell als $LASTEXITCODE und in cmd als %ERRORLEVEL% an; umgeleitete Ausgabe ist UTF-8 ohne BOM. Die Fälle
/// hängen nicht vom Stand der Engine ab: Fixtures plus feste Token-Listen (--critical/--warning). Nur lesend: ein
/// synthetischer Export wird ausgewertet, nichts gespeichert, kein UAC (Manifest asInvoker).
/// </summary>
public sealed class ConsoleHostTests
{
    /// <summary>(erwarteter Exitcode, Argumente)</summary>
    public static TheoryData<int, string[]> Cases()
    {
        var admin = RepoPaths.Synthetic("win11-24h2-workgroup-admin");
        var nonAdmin = RepoPaths.Synthetic("win11-24h2-workgroup-nonadmin");
        return new TheoryData<int, string[]>
        {
            { 0, ["--version"] },
            { 0, ["check", "--snapshot", admin, "--critical", "none", "--warning", "none"] },
            // nicht als Administrator erfasst: Token „incomplete“ (spec 2.4 Punkt 9)
            { 1, ["check", "--snapshot", nonAdmin, "--critical", "none", "--warning", "incomplete"] },
            { 2, ["check", "--snapshot", nonAdmin, "--critical", "incomplete", "--warning", "none", "--brief"] },
            { 3, ["check", "--snapshot", Path.Combine(RepoPaths.SyntheticFixtures, "gibt-es-nicht.snapshot.json")] },
            { 3, ["gibt-es-nicht"] },
        };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Exitcode_kommt_in_PowerShell_als_LASTEXITCODE_an(int expected, string[] args)
    {
        using var data = new TempFolder();
        var command = "& " + PsQuote(Exe()) + " " + string.Join(" ", args.Select(PsQuote)) + "; Write-Output ('EXIT=' + $LASTEXITCODE)";
        var (stdout, _) = Start("powershell.exe", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command], data.Path);
        Assert.Equal($"EXIT={expected}", LastLine(stdout));

        // PowerShell 7, wenn vorhanden (auf den Windows-Runnern ja)
        if (FindOnPath("pwsh.exe") is { } pwsh)
        {
            var (pwshOut, _) = Start(pwsh, ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command], data.Path);
            Assert.Equal($"EXIT={expected}", LastLine(pwshOut));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Exitcode_kommt_in_cmd_als_ERRORLEVEL_an(int expected, string[] args)
    {
        using var data = new TempFolder();
        var script = Path.Combine(data.Path, "riegel-test.cmd");
        File.WriteAllText(script,
            "@echo off\r\n" +
            "\"" + Exe() + "\" " + string.Join(" ", args.Select(CmdQuote)) + "\r\n" +
            "echo EXIT=%ERRORLEVEL%\r\n",
            Encoding.ASCII);
        var (stdout, _) = Start("cmd.exe", ["/d", "/c", script], data.Path);
        Assert.Equal($"EXIT={expected}", LastLine(stdout));
    }

    [Theory]
    [InlineData("de", "Prüfen und überwachen")]
    [InlineData("fr", "Vérifier et surveiller")]
    [InlineData("zh-Hans", "检查和监控")]
    public void Umgeleitete_Ausgabe_ist_UTF8_ohne_BOM(string language, string expected)
    {
        using var data = new TempFolder();
        var info = Info(Exe(), ["--help"], data.Path);
        info.Environment["RIEGEL_LANGUAGE"] = language;
        using var process = Process.Start(info)!;
        using var bytes = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(bytes);
        process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000));
        Assert.Equal(0, process.ExitCode);
        var raw = bytes.ToArray();
        Assert.False(raw.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]), "BOM in der Ausgabe");
        var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(raw);
        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Contains("Riegel.Cli.exe <", text, StringComparison.Ordinal);
    }

    // ================= Hilfen =================

    private static string Exe()
    {
        var configuration = typeof(ConsoleHostTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Release";
        var exe = Path.Combine(RepoPaths.Src, "Riegel.Cli", "bin", configuration, "net10.0-windows", "Riegel.Cli.exe");
        Assert.True(File.Exists(exe), $"{exe} fehlt – erst die Lösung bauen: dotnet build Riegel.sln -c {configuration}");
        return exe;
    }

    private static ProcessStartInfo Info(string file, IEnumerable<string> args, string dataFolder)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = dataFolder,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        // Eigener Datenordner und feste Sprache: der Test liest keine Einstellungen dieses Benutzers
        info.Environment["RIEGEL_DATA_DIR"] = dataFolder;
        info.Environment["RIEGEL_LANGUAGE"] = "en";
        return info;
    }

    private static (string Out, string Err) Start(string file, IEnumerable<string> args, string dataFolder)
    {
        using var process = Process.Start(Info(file, args, dataFolder))!;
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(120_000), "Zeitüberschreitung");
        return (stdout, stderr.Result);
    }

    private static string LastLine(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";

    private static string PsQuote(string arg) => "'" + arg.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string CmdQuote(string arg) =>
        arg.Length > 0 && arg.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':' or '\\') ? arg : "\"" + arg + "\"";

    private static string? FindOnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir.Trim(), exe)).FirstOrDefault(File.Exists);
}
#endif
