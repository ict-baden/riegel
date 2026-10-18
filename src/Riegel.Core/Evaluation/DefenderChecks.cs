// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Facts;
using Riegel.Core.Model;

namespace Riegel.Core.Evaluation;

/// <summary>Prüfarten defenderSetting, asrRule und avStatus mit der Sperre nach defender.mode (spec 2.5).</summary>
internal sealed partial class EvaluationSession
{
    private enum Gate { Evaluate, NotApplicable, Fail, NotCheckable }

    /// <summary>
    /// Sperrtabelle aus spec 2.5. active → auswerten; passive/edrBlock/off mit aktivem Drittanbieter → n/a, ohne → fail;
    /// absent mit Drittanbieter → n/a, ohne → fail (Einstellungen) bzw. bei ASR fail nur, wenn die Regel das Fehlen der
    /// Fähigkeit als Lücke wertet (ifCapabilityAbsent fail, z. B. DEF-09), sonst n/a; unknown → nicht prüfbar.
    /// „off“ mit aktivem Drittanbieter fehlt in der Tabelle und wird wie passive behandelt.
    /// </summary>
    private (Gate Gate, Outcome Outcome) DefenderGate(bool asr)
    {
        var mode = Facts.Text(FactNames.DefenderMode);
        var third = Facts.Bool(FactNames.AvThirdPartyActive);
        var flags = Facts.Derived.Contains(FactNames.DefenderMode) ? ResultFlags.Derived : ResultFlags.None;
        var deciding = Facts.SourcesOf(FactNames.DefenderMode);
        Outcome Make(ResultStatus status, string? reason, bool usesThird)
        {
            var o = new Outcome(status, reason) { Source = ValueSource.Runtime }.WithFlags(flags).WithDeciding(deciding);
            if (usesThird)
            {
                o = o.WithDeciding(Facts.SourcesOf(FactNames.AvThirdPartyActive));
                if (Facts.Heuristic.Contains(FactNames.AvThirdPartyActive)) o = o.WithFlags(ResultFlags.Heuristic);
            }
            return o;
        }
        switch (mode)
        {
            case FactNames.Defender.Active:
                return (Gate.Evaluate, Make(ResultStatus.Pass, null, false));
            case FactNames.Defender.Passive:
            case FactNames.Defender.EdrBlock:
            case FactNames.Defender.Off:
            case FactNames.Defender.Absent:
                if (third == true)
                    return (Gate.NotApplicable, Make(ResultStatus.NotApplicable, RglCodesEngine.NotApplicableThirdPartyAv, true).With(T["Detail_ThirdPartyAv"]));
                if (third is null)
                    return (Gate.NotCheckable, Make(ResultStatus.NotCheckable, RglCodesEngine.DefenderStateUnknown, true).With(T["Detail_ThirdPartyUnknown", mode]));
                if (mode == FactNames.Defender.Absent && asr
                    && !(Rule.AppliesTo.Capability is not null && Rule.AppliesTo.IfCapabilityAbsent == AbsentCapability.Fail))
                    return (Gate.NotApplicable, Make(ResultStatus.NotApplicable, RglCodesEngine.NotApplicableCapability, true).With(T["Detail_DefenderAbsent"]));
                return (Gate.Fail, Make(ResultStatus.Fail, null, true).With(T["Detail_NoActiveAv", mode]));
            default:
                return (Gate.NotCheckable, Make(ResultStatus.NotCheckable, RglCodesEngine.DefenderStateUnknown, false).With(T["Detail_DefenderUnknown"]));
        }
    }

    /// <summary>Eine Defender-Einstellung aus MSFT_MpComputerStatus (status) oder MSFT_MpPreference (preference).</summary>
    private Outcome DefenderSetting(DefenderSettingCheck c)
    {
        var expected = c.Op is CheckOperator.Exists or CheckOperator.NotExists ? null : CanonicalValue.Render(c.Expected);
        var (gate, gated) = DefenderGate(asr: false);
        if (gate != Gate.Evaluate) return gated with { Expected = expected };
        var item = Store.Item(c.Source == DefenderSource.Status ? Known.MpStatus : Known.MpPreference);
        if (!item.IsPresent) return Unavailable(item.Reason, item.Key, null, expected).WithFlags(gated.Flags).WithDeciding(gated.Deciding);
        if (JsonRead.Rows(item.Value) is not { Count: > 0 } rows)
            return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.UnexpectedValue) { Expected = expected }.WithFlags(gated.Flags);
        var row = rows[0];
        // Ohne Admin-Rechte liefert MpPreference bei manchen Eigenschaften einen Hinweistext statt des Werts
        if (JsonRead.Get(row, c.Property) is { } v && IsAdminOnlyNotice(v))
            return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.ItemDenied) { Expected = expected }.WithFlags(gated.Flags);
        var (verdict, actual) = RowVerdict(row, c.Property, c.Op, c.Expected);
        var o = new Outcome(ResultStatus.Pass) { Expected = expected, Actual = actual, Source = ValueSource.Runtime }
            .WithFlags(gated.Flags).WithDeciding(gated.Deciding);
        return ApplyVerdict(o, verdict);
    }

    /// <summary>„N/A: Must be an administrator to view exclusions“ (research-technik §3) statt eines Werts.</summary>
    internal static bool IsAdminOnlyNotice(JsonElement v)
    {
        static bool Notice(JsonElement e) => e.ValueKind == JsonValueKind.String && e.GetString()!.StartsWith("N/A", StringComparison.OrdinalIgnoreCase);
        return Notice(v) || (v.ValueKind == JsonValueKind.Array && v.EnumerateArray().Any(Notice));
    }

    // ---------------------------------------------------------------- asrRule

    /// <summary>ASR-Modi (spec 2.3): 0 aus, 1 blockieren, 2 überwachen, 5 nicht konfiguriert, 6 warnen.</summary>
    private static readonly int[] AsrModes = [0, 1, 2, 5, 6];

    private static int[] AsrAccepted(AsrAccept accept) => accept switch
    {
        AsrAccept.BlockOrWarn => [1, 6],
        AsrAccept.BlockOrAudit => [1, 2],
        _ => [1],
    };

    /// <summary>Wie die Regel konfiguriert ist: Modus (null = nicht konfiguriert), ob nur aus der Konfiguration, oder warum unbekannt.</summary>
    private sealed record AsrState(int? Mode, bool ConfiguredOnly, string? Unavailable, string? From, string? Key = null);

    /// <summary>
    /// MSFT_MpPreference (Ids/Actions) ist der wirksame Stand und entscheidet, wenn lesbar. Nur wenn es fehlt, gelten
    /// die Richtlinie (…\Windows Defender Exploit Guard\ASR\Rules, Wertname = GUID) und dann MDM
    /// (PolicyManager\current\device\Defender, Wert AttackSurfaceReductionRules „GUID=Modus|…“) – als ConfiguredOnly.
    /// </summary>
    private AsrState Asr(string guid)
    {
        var id = guid.Trim().Trim('{', '}');
        var pref = Store.Item(Known.MpPreference);
        if (pref.IsPresent && JsonRead.Rows(pref.Value) is { Count: > 0 } rows)
        {
            var row = rows[0];
            var ids = JsonRead.Get(row, "AttackSurfaceReductionRules_Ids");
            var actions = JsonRead.Get(row, "AttackSurfaceReductionRules_Actions");
            if (ids is null) return new(null, false, null, "runtime");
            if (ids.Value.ValueKind != JsonValueKind.Array) return new(null, false, RglCodesEngine.UnexpectedValue, null);
            int index = 0;
            foreach (var e in ids.Value.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String && string.Equals(e.GetString()!.Trim().Trim('{', '}'), id, StringComparison.OrdinalIgnoreCase))
                {
                    if (actions is not { ValueKind: JsonValueKind.Array } a || index >= a.GetArrayLength() || JsonRead.Integer(a[index]) is not { } mode)
                        return new(null, false, RglCodesEngine.UnexpectedValue, null);
                    return new((int)mode, false, null, "runtime");
                }
                index++;
            }
            return new(null, false, null, "runtime");
        }
        // Nur Konfiguration
        var policy = Store.RegistryValue(Known.AsrPolicyRules, id);
        if (policy.Class == EvidenceClass.NotSet) policy = Store.RegistryValue(Known.AsrPolicyRules, "{" + id + "}") is { Class: EvidenceClass.Present } braced ? braced : policy;
        if (policy.Class == EvidenceClass.Unavailable) return new(null, false, policy.Reason, null);
        if (policy.IsPresent) return ParseMode(policy.V) is { } m ? new(m, true, null, "policy", policy.ItemKey) : new(null, false, RglCodesEngine.UnexpectedValue, null);
        var mdm = Store.RegistryValue(Known.MdmDefender, "AttackSurfaceReductionRules");
        if (mdm.Class == EvidenceClass.Unavailable) return new(null, false, mdm.Reason, null);
        if (mdm.IsPresent && mdm.V is { ValueKind: JsonValueKind.String } list)
        {
            foreach (var pair in list.GetString()!.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0 || !string.Equals(pair[..eq].Trim().Trim('{', '}'), id, StringComparison.OrdinalIgnoreCase)) continue;
                return int.TryParse(pair[(eq + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? new(m, true, null, "mdm", mdm.ItemKey) : new(null, false, RglCodesEngine.UnexpectedValue, null);
            }
        }
        // Die Konfiguration nennt die Regel nicht, der wirksame Stand ist nicht lesbar
        return new(null, false, pref.Reason ?? RglCodesEngine.ItemNotCollected, null);
    }

    private static int? ParseMode(JsonElement? v) => v switch
    {
        { ValueKind: JsonValueKind.Number } n when n.TryGetInt32(out var i) => i,
        { ValueKind: JsonValueKind.String } s when int.TryParse(s.GetString()!.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var j) => j,
        _ => null,
    };

    /// <summary>ASR-Regel: pass nur bei defender.mode = active, Echtzeitschutz an und einem zulässigen Modus (spec 2.3).</summary>
    private Outcome AsrRule(AsrRuleCheck c)
    {
        var accepted = AsrAccepted(c.Accept);
        var expected = accepted.Length == 1 ? accepted[0].ToString(CultureInfo.InvariantCulture) : CanonicalValue.Render(JsonValues.Build(w =>
        {
            w.WriteStartArray();
            foreach (var m in accepted) w.WriteNumberValue(m);
            w.WriteEndArray();
        }));
        var state = Asr(c.Guid);
        var actual = state.Mode?.ToString(CultureInfo.InvariantCulture);
        var (gate, gated) = DefenderGate(asr: true);
        if (gate != Gate.Evaluate)
        {
            var o = gated with { Expected = expected, Actual = actual };
            if (gate == Gate.Fail)
                o = o.With(state.Mode is { } m && accepted.Contains(m) ? T["Detail_AsrNotEffective"] : T["Detail_AsrNotConfigured"]);
            return o;
        }
        var outcome = new Outcome(ResultStatus.Fail) { Expected = expected, Actual = actual, Source = state.ConfiguredOnly ? ValueSource.Unknown : ValueSource.Runtime }
            .WithFlags(gated.Flags).WithDeciding(gated.Deciding).DecidedBy(state.ConfiguredOnly ? state.Key : Known.MpPreference);
        if (state.Unavailable is { } reason)
            return (outcome with { Status = ResultStatus.NotCheckable, ReasonCode = reason })
                .With(T["Detail_Evidence", Store.Canonical(Known.MpPreference), RglCodesEngine.Message(T.Culture, reason)]);
        if (state.Mode is not { } mode) return outcome.With(T["Detail_AsrNotConfigured"]);
        if (!AsrModes.Contains(mode)) return outcome with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.UnexpectedValue };
        if (!accepted.Contains(mode)) return outcome;
        if (state.ConfiguredOnly)
            return (outcome with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.ConfiguredOnly, SourceDetail = state.From })
                .WithFlags(ResultFlags.ConfiguredOnly).With(T["Detail_ConfiguredOnly"]);
        var rtp = Facts.Bool(FactNames.DefenderRtp);
        outcome = outcome.WithDeciding(Facts.SourcesOf(FactNames.DefenderRtp));
        return rtp switch
        {
            true => CloudDependentAsr.Contains(c.Guid.Trim().Trim('{', '}')) ? AsrCloud(outcome) : outcome with { Status = ResultStatus.Pass },
            false => outcome.With(T["Detail_AsrNotEffective"]),
            _ => outcome with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.DefenderStateUnknown },
        };
    }

    /// <summary>
    /// ASR-Regeln, die laut Microsoft den Cloud-Schutz brauchen („Dependencies: … Cloud Protection“, „To use this ASR
    /// rule, you must enable cloud-delivered protection“; ASR rules reference, gesehen 2026-10-18,
    /// https://learn.microsoft.com/en-us/defender-endpoint/attack-surface-reduction-rules-reference): Ausführbare
    /// Dateien nach Verbreitung/Alter/Vertrauensliste, verschleierte Skripte, erweiterter Ransomware-Schutz.
    /// </summary>
    internal static readonly HashSet<string> CloudDependentAsr = new(StringComparer.OrdinalIgnoreCase)
    {
        "01443614-cd74-433a-b99e-2ecdc07bfc25",
        "5beb7efe-fd9a-4556-801d-275e5ffc04cc",
        "c1db55ab-c21a-4637-bb3f-a12568109d35",
    };

    /// <summary>
    /// Review evidence-5: eine cloudabhängige ASR-Regel wirkt nur mit Cloud-Schutz – MAPSReporting 1 oder 2 aus
    /// MSFT_MpPreference (wie DEF-03); 0 → „konfiguriert, aber nicht wirksam“, nicht lesbar → nicht prüfbar.
    /// </summary>
    private Outcome AsrCloud(Outcome outcome)
    {
        var pref = Store.Item(Known.MpPreference);
        outcome = outcome.DecidedBy(Known.MpPreference);
        if (!pref.IsPresent || JsonRead.Rows(pref.Value) is not { Count: > 0 } rows)
            return (outcome with { Status = ResultStatus.NotCheckable, ReasonCode = pref.Reason ?? RglCodesEngine.ItemNotCollected })
                .With(T["Detail_AsrCloudUnknown"]);
        var maps = JsonRead.Get(rows[0], "MAPSReporting");
        if (maps is { } v && IsAdminOnlyNotice(v))
            return (outcome with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.ItemDenied }).With(T["Detail_AsrCloudUnknown"]);
        return (maps is { } m ? JsonRead.Integer(m) : null) switch
        {
            1 or 2 => outcome with { Status = ResultStatus.Pass },
            0 => outcome.With(T["Detail_AsrCloudOff"]),
            _ => (outcome with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.UnexpectedValue }).With(T["Detail_AsrCloudUnknown"]),
        };
    }

    // ---------------------------------------------------------------- avStatus

    /// <summary>
    /// Ein Virenschutz ist aktiv und aktuell (DEF-18): Defender-Status zuerst (aktiv, Echtzeitschutz, Alter der
    /// Signaturen relativ zu CollectedAtUtc), dann die dokumentierte WSC-API, dann SecurityCenter2 – das allein ist nur
    /// Heuristik und ergibt nie pass. Kein Virenschutz überhaupt → fail.
    /// </summary>
    private Outcome AvStatus(AvStatusCheck c)
    {
        int maxAge = c.MaxSignatureAgeDays ?? 7;
        var expected = maxAge.ToString(CultureInfo.InvariantCulture);
        var mode = Facts.Text(FactNames.DefenderMode);
        var flags = Facts.Derived.Contains(FactNames.DefenderMode) ? ResultFlags.Derived : ResultFlags.None;
        var o = new Outcome(ResultStatus.Fail) { Expected = expected, Source = ValueSource.Runtime }.WithFlags(flags);
        var modeSources = Facts.SourcesOf(FactNames.DefenderMode);

        // 1. Microsoft Defender als aktiver Virenschutz
        if (mode == FactNames.Defender.Active && FactsDeriver.DefenderRow(Store) is { } row)
        {
            o = (o with { SourceDetail = "Microsoft Defender" }).WithDeciding(modeSources).DecidedBy(Known.MpStatus);
            if (JsonRead.Bool(row, "RealTimeProtectionEnabled") != true) return o.With(T["Detail_RtpOff"]);
            int? age = null;
            if (JsonRead.Time(JsonRead.Get(row, "AntivirusSignatureLastUpdated")) is { } updated)
            {
                if (Clock.Suspect)
                    return (o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.ClockSuspect }).WithFlags(ResultFlags.ClockSuspect).With(T["Detail_ClockSuspect"]);
                age = ValueComparison.AgeDays(updated, CollectedAtUtc);
            }
            else if (JsonRead.Integer(row, "AntivirusSignatureAge") is { } reported)
            {
                if (Clock.Suspect)
                    return (o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.ClockSuspect }).WithFlags(ResultFlags.ClockSuspect).With(T["Detail_ClockSuspect"]);
                age = (int)Math.Clamp(reported, 0, int.MaxValue);
            }
            if (age is null) return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.UnexpectedValue };
            o = o with { Actual = age.Value.ToString(CultureInfo.InvariantCulture) };
            return age <= maxAge ? o with { Status = ResultStatus.Pass } : o.With(T["Detail_SignaturesOld", age.Value]);
        }

        // 2. Windows-Sicherheitscenter über die dokumentierte API
        var wsc = Store.Item(ItemKeys.WscAntivirus);
        if (wsc.IsPresent && wsc.Value.ValueKind == JsonValueKind.Array)
        {
            var products = wsc.Value.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Object).ToList();
            var on = products.Where(p => string.Equals(JsonRead.String(p, "state"), "on", StringComparison.OrdinalIgnoreCase)).ToList();
            var current = on.Where(p => string.Equals(JsonRead.String(p, "signature"), "upToDate", StringComparison.OrdinalIgnoreCase)).ToList();
            var names = string.Join(", ", (current.Count > 0 ? current : on.Count > 0 ? on : products).Select(p => JsonRead.String(p, "name") ?? "?"));
            o = (o with { Actual = names.Length == 0 ? null : names, SourceDetail = "WSC" }).DecidedBy(ItemKeys.WscAntivirus);
            // „kein Virenschutz“ stützt sich auch darauf, dass Defender nicht aktiv ist
            if (current.Count == 0) o = o.WithDeciding(modeSources);
            if (current.Count > 0) return o with { Status = ResultStatus.Pass };
            if (on.Count > 0) return o.With(T["Detail_AvOutOfDate", names]);
            if (products.Count > 0 || mode is FactNames.Defender.Absent or FactNames.Defender.Off or FactNames.Defender.Passive or FactNames.Defender.EdrBlock)
                return o.With(T["Detail_NoAv"]);
            return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.DefenderStateUnknown };
        }

        // 3. SecurityCenter2: nur Heuristik
        var sc = Store.Item(Known.SecurityCenterAv);
        if (sc.IsPresent && JsonRead.Rows(sc.Value) is { } rows)
        {
            o = o.WithDeciding(modeSources).DecidedBy(Known.SecurityCenterAv);
            if (rows.Count == 0)
            {
                // Kein Produkt registriert und Defender nachweislich nicht aktiv: kein Virenschutz
                if (mode is FactNames.Defender.Absent or FactNames.Defender.Off or FactNames.Defender.Passive or FactNames.Defender.EdrBlock)
                    return o.With(T["Detail_NoAv"]);
                return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.DefenderStateUnknown };
            }
            var heuristic = (o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.HeuristicOnly, SourceDetail = "SecurityCenter2" })
                .WithFlags(ResultFlags.Heuristic);
            foreach (var r in rows)
            {
                var name = JsonRead.String(r, "displayName") ?? "?";
                heuristic = heuristic.With(FactsDeriver.DecodeProductState(r) is { } d
                    ? T["Detail_HeuristicProduct", name, T[d.On ? "Word_On" : "Word_Off"], T[d.UpToDate ? "Word_UpToDate" : "Word_OutOfDate"]]
                    : T["Detail_HeuristicProduct", name, "?", "?"]);
            }
            return heuristic;
        }
        var reason = wsc.Reason ?? RglCodesEngine.DefenderStateUnknown;
        return Unavailable(reason, wsc.Key, null, expected).WithFlags(flags);
    }
}
