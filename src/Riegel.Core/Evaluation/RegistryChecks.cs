// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Facts;
using Riegel.Core.Model;

namespace Riegel.Core.Evaluation;

/// <summary>Prüfarten registry und registryEffective (spec 2.3, 2.4).</summary>
internal sealed partial class EvaluationSession
{
    /// <summary>Gemeinsame Vergleichsfelder von registry und registryEffective.</summary>
    private sealed record RegistryRule(RegValueType ValueType, IReadOnlyList<RegValueType>? AcceptTypes, CheckOperator Op,
        JsonElement? Expected, WhenMissing WhenMissing, IReadOnlyList<OsDefault>? OsDefaults, MissingPass? MissingPass, MultiSzMode? MultiSz);

    private Outcome Registry(RegistryCheck c)
    {
        // Ein 32-Bit-Windows hat keine 32-Bit-Ansicht HKLM\SOFTWARE\WOW6432Node: dieser Teil ist dort nicht anwendbar
        // (statt „fehlt“ und damit fail, z. B. TLS-09/10 auf Windows 7 x86)
        if (Is32BitWowKey(c.Key))
            return new Outcome(ResultStatus.NotApplicable, RglCodesEngine.NotApplicableOs).DecidedBy(ItemKeys.Registry(c.Key));
        var rule = new RegistryRule(c.ValueType, c.AcceptTypes, c.Op, c.Expected, c.WhenMissing, c.OsDefaults, c.MissingPass, c.MultiSz);
        var evidence = Store.RegistryValue(c.Key, c.Value);
        var outcome = evidence.Class == EvidenceClass.NotSet ? Missing(rule, evidence) : CompareRegistry(rule, evidence, null);
        return AutoLogonTrap(c, outcome);
    }

    /// <summary>
    /// Schlüssel unter HKLM\SOFTWARE\WOW6432Node auf einem 32-Bit-Windows (os.arch x86 oder ARM laut Rechnerkopf).
    /// Unbekannte Architektur: nein – dann gilt der Schlüssel wie jeder andere.
    /// </summary>
    private bool Is32BitWowKey(string key) =>
        key.StartsWith(@"HKLM\SOFTWARE\WOW6432Node\", StringComparison.OrdinalIgnoreCase)
        && Facts.Text(FactNames.OsArch) is { } arch && (arch.Equals("x86", StringComparison.OrdinalIgnoreCase) || arch.Equals("ARM", StringComparison.OrdinalIgnoreCase));

    /// <summary>Die erste vorhandene Quelle gewinnt; eine nicht verfügbare Quelle davor macht die Prüfung „nicht prüfbar“.</summary>
    private Outcome RegistryEffective(RegistryEffectiveCheck c)
    {
        var rule = new RegistryRule(c.ValueType, c.AcceptTypes, c.Op, c.Expected, c.WhenMissing, c.OsDefaults, c.MissingPass, c.MultiSz);
        RegValueEvidence? last = null;
        foreach (var source in c.Sources)
        {
            var evidence = Store.RegistryValue(source.Key, source.Value);
            last = evidence;
            if (evidence.Class == EvidenceClass.NotSet) continue;
            if (evidence.Class == EvidenceClass.Unavailable) return Unavailable(evidence.Reason, evidence.ItemKey, null, ExpectedText(rule));
            return CompareRegistry(rule, evidence, source.Origin).DecidedBy(evidence.ItemKey);
        }
        return last is null ? Outcome.NotCheckable(RglCodesEngine.InvalidRule) : Missing(rule, last);
    }

    private static string? ExpectedText(RegistryRule rule) =>
        rule.Op is CheckOperator.Exists or CheckOperator.NotExists ? null : CanonicalValue.Render(rule.Expected);

    /// <summary>Vorhandener oder nicht verfügbarer Wert.</summary>
    private Outcome CompareRegistry(RegistryRule rule, RegValueEvidence evidence, ValueOrigin? origin)
    {
        var expected = ExpectedText(rule);
        if (evidence.Class == EvidenceClass.Unavailable) return Unavailable(evidence.Reason, evidence.ItemKey, null, expected);
        var actual = evidence.Raw is { } raw ? CanonicalValue.RenderRegistry(raw) : null;
        var (source, detail) = SourceOf(evidence.ItemKey, evidence.ValueName, origin);
        var o = new Outcome(ResultStatus.Pass) { Expected = expected, Actual = actual, Source = source, SourceDetail = detail };
        if (rule.Op == CheckOperator.Exists) return o;
        if (rule.Op == CheckOperator.NotExists) return o with { Status = ResultStatus.Fail };
        if (evidence.Redacted) return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.RedactedValue };
        var (typed, wrongType) = ConvertRegistry(evidence.Type, evidence.V, rule.ValueType, rule.AcceptTypes);
        if (wrongType || typed is null)
            return o with { Status = ResultStatus.Fail, Details = o.Details.Add(T["Detail_WrongType", evidence.Type ?? "?", TypeName(rule.ValueType)]) };
        var verdict = ValueComparison.Compare(rule.Op, typed, rule.Expected, rule.MultiSz == MultiSzMode.List, CollectedAtUtc, Clock.Suspect);
        return ApplyVerdict(o, verdict);
    }

    /// <summary>Nachweislich nicht gesetzt: exists/notExists, sonst whenMissing (spec 2.4 Punkt 2).</summary>
    private Outcome Missing(RegistryRule rule, RegValueEvidence evidence)
    {
        var expected = ExpectedText(rule);
        var o = new Outcome(ResultStatus.Fail) { Expected = expected, Actual = null, Source = ValueSource.NotSet };
        if (rule.Op == CheckOperator.Exists) return o.With(T["Detail_NotSet"]);
        if (rule.Op == CheckOperator.NotExists) return o with { Status = ResultStatus.Pass };
        switch (rule.WhenMissing)
        {
            case WhenMissing.Fail:
                return o.With(T["Detail_NotSet"]);
            case WhenMissing.OsDefault:
            {
                var match = MatchingDefault(rule.OsDefaults);
                if (match is null)
                    return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.DefaultUnknown, Details = o.Details.Add(T["Detail_DefaultUnknown"]) };
                var typed = DefaultValue(match.Value, rule.ValueType);
                var detail = T["Detail_DefaultAssumed", CanonicalValue.Render(match.Value) ?? "null", match.Source, Date(match.Seen)];
                if (typed is null)
                    return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.UnexpectedValue, Details = o.Details.Add(detail) };
                var verdict = ValueComparison.Compare(rule.Op, typed, rule.Expected, rule.MultiSz == MultiSzMode.List, CollectedAtUtc, Clock.Suspect);
                var result = ApplyVerdict(o with { Source = ValueSource.OsDefault }, verdict).With(detail);
                // Ein „erfüllt“ aus einem Standardwert trägt immer das Flag (spec 2.4: „Standardwert angenommen“)
                return result.Status == ResultStatus.Pass ? result.WithFlags(ResultFlags.FromDefault) : result;
            }
            case WhenMissing.Pass:
                if (rule.MissingPass is not { } proof)
                    return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.MissingRuleIncomplete };
                return (o with { Status = ResultStatus.Pass, Source = ValueSource.OsDefault })
                    .WithFlags(ResultFlags.FromDefault)
                    .With(T["Detail_MissingPass", proof.Note, proof.Source, Date(proof.Seen)]);
            default:
                return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.NotSetNoDefault, Details = o.Details.Add(T["Detail_NotSet"]) };
        }
    }

    /// <summary>
    /// Erster belegter Standardwert, dessen Angaben alle zum Rechner passen. Ist eine Angabe nicht prüfbar (Kopf-Feld
    /// fehlt), bricht die Suche ab: ein späterer Eintrag darf einen früheren nicht verdrängen.
    /// </summary>
    private OsDefault? MatchingDefault(IReadOnlyList<OsDefault>? defaults)
    {
        foreach (var d in defaults ?? [])
        {
            bool? match = true;
            if (d.MinBuild is not null || d.MaxBuild is not null)
            {
                if (Facts.Build is not { } b) match = null;
                else if ((d.MinBuild is { } min && b < min) || (d.MaxBuild is { } max && b > max)) match = false;
            }
            if (match != false && d.Editions is { Count: > 0 } editions)
            {
                if (Facts.Edition is not { } e) match = null;
                else if (!editions.Contains(e, StringComparer.OrdinalIgnoreCase)) match = false;
            }
            if (match != false && d.InstallationTypes is { Count: > 0 } types)
            {
                if (Facts.InstallationType is not { } i) match = null;
                else if (!types.Contains(i, StringComparer.OrdinalIgnoreCase)) match = false;
            }
            if (match != false && d.DomainJoined is { } joined)
            {
                if (Facts.DomainJoined is not { } j) match = null;
                else if (j != joined) match = false;
            }
            if (match is null) return null;
            if (match == true) return d;
        }
        return null;
    }

    /// <summary>pass/fail/nicht entscheidbar aus einem Vergleich.</summary>
    private Outcome ApplyVerdict(Outcome o, Verdict verdict) => verdict switch
    {
        Verdict.Pass => o with { Status = ResultStatus.Pass },
        Verdict.Fail => o with { Status = ResultStatus.Fail },
        Verdict.Unknown when Clock.Suspect => (o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.ClockSuspect })
            .WithFlags(ResultFlags.ClockSuspect).With(T["Detail_ClockSuspect"]),
        _ => o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.UnexpectedValue },
    };

    /// <summary>„nicht prüfbar“ mit dem Grund je Eintragsstatus und einer Zeile, welcher Beleg fehlt.</summary>
    private Outcome Unavailable(string? reason, string key, string? actual, string? expected)
    {
        var code = reason ?? RglCodesEngine.ItemNotCollected;
        return new Outcome(ResultStatus.NotCheckable, code) { Expected = expected, Actual = actual }
            .With(T["Detail_Evidence", Store.Canonical(key), RglCodesEngine.Message(T.Culture, code)]);
    }

    /// <summary>Herkunft eines Registry-Werts aus der Zuordnung (spec 2.10); MDM-Quellen ohne Zuordnung heißen mdm.</summary>
    private (ValueSource Source, string? Detail) SourceOf(string itemKey, string valueName, ValueOrigin? origin)
    {
        if (Attribution.Find(itemKey, valueName) is { } a) return (a.Source, a.Detail);
        return origin switch
        {
            ValueOrigin.Mdm => (ValueSource.Mdm, null),
            { } o => (ValueSource.Unknown, o.ToString().ToLowerInvariant()),
            null => (ValueSource.Unknown, null),
        };
    }

    // ---------------------------------------------------------------- strenge Typen (spec 2.4 Punkt 3)

    private static RegValueType? ParseType(string? t) => t switch
    {
        "dword" => RegValueType.Dword,
        "qword" => RegValueType.Qword,
        "sz" => RegValueType.Sz,
        "expand_sz" => RegValueType.ExpandSz,
        "multi_sz" => RegValueType.MultiSz,
        "binary" => RegValueType.Binary,
        _ => null,
    };

    internal static string TypeName(RegValueType type) => type switch
    {
        RegValueType.Dword => "dword",
        RegValueType.Qword => "qword",
        RegValueType.Sz => "sz",
        RegValueType.ExpandSz => "expand_sz",
        RegValueType.MultiSz => "multi_sz",
        _ => "binary",
    };

    /// <summary>
    /// Registry-Wert in den Vergleichstyp der Regel. Ein Typ, der weder valueType noch in acceptTypes ist, oder ein
    /// unlesbarer Text bei acceptTypes sz → „falscher Werttyp“ (Windows ignoriert falsch typisierte Richtlinienwerte).
    /// </summary>
    internal static (Typed? Value, bool WrongType) ConvertRegistry(string? typeText, JsonElement? v, RegValueType target, IReadOnlyList<RegValueType>? accept)
    {
        if (ParseType(typeText) is not { } actual) return (null, true);
        if (actual != target && !(accept?.Contains(actual) ?? false)) return (null, true);
        switch (target)
        {
            case RegValueType.Dword:
            case RegValueType.Qword:
            {
                decimal? n = actual switch
                {
                    RegValueType.Dword or RegValueType.Qword => v is { } e ? RegistryInteger(e) : null,
                    RegValueType.Sz or RegValueType.ExpandSz => v is { ValueKind: JsonValueKind.String } s && ulong.TryParse(s.GetString()!.Trim(),
                        NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
                    _ => null,
                };
                if (n is null) return (null, true);
                if (target == RegValueType.Dword && n > uint.MaxValue) return (null, true);
                return (new Typed.Num(n.Value), false);
            }
            case RegValueType.Sz:
            case RegValueType.ExpandSz:
                return actual switch
                {
                    RegValueType.Sz or RegValueType.ExpandSz when v is { ValueKind: JsonValueKind.String } s => (new Typed.Text(s.GetString()!), false),
                    RegValueType.Dword or RegValueType.Qword when v is { } e && RegistryInteger(e) is { } n =>
                        (new Typed.Text(n.ToString(CultureInfo.InvariantCulture)), false),
                    _ => (null, true),
                };
            case RegValueType.MultiSz:
                if (actual == RegValueType.MultiSz && v is { ValueKind: JsonValueKind.Array } a && a.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String))
                    return (new Typed.List([.. a.EnumerateArray().Select(x => (Typed)new Typed.Text(x.GetString()!))]), false);
                if (actual is RegValueType.Sz or RegValueType.ExpandSz && v is { ValueKind: JsonValueKind.String } single)
                    return (new Typed.List([new Typed.Text(single.GetString()!)]), false);
                return (null, true);
            default:
                if (actual == RegValueType.Binary && v is { ValueKind: JsonValueKind.String } b)
                {
                    try { return (new Typed.Bytes(Convert.FromBase64String(b.GetString()!)), false); }
                    catch (FormatException) { return (null, true); }
                }
                return (null, true);
        }
    }

    /// <summary>DWORD als Zahl, QWORD als Dezimaltext (spec 7.3), ohne Vorzeichen.</summary>
    private static decimal? RegistryInteger(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number when e.TryGetDecimal(out var d) && d >= 0 && decimal.Truncate(d) == d => d,
        JsonValueKind.String when ulong.TryParse(e.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var u) => u,
        _ => null,
    };

    /// <summary>Wert eines osDefaults im Vergleichstyp der Regel.</summary>
    private static Typed? DefaultValue(JsonElement value, RegValueType target) => target switch
    {
        RegValueType.Dword or RegValueType.Qword => ValueComparison.ExpectedNumber(value) is { } n ? new Typed.Num(n) : null,
        RegValueType.Sz or RegValueType.ExpandSz => value.ValueKind == JsonValueKind.String ? new Typed.Text(value.GetString()!) : null,
        RegValueType.MultiSz => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String)
            ? new Typed.List([.. value.EnumerateArray().Select(x => (Typed)new Typed.Text(x.GetString()!))]) : null,
        _ => value.ValueKind == JsonValueKind.String && ValueComparison.ParseHex(value.GetString()!) is { } bytes ? new Typed.Bytes(bytes) : null,
    };

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- bekannte Falle LOGON-10 (spec 2.4 Punkt 7)

    /// <summary>
    /// Automatische Anmeldung: AutoAdminLogon = 1 ist ein Befund, ob DefaultPassword in der Registry steht oder nicht –
    /// das Kennwort kann auch in einem LSA-Geheimnis liegen. Die Engine ergänzt nur den Hinweis zur Ablage.
    /// </summary>
    private Outcome AutoLogonTrap(RegistryCheck c, Outcome outcome)
    {
        if (outcome.Status != ResultStatus.Fail || !string.Equals(c.Value, "AutoAdminLogon", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(EvidenceStore.NormalizePath(c.Key), Known.Winlogon, StringComparison.OrdinalIgnoreCase))
            return outcome;
        var password = Store.RegistryValue(Known.Winlogon, "DefaultPassword");
        return outcome.With(T[password.IsPresent ? "Detail_AutoLogonPasswordInRegistry" : "Detail_AutoLogonPasswordElsewhere"]);
    }
}
