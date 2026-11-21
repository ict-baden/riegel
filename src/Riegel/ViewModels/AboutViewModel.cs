// SPDX-License-Identifier: MIT
using Riegel.Core.About;
using Riegel.Core.Diagnostics;
using Riegel.Core.Model;
using Riegel.Core.Settings;
using Riegel.Session;
using Riegel.UI;

namespace Riegel.ViewModels;

/// <summary>
/// Info (spec 7.11, Richtlinie 07 und 10): Produkt, Version, Build, Sammler- und Katalogversion; Copyright und
/// MIT-Lizenz mit Lizenztext; Drittanbieter-Lizenzen samt Namensnennung der Regelquellen; Datenordner; „Diese Software
/// sendet keine Daten an ICT-Baden.“; Links zu Webseite, Impressum, Datenschutz, Quellcode, Hilfe &amp; Support;
/// „Diagnosepaket erstellen (ohne Namen und Adressen)“; Befehlszeile mit Exitcodes und Tastenkürzel.
/// </summary>
internal sealed class AboutViewModel : PageViewModel
{
    private bool _anonymize = true;
    private string? _lastFolder;

    public AboutViewModel(AppSession session, IDialogs dialogs) : base(session, dialogs, "about", "\uE946")
    {
        LicenseCommand = new RelayCommand(() => ShowText(Loc.T("About_License"), LicenseTexts.License(), AppInfo.LicenseUrl));
        NoticesCommand = new RelayCommand(() => ShowText(Loc.T("About_Notices"), LicenseTexts.ThirdPartyNotices(), AppInfo.RepositoryUrl + "/blob/main/THIRD-PARTY-NOTICES.txt"));
        LinkCommand = new RelayCommand(p => { if (p is string url && Shell.OpenLink(Session.Platform, url) is { } problem) Session.Report(NoticeKind.Warning, problem); });
        DataFolderCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(AppPaths.DataFolder);
            if (Shell.ShowInFolder(Session.Platform, AppPaths.DataFolder) is { } p) Session.Report(NoticeKind.Warning, p);
        }, () => Session.Mode == AppMode.Normal);
        DiagnosticsCommand = new AsyncCommand(CreateDiagnosticsAsync, () => Session.Mode != AppMode.Screenshots);
    }

    public override bool NeedsPlant => false;

    public string ProductName => Loc.T("About_ProductName");
    public string Tagline => Loc.T("About_Tagline");
    public string Description => Loc.T("About_Description");
    public string VersionText => Loc.F("About_Version", AppInfo.Version, AppInfo.Commit ?? "–", Environment.Version.ToString(3));
    public string ComponentsText => Loc.F("About_Components", AppInfo.CollectorVersion, CatalogueVersion);
    public string CopyrightText => $"{AppInfo.Copyright} · {Loc.T("About_MitLicense")}";
    public string Publisher => AppInfo.Publisher;
    public string PublishedByText => Loc.F("About_PublishedBy", AppInfo.Publisher);
    public string NoData => Loc.C("About_NoData");
    public string ReadOnlyText => Loc.T("About_ReadOnly") + " " + NoData;
    public string DataFolder => SettingsViewModel.Shorten(AppPaths.DataFolder);
    public string SourcesText => string.Join(" · ", RuleSources.All.Select(s => s.Src));

    public string WebsiteUrl => AppInfo.WebsiteUrl;
    public string ImpressumUrl => AppInfo.ImpressumUrl;
    public string PrivacyUrl => AppInfo.PrivacyUrl;
    public string RepositoryUrl => AppInfo.RepositoryUrl;
    public string SupportUrl => AppInfo.SupportUrl;
    public string HelpUrl => HelpLink;

    /// <summary>Hilfe zur Bedienung (F1).</summary>
    public const string HelpLink = AppInfo.RepositoryUrl + "/blob/main/docs/usage.md";

    public string CommandExample => "\"C:\\Program Files\\Riegel\\Riegel.Cli.exe\" check --plant D:\\Anlagen\\Linie-3 --brief";

    public bool Anonymize { get => _anonymize; set => Set(ref _anonymize, value); }

    public RelayCommand LicenseCommand { get; }
    public RelayCommand NoticesCommand { get; }
    public RelayCommand LinkCommand { get; }
    public RelayCommand DataFolderCommand { get; }
    public AsyncCommand DiagnosticsCommand { get; }

    private string CatalogueVersion
    {
        get
        {
            try { return Session.Services.Catalogue.LoadEmbedded().Version; }
            catch (RiegelException) { return "–"; }
        }
    }

    private void ShowText(string title, string? text, string url)
    {
        if (text is null)
        {
            Session.Report(NoticeKind.Warning, Loc.F("About_TextMissing", url));
            return;
        }
        Dialogs.ShowText(title, text);
    }

    private async Task CreateDiagnosticsAsync()
    {
        var path = Dialogs.SaveFile(Loc.T("About_DiagnosticsSave"), Loc.T("About_DiagnosticsFilter"), DiagnosticsPackage.SuggestedFileName(DateTime.Now));
        if (path is null) return;
        _lastFolder = Path.GetDirectoryName(path);
        bool anonymize = Anonymize;
        var info = AppDiagnostics.Info(Session);
        var hosts = Session.Plant?.Hosts.ToList() ?? [];
        try
        {
            await Session.Work(Loc.T("Busy_Diagnostics"), () => DiagnosticsPackage.Write(path, info, anonymize, hosts));
            Log.Info($"Diagnosepaket erstellt ({(anonymize ? "ohne" : "mit")} Namen und Adressen)");
            Session.Report(NoticeKind.Success, Loc.F(anonymize ? "About_DiagnosticsAnonymous" : "About_DiagnosticsNamed", path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Session.Report(NoticeKind.Error, RglCodesCommon.Message(RglCodesCommon.DiagnosticsFailed, e.Message) is var m ? $"{RglCodesCommon.DiagnosticsFailed}: {m}" : "");
        }
    }
}
