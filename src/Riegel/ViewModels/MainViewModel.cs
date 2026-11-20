// SPDX-License-Identifier: MIT
using System.Windows.Threading;
using Riegel.Session;
using Riegel.UI;

namespace Riegel.ViewModels;

/// <summary>
/// Hauptfenster (spec 7.8): Navigation links wie Zutritt, Seiten rechts, Meldungen unten, Anlagenkarte mit Modus
/// (Demo, Prüfermodus). Bündelt die Tastenkürzel (<see cref="Shortcuts"/>).
/// </summary>
internal sealed class MainViewModel : ObservableObject
{
    private PageViewModel _selected;
    private string? _notice;
    private NoticeKind _noticeKind;
    private readonly DispatcherTimer? _noticeTimer;

    public MainViewModel(AppSession session, IDialogs dialogs, bool screenshotMode = false)
    {
        Session = session;
        Dialogs = dialogs;
        Start = new StartViewModel(session, dialogs);
        Machines = new MachinesViewModel(session, dialogs);
        Results = new ResultsViewModel(session, dialogs);
        Exceptions = new ExceptionsViewModel(session, dialogs);
        Tailoring = new TailoringViewModel(session, dialogs);
        Measures = new MeasuresViewModel(session, dialogs);
        Comparison = new ComparisonViewModel(session, dialogs);
        Reports = new ReportsViewModel(session, dialogs);
        Catalogue = new CatalogueViewModel(session, dialogs);
        Settings = new SettingsViewModel(session, dialogs);
        About = new AboutViewModel(session, dialogs);
        Pages = [Start, Machines, Results, Exceptions, Tailoring, Measures, Comparison, Reports, Catalogue];
        FooterPages = [Settings, About];
        _selected = Start;
        Start.IsSelected = true;

        NavigateCommand = new RelayCommand(p => { if (p is PageViewModel page) Select(page); });
        CloseNoticeCommand = new RelayCommand(() => Notice = null);
        session.PlantChanged += OnPlantChanged;
        session.Notice += OnNotice;
        session.PageRequested += OnPageRequested;
        session.PropertyChanged += OnSessionChanged;
        if (!screenshotMode && System.Windows.Application.Current is not null)
        {
            _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _noticeTimer.Tick += (_, _) => { _noticeTimer.Stop(); Notice = null; };
        }
        OnPlantChanged();
    }

    public AppSession Session { get; }

    public IDialogs Dialogs { get; }

    private void OnSessionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSession.IsBusy) or nameof(AppSession.BusyText) or "")
        {
            Raise(nameof(IsBusy));
            Raise(nameof(IsIdle));
            Raise(nameof(BusyText));
            if (System.Windows.Application.Current is not null) System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Fenster geschlossen (z. B. Sprachwechsel): von der Sitzung abmelden.</summary>
    public void Detach()
    {
        Session.PlantChanged -= OnPlantChanged;
        Session.Notice -= OnNotice;
        Session.PageRequested -= OnPageRequested;
        Session.PropertyChanged -= OnSessionChanged;
        _noticeTimer?.Stop();
    }

    public StartViewModel Start { get; }
    public MachinesViewModel Machines { get; }
    public ResultsViewModel Results { get; }
    public ExceptionsViewModel Exceptions { get; }
    public TailoringViewModel Tailoring { get; }
    public MeasuresViewModel Measures { get; }
    public ComparisonViewModel Comparison { get; }
    public ReportsViewModel Reports { get; }
    public CatalogueViewModel Catalogue { get; }
    public SettingsViewModel Settings { get; }
    public AboutViewModel About { get; }

    /// <summary>Seiten der Navigation (oben), Einstellungen und Info unten.</summary>
    public IReadOnlyList<PageViewModel> Pages { get; }
    public IReadOnlyList<PageViewModel> FooterPages { get; }
    public IEnumerable<PageViewModel> AllPages => Pages.Concat(FooterPages);

    public RelayCommand NavigateCommand { get; }
    public RelayCommand CloseNoticeCommand { get; }

    public PageViewModel SelectedPage => _selected;

    public bool IsBusy => Session.IsBusy;

    /// <summary>Keine Arbeit läuft: Navigation und Seiten sind bedienbar (während der Arbeit gesperrt, auch für Tastatur).</summary>
    public bool IsIdle => !Session.IsBusy;
    public string BusyText => Session.BusyText;

    // ---------------------------------------------------------------- Anlagenkarte

    public string PlantTitle => Session.Plant?.Plant.Name ?? Loc.T("Main_NoPlant");

    public string PlantDetail => Session.Plant is not { } p ? Loc.T("Main_NoPlantHint")
        : p.IsDemo ? Loc.F("Main_DemoDetail", Texts.Date(p.Today))
        : p.ReadOnly ? Loc.T("Mode_ReadOnly")
        : Loc.P("Main_PlantDetail", p.Machines.Count, p.Machines.Count);

    /// <summary>Kennzeichen über allen Seiten: Prüfermodus (nichts wird geschrieben) bzw. Demo.</summary>
    public string? ModeBanner => Session.Plant is not { } p ? null
        : p.ReadOnly ? Loc.T("Main_ReadOnlyBanner")
        : p.IsDemo ? Loc.T("Main_DemoBanner") : null;

    public bool HasModeBanner => ModeBanner is not null;

    public string WindowTitle => Session.Plant is { } p ? $"{p.Plant.Name} – Riegel" + (p.IsDemo ? " – " + Loc.T("Mode_Demo") : p.ReadOnly ? " – " + Loc.T("Mode_ReadOnly") : "") : "Riegel";

    // ---------------------------------------------------------------- Meldungen

    public string? Notice
    {
        get => _notice;
        private set { if (Set(ref _notice, value)) Raise(nameof(HasNotice)); }
    }

    public bool HasNotice => Notice is not null;

    public string NoticeTone => _noticeKind switch { NoticeKind.Error => "Critical", NoticeKind.Warning => "Caution", NoticeKind.Success => "Good", _ => "Info" };

    public string NoticeGlyph => _noticeKind switch { NoticeKind.Error => "", NoticeKind.Warning => "", NoticeKind.Success => "", _ => "" };

    private void OnNotice(NoticeKind kind, string text)
    {
        _noticeKind = kind;
        Notice = text;
        Raise(nameof(NoticeTone));
        Raise(nameof(NoticeGlyph));
        _noticeTimer?.Stop();
        if (kind is NoticeKind.Info or NoticeKind.Success) _noticeTimer?.Start();
    }

    // ---------------------------------------------------------------- Navigation

    public void Select(PageViewModel page)
    {
        foreach (var p in AllPages) p.IsSelected = ReferenceEquals(p, page);
        if (ReferenceEquals(_selected, page)) { page.OnShown(); return; }
        _selected = page;
        Raise(nameof(SelectedPage));
        page.OnShown();
    }

    public PageViewModel Page(string slug) => AllPages.FirstOrDefault(p => p.Slug == slug) ?? Start;

    private void OnPageRequested(string slug, string? argument)
    {
        var page = Page(slug);
        Select(page);
        switch (page)
        {
            case ResultsViewModel r: r.SelectHost(argument); break;
            case ComparisonViewModel c: c.SelectHost(argument); break;
            case ExceptionsViewModel e: e.SelectEntry(argument); break;
        }
    }

    private void OnPlantChanged()
    {
        foreach (var p in AllPages) p.OnPlantChanged();
        foreach (var n in new[] { nameof(PlantTitle), nameof(PlantDetail), nameof(ModeBanner), nameof(HasModeBanner), nameof(WindowTitle) }) Raise(n);
    }

    // ---------------------------------------------------------------- Tastenkürzel

    /// <summary>Was die Ansicht nach einem Kürzel noch tun soll (Fokus auf Suche oder Berichtsknopf).</summary>
    public enum FocusRequest { None, Search, ReportButton }

    /// <summary>Kürzel ausführen; true = erledigt. Die Prüfung von Strg/AltGr steckt in <see cref="Shortcuts.Resolve"/>.</summary>
    public (bool Handled, FocusRequest Focus) Execute(ShortcutAction action, int page)
    {
        // Während einer Arbeit nichts Neues starten und nicht die Seite wechseln (zwei Schreibvorgänge im Anlagenordner);
        // nur die Hilfe bleibt. Das Kürzel gilt als erledigt, damit es nicht an die gesperrte Seite geht.
        if (Session.IsBusy && action != ShortcutAction.Help) return (true, FocusRequest.None);
        switch (action)
        {
            case ShortcutAction.OpenPlant:
                Select(Start);
                Start.OpenCommand.Execute(null);
                return (true, FocusRequest.None);
            case ShortcutAction.Import:
                Select(Start);
                Start.ImportCommand.Execute(null);
                return (true, FocusRequest.None);
            case ShortcutAction.CheckThisPc:
                if (!Start.CheckCommand.CanExecute(null))
                {
                    if (!Session.CollectionAllowed) Session.Report(NoticeKind.Info, RglCodesApp.Message(RglCodesApp.NotInDemo));
                    return (true, FocusRequest.None);
                }
                Start.CheckCommand.Execute(null);
                return (true, FocusRequest.None);
            case ShortcutAction.Exception:
                if (SelectedPage is ResultsViewModel results && results.ExceptionCommand.CanExecute(null))
                {
                    results.ExceptionCommand.Execute(null);
                    return (true, FocusRequest.None);
                }
                return (false, FocusRequest.None);
            case ShortcutAction.Report:
                Select(Reports);
                return (true, FocusRequest.ReportButton);
            case ShortcutAction.Help:
                if (Shell.OpenLink(Session.Platform, AboutViewModel.HelpLink) is { } problem) Session.Report(NoticeKind.Warning, problem);
                return (true, FocusRequest.None);
            case ShortcutAction.Search:
                return SelectedPage.HasSearch ? (true, FocusRequest.Search) : (false, FocusRequest.None);
            case ShortcutAction.Page:
                var all = AllPages.ToList();
                if (page >= 0 && page < all.Count) Select(all[page]);
                return (true, FocusRequest.None);
            default:
                return (false, FocusRequest.None);
        }
    }
}
