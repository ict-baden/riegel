// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Reflection;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Rules.A;

/// <summary>
/// Startet das gebaute Entwicklerwerkzeug RuleTool (tools/RuleTool) als eigenen Prozess. Das Testprojekt referenziert
/// RuleTool nicht; ohne gebautes Werkzeug (z. B. unter Linux, wo die CI nur das Testprojekt baut) werden die Tests
/// übersprungen. Das Werkzeug liest nur das Repository bzw. Temp-Ordner und schreibt nur in Temp-Ordner.
/// </summary>
internal static class RuleToolRunner
{
    /// <summary>Pfad der gebauten RuleTool.dll in derselben Konfiguration wie die Tests, sonst null.</summary>
    public static string? DllPath
    {
        get
        {
            var configuration = typeof(RuleToolRunner).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Release";
            var path = Path.Combine(RepoPaths.Root, "tools", "RuleTool", "bin", configuration, "net10.0", "RuleTool.dll");
            return File.Exists(path) ? path : null;
        }
    }

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

/// <summary>Test, der das gebaute RuleTool braucht; ohne Werkzeug übersprungen.</summary>
public sealed class RuleToolFactAttribute : FactAttribute
{
    public RuleToolFactAttribute()
    {
        if (RuleToolRunner.DllPath is null) Skip = "RuleTool ist in dieser Konfiguration nicht gebaut (dotnet build Riegel.sln).";
    }
}
