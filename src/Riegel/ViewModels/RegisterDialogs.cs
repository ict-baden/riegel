// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Session;
using Riegel.UI;

namespace Riegel.ViewModels;

/// <summary>Geltungsbereich zur Auswahl: dieser Rechner, seine Gruppe, die ganze Anlage.</summary>
internal sealed record ScopeChoice(RegisterScope Scope, string Text)
{
    public override string ToString() => Text;

    public static IReadOnlyList<ScopeChoice> For(PlantContext plant, string host)
    {
        var list = new List<ScopeChoice> { new(new RegisterScope([host], [], false), Loc.F("Scope_ThisMachine", host)) };
        if (plant.Machine(host)?.Group is { } g) list.Add(new(new RegisterScope([], [g.Id], false), Loc.F("Scope_ThisGroup", g.Name, Texts.Role(g.Role))));
        list.Add(new(new RegisterScope([], [], true), Loc.T("Scope_All")));
        return list;
    }

    /// <summary>Auswahl um einen vorhandenen Bereich ergänzen (falls er keiner der Vorschläge ist).</summary>
    public static IReadOnlyList<ScopeChoice> With(IReadOnlyList<ScopeChoice> choices, RegisterScope existing, Plant plant)
    {
        if (choices.Any(c => Same(c.Scope, existing))) return choices;
        return [new ScopeChoice(existing, Texts.Scope(existing, plant)), .. choices];
    }

    public static bool Same(RegisterScope a, RegisterScope b) =>
        a.AllMachines == b.AllMachines && a.Machines.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(b.Machines.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
        && a.Groups.Order(StringComparer.Ordinal).SequenceEqual(b.Groups.Order(StringComparer.Ordinal), StringComparer.Ordinal);
}

/// <summary>Gemeinsames der Register-Dialoge: Prüfung beim Tippen, Fehler und Warnungen mit Code.</summary>
internal abstract class EntryDialogViewModel : ObservableObject, IDialogViewModel
{
    private IReadOnlyList<string> _errors = [];
    private IReadOnlyList<string> _warnings = [];

    public abstract string Title { get; }

    public IReadOnlyList<string> Errors { get => _errors; private set => Set(ref _errors, value); }

    public IReadOnlyList<string> Warnings { get => _warnings; private set => Set(ref _warnings, value); }

    public bool HasErrors => Errors.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;

    public virtual bool CanAccept => !HasErrors;

    /// <summary>Eintrag aus den Feldern bilden und prüfen.</summary>
    protected abstract IReadOnlyList<ValidationIssue> Validate();

    protected void Revalidate()
    {
        var issues = Validate();
        Errors = [.. issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => $"{i.Code}: {i.Message}")];
        Warnings = [.. issues.Where(i => i.Severity == IssueSeverity.Warning).Select(i => $"{i.Code}: {i.Message}")];
        Raise(nameof(HasErrors));
        Raise(nameof(HasWarnings));
        Raise(nameof(CanAccept));
    }

    public abstract bool Accept();

    protected static string? N(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>
/// Dialog „Als bewusst offen begründen“ (spec 2.11): vorbelegt aus OT-Hinweis und vorgeschlagener Begründungsart der
/// Regel, akzeptierter Wert = kanonischer Istwert (änderbar). Die Vorbelegung muss bestätigt werden. Die Freigabe trägt
/// erst „Genehmigung eintragen“ ein – bis dahin ist die Ausnahme beantragt.
/// </summary>
internal sealed class ExceptionDialogViewModel : EntryDialogViewModel
{
    private readonly PlantContext _plant;
    private readonly ExceptionEntry? _existing;
    private readonly Rule? _rule;
    private ScopeChoice _scope;
    private Choice<ReasonCategory>? _category;
    private string _justification;
    private string _compensating;
    private string _reference;
    private string _accepted;
    private string _createdByRole;
    private string _comment;
    private bool _confirmed;

    private ExceptionDialogViewModel(PlantContext plant, ExceptionEntry? existing, string ruleId, Rule? rule, string title, IReadOnlyList<ScopeChoice> scopes,
        ScopeChoice scope, ReasonCategory? category, string justification, string accepted, bool prefilled)
    {
        _plant = plant;
        _existing = existing;
        _rule = rule;
        RuleId = ruleId;
        RuleTitle = title;
        Scopes = scopes;
        _scope = scope;
        Categories = [.. Enum.GetValues<ReasonCategory>().Select(c => new Choice<ReasonCategory>(c, Texts.Reason(c)))];
        _category = category is { } c ? Categories.First(x => x.Value == c) : null;
        _justification = justification;
        _compensating = existing?.CompensatingMeasure ?? "";
        _reference = existing?.Reference ?? "";
        _accepted = accepted;
        _createdByRole = existing?.CreatedByRole ?? plant.Plant.Settings.OperatorRole ?? "";
        _comment = existing?.Comment ?? "";
        Prefilled = prefilled;
        _confirmed = !prefilled;
        Revalidate();
    }

    /// <summary>Neue Ausnahme zu einem Ergebnis, vorbelegt aus der Regel.</summary>
    public static ExceptionDialogViewModel ForResult(PlantContext plant, string host, RuleResult result, Rule? rule, string title)
    {
        var scopes = ScopeChoice.For(plant, host);
        var ot = rule?.OtNote?.Get(AppSession.Language) ?? "";
        return new ExceptionDialogViewModel(plant, null, result.RuleId, rule, title, scopes, scopes[0], rule?.SuggestedReason, ot,
            result.Actual ?? "*", prefilled: rule?.OtNote is not null || rule?.SuggestedReason is not null);
    }

    /// <summary>Vorhandene Ausnahme bearbeiten.</summary>
    public static ExceptionDialogViewModel ForEntry(PlantContext plant, ExceptionEntry entry, Rule? rule, string title)
    {
        var host = entry.Scope.Machines.FirstOrDefault() ?? plant.Hosts.FirstOrDefault() ?? "";
        var scopes = ScopeChoice.With(ScopeChoice.For(plant, host), entry.Scope, plant.Plant);
        return new ExceptionDialogViewModel(plant, entry, entry.RuleId, rule, title, scopes, scopes.First(s => ScopeChoice.Same(s.Scope, entry.Scope)),
            entry.Category, entry.Justification, entry.AcceptedValue ?? "", prefilled: false);
    }

    public override string Title => _existing is null ? Loc.F("ExceptionDialog_TitleNew", RuleId) : Loc.F("ExceptionDialog_TitleEdit", _existing.Id, RuleId);

    public string RuleId { get; }
    public string RuleTitle { get; }
    public string Severity => _rule is null ? "" : Texts.Severity(_rule.Severity);
    public IReadOnlyList<ScopeChoice> Scopes { get; }
    public IReadOnlyList<Choice<ReasonCategory>> Categories { get; }

    /// <summary>Begründung und Begründungsart kommen aus dem Regelwerk – bitte prüfen.</summary>
    public bool Prefilled { get; }

    /// <summary>Freigegebener Eintrag: jede inhaltliche Änderung macht ihn wieder „beantragt“.</summary>
    public bool WasApproved => _existing is not null && RegisterRules.Complete(_existing.Approval);

    public ScopeChoice Scope { get => _scope; set { if (Set(ref _scope, value)) Revalidate(); } }
    public Choice<ReasonCategory>? Category { get => _category; set { if (Set(ref _category, value)) { Raise(nameof(NeedsReference)); Raise(nameof(NeedsCompensating)); Revalidate(); } } }
    public string Justification { get => _justification; set { if (Set(ref _justification, value)) Revalidate(); } }
    public string CompensatingMeasure { get => _compensating; set { if (Set(ref _compensating, value)) Revalidate(); } }
    public string Reference { get => _reference; set { if (Set(ref _reference, value)) Revalidate(); } }
    public string AcceptedValue { get => _accepted; set { if (Set(ref _accepted, value)) Revalidate(); } }
    public string CreatedByRole { get => _createdByRole; set => Set(ref _createdByRole, value); }
    public string Comment { get => _comment; set => Set(ref _comment, value); }
    public bool Confirmed { get => _confirmed; set { if (Set(ref _confirmed, value)) Raise(nameof(CanAccept)); } }

    public bool NeedsReference => Category?.Value == ReasonCategory.VendorRequirement;
    public bool NeedsCompensating => Category?.Value == ReasonCategory.CompensatingControl;

    public string JustificationCounter => Loc.F("Dialog_Characters", Justification.Trim().Length, RegisterRules.MinTextLength);

    public override bool CanAccept => base.CanAccept && Confirmed && Category is not null;

    public ExceptionEntry? Result { get; private set; }

    private ExceptionEntry Build() => new(
        _existing?.Id ?? RegisterRules.NextId("EX-", _plant.Registers.Exceptions.Exceptions.Select(e => e.Id)),
        RuleId, Scope.Scope, Category?.Value ?? ReasonCategory.VendorRequirement, Justification.Trim(), N(CompensatingMeasure), N(Reference),
        N(AcceptedValue), _existing?.Approval ?? Approval.None, _existing?.CreatedOn ?? _plant.Today, N(CreatedByRole),
        _existing is null ? null : _plant.Today, _plant.Catalogue.Version, N(Comment));

    protected override IReadOnlyList<ValidationIssue> Validate()
    {
        Raise(nameof(JustificationCounter));
        return RegisterRules.IssuesFor(Build(), _rule, _plant.Plant.Settings);
    }

    public override bool Accept()
    {
        Revalidate();
        if (!CanAccept) return false;
        Result = Build();
        return true;
    }
}

/// <summary>Dialog „Zuschneiden“ (spec 2.12): Regel ist nicht Teil der Soll-Konfiguration dieser Rolle.</summary>
internal sealed class TailoringDialogViewModel : EntryDialogViewModel
{
    private readonly PlantContext _plant;
    private readonly TailoringEntry? _existing;
    private readonly Rule? _rule;
    private ScopeChoice _scope;
    private string _reason;
    private string _createdByRole;
    private string _comment;

    public TailoringDialogViewModel(PlantContext plant, TailoringEntry? existing, string host, string ruleId, Rule? rule, string title)
    {
        _plant = plant;
        _existing = existing;
        _rule = rule;
        RuleId = ruleId;
        RuleTitle = title;
        var choices = ScopeChoice.For(plant, host);
        Scopes = existing is null ? choices : ScopeChoice.With(choices, existing.Scope, plant.Plant);
        // Zuschnitt gilt typisch für eine Rolle: die Gruppe vorschlagen, wenn es eine gibt
        _scope = existing is not null ? Scopes.First(s => ScopeChoice.Same(s.Scope, existing.Scope)) : choices.Count > 2 ? choices[1] : choices[0];
        _reason = existing?.Reason ?? (rule?.SuggestedReason == ReasonCategory.NotRelevantHere ? rule.OtNote?.Get(AppSession.Language) ?? "" : "");
        _createdByRole = existing?.CreatedByRole ?? plant.Plant.Settings.OperatorRole ?? "";
        _comment = existing?.Comment ?? "";
        Revalidate();
    }

    public override string Title => _existing is null ? Loc.F("TailoringDialog_TitleNew", RuleId) : Loc.F("TailoringDialog_TitleEdit", _existing.Id, RuleId);
    public string RuleId { get; }
    public string RuleTitle { get; }
    public IReadOnlyList<ScopeChoice> Scopes { get; }
    public bool WasApproved => _existing is not null && RegisterRules.Complete(_existing.Approval);
    public ScopeChoice Scope { get => _scope; set { if (Set(ref _scope, value)) Revalidate(); } }
    public string Reason { get => _reason; set { if (Set(ref _reason, value)) Revalidate(); } }
    public string CreatedByRole { get => _createdByRole; set => Set(ref _createdByRole, value); }
    public string Comment { get => _comment; set => Set(ref _comment, value); }
    public string ReasonCounter => Loc.F("Dialog_Characters", Reason.Trim().Length, RegisterRules.MinTextLength);
    public TailoringEntry? Result { get; private set; }

    private TailoringEntry Build() => new(
        _existing?.Id ?? RegisterRules.NextId("TL-", _plant.Registers.Tailoring.Entries.Select(e => e.Id)),
        RuleId, Scope.Scope, Reason.Trim(), _existing?.Approval ?? Approval.None, _existing?.CreatedOn ?? _plant.Today, N(CreatedByRole),
        _existing is null ? null : _plant.Today, N(Comment));

    protected override IReadOnlyList<ValidationIssue> Validate()
    {
        Raise(nameof(ReasonCounter));
        return RegisterRules.IssuesFor(Build(), _rule);
    }

    public override bool Accept()
    {
        Revalidate();
        if (!CanAccept) return false;
        Result = Build();
        return true;
    }
}

/// <summary>Dialog „Maßnahme planen“ (spec 2.13): Aktion, verantwortliche Rolle, Termin, Ticket, Stand.</summary>
internal sealed class MeasureDialogViewModel : EntryDialogViewModel
{
    private readonly PlantContext _plant;
    private readonly MeasureEntry? _existing;
    private readonly Rule? _rule;
    private ScopeChoice _scope;
    private string _action;
    private string _owner;
    private DateTime? _due;
    private string _ticket;
    private Choice<MeasureState> _state;
    private string _comment;

    public MeasureDialogViewModel(PlantContext plant, MeasureEntry? existing, string host, string ruleId, Rule? rule, string title)
    {
        _plant = plant;
        _existing = existing;
        _rule = rule;
        RuleId = ruleId;
        RuleTitle = title;
        var choices = ScopeChoice.For(plant, host);
        Scopes = existing is null ? choices : ScopeChoice.With(choices, existing.Scope, plant.Plant);
        _scope = existing is not null ? Scopes.First(s => ScopeChoice.Same(s.Scope, existing.Scope)) : choices[0];
        _action = existing?.Action ?? rule?.Remedy?.Get(AppSession.Language) ?? "";
        _owner = existing?.OwnerRole ?? plant.Plant.Settings.OperatorRole ?? "";
        _due = (existing?.DueDate ?? plant.Today.AddDays(90)).ToDateTime(TimeOnly.MinValue);
        _ticket = existing?.Ticket ?? "";
        States = [.. Enum.GetValues<MeasureState>().Select(s => new Choice<MeasureState>(s, Texts.MeasureState(s)))];
        _state = States.First(s => s.Value == (existing?.State ?? MeasureState.Planned));
        _comment = existing?.Comment ?? "";
        Revalidate();
    }

    public override string Title => _existing is null ? Loc.F("MeasureDialog_TitleNew", RuleId) : Loc.F("MeasureDialog_TitleEdit", _existing.Id, RuleId);
    public string RuleId { get; }
    public string RuleTitle { get; }
    public IReadOnlyList<ScopeChoice> Scopes { get; }
    public IReadOnlyList<Choice<MeasureState>> States { get; }
    public ScopeChoice Scope { get => _scope; set { if (Set(ref _scope, value)) Revalidate(); } }
    public string Action { get => _action; set { if (Set(ref _action, value)) Revalidate(); } }
    public string OwnerRole { get => _owner; set { if (Set(ref _owner, value)) Revalidate(); } }
    public DateTime? DueDate { get => _due; set { if (Set(ref _due, value)) Revalidate(); } }
    public string Ticket { get => _ticket; set => Set(ref _ticket, value); }
    public Choice<MeasureState> State { get => _state; set => Set(ref _state, value); }
    public string Comment { get => _comment; set => Set(ref _comment, value); }
    public MeasureEntry? Result { get; private set; }

    public override bool CanAccept => base.CanAccept && DueDate is not null;

    private MeasureEntry Build() => new(
        _existing?.Id ?? RegisterRules.NextId("M-", _plant.Registers.Measures.Measures.Select(e => e.Id)),
        RuleId, Scope.Scope, Action.Trim(), OwnerRole.Trim(), DateOnly.FromDateTime(DueDate ?? DateTime.Today), N(Ticket), State.Value,
        _existing?.CreatedOn ?? _plant.Today, _existing?.CreatedByRole ?? _plant.Plant.Settings.OperatorRole, _existing is null ? null : _plant.Today, N(Comment));

    protected override IReadOnlyList<ValidationIssue> Validate() => RegisterRules.IssuesFor(Build(), _rule);

    public override bool Accept()
    {
        Revalidate();
        if (!CanAccept) return false;
        Result = Build();
        return true;
    }
}

/// <summary>
/// Dialog „Genehmigung eintragen“ (spec 2.11) für ausgewählte Einträge – oder „Diesen Lauf freigeben“ (spec 2.14, ohne
/// Prüftermin): Rolle, Datum, Prüftermin (höchstens 36 Monate, Warnung ab 12), Freigabe-Nachweis, optional Name.
/// </summary>
internal sealed class ApprovalDialogViewModel : ObservableObject, IDialogViewModel
{
    private string _role;
    private DateTime? _on;
    private DateTime? _reviewBy;
    private string _record;
    private string _name = "";
    private IReadOnlyList<string> _errors = [];
    private IReadOnlyList<string> _warnings = [];

    public ApprovalDialogViewModel(string title, string intro, IReadOnlyList<string> items, IEnumerable<string> suggestedRoles, DateOnly today,
        bool withReview, string? record = null)
    {
        Title = title;
        Intro = intro;
        Items = items;
        WithReview = withReview;
        Roles = [.. suggestedRoles.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase)];
        _role = Roles.FirstOrDefault() ?? "";
        _on = today.ToDateTime(TimeOnly.MinValue);
        _reviewBy = withReview ? today.AddMonths(RegisterRules.WarnReviewMonths).ToDateTime(TimeOnly.MinValue) : null;
        _record = record ?? "";
        Revalidate();
    }

    public string Title { get; }
    public string Intro { get; }
    public IReadOnlyList<string> Items { get; }
    public bool WithReview { get; }
    public IReadOnlyList<string> Roles { get; }
    public string Role { get => _role; set { if (Set(ref _role, value)) Revalidate(); } }
    public DateTime? ApprovedOn { get => _on; set { if (Set(ref _on, value)) Revalidate(); } }
    public DateTime? ReviewBy { get => _reviewBy; set { if (Set(ref _reviewBy, value)) Revalidate(); } }
    public string Record { get => _record; set { if (Set(ref _record, value)) Revalidate(); } }
    public string Name { get => _name; set => Set(ref _name, value); }
    public IReadOnlyList<string> Errors { get => _errors; private set => Set(ref _errors, value); }
    public IReadOnlyList<string> Warnings { get => _warnings; private set => Set(ref _warnings, value); }
    public bool HasErrors => Errors.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;
    public bool CanAccept => !HasErrors;
    public Approval? Result { get; private set; }

    private void Revalidate()
    {
        var on = ApprovedOn is { } a ? DateOnly.FromDateTime(a) : (DateOnly?)null;
        var review = WithReview ? (ReviewBy is { } r ? DateOnly.FromDateTime(r) : null) : on;
        var errors = RegisterRules.ApprovalErrors(Role, on, review, Record, out var warnings);
        Errors = [.. errors.Select(c => RglCodesApp.Message(c))];
        Warnings = [.. warnings.Select(c => RglCodesApp.Message(c))];
        Raise(nameof(HasErrors));
        Raise(nameof(HasWarnings));
        Raise(nameof(CanAccept));
    }

    public bool Accept()
    {
        Revalidate();
        if (!CanAccept) return false;
        var on = DateOnly.FromDateTime(ApprovedOn!.Value);
        Result = new Approval(Role.Trim(), string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(), on,
            WithReview ? DateOnly.FromDateTime(ReviewBy!.Value) : null, Record.Trim(), null);
        return true;
    }
}
