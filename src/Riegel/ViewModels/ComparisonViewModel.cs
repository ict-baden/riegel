// SPDX-License-Identifier: MIT
using System.Collections.ObjectModel;
using Riegel.Core.Model;
using Riegel.Session;
using Riegel.UI;

namespace Riegel.ViewModels;

/// <summary>Eine Statusänderung in der Liste.</summary>
internal sealed class ChangeRow
{
    public required StatusChange Change { get; init; }
    public string RuleId => Change.RuleId.Length == 0 ? "–" : Change.RuleId;
    public string Title { get; init; } = "";
    public Severity? Severity { get; init; }
    public string KindText => Texts.ChangeKind(Change.Kind);
    public string KindTone => Change.Kind switch
    {
        StatusChangeKind.Worse or StatusChangeKind.ExceptionExpired or StatusChangeKind.ExceptionRemoved => "Critical",
        StatusChangeKind.Better or StatusChangeKind.ExceptionAdded or StatusChangeKind.TailoringAdded => "Good",
        StatusChangeKind.ScopeChanged or StatusChangeKind.NewRule => "Caution",
        _ => "",
    };
    public string FromText => Change.From is { } f ? Texts.Status(f) : "–";
    public string ToText => Change.To is { } t ? Texts.Status(t) : "–";
    public bool IsWorse => Change.Kind is StatusChangeKind.Worse or StatusChangeKind.ExceptionRemoved;
}

/// <summary>Eine Abweichung eines Rohwerts.</summary>
internal sealed class DriftRow
{
    public required ValueDrift Drift { get; init; }
    public string Item => Short(Drift.Key);
    public string Tooltip => Drift.Path is { Length: > 0 } p ? Drift.Key + "\n" + p : Drift.Key;
    public string Path => Drift.Path ?? "";
    public string From => Drift.From ?? "–";
    public string To => Drift.To ?? "–";
    public string KindText => Texts.DriftKind(Drift.Kind);

    /// <summary>
    /// Quelle kurz: WMI nur die Klasse („Win32_Service“), Registry „HKLM\…\Schlüssel\Unterschlüssel“, sonst gekürzt.
    /// Der volle Schlüssel steht im Tooltip.
    /// </summary>
    internal static string Short(string key)
    {
        if (key.StartsWith("wmi:", StringComparison.OrdinalIgnoreCase) && key.LastIndexOf(':') is var colon and > 3)
            return key[(colon + 1)..];
        var path = key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase) ? key[4..] : key;
        var parts = path.Split('\\');
        if (parts.Length > 3) return $"{parts[0]}\\…\\{parts[^2]}\\{parts[^1]}";
        return path.Length <= 48 ? path : path[..47] + "…";
    }
}

/// <summary>
/// Vergleich (spec 7.8 Seite 7, spec 2.14): gegenüber dem vorigen Lauf oder dem freigegebenen Stand; Statusänderungen
/// („Verschlechtert“ zuerst) und Abweichungen der Rohwerte; „Diesen Lauf freigeben…“ mit Rolle und Nachweis.
/// </summary>
internal sealed class ComparisonViewModel : PageViewModel
{
    private Choice<string>? _host;
    private bool _againstApproved;
    private RunComparison? _comparison;
    private bool _suppress;
    private int _version;

    public ComparisonViewModel(AppSession session, IDialogs dialogs) : base(session, dialogs, "comparison", "")
    {
        PreviousCommand = new AsyncCommand(() => SetBasisAsync(false));
        ApprovedCommand = new AsyncCommand(() => SetBasisAsync(true), () => Machine?.Approved is not null);
        ApproveRunCommand = new AsyncCommand(ApproveRunAsync, () => Session.CanWrite && Machine?.Latest is not null);
    }

    public ObservableCollection<Choice<string>> Hosts { get; } = [];

    public Choice<string>? Host
    {
        get => _host;
        set { if (Set(ref _host, value) && !_suppress) _ = RefreshAsync(); }
    }

    public bool AgainstApproved { get => _againstApproved; private set { if (Set(ref _againstApproved, value)) Raise(nameof(AgainstPrevious)); } }

    public bool AgainstPrevious => !AgainstApproved;

    public AsyncCommand PreviousCommand { get; }
    public AsyncCommand ApprovedCommand { get; }
    public AsyncCommand ApproveRunCommand { get; }

    public ObservableCollection<ChangeRow> Worse { get; } = [];
    public ObservableCollection<ChangeRow> Other { get; } = [];
    public ObservableCollection<DriftRow> Drift { get; } = [];

    public bool HasWorse => Worse.Count > 0;
    public bool HasOther => Other.Count > 0;
    public bool HasDrift => Drift.Count > 0;
    public bool HasComparison => _comparison is not null;
    public string WorseHeader => Loc.F(AgainstApproved ? "Comparison_WorseSinceApproval" : "Comparison_WorseSincePrevious", Worse.Count);
    public string OtherHeader => Loc.F("Comparison_OtherChanges", Other.Count);
    public string DriftHeader => Loc.F("Comparison_Drift", Drift.Count);

    private MachineState? Machine => Plant?.Machine(Host?.Value);

    public string FromText { get; private set; } = "";
    public string ToText { get; private set; } = "";
    public string EmptyText { get; private set; } = "";
    public bool IdentityChanged => _comparison?.MachineIdentityChanged == true;

    public string ApprovedText => Machine?.Approved is { } a
        ? Loc.F("Comparison_Approved", Texts.Date(a.Approval.ApprovedOn), a.Approval.ApprovedByRole, a.Approval.ApprovalRecord)
        : Loc.T("Comparison_NotApproved");

    public override void OnPlantChanged()
    {
        base.OnPlantChanged();
        var host = Host?.Value;
        _suppress = true;
        Hosts.Clear();
        foreach (var m in Plant?.Machines ?? [])
            if (m.Latest is not null) Hosts.Add(new Choice<string>(m.HostName, m.HostName));
        _host = Hosts.FirstOrDefault(h => string.Equals(h.Value, host, StringComparison.OrdinalIgnoreCase)) ?? Hosts.FirstOrDefault();
        _suppress = false;
        Raise(nameof(Host));
        int worse = Plant?.Machines.Sum(m => m.Latest?.Results.Count(r => (r.Flags & ResultFlags.Regression) != 0) ?? 0) ?? 0;
        Badge = worse == 0 ? null : Texts.Number(worse);
        BadgeTone = worse == 0 ? "" : "High";
        _ = RefreshAsync();
    }

    public void SelectHost(string? host)
    {
        if (host is not null && Hosts.FirstOrDefault(h => string.Equals(h.Value, host, StringComparison.OrdinalIgnoreCase)) is { } choice) Host = choice;
    }

    private Task SetBasisAsync(bool approved)
    {
        AgainstApproved = approved;
        return RefreshAsync();
    }

    /// <summary>Vergleich neu berechnen (Exporte lesen im Hintergrund).</summary>
    public async Task RefreshAsync()
    {
        int version = ++_version;
        Worse.Clear();
        Other.Clear();
        Drift.Clear();
        _comparison = null;
        var plant = Plant;
        var machine = Machine;
        if (AgainstApproved && machine?.Approved is null) AgainstApproved = false;
        FromText = ToText = "";
        EmptyText = plant is null ? "" : machine?.Latest is null ? Loc.T("Comparison_NoRun") : "";
        if (plant is not null && machine?.Latest is { } to)
        {
            bool approved = AgainstApproved;
            var basis = approved ? ComparisonBasis.Approved : ComparisonBasis.Previous;
            (Run? From, RunComparison? Comparison, string? Problem) result;
            try
            {
                result = await Task.Run(() =>
                {
                    Run? from = approved ? (machine.Approved is { } a ? TryLoad(plant, a.RunId) : null) : machine.Previous;
                    // Basis ohne brauchbaren Export: Vergleich ohne Rohwerte; Ziel ohne brauchbaren Export: kein Vergleich
                    // (RGL-0415/0416 stehen dann im Hinweis der Seite)
                    string? fromProblem = null;
                    var fromSnapshot = from is null ? null : plant.TryReadSnapshot(from, out fromProblem);
                    var toSnapshot = plant.TryReadSnapshot(to, out var toProblem);
                    return (from, toSnapshot is null ? null : plant.Services.Comparison.Compare(from, fromSnapshot, to, toSnapshot, basis), toProblem ?? fromProblem);
                });
            }
            catch (Exception e) when (e is RiegelException or IOException or UnauthorizedAccessException)
            {
                // Anlagenordner im Netz kurz weg o. Ä.: Grund auf der Seite zeigen statt still einen halben Stand zu lassen
                result = (null, null, AppSession.Describe(e));
            }
            if (version != _version) return;
            var (fromRun, comparison, problem) = result;
            if (problem is not null) EmptyText = problem;
            _comparison = comparison;
            ToText = Loc.F("Comparison_To", Texts.DateTime(to.CollectedAtUtc), to.Id);
            FromText = fromRun is null ? Loc.T(approved ? "Comparison_NoApprovedRun" : "Comparison_NoPrevious")
                : Loc.F(approved ? "Comparison_FromApproved" : "Comparison_FromPrevious", Texts.DateTime(fromRun.CollectedAtUtc), fromRun.Id);
            if (comparison is not null)
            {
                var language = AppSession.Language;
                foreach (var c in comparison.StatusChanges)
                {
                    var row = new ChangeRow { Change = c, Title = c.RuleId.Length == 0 ? c.Detail ?? "" : plant.Title(c.RuleId, language), Severity = plant.Rule(c.RuleId)?.Severity };
                    (row.IsWorse || c.Kind == StatusChangeKind.ExceptionExpired ? Worse : Other).Add(row);
                }
                foreach (var d in comparison.Drift) Drift.Add(new DriftRow { Drift = d });
                if (problem is null && fromRun is not null && comparison.StatusChanges.Count == 0 && comparison.Drift.Count == 0) EmptyText = Loc.T("Comparison_NoChanges");
            }
        }
        foreach (var n in new[] { nameof(HasWorse), nameof(HasOther), nameof(HasDrift), nameof(HasComparison), nameof(WorseHeader), nameof(OtherHeader),
                     nameof(DriftHeader), nameof(FromText), nameof(ToText), nameof(EmptyText), nameof(IdentityChanged), nameof(ApprovedText) })
            Raise(n);
    }

    private static Run? TryLoad(PlantContext plant, string runId)
    {
        try { return plant.Services.Plants.LoadRun(plant.Plant, runId); }
        catch (Exception e) when (e is RiegelException or IOException or UnauthorizedAccessException) { return null; }
    }

    private async Task ApproveRunAsync()
    {
        if (Machine?.Latest is not { } run || Plant is null) return;
        var roles = Plant.Plant.Settings.SignOff.Select(s => s.Role).Concat([Loc.C("Role_QualityAssurance"), Loc.C("Role_PlantOwner")]);
        var dialog = new ApprovalDialogViewModel(Loc.F("ApproveRun_Title", run.HostName), Loc.T("ApproveRun_Intro"),
            [Loc.F("ApproveRun_Item", run.HostName, Texts.DateTime(run.CollectedAtUtc), run.Id)], roles, Plant.Today, withReview: false);
        if (!Dialogs.Show(dialog)) return;
        await Session.ApproveRunAsync(run.HostName, run.Id, dialog.Result!);
    }

    public override async Task PrepareScreenshotAsync()
    {
        _suppress = true;
        _host = Hosts.FirstOrDefault(h => h.Value == "HMI-L3-02") ?? Hosts.FirstOrDefault();
        _suppress = false;
        Raise(nameof(Host));
        AgainstApproved = Machine?.Approved is not null;
        await RefreshAsync();
    }
}
