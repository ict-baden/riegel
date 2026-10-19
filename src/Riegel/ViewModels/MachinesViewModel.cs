// SPDX-License-Identifier: MIT
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Session;
using Riegel.UI;

namespace Riegel.ViewModels;

/// <summary>Eine Zeile der Rechnerliste.</summary>
internal sealed class MachineRow
{
    public required MachineState State { get; init; }
    public string Host => State.HostName;
    public string Label { get; init; } = "";
    public string Group { get; init; } = "";
    public string Role { get; init; } = "";
    public string Os { get; init; } = "";
    public string Collected { get; init; } = "–";
    public string CollectedHint { get; init; } = "";
    public bool Stale { get; init; }
    public string StaleText { get; init; } = "";
    public int OpenHigh { get; init; }
    public int OpenMedium { get; init; }
    public int OpenLow { get; init; }
    public string OpenText => State.Latest is null ? "–" : $"{OpenHigh} · {OpenMedium} · {OpenLow}";
    public int? Score { get; init; }
    public string ScoreText { get; init; } = "–";
    public string ShareText { get; init; } = "–";
    /// <summary>Trend gegenüber dem vorigen Lauf: +1 besser, -1 schlechter, 0 gleich, null ohne Vergleich.</summary>
    public int? Trend { get; init; }
    public string TrendGlyph => Trend switch { > 0 => "", < 0 => "", 0 => "", _ => "" };
    public string TrendText { get; init; } = "";
    public int Worse { get; init; }
    public string WorseText => Worse == 0 ? "" : Texts.Number(Worse);
    public bool Esu => State.Entry?.EsuCovered == true;
    public bool HasIdentity => State.Entry?.Identity is not null;
    public bool NotInPlant => State.Entry is null;
}

/// <summary>
/// Maschinen (spec 7.8 Seite 2): Rechner mit Gruppe/Rolle, Identität, letztem Lauf, Zählungen, Werten, Prüfumfang,
/// Trend, Verschlechterungen und Markierung veralteter Erfassungen; Gruppen und Rollen zuordnen, ESU, Identität.
/// </summary>
internal sealed class MachinesViewModel : PageViewModel
{
    private MachineRow? _selected;

    public MachinesViewModel(AppSession session, IDialogs dialogs) : base(session, dialogs, "machines", "")
    {
        EditCommand = new AsyncCommand(EditAsync, () => Selected is not null && Session.CanWrite);
        GroupsCommand = new AsyncCommand(GroupsAsync, () => Session.CanWrite);
        ReEvaluateCommand = new AsyncCommand(() => Session.ReEvaluateAsync(), () => Session.CanWrite && Rows.Any(r => r.State.Latest is not null));
        ResultsCommand = new RelayCommand(() => Session.ShowPage("results", Selected?.Host), () => Selected?.State.Latest is not null);
        CompareCommand = new RelayCommand(() => Session.ShowPage("comparison", Selected?.Host), () => Selected?.State.Latest is not null);
    }

    public ObservableCollection<MachineRow> Rows { get; } = [];

    public MachineRow? Selected
    {
        get => _selected;
        set { if (Set(ref _selected, value)) RaiseDetail(); }
    }

    public AsyncCommand EditCommand { get; }
    public AsyncCommand GroupsCommand { get; }
    public AsyncCommand ReEvaluateCommand { get; }
    public RelayCommand ResultsCommand { get; }
    public RelayCommand CompareCommand { get; }

    public bool CanWrite => Session.CanWrite;

    public override void OnPlantChanged()
    {
        base.OnPlantChanged();
        var host = Selected?.Host;
        Rows.Clear();
        if (Plant is { } plant)
            foreach (var m in plant.Machines) Rows.Add(RowFor(plant, m));
        Selected = Rows.FirstOrDefault(r => string.Equals(r.Host, host, StringComparison.OrdinalIgnoreCase)) ?? Rows.FirstOrDefault();
        Badge = Rows.Count == 0 ? null : Texts.Number(Rows.Count);
        Raise(nameof(CanWrite));
        Raise(nameof(Summary));
    }

    public string Summary => Plant is null ? "" : Loc.F("Machines_Summary", Loc.P("Machines_SummaryMachines", Rows.Count, Rows.Count), Loc.F("Machines_SummaryStale", Rows.Count(r => r.Stale)),
        Loc.P("Machines_SummaryWorse", Rows.Sum(r => r.Worse), Rows.Sum(r => r.Worse)));

    internal static MachineRow RowFor(PlantContext plant, MachineState m)
    {
        var run = m.Latest;
        int? trend = null;
        string trendText = Loc.T("Machines_TrendNone");
        if (run is not null && m.Previous is { } prev)
        {
            int now = run.Summary.OpenHigh * 3 + run.Summary.OpenMedium * 2 + run.Summary.OpenLow;
            int before = prev.Summary.OpenHigh * 3 + prev.Summary.OpenMedium * 2 + prev.Summary.OpenLow;
            trend = Math.Sign(before - now);
            trendText = trend switch { > 0 => Loc.T("Machines_TrendBetter"), < 0 => Loc.T("Machines_TrendWorse"), _ => Loc.T("Machines_TrendSame") };
        }
        int age = run is null ? 0 : plant.Today.DayNumber - DateOnly.FromDateTime(run.CollectedAtUtc.UtcDateTime).DayNumber;
        bool stale = run is not null && (run.Flags & RunFlags.StaleSnapshot) != 0;
        return new MachineRow
        {
            State = m,
            Label = m.Entry?.Label ?? "",
            Group = m.Group?.Name ?? Loc.C("Group_None"),
            Role = Texts.Role(m.Group?.Role),
            Os = OsText(run?.Facts),
            Collected = run is null ? Loc.T("Machines_NoRun") : Texts.Date(run.CollectedAtUtc),
            CollectedHint = run is null ? "" : Loc.P("Format_DaysAgo", age, age),
            Stale = stale,
            StaleText = stale ? Loc.T("Machines_Stale") : "",
            OpenHigh = run?.Summary.OpenHigh ?? 0,
            OpenMedium = run?.Summary.OpenMedium ?? 0,
            OpenLow = run?.Summary.OpenLow ?? 0,
            Score = run?.Summary.Score,
            ScoreText = run is null ? "–" : (run.Flags & RunFlags.DomainController) != 0 ? Loc.T("Machines_DcNotScored") : Texts.Percent(run.Summary.Score),
            ShareText = run is null ? "" : Loc.F("Machines_Checked", Texts.Percent(run.Summary.CheckedShare)),
            Trend = trend,
            TrendText = trendText,
            Worse = run?.Results.Count(r => (r.Flags & ResultFlags.Regression) != 0) ?? 0,
        };
    }

    /// <summary>Betriebssystem aus den Fakten (os.edition, os.build, os.ubr).</summary>
    internal static string OsText(IReadOnlyDictionary<string, JsonElement>? facts)
    {
        if (facts is null) return "–";
        string? Fact(string name) => facts.TryGetValue(name, out var v) && v.ValueKind != JsonValueKind.Null ? CanonicalValue.Render(v) : null;
        var edition = Fact("os.edition");
        var build = Fact("os.build");
        var ubr = Fact("os.ubr");
        var kind = Fact("os.kind") is { } k ? Loc.T("OsKind_" + k) : null;
        var version = build is null ? null : ubr is null ? build : $"{build}.{ubr}";
        var parts = new[] { kind, edition, version }.Where(p => !string.IsNullOrEmpty(p)).ToList();
        return parts.Count == 0 ? "–" : string.Join(" · ", parts);
    }

    // ---------------------------------------------------------------- Detail

    public bool HasSelection => Selected is not null;
    public string DetailTitle => Selected is null ? "" : string.IsNullOrEmpty(Selected.Label) ? Selected.Host : $"{Selected.Host} – {Selected.Label}";
    public string DetailGroup => Selected is null ? "" : string.IsNullOrEmpty(Selected.Role) ? Selected.Group : $"{Selected.Group} · {Selected.Role}";
    public string DetailNotes => Selected?.State.Entry?.Notes ?? "";

    /// <summary>Maschinenidentität (spec 2.17) als Zeilen „Beschriftung: Wert“.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Identity
    {
        get
        {
            if (Selected?.State.Entry?.Identity is not { } id) return [];
            var list = new List<KeyValuePair<string, string>>();
            void Add(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) list.Add(new(Loc.T(key), value)); }
            Add("Identity_MachineType", id.MachineType);
            Add("Identity_SerialNumber", id.SerialNumber);
            Add("Identity_OrderNumber", id.OrderNumber);
            Add("Identity_DeliveryDate", id.DeliveryDate is { } d ? Texts.Date(d) : null);
            Add("Identity_PlantSoftware", id.PlantSoftware);
            Add("Identity_Builder", id.Builder);
            Add("Identity_Location", id.Location);
            return list;
        }
    }

    public bool HasIdentity => Identity.Count > 0;

    /// <summary>Angaben zur letzten Erhebung (Erfassung, Import, Rechte, Herkunft).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Collection
    {
        get
        {
            if (Selected?.State.Latest is not { } run || Plant is null) return [];
            var list = new List<KeyValuePair<string, string>>
            {
                new(Loc.T("Machines_ColOs"), Selected.Os),
                new(Loc.T("Machines_CollectedAt"), Texts.DateTime(run.CollectedAtUtc)),
                new(Loc.T("Machines_ImportedAt"), Texts.DateTime(run.ImportedAtUtc)),
                new(Loc.T("Machines_Admin"), run.IsAdmin ? Loc.T("Common_Yes") : Loc.T("Common_No")),
            };
            var file = Plant.Imports.SelectMany(i => i.Files).FirstOrDefault(f => f.RunId == run.Id || f.Sha256 == run.Snapshot.Sha256);
            if (file is not null)
            {
                list.Add(new(Loc.T("Machines_Origin"), Texts.Origin(file.Origin)));
                list.Add(new(Loc.T("Machines_Collector"), $"{file.CollectorName} {file.CollectorVersion}"));
            }
            if (run.Options.Minimal) list.Add(new(Loc.T("Machines_Options"), "--minimal"));
            list.Add(new(Loc.T("Machines_Runs"), Texts.Number(Selected.State.Runs.Count)));
            foreach (var flag in Texts.RunFlags(run.Flags)) list.Add(new(Loc.T("Machines_Note"), flag));
            return list;
        }
    }

    public string ApprovedText => Selected?.State.Approved is { } a
        ? Loc.F("Machines_Approved", Texts.Date(a.Approval.ApprovedOn), a.Approval.ApprovedByRole, a.Approval.ApprovalRecord)
        : Loc.T("Machines_NotApproved");

    public string EsuText => Selected?.Esu == true ? Loc.T("Machines_EsuYes") : Loc.T("Machines_EsuNo");

    private void RaiseDetail()
    {
        Raise(nameof(HasSelection));
        Raise(nameof(DetailTitle));
        Raise(nameof(DetailGroup));
        Raise(nameof(DetailNotes));
        Raise(nameof(Identity));
        Raise(nameof(HasIdentity));
        Raise(nameof(Collection));
        Raise(nameof(ApprovedText));
        Raise(nameof(EsuText));
    }

    // ---------------------------------------------------------------- Bearbeiten

    private async Task EditAsync()
    {
        if (Selected is null || Plant is null) return;
        var dialog = new MachineDialogViewModel(Plant.Plant, Selected.State.Entry ?? new PlantMachine(Selected.Host, null, null, false, null, null));
        if (!Dialogs.Show(dialog)) return;
        var machine = dialog.Result!;
        await Session.SavePlantAsync(p => p with { Machines = Upsert(p.Machines, machine) }, Loc.F("Notice_MachineSaved", machine.HostName));
    }

    /// <summary>Rechner an seiner Stelle ersetzen oder (aus einem Lauf bekannt, aber nicht in der Anlage) anhängen.</summary>
    internal static IReadOnlyList<PlantMachine> Upsert(IReadOnlyList<PlantMachine> list, PlantMachine machine)
    {
        var result = list.ToList();
        int i = result.FindIndex(m => string.Equals(m.HostName, machine.HostName, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) result[i] = machine;
        else result.Add(machine);
        return result;
    }

    private async Task GroupsAsync()
    {
        if (Plant is null) return;
        var dialog = new GroupsDialogViewModel(Plant.Plant.Groups);
        if (!Dialogs.Show(dialog)) return;
        var groups = dialog.Result;
        var ids = groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        await Session.SavePlantAsync(p => p with
        {
            Groups = groups,
            Machines = [.. p.Machines.Select(m => m.GroupId is not null && !ids.Contains(m.GroupId) ? m with { GroupId = null } : m)],
        }, Loc.T("Notice_GroupsSaved"));
    }

    public override Task PrepareScreenshotAsync()
    {
        Selected = Rows.FirstOrDefault(r => r.HasIdentity) ?? Rows.FirstOrDefault();
        return Task.CompletedTask;
    }
}

/// <summary>Auswahl in Listen (Wert und Anzeigetext).</summary>
internal sealed record Choice<T>(T Value, string Text)
{
    public override string ToString() => Text;
}

/// <summary>Dialog „Rechner bearbeiten“: Bezeichnung, Gruppe, ESU, Notiz und Maschinenidentität (spec 2.17).</summary>
internal sealed class MachineDialogViewModel : ObservableObject, IDialogViewModel
{
    public MachineDialogViewModel(Plant plant, PlantMachine machine)
    {
        HostName = machine.HostName;
        Label = machine.Label ?? "";
        Groups = [new Choice<string?>(null, Loc.C("Group_None")), .. plant.Groups.Select(g => new Choice<string?>(g.Id, $"{g.Name} ({Texts.Role(g.Role)})"))];
        Group = Groups.FirstOrDefault(g => g.Value == machine.GroupId) ?? Groups[0];
        EsuCovered = machine.EsuCovered;
        Notes = machine.Notes ?? "";
        var id = machine.Identity;
        MachineType = id?.MachineType ?? "";
        SerialNumber = id?.SerialNumber ?? "";
        OrderNumber = id?.OrderNumber ?? "";
        DeliveryDate = id?.DeliveryDate?.ToDateTime(TimeOnly.MinValue);
        PlantSoftware = id?.PlantSoftware ?? "";
        Builder = id?.Builder ?? "";
        Location = id?.Location ?? "";
    }

    public string Title => Loc.F("Machine_Title", HostName);
    public string HostName { get; }
    public string Label { get; set; }
    public IReadOnlyList<Choice<string?>> Groups { get; }
    public Choice<string?> Group { get; set; }
    public bool EsuCovered { get; set; }
    public string Notes { get; set; }
    public string MachineType { get; set; }
    public string SerialNumber { get; set; }
    public string OrderNumber { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public string PlantSoftware { get; set; }
    public string Builder { get; set; }
    public string Location { get; set; }

    public PlantMachine? Result { get; private set; }

    public bool CanAccept => true;

    public bool Accept()
    {
        static string? N(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var identity = new MachineIdentity(N(MachineType), N(SerialNumber), N(OrderNumber),
            DeliveryDate is { } d ? DateOnly.FromDateTime(d) : null, N(PlantSoftware), N(Builder), N(Location));
        bool empty = identity == new MachineIdentity(null, null, null, null, null, null, null);
        Result = new PlantMachine(HostName, Group.Value, N(Label), EsuCovered, N(Notes), empty ? null : identity);
        return true;
    }
}

/// <summary>Eine bearbeitbare Gruppe im Dialog „Gruppen und Rollen“.</summary>
internal sealed class GroupRow : ObservableObject
{
    private string _name;
    private Choice<MachineRole> _role;

    public GroupRow(string id, string name, MachineRole role, IReadOnlyList<Choice<MachineRole>> roles)
    {
        Id = id;
        _name = name;
        Roles = roles;
        _role = roles.First(r => r.Value == role);
    }

    public string Id { get; }
    public string Name { get => _name; set => Set(ref _name, value); }
    public IReadOnlyList<Choice<MachineRole>> Roles { get; }
    public Choice<MachineRole> Role { get => _role; set => Set(ref _role, value); }
}

/// <summary>Dialog „Gruppen und Rollen“ (Zuschnitt und Soll-Konfiguration gelten je Rolle).</summary>
internal sealed class GroupsDialogViewModel : ObservableObject, IDialogViewModel
{
    private GroupRow? _selected;

    public GroupsDialogViewModel(IReadOnlyList<MachineGroup> groups)
    {
        RoleChoices = [.. Enum.GetValues<MachineRole>().Select(r => new Choice<MachineRole>(r, Texts.Role(r)))];
        foreach (var g in groups) Rows.Add(new GroupRow(g.Id, g.Name, g.Role, RoleChoices));
        AddCommand = new RelayCommand(Add);
        RemoveCommand = new RelayCommand(() => { if (Selected is { } s) Rows.Remove(s); }, () => Selected is not null);
    }

    public string Title => Loc.T("Groups_Title");
    public IReadOnlyList<Choice<MachineRole>> RoleChoices { get; }
    public ObservableCollection<GroupRow> Rows { get; } = [];
    public GroupRow? Selected { get => _selected; set => Set(ref _selected, value); }
    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public IReadOnlyList<MachineGroup> Result { get; private set; } = [];

    public bool CanAccept => Rows.All(r => !string.IsNullOrWhiteSpace(r.Name));

    private void Add()
    {
        int n = Rows.Count + 1;
        string id;
        do id = "g-" + n++.ToString(CultureInfo.InvariantCulture);
        while (Rows.Any(r => r.Id == id));
        var row = new GroupRow(id, Loc.T("Groups_NewName"), MachineRole.Other, RoleChoices);
        Rows.Add(row);
        Selected = row;
    }

    public bool Accept()
    {
        if (!CanAccept) return false;
        Result = [.. Rows.Select(r => new MachineGroup(r.Id, r.Name.Trim(), r.Role.Value))];
        return true;
    }
}
