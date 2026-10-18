// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.RegularExpressions;
using Riegel.Core.Evaluation;
using Riegel.Core.Facts;
using Riegel.Core.Model;

namespace Riegel.Core.Catalogue;

/// <summary>
/// Inhaltliche Prüfung eines Katalogs beim Laden (spec 2.3): Operatoren passend zum Werttyp, Standardwerte mit
/// Quelle und Datum, bekannte Fähigkeiten und Fakten, eindeutige IDs und Fähigkeiten, gültige Tabellen. Ein Katalog
/// mit Fehlern wird nie benutzt (RGL-0200 bis RGL-0212).
/// </summary>
internal static partial class CatalogueValidator
{
    private static readonly HashSet<CheckOperator> NumberOps =
    [
        CheckOperator.Eq, CheckOperator.Ne, CheckOperator.Lt, CheckOperator.Le, CheckOperator.Gt, CheckOperator.Ge, CheckOperator.In,
        CheckOperator.NotIn, CheckOperator.BitsSet, CheckOperator.BitsClear, CheckOperator.Exists, CheckOperator.NotExists,
    ];

    private static readonly HashSet<CheckOperator> TextOps =
        [CheckOperator.Eq, CheckOperator.Ne, CheckOperator.In, CheckOperator.NotIn, CheckOperator.Exists, CheckOperator.NotExists, CheckOperator.AgeDaysLe, CheckOperator.AgeDaysGt];

    private static readonly HashSet<CheckOperator> ListOps =
        [CheckOperator.Eq, CheckOperator.Ne, CheckOperator.ContainsAll, CheckOperator.ContainsNone, CheckOperator.Exists, CheckOperator.NotExists];

    private static readonly HashSet<CheckOperator> BinaryOps = [CheckOperator.Eq, CheckOperator.Ne, CheckOperator.Exists, CheckOperator.NotExists];

    private static readonly string[] StartModes = ["Boot", "System", "Auto", "Manual", "Disabled"];

    [GeneratedRegex(@"^[A-Z]{2,5}-\d{2}$")]
    private static partial Regex CoreId();

    [GeneratedRegex(@"^IMP-[a-z0-9-]+-[A-Za-z0-9_.-]+$")]
    private static partial Regex ImportedId();

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$")]
    private static partial Regex BracedGuid();

    [GeneratedRegex(@"^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$")]
    private static partial Regex PlainGuid();

    [GeneratedRegex(@"^(S-1-\d+(-\d+)*|\*-\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SidPattern();

    [GeneratedRegex(@"^Se[A-Za-z]+(Privilege|Right)$")]
    private static partial Regex RightName();

    /// <summary>Alle Probleme der Regeln (gegen die Fähigkeitstabelle des Katalogs).</summary>
    public static List<CatalogueIssue> ValidateRules(IReadOnlyList<Rule> rules, CapabilityTable capabilities)
    {
        var issues = new List<CatalogueIssue>();
        var known = new HashSet<string>(capabilities.Capabilities.Select(c => c.Capability), StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            if (rule is null) { issues.Add(new(RglCodesEngine.InvalidRule, "rules", "null")); continue; }
            var where = string.IsNullOrWhiteSpace(rule.Id) ? "(no id)" : rule.Id;
            void Add(string code, string what) => issues.Add(new(code, where, what));
            if (string.IsNullOrWhiteSpace(rule.Id)) { Add(RglCodesEngine.InvalidRule, "id"); continue; }
            if (!ids.Add(rule.Id)) Add(RglCodesEngine.DuplicateRuleId, "id");
            bool core = rule.RuleSetId == "riegel";
            if (core && !CoreId().IsMatch(rule.Id)) Add(RglCodesEngine.InvalidRule, "id format <PREFIX>-<NN>");
            if (!core && !ImportedId().IsMatch(rule.Id)) Add(RglCodesEngine.BaselineConflict, "id format IMP-<baseline>-<id>");
            if (string.IsNullOrWhiteSpace(rule.RuleSetId)) Add(RglCodesEngine.InvalidRule, "ruleSetId");
            if (!RuleCategories.IsKnown(rule.Category)) Add(RglCodesEngine.InvalidRule, $"category \"{rule.Category}\"");
            if (string.IsNullOrWhiteSpace(rule.Since)) Add(RglCodesEngine.InvalidRule, "since");
            if (rule.Title is null || rule.Title.Get("en").Length == 0) Add(RglCodesEngine.InvalidRule, "title");
            if (rule.AppliesTo is null) Add(RglCodesEngine.InvalidRule, "appliesTo");
            else ValidateApplicability(rule.AppliesTo, known, Add);
            if (rule.Check is null) { Add(RglCodesEngine.InvalidRule, "check"); continue; }
            if (rule.Kind == RuleKind.Check && Flatten(rule.Check).Any(c => c is InventoryCheck))
                Add(RglCodesEngine.InvalidRule, "inventory check in a rule of kind check");
            if (rule.Check is not InventoryCheck && Flatten(rule.Check).Any(c => c is InventoryCheck))
                Add(RglCodesEngine.InvalidRule, "inventory check nested in a composite");
            ValidateCheck(rule.Check, rule, "check", Add, 0);
        }
        return issues;
    }

    private static void ValidateApplicability(Applicability a, HashSet<string> capabilities, Action<string, string> add)
    {
        foreach (var os in a.Os)
            if (os is not ("client" or "server")) add(RglCodesEngine.InvalidRule, $"appliesTo.os \"{os}\"");
        if (a.MinBuild is { } min && a.MaxBuild is { } max && min > max) add(RglCodesEngine.InvalidRule, "appliesTo minBuild > maxBuild");
        if (a.Capability is { } cap && !capabilities.Contains(cap)) add(RglCodesEngine.UnknownCapability, $"capability \"{cap}\"");
        foreach (var w in a.When)
        {
            if (w is null || string.IsNullOrWhiteSpace(w.Fact)) { add(RglCodesEngine.UnknownFact, "when.fact"); continue; }
            var described = FactNames.Describe(w.Fact, capabilities.Contains);
            if (described is not { } d) { add(RglCodesEngine.UnknownFact, $"fact \"{w.Fact}\""); continue; }
            if (w.Op is not ("eq" or "ne" or "in" or "notIn")) { add(RglCodesEngine.InvalidRule, $"when.op \"{w.Op}\""); continue; }
            var values = w.Op is "in" or "notIn"
                ? (w.Value.ValueKind == JsonValueKind.Array ? w.Value.EnumerateArray().ToList() : null)
                : [w.Value];
            if (values is null) { add(RglCodesEngine.UnknownFact, $"fact \"{w.Fact}\": {w.Op} needs a list"); continue; }
            foreach (var v in values)
            {
                bool fits = d.Kind switch
                {
                    FactKind.Bool => v.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    FactKind.Number => v.ValueKind == JsonValueKind.Number,
                    _ => v.ValueKind == JsonValueKind.String && (d.Values is null || d.Values.Contains(v.GetString()!, StringComparer.OrdinalIgnoreCase)),
                };
                if (!fits) add(RglCodesEngine.UnknownFact, $"fact \"{w.Fact}\": value {v.GetRawText()} does not fit");
            }
        }
    }

    /// <summary>Alle Prüfungen einer Regel, auch die verschachtelten.</summary>
    public static IEnumerable<CheckSpec> Flatten(CheckSpec check)
    {
        if (check is null) yield break;
        yield return check;
        IEnumerable<CheckSpec> children = check switch
        {
            FirstOfCheck f => f.Checks,
            AllOfCheck a => a.Checks,
            AnyOfCheck a => a.Checks,
            NoneOfCheck n => n.Checks,
            NotCheck n => n.Check is null ? [] : [n.Check],
            _ => [],
        };
        foreach (var child in children)
            foreach (var nested in Flatten(child)) yield return nested;
    }

    private static void ValidateCheck(CheckSpec check, Rule rule, string path, Action<string, string> add, int depth)
    {
        void Bad(string code, string what) => add(code, $"{path} ({check.Type}): {what}");
        if (depth > 32) { Bad(RglCodesEngine.InvalidRule, "nested too deep"); return; }
        switch (check)
        {
            case FirstOfCheck or AllOfCheck or AnyOfCheck or NoneOfCheck:
            {
                var children = check switch
                {
                    FirstOfCheck f => f.Checks,
                    AllOfCheck a => a.Checks,
                    AnyOfCheck a => a.Checks,
                    _ => ((NoneOfCheck)check).Checks,
                };
                if (children.Count == 0) Bad(RglCodesEngine.InvalidRule, "checks is empty");
                for (int i = 0; i < children.Count; i++)
                    if (children[i] is null) Bad(RglCodesEngine.InvalidRule, $"checks[{i}] is null");
                    else ValidateCheck(children[i], rule, $"{path}.checks[{i}]", add, depth + 1);
                break;
            }
            case NotCheck n:
                if (n.Check is null) Bad(RglCodesEngine.InvalidRule, "check is missing");
                else ValidateCheck(n.Check, rule, path + ".check", add, depth + 1);
                break;
            case RegistryCheck r:
                RegistryKeyValid(r.Key, Bad);
                if (r.Value is null) Bad(RglCodesEngine.InvalidRule, "value is missing");
                RegistryComparison(r.ValueType, r.AcceptTypes, r.Op, r.Expected, r.WhenMissing, r.OsDefaults, r.MissingPass, r.MultiSz, Bad);
                break;
            case RegistryEffectiveCheck e:
                if (e.Sources.Count == 0) Bad(RglCodesEngine.InvalidRule, "sources is empty");
                foreach (var s in e.Sources)
                {
                    if (s is null) { Bad(RglCodesEngine.InvalidRule, "source is null"); continue; }
                    RegistryKeyValid(s.Key, Bad);
                    if (s.Value is null) Bad(RglCodesEngine.InvalidRule, "source value is missing");
                }
                RegistryComparison(e.ValueType, e.AcceptTypes, e.Op, e.Expected, e.WhenMissing, e.OsDefaults, e.MissingPass, e.MultiSz, Bad);
                break;
            case SeceditCheck s:
                if (string.IsNullOrWhiteSpace(s.Section) || string.IsNullOrWhiteSpace(s.Key)) Bad(RglCodesEngine.InvalidRule, "section and key are required");
                if (s.WhenMissing is not (WhenMissing.Fail or WhenMissing.NotCheckable))
                    Bad(RglCodesEngine.MissingRuleIncomplete, $"whenMissing {s.WhenMissing} is not supported for secedit (fail or notCheckable)");
                switch (s.ValueKind)
                {
                    case SeceditValueKind.Number: OperatorFits(s.Op, s.Expected, NumberOps, ExpectedKind.Number, Bad); break;
                    case SeceditValueKind.String: OperatorFits(s.Op, s.Expected, TextOps.Except([CheckOperator.AgeDaysLe, CheckOperator.AgeDaysGt]).ToHashSet(), ExpectedKind.Text, Bad); break;
                    default:
                    {
                        // [Registry Values]: der Sollwert bestimmt den erwarteten Typ (Zahl = DWORD, Text = SZ, Liste = MULTI_SZ)
                        bool membership = s.Op is CheckOperator.In or CheckOperator.NotIn;
                        bool list = !membership && s.Expected is { ValueKind: JsonValueKind.Array };
                        bool text = s.Expected is { ValueKind: JsonValueKind.String }
                                    || (membership && s.Expected is { ValueKind: JsonValueKind.Array } m && m.GetArrayLength() > 0 && m[0].ValueKind == JsonValueKind.String);
                        if (list) OperatorFits(s.Op, s.Expected, ListOps, ExpectedKind.List, Bad);
                        else if (text) OperatorFits(s.Op, s.Expected, TextOps.Except([CheckOperator.AgeDaysLe, CheckOperator.AgeDaysGt]).ToHashSet(), ExpectedKind.Text, Bad);
                        else OperatorFits(s.Op, s.Expected, NumberOps, ExpectedKind.Number, Bad);
                        break;
                    }
                }
                break;
            case AccountPolicyCheck a:
            {
                var ops = a.Field is AccountPolicyField.PasswordComplexity or AccountPolicyField.ClearTextPassword
                    ? new HashSet<CheckOperator> { CheckOperator.Eq, CheckOperator.Ne, CheckOperator.In, CheckOperator.NotIn }
                    : new HashSet<CheckOperator> { CheckOperator.Eq, CheckOperator.Ne, CheckOperator.Lt, CheckOperator.Le, CheckOperator.Gt, CheckOperator.Ge, CheckOperator.In, CheckOperator.NotIn };
                if (a.Field is AccountPolicyField.PasswordComplexity or AccountPolicyField.ClearTextPassword && a.Expected is { ValueKind: JsonValueKind.True or JsonValueKind.False })
                {
                    if (!ops.Contains(a.Op)) Bad(RglCodesEngine.OperatorMismatch, $"op {a.Op} for {a.Field}");
                }
                else OperatorFits(a.Op, a.Expected, ops, ExpectedKind.Number, Bad);
                break;
            }
            case AuditSubcategoryCheck au:
                if (au.Guid is null || !BracedGuid().IsMatch(au.Guid)) Bad(RglCodesEngine.InvalidRule, "guid must be {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}");
                break;
            case UserRightCheck u:
                if (u.Right is null || !RightName().IsMatch(u.Right)) Bad(RglCodesEngine.InvalidRule, $"right \"{u.Right}\"");
                if (u.Mode != UserRightMode.Empty && u.Sids.Count == 0) Bad(RglCodesEngine.InvalidRule, "sids is empty");
                foreach (var sid in u.Sids)
                    if (sid is null || !SidPattern().IsMatch(sid.Trim())) Bad(RglCodesEngine.InvalidRule, $"sid \"{sid}\" (well-known SID or *-<RID>)");
                break;
            case ServiceCheck sv:
                if (string.IsNullOrWhiteSpace(sv.Name)) Bad(RglCodesEngine.InvalidRule, "name is required");
                // Eine Bestandsregel (spec 2.1: geplante Prüfung ohne belegten Sollwert) meldet nur Starttyp und Zustand
                if (rule.Kind == RuleKind.Check && (sv.StartMode?.Count ?? 0) == 0 && (sv.State?.Count ?? 0) == 0)
                    Bad(RglCodesEngine.InvalidRule, "startMode or state is required");
                foreach (var m in sv.StartMode ?? [])
                    if (!StartModes.Contains(m, StringComparer.OrdinalIgnoreCase)) Bad(RglCodesEngine.InvalidRule, $"startMode \"{m}\"");
                foreach (var st in sv.State ?? [])
                    if (string.IsNullOrWhiteSpace(st)) Bad(RglCodesEngine.InvalidRule, "state is empty");
                break;
            case FeatureCheck f:
                if (string.IsNullOrWhiteSpace(f.Name)) Bad(RglCodesEngine.InvalidRule, "name is required");
                if (f.InstallState.Count == 0 || f.InstallState.Any(s => s is < 1 or > 3)) Bad(RglCodesEngine.InvalidRule, "installState must be 1, 2 or 3");
                break;
            case WmiPropertyCheck w:
                if (w.Item is null || !w.Item.StartsWith("wmi:", StringComparison.OrdinalIgnoreCase)) Bad(RglCodesEngine.InvalidRule, "item must be a wmi: key");
                if (string.IsNullOrWhiteSpace(w.Property)) Bad(RglCodesEngine.InvalidRule, "property is required");
                if (w.RowFilter is { } rf)
                {
                    if (string.IsNullOrWhiteSpace(rf.Property)) Bad(RglCodesEngine.InvalidRule, "rowFilter.property is required");
                    if (rf.Op is not (CheckOperator.Eq or CheckOperator.Ne or CheckOperator.In or CheckOperator.NotIn or CheckOperator.Lt or CheckOperator.Le or CheckOperator.Gt or CheckOperator.Ge))
                        Bad(RglCodesEngine.OperatorMismatch, $"rowFilter.op {rf.Op}");
                    else JsonOperatorFits(rf.Op, rf.Value, Bad);
                }
                JsonOperatorFits(w.Op, w.Expected, Bad);
                break;
            case DefenderSettingCheck d:
                if (string.IsNullOrWhiteSpace(d.Property)) Bad(RglCodesEngine.InvalidRule, "property is required");
                JsonOperatorFits(d.Op, d.Expected, Bad);
                break;
            case AsrRuleCheck asr:
                if (asr.Guid is null || !PlainGuid().IsMatch(asr.Guid)) Bad(RglCodesEngine.InvalidRule, "guid must be xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx");
                break;
            case FirewallExposureCheck fe:
                if (fe.Ports.Count == 0) Bad(RglCodesEngine.InvalidRule, "ports is empty");
                foreach (var p in fe.Ports)
                    if (p is null || p.Port is < 1 or > 65535) Bad(RglCodesEngine.InvalidRule, "port must be 1–65535");
                break;
            case UpdateAgeCheck ua:
                if (ua.MaxDays is < 0) Bad(RglCodesEngine.InvalidRule, "maxDays < 0");
                break;
            case AvStatusCheck av:
                if (av.MaxSignatureAgeDays is < 0) Bad(RglCodesEngine.InvalidRule, "maxSignatureAgeDays < 0");
                break;
            case LocalAccountCheck la:
                if (la.Rid < 0) Bad(RglCodesEngine.InvalidRule, "rid < 0");
                break;
            case LocalAccountsCheck las:
                if (las.ExcludeRids?.Any(r => r < 0) == true) Bad(RglCodesEngine.InvalidRule, "excludeRids < 0");
                break;
            case LocalGroupCheck lg:
                if (lg.Sid is null || !SidPattern().IsMatch(lg.Sid) || lg.Sid.StartsWith('*')) Bad(RglCodesEngine.InvalidRule, $"sid \"{lg.Sid}\"");
                if (lg.MaxMembers is < 0) Bad(RglCodesEngine.InvalidRule, "maxMembers < 0");
                foreach (var s in lg.AllowedSids ?? [])
                    if (s is null || !SidPattern().IsMatch(s.Trim())) Bad(RglCodesEngine.InvalidRule, $"allowedSids \"{s}\"");
                if (rule.Kind == RuleKind.Check && lg.MaxMembers is null && lg.AllowedSids is null)
                    Bad(RglCodesEngine.InvalidRule, "maxMembers or allowedSids is required in a check rule");
                break;
            case InventoryCheck inv:
                if (inv.Items.Count == 0) Bad(RglCodesEngine.InvalidRule, "items is empty");
                foreach (var item in inv.Items)
                    if (item is null || !Regex.IsMatch(item, "^(reg|wmi|api|evt|raw|parsed):", RegexOptions.IgnoreCase)) Bad(RglCodesEngine.InvalidRule, $"item \"{item}\"");
                break;
        }
    }

    private static void RegistryKeyValid(string? key, Action<string, string> bad)
    {
        var k = key?.StartsWith("reg:", StringComparison.OrdinalIgnoreCase) == true ? key[4..] : key;
        if (string.IsNullOrWhiteSpace(k) || !(k.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase) || k.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase)))
            bad(RglCodesEngine.InvalidRule, $"key \"{key}\" must start with HKLM\\ (machine settings only)");
    }

    private enum ExpectedKind { Number, Text, List, Binary, Any }

    private static void RegistryComparison(RegValueType type, IReadOnlyList<RegValueType>? accept, CheckOperator op, JsonElement? expected,
        WhenMissing whenMissing, IReadOnlyList<OsDefault>? osDefaults, MissingPass? missingPass, MultiSzMode? multiSz, Action<string, string> bad)
    {
        var (ops, kind) = type switch
        {
            RegValueType.Dword or RegValueType.Qword => (NumberOps, ExpectedKind.Number),
            RegValueType.Sz or RegValueType.ExpandSz => (TextOps, ExpectedKind.Text),
            RegValueType.MultiSz => (ListOps, ExpectedKind.List),
            _ => (BinaryOps, ExpectedKind.Binary),
        };
        OperatorFits(op, expected, ops, kind, bad);
        foreach (var a in accept ?? [])
        {
            bool convertible = type switch
            {
                RegValueType.Dword or RegValueType.Qword => a is RegValueType.Dword or RegValueType.Qword or RegValueType.Sz or RegValueType.ExpandSz,
                RegValueType.Sz or RegValueType.ExpandSz => a is RegValueType.Sz or RegValueType.ExpandSz or RegValueType.Dword or RegValueType.Qword,
                RegValueType.MultiSz => a is RegValueType.MultiSz or RegValueType.Sz or RegValueType.ExpandSz,
                _ => a == RegValueType.Binary,
            };
            if (!convertible) bad(RglCodesEngine.OperatorMismatch, $"acceptTypes {a} cannot be compared as {type}");
        }
        if (multiSz is not null && type != RegValueType.MultiSz) bad(RglCodesEngine.OperatorMismatch, "multiSz only for valueType multi_sz");
        foreach (var d in osDefaults ?? [])
        {
            if (d is null) { bad(RglCodesEngine.DefaultWithoutSource, "osDefault is null"); continue; }
            if (string.IsNullOrWhiteSpace(d.Source) || !d.Source.StartsWith("https://", StringComparison.Ordinal) || d.Seen == default)
                bad(RglCodesEngine.DefaultWithoutSource, "osDefault without source (https) or seen date");
            if (d.MinBuild is { } min && d.MaxBuild is { } max && min > max) bad(RglCodesEngine.InvalidRule, "osDefault minBuild > maxBuild");
            bool valueFits = kind switch
            {
                ExpectedKind.Number => ValueComparison.ExpectedNumber(d.Value) is not null,
                ExpectedKind.Text => d.Value.ValueKind == JsonValueKind.String,
                ExpectedKind.List => d.Value.ValueKind == JsonValueKind.Array,
                _ => d.Value.ValueKind == JsonValueKind.String && ValueComparison.ParseHex(d.Value.GetString()!) is not null,
            };
            if (!valueFits) bad(RglCodesEngine.OperatorMismatch, $"osDefault value {d.Value.GetRawText()} does not fit {type}");
        }
        if (whenMissing == WhenMissing.OsDefault && (osDefaults is null || osDefaults.Count == 0))
            bad(RglCodesEngine.MissingRuleIncomplete, "whenMissing osDefault without osDefaults");
        if (whenMissing == WhenMissing.Pass)
        {
            if (missingPass is null) bad(RglCodesEngine.MissingRuleIncomplete, "whenMissing pass without missingPass");
            else if (string.IsNullOrWhiteSpace(missingPass.Source) || !missingPass.Source.StartsWith("https://", StringComparison.Ordinal) || missingPass.Seen == default
                     || string.IsNullOrWhiteSpace(missingPass.Note))
                bad(RglCodesEngine.DefaultWithoutSource, "missingPass without source (https), seen date or note");
        }
    }

    /// <summary>Operator für den Werttyp erlaubt und Sollwert vorhanden mit passendem JSON-Typ.</summary>
    private static void OperatorFits(CheckOperator op, JsonElement? expected, IReadOnlySet<CheckOperator> allowed, ExpectedKind kind, Action<string, string> bad)
    {
        if (!allowed.Contains(op)) { bad(RglCodesEngine.OperatorMismatch, $"op {op} not defined for this value type"); return; }
        if (op is CheckOperator.Exists or CheckOperator.NotExists) return;
        if (expected is not { } e || e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) { bad(RglCodesEngine.OperatorMismatch, $"op {op} needs expected"); return; }
        bool Scalar(JsonElement v) => kind switch
        {
            ExpectedKind.Number => ValueComparison.ExpectedNumber(v) is { } n && (op is not (CheckOperator.BitsSet or CheckOperator.BitsClear) || (n >= 0 && decimal.Truncate(n) == n)),
            ExpectedKind.Text => v.ValueKind == JsonValueKind.String,
            ExpectedKind.Binary => v.ValueKind == JsonValueKind.String && ValueComparison.ParseHex(v.GetString()!) is not null,
            _ => true,
        };
        bool ok = op switch
        {
            CheckOperator.In or CheckOperator.NotIn => e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0 && e.EnumerateArray().All(Scalar),
            CheckOperator.AgeDaysLe or CheckOperator.AgeDaysGt => ValueComparison.ExpectedNumber(e) is { } days && days >= 0,
            CheckOperator.ContainsAll or CheckOperator.ContainsNone => e.ValueKind == JsonValueKind.Array && e.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String),
            _ when kind == ExpectedKind.List => e.ValueKind == JsonValueKind.Array && e.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String),
            _ => Scalar(e),
        };
        if (!ok) bad(RglCodesEngine.OperatorMismatch, $"expected {e.GetRawText()} does not fit op {op}");
    }

    /// <summary>Operatoren auf WMI-/API-Werten: der Sollwert bestimmt den Typ (Zahl, Text, Wahrheitswert, Liste).</summary>
    private static void JsonOperatorFits(CheckOperator op, JsonElement? expected, Action<string, string> bad)
    {
        if (op is CheckOperator.Exists or CheckOperator.NotExists) return;
        if (expected is not { } e || e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) { bad(RglCodesEngine.OperatorMismatch, $"op {op} needs expected"); return; }
        bool ok = op switch
        {
            CheckOperator.Eq or CheckOperator.Ne => e.ValueKind is not JsonValueKind.Object,
            CheckOperator.In or CheckOperator.NotIn => e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0
                                                         && e.EnumerateArray().All(x => x.ValueKind is JsonValueKind.Number or JsonValueKind.String or JsonValueKind.True or JsonValueKind.False),
            CheckOperator.Lt or CheckOperator.Le or CheckOperator.Gt or CheckOperator.Ge => ValueComparison.ExpectedNumber(e) is not null,
            CheckOperator.BitsSet or CheckOperator.BitsClear => ValueComparison.ExpectedNumber(e) is { } n && n >= 0 && decimal.Truncate(n) == n,
            CheckOperator.ContainsAll or CheckOperator.ContainsNone => e.ValueKind == JsonValueKind.Array,
            CheckOperator.AgeDaysLe or CheckOperator.AgeDaysGt => ValueComparison.ExpectedNumber(e) is { } d && d >= 0,
            _ => false,
        };
        if (!ok) bad(RglCodesEngine.OperatorMismatch, $"expected {e.GetRawText()} does not fit op {op}");
    }

    // ---------------------------------------------------------------- Tabellen

    public static List<CatalogueIssue> ValidateTables(LifecycleTable lifecycle, BuildTable builds, CapabilityTable capabilities)
    {
        var issues = new List<CatalogueIssue>();
        void Add(string code, string where, string what) => issues.Add(new(code, where, what));
        var productIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in lifecycle.Products)
        {
            if (p is null) { Add(RglCodesEngine.InvalidTable, "lifecycle", "null"); continue; }
            var where = $"lifecycle {p.Id}";
            if (string.IsNullOrWhiteSpace(p.Id) || !productIds.Add(p.Id)) Add(RglCodesEngine.InvalidTable, where, "id missing or duplicate");
            if (p.Os is not ("client" or "server")) Add(RglCodesEngine.InvalidTable, where, $"os \"{p.Os}\"");
            if (string.IsNullOrWhiteSpace(p.Source) || !p.Source.StartsWith("https://", StringComparison.Ordinal) || p.Seen == default)
                Add(RglCodesEngine.InvalidTable, where, "source (https) and seen date required");
        }
        // Ein Build darf einmal ohne os oder je einmal mit os client/server vorkommen, nicht gemischt
        var buildKeys = new HashSet<(int, string?)>();
        foreach (var b in builds.Builds)
        {
            if (b is null) { Add(RglCodesEngine.InvalidTable, "builds", "null"); continue; }
            var where = $"builds {b.Build}";
            if (b.Os is not (null or "client" or "server")) Add(RglCodesEngine.InvalidTable, where, $"os \"{b.Os}\"");
            bool mixed = b.Os is null ? buildKeys.Any(k => k.Item1 == b.Build) : buildKeys.Contains((b.Build, null));
            if (!buildKeys.Add((b.Build, b.Os)) || mixed) Add(RglCodesEngine.InvalidTable, where, "duplicate build");
            if (string.IsNullOrWhiteSpace(b.Source) || !b.Source.StartsWith("https://", StringComparison.Ordinal) || b.Seen == default)
                Add(RglCodesEngine.InvalidTable, where, "source (https) and seen date required");
            for (int i = 1; i < b.Releases.Count; i++)
            {
                if (b.Releases[i].Ubr <= b.Releases[i - 1].Ubr) Add(RglCodesEngine.InvalidTable, where, "releases must be in ascending UBR");
                if (b.Releases[i].Date < b.Releases[i - 1].Date) Add(RglCodesEngine.InvalidTable, where, "release dates must not decrease with the UBR");
            }
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in capabilities.Capabilities)
        {
            if (c is null) { Add(RglCodesEngine.InvalidTable, "capabilities", "null"); continue; }
            var where = $"capability {c.Capability}";
            if (string.IsNullOrWhiteSpace(c.Capability)) Add(RglCodesEngine.InvalidTable, where, "name missing");
            else if (!names.Add(c.Capability)) Add(RglCodesEngine.DuplicateCapability, where, "defined more than once");
            if (string.IsNullOrWhiteSpace(c.Source) || !c.Source.StartsWith("https://", StringComparison.Ordinal) || c.Seen == default)
                Add(RglCodesEngine.InvalidTable, where, "source (https) and seen date required");
            if (c.Exists.Count == 0) Add(RglCodesEngine.InvalidTable, where, "exists is empty");
            foreach (var e in c.Exists)
                if (e?.Os is { } os && os is not ("client" or "server")) Add(RglCodesEngine.InvalidTable, where, $"os \"{os}\"");
        }
        return issues;
    }

    /// <summary>Wirft bei Problemen eine Ausnahme mit dem Code des ersten Problems und allen Stellen (höchstens 20).</summary>
    public static void ThrowIfAny(IReadOnlyList<CatalogueIssue> issues)
    {
        if (issues.Count == 0) return;
        var shown = string.Join("; ", issues.Take(20).Select(i => $"{i.Code} {i}"));
        if (issues.Count > 20) shown += $"; … (+{issues.Count - 20})";
        throw RglCodesEngine.Exception(issues[0].Code, shown);
    }
}
