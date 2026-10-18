// SPDX-License-Identifier: MIT
using System.Collections.ObjectModel;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Session;
using Riegel.UI;

namespace Riegel.ViewModels;

/// <summary>Eine Zeile im Ausnahmen- oder Zuschnittregister.</summary>
internal sealed class EntryRow : ObservableObject
{
    private bool _checked;

    public required string Id { get; init; }
    public required string RuleId { get; init; }
    public string Title { get; init; } = "";
    public Severity? Severity { get; init; }
    public string ScopeText { get; init; } = "";
    public string CategoryText { get; init; } = "";
    public RegisterEntryState State { get; init; }
    public string StateText => Texts.EntryState(State);
    public string StateTone => Texts.EntryTone(State);
    public RegisterFlags Flags { get; init; }
    public string FlagsText => string.Join(" · ", Texts.RegisterFlagTexts(Flags));
    public string ReviewBy { get; init; } = "–";
    public string ReviewLine => ReviewBy is "" or "–" ? "" : Loc.F("Register_ReviewByLine", ReviewBy);
    public string Record { get; init; } = "";
    public IReadOnlyList<string> Hosts { get; init; } = [];
    public IReadOnlyList<ValidationIssue> Issues { get; init; } = [];
    public object? Entry { get; init; }

    /// <summary>Für „Genehmigung eintragen“ und das Genehmigungsblatt markiert.</summary>
    public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
}

/// <summary>Filter des Registers (Zustand oder Kennzeichen).</summary>
internal sealed class EntryFilter(string key, string text, Func<EntryRow, bool> match) : ObservableObject
{
    private int _count;
    private bool _checked;

    public string Key { get; } = key;
    public string Text { get; } = text;
    public Func<EntryRow, bool> Match { get; } = match;
    public int Count { get => _count; set { if (Set(ref _count, value)) Raise(nameof(Label)); } }
    public string Label => Loc.F("Filter_WithCount", Text, Count);
    public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
}

/// <summary>Gemeinsames von Ausnahmen- und Zuschnittregister: Filter, Auswahl, Genehmigung, Genehmigungsblatt.</summary>
internal abstract class RegisterPageViewModel : PageViewModel
{
    private readonly List<EntryRow> _all = [];
    private EntryRow? _selected;
    private string _search = "";

    protected RegisterPageViewModel(AppSession session, IDialogs dialogs, string slug, string glyph, RegisterKind kind) : base(session, dialogs, slug, glyph)
    {
        Kind = kind;
        FilterCommand = new RelayCommand(p => { if (p is EntryFilter f) SelectFilter(f); });
        ApproveCommand = new AsyncCommand(ApproveAsync, () => Session.CanWrite && Targets().Count > 0);
        SheetCommand = new AsyncCommand(SheetAsync, () => Session.HasPlant && (Targets().Count > 0 || _all.Any(r => r.State is RegisterEntryState.Pending or RegisterEntryState.ChangedAfterApproval)));
        RetireCommand = new AsyncCommand(RetireAsync, () => Session.CanWrite && Selected is not null);
        TemplateCommand = new AsyncCommand(TemplateAsync, () => Session.CanWrite);
        RenewCommand = new AsyncCommand(RenewAsync, () => Session.CanWrite && Selected is { State: not RegisterEntryState.Pending });
        // Neue Einträge entstehen aus einem Ergebnis (Regel, Rechner und Istwert stehen dann fest)
        NewCommand = new RelayCommand(() =>
        {
            Session.ShowPage("results");
            Session.Report(NoticeKind.Info, Loc.T(Kind == RegisterKind.Exceptions ? "Exceptions_NewHint" : "Tailoring_NewHint"));
        }, () => Session.CanWrite);
    }

    public RelayCommand NewCommand { get; }

    protected RegisterKind Kind { get; }

    public override bool HasSearch => true;

    public ObservableCollection<EntryFilter> Filters { get; } = [];

    public ObservableCollection<EntryRow> Rows { get; } = [];

    public EntryRow? Selected { get => _selected; set { if (Set(ref _selected, value)) RaiseDetail(); } }

    public string Search { get => _search; set { if (Set(ref _search, value)) Apply(); } }

    public bool HasRows => Rows.Count > 0;

    public bool CanWrite => Session.CanWrite;

    public RelayCommand FilterCommand { get; }
    public AsyncCommand ApproveCommand { get; }
    public AsyncCommand SheetCommand { get; }
    public AsyncCommand RetireCommand { get; }
    public AsyncCommand TemplateCommand { get; }
    public AsyncCommand RenewCommand { get; }

    protected abstract IEnumerable<EntryRow> BuildRows(PlantContext plant);

    protected abstract IEnumerable<EntryFilter> BuildFilters();

    public override void OnPlantChanged()
    {
        base.OnPlantChanged();
        var keep = Selected?.Id;
        var checkedIds = _all.Where(r => r.IsChecked).Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _all.Clear();
        if (Plant is { } plant) _all.AddRange(BuildRows(plant));
        foreach (var r in _all) r.IsChecked = checkedIds.Contains(r.Id);
        var active = Filters.FirstOrDefault(f => f.IsChecked)?.Key;
        Filters.Clear();
        foreach (var f in BuildFilters()) Filters.Add(f);
        foreach (var f in Filters) f.Count = _all.Count(f.Match);
        (Filters.FirstOrDefault(f => f.Key == active) ?? Filters[0]).IsChecked = true;
        Apply(keep);
        int attention = _all.Count(r => r.State is RegisterEntryState.Pending or RegisterEntryState.ChangedAfterApproval or RegisterEntryState.Expiring
            or RegisterEntryState.Expired or RegisterEntryState.Invalid);
        Badge = attention == 0 ? null : Texts.Number(attention);
        BadgeTone = attention == 0 ? "" : "Medium";
        Raise(nameof(CanWrite));
    }

    private void SelectFilter(EntryFilter filter)
    {
        foreach (var f in Filters) f.IsChecked = ReferenceEquals(f, filter);
        Apply();
    }

    public void FilterBy(string key) => SelectFilter(Filters.First(f => f.Key == key));

    private void Apply(string? keep = null)
    {
        keep ??= Selected?.Id;
        var filter = Filters.FirstOrDefault(f => f.IsChecked);
        var search = Search.Trim();
        Rows.Clear();
        foreach (var r in _all.Where(r => filter is null || filter.Match(r))
                     .Where(r => search.Length == 0 || r.Id.Contains(search, StringComparison.OrdinalIgnoreCase) || r.RuleId.Contains(search, StringComparison.OrdinalIgnoreCase)
                                 || r.Title.Contains(search, StringComparison.OrdinalIgnoreCase) || r.ScopeText.Contains(search, StringComparison.OrdinalIgnoreCase)))
            Rows.Add(r);
        Selected = Rows.FirstOrDefault(r => r.Id == keep) ?? Rows.FirstOrDefault();
        Raise(nameof(HasRows));
    }

    /// <summary>Eintrag auswählen (Sprung von „Ergebnisse“).</summary>
    public void SelectEntry(string? id)
    {
        if (id is null) return;
        if (Rows.FirstOrDefault(r => r.Id == id) is not { } row)
        {
            SelectFilter(Filters[0]);
            row = Rows.FirstOrDefault(r => r.Id == id)!;
        }
        Selected = row;
    }

    protected virtual void RaiseDetail()
    {
        Raise(nameof(HasSelection));
        Raise(nameof(SelectedIssues));
        Raise(nameof(HasIssues));
        Raise(nameof(SelectedHosts));
    }

    public bool HasSelection => Selected is not null;

    public IReadOnlyList<string> SelectedIssues => Selected is null ? [] : [.. Selected.Issues.Select(i => $"{i.Code}: {i.Message}")];

    public bool HasIssues => SelectedIssues.Count > 0;

    public string SelectedHosts => Selected is null ? "" : Selected.Hosts.Count == 0 ? Loc.T("Register_NoHosts")
        : string.Join(", ", Selected.Hosts) is var hosts && hosts == Selected.ScopeText ? "" : hosts;

    /// <summary>Markierte Einträge, sonst der ausgewählte.</summary>
    private List<EntryRow> Targets()
    {
        var marked = _all.Where(r => r.IsChecked).ToList();
        return marked.Count > 0 ? marked : Selected is null ? [] : [Selected];
    }

    /// <summary>Rollen für die Freigabe: Freigabe-Rollen der Anlage, Rollen für „Risiko akzeptiert“, Standardrollen.</summary>
    protected IEnumerable<string> ApprovalRoles()
    {
        if (Plant is null) yield break;
        foreach (var s in Plant.Plant.Settings.SignOff) yield return s.Role;
        foreach (var r in Plant.Plant.Settings.RiskAcceptanceRoles) yield return r;
        yield return Loc.C("Role_PlantOwner");
        yield return Loc.C("Role_SecurityOfficer");
        yield return Loc.C("Role_QualityAssurance");
        yield return Loc.C("Role_PlantManagement");
    }

    private async Task ApproveAsync()
    {
        var targets = Targets();
        if (targets.Count == 0 || Plant is null) return;
        var dialog = new ApprovalDialogViewModel(Loc.T("Approval_Title"), Loc.T("Approval_Intro"),
            [.. targets.Select(t => $"{t.Id} · {t.RuleId} · {t.Title}")], ApprovalRoles(), Plant.Today, withReview: true);
        if (!Dialogs.Show(dialog)) return;
        await Session.ApproveEntriesAsync(Kind, [.. targets.Select(t => t.Id)], dialog.Result!);
        foreach (var r in _all) r.IsChecked = false;
    }

    /// <summary>„Genehmigungsblatt erstellen“ (O12): markierte Einträge, sonst alle beantragten und geänderten.</summary>
    private async Task SheetAsync()
    {
        if (Plant is null) return;
        var marked = _all.Where(r => r.IsChecked).Select(r => r.Id).ToList();
        var folder = Session.DefaultOutputFolder is { } d ? Session.NewOutputFolder(d) : Dialogs.PickFolder(Loc.T("Reports_PickFolder"));
        if (folder is null) return;
        var files = await Session.GenerateAsync(new DocumentRequest(DocumentKind.ApprovalSheet, folder, Session.ReportLanguage, Ids: marked.Count == 0 ? null : marked));
        if (files.FirstOrDefault() is { } file && Dialogs.Reveal(file) is { } problem) Session.Report(NoticeKind.Warning, problem);
    }

    private async Task RetireAsync()
    {
        if (Selected is null) return;
        if (!Dialogs.Confirm(Loc.T("Register_RetireTitle"), Loc.F("Register_RetireText", Selected.Id))) return;
        await Session.RetireAsync(Kind, Selected.Id);
    }

    private async Task TemplateAsync()
    {
        var file = Dialogs.PickFiles(Loc.T("Register_TemplateTitle"), Loc.T("Register_TemplateFilter"), multiple: false).FirstOrDefault();
        if (file is null) return;
        await Session.ImportTemplateAsync(file);
    }

    /// <summary>„Verlängern“: dieselbe Fassung mit neuem Prüftermin und Nachweis erneut freigeben.</summary>
    private async Task RenewAsync()
    {
        if (Selected is null || Plant is null) return;
        var dialog = new ApprovalDialogViewModel(Loc.F("Renew_Title", Selected.Id), Loc.T("Renew_Intro"),
            [$"{Selected.Id} · {Selected.RuleId} · {Selected.Title}"], ApprovalRoles(), Plant.Today, withReview: true);
        if (!Dialogs.Show(dialog)) return;
        await Session.ApproveEntriesAsync(Kind, [Selected.Id], dialog.Result!);
    }

    protected static IEnumerable<EntryFilter> StateFilters(bool withValueFlags)
    {
        yield return new EntryFilter("all", Loc.T("Filter_All"), _ => true);
        yield return new EntryFilter("valid", Texts.EntryState(RegisterEntryState.Valid), r => r.State == RegisterEntryState.Valid);
        yield return new EntryFilter("pending", Texts.EntryState(RegisterEntryState.Pending), r => r.State == RegisterEntryState.Pending);
        yield return new EntryFilter("changed", Texts.EntryState(RegisterEntryState.ChangedAfterApproval), r => r.State == RegisterEntryState.ChangedAfterApproval);
        yield return new EntryFilter("expiring", Texts.EntryState(RegisterEntryState.Expiring), r => r.State == RegisterEntryState.Expiring);
        yield return new EntryFilter("expired", Texts.EntryState(RegisterEntryState.Expired), r => r.State == RegisterEntryState.Expired);
        if (withValueFlags)
        {
            yield return new EntryFilter("mismatch", Loc.C("RegisterFlag_ValueMismatch"), r => (r.Flags & RegisterFlags.ValueMismatch) != 0);
            yield return new EntryFilter("unused", Loc.C("RegisterFlag_Unused"), r => (r.Flags & RegisterFlags.Unused) != 0);
        }
        yield return new EntryFilter("invalid", Texts.EntryState(RegisterEntryState.Invalid), r => r.State == RegisterEntryState.Invalid);
    }
}

/// <summary>
/// Ausnahmen (spec 7.8 Seite 4): Register mit Filtern (beantragt, nach Freigabe geändert, läuft bald ab, abgelaufen,
/// Wert weicht ab, nicht mehr nötig); anlegen, bearbeiten, verlängern, zurückziehen; Vorlage importieren;
/// „Genehmigungsblatt erstellen“ (O12); „Genehmigung eintragen“; Prüfung direkt am Eintrag.
/// </summary>
internal sealed class ExceptionsViewModel : RegisterPageViewModel
{
    public ExceptionsViewModel(AppSession session, IDialogs dialogs) : base(session, dialogs, "exceptions", "", RegisterKind.Exceptions)
    {
        EditCommand = new AsyncCommand(EditAsync, () => Session.CanWrite && Selected is not null);
    }

    public AsyncCommand EditCommand { get; }

    private ExceptionEntry? Entry => Selected?.Entry as ExceptionEntry;

    protected override IEnumerable<EntryRow> BuildRows(PlantContext plant)
    {
        var language = AppSession.Language;
        foreach (var e in plant.Registers.Exceptions.Exceptions)
        {
            var info = plant.Info(e);
            yield return new EntryRow
            {
                Id = e.Id,
                RuleId = e.RuleId,
                Title = plant.Title(e.RuleId, language),
                Severity = plant.Rule(e.RuleId)?.Severity,
                ScopeText = Texts.Scope(e.Scope, plant.Plant),
                CategoryText = Texts.Reason(e.Category),
                State = info.State,
                Flags = info.Flags,
                ReviewBy = Texts.Date(e.Approval.ReviewBy),
                Record = e.Approval.ApprovalRecord ?? "",
                Hosts = info.Hosts,
                Issues = info.Issues,
                Entry = e,
            };
        }
    }

    protected override IEnumerable<EntryFilter> BuildFilters() => StateFilters(withValueFlags: true);

    public string DetailJustification => Entry?.Justification ?? "";
    public string DetailCompensating => Entry?.CompensatingMeasure ?? "";
    public bool HasCompensating => DetailCompensating.Length > 0;
    public string DetailReference => Entry?.Reference ?? "";
    public bool HasReference => DetailReference.Length > 0;
    public string DetailAccepted => Entry is null ? "" : Entry.AcceptedValue ?? Loc.C("Value_NotSet");
    public string DetailApproval => Entry?.Approval is not { } a || !RegisterRules.Complete(a) ? Loc.T("Register_NotApproved")
        : Loc.F("Register_ApprovedBy", a.ApprovedByRole, Texts.Date(a.ApprovedOn), a.ApprovalRecord, Texts.Date(a.ReviewBy))
          + (a.ApprovedByName is { } n ? $" ({n})" : "");
    public string DetailCreated => Entry is null ? "" : Loc.F("Register_Created", Texts.Date(Entry.CreatedOn), Entry.CreatedByRole ?? "–")
                                                        + (Entry.ModifiedOn is { } m ? " · " + Loc.F("Register_Modified", Texts.Date(m)) : "");
    public string DetailHash => Entry is null ? "" : Hashes.Short(ContentHashes.Of(Entry));
    public string DetailComment => Entry?.Comment ?? "";
    public bool HasComment => DetailComment.Length > 0;

    protected override void RaiseDetail()
    {
        base.RaiseDetail();
        foreach (var n in new[] { nameof(DetailJustification), nameof(DetailCompensating), nameof(HasCompensating), nameof(DetailReference), nameof(HasReference),
                     nameof(DetailAccepted), nameof(DetailApproval), nameof(DetailCreated), nameof(DetailHash), nameof(DetailComment), nameof(HasComment) })
            Raise(n);
    }

    private async Task EditAsync()
    {
        if (Entry is not { } entry || Plant is null) return;
        var dialog = ExceptionDialogViewModel.ForEntry(Plant, entry, Plant.Rule(entry.RuleId), Selected!.Title);
        if (!Dialogs.Show(dialog)) return;
        await Session.SaveExceptionAsync(dialog.Result!);
    }

    public override Task PrepareScreenshotAsync()
    {
        FilterBy("all");
        SelectEntry(Rows.FirstOrDefault(r => r.State == RegisterEntryState.Expiring)?.Id ?? Rows.FirstOrDefault()?.Id);
        return Task.CompletedTask;
    }
}

/// <summary>Zuschnitt (spec 7.8 Seite 5): Register, Rollenvorlagen, Freigabe wie bei Ausnahmen.</summary>
internal sealed class TailoringViewModel : RegisterPageViewModel
{
    public TailoringViewModel(AppSession session, IDialogs dialogs) : base(session, dialogs, "tailoring", "", RegisterKind.Tailoring)
    {
        EditCommand = new AsyncCommand(EditAsync, () => Session.CanWrite && Selected is not null);
        RoleTemplateCommand = new AsyncCommand(p => p is RoleTemplate t ? ImportRoleTemplateAsync(t) : Task.CompletedTask, _ => Session.CanWrite);
        foreach (var t in RoleTemplate.Embedded()) RoleTemplates.Add(t);
    }

    public AsyncCommand EditCommand { get; }
    public AsyncCommand RoleTemplateCommand { get; }

    /// <summary>Rollenvorlagen aus templates/tailoring (eingebettet in Riegel.Core; Paket rules-a).</summary>
    public ObservableCollection<RoleTemplate> RoleTemplates { get; } = [];

    public bool HasRoleTemplates => RoleTemplates.Count > 0;

    private TailoringEntry? Entry => Selected?.Entry as TailoringEntry;

    protected override IEnumerable<EntryRow> BuildRows(PlantContext plant)
    {
        var language = AppSession.Language;
        foreach (var t in plant.Registers.Tailoring.Entries)
        {
            var info = plant.Info(t);
            yield return new EntryRow
            {
                Id = t.Id,
                RuleId = t.RuleId,
                Title = plant.Title(t.RuleId, language),
                Severity = plant.Rule(t.RuleId)?.Severity,
                ScopeText = Texts.Scope(t.Scope, plant.Plant),
                State = info.State,
                Flags = info.Flags,
                ReviewBy = Texts.Date(t.Approval.ReviewBy),
                Record = t.Approval.ApprovalRecord ?? "",
                Hosts = info.Hosts,
                Issues = info.Issues,
                Entry = t,
            };
        }
    }

    protected override IEnumerable<EntryFilter> BuildFilters() => StateFilters(withValueFlags: false);

    public string DetailReason => Entry?.Reason ?? "";
    public string DetailApproval => Entry?.Approval is not { } a || !RegisterRules.Complete(a) ? Loc.T("Register_NotApproved")
        : Loc.F("Register_ApprovedBy", a.ApprovedByRole, Texts.Date(a.ApprovedOn), a.ApprovalRecord, Texts.Date(a.ReviewBy));
    public string DetailCreated => Entry is null ? "" : Loc.F("Register_Created", Texts.Date(Entry.CreatedOn), Entry.CreatedByRole ?? "–");
    public string DetailComment => Entry?.Comment ?? "";
    public bool HasComment => DetailComment.Length > 0;
    public string DetailHash => Entry is null ? "" : Hashes.Short(ContentHashes.Of(Entry));

    protected override void RaiseDetail()
    {
        base.RaiseDetail();
        foreach (var n in new[] { nameof(DetailReason), nameof(DetailApproval), nameof(DetailCreated), nameof(DetailComment), nameof(HasComment), nameof(DetailHash) })
            Raise(n);
    }

    private async Task EditAsync()
    {
        if (Entry is not { } entry || Plant is null) return;
        var host = entry.Scope.Machines.FirstOrDefault() ?? Plant.Hosts.FirstOrDefault() ?? "";
        var dialog = new TailoringDialogViewModel(Plant, entry, host, entry.RuleId, Plant.Rule(entry.RuleId), Selected!.Title);
        if (!Dialogs.Show(dialog)) return;
        await Session.SaveTailoringAsync(dialog.Result!);
    }

    private async Task ImportRoleTemplateAsync(RoleTemplate template)
    {
        var path = template.Extract();
        try { await Session.ImportTemplateAsync(path); }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    public override Task PrepareScreenshotAsync()
    {
        FilterBy("all");
        SelectEntry(Rows.FirstOrDefault()?.Id);
        return Task.CompletedTask;
    }
}

/// <summary>Eine eingebettete Rollenvorlage für den Zuschnitt (templates/tailoring/*.json in Riegel.Core).</summary>
internal sealed record RoleTemplate(string ResourceName, string Name)
{
    public static IEnumerable<RoleTemplate> Embedded()
    {
        var assembly = typeof(Core.Model.Snapshot).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("templates/tailoring/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
            yield return new RoleTemplate(name, Path.GetFileNameWithoutExtension(name).Replace(".tailoring-template", "", StringComparison.Ordinal));
    }

    /// <summary>In eine temporäre Datei schreiben (der Register-Dienst liest Vorlagen aus Dateien).</summary>
    public string Extract()
    {
        var path = Path.Combine(Path.GetTempPath(), "riegel-" + Guid.NewGuid().ToString("N")[..8] + "-" + Path.GetFileName(ResourceName));
        using var source = typeof(Core.Model.Snapshot).Assembly.GetManifestResourceStream(ResourceName)!;
        using var target = File.Create(path);
        source.CopyTo(target);
        return path;
    }
}

/// <summary>Eine Zeile im Maßnahmenplan.</summary>
internal sealed class MeasureRow
{
    public required MeasureEntry Entry { get; init; }
    public string Id => Entry.Id;
    public string RuleId => Entry.RuleId;
    public string Title { get; init; } = "";
    public Severity? Severity { get; init; }
    public string Action => Entry.Action;
    public string Owner => Entry.OwnerRole;
    public string Due => Texts.Date(Entry.DueDate);
    public DateOnly DueDate => Entry.DueDate;
    public string Ticket => Entry.Ticket ?? "";
    public string StateText { get; init; } = "";
    public string StateTone { get; init; } = "";
    public bool Overdue { get; init; }
    public string ScopeText { get; init; } = "";
    public int OpenCount { get; init; }
    public IReadOnlyList<string> Hosts { get; init; } = [];
    public RegisterFlags Flags { get; init; }
    public string FlagsText => string.Join(" · ", Texts.RegisterFlagTexts(Flags));
}

/// <summary>Maßnahmen (spec 7.8 Seite 6): Plan mit verantwortlicher Rolle, Termin und Ticket; Filter „überfällig“.</summary>
internal sealed class MeasuresViewModel : PageViewModel
{
    private readonly List<MeasureRow> _all = [];
    private MeasureRow? _selected;
    private bool _overdueOnly;
    private bool _openOnly = true;

    public MeasuresViewModel(AppSession session, IDialogs dialogs) : base(session, dialogs, "measures", "")
    {
        EditCommand = new AsyncCommand(EditAsync, () => Session.CanWrite && Selected is not null);
        DoneCommand = new AsyncCommand(() => SetStateAsync(MeasureState.Done), () => Session.CanWrite && Selected?.Entry.State == MeasureState.Planned);
        DropCommand = new AsyncCommand(() => SetStateAsync(MeasureState.Dropped), () => Session.CanWrite && Selected?.Entry.State == MeasureState.Planned);
    }

    public ObservableCollection<MeasureRow> Rows { get; } = [];
    public MeasureRow? Selected { get => _selected; set { if (Set(ref _selected, value)) Raise(nameof(HasSelection)); Raise(nameof(SelectedHosts)); } }
    public bool HasSelection => Selected is not null;
    public bool HasRows => Rows.Count > 0;
    public bool CanWrite => Session.CanWrite;
    public string SelectedHosts => Selected is null ? "" : Selected.Hosts.Count == 0 ? Loc.T("Register_NoHosts")
        : string.Join(", ", Selected.Hosts) is var hosts && hosts == Selected.ScopeText ? "" : hosts;

    public bool OverdueOnly { get => _overdueOnly; set { if (Set(ref _overdueOnly, value)) Apply(); } }
    public bool OpenOnly { get => _openOnly; set { if (Set(ref _openOnly, value)) Apply(); } }

    public AsyncCommand EditCommand { get; }
    public AsyncCommand DoneCommand { get; }
    public AsyncCommand DropCommand { get; }

    public string Summary => Loc.F("Measures_Summary", _all.Count(r => r.Entry.State == MeasureState.Planned), _all.Count(r => r.Overdue));

    public override void OnPlantChanged()
    {
        base.OnPlantChanged();
        var keep = Selected?.Id;
        _all.Clear();
        if (Plant is { } plant)
        {
            var language = AppSession.Language;
            foreach (var m in plant.Registers.Measures.Measures)
            {
                var info = plant.Info(m);
                _all.Add(new MeasureRow
                {
                    Entry = m,
                    Title = plant.Title(m.RuleId, language),
                    Severity = plant.Rule(m.RuleId)?.Severity,
                    StateText = info.Overdue ? Loc.T("Measures_Overdue") : Texts.MeasureState(m.State),
                    StateTone = info.Overdue ? "Critical" : m.State == MeasureState.Planned ? "Info" : m.State == MeasureState.Done ? "Good" : "",
                    Overdue = info.Overdue,
                    ScopeText = Texts.Scope(m.Scope, plant.Plant),
                    OpenCount = info.OpenCount,
                    Hosts = info.Hosts,
                    Flags = info.Flags,
                });
            }
        }
        Apply(keep);
        int overdue = _all.Count(r => r.Overdue);
        Badge = overdue == 0 ? null : Texts.Number(overdue);
        BadgeTone = overdue == 0 ? "" : "High";
        Raise(nameof(CanWrite));
        Raise(nameof(Summary));
    }

    private void Apply(string? keep = null)
    {
        keep ??= Selected?.Id;
        Rows.Clear();
        foreach (var r in _all.Where(r => !OverdueOnly || r.Overdue).Where(r => !OpenOnly || r.Entry.State == MeasureState.Planned)
                     .OrderByDescending(r => r.Overdue).ThenBy(r => r.Entry.DueDate).ThenBy(r => r.Id, StringComparer.Ordinal))
            Rows.Add(r);
        Selected = Rows.FirstOrDefault(r => r.Id == keep) ?? Rows.FirstOrDefault();
        Raise(nameof(HasRows));
    }

    private async Task EditAsync()
    {
        if (Selected is null || Plant is null) return;
        var e = Selected.Entry;
        var host = e.Scope.Machines.FirstOrDefault() ?? Plant.Hosts.FirstOrDefault() ?? "";
        var dialog = new MeasureDialogViewModel(Plant, e, host, e.RuleId, Plant.Rule(e.RuleId), Selected.Title);
        if (!Dialogs.Show(dialog)) return;
        await Session.SaveMeasureAsync(dialog.Result!);
    }

    private Task SetStateAsync(MeasureState state)
    {
        if (Selected is null || Plant is null) return Task.CompletedTask;
        return Session.SaveMeasureAsync(Selected.Entry with { State = state, ModifiedOn = Plant.Today });
    }

    public override Task PrepareScreenshotAsync()
    {
        OverdueOnly = false;
        OpenOnly = true;
        Selected = Rows.FirstOrDefault(r => r.Overdue) ?? Rows.FirstOrDefault();
        return Task.CompletedTask;
    }
}
