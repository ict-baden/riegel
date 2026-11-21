// SPDX-License-Identifier: MIT
using System.Reflection;

namespace Riegel.Tests.TestSupport;

/// <summary>
/// Entwicklerwerkzeuge unter tools/ (RuleTool, SnapshotAnonymizer), die das Testprojekt per ProjectReference ohne
/// Assembly-Verweis mitbaut (auch im Linux-Job) und als eigenen Prozess startet. Gesucht wird in derselben Konfiguration
/// wie die Tests, nie in einer anderen (sonst liefe ein veralteter Debug-Build).
/// </summary>
public static class BuiltTool
{
    /// <summary>Konfiguration dieses Testlaufs (Debug oder Release).</summary>
    public static string Configuration =>
        typeof(BuiltTool).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Release";

    /// <summary>Pfad der gebauten DLL unter tools/&lt;Werkzeug&gt;/bin/&lt;Konfiguration&gt;/net10.0, sonst null.</summary>
    public static string? DllPath(string tool)
    {
        var path = Path.Combine(RepoPaths.Root, "tools", tool, "bin", Configuration, "net10.0", tool + ".dll");
        return File.Exists(path) ? path : null;
    }

    /// <summary>In der CI (GitHub Actions) darf ein Werkzeug-Test nie still übersprungen werden.</summary>
    public static bool InCi => Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
}

/// <summary>
/// Test, der ein gebautes Werkzeug braucht. Fehlt es, wird er lokal übersprungen; in der CI läuft er und scheitert mit
/// einer klaren Meldung, damit ein Gate (z. B. „docs/rules.md aktuell“) nicht unbemerkt wegfällt, wenn sich
/// Zielframework oder Ausgabepfad des Werkzeugs ändern.
/// </summary>
public sealed class BuiltToolFactAttribute : FactAttribute
{
    public BuiltToolFactAttribute(string tool)
    {
        if (BuiltTool.DllPath(tool) is null && !BuiltTool.InCi)
            Skip = $"{tool} ist in dieser Konfiguration nicht gebaut (dotnet build Riegel.sln).";
    }
}
