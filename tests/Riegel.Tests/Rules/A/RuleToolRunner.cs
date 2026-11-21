// SPDX-License-Identifier: MIT
using System.Diagnostics;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Rules.A;

/// <summary>
/// Startet das gebaute Entwicklerwerkzeug RuleTool (tools/RuleTool) als eigenen Prozess. Das Testprojekt baut RuleTool
/// über eine ProjectReference ohne Assembly-Verweis mit, auch im Linux-Job. Fehlt die DLL, werden die Tests lokal
/// übersprungen und scheitern in der CI (<see cref="BuiltToolFactAttribute"/>). Das Werkzeug liest nur das Repository
/// bzw. Temp-Ordner und schreibt nur in Temp-Ordner.
/// </summary>
internal static class RuleToolRunner
{
    /// <summary>Pfad der gebauten RuleTool.dll in derselben Konfiguration wie die Tests, sonst null.</summary>
    public static string? DllPath => BuiltTool.DllPath("RuleTool");

    /// <summary>Führt RuleTool mit den Argumenten aus; Arbeitsordner ist <paramref name="workingDirectory"/> (Standard: Repo-Wurzel).</summary>
    public static (int ExitCode, string Stdout, string Stderr) Run(string? workingDirectory, params string[] args)
    {
        var dll = DllPath ?? throw new InvalidOperationException("RuleTool ist nicht gebaut.");
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var start = new ProcessStartInfo(string.IsNullOrEmpty(host) ? "dotnet" : host)
        {
            WorkingDirectory = workingDirectory ?? RepoPaths.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(dll);
        foreach (var a in args) start.ArgumentList.Add(a);
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("RuleTool ließ sich nicht starten.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("RuleTool lief länger als 120 s.");
        }
        return (process.ExitCode, stdout.Result, stderr.Result);
    }
}

/// <summary>Test, der das gebaute RuleTool braucht; ohne Werkzeug lokal übersprungen, in der CI rot.</summary>
public sealed class RuleToolFactAttribute : FactAttribute
{
    public RuleToolFactAttribute()
    {
        if (RuleToolRunner.DllPath is null && !BuiltTool.InCi)
            Skip = "RuleTool ist in dieser Konfiguration nicht gebaut (dotnet build Riegel.sln).";
    }
}
