// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Commands.Resources;
using Riegel.Core.About;
using Riegel.Core.Resources;
using Riegel.Core.Settings;

namespace Riegel.Commands.Commands;

/// <summary>
/// Hilfe und Angaben über das Programm (Richtlinie 07, spec 7.11): <c>--help</c>/<c>--hilfe</c> (Übersicht und je
/// Befehl), <c>--version</c>, <c>--info</c>, <c>--licenses</c>/<c>--lizenzen</c>. Alle Texte aus CliStrings.
/// </summary>
internal static class InfoCommands
{
    /// <summary>Befehle mit eigener Hilfe; Schlüssel in CliStrings: <c>Help_&lt;Name&gt;</c>.</summary>
    internal static IReadOnlyDictionary<string, string> CommandHelpKeys { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["check"] = "Help_Check",
        ["collect"] = "Help_Collect",
        ["import"] = "Help_Import",
        ["report"] = "Help_Report",
        ["summary"] = "Help_Summary",
        ["target-config"] = "Help_TargetConfig",
        ["collection-record"] = "Help_CollectionRecord",
        ["compare"] = "Help_Compare",
        ["compare-snapshot"] = "Help_CompareSnapshot",
        ["approve"] = "Help_Approve",
        ["exceptions"] = "Help_Exceptions",
        ["tailoring"] = "Help_Tailoring",
        ["measures"] = "Help_Measures",
        ["remediate"] = "Help_Remediate",
        ["baseline"] = "Help_Baseline",
        ["rules"] = "Help_Rules",
        ["verify"] = "Help_Verify",
        ["collector"] = "Help_Collector",
        ["plant"] = "Help_Plant",
    };

    public static int Overview(CommandContext ctx)
    {
        ctx.Line($"Riegel {AppInfo.VersionWithBuild}");
        ctx.Line(CliStrings.Cli_Description);
        ctx.Line();
        ctx.Line(CliStrings.Help_Overview, CommandContext.ProgramName);
        ctx.Line();
        ctx.Line(CliStrings.Help_ExitCodes);
        if (OperatingSystem.IsWindows())
        {
            ctx.Line();
            ctx.Line(CliStrings.Help_ScriptsNote);
        }
        return CommandApp.Ok;
    }

    /// <summary>Hilfe eines Befehls (<c>&lt;befehl&gt; --help</c>).</summary>
    public static int Command(CommandContext ctx, string command)
    {
        var key = CommandHelpKeys[command];
        var text = CliStrings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        ctx.Line(text, CommandContext.ProgramName);
        return CommandApp.Ok;
    }

    public static int Version(CommandContext ctx)
    {
        ctx.Line(CliStrings.Cli_VersionLine, AppInfo.VersionWithBuild, AppInfo.CollectorVersion, ctx.Services.Catalogue.LoadEmbedded().Version);
        return CommandApp.Ok;
    }

    /// <summary>Dieselben Angaben wie die Info-Seite, als Text (Richtlinie 07, spec 7.11).</summary>
    public static int Info(CommandContext ctx)
    {
        var catalogue = ctx.Services.Catalogue.LoadEmbedded();
        ctx.Line("Riegel (Härtungs-Check)");
        ctx.Line(CliStrings.Cli_Description);
        ctx.Line();
        ctx.Line(CliStrings.Cli_InfoVersion, AppInfo.VersionWithBuild);
        ctx.Line(CliStrings.Cli_InfoCollector, AppInfo.CollectorVersion);
        ctx.Line(CliStrings.Cli_InfoCatalogue, catalogue.Id, catalogue.Version);
        ctx.Line(AppInfo.Copyright);
        ctx.Line(CliStrings.Cli_InfoPublisher, AppInfo.Publisher);
        ctx.Line(CliStrings.Cli_InfoLicence, AppInfo.LicenseUrl);
        ctx.Line(CliStrings.Cli_InfoThirdParty, CommandContext.ProgramName);
        ctx.Line(CommonStrings.About_NoData);
        ctx.Line();
        ctx.Line(CliStrings.Cli_InfoLinks, AppInfo.WebsiteUrl, AppInfo.ImpressumUrl, AppInfo.RepositoryUrl, AppInfo.SupportUrl, AppInfo.PrivacyUrl);
        ctx.Line(CliStrings.Cli_InfoDataFolder, AppPaths.DataFolder);
        ctx.Line();
        ctx.Line(CliStrings.Help_ExitCodes);
        return CommandApp.Ok;
    }

    public static int Licenses(CommandContext ctx)
    {
        var license = LicenseTexts.License();
        var notices = LicenseTexts.ThirdPartyNotices();
        if (license is null || notices is null)
        {
            ctx.Err.WriteLine(CliStrings.Cli_LicensesMissing);
            return CommandApp.Unknown;
        }
        ctx.Line(license.TrimEnd());
        ctx.Line();
        ctx.Line(notices.TrimEnd());
        return CommandApp.Ok;
    }
}
