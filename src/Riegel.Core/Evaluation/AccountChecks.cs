// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Core.Evaluation;

/// <summary>Prüfarten localAccount, localAccounts und localGroup (spec 2.3). Konten zählen nur über SID/RID, nie über Namen.</summary>
internal sealed partial class EvaluationSession
{
    private static long? RidOf(string? sid)
    {
        if (sid is null || !sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase)) return null;
        int cut = sid.LastIndexOf('-');
        return long.TryParse(sid[(cut + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var rid) ? rid : null;
    }

    /// <summary>
    /// Ein lokales Konto nach RID: Win32_UserAccount (LocalAccount=TRUE), sonst api:netuserenum. Gibt es das Konto in
    /// keiner lesbaren Quelle, ist die Prüfung nicht prüfbar (z. B. Domänencontroller ohne lokale Konten).
    /// </summary>
    private Outcome LocalAccount(LocalAccountCheck c)
    {
        var o = new Outcome(ResultStatus.Fail) { Expected = "disabled", Source = ValueSource.Runtime };
        ItemEvidence? firstMissing = null;
        bool anyPresent = false;
        var wmi = Store.Item(Known.UserAccount);
        if (wmi.IsPresent && JsonRead.Rows(wmi.Value) is { } rows)
        {
            anyPresent = true;
            var row = rows.FirstOrDefault(r => RidOf(JsonRead.String(r, "SID")) == c.Rid);
            if (row.ValueKind == JsonValueKind.Object && JsonRead.Bool(row, "Disabled") is { } disabled)
                return (o with { Status = disabled ? ResultStatus.Pass : ResultStatus.Fail, Actual = disabled ? "disabled" : "enabled" }).DecidedBy(Known.UserAccount);
        }
        else if (!wmi.IsPresent) firstMissing = wmi;
        var api = Store.Item(ItemKeys.NetUserEnum);
        if (api.IsPresent && api.Value.ValueKind == JsonValueKind.Array)
        {
            anyPresent = true;
            var account = api.Value.EnumerateArray().FirstOrDefault(a => a.ValueKind == JsonValueKind.Object
                && (JsonRead.Integer(a, "rid") == c.Rid || RidOf(JsonRead.String(a, "sid")) == c.Rid));
            if (account.ValueKind == JsonValueKind.Object && JsonRead.Bool(account, "disabled") is { } disabled)
                return (o with { Status = disabled ? ResultStatus.Pass : ResultStatus.Fail, Actual = disabled ? "disabled" : "enabled" }).DecidedBy(ItemKeys.NetUserEnum);
        }
        else if (!api.IsPresent) firstMissing ??= api;
        if (!anyPresent && firstMissing is not null) return Unavailable(firstMissing.Reason, firstMissing.Key, null, "disabled");
        return (o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.NoDecisiveEvidence }).With(T["Detail_AccountNotFound", c.Rid]);
    }

    /// <summary>
    /// Kein (aktiviertes) lokales Konto erfüllt die Bedingung; Treffer werden aufgelistet. unusedDays: letzte Anmeldung
    /// länger her als die Anlageneinstellung unusedAccountDays (gemessen an CollectedAtUtc) oder nie.
    /// </summary>
    private Outcome LocalAccounts(LocalAccountsCheck c)
    {
        var o = new Outcome(ResultStatus.Pass) { Expected = "[]", Source = ValueSource.Runtime };
        var api = Store.Item(ItemKeys.NetUserEnum);
        if (!api.IsPresent) return Unavailable(api.Reason, api.Key, null, "[]");
        if (api.Value.ValueKind != JsonValueKind.Array) return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.UnexpectedValue };
        if (c.Condition == LocalAccountCondition.UnusedDays && Clock.Suspect)
            return (o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.ClockSuspect }).WithFlags(ResultFlags.ClockSuspect).With(T["Detail_ClockSuspect"]);
        var hits = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        bool unknown = false;
        int days = Context.UnusedAccountDays;
        foreach (var account in api.Value.EnumerateArray())
        {
            if (account.ValueKind != JsonValueKind.Object) { unknown = true; continue; }
            var rid = JsonRead.Integer(account, "rid") ?? RidOf(JsonRead.String(account, "sid"));
            if (rid is { } r && c.ExcludeRids is { } excluded && excluded.Contains((int)r)) continue;
            var disabled = JsonRead.Bool(account, "disabled");
            if (c.EnabledOnly && disabled == true) continue;
            if (c.EnabledOnly && disabled is null) { unknown = true; continue; }
            bool? match = c.Condition switch
            {
                LocalAccountCondition.PasswordNeverExpires => JsonRead.Bool(account, "passwordNeverExpires"),
                LocalAccountCondition.PasswordNotRequired => JsonRead.Bool(account, "passwordNotRequired"),
                _ => Unused(account, days),
            };
            if (match is null) { unknown = true; continue; }
            if (match == true) hits.Add($"{JsonRead.String(account, "name") ?? "?"} ({JsonRead.String(account, "sid") ?? rid?.ToString(CultureInfo.InvariantCulture) ?? "?"})");
        }
        o = o with { Actual = CanonicalValue.Render(JsonValues.Texts(hits)) };
        if (hits.Count > 0) return hits.Aggregate(o with { Status = ResultStatus.Fail }, (x, h) => x.With(h));
        if (unknown) return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.UnexpectedValue };
        return o;
    }

    private bool? Unused(JsonElement account, int days)
    {
        if (JsonRead.TryGet(account, "lastLogonDate", out var last))
        {
            // Nie angemeldet: keine Anmeldung innerhalb der Frist, also unbenutzt (wie die DISA-Prüfung „Never“ wertet;
            // ein neues, noch ungenutztes Konto erscheint so lange in der Liste, bis es benutzt wird)
            if (last.ValueKind == JsonValueKind.Null) return true;
            return JsonRead.Time(last) is { } t && ValueComparison.AgeDays(t, CollectedAtUtc) is { } a ? a > days : null;
        }
        return null;
    }

    /// <summary>
    /// Mitglieder einer lokalen Gruppe (api:localgroup:&lt;SID&gt;) gegen maxMembers und allowedSids (bekannte SIDs oder
    /// „*-RID“). Ohne Grenzen nur Bestand (pass, als info in Bestandsregeln).
    /// </summary>
    private Outcome LocalGroup(LocalGroupCheck c)
    {
        var expected = c.AllowedSids is { } allowed ? CanonicalValue.Render(JsonValues.Texts(allowed)) : c.MaxMembers?.ToString(CultureInfo.InvariantCulture);
        var item = Store.Item(ItemKeys.LocalGroup(c.Sid));
        if (!item.IsPresent) return Unavailable(item.Reason, item.Key, null, expected);
        if (item.Value.ValueKind != JsonValueKind.Array) return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.UnexpectedValue) { Expected = expected };
        var members = item.Value.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.Object).ToList();
        var lines = members.Select(m => T["Detail_Member", JsonRead.String(m, "name") ?? "?", JsonRead.String(m, "sid") ?? "?", JsonRead.String(m, "kind") ?? "?"]).ToList();
        var sids = members.Select(m => JsonRead.String(m, "sid")).ToList();
        var o = new Outcome(ResultStatus.Pass)
        {
            Expected = expected,
            Actual = CanonicalValue.Render(JsonValues.Texts(sids.Select(s => s ?? "?"))),
            Source = ValueSource.Runtime,
        };
        o = lines.Aggregate(o, (x, l) => x.With(l));
        if (c.MaxMembers is { } max && members.Count > max) return (o with { Status = ResultStatus.Fail }).With(T["Detail_TooManyMembers", members.Count, max]);
        o = o.DecidedBy(item.Key);
        if (c.AllowedSids is { } allowedSids)
        {
            var (prefix, prefixKey) = allowedSids.Any(p => p.Trim().StartsWith("*-", StringComparison.Ordinal)) ? MachineSid() : (null, null);
            o = o.DecidedBy(prefixKey);
            bool uncertain = false;
            foreach (var sid in sids)
            {
                if (sid is null) { uncertain = true; continue; }
                bool? ok = false;
                foreach (var pattern in allowedSids)
                {
                    var p = pattern.Trim();
                    if (p.StartsWith("*-", StringComparison.Ordinal))
                    {
                        if (!sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) || !sid.EndsWith(p[1..], StringComparison.OrdinalIgnoreCase)) continue;
                        if (prefix is null) ok = null;
                        else if (string.Equals(sid, prefix + p[1..], StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
                    }
                    else if (string.Equals(sid, p, StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
                }
                if (ok == false) return (o with { Status = ResultStatus.Fail }).With(T["Detail_MemberNotAllowed", sid]);
                if (ok is null) uncertain = true;
            }
            if (uncertain) return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.NoDecisiveEvidence };
        }
        return o;
    }
}
