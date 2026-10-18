// SPDX-License-Identifier: MIT
using System.Collections.Immutable;
using System.Text.Json;
using Riegel.Core.Facts;
using Riegel.Core.Model;
using Riegel.Core.Services;

namespace Riegel.Core.Evaluation;

/// <summary>
/// Die Auswertung (spec 2.3, 2.4, 2.5): jede Regel des Katalogs ergibt genau ein Ergebnis mit rohem Status und
/// Flags. Register, Regressionen und Wertung macht das Paket workflow. Ein „erfüllt“ entsteht nur aus einem
/// vorhandenen Beleg oder einem belegten Standardwert (Flag FromDefault); alles Fehlende, Verweigerte, Abgeschnittene
/// oder nicht Erfasste wird „nicht prüfbar“ mit einem RGL-Code.
/// </summary>
internal sealed class Evaluator(RiegelServices? services, Func<Snapshot, AttributionMap>? attribution = null) : IEvaluator
{
    public IReadOnlyList<RuleResult> Evaluate(Snapshot s, ICatalogue c, EvaluationContext ctx) => Evaluate(s, c, ctx, null, null);

    /// <summary>
    /// Wie <see cref="Evaluate(Snapshot, ICatalogue, EvaluationContext)"/>, mit überschriebenen Fakten (Testvektoren)
    /// und optional nur für ausgewählte Regeln.
    /// </summary>
    internal IReadOnlyList<RuleResult> Evaluate(Snapshot s, ICatalogue c, EvaluationContext ctx,
        IReadOnlyDictionary<string, JsonElement>? factOverrides, IEnumerable<Rule>? only)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(ctx);
        var session = CreateSession(s, c, ctx, factOverrides);
        var rules = (only ?? c.Rules).OrderBy(r => r.RuleSetId, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal);
        return [.. rules.Select(r => session.EvaluateRule(r).Result)];
    }

    /// <summary>Für Tests: Ergebnis und die entscheidenden Belege je Regel.</summary>
    internal IReadOnlyList<(RuleResult Result, ImmutableHashSet<string> Deciding)> EvaluateDetailed(Snapshot s, ICatalogue c,
        EvaluationContext ctx, IReadOnlyDictionary<string, JsonElement>? factOverrides, IEnumerable<Rule>? only)
    {
        var session = CreateSession(s, c, ctx, factOverrides);
        var rules = (only ?? c.Rules).OrderBy(r => r.RuleSetId, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal);
        return [.. rules.Select(session.EvaluateRule)];
    }

    private EvaluationSession CreateSession(Snapshot s, ICatalogue c, EvaluationContext ctx, IReadOnlyDictionary<string, JsonElement>? factOverrides)
    {
        var store = new EvidenceStore(s);
        var facts = FactsDeriver.DeriveDetailed(store, c);
        facts.Override(factOverrides);
        // Herkunft der Werte (spec 2.10) aus dem Paket parsing; ohne Zuordnung bleibt sie Unknown
        AttributionMap map;
        try { map = attribution?.Invoke(s) ?? services?.Attribution.Build(s) ?? AttributionMap.Empty; }
        catch (RiegelException) { map = AttributionMap.Empty; }
        var clock = EvaluationClock.Assess(s, c, ctx, facts);
        return new EvaluationSession(store, facts, c, ctx, map, clock, EngineText.For(ctx.Language));
    }
}

/// <summary>Eine Auswertung eines Exports: Belege, Fakten, Zeitbasis und die Prüfarten.</summary>
internal sealed partial class EvaluationSession
{
    private const int MaxDepth = 32;
    private readonly HashSet<string> _ruleKeys = new(StringComparer.OrdinalIgnoreCase);
    private ImmutableHashSet<string>.Builder? _leafKeys;

    public EvaluationSession(EvidenceStore store, FactSet facts, ICatalogue catalogue, EvaluationContext context,
        AttributionMap attribution, ClockAssessment clock, EngineText text)
    {
        Store = store;
        Facts = facts;
        Catalogue = catalogue;
        Context = context;
        Attribution = attribution;
        Clock = clock;
        T = text;
        Store.Observer = key =>
        {
            _ruleKeys.Add(key);
            _leafKeys?.Add(key);
        };
    }

    public EvidenceStore Store { get; }
    public FactSet Facts { get; }
    public ICatalogue Catalogue { get; }
    public EvaluationContext Context { get; }
    public AttributionMap Attribution { get; }
    public ClockAssessment Clock { get; }
    public EngineText T { get; }

    /// <summary>Die Regel, die gerade ausgewertet wird (für Sperren nach defender.mode und Fähigkeit).</summary>
    public Rule Rule { get; private set; } = null!;

    public DateTimeOffset CollectedAtUtc => Store.Snapshot.CollectedAtUtc;

    // ---------------------------------------------------------------- eine Regel

    public (RuleResult Result, ImmutableHashSet<string> Deciding) EvaluateRule(Rule rule)
    {
        Rule = rule;
        _ruleKeys.Clear();
        Outcome outcome;
        try
        {
            if (rule.State == RuleState.Retired)
                outcome = Outcome.NotApplicable(RglCodesEngine.Retired);
            else
            {
                outcome = Applicability(rule) ?? Run(rule.Check, 0);
                // Bestandsregeln sind nie ein Befund für sich (spec 2.1): pass/fail werden zu info
                if (rule.Kind == RuleKind.Inventory && outcome.IsDecisive) outcome = outcome with { Status = ResultStatus.Info };
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Nie ein Absturz und nie ein „erfüllt“: ein Fehler in der Auswertung macht die Regel „nicht prüfbar“
            outcome = Outcome.NotCheckable(RglCodesEngine.EvaluationFailed).With(T["Detail_EvaluationFailed", e.GetType().Name]);
        }
        var keys = _ruleKeys.Select(Store.Canonical).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ThenBy(k => k, StringComparer.Ordinal).ToList();
        var result = new RuleResult(rule.RuleSetId, rule.Id, outcome.Status, rule.Severity, outcome.Expected, outcome.Actual,
            outcome.Source, outcome.SourceDetail, keys, null, null, null, outcome.Flags,
            outcome.Status is ResultStatus.Pass or ResultStatus.Fail or ResultStatus.Info ? null : outcome.ReasonCode,
            outcome.Details.Count == 0 ? null : outcome.Details);
        return (result, outcome.Deciding);
    }

    // ---------------------------------------------------------------- Anwendbarkeit (spec 2.3 „appliesTo“)

    /// <summary>null = anwendbar; sonst notApplicable, notCheckable (Anwendbarkeit unbekannt) oder fail (Fähigkeit fehlt).</summary>
    private Outcome? Applicability(Rule rule)
    {
        var a = rule.AppliesTo;
        Outcome? notApplicable = null;
        var unknown = new List<string>();
        var sources = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        void Use(string fact) => sources.UnionWith(Facts.SourcesOf(fact));
        void NotApplicable(string reason, string detail) => notApplicable ??= Outcome.NotApplicable(reason).With(detail);

        if (a.Os.Count > 0)
        {
            Use(FactNames.OsKind);
            if (Facts.Kind is not { } kind) unknown.Add(FactNames.OsKind);
            else if (!a.Os.Contains(kind, StringComparer.OrdinalIgnoreCase)) NotApplicable(RglCodesEngine.NotApplicableOs, T["Detail_NaOs", kind]);
        }
        if (a.MinBuild is not null || a.MaxBuild is not null)
        {
            Use(FactNames.OsBuild);
            if (Facts.Build is not { } build) unknown.Add(FactNames.OsBuild);
            else if ((a.MinBuild is { } min && build < min) || (a.MaxBuild is { } max && build > max))
                NotApplicable(RglCodesEngine.NotApplicableBuild, T["Detail_NaBuild", build]);
        }
        if (a.Domain != DomainScope.Any)
        {
            Use(FactNames.DomainJoined);
            if (Facts.DomainJoined is not { } joined) unknown.Add(FactNames.DomainJoined);
            else if (joined != (a.Domain == DomainScope.Joined))
                NotApplicable(RglCodesEngine.NotApplicableDomain, T[joined ? "Detail_NaDomainJoined" : "Detail_NaDomainStandalone"]);
        }
        if (a.ExcludeDc)
        {
            Use(FactNames.DomainIsDc);
            if (Facts.IsDc is not { } dc) unknown.Add(FactNames.DomainIsDc);
            else if (dc) NotApplicable(RglCodesEngine.NotApplicableDomainController, T["Detail_NaDc"]);
        }
        foreach (var condition in a.When)
        {
            Use(condition.Fact);
            if (Facts.Get(condition.Fact) is not { } value) { unknown.Add(condition.Fact); continue; }
            if (!FactCondition(value, condition)) NotApplicable(RglCodesEngine.NotApplicableCondition, T["Detail_NaCondition", condition.Fact, CanonicalValue.Render(value)]);
        }
        bool capabilityAbsentFail = false;
        if (a.Capability is { } capability)
        {
            var fact = FactNames.CapabilityPrefix + capability;
            Use(fact);
            var exists = Facts.Bool(fact);
            if (exists is null) unknown.Add(fact);
            else if (exists == false)
            {
                if (a.IfCapabilityAbsent == AbsentCapability.NotApplicable) NotApplicable(RglCodesEngine.NotApplicableCapability, T["Detail_NaCapability", capability]);
                else capabilityAbsentFail = true;
            }
        }
        // Ein sicheres „nicht anwendbar“ geht vor; dann Unbekanntes; dann das Fehlen einer Schutzfunktion (spec 2.4 Punkt 8)
        if (notApplicable is not null) return notApplicable.WithDeciding(sources);
        if (unknown.Count > 0)
            return Outcome.NotCheckable(RglCodesEngine.ApplicabilityUnknown).With(T["Detail_ApplicabilityUnknown", string.Join(", ", unknown)]).WithDeciding(sources);
        if (capabilityAbsentFail)
            return Outcome.Fail().With(T["Detail_CapabilityAbsentFail", a.Capability!]).WithDeciding(sources);
        return null;
    }

    private static bool FactCondition(JsonElement value, FactCondition condition)
    {
        bool Same(JsonElement a, JsonElement b)
        {
            if (a.ValueKind == JsonValueKind.String && b.ValueKind == JsonValueKind.String)
                return string.Equals(a.GetString(), b.GetString(), StringComparison.OrdinalIgnoreCase);
            if (a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number)
                return a.TryGetDecimal(out var x) && b.TryGetDecimal(out var y) && x == y;
            if (a.ValueKind is JsonValueKind.True or JsonValueKind.False) return a.ValueKind == b.ValueKind;
            return false;
        }
        return condition.Op switch
        {
            "eq" => Same(value, condition.Value),
            "ne" => !Same(value, condition.Value),
            "in" => condition.Value.ValueKind == JsonValueKind.Array && condition.Value.EnumerateArray().Any(v => Same(value, v)),
            "notIn" => condition.Value.ValueKind == JsonValueKind.Array && !condition.Value.EnumerateArray().Any(v => Same(value, v)),
            _ => false,
        };
    }

    // ---------------------------------------------------------------- Prüfungen

    /// <summary>Eine Prüfung auswerten; Blätter merken sich ihre Belege als entscheidend.</summary>
    public Outcome Run(CheckSpec check, int depth)
    {
        if (depth > MaxDepth) return Outcome.NotCheckable(RglCodesEngine.EvaluationFailed);
        switch (check)
        {
            case FirstOfCheck f: return FirstOf(f.Checks, depth);
            case AllOfCheck a: return AllOf(a.Checks, depth);
            case AnyOfCheck a: return AnyOf(a.Checks, depth);
            case NoneOfCheck n: return AnyOf(n.Checks, depth).Negate();
            case NotCheck n: return n.Check is null ? Outcome.NotCheckable(RglCodesEngine.InvalidRule) : Run(n.Check, depth + 1).Negate();
        }
        var outer = _leafKeys;
        var keys = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        _leafKeys = keys;
        try
        {
            var outcome = Leaf(check);
            if (outcome.ExplicitDeciding)
                return outcome with { Deciding = outcome.Deciding.Select(Store.Canonical).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase) };
            return outcome.WithDeciding(keys.Select(Store.Canonical));
        }
        finally
        {
            _leafKeys = outer;
            outer?.UnionWith(keys);
        }
    }

    private Outcome Leaf(CheckSpec check) => check switch
    {
        RegistryCheck c => Registry(c),
        RegistryEffectiveCheck c => RegistryEffective(c),
        SeceditCheck c => Secedit(c),
        AccountPolicyCheck c => AccountPolicy(c),
        AuditSubcategoryCheck c => AuditSubcategory(c),
        UserRightCheck c => UserRight(c),
        ServiceCheck c => Service(c),
        FeatureCheck c => Feature(c),
        WmiPropertyCheck c => WmiProperty(c),
        DefenderSettingCheck c => DefenderSetting(c),
        AsrRuleCheck c => AsrRule(c),
        FirewallProfileCheck c => FirewallProfile(c),
        FirewallExposureCheck c => FirewallExposure(c),
        LifecycleCheck => Lifecycle(),
        UpdateAgeCheck c => UpdateAge(c),
        LsaProtectionCheck => LsaProtection(),
        DeviceGuardCheck c => DeviceGuard(c),
        AvStatusCheck c => AvStatus(c),
        LocalAccountCheck c => LocalAccount(c),
        LocalAccountsCheck c => LocalAccounts(c),
        LocalGroupCheck c => LocalGroup(c),
        InventoryCheck c => Inventory(c),
        _ => Outcome.NotCheckable(RglCodesEngine.UnknownCheckType),
    };

    // ---------------------------------------------------------------- dreiwertige Verknüpfungen (spec 2.4 Punkt 5)

    /// <summary>Beleg-Vorrang: der erste Teil mit pass oder fail entscheidet; sonst nicht prüfbar.</summary>
    private Outcome FirstOf(IReadOnlyList<CheckSpec> checks, int depth)
    {
        Outcome? firstUndecided = null;
        foreach (var check in checks)
        {
            var o = Run(check, depth + 1);
            if (o.IsDecisive) return o;
            firstUndecided ??= o;
        }
        // Keiner entscheidet: nicht prüfbar mit dem Grund (und den Flags) des ersten Teils
        var reason = firstUndecided?.Status == ResultStatus.NotCheckable ? firstUndecided.ReasonCode : RglCodesEngine.NoDecisiveEvidence;
        return (firstUndecided ?? Outcome.NotCheckable(RglCodesEngine.NoDecisiveEvidence)) with
        {
            Status = ResultStatus.NotCheckable,
            ReasonCode = reason ?? RglCodesEngine.NoDecisiveEvidence,
        };
    }

    /// <summary>Jedes fail → fail; alle pass → pass; alle n/a → n/a; sonst nicht prüfbar.</summary>
    private Outcome AllOf(IReadOnlyList<CheckSpec> checks, int depth)
    {
        var parts = checks.Select(c => Run(c, depth + 1)).ToList();
        if (parts.Count == 0) return Outcome.NotCheckable(RglCodesEngine.InvalidRule);
        if (parts.FirstOrDefault(p => p.Status == ResultStatus.Fail) is { } fail) return fail;
        if (parts.All(p => p.Status == ResultStatus.Pass)) return Merge(parts, ResultStatus.Pass);
        if (parts.All(p => p.Status == ResultStatus.NotApplicable)) return Merge(parts, ResultStatus.NotApplicable) with { ReasonCode = parts[0].ReasonCode };
        // Gemischt (z. B. pass und n/a, oder nicht prüfbare Teile): nicht prüfbar
        var undecided = parts.FirstOrDefault(p => p.Status == ResultStatus.NotCheckable) ?? parts.First(p => p.Status != ResultStatus.Pass);
        return undecided with { Status = ResultStatus.NotCheckable, ReasonCode = undecided.Status == ResultStatus.NotCheckable ? undecided.ReasonCode ?? RglCodesEngine.NoDecisiveEvidence : RglCodesEngine.NoDecisiveEvidence };
    }

    /// <summary>Jedes pass → pass; alle fail → fail (n/a zählt nicht, außer alle sind n/a); sonst nicht prüfbar.</summary>
    private Outcome AnyOf(IReadOnlyList<CheckSpec> checks, int depth)
    {
        var parts = checks.Select(c => Run(c, depth + 1)).ToList();
        if (parts.Count == 0) return Outcome.NotCheckable(RglCodesEngine.InvalidRule);
        if (parts.FirstOrDefault(p => p.Status == ResultStatus.Pass) is { } pass) return pass;
        var relevant = parts.Where(p => p.Status != ResultStatus.NotApplicable).ToList();
        if (relevant.Count == 0) return Merge(parts, ResultStatus.NotApplicable) with { ReasonCode = parts[0].ReasonCode };
        if (relevant.All(p => p.Status == ResultStatus.Fail)) return Merge(relevant, ResultStatus.Fail);
        var undecided = relevant.First(p => p.Status != ResultStatus.Fail);
        return undecided with { Status = ResultStatus.NotCheckable, ReasonCode = undecided.Status == ResultStatus.NotCheckable ? undecided.ReasonCode ?? RglCodesEngine.NoDecisiveEvidence : RglCodesEngine.NoDecisiveEvidence };
    }

    /// <summary>Mehrere Teile tragen gemeinsam: Flags, Details und Belege vereinigt, Soll/Ist vom ersten Teil.</summary>
    private static Outcome Merge(IReadOnlyList<Outcome> parts, ResultStatus status)
    {
        var first = parts[0];
        var flags = parts.Aggregate(ResultFlags.None, (f, p) => f | p.Flags);
        var details = parts.SelectMany(p => p.Details).Distinct(StringComparer.Ordinal).ToImmutableList();
        var deciding = parts.Aggregate(first.Deciding, (d, p) => d.Union(p.Deciding));
        return first with { Status = status, Flags = flags, Details = details, Deciding = deciding };
    }
}
