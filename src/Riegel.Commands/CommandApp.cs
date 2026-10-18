// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Commands.Resources;
using Riegel.Core.About;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;
using Riegel.Core.Settings;

namespace Riegel.Commands;

/// <summary>
/// Öffentlicher Einstieg aller Befehle (spec 7.7). Riegel.Cli (Windows/Linux) und die Konsolen-Anbindung von
/// Riegel.exe rufen nur <see cref="Run"/> auf. Das Gerüst kann: <c>--help</c>, <c>--version</c>, <c>--info</c>,
/// <c>--licenses</c>, <c>check --snapshot</c> und <c>collect</c>; das Paket cli ergänzt alle übrigen Befehle und behält
/// diese Signatur.
/// </summary>
public static class CommandApp
{
    /// <summary>Exitcodes nach Nagios (spec 7.7).</summary>
    public const int Ok = 0, Warning = 1, Critical = 2, Unknown = 3;

    /// <summary>Führt einen Befehl aus und gibt den Exitcode zurück; schreibt nur in <paramref name="output"/> und <paramref name="error"/>.</summary>
    /// <param name="platform">Windows: aus Riegel.Platform.Windows.PlatformFactory; Linux und Tests: <see cref="NullPlatform"/>.</param>
    public static int Run(string[] args, IPlatform platform, TextWriter output, TextWriter error) =>
        Run(args, RiegelServices.CreateDefault(platform), output, error);

    /// <summary>Wie <see cref="Run(string[], IPlatform, TextWriter, TextWriter)"/>, mit eigenen Diensten (Tests: feste Uhr).</summary>
    public static int Run(string[] args, RiegelServices services, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        // --nur-lesen/--read-only gelten überall (Richtlinie 09); das Gerüst schreibt ohnehin nicht in Anlagenordner
        var rest = args.Where(a => !IsAny(a, "--nur-lesen", "--read-only")).ToList();
        try
        {
            if (rest.Count == 0 || IsAny(rest[0], "--help", "--hilfe", "-h", "/?")) return Help(output);
            if (IsAny(rest[0], "--version")) return Version(services, output);
            if (IsAny(rest[0], "--info")) return Info(services, output);
            if (IsAny(rest[0], "--licenses", "--lizenzen")) return Licenses(output, error);
            if (IsAny(rest[0], "check")) return Check(services, rest.Skip(1).ToList(), output, error);
            if (IsAny(rest[0], "collect")) return Collect(services, rest.Skip(1).ToList(), output, error);
            return Usage(error, rest[0]);
        }
        catch (RiegelException e)
        {
            error.WriteLine(e.ToString());
            return Unknown;
        }
    }

    private static int Help(TextWriter output)
    {
        output.WriteLine($"Riegel {AppInfo.VersionWithBuild}");
        output.WriteLine(CliStrings.Cli_Description);
        output.WriteLine();
        output.WriteLine(string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_Usage, ProgramName()));
        return Ok;
    }

    private static int Version(RiegelServices services, TextWriter output)
    {
        output.WriteLine(string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_VersionLine, AppInfo.VersionWithBuild,
            AppInfo.CollectorVersion, services.Catalogue.LoadEmbedded().Version));
        return Ok;
    }

    private static int Info(RiegelServices services, TextWriter output)
    {
        var catalogue = services.Catalogue.LoadEmbedded();
        output.WriteLine("Riegel (Härtungs-Check)");
        output.WriteLine(CliStrings.Cli_Description);
        output.WriteLine();
        output.WriteLine(string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_InfoVersion, AppInfo.VersionWithBuild));
        output.WriteLine(string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_InfoCollector, AppInfo.CollectorVersion));
        output.WriteLine(string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_InfoCatalogue, catalogue.Id, catalogue.Version));
        output.WriteLine(AppInfo.Copyright);
        output.WriteLine(string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_InfoLicence, AppInfo.LicenseUrl));
        output.WriteLine(Core.Resources.CommonStrings.About_NoData);
        output.WriteLine();
        output.WriteLine(string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_InfoLinks, AppInfo.WebsiteUrl, AppInfo.ImpressumUrl,
            AppInfo.RepositoryUrl, AppInfo.SupportUrl, AppInfo.PrivacyUrl));
        output.WriteLine(string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_InfoDataFolder, AppPaths.DataFolder));
        return Ok;
    }

    private static int Licenses(TextWriter output, TextWriter error)
    {
        var license = LicenseTexts.License();
        var notices = LicenseTexts.ThirdPartyNotices();
        if (license is null || notices is null)
        {
            error.WriteLine(CliStrings.Cli_LicensesMissing);
            return Unknown;
        }
        output.WriteLine(license.TrimEnd());
        output.WriteLine();
        output.WriteLine(notices.TrimEnd());
        return Ok;
    }

    /// <summary>
    /// Gerüst: <c>check --snapshot &lt;Datei&gt; [--format text|json]</c> ohne Anlagenordner, Alarmart „state“ (spec 7.7):
    /// kritisch bei regression/high, Warnung bei medium/low/expired/expiring/pending/overdue/incomplete/stale/clock.
    /// </summary>
    private static int Check(RiegelServices services, List<string> args, TextWriter output, TextWriter error)
    {
        string? snapshotPath = null;
        string format = "text";
        for (int i = 0; i < args.Count; i++)
        {
            if (IsAny(args[i], "--snapshot", "--format"))
            {
                if (i + 1 >= args.Count) { error.WriteLine(string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_MissingValue, args[i])); return Unknown; }
                if (IsAny(args[i], "--snapshot")) snapshotPath = args[++i];
                else format = args[++i].ToLowerInvariant();
                continue;
            }
            return Usage(error, args[i]);
        }
        if (format is not ("text" or "json")) return Usage(error, format);
        if (snapshotPath is null) { error.WriteLine(CliStrings.Cli_CheckNeedsSnapshot); return Unknown; }

        var snapshot = services.Snapshots.Read(snapshotPath, ReadLimits.Default);
        var language = CultureInfo.CurrentUICulture.Name;
        var run = services.Runs.EvaluateOnly(snapshot, new RunOptions(Language: language, Store: false));
        var exit = StateExitCode(run);
        if (format == "json")
        {
            output.Write(RiegelJson.Serialize(run));
            return exit;
        }
        var s = run.Summary;
        output.WriteLine(string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_CheckSummary, run.HostName, run.Results.Count,
            s.Pass, s.Fail, s.Exception, s.OutOfScope, s.NotApplicable, s.NotCheckable, s.Info));
        output.WriteLine(string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_CheckOpen, s.OpenHigh, s.OpenMedium, s.OpenLow));
        output.WriteLine(string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_CheckShare, s.CheckedShare));
        if ((run.Flags & RunFlags.Incomplete) != 0) output.WriteLine(CliStrings.Cli_CheckIncomplete);
        return exit;
    }

    /// <summary>Alarmart „state“ (spec 7.7) für einen Lauf ohne Vergleich.</summary>
    internal static int StateExitCode(Run run)
    {
        var s = run.Summary;
        bool critical = s.Regressions > 0 || s.OpenHigh > 0;
        bool warning = s.OpenMedium > 0 || s.OpenLow > 0 || s.ExceptionsExpired > 0 || s.ExceptionsExpiring > 0 ||
                       s.ExceptionsPending > 0 || s.MeasuresOverdue > 0 ||
                       (run.Flags & (RunFlags.Incomplete | RunFlags.StaleSnapshot | RunFlags.ClockSuspect)) != 0;
        return critical ? Critical : warning ? Warning : Ok;
    }

    /// <summary>Gerüst: <c>collect [--out &lt;Ordner&gt;] [--gzip] [--minimal] [--deep] [--pseudonymize]</c> über <see cref="IPlatform"/>.</summary>
    private static int Collect(RiegelServices services, List<string> args, TextWriter output, TextWriter error)
    {
        string? outDir = null;
        bool gzip = false, minimal = false, deep = false, pseudonymize = false;
        for (int i = 0; i < args.Count; i++)
        {
            if (IsAny(args[i], "--out"))
            {
                if (i + 1 >= args.Count) { error.WriteLine(string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_MissingValue, args[i])); return Unknown; }
                outDir = args[++i];
            }
            else if (IsAny(args[i], "--gzip")) gzip = true;
            else if (IsAny(args[i], "--minimal")) minimal = true;
            else if (IsAny(args[i], "--deep")) deep = true;
            else if (IsAny(args[i], "--pseudonymize")) pseudonymize = true;
            else return Usage(error, args[i]);
        }
        if (!services.Platform.CanCollect)
        {
            error.WriteLine(RiegelException.Common(RglCodesCommon.PlatformNotSupported).ToString());
            return Unknown;
        }
        var snapshot = services.Platform.CollectThisMachine(new CollectOptions(minimal, deep, pseudonymize), CancellationToken.None);
        var path = SnapshotFile.Write(snapshot, outDir ?? Directory.GetCurrentDirectory(), gzip);
        output.WriteLine(string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_CollectWritten, path, Hashes.Sha256File(path)));
        bool incomplete = !snapshot.IsAdmin || snapshot.Items.Any(i => i.Status is ItemStatus.Denied or ItemStatus.Error);
        return incomplete ? Warning : Ok;
    }

    private static int Usage(TextWriter error, string argument)
    {
        error.WriteLine($"{RglCodesCli.UsageError}: {string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_UnknownArgument, argument)}");
        return Unknown;
    }

    private static bool IsAny(string arg, params string[] names) =>
        names.Any(n => string.Equals(arg, n, StringComparison.OrdinalIgnoreCase));

    private static string ProgramName() =>
        OperatingSystem.IsWindows() ? "Riegel.Cli.exe" : "riegel";
}
