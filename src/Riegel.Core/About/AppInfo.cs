// SPDX-License-Identifier: MIT
using System.Reflection;
using Riegel.Core.Model;
using Riegel.Core.Resources;

namespace Riegel.Core.About;

/// <summary>
/// Angaben über das Programm (Info-Seite, <c>--version</c>/<c>--info</c>, Läufe, Diagnosepaket; Muster aus Zutritt).
/// Firma und Copyright kommen aus den Assembly-Angaben (Directory.Build.props), damit ein Wechsel der Firmierung eine
/// einzige Änderung ist.
/// </summary>
public static class AppInfo
{
    public const string Name = "Riegel";
    public const string RepositoryUrl = "https://github.com/ict-baden/riegel";
    public const string SupportUrl = RepositoryUrl + "/blob/main/SUPPORT.md";
    public const string PrivacyUrl = RepositoryUrl + "/blob/main/docs/data-and-privacy.md";
    public const string LicenseUrl = RepositoryUrl + "/blob/main/LICENSE";

    /// <summary>
    /// Impressum – es gibt nur eines, auf der Webseite (Richtlinie 07). Feste Adresse der neuen Webseite.
    /// </summary>
    public const string ImpressumUrl = "https://ict-baden.de/impressum/";

    /// <summary>Produktseite auf der Webseite von ICT-Baden.</summary>
    public const string WebsiteUrl = "https://ict-baden.de/produkte/riegel/";

    private const string FallbackPublisher = "ICT-Baden";

    private static readonly Assembly Self = typeof(AppInfo).Assembly;

    private static readonly string Informational =
        Self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    /// <summary>Version ohne Build-Anhang, z. B. „0.1.0“ oder „0.1.0-ci.12“; „unknown“, wenn sie fehlt.</summary>
    public static string Version { get; } = ParseVersion(Informational);

    /// <summary>Commit-Kürzel (7 Zeichen) aus dem Build-Anhang „+&lt;commit&gt;“, null ohne.</summary>
    public static string? Commit { get; } = ParseCommit(Informational);

    /// <summary>„0.1.0-ci.12 (Build abc1234)“ bzw. nur die Version.</summary>
    public static string VersionWithBuild => Commit is null ? Version : string.Format(System.Globalization.CultureInfo.CurrentCulture, CommonStrings.About_VersionWithBuild, Version, Commit);

    /// <summary>Version des portablen Sammlers, der zu diesem Build gehört (collector-version.txt, eingebettet).</summary>
    public static string CollectorVersion { get; } = ReadEmbedded("collector-version.txt")?.Trim() is { Length: > 0 } v ? v : "unknown";

    /// <summary>Herausgeber (AssemblyCompany), solange die Firmierung offen ist „ICT-Baden“.</summary>
    public static string Publisher { get; } =
        Self.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company is { Length: > 0 } company && company != Name ? company : FallbackPublisher;

    /// <summary>„© 2026 ICT-Baden“ (AssemblyCopyright).</summary>
    public static string Copyright { get; } =
        Self.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright is { Length: > 0 } copyright ? copyright : $"© 2026 {Publisher}";

    private static readonly Lazy<string?> ExeHash = new(() =>
    {
        try
        {
            return Environment.ProcessPath is { } exe && File.Exists(exe) ? Hashes.Sha256File(exe) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    });

    /// <summary>SHA-256 der laufenden Exe (steht in Lauf, Bericht und Nachweispaket), null wenn nicht lesbar.</summary>
    public static string? ExeSha256 => ExeHash.Value;

    /// <summary>Werkzeugangabe für Läufe und Berichte.</summary>
    public static ToolInfo Tool() => new(Name, Version, Commit, ExeSha256);

    internal static string ParseVersion(string informational)
    {
        var version = informational.Split('+')[0].Trim();
        return version.Length > 0 ? version : "unknown";
    }

    internal static string? ParseCommit(string informational)
    {
        int plus = informational.IndexOf('+');
        if (plus < 0) return null;
        var commit = informational[(plus + 1)..].Trim();
        if (commit.Length == 0) return null;
        return commit.Length > 7 ? commit[..7] : commit;
    }

    internal static string? ReadEmbedded(string name)
    {
        using var stream = Self.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// Lizenztexte für Info-Seite, <c>--info</c> und <c>--licenses</c>: die eigene MIT-Lizenz (LICENSE) und die der
/// mitgelieferten Bestandteile samt Namensnennung der Regelquellen (THIRD-PARTY-NOTICES.txt). Beide sind in
/// Riegel.Core eingebettet – die einzelne Exe zum Herunterladen hat keine Dateien daneben.
/// </summary>
public static class LicenseTexts
{
    public const string LicenseFile = "LICENSE";
    public const string NoticesFile = "THIRD-PARTY-NOTICES.txt";

    /// <summary>
    /// Die Hinweise der mitgelieferten .NET-Laufzeiten, unverändert (notices/, Review legal-2): dotnet/runtime und
    /// dotnet/wpf zum Tag v10.0.13; THIRD-PARTY-NOTICES.txt Abschnitt 12 verweist darauf.
    /// </summary>
    public static readonly string[] RuntimeNoticesFiles = ["THIRD-PARTY-NOTICES-dotnet-runtime.txt", "THIRD-PARTY-NOTICES-dotnet-wpf.txt"];

    /// <summary>Lizenztext von Riegel, null wenn er in dieser Ausgabe fehlt.</summary>
    public static string? License() => Read(LicenseFile);

    /// <summary>
    /// Lizenzen der mitgelieferten Bestandteile, null wenn sie fehlen: THIRD-PARTY-NOTICES.txt, danach die Hinweise der
    /// .NET-Laufzeiten (auch in der einzelnen Exe ohne Dateien daneben).
    /// </summary>
    public static string? ThirdPartyNotices()
    {
        var main = Read(NoticesFile);
        if (main is null) return null;
        var text = new System.Text.StringBuilder(main.TrimEnd());
        foreach (var file in RuntimeNoticesFiles)
            if (Read(file) is { } runtime)
                text.Append("\n\n\n===== ").Append(file).Append(" (see section 12) =====\n\n").Append(runtime.TrimEnd());
        return text.Append('\n').ToString();
    }

    /// <summary>Eingebettete Fassung, sonst die Datei neben der Exe.</summary>
    internal static string? Read(string name)
    {
        var embedded = AppInfo.ReadEmbedded(name);
        if (embedded is not null) return embedded;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
