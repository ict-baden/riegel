// SPDX-License-Identifier: MIT
using System.Collections.ObjectModel;
using Riegel.Core.Model;
using Riegel.Session;
using Riegel.UI;

namespace Riegel.ViewModels;

/// <summary>Eine zuletzt geöffnete Anlage.</summary>
internal sealed record RecentPlant(string Folder, string Name);

/// <summary>
/// Start (spec 7.8 Seite 1): Anlage öffnen oder anlegen, zuletzt geöffnete Anlagen, „Diesen PC prüfen“ (bzw. „Als
/// Administrator prüfen“ über die Übergabe – in Demo und Screenshots gesperrt), Exporte importieren (Auswahl oder Ziehen
/// und Ablegen, mit erhebender Rolle und Zeuge), Sammler kopieren, Demo, Prüfermodus. Mit geöffneter Anlage zuerst die
/// Zählungen (spec 2.6), dann die Werte.
/// </summary>
internal sealed class StartViewModel : PageViewModel
{
    private bool _openReadOnly;

    public StartViewModel(AppSession session, IDialogs dialogs) : base(session, dialogs, "start", "")
    {
        OpenCommand = new AsyncCommand(OpenAsync, () => !Session.IsBusy);
        OpenRecentCommand = new AsyncCommand(p => p is RecentPlant r ? Session.OpenAsync(r.Folder, OpenReadOnly) : Task.CompletedTask, _ => !Session.IsBusy);
        ForgetRecentCommand = new RelayCommand(p => { if (p is RecentPlant r) { Session.ForgetRecent(r.Folder); Refresh(); } });
        CreateCommand = new AsyncCommand(CreateAsync, () => !Session.IsBusy);
        DemoCommand = new AsyncCommand(() => Session.OpenDemoAsync(), () => !Session.IsBusy);
        CloseCommand = new RelayCommand(() => Session.ClosePlant(), () => Session.HasPlant && !Session.IsBusy);
        ImportCommand = new AsyncCommand(() => ImportAsync([]), () => Session.CanWrite && !Session.IsBusy);
        CheckCommand = new AsyncCommand(() => Session.CheckThisPcAsync(elevate: true), () => CanCheck);
        CheckLimitedCommand = new AsyncCommand(() => Session.CheckThisPcAsync(elevate: false), () => CanCheck && !Session.IsElevated);
        CopyCollectorCommand = new AsyncCommand(CopyCollectorAsync, () => !Session.IsBusy && Session.Mode != AppMode.Screenshots);
        GoResultsCommand = new RelayCommand(() => Session.ShowPage("results"), () => Session.HasPlant);
        GoMachinesCommand = new RelayCommand(() => Session.ShowPage("machines"), () => Session.HasPlant);
        Refresh();
    }

    public override bool NeedsPlant => false;

    public AsyncCommand OpenCommand { get; }
    public AsyncCommand OpenRecentCommand { get; }
    public RelayCommand ForgetRecentCommand { get; }
    public AsyncCommand CreateCommand { get; }
    public AsyncCommand DemoCommand { get; }
    public RelayCommand CloseCommand { get; }
    public AsyncCommand ImportCommand { get; }
    public AsyncCommand CheckCommand { get; }
    public AsyncCommand CheckLimitedCommand { get; }
    public AsyncCommand CopyCollectorCommand { get; }
    public RelayCommand GoResultsCommand { get; }
    public RelayCommand GoMachinesCommand { get; }

    /// <summary>Beim Öffnen den Prüfermodus wählen (nichts in den Anlagenordner schreiben, spec 7.6).</summary>
    public bool OpenReadOnly { get => _openReadOnly; set => Set(ref _openReadOnly, value); }

    public ObservableCollection<RecentPlant> Recent { get; } = [];

    public bool HasRecent => Recent.Count > 0;

    // ---------------------------------------------------------------- Diesen PC prüfen

    public bool CanCheck => Session.CollectionAllowed && Session.CanWrite && !Session.IsBusy && Session.Platform.CanCollect;

    /// <summary>„Diesen PC prüfen“ oder, ohne Rechte, „Als Administrator prüfen“.</summary>
    public string CheckLabel => Session.IsElevated ? Loc.T("Start_CheckThisPc") : Loc.T("Start_CheckAsAdmin");

    /// <summary>Warum die Prüfung gerade nicht geht (Demo, Screenshots, keine Anlage, Prüfermodus) – leer, wenn sie geht.</summary>
    public string CheckHint => Session.Mode == AppMode.Screenshots || Session.IsDemoPlant ? Loc.T("Start_CheckNotInDemo")
        : !Session.HasPlant ? Loc.T("Start_CheckNeedsPlant")
        : !Session.CanWrite ? Loc.T("Start_CheckReadOnly")
        : Session.IsElevated ? Loc.T("Start_CheckElevated") : Loc.T("Start_CheckNotElevated");

    public bool ShowLimitedCheck => !Session.IsElevated;

    // ---------------------------------------------------------------- Anlage

    public string PlantName => Plant?.Plant.Name ?? "";

    public string PlantFolder => Plant is null ? "" : Plant.IsDemo ? Texts.ShortPath(Plant.Folder) : Plant.Folder;

    public string PlantMode => Plant is null ? "" : Plant.IsDemo ? Loc.T("Mode_Demo") : Plant.ReadOnly ? Loc.T("Mode_ReadOnly") : "";

    public bool HasPlantMode => PlantMode.Length > 0;

    public string MachinesText => Plant is null ? "" : Loc.F("Start_Machines", Plant.Machines.Count, Plant.Machines.Count(m => m.Latest is not null));

    /// <summary>Zählungen zuerst: „Offene Befunde: 3 hoch, 12 mittel, 7 niedrig“.</summary>
    public string OpenCountsText => Plant is null ? "" : Counts(c => Loc.F("Start_OpenCounts", c.High, c.Medium, c.Low));

    public string OpenHigh => Plant is null ? "–" : Texts.Number(Plant.Counts().High);
    public string OpenMedium => Plant is null ? "–" : Texts.Number(Plant.Counts().Medium);
    public string OpenLow => Plant is null ? "–" : Texts.Number(Plant.Counts().Low);
    public string Regressions => Plant is null ? "–" : Texts.Number(Plant.Counts().Regressions);
    public string Expiring => Plant is null ? "–" : Texts.Number(Plant.Counts().Expiring);
    public string Pending => Plant is null ? "–" : Texts.Number(Plant.Counts().Pending);
    public string Overdue => Plant is null ? "–" : Texts.Number(Plant.Counts().Overdue);

    public string ScoreText => Plant is null ? "–" : Texts.Percent(Plant.PlantScore().Summary.Score);
    public int ScoreValue => Plant?.PlantScore().Summary.Score ?? 0;
    public string DocumentedText => Plant is null ? "–" : Texts.Percent(Plant.PlantScore().Summary.Documented);
    public int DocumentedValue => Plant?.PlantScore().Summary.Documented ?? 0;
    public string ShareText => Plant is null ? "–" : Texts.Percent(Plant.PlantScore().Summary.CheckedShare);

    /// <summary>„Unvollständige Prüfung“, wenn unter 90 % prüfbar (spec 2.4 Punkt 9): Werte grau.</summary>
    public bool Incomplete => Plant is not null && (Plant.PlantScore().Summary.CheckedShare < 90 || Plant.Machines.Any(m => m.Latest is { } r && (r.Flags & RunFlags.Incomplete) != 0));

    public string IncompleteText => Plant is null ? "" : Loc.F("Start_Incomplete", Plant.PlantScore().Summary.CheckedShare);

    private string Counts(Func<(int High, int Medium, int Low, int Regressions, int Expiring, int Pending, int Overdue, int NotCheckable), string> format) =>
        Plant is null ? "" : format(Plant.Counts());

    public override void OnPlantChanged()
    {
        base.OnPlantChanged();
        Refresh();
    }

    private void Refresh()
    {
        Recent.Clear();
        foreach (var folder in Session.RecentPlants)
            Recent.Add(new RecentPlant(folder, Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar))));
        RaiseAll();
    }

    // ---------------------------------------------------------------- Befehle

    private async Task OpenAsync()
    {
        var folder = Dialogs.PickFolder(Loc.T("Start_OpenTitle"), Session.Settings.DefaultPlantFolder);
        if (folder is null) return;
        await Session.OpenAsync(folder, OpenReadOnly);
    }

    private async Task CreateAsync()
    {
        var dialog = new NewPlantDialogViewModel(Dialogs, Session.Settings.DefaultPlantFolder);
        if (!Dialogs.Show(dialog)) return;
        await Session.CreateAsync(dialog.Folder, dialog.Name.Trim());
    }

    /// <summary>Import mit Dialog (Rollen, Erhebungsprotokoll); <paramref name="paths"/> vorbelegt (Ziehen und Ablegen).</summary>
    public async Task ImportAsync(IReadOnlyList<string> paths)
    {
        if (!Session.CanWrite)
        {
            Session.Report(NoticeKind.Warning, Session.HasPlant ? RiegelException.Common(RglCodesCommon.ReadOnlyMode).ToString() : RglCodesApp.Message(RglCodesApp.NoPlant));
            return;
        }
        var dialog = new ImportDialogViewModel(Dialogs, paths, Session.Plant!.Plant.Settings.OperatorRole);
        if (!Dialogs.Show(dialog)) return;
        await Session.ImportAsync([.. dialog.Files], dialog.CollectedByRole, dialog.WitnessRole, dialog.WriteRecord);
    }

    private async Task CopyCollectorAsync()
    {
        var folder = Dialogs.PickFolder(Loc.T("Start_CopyCollectorTitle"));
        if (folder is null) return;
        await Session.ExportCollectorAsync(folder);
    }
}

/// <summary>Dialog „Exporte importieren“: Dateien und Bordmittel-Pakete, erhebende Rolle, Zeuge, Erhebungsprotokoll (O14).</summary>
internal sealed class ImportDialogViewModel : ObservableObject, IDialogViewModel
{
    private readonly IDialogs _dialogs;
    private string _collectedByRole = "";
    private string _witnessRole;
    private bool _writeRecord = true;
    private string? _selected;

    public ImportDialogViewModel(IDialogs dialogs, IReadOnlyList<string> paths, string? operatorRole)
    {
        _dialogs = dialogs;
        _witnessRole = operatorRole ?? "";
        foreach (var p in paths) Files.Add(p);
        AddFilesCommand = new RelayCommand(AddFiles);
        AddFolderCommand = new RelayCommand(AddFolder);
        RemoveCommand = new RelayCommand(() => { if (Selected is { } s) Files.Remove(s); }, () => Selected is not null);
        Files.CollectionChanged += (_, _) => Raise(nameof(CanAccept));
    }

    public string Title => Loc.T("Import_Title");

    public ObservableCollection<string> Files { get; } = [];

    public string? Selected { get => _selected; set => Set(ref _selected, value); }

    public string CollectedByRole { get => _collectedByRole; set => Set(ref _collectedByRole, value); }

    public string WitnessRole { get => _witnessRole; set => Set(ref _witnessRole, value); }

    public bool WriteRecord { get => _writeRecord; set => Set(ref _writeRecord, value); }

    public RelayCommand AddFilesCommand { get; }
    public RelayCommand AddFolderCommand { get; }
    public RelayCommand RemoveCommand { get; }

    public bool CanAccept => Files.Count > 0;

    public bool Accept() => CanAccept;

    private void AddFiles()
    {
        foreach (var f in _dialogs.PickFiles(Loc.T("Import_PickFiles"), Loc.T("Import_Filter"), multiple: true))
            if (!Files.Contains(f, StringComparer.OrdinalIgnoreCase)) Files.Add(f);
    }

    private void AddFolder()
    {
        if (_dialogs.PickFolder(Loc.T("Import_PickBundle")) is { } folder && !Files.Contains(folder, StringComparer.OrdinalIgnoreCase)) Files.Add(folder);
    }

    /// <summary>Was sich importieren lässt: Exporte (.snapshot.json, .json.gz) und Ordner von Bordmittel-Paketen.</summary>
    public static bool IsImportable(string path) =>
        Directory.Exists(path)
            ? path.EndsWith(".riegel-builtin", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(path, "secedit.inf"))
            : path.EndsWith(SnapshotFile.Extension, StringComparison.OrdinalIgnoreCase) || path.EndsWith(SnapshotFile.GzipExtension, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Dialog „Neue Anlage“: Ordner und Name.</summary>
internal sealed class NewPlantDialogViewModel : ObservableObject, IDialogViewModel
{
    private readonly IDialogs _dialogs;
    private string _folder;
    private string _name = "";

    public NewPlantDialogViewModel(IDialogs dialogs, string? defaultFolder)
    {
        _dialogs = dialogs;
        _folder = defaultFolder ?? "";
        BrowseCommand = new RelayCommand(() => { if (_dialogs.PickFolder(Loc.T("NewPlant_Browse"), Folder) is { } f) Folder = f; });
    }

    public string Title => Loc.T("NewPlant_Title");

    public string Folder { get => _folder; set { if (Set(ref _folder, value)) Raise(nameof(CanAccept)); Raise(nameof(Error)); } }

    public string Name { get => _name; set { if (Set(ref _name, value)) Raise(nameof(CanAccept)); Raise(nameof(Error)); } }

    public RelayCommand BrowseCommand { get; }

    public string? Error => string.IsNullOrWhiteSpace(Folder) ? null
        : File.Exists(Path.Combine(Folder, "riegel-plant.json")) ? RglCodesCommon.Message(RglCodesCommon.AlreadyExists, Folder) : null;

    public bool CanAccept => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Folder) && Error is null && Path.IsPathRooted(Folder);

    public bool Accept() => CanAccept;
}
