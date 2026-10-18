// SPDX-License-Identifier: MIT
using System.Collections.ObjectModel;
using Riegel.Core.Localization;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Session;
using Riegel.UI;

namespace Riegel.ViewModels;

/// <summary>Eine Anlage für die Übersicht der Geschäftsführung (O6).</summary>
internal sealed class PlantPick(string folder, string name, bool current) : ObservableObject
{
    private bool _checked = true;
    public string Folder { get; } = folder;
    public string Name { get; } = name;
    public bool Current { get; } = current;
    public bool CanToggle => !Current;
    public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
}

/// <summary>
/// Berichte (spec 7.8 Seite 8, spec 5): Anlagenbericht zum Abzeichnen mit Preset, Rechner-PDFs, Anhang, Formaten,
/// Sprache, Freigaberollen, Vertraulichkeit, „Durchgeführt von“ und Logo; Übersicht über mehrere Anlagen;
/// Soll-Konfiguration; Genehmigungsblatt; Erhebungsprotokoll; Nachweispaket; Abhilfe-Vorschlag; Sammler-Übergabe.
/// Ausgabe nach reports/&lt;Zeitpunkt&gt;/ im Anlagenordner, im Prüfermodus nur in einen gewählten Ordner.
/// </summary>
internal sealed class ReportsViewModel : PageViewModel
{
    private string _outputFolder = "";
    private bool _asDelivered;
    private bool _perMachine;
    private bool _appendix = true;
    private bool _pdf = true, _xlsx = true, _csv, _json;
    private bool _evidence;
    private bool _baselineContent;
    private bool _saveToPlant;
    private Choice<string> _language;
    private Choice<string?> _host;
    private Choice<string?> _group;
    private Choice<string?> _import;
    private Choice<string?> _remediationHost;
    private string _created = "", _reviewed = "", _approved = "";
    private string _confidentiality = "";
    private string _performedBy = "";
    private string? _lastFolder;

    public ReportsViewModel(AppSession session, IDialogs dialogs) : base(session, dialogs, "reports", "")
    {
        Languages = [.. UiLanguage.Supported.Select(c => new Choice<string>(c, UiLanguage.NativeName(c)))];
        _language = Languages[0];
        _host = _group = _import = _remediationHost = new Choice<string?>(null, Loc.T("Reports_All"));
        BrowseCommand = new RelayCommand(Browse);
        PlantReportCommand = new AsyncCommand(PlantReportAsync, () => Session.HasPlant && (Pdf || Xlsx || Csv || Json));
        SummaryCommand = new AsyncCommand(() => GenerateAsync(new DocumentRequest(DocumentKind.Summary, "", Language.Value,
            PlantFolders: [.. Plants.Where(p => p.IsChecked && !p.Current).Select(p => p.Folder)])), () => Session.HasPlant);
        TargetConfigCommand = new AsyncCommand(() => GenerateAsync(new DocumentRequest(DocumentKind.TargetConfig, "", Language.Value, GroupId: Group.Value)), () => Session.HasPlant);
        ApprovalSheetCommand = new AsyncCommand(() => GenerateAsync(new DocumentRequest(DocumentKind.ApprovalSheet, "", Language.Value)), () => Session.HasPlant);
        CollectionRecordCommand = new AsyncCommand(() => GenerateAsync(new DocumentRequest(DocumentKind.CollectionRecord, "", Language.Value, ImportId: Import.Value ?? "latest")),
            () => Session.HasPlant && Imports.Count > 1);
        EvidenceCommand = new AsyncCommand(() => GenerateAsync(new DocumentRequest(DocumentKind.Evidence, "", Language.Value, IncludeBaselineContent: BaselineContent)), () => Session.HasPlant);
        RemediationCommand = new AsyncCommand(() => GenerateAsync(new DocumentRequest(DocumentKind.Remediation, "", Language.Value,
            Hosts: RemediationHost.Value is { } h ? [h] : null)), () => Session.HasPlant);
        HandoverCommand = new AsyncCommand(HandoverAsync, () => Session.Mode != AppMode.Screenshots);
        OpenFolderCommand = new RelayCommand(() => { if (_lastFolder is not null && Shell.ShowInFolder(Session.Platform, _lastFolder) is { } p) Session.Report(NoticeKind.Warning, p); },
            () => _lastFolder is not null);
        PickLogoCommand = new AsyncCommand(PickLogoAsync, () => Session.CanWrite);
    }

    public IReadOnlyList<Choice<string>> Languages { get; }
    public ObservableCollection<Choice<string?>> HostChoices { get; } = [];
    public ObservableCollection<Choice<string?>> Groups { get; } = [];
    public ObservableCollection<Choice<string?>> Imports { get; } = [];
    public ObservableCollection<PlantPick> Plants { get; } = [];
    public ObservableCollection<string> Written { get; } = [];

    public string OutputFolder { get => _outputFolder; set => Set(ref _outputFolder, value); }
    public string OutputHint => Plant?.ReadOnly == true ? Loc.T("Reports_OutputReadOnly") : Loc.T("Reports_OutputDefault");
    public bool AsDelivered { get => _asDelivered; set { if (Set(ref _asDelivered, value)) { Raise(nameof(Audit)); ApplyPresetRoles(); } } }
    public bool Audit { get => !_asDelivered; set => AsDelivered = !value; }
    public bool PerMachine { get => _perMachine; set => Set(ref _perMachine, value); }
    public bool Appendix { get => _appendix; set => Set(ref _appendix, value); }
    public bool Pdf { get => _pdf; set => Set(ref _pdf, value); }
    public bool Xlsx { get => _xlsx; set => Set(ref _xlsx, value); }
    public bool Csv { get => _csv; set => Set(ref _csv, value); }
    public bool Json { get => _json; set => Set(ref _json, value); }
    public bool Evidence { get => _evidence; set => Set(ref _evidence, value); }
    public bool BaselineContent { get => _baselineContent; set => Set(ref _baselineContent, value); }
    public bool SaveToPlant { get => _saveToPlant; set => Set(ref _saveToPlant, value); }
    public bool CanSaveToPlant => Session.CanWrite;
    public Choice<string> Language { get => _language; set { if (Set(ref _language, value)) Raise(nameof(PreTranslated)); } }
    public bool PreTranslated => !UiLanguage.IsReviewedReportLanguage(Language.Value);
    public Choice<string?> Host { get => _host; set => Set(ref _host, value); }
    public Choice<string?> Group { get => _group; set => Set(ref _group, value); }
    public Choice<string?> Import { get => _import; set => Set(ref _import, value); }
    public Choice<string?> RemediationHost { get => _remediationHost; set => Set(ref _remediationHost, value); }
    public string CreatedRole { get => _created; set => Set(ref _created, value); }
    public string ReviewedRole { get => _reviewed; set => Set(ref _reviewed, value); }
    public string ApprovedRole { get => _approved; set => Set(ref _approved, value); }
    public string Confidentiality { get => _confidentiality; set => Set(ref _confidentiality, value); }
    public string PerformedBy { get => _performedBy; set => Set(ref _performedBy, value); }
    public string LogoText => Plant?.Plant.Settings.CoverLogoFile is { } logo ? logo : Loc.T("Reports_NoLogo");
    public int PendingCount => Plant?.Registers.Exceptions.Exceptions.Count(e => Plant.Info(e).State is RegisterEntryState.Pending or RegisterEntryState.ChangedAfterApproval) ?? 0;
    public string PendingText => Loc.F("Reports_PendingEntries", PendingCount);
    public bool HasWritten => Written.Count > 0;

    public RelayCommand BrowseCommand { get; }
    public AsyncCommand PlantReportCommand { get; }
    public AsyncCommand SummaryCommand { get; }
    public AsyncCommand TargetConfigCommand { get; }
    public AsyncCommand ApprovalSheetCommand { get; }
    public AsyncCommand CollectionRecordCommand { get; }
    public AsyncCommand EvidenceCommand { get; }
    public AsyncCommand RemediationCommand { get; }
    public AsyncCommand HandoverCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public AsyncCommand PickLogoCommand { get; }

    public override void OnPlantChanged()
    {
        base.OnPlantChanged();
        var all = new Choice<string?>(null, Loc.T("Reports_All"));
        HostChoices.Clear();
        HostChoices.Add(all);
        Groups.Clear();
        Groups.Add(all);
        Imports.Clear();
        Imports.Add(new Choice<string?>(null, Loc.T("Reports_LatestImport")));
        Plants.Clear();
        if (Plant is { } plant)
        {
            foreach (var m in plant.Machines.Where(m => m.Latest is not null)) HostChoices.Add(new Choice<string?>(m.HostName, m.HostName));
            foreach (var g in plant.Plant.Groups) Groups.Add(new Choice<string?>(g.Id, $"{g.Name} ({Texts.Role(g.Role)})"));
            foreach (var i in plant.Imports.OrderByDescending(i => i.ImportedAtUtc))
                Imports.Add(new Choice<string?>(i.Id, Loc.F("Reports_ImportItem", Texts.DateTime(i.ImportedAtUtc), i.Files.Count)));
            Plants.Add(new PlantPick(plant.Folder, plant.Plant.Name, true));
            foreach (var folder in Session.RecentPlants.Where(f => !string.Equals(f, plant.Folder, StringComparison.OrdinalIgnoreCase)))
                Plants.Add(new PlantPick(folder, Path.GetFileName(folder), false) { IsChecked = false });
            var s = plant.Plant.Settings;
            _asDelivered = s.ReportPreset == ReportPreset.AsDelivered;
            ApplyPresetRoles();
            Confidentiality = s.Confidentiality ?? Loc.C("Confidentiality_Default");
            PerformedBy = s.PerformedBy ?? "";
            var lang = UiLanguage.Normalize(s.ReportLanguage) is { Length: > 0 } l ? l : Session.ReportLanguage;
            Language = Languages.FirstOrDefault(x => x.Value == lang) ?? Languages[0];
            OutputFolder = Session.DefaultOutputFolder is { } d ? plant.IsDemo ? Texts.ShortPath(d) : d : "";
        }
        Host = HostChoices[0];
        Group = Groups[0];
        Import = Imports[0];
        RemediationHost = HostChoices[0];
        Written.Clear();
        _lastFolder = null;
        RaiseAll();
    }

    private void ApplyPresetRoles()
    {
        var signOff = Plant?.Plant.Settings.SignOff ?? [];
        string Role(string step, string fallback) => signOff.FirstOrDefault(s => s.Step == step)?.Role ?? fallback;
        if (AsDelivered)
        {
            // Auslieferungszustand (spec 2.17): Hersteller erstellt / Hersteller geprüft / Betreiber übernommen
            CreatedRole = Loc.T("Role_BuilderCreated");
            ReviewedRole = Loc.T("Role_BuilderReviewed");
            ApprovedRole = Loc.T("Role_OperatorAccepted");
        }
        else
        {
            CreatedRole = Role(SignOffSteps.Created, Loc.C("Role_PlantOwner"));
            ReviewedRole = Role(SignOffSteps.Reviewed, Loc.C("Role_SecurityOfficer"));
            ApprovedRole = Role(SignOffSteps.Approved, Loc.C("Role_PlantManagement"));
        }
    }

    private void Browse()
    {
        if (Dialogs.PickFolder(Loc.T("Reports_PickFolder"), OutputFolder.Length > 0 ? OutputFolder : null) is { } folder) OutputFolder = folder;
    }

    /// <summary>Anlageneinstellungen dieses Berichts (Freigaberollen, Vertraulichkeit, „Durchgeführt von“, Preset, Sprache).</summary>
    private PlantSettings ReportSettings(PlantSettings s)
    {
        var steps = AsDelivered
            ? new[] { (SignOffSteps.Created, CreatedRole), (SignOffSteps.Reviewed, ReviewedRole), (SignOffSteps.Accepted, ApprovedRole) }
            : [(SignOffSteps.Created, CreatedRole), (SignOffSteps.Reviewed, ReviewedRole), (SignOffSteps.Approved, ApprovedRole)];
        return s with
        {
            SignOff = [.. steps.Where(x => !string.IsNullOrWhiteSpace(x.Item2)).Select(x => new SignOffRole(x.Item1, x.Item2.Trim()))],
            Confidentiality = string.IsNullOrWhiteSpace(Confidentiality) ? null : Confidentiality.Trim(),
            PerformedBy = string.IsNullOrWhiteSpace(PerformedBy) ? null : PerformedBy.Trim(),
            ReportPreset = AsDelivered ? ReportPreset.AsDelivered : ReportPreset.Audit,
            ReportLanguage = Language.Value,
        };
    }

    private async Task PlantReportAsync()
    {
        if (Plant is null) return;
        var formats = new List<ReportFormat>();
        if (Pdf) formats.Add(ReportFormat.Pdf);
        if (Xlsx) formats.Add(ReportFormat.Xlsx);
        if (Csv) formats.Add(ReportFormat.Csv);
        if (Json) formats.Add(ReportFormat.Json);
        var settings = ReportSettings(Plant.Plant.Settings);
        if (SaveToPlant && Session.CanWrite)
            await Session.SavePlantAsync(p => p with { Settings = settings }, Loc.T("Notice_ReportSettingsSaved"));
        await GenerateAsync(new DocumentRequest(DocumentKind.PlantReport, "", Language.Value, settings.ReportPreset, PerMachine, Appendix, formats, Evidence,
            BaselineContent, Host.Value is { } h ? [h] : null, Settings: settings));
    }

    private async Task GenerateAsync(DocumentRequest request)
    {
        var folder = TargetFolder();
        if (folder is null) return;
        var files = await Session.GenerateAsync(request with { OutDir = folder });
        Written.Clear();
        foreach (var f in files) Written.Add(Path.GetRelativePath(folder, f));
        _lastFolder = folder;
        Raise(nameof(HasWritten));
    }

    /// <summary>Ausgabeordner: gewählter Ordner oder reports/ der Anlage, jeweils mit Unterordner je Erzeugung (spec 7.6).</summary>
    private string? TargetFolder()
    {
        var root = Environment.ExpandEnvironmentVariables(OutputFolder.Trim());
        if (root.Length == 0)
        {
            Browse();
            root = OutputFolder.Trim();
            if (root.Length == 0) return null;
        }
        return Session.NewOutputFolder(root);
    }

    private async Task HandoverAsync()
    {
        var folder = Dialogs.PickFolder(Loc.T("Start_CopyCollectorTitle"));
        if (folder is null) return;
        await Session.ExportCollectorAsync(folder);
    }

    private async Task PickLogoAsync()
    {
        if (Plant is null) return;
        var file = Dialogs.PickFiles(Loc.T("Reports_PickLogo"), Loc.T("Reports_LogoFilter"), multiple: false).FirstOrDefault();
        if (file is null) return;
        var info = new FileInfo(file);
        if (info.Length > ReadLimits.Default.MaxLogoBytes)
        {
            Session.Report(NoticeKind.Error, RiegelException.Common(RglCodesCommon.LimitExceeded, info.Name, ReadLimits.Default.MaxLogoBytes).ToString());
            return;
        }
        // Das Logo liegt im Anlagenordner (Kundendaten, nie ausgeliefert, spec 5 O4)
        var name = "logo" + info.Extension.ToLowerInvariant();
        var plant = Plant;
        await Session.Work(Loc.T("Busy_Saving"), () => File.Copy(file, Path.Combine(plant.Folder, name), overwrite: true));
        await Session.SavePlantAsync(p => p with { Settings = p.Settings with { CoverLogoFile = name } }, Loc.T("Notice_LogoSaved"));
    }

    public override Task PrepareScreenshotAsync()
    {
        AsDelivered = false;
        PerMachine = true;
        Evidence = true;
        return Task.CompletedTask;
    }
}
