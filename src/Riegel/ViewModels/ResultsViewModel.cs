// SPDX-License-Identifier: MIT
using System.Collections.ObjectModel;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Session;
using Riegel.UI;

namespace Riegel.ViewModels;

/// <summary>Eine Zeile der Ergebnisliste.</summary>
internal sealed class ResultRow
{
    public required RuleResult Result { get; init; }
    public Rule? Rule { get; init; }
    public string RuleId => Result.RuleId;
    public string Title { get; init; } = "";
    public string Category { get; init; } = "";
    public string CategoryText { get; init; } = "";
    public Severity Severity => Result.Severity;
    public string SeverityText => Texts.Severity(Severity);
    public ResultStatus Status => Result.Status;
    public string FlagsText { get; init; } = "";
    public string FlagsSuffix => FlagsText.Length == 0 ? "" : " · " + FlagsText;
    public string Actual => Texts.Value(Result.Actual);
    public int Order { get; init; }
}

/// <summary>Eine Statusauswahl als Chip mit Zahl.</summary>
internal sealed class StatusFilter(ResultStatus? status, string text) : ObservableObject
{
    private int _count;
    private bool _checked;

    public ResultStatus? Status { get; } = status;
    public string Text { get; } = text;
    public int Count { get => _count; set { if (Set(ref _count, value)) Raise(nameof(Label)); } }
    public string Label => Loc.F("Filter_WithCount", Text, Count);
    public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
}

/// <summary>
/// Ergebnisse je Rechner (spec 7.8 Seite 3): Filter nach Status, Kategorie, Schwere und Flags, Suche; Detail mit Soll,
/// Ist, Herkunft, Flags in Worten, Begründung, OT-Hinweis, Verweisen, Abhilfe und Belegen. Von hier aus: „Als bewusst
/// offen begründen…“ (Strg+E), „Zuschneiden…“, „Maßnahme planen…“.
/// </summary>
internal sealed class ResultsViewModel : PageViewModel
{
    private static readonly ResultStatus[] StatusOrder =
        [ResultStatus.Fail, ResultStatus.Exception, ResultStatus.NotCheckable, ResultStatus.OutOfScope, ResultStatus.Info, ResultStatus.NotApplicable, ResultStatus.Pass];

    private readonly List<ResultRow> _all = [];
    private Choice<string>? _host;
    private ResultRow? _selected;
    private string _search = "";
    private Choice<string?> _category;
    private Choice<Severity?> _severity;
    private Choice<ResultFlags> _flag;
    private bool _suppress;

    public ResultsViewModel(AppSession session, IDialogs dialogs) : base(session, dialogs, "results", "")
    {
        Statuses = [new StatusFilter(null, Loc.T("Filter_All")), .. StatusOrder.Select(s => new StatusFilter(s, Texts.Status(s)))];
        Statuses[0].IsChecked = true;
        Categories = [new Choice<string?>(null, Loc.T("Filter_AllCategories")), .. RuleCategories.All.Select(c => new Choice<string?>(c, Texts.Category(c)))];
        _category = Categories[0];
        Severities = [new Choice<Severity?>(null, Loc.T("Filter_AllSeverities")), .. new[] { Severity.High, Severity.Medium, Severity.Low }.Select(s => new Choice<Severity?>(s, Texts.Severity(s)))];
        _severity = Severities[0];
        FlagChoices = [new Choice<ResultFlags>(ResultFlags.None, Loc.T("Filter_AllFlags")),
            .. Enum.GetValues<ResultFlags>().Where(f => f != ResultFlags.None).Select(f => new Choice<ResultFlags>(f, Loc.C("Flag_" + f)))];
        _flag = FlagChoices[0];
        StatusCommand = new RelayCommand(p => { if (p is StatusFilter f) SelectStatus(f); });
        ExceptionCommand = new AsyncCommand(ExceptionAsync, () => CanException);
        TailorCommand = new AsyncCommand(TailorAsync, () => CanTailor);
        MeasureCommand = new AsyncCommand(MeasureAsync, () => CanMeasure);
        ReEvaluateCommand = new AsyncCommand(() => Session.ReEvaluateAsync(Host is null ? null : [Host.Value]), () => Session.CanWrite && Host is not null);
        ShowExceptionCommand = new RelayCommand(() => Session.ShowPage("exceptions", Selected?.Result.ExceptionId), () => Selected?.Result.ExceptionId is not null);
    }

    public override bool HasSearch => true;

    // ---------------------------------------------------------------- Auswahl und Filter

    public ObservableCollection<Choice<string>> Hosts { get; } = [];

    public Choice<string>? Host
    {
        get => _host;
        set { if (Set(ref _host, value)) Load(); }
    }

    public IReadOnlyList<StatusFilter> Statuses { get; }
    public IReadOnlyList<Choice<string?>> Categories { get; }
    public IReadOnlyList<Choice<Severity?>> Severities { get; }
    public IReadOnlyList<Choice<ResultFlags>> FlagChoices { get; }

    public Choice<string?> Category { get => _category; set { if (Set(ref _category, value)) Apply(); } }
    public Choice<Severity?> SeverityFilter { get => _severity; set { if (Set(ref _severity, value)) Apply(); } }
    public Choice<ResultFlags> Flag { get => _flag; set { if (Set(ref _flag, value)) Apply(); } }
    public string Search { get => _search; set { if (Set(ref _search, value)) Apply(); } }

    public RelayCommand StatusCommand { get; }
    public AsyncCommand ExceptionCommand { get; }
    public AsyncCommand TailorCommand { get; }
    public AsyncCommand MeasureCommand { get; }
    public AsyncCommand ReEvaluateCommand { get; }
    public RelayCommand ShowExceptionCommand { get; }

    public ObservableCollection<ResultRow> Rows { get; } = [];

    public ResultRow? Selected { get => _selected; set { if (Set(ref _selected, value)) RaiseDetail(); } }

    public bool HasRows => Rows.Count > 0;

    public string RunText { get; private set; } = "";

    /// <summary>Lauf mit unvollständiger Prüfung oder veralteter Erfassung: Hinweis über der Liste.</summary>
    public string RunWarning { get; private set; } = "";

    public bool HasRunWarning => RunWarning.Length > 0;

    private MachineState? Machine => Plant?.Machine(Host?.Value);

    public override void OnPlantChanged()
    {
        base.OnPlantChanged();
        var host = Host?.Value;
        _suppress = true;
        Hosts.Clear();
        foreach (var m in Plant?.Machines ?? [])
            if (m.Latest is not null) Hosts.Add(new Choice<string>(m.HostName, string.IsNullOrEmpty(m.Entry?.Label) ? m.HostName : $"{m.HostName} – {m.Entry!.Label}"));
        _suppress = false;
        _host = Hosts.FirstOrDefault(h => string.Equals(h.Value, host, StringComparison.OrdinalIgnoreCase)) ?? Hosts.FirstOrDefault();
        Raise(nameof(Host));
        Load();
        int open = Plant?.Machines.Sum(m => m.Latest?.Summary.Fail ?? 0) ?? 0;
        Badge = open == 0 ? null : Texts.Number(open);
        BadgeTone = (Plant?.Counts().High ?? 0) > 0 ? "High" : open > 0 ? "Medium" : "";
    }

    /// <summary>Rechner wählen (von anderen Seiten: „Ergebnisse anzeigen“).</summary>
    public void SelectHost(string? host)
    {
        if (host is null) return;
        var choice = Hosts.FirstOrDefault(h => string.Equals(h.Value, host, StringComparison.OrdinalIgnoreCase));
        if (choice is not null) Host = choice;
    }

    private void Load()
    {
        if (_suppress) return;
        var rule = Selected?.RuleId;
        _all.Clear();
        var machine = Machine;
        RunText = "";
        RunWarning = "";
        if (machine?.Latest is { } run && Plant is { } plant)
        {
            var language = AppSession.Language;
            foreach (var r in run.Results)
            {
                var def = plant.Rule(r.RuleId);
                var category = def?.Category ?? plant.Category(r.RuleId);
                _all.Add(new ResultRow
                {
                    Result = r,
                    Rule = def,
                    Title = plant.Title(r.RuleId, language),
                    Category = category,
                    CategoryText = Texts.Category(category),
                    FlagsText = Texts.FlagsLine(r.Flags),
                    Order = Array.IndexOf(StatusOrder, r.Status) * 10 + (2 - (int)r.Severity),
                });
            }
            RunText = Loc.F("Results_Run", Texts.DateTime(run.CollectedAtUtc), Texts.Date(run.EvaluationDate), run.Results.Count);
            var warnings = Texts.RunFlags(run.Flags & (RunFlags.Incomplete | RunFlags.StaleSnapshot | RunFlags.ClockSuspect | RunFlags.MachineIdentityChanged | RunFlags.DomainController));
            if (warnings.Count > 0) RunWarning = string.Join(" · ", warnings) + ((run.Flags & RunFlags.Incomplete) != 0 ? " – " + Loc.F("Results_Share", run.Summary.CheckedShare) : "");
        }
        foreach (var f in Statuses) f.Count = f.Status is null ? _all.Count : _all.Count(r => r.Status == f.Status);
        Raise(nameof(RunText));
        Raise(nameof(RunWarning));
        Raise(nameof(HasRunWarning));
        Apply(rule);
    }

    private void SelectStatus(StatusFilter filter)
    {
        foreach (var f in Statuses) f.IsChecked = ReferenceEquals(f, filter);
        Apply();
    }

    /// <summary>Statusfilter von außen setzen (Screenshots, Tests).</summary>
    public void FilterStatus(ResultStatus? status) => SelectStatus(Statuses.First(f => f.Status == status));

    private void Apply(string? keepRule = null)
    {
        keepRule ??= Selected?.RuleId;
        var status = Statuses.FirstOrDefault(f => f.IsChecked)?.Status;
        var search = Search.Trim();
        var rows = _all.Where(r => status is null || r.Status == status)
            .Where(r => Category.Value is null || r.Category == Category.Value)
            .Where(r => SeverityFilter.Value is null || r.Severity == SeverityFilter.Value)
            .Where(r => Flag.Value == ResultFlags.None || (r.Result.Flags & Flag.Value) != 0)
            .Where(r => search.Length == 0 || r.RuleId.Contains(search, StringComparison.OrdinalIgnoreCase) || r.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || (r.Result.Actual?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) || r.FlagsText.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Order).ThenBy(r => r.RuleId, StringComparer.Ordinal)
            .ToList();
        Rows.Clear();
        foreach (var r in rows) Rows.Add(r);
        Selected = Rows.FirstOrDefault(r => r.RuleId == keepRule) ?? Rows.FirstOrDefault();
        Raise(nameof(HasRows));
    }

    /// <summary>Regel auswählen (Screenshots, Tests, Sprung von anderen Seiten).</summary>
    public void SelectRule(string ruleId)
    {
        if (Rows.FirstOrDefault(r => r.RuleId == ruleId) is not { } row)
        {
            FilterStatus(null);
            row = Rows.FirstOrDefault(r => r.RuleId == ruleId)!;
        }
        Selected = row;
    }

    // ---------------------------------------------------------------- Detail

    public bool HasSelection => Selected is not null;
    public string DetailTitle => Selected?.Title ?? "";
    public string DetailRule => Selected is null ? ""
        : (Selected.Rule?.Kind == RuleKind.Inventory ? Loc.F("Results_RuleInventory", Selected.RuleId) : Selected.RuleId) + " · " + Selected.CategoryText;
    public string DetailExpected => Selected is null ? "" : Texts.Value(Selected.Result.Expected);
    public string DetailActual => Selected is null ? "" : Texts.Value(Selected.Result.Actual);
    public bool HasExpected => Selected?.Result.Expected is not null || Selected?.Result.Status is ResultStatus.Pass or ResultStatus.Fail or ResultStatus.Exception;

    public string DetailSource => Selected is null ? "" : Selected.Result.Source switch
    {
        ValueSource.DomainGpo => Loc.F("Source_DomainGpoNamed", Selected.Result.SourceDetail ?? "?"),
        _ => Texts.Source(Selected.Result.Source) + (Selected.Result.SourceDetail is { } d ? $" ({d})" : ""),
    };

    public IReadOnlyList<string> DetailFlags => Selected is null ? [] : Texts.Flags(Selected.Result.Flags);
    public bool HasFlags => DetailFlags.Count > 0;
    public string DetailRationale => Selected?.Rule?.Rationale.Get(AppSession.Language) ?? "";
    public string DetailOtNote => Selected?.Rule?.OtNote?.Get(AppSession.Language) ?? "";
    public bool HasOtNote => DetailOtNote.Length > 0;
    public string DetailRemedy => Selected?.Rule?.Remedy?.Get(AppSession.Language) ?? "";
    public bool HasRemedy => DetailRemedy.Length > 0;
    public IReadOnlyList<string> DetailDetails => Selected?.Result.Details ?? [];
    public bool HasDetails => DetailDetails.Count > 0;
    public string DetailReason => Texts.ReasonCode(Selected?.Result.ReasonCode);
    public bool HasReason => DetailReason.Length > 0;
    public IReadOnlyList<string> DetailEvidence => Selected?.Result.EvidenceKeys ?? [];
    public bool HasEvidence => DetailEvidence.Count > 0;

    /// <summary>Verweise der Regel lesbar (ACSC-Abschnitt, MS-Docs, ATT&amp;CK-Kennungen …).</summary>
    public IReadOnlyList<string> DetailReferences => Selected?.Rule is not { } rule ? [] : [.. rule.References.Select(Reference)];
    public bool HasReferences => DetailReferences.Count > 0;

    internal static string Reference(RuleReference r)
    {
        var parts = new List<string>();
        foreach (var p in new[] { r.Doc, r.Section, r.Setting, r.Id }) if (!string.IsNullOrWhiteSpace(p)) parts.Add(p);
        if (r.Ids is { Count: > 0 } ids) parts.Add(string.Join(", ", ids));
        return parts.Count == 0 ? r.Src : $"{r.Src}: {string.Join(" – ", parts)}";
    }

    /// <summary>Registereinträge zu diesem Ergebnis: Ausnahme, Zuschnitt, Maßnahme (mit Zustand).</summary>
    public IReadOnlyList<string> DetailRegisters
    {
        get
        {
            if (Selected is null || Plant is null) return [];
            var list = new List<string>();
            var r = Selected.Result;
            if (r.ExceptionId is { } ex && Plant.Registers.Exceptions.Exceptions.FirstOrDefault(e => e.Id == ex) is { } entry)
                list.Add(Loc.F("Results_RegisterException", ex, Texts.EntryState(Plant.Info(entry).State), Texts.Reason(entry.Category)));
            if (r.TailoringId is { } tl && Plant.Registers.Tailoring.Entries.FirstOrDefault(e => e.Id == tl) is { } t)
                list.Add(Loc.F("Results_RegisterTailoring", tl, Texts.EntryState(Plant.Info(t).State)));
            if (r.MeasureId is { } mid && Plant.Registers.Measures.Measures.FirstOrDefault(e => e.Id == mid) is { } m)
                list.Add(Loc.F("Results_RegisterMeasure", mid, Texts.Date(m.DueDate), m.OwnerRole));
            return list;
        }
    }

    public bool HasRegisters => DetailRegisters.Count > 0;

    private void RaiseDetail()
    {
        foreach (var name in new[] { nameof(HasSelection), nameof(DetailTitle), nameof(DetailRule), nameof(DetailExpected), nameof(DetailActual),
                     nameof(HasExpected), nameof(DetailSource), nameof(DetailFlags), nameof(HasFlags), nameof(DetailRationale), nameof(DetailOtNote),
                     nameof(HasOtNote), nameof(DetailRemedy), nameof(HasRemedy), nameof(DetailDetails), nameof(HasDetails), nameof(DetailReason),
                     nameof(HasReason), nameof(DetailEvidence), nameof(HasEvidence), nameof(DetailReferences), nameof(HasReferences),
                     nameof(DetailRegisters), nameof(HasRegisters), nameof(CanException), nameof(CanTailor), nameof(CanMeasure) })
            Raise(name);
    }

    // ---------------------------------------------------------------- Register-Dialoge

    public bool CanException => Session.CanWrite && Selected is { } s && s.Rule?.Kind != RuleKind.Inventory
                                && s.Status is ResultStatus.Fail or ResultStatus.Exception;
    public bool CanTailor => Session.CanWrite && Selected is { } s && s.Status is not (ResultStatus.Info or ResultStatus.NotApplicable);
    public bool CanMeasure => Session.CanWrite && Selected is { } s && s.Status is ResultStatus.Fail or ResultStatus.Exception or ResultStatus.NotCheckable;

    private async Task ExceptionAsync()
    {
        if (Selected is null || Plant is null || Host is null) return;
        var existing = Selected.Result.ExceptionId is { } id ? Plant.Registers.Exceptions.Exceptions.FirstOrDefault(e => e.Id == id) : null;
        var dialog = existing is null
            ? ExceptionDialogViewModel.ForResult(Plant, Host.Value, Selected.Result, Selected.Rule, Selected.Title)
            : ExceptionDialogViewModel.ForEntry(Plant, existing, Selected.Rule, Selected.Title);
        if (!Dialogs.Show(dialog)) return;
        await Session.SaveExceptionAsync(dialog.Result!);
    }

    private async Task TailorAsync()
    {
        if (Selected is null || Plant is null || Host is null) return;
        var existing = Selected.Result.TailoringId is { } id ? Plant.Registers.Tailoring.Entries.FirstOrDefault(e => e.Id == id) : null;
        var dialog = new TailoringDialogViewModel(Plant, existing, Host.Value, Selected.RuleId, Selected.Rule, Selected.Title);
        if (!Dialogs.Show(dialog)) return;
        await Session.SaveTailoringAsync(dialog.Result!);
    }

    private async Task MeasureAsync()
    {
        if (Selected is null || Plant is null || Host is null) return;
        var existing = Selected.Result.MeasureId is { } id ? Plant.Registers.Measures.Measures.FirstOrDefault(e => e.Id == id) : null;
        var dialog = new MeasureDialogViewModel(Plant, existing, Host.Value, Selected.RuleId, Selected.Rule, Selected.Title);
        if (!Dialogs.Show(dialog)) return;
        await Session.SaveMeasureAsync(dialog.Result!);
    }

    public override Task PrepareScreenshotAsync()
    {
        // Bedienstation 2: Verschlechterung (SMBv1) gegenüber dem freigegebenen Stand
        SelectHost("HMI-L3-02");
        FilterStatus(null);
        if (Rows.FirstOrDefault(r => (r.Result.Flags & ResultFlags.Regression) != 0) is { } regression) Selected = regression;
        return Task.CompletedTask;
    }
}
