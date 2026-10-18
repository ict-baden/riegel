// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Tests.TestSupport;

/// <summary>
/// Baut Regeln für Tests (Vertrag spec 2.3), z. B. eigene Testregeln je Prüfart der Engine:
/// <code>
/// var rule = RuleBuilder.Check("NET-08", "network")
///     .Registry(@"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "RequireSecuritySignature",
///               RegValueType.Dword, CheckOperator.Eq, 1, WhenMissing.Fail)
///     .Verified("MSDOCS-CCBY", "https://learn.microsoft.com/…")
///     .Build();
/// </code>
/// Standard: aktiv, mittlere Schwere, Client und Server, jede Domänenrolle, Texte en+de als Platzhalter.
/// </summary>
public sealed class RuleBuilder
{
    private string _id;
    private string _category;
    private RuleKind _kind;
    private Core.Model.Severity _severity = Core.Model.Severity.Medium;
    private RuleState _state = RuleState.Active;
    private string _since = "2026.10";
    private LocalizedText _title;
    private LocalizedText _rationale;
    private LocalizedText? _otNote;
    private LocalizedText? _remedy;
    private ReasonCategory? _suggested;
    private Applicability _applies = new(["client", "server"], null, null, DomainScope.Any, false, [], null, AbsentCapability.NotApplicable);
    private bool _needsAdmin;
    private CheckSpec _check = new LifecycleCheck();
    private RemediationSpec? _remediation;
    private readonly List<RuleReference> _references = [];
    private readonly List<TextSource> _textSources = [];
    private readonly List<VerificationNote> _verified = [];
    private readonly List<RuleTestVector> _tests = [];
    private string _ruleSetId = "riegel";

    private RuleBuilder(string id, string category, RuleKind kind)
    {
        _id = id;
        _category = category;
        _kind = kind;
        _title = LocalizedText.Of(("en", $"Test rule {id}"), ("de", $"Testregel {id}"));
        _rationale = LocalizedText.Of(("en", "Rationale of the test rule."), ("de", "Begründung der Testregel."));
    }

    /// <summary>Prüfregel (kind check).</summary>
    public static RuleBuilder Check(string id, string category = "system") => new(id, category, RuleKind.Check);

    /// <summary>Bestandsregel (kind inventory).</summary>
    public static RuleBuilder Inventory(string id, string category = "system") => new(id, category, RuleKind.Inventory);

    public RuleBuilder Id(string id) { _id = id; return this; }
    public RuleBuilder Category(string category) { _category = category; return this; }
    public RuleBuilder Severity(Core.Model.Severity severity) { _severity = severity; return this; }
    public RuleBuilder Retired() { _state = RuleState.Retired; return this; }
    public RuleBuilder Since(string version) { _since = version; return this; }
    public RuleBuilder RuleSet(string ruleSetId) { _ruleSetId = ruleSetId; return this; }
    public RuleBuilder Title(string en, string de) { _title = LocalizedText.Of(("en", en), ("de", de)); return this; }
    public RuleBuilder Rationale(string en, string de) { _rationale = LocalizedText.Of(("en", en), ("de", de)); return this; }
    public RuleBuilder OtNote(string en, string de) { _otNote = LocalizedText.Of(("en", en), ("de", de)); return this; }
    public RuleBuilder Remedy(string en, string de) { _remedy = LocalizedText.Of(("en", en), ("de", de)); return this; }
    public RuleBuilder SuggestedReason(ReasonCategory reason) { _suggested = reason; return this; }
    public RuleBuilder NeedsAdmin(bool needsAdmin = true) { _needsAdmin = needsAdmin; return this; }
    public RuleBuilder Remediation(RemediationSpec remediation) { _remediation = remediation; return this; }

    // ---------------------------------------------------------------- Anwendbarkeit

    public RuleBuilder AppliesTo(Applicability applicability) { _applies = applicability; return this; }
    public RuleBuilder Os(params string[] os) { _applies = _applies with { Os = os }; return this; }
    public RuleBuilder Builds(int? minBuild, int? maxBuild = null) { _applies = _applies with { MinBuild = minBuild, MaxBuild = maxBuild }; return this; }
    public RuleBuilder Domain(DomainScope domain) { _applies = _applies with { Domain = domain }; return this; }
    public RuleBuilder ExcludeDc(bool exclude = true) { _applies = _applies with { ExcludeDc = exclude }; return this; }

    public RuleBuilder Capability(string capability, AbsentCapability ifAbsent)
    {
        _applies = _applies with { Capability = capability, IfCapabilityAbsent = ifAbsent };
        return this;
    }

    /// <summary>Faktbedingung, z. B. <c>When("office.installed", "eq", true)</c>.</summary>
    public RuleBuilder When(string fact, string op, object value)
    {
        _applies = _applies with { When = [.. _applies.When, new FactCondition(fact, op, JsonSerializer.SerializeToElement(value))] };
        return this;
    }

    // ---------------------------------------------------------------- Prüfung

    /// <summary>Beliebige Prüfung aus dem geschlossenen Vokabular.</summary>
    public RuleBuilder With(CheckSpec check) { _check = check; return this; }

    /// <summary>Registry-Prüfung (häufigster Fall).</summary>
    public RuleBuilder Registry(string key, string value, RegValueType type, CheckOperator op, object? expected, WhenMissing whenMissing,
        IReadOnlyList<OsDefault>? osDefaults = null, MissingPass? missingPass = null, IReadOnlyList<RegValueType>? acceptTypes = null)
    {
        _check = new RegistryCheck(key, value, type, acceptTypes, op, expected is null ? null : JsonSerializer.SerializeToElement(expected),
            whenMissing, osDefaults, missingPass, null);
        return this;
    }

    // ---------------------------------------------------------------- Belege und Verweise

    public RuleBuilder Reference(RuleReference reference) { _references.Add(reference); return this; }

    /// <summary>Beleg des Sollwerts (für check-Regeln Pflicht: ACSC, MSDOCS-CCBY, MSLEARN-FACT oder DISA).</summary>
    public RuleBuilder Verified(string src, string url, DateOnly? date = null, string? doc = null)
    {
        _verified.Add(new VerificationNote(src, doc, url, date ?? new DateOnly(2026, 10, 18), null, null));
        return this;
    }

    public RuleBuilder TextSource(TextSource source) { _textSources.Add(source); return this; }

    // ---------------------------------------------------------------- Testvektoren

    /// <summary>Vektor mit Rechnerkopf, Einträgen und erwartetem Status.</summary>
    public RuleBuilder Vector(string name, ResultStatus expect, IReadOnlyDictionary<string, VectorItem> items, VectorMachine? machine = null,
        IReadOnlyList<CoverageEntry>? coverage = null, IReadOnlyDictionary<string, JsonElement>? facts = null, params string[] expectFlags)
    {
        _tests.Add(new RuleTestVector(name, machine ?? DefaultMachine, coverage, facts, items, expect, expectFlags.Length == 0 ? null : expectFlags));
        return this;
    }

    /// <summary>Rechnerkopf der Vektoren ohne Angabe: Windows 11 24H2, Client, Arbeitsgruppe, Administrator.</summary>
    public static VectorMachine DefaultMachine { get; } = new(26100, 6899, "Professional", "Client", 1, 0, true);

    /// <summary>Eintrag für einen Vektor.</summary>
    public static VectorItem Item(ItemStatus status, object? value = null) =>
        new(status, value is null ? null : JsonSerializer.SerializeToElement(value));

    public Rule Build() => new(_id, _since, _state, _category, _severity, _kind, _title, _rationale, _otNote, _remedy, _suggested, _applies,
        _needsAdmin, _check, _remediation, [.. _references], [.. _textSources], [.. _verified], [.. _tests], _ruleSetId);

    /// <summary>Eine Regeldatei <c>riegel.rules/1</c> aus Regeln.</summary>
    public static RuleFile File(string prefix, params Rule[] rules) => new(SchemaIds.Rules, prefix, rules);
}
