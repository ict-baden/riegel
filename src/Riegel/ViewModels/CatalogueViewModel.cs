// SPDX-License-Identifier: MIT
using System.Collections.ObjectModel;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Session;
using Riegel.UI;

namespace Riegel.ViewModels;

/// <summary>Eine Regel in der Liste des Regelwerks.</summary>
internal sealed class RuleRow
{
    public required Rule Rule { get; init; }
    public string Id => Rule.Id;
    public string Title { get; init; } = "";
    public string CategoryText { get; init; } = "";
    public Severity Severity => Rule.Severity;
    public string KindText => Rule.Kind == RuleKind.Inventory ? Loc.T("Catalogue_KindInventory") : Loc.T("Catalogue_KindCheck");
    public bool Retired => Rule.State == RuleState.Retired;
}

/// <summary>Eine importierte Kunden-Baseline.</summary>
internal sealed class BaselineRow
{
    public required BaselineFile File { get; init; }
    public string Id => File.Id;
    public string Name => File.Name;
    public string Source => File.SourceFileName ?? "";
    public string Imported => Texts.DateTime(File.ImportedAtUtc);
    public string Rules => Texts.Number(File.Rules.Count);
    public string Sha => File.SourceSha256 is { } s ? Hashes.Short(s) : "–";
}

/// <summary>
/// Regelwerk (spec 7.8 Seite 9): Regeln mit Verweisen, Quellen und Lizenzen mit Namensnennung (spec 2.2), importierte
/// Kunden-Baselines (importieren oder entfernen, spec 2.15).
/// </summary>
internal sealed class CatalogueViewModel : PageViewModel
{
    private readonly List<RuleRow> _all = [];
    private RuleRow? _selected;
    private string _search = "";
    private Choice<string?> _category;
    private BaselineRow? _baseline;
    private ICatalogue? _catalogue;

    public CatalogueViewModel(AppSession session, IDialogs dialogs) : base(session, dialogs, "catalogue", "")
    {
        Categories = [new Choice<string?>(null, Loc.T("Filter_AllCategories")), .. RuleCategories.All.Select(c => new Choice<string?>(c, Texts.Category(c)))];
        _category = Categories[0];
        ImportBaselineCommand = new AsyncCommand(ImportBaselineAsync, () => Session.CanWrite);
        ImportBaselineFolderCommand = new AsyncCommand(ImportBaselineFolderAsync, () => Session.CanWrite);
        RemoveBaselineCommand = new AsyncCommand(RemoveBaselineAsync, () => Session.CanWrite && SelectedBaseline is not null);
        OpenSourceCommand = new RelayCommand(p => { if (p is string url && Shell.OpenLink(Session.Platform, url) is { } problem) Session.Report(NoticeKind.Warning, problem); });
        foreach (var s in RuleSources.All) Sources.Add(s);
    }

    public override bool NeedsPlant => false;

    public override bool HasSearch => true;

    public IReadOnlyList<Choice<string?>> Categories { get; }
    public ObservableCollection<RuleRow> Rows { get; } = [];
    public ObservableCollection<SourceAttribution> Sources { get; } = [];
    public ObservableCollection<BaselineRow> Baselines { get; } = [];

    public RuleRow? Selected { get => _selected; set { if (Set(ref _selected, value)) RaiseDetail(); } }
    public BaselineRow? SelectedBaseline { get => _baseline; set => Set(ref _baseline, value); }
    public string Search { get => _search; set { if (Set(ref _search, value)) Apply(); } }
    public Choice<string?> Category { get => _category; set { if (Set(ref _category, value)) Apply(); } }

    public AsyncCommand ImportBaselineCommand { get; }
    public AsyncCommand ImportBaselineFolderCommand { get; }
    public AsyncCommand RemoveBaselineCommand { get; }
    public RelayCommand OpenSourceCommand { get; }

    public bool HasRules => _all.Count > 0;
    public bool HasBaselines => Baselines.Count > 0;
    public bool CanWrite => Session.CanWrite;

    public string Header => _catalogue is null ? "" : Loc.F("Catalogue_Header", _catalogue.Id, _catalogue.Version, Texts.Date(_catalogue.ReleasedOn), Hashes.Short(_catalogue.Sha256));

    public string CountsText => _catalogue is null ? "" : Loc.F("Catalogue_Counts", _all.Count, _all.Count(r => r.Rule.Kind == RuleKind.Check),
        _all.Count(r => r.Rule.Kind == RuleKind.Inventory), _all.Count(r => r.Retired));

    /// <summary>Hinweis, wenn der Katalog noch leer ist (Gerüst, Regeln kommen mit den Regelpaketen).</summary>
    public string EmptyText => Loc.T("Catalogue_Empty");

    public override void OnShown() => Load();

    public override void OnPlantChanged()
    {
        base.OnPlantChanged();
        Load();
        Baselines.Clear();
        foreach (var b in Plant?.Baselines ?? []) Baselines.Add(new BaselineRow { File = b });
        SelectedBaseline = Baselines.FirstOrDefault();
        Raise(nameof(HasBaselines));
        Raise(nameof(CanWrite));
    }

    private void Load()
    {
        var catalogue = Plant?.Catalogue ?? Session.Services.Catalogue.LoadEmbedded();
        if (ReferenceEquals(catalogue, _catalogue)) return;
        _catalogue = catalogue;
        var language = AppSession.Language;
        _all.Clear();
        foreach (var r in catalogue.Rules)
            _all.Add(new RuleRow { Rule = r, Title = r.Title.Get(language), CategoryText = Texts.Category(r.Category) });
        Apply();
        Raise(nameof(Header));
        Raise(nameof(CountsText));
        Raise(nameof(HasRules));
    }

    private void Apply()
    {
        var keep = Selected?.Id;
        var search = Search.Trim();
        Rows.Clear();
        foreach (var r in _all.Where(r => Category.Value is null || r.Rule.Category == Category.Value)
                     .Where(r => search.Length == 0 || r.Id.Contains(search, StringComparison.OrdinalIgnoreCase) || r.Title.Contains(search, StringComparison.OrdinalIgnoreCase)))
            Rows.Add(r);
        Selected = Rows.FirstOrDefault(r => r.Id == keep) ?? Rows.FirstOrDefault();
    }

    public void SelectRule(string id)
    {
        Search = "";
        Category = Categories[0];
        Selected = Rows.FirstOrDefault(r => r.Id == id) ?? Selected;
    }

    // ---------------------------------------------------------------- Detail

    public bool HasSelection => Selected is not null;
    private Rule? R => Selected?.Rule;
    public string DetailTitle => Selected?.Title ?? "";
    public string DetailMeta => R is null ? "" : Loc.F("Catalogue_Meta", R.Id, Texts.Category(R.Category), Texts.Severity(R.Severity), Selected!.KindText, R.Since);
    public string DetailRationale => R?.Rationale.Get(AppSession.Language) ?? "";
    public string DetailOtNote => R?.OtNote?.Get(AppSession.Language) ?? "";
    public bool HasOtNote => DetailOtNote.Length > 0;
    public string DetailRemedy => R?.Remedy?.Get(AppSession.Language) ?? "";
    public bool HasRemedy => DetailRemedy.Length > 0;

    public string DetailApplies
    {
        get
        {
            if (R is null) return "";
            var a = R.AppliesTo;
            var parts = new List<string> { string.Join(", ", a.Os.Select(o => Loc.T("OsKind_" + o))) };
            if (a.Domain != DomainScope.Any) parts.Add(Loc.T("Catalogue_Domain_" + a.Domain));
            if (a.MinBuild is { } min) parts.Add(Loc.F("Catalogue_MinBuild", min));
            if (a.ExcludeDc) parts.Add(Loc.T("Catalogue_ExcludeDc"));
            if (R.NeedsAdmin) parts.Add(Loc.T("Catalogue_NeedsAdmin"));
            return string.Join(" · ", parts.Where(p => p.Length > 0));
        }
    }

    public string DetailCheck => R is null ? "" : Loc.F("Catalogue_CheckType", R.Check.Type);
    public string DetailSuggested => R?.SuggestedReason is { } s ? Texts.Reason(s) : "";
    public bool HasSuggested => DetailSuggested.Length > 0;
    public IReadOnlyList<string> DetailReferences => R is null ? [] : [.. R.References.Select(ResultsViewModel.Reference)];
    public bool HasReferences => DetailReferences.Count > 0;
    public IReadOnlyList<string> DetailVerified => R is null ? [] : [.. R.Verified.Select(v => $"{v.Src}: {v.Doc ?? v.Url} ({Texts.Date(v.Date)})")];
    public bool HasVerified => DetailVerified.Count > 0;

    private void RaiseDetail()
    {
        foreach (var n in new[] { nameof(HasSelection), nameof(DetailTitle), nameof(DetailMeta), nameof(DetailRationale), nameof(DetailOtNote), nameof(HasOtNote),
                     nameof(DetailRemedy), nameof(HasRemedy), nameof(DetailApplies), nameof(DetailCheck), nameof(DetailSuggested), nameof(HasSuggested),
                     nameof(DetailReferences), nameof(HasReferences), nameof(DetailVerified), nameof(HasVerified) })
            Raise(n);
    }

    // ---------------------------------------------------------------- Baselines

    private async Task ImportBaselineAsync()
    {
        var file = Dialogs.PickFiles(Loc.T("Catalogue_ImportBaseline"), Loc.T("Catalogue_BaselineFilter"), multiple: false).FirstOrDefault();
        if (file is null) return;
        await Session.ImportBaselineAsync(file, Path.GetFileNameWithoutExtension(file));
    }

    private async Task ImportBaselineFolderAsync()
    {
        var folder = Dialogs.PickFolder(Loc.T("Catalogue_ImportGpo"));
        if (folder is null) return;
        await Session.ImportBaselineAsync(folder, Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)));
    }

    private async Task RemoveBaselineAsync()
    {
        if (SelectedBaseline is null) return;
        if (!Dialogs.Confirm(Loc.T("Catalogue_RemoveTitle"), Loc.F("Catalogue_RemoveText", SelectedBaseline.Name))) return;
        await Session.RemoveBaselineAsync(SelectedBaseline.Id);
    }

    public override Task PrepareScreenshotAsync()
    {
        Load();
        if (Rows.FirstOrDefault(r => r.Rule.OtNote is not null) is { } row) Selected = row;
        return Task.CompletedTask;
    }
}
