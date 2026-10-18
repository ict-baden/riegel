// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Plants;

/// <summary>
/// Testhilfen des Pakets workflow: fester Tag, Dienste mit fester Uhr, ein kleiner Katalog ohne Engine-Prüfung und
/// eine geskriptete Auswertung (die echte Engine ist ein anderes Paket – hier zählt nur, was der Arbeitsablauf aus den
/// Rohergebnissen macht).
/// </summary>
internal static class WorkflowKit
{
    /// <summary>Der feste „heute“ aller Tests (spec: 2026-10-18).</summary>
    public static readonly DateOnly Today = new(2026, 10, 18);

    public static RiegelServices Services(FixedClock? clock = null) => RiegelServices.Create(new NullPlatform(), clock ?? new FixedClock());

    /// <summary>Registry-Schlüssel, aus dem <see cref="ScriptedEvaluator"/> die Rohstatus liest.</summary>
    public const string ScriptKey = @"HKLM\SOFTWARE\Policies\RiegelTest";

    public static Rule Check(string id, string category = "network", Severity severity = Severity.Medium) =>
        RuleBuilder.Check(id, category).Severity(severity)
            .Registry(@"HKLM\SOFTWARE\Policies\Beispiel", id, RegValueType.Dword, CheckOperator.Eq, 1, WhenMissing.Fail)
            .Verified("ACSC", "https://www.cyber.gov.au/copyright").Build();

    public static Rule Inventory(string id, string category = "accounts") =>
        RuleBuilder.Inventory(id, category).Severity(Severity.Low).With(new InventoryCheck(InventoryRenderer.LocalAdmins, [ItemKeys.LocalAdministrators])).Build();

    /// <summary>Auswertung, die je Regel den Rohstatus aus <see cref="ScriptKey"/> liest: Wert „status|actual|flags|source“.</summary>
    public static SnapshotBuilder Script(this SnapshotBuilder b, string ruleId, string spec) =>
        b.RegistryValue(ScriptKey, ruleId, RegValueType.Sz, spec);

    public static RegisterScope Machines(params string[] hosts) => new(hosts, [], false);
    public static RegisterScope Groups(params string[] groups) => new([], groups, false);
    public static RegisterScope All { get; } = new([], [], true);

    public const string Justification = "Begründung mit mehr als zwanzig Zeichen für den Test.";

    public static ExceptionEntry Exception(string id, string ruleId, RegisterScope scope, string? accepted = "0",
        ReasonCategory category = ReasonCategory.TechnicalNecessity, string justification = Justification) =>
        new(id, ruleId, scope, category, justification, category == ReasonCategory.CompensatingControl ? "Firewall-Regel FW-1" : null,
            category == ReasonCategory.VendorRequirement ? "Herstellerdokument HD-4711" : null, accepted, Approval.None, Today.AddMonths(-2),
            "Anlagenverantwortlicher", null, "2026.10", null);

    public static TailoringEntry Tailoring(string id, string ruleId, RegisterScope scope) =>
        new(id, ruleId, scope, "Dieser Rolle fehlt die Funktion, deshalb nicht im Prüfumfang.", Approval.None, Today.AddMonths(-2),
            "Anlagenverantwortlicher", null, null);

    public static MeasureEntry Measure(string id, string ruleId, RegisterScope scope, DateOnly due, MeasureState state = MeasureState.Planned) =>
        new(id, ruleId, scope, "Einstellung im Wartungsfenster korrigieren", "Anlagenverantwortlicher", due, "TICKET-1", state, Today.AddMonths(-1),
            "Anlagenverantwortlicher", null, null);

    /// <summary>Gültige, gebundene Freigabe (Rolle, Datum, Prüftermin, Nachweis, Inhalts-Hash).</summary>
    public static ExceptionEntry Approve(this ExceptionEntry e, DateOnly? on = null, DateOnly? reviewBy = null, string role = "Informationssicherheitsbeauftragter")
    {
        var approvedOn = on ?? Today.AddMonths(-1);
        return e with { Approval = new Approval(role, null, approvedOn, reviewBy ?? approvedOn.AddMonths(12), "CHG-TEST-0001", ContentHashes.Of(e)) };
    }

    public static TailoringEntry Approve(this TailoringEntry t, DateOnly? on = null, DateOnly? reviewBy = null)
    {
        var approvedOn = on ?? Today.AddMonths(-1);
        return t with { Approval = new Approval("Informationssicherheitsbeauftragter", null, approvedOn, reviewBy ?? approvedOn.AddMonths(12), "CHG-TEST-0002", ContentHashes.Of(t)) };
    }

    public static RegisterSet Set(IEnumerable<ExceptionEntry>? exceptions = null, IEnumerable<TailoringEntry>? tailoring = null,
        IEnumerable<MeasureEntry>? measures = null, string plantId = "testanlage") =>
        new(new ExceptionFile(SchemaIds.Exceptions, plantId, [.. exceptions ?? []]), new TailoringFile(SchemaIds.Tailoring, plantId, [.. tailoring ?? []]),
            new MeasureFile(SchemaIds.Measures, plantId, [.. measures ?? []]), RegisterHashes.Empty);

    /// <summary>Kleine Anlage: Gruppe g-hmi mit HMI-TEST, W11-TEST ohne Gruppe.</summary>
    public static Plant Plant(PlantSettings? settings = null) => TempPlant.Default() with { Settings = settings ?? PlantSettings.Default };

    public static RuleResult Result(string ruleId, ResultStatus status, Severity severity = Severity.Medium, string? actual = "0",
        ResultFlags flags = ResultFlags.None, string ruleSet = "riegel") =>
        new(ruleSet, ruleId, status, severity, "1", actual, ValueSource.LocalPolicy, null, [], null, null, null, flags, null, null);

    public static string ToJson<T>(T value) => RiegelJson.Serialize(value);
}

/// <summary>Katalog für Tests: nur Regeln, keine Tabellen; SHA-256 über die Regel-IDs.</summary>
internal sealed class TestCatalogue(IEnumerable<Rule> rules, DateOnly? releasedOn = null, BuildTable? builds = null) : ICatalogue
{
    private readonly List<Rule> _rules = [.. rules];

    public string Id => "riegel";
    public string Version => "2026.10";
    public DateOnly ReleasedOn { get; } = releasedOn ?? new DateOnly(2026, 10, 18);
    public string Sha256 => Hashes.Sha256Hex(string.Join("\n", _rules.Select(r => r.RuleSetId + "|" + r.Id + "|" + r.State)));
    public IReadOnlyList<Rule> Rules => _rules;
    public Rule? Find(string id) => _rules.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
    public LifecycleTable Lifecycle { get; init; } = LifecycleTable.Empty;
    public BuildTable Builds { get; } = builds ?? BuildTable.Empty;
    public CapabilityTable Capabilities => CapabilityTable.Empty;
    public CatalogueData Data => CatalogueData.Empty;
}

/// <summary>
/// Auswertung nach Drehbuch: je Regel des Katalogs der Wert <c>status|actual|flags|source</c> unter
/// <see cref="WorkflowKit.ScriptKey"/> (fehlt er: notCheckable); zurückgezogene Regeln werden nicht ausgewertet. Beispiel: „fail|0“,
/// „pass|1“, „notCheckable||clockSuspect“, „fail|0||domainGpo“.
/// </summary>
internal sealed class ScriptedEvaluator : IEvaluator
{
    public IReadOnlyList<RuleResult> Evaluate(Snapshot s, ICatalogue c, EvaluationContext ctx)
    {
        var item = s.Items.FirstOrDefault(i => ItemKeys.Same(i.Key, ItemKeys.Registry(WorkflowKit.ScriptKey)));
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (item?.Value is { } v && v.TryGetProperty("values", out var named))
            foreach (var p in named.EnumerateObject())
                if (p.Value.TryGetProperty("v", out var text) && text.ValueKind == JsonValueKind.String) values[p.Name] = text.GetString()!;
        return
        [
            .. c.Rules.Where(r => r.State == RuleState.Active).OrderBy(r => r.RuleSetId, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).Select(rule =>
            {
                var parts = (values.TryGetValue(rule.Id, out var spec) ? spec : "notCheckable").Split('|');
                var status = Enum.Parse<ResultStatus>(parts[0], ignoreCase: true);
                string? actual = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
                var flags = ResultFlags.None;
                if (parts.Length > 2)
                    foreach (var f in parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries)) flags |= Enum.Parse<ResultFlags>(f, ignoreCase: true);
                var source = parts.Length > 3 ? Enum.Parse<ValueSource>(parts[3], ignoreCase: true) : ValueSource.LocalPolicy;
                return new RuleResult(rule.RuleSetId, rule.Id, status, rule.Severity, "1", actual, source, null, [ItemKeys.Registry(WorkflowKit.ScriptKey)],
                    null, null, null, flags, status == ResultStatus.NotCheckable ? "RGL-0003" : null, null);
            }),
        ];
    }
}
