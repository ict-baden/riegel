// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Core.Evaluation;

/// <summary>Prüfarten secedit, accountPolicy, auditSubcategory und userRight (spec 2.3).</summary>
internal sealed partial class EvaluationSession
{
    // ---------------------------------------------------------------- secedit

    /// <summary>Ein Wert eines Abschnitts von parsed:secedit, null wenn der Schlüssel (oder Abschnitt) fehlt.</summary>
    private (ItemEvidence Item, JsonElement? Value) SeceditValue(string section, string key)
    {
        var item = Store.Item(ItemKeys.ParsedSecedit);
        if (!item.IsPresent) return (item, null);
        if (JsonRead.Get(item.Value, "sections") is not { ValueKind: JsonValueKind.Object } sections)
            return (item with { Class = EvidenceClass.Unavailable, Reason = RglCodesEngine.UnexpectedValue }, null);
        if (!JsonRead.TryGet(sections, section, out var s) || s.ValueKind != JsonValueKind.Object) return (item, null);
        return JsonRead.TryGet(s, key, out var v) && v.ValueKind != JsonValueKind.Null ? (item, v) : (item, null);
    }

    private Outcome Secedit(SeceditCheck c)
    {
        var expected = c.Op is CheckOperator.Exists or CheckOperator.NotExists ? null : CanonicalValue.Render(c.Expected);
        var (item, value) = SeceditValue(c.Section, c.Key);
        if (!item.IsPresent) return Unavailable(item.Reason, item.Key, null, expected);
        var o = new Outcome(ResultStatus.Fail) { Expected = expected };
        if (value is not { } v)
        {
            // Nicht im Sicherheitstemplate: nicht festgelegt (spec 2.4 Punkt 2; secedit kennt nur fail und notCheckable)
            o = o with { Source = ValueSource.NotSet };
            return c.Op switch
            {
                CheckOperator.Exists => o.With(T["Detail_NotSet"]),
                CheckOperator.NotExists => o with { Status = ResultStatus.Pass },
                _ => c.WhenMissing switch
                {
                    WhenMissing.Fail => o.With(T["Detail_NotSet"]),
                    WhenMissing.NotCheckable => o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.NotSetNoDefault },
                    _ => o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.MissingRuleIncomplete },
                },
            };
        }
        if (c.Op == CheckOperator.Exists) return o with { Status = ResultStatus.Pass, Actual = SeceditText(v) };
        if (c.Op == CheckOperator.NotExists) return o with { Actual = SeceditText(v) };
        Typed? typed;
        switch (c.ValueKind)
        {
            case SeceditValueKind.Number:
                typed = SeceditText(v) is { } n && long.TryParse(n.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                    ? new Typed.Num(number) : null;
                if (typed is null) return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.UnexpectedValue, Actual = SeceditText(v) };
                break;
            case SeceditValueKind.String:
                typed = SeceditText(v) is { } s ? new Typed.Text(Unquote(s)) : null;
                if (typed is null) return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.UnexpectedValue };
                break;
            default:
            {
                var (t, wrong, raw) = SeceditRegistryValue(v, c.Expected);
                o = o with { Actual = raw };
                if (wrong) return o.With(T["Detail_WrongTypeSecedit"]);
                if (t is null) return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.UnexpectedValue };
                typed = t;
                break;
            }
        }
        o = o with { Actual = ValueComparison.Render(typed) };
        return ApplyVerdict(o, ValueComparison.Compare(c.Op, typed, c.Expected));
    }

    private static string? SeceditText(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        _ => null,
    };

    private static string Unquote(string s)
    {
        var t = s.Trim();
        return t.Length >= 2 && t[0] == '"' && t[^1] == '"' ? t[1..^1] : t;
    }

    /// <summary>
    /// [Registry Values] = „typ,wert“ (spec 2.3; parsed: <c>{ type, value }</c>): 4 = DWORD, 1 = SZ, 2 = EXPAND_SZ,
    /// 7 = MULTI_SZ (durch Kommas getrennt), 3 = BINARY (research-technik §3.1). Der Sollwert bestimmt den erwarteten
    /// Typ; ein anderer Typ ist „falscher Werttyp“.
    /// </summary>
    private static (Typed? Value, bool WrongType, string? Raw) SeceditRegistryValue(JsonElement v, JsonElement? expected)
    {
        int? type = null;
        string? raw = null;
        if (v.ValueKind == JsonValueKind.Object)
        {
            type = JsonRead.Integer(v, "type") is { } t && t is >= 0 and <= 11 ? (int)t : null;
            raw = JsonRead.String(v, "value") ?? (JsonRead.Get(v, "value") is { ValueKind: JsonValueKind.Number } num ? num.GetRawText() : null);
        }
        else if (v.ValueKind == JsonValueKind.String)
        {
            var text = v.GetString()!;
            int comma = text.IndexOf(',');
            if (comma > 0 && int.TryParse(text[..comma], NumberStyles.None, CultureInfo.InvariantCulture, out var t)) { type = t; raw = text[(comma + 1)..]; }
        }
        if (type is null || raw is null) return (null, false, raw);
        bool wantsNumber = expected is { } e && (e.ValueKind == JsonValueKind.Number
                                                 || (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0 && e[0].ValueKind == JsonValueKind.Number));
        bool wantsList = expected is { ValueKind: JsonValueKind.Array } a && (a.GetArrayLength() == 0 || a[0].ValueKind == JsonValueKind.String);
        switch (type)
        {
            case 4:
                if (!wantsNumber) return (null, true, raw);
                return ulong.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? (new Typed.Num(n), false, raw) : (null, true, raw);
            case 1:
            case 2:
                if (wantsNumber) return (null, true, raw);
                return (new Typed.Text(Unquote(raw)), false, raw);
            case 7:
                if (!wantsList) return (null, true, raw);
                return (new Typed.List([.. raw.Split(',').Select(p => (Typed)new Typed.Text(Unquote(p)))]), false, raw);
            default:
                return (null, true, raw);
        }
    }

    // ---------------------------------------------------------------- accountPolicy

    /// <summary>Schlüssel in [System Access] je Feld (feste Zuordnung, docs/file-formats.md).</summary>
    private static string SeceditKey(AccountPolicyField f) => f switch
    {
        AccountPolicyField.MinPwLen => "MinimumPasswordLength",
        AccountPolicyField.MaxPwAgeDays => "MaximumPasswordAge",
        AccountPolicyField.MinPwAgeDays => "MinimumPasswordAge",
        AccountPolicyField.PwHistLen => "PasswordHistorySize",
        AccountPolicyField.LockoutThreshold => "LockoutBadCount",
        AccountPolicyField.LockoutDurationMin => "LockoutDuration",
        AccountPolicyField.LockoutWindowMin => "ResetLockoutCount",
        AccountPolicyField.PasswordComplexity => "PasswordComplexity",
        _ => "ClearTextPassword",
    };

    private static string ApiField(AccountPolicyField f) => f switch
    {
        AccountPolicyField.MinPwLen => "minPwLen",
        AccountPolicyField.MaxPwAgeDays => "maxPwAgeDays",
        AccountPolicyField.MinPwAgeDays => "minPwAgeDays",
        AccountPolicyField.PwHistLen => "pwHistLen",
        AccountPolicyField.LockoutThreshold => "lockoutThreshold",
        AccountPolicyField.LockoutDurationMin => "lockoutDurationMin",
        _ => "lockoutWindowMin",
    };

    /// <summary>
    /// Kontorichtlinie: api:netusermodals, dann parsed:secedit [System Access]. Sonderwerte: maxPwAgeDays null (API)
    /// bzw. ≤ 0 (secedit) = läuft nie ab (unbegrenzt); lockoutDurationMin null/≤ 0 = bis zur Entsperrung (unbegrenzt);
    /// lockoutThreshold 0 = sperrt nie – das erfüllt keine Schranke (nur eq/in mit 0).
    /// </summary>
    private Outcome AccountPolicy(AccountPolicyField field, CheckOperator op, JsonElement? expectedValue)
    {
        var expected = op is CheckOperator.Exists or CheckOperator.NotExists ? null : CanonicalValue.Render(expectedValue);
        bool seceditOnly = field is AccountPolicyField.PasswordComplexity or AccountPolicyField.ClearTextPassword;
        decimal? value = null;
        bool unlimited = false, found = false;
        var source = ValueSource.Unknown;
        string decidingKey = ItemKeys.NetUserModals;
        string? reason = null, reasonKey = null;
        if (!seceditOnly)
        {
            var api = Store.Item(ItemKeys.NetUserModals);
            if (api.IsPresent && JsonRead.TryGet(api.Value, ApiField(field), out var v))
            {
                if (v.ValueKind == JsonValueKind.Null && field is AccountPolicyField.MaxPwAgeDays or AccountPolicyField.LockoutDurationMin)
                { unlimited = true; found = true; }
                else if (JsonRead.Number(v) is { } n) { value = n; found = true; }
                if (found) source = ValueSource.Runtime;
            }
            else if (!api.IsPresent) { reason = api.Reason; reasonKey = api.Key; }
        }
        if (!found)
        {
            var (item, raw) = SeceditValue("System Access", SeceditKey(field));
            if (item.IsPresent && raw is { } r && SeceditText(r) is { } text
                && long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
            {
                value = n;
                found = true;
                decidingKey = ItemKeys.ParsedSecedit;
            }
            else if (item.IsPresent && raw is null)
                return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.NotSetNoDefault) { Expected = expected, Source = ValueSource.NotSet };
            else if (!item.IsPresent) { reason ??= item.Reason; reasonKey ??= item.Key; }
            else return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.UnexpectedValue) { Expected = expected };
        }
        if (!found) return Unavailable(reason, reasonKey ?? ItemKeys.NetUserModals, null, expected);

        if (field is AccountPolicyField.MaxPwAgeDays or AccountPolicyField.LockoutDurationMin && value is { } v0 && v0 <= 0)
        {
            unlimited = true;
            value = null;
        }
        var o = new Outcome(ResultStatus.Fail) { Expected = expected, Source = source }.DecidedBy(decidingKey);
        if (op == CheckOperator.Exists) return o with { Status = ResultStatus.Pass, Actual = unlimited ? "unlimited" : Num(value) };
        if (op == CheckOperator.NotExists) return o with { Actual = unlimited ? "unlimited" : Num(value) };
        if (unlimited)
        {
            o = o with { Actual = "unlimited" };
            var verdict = op switch
            {
                CheckOperator.Gt or CheckOperator.Ge or CheckOperator.Ne or CheckOperator.NotIn => Verdict.Pass,
                CheckOperator.Lt or CheckOperator.Le or CheckOperator.Eq or CheckOperator.In => Verdict.Fail,
                _ => Verdict.TypeMismatch,
            };
            return ApplyVerdict(o, verdict).With(T["Detail_Unlimited"]);
        }
        o = o with { Actual = Num(value) };
        if (field == AccountPolicyField.LockoutThreshold && value == 0)
        {
            // 0 = sperrt nie: nur eq/in mit 0 bzw. ne/notIn ohne 0 sind wahr; keine Schranke wird erfüllt
            var verdict = op switch
            {
                CheckOperator.Eq or CheckOperator.Ne or CheckOperator.In or CheckOperator.NotIn => ValueComparison.Compare(op, new Typed.Num(0), expectedValue),
                _ => Verdict.Fail,
            };
            return ApplyVerdict(o, verdict).With(T["Detail_NeverLocks"]);
        }
        var expectedNumber = expectedValue switch
        {
            { ValueKind: JsonValueKind.True } => JsonValues.Number(1),
            { ValueKind: JsonValueKind.False } => JsonValues.Number(0),
            _ => expectedValue,
        };
        return ApplyVerdict(o, ValueComparison.Compare(op, new Typed.Num(value!.Value), expectedNumber));
    }

    private Outcome AccountPolicy(AccountPolicyCheck c) => AccountPolicy(c.Field, c.Op, c.Expected);

    private static string? Num(decimal? v) => v?.ToString(CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- auditSubcategory

    private static string AuditName(long v) => v switch
    {
        0 => "noAuditing",
        1 => "success",
        2 => "failure",
        3 => "successAndFailure",
        _ => v.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Unterkategorie nach GUID mit den Zahlen 0–3 (1 Erfolg, 2 Fehler); fehlt die GUID, ist sie nicht erfasst.</summary>
    private Outcome AuditSubcategory(AuditSubcategoryCheck c)
    {
        var expected = JsonNamingCamel(c.Expected.ToString());
        var item = Store.Item(ItemKeys.AuditPolicy);
        if (!item.IsPresent) return Unavailable(item.Reason, item.Key, null, expected);
        var guid = c.Guid.Trim();
        if (!guid.StartsWith('{')) guid = "{" + guid + "}";
        if (!JsonRead.TryGet(item.Value, guid, out var raw))
            return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.ItemNotCollected) { Expected = expected }.With(T["Detail_AuditGuidMissing", guid]);
        if (JsonRead.Integer(raw) is not { } v || v is < 0 or > 3)
            return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.UnexpectedValue) { Expected = expected };
        bool ok = c.Expected switch
        {
            AuditExpectation.Success => v == 1,
            AuditExpectation.Failure => v == 2,
            AuditExpectation.SuccessAndFailure => v == 3,
            AuditExpectation.IncludesSuccess => (v & 1) != 0,
            AuditExpectation.IncludesFailure => (v & 2) != 0,
            _ => v == 0,
        };
        return new Outcome(ok ? ResultStatus.Pass : ResultStatus.Fail) { Expected = expected, Actual = AuditName(v), Source = ValueSource.Runtime };
    }

    private static string JsonNamingCamel(string pascal) => pascal.Length == 0 ? pascal : char.ToLowerInvariant(pascal[0]) + pascal[1..];

    // ---------------------------------------------------------------- userRight

    /// <summary>SID-Präfix der lokalen Konten (S-1-5-21-a-b-c), aus api:netuserenum oder Win32_UserAccount, mit dem Beleg.</summary>
    private (string? Prefix, string? Key) MachineSid()
    {
        foreach (var key in new[] { ItemKeys.NetUserEnum, Known.UserAccount })
        {
            var item = Store.Item(key);
            if (!item.IsPresent) continue;
            var rows = key == ItemKeys.NetUserEnum ? (item.Value.ValueKind == JsonValueKind.Array ? item.Value.EnumerateArray().ToList() : null) : JsonRead.Rows(item.Value)?.ToList();
            foreach (var row in rows ?? [])
            {
                var sid = JsonRead.String(row, "sid") ?? JsonRead.String(row, "SID");
                if (sid is null || !sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase)) continue;
                int cut = sid.LastIndexOf('-');
                if (cut > 9) return (sid[..cut].ToUpperInvariant(), key);
            }
        }
        return (null, null);
    }

    /// <summary>
    /// Benutzerrecht: api:lsa-rights (SIDs), dann parsed:secedit [Privilege Rights] („*SID“ oder Namen). Erwartete
    /// SIDs sind bekannte SIDs oder „*-RID“ für lokale Konten. Ein nicht aufgelöster Name (ohne „*“) macht
    /// mustNotInclude/onlyFrom/empty „nicht prüfbar“, außer ein Verstoß steht schon über SIDs fest; mustInclude kann
    /// über SIDs allein bestehen. Ein Recht, das im Template fehlt, hat niemand.
    /// </summary>
    private Outcome UserRight(UserRightCheck c)
    {
        var expected = CanonicalValue.Render(JsonValues.Texts(c.Sids));
        List<string>? entries = null;
        var source = ValueSource.Unknown;
        string sourceKey = ItemKeys.LsaRights;
        var api = Store.Item(ItemKeys.LsaRights);
        if (api.IsPresent && JsonRead.TryGet(api.Value, c.Right, out var list) && list.ValueKind == JsonValueKind.Array)
        {
            entries = [.. list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)];
            source = ValueSource.Runtime;
        }
        else
        {
            var (item, value) = SeceditValue("Privilege Rights", c.Right);
            sourceKey = ItemKeys.ParsedSecedit;
            if (item.IsPresent)
            {
                entries = value switch
                {
                    null => [],
                    { ValueKind: JsonValueKind.Array } a => [.. a.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)],
                    { ValueKind: JsonValueKind.String } s => [.. s.GetString()!.Split(',')],
                    _ => null,
                };
                if (entries is null) return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.UnexpectedValue) { Expected = expected };
            }
            else return Unavailable(api.IsPresent ? item.Reason : api.Reason, api.IsPresent ? item.Key : api.Key, null, expected);
        }

        var sids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in entries)
        {
            var e = raw.Trim();
            if (e.Length == 0) continue;
            var sid = e.StartsWith('*') ? e[1..] : e;
            if (sid.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)) sids.Add(sid.ToUpperInvariant());
            else names.Add(e);
        }
        var actual = CanonicalValue.Render(JsonValues.Texts([.. sids, .. names]));
        bool usesPattern = c.Sids.Any(p => p.Trim().StartsWith("*-", StringComparison.Ordinal));
        var (prefix, prefixKey) = usesPattern ? MachineSid() : (null, null);
        var o = new Outcome(ResultStatus.Fail) { Expected = expected, Actual = actual, Source = source }.DecidedBy(sourceKey, prefixKey);

        // Treffer eines erwarteten Eintrags: true, false oder null (Rechner-SID unbekannt)
        bool? Holds(string pattern)
        {
            var p = pattern.Trim();
            if (p.StartsWith("*-", StringComparison.Ordinal))
            {
                var rid = p[1..];
                if (prefix is not null) return sids.Contains(prefix + rid);
                return sids.Any(s => s.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) && s.EndsWith(rid, StringComparison.OrdinalIgnoreCase)) ? null : false;
            }
            return sids.Contains(p.ToUpperInvariant());
        }
        bool? Allowed(string sid)
        {
            bool? best = false;
            foreach (var pattern in c.Sids)
            {
                var p = pattern.Trim();
                if (p.StartsWith("*-", StringComparison.Ordinal))
                {
                    if (!sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) || !sid.EndsWith(p[1..], StringComparison.OrdinalIgnoreCase)) continue;
                    if (prefix is null) best = null;
                    else if (string.Equals(sid, prefix + p[1..], StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (string.Equals(sid, p, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return best;
        }
        Outcome Unresolved() => (o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.UnresolvedNames })
            .With(T["Detail_UnresolvedNames", string.Join(", ", names)]);
        Outcome Uncertain() => (o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.NoDecisiveEvidence }).With(T["Detail_MachineSidUnknown"]);

        switch (c.Mode)
        {
            case UserRightMode.MustInclude:
            {
                var results = c.Sids.Select(Holds).ToList();
                if (results.All(r => r == true)) return o with { Status = ResultStatus.Pass };
                if (names.Count > 0) return Unresolved();
                if (results.Any(r => r is null)) return Uncertain();
                return o.With(T["Detail_RightMissing", string.Join(", ", c.Sids.Where((_, i) => results[i] == false))]);
            }
            case UserRightMode.MustNotInclude:
            {
                var results = c.Sids.Select(Holds).ToList();
                if (results.Any(r => r == true)) return o.With(T["Detail_RightHeldBy", string.Join(", ", c.Sids.Where((_, i) => results[i] == true))]);
                if (names.Count > 0) return Unresolved();
                if (results.Any(r => r is null)) return Uncertain();
                return o with { Status = ResultStatus.Pass };
            }
            case UserRightMode.OnlyFrom:
            {
                var verdicts = sids.Select(s => (Sid: s, Ok: Allowed(s))).ToList();
                if (verdicts.Any(v => v.Ok == false)) return o.With(T["Detail_RightHeldBy", string.Join(", ", verdicts.Where(v => v.Ok == false).Select(v => v.Sid))]);
                if (names.Count > 0) return Unresolved();
                if (verdicts.Any(v => v.Ok is null)) return Uncertain();
                return o with { Status = ResultStatus.Pass };
            }
            default:
                if (sids.Count > 0) return o.With(T["Detail_RightHeldBy", string.Join(", ", sids)]);
                if (names.Count > 0) return Unresolved();
                return o with { Status = ResultStatus.Pass };
        }
    }
}
