// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Core.Evaluation;

/// <summary>Prüfarten service, feature und wmiProperty (spec 2.3).</summary>
internal sealed partial class EvaluationSession
{
    /// <summary>Start-Werte der Dienste in der Registry (Services\&lt;Name&gt;\Start): 0 Boot … 4 Disabled.</summary>
    private static readonly string[] StartModes = ["Boot", "System", "Auto", "Manual", "Disabled"];

    private static string NormalizeStartMode(string mode) =>
        string.Equals(mode, "Automatic", StringComparison.OrdinalIgnoreCase) ? "Auto" : mode;

    private static string ServiceText(string? startMode, string? state) => CanonicalValue.Render(JsonValues.Build(w =>
    {
        w.WriteStartObject();
        if (startMode is not null) w.WriteString("startMode", startMode);
        if (state is not null) w.WriteString("state", state);
        w.WriteEndObject();
    }))!;

    private static string ServiceExpected(ServiceCheck c) => CanonicalValue.Render(JsonValues.Build(w =>
    {
        w.WriteStartObject();
        if (c.StartMode is { } modes) { w.WritePropertyName("startMode"); w.WriteStartArray(); foreach (var m in modes) w.WriteStringValue(m); w.WriteEndArray(); }
        if (c.State is { } states) { w.WritePropertyName("state"); w.WriteStartArray(); foreach (var s in states) w.WriteStringValue(s); w.WriteEndArray(); }
        w.WriteEndObject();
    }))!;

    private Outcome Absent(AbsentOutcome ifAbsent, string? expected, string detail) => ifAbsent switch
    {
        AbsentOutcome.Pass => new Outcome(ResultStatus.Pass) { Expected = expected, Source = ValueSource.Runtime }.With(detail),
        AbsentOutcome.Fail => new Outcome(ResultStatus.Fail) { Expected = expected, Source = ValueSource.Runtime }.With(detail),
        _ => new Outcome(ResultStatus.NotApplicable, RglCodesEngine.NotApplicableAbsent) { Expected = expected }.With(detail),
    };

    /// <summary>
    /// Dienst: Win32_Service (Name ohne Groß/klein), sonst Services\&lt;Name&gt;\Start in der Registry (nur Starttyp).
    /// Ohne Laufzeitzustand ist eine state-Erwartung nicht prüfbar – außer der Starttyp verfehlt schon die Erwartung.
    /// </summary>
    private Outcome Service(ServiceCheck c)
    {
        var expected = ServiceExpected(c);
        var wmi = Store.Item(Known.Service);
        if (wmi.IsPresent && JsonRead.Rows(wmi.Value) is { } rows)
        {
            var row = rows.FirstOrDefault(r => string.Equals(JsonRead.String(r, "Name"), c.Name, StringComparison.OrdinalIgnoreCase));
            if (row.ValueKind != JsonValueKind.Object) return Absent(c.IfAbsent, expected, T["Detail_ServiceAbsent", c.Name]).DecidedBy(Known.Service);
            var mode = JsonRead.String(row, "StartMode") is { } m ? NormalizeStartMode(m) : null;
            var state = JsonRead.String(row, "State");
            var o = new Outcome(ResultStatus.Pass) { Expected = expected, Actual = ServiceText(mode, state), Source = ValueSource.Runtime }.DecidedBy(Known.Service);
            if (c.StartMode is { Count: > 0 } allowedModes)
            {
                if (mode is null) return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.UnexpectedValue };
                if (!allowedModes.Any(a => string.Equals(NormalizeStartMode(a), mode, StringComparison.OrdinalIgnoreCase))) return o with { Status = ResultStatus.Fail };
            }
            if (c.State is { Count: > 0 } allowedStates)
            {
                if (state is null) return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.UnexpectedValue };
                if (!allowedStates.Contains(state, StringComparer.OrdinalIgnoreCase)) return o with { Status = ResultStatus.Fail };
            }
            return o;
        }
        if (wmi.IsPresent) return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.UnexpectedValue) { Expected = expected };

        // Ohne Win32_Service: Starttyp aus der Registry (Bordmittel-Pakete), der Laufzeitzustand bleibt unbekannt
        var key = Store.RegistryKey(Known.ServicesRoot + "\\" + c.Name);
        if (key.Class == EvidenceClass.NotSet) return Absent(c.IfAbsent, expected, T["Detail_ServiceAbsent", c.Name]).DecidedBy(key.Key);
        if (key.Class == EvidenceClass.Unavailable) return Unavailable(wmi.Reason, wmi.Key, null, expected);
        var start = Store.RegistryValue(Known.ServicesRoot + "\\" + c.Name, "Start");
        if (!start.IsPresent || start.Type != "dword" || start.V is not { ValueKind: JsonValueKind.Number } n || !n.TryGetInt32(out var startValue)
            || startValue is < 0 or > 4)
            return Unavailable(start.Reason ?? RglCodesEngine.UnexpectedValue, start.ItemKey, null, expected);
        var regMode = StartModes[startValue];
        var (src, detail) = SourceOf(start.ItemKey, "Start", null);
        var r = new Outcome(ResultStatus.Pass) { Expected = expected, Actual = ServiceText(regMode, null), Source = src, SourceDetail = detail }.DecidedBy(start.ItemKey);
        if (c.StartMode is { Count: > 0 } modes && !modes.Any(a => string.Equals(NormalizeStartMode(a), regMode, StringComparison.OrdinalIgnoreCase)))
            return r with { Status = ResultStatus.Fail };
        if (c.State is { Count: > 0 })
            return (r with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.ItemNotCollected }).With(T["Detail_ServiceStateUnknown"]);
        return r;
    }

    /// <summary>Optionales Feature (Win32_OptionalFeature): InstallState 1 aktiviert, 2 deaktiviert, 3 nicht vorhanden.</summary>
    private Outcome Feature(FeatureCheck c)
    {
        var expected = CanonicalValue.Render(JsonValues.Build(w => { w.WriteStartArray(); foreach (var s in c.InstallState) w.WriteNumberValue(s); w.WriteEndArray(); }));
        var wmi = Store.Item(Known.OptionalFeature);
        if (!wmi.IsPresent) return Unavailable(wmi.Reason, wmi.Key, null, expected);
        if (JsonRead.Rows(wmi.Value) is not { } rows) return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.UnexpectedValue) { Expected = expected };
        var row = rows.FirstOrDefault(r => string.Equals(JsonRead.String(r, "Name"), c.Name, StringComparison.OrdinalIgnoreCase));
        if (row.ValueKind != JsonValueKind.Object) return Absent(c.IfAbsent, expected, T["Detail_FeatureAbsent", c.Name]);
        if (JsonRead.Integer(row, "InstallState") is not { } state)
            return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.UnexpectedValue) { Expected = expected };
        return new Outcome(c.InstallState.Contains((int)state) ? ResultStatus.Pass : ResultStatus.Fail)
        {
            Expected = expected,
            Actual = state.ToString(CultureInfo.InvariantCulture),
            Source = ValueSource.Runtime,
        };
    }

    /// <summary>
    /// Eigenschaft eines WMI-Eintrags (Zeilen oder Methoden-out), mit Zeilenfilter und any/all/first. Eine fehlende
    /// Eigenschaft oder ein Wert unerwarteten Typs ist nicht prüfbar (Rohdaten, kein Zustand des Rechners); JSON null
    /// zählt nur für exists/notExists als „nicht gesetzt“.
    /// </summary>
    private Outcome WmiProperty(WmiPropertyCheck c)
    {
        var expected = c.Op is CheckOperator.Exists or CheckOperator.NotExists ? null : CanonicalValue.Render(c.Expected);
        var item = Store.Item(c.Item);
        if (!item.IsPresent) return Unavailable(item.Reason, item.Key, null, expected);
        if (JsonRead.Rows(item.Value) is not { } allRows) return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.UnexpectedValue) { Expected = expected };
        var rows = new List<JsonElement>();
        foreach (var row in allRows)
        {
            if (c.RowFilter is not { } f) { rows.Add(row); continue; }
            if (!JsonRead.TryGet(row, f.Property, out var fv)) return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.UnexpectedValue) { Expected = expected };
            if (ValueComparison.FromJson(fv, f.Value) is not { } typed) continue;
            var verdict = ValueComparison.Compare(f.Op, typed, f.Value);
            if (verdict == Verdict.Pass) rows.Add(row);
            else if (verdict != Verdict.Fail) return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.UnexpectedValue) { Expected = expected };
        }
        if (rows.Count == 0)
        {
            var detail = T["Detail_NoRows", Store.Canonical(c.Item)];
            return c.IfNoRows switch
            {
                NoRowsOutcome.Pass => new Outcome(ResultStatus.Pass) { Expected = expected, Source = ValueSource.Runtime }.With(detail),
                NoRowsOutcome.Fail => new Outcome(ResultStatus.Fail) { Expected = expected, Source = ValueSource.Runtime }.With(detail),
                NoRowsOutcome.NotApplicable => new Outcome(ResultStatus.NotApplicable, RglCodesEngine.NotApplicableAbsent) { Expected = expected }.With(detail),
                _ => new Outcome(ResultStatus.NotCheckable, RglCodesEngine.NoDecisiveEvidence) { Expected = expected }.With(detail),
            };
        }
        if (c.Rows == RowsMode.First) rows = [rows[0]];
        var verdicts = new List<Verdict>();
        var actuals = new List<string?>();
        foreach (var row in rows)
        {
            var (verdict, rendered) = RowVerdict(row, c.Property, c.Op, c.Expected);
            verdicts.Add(verdict);
            actuals.Add(rendered);
        }
        var actual = actuals.Count == 1 ? actuals[0] : CanonicalValue.Render(JsonValues.Build(w =>
        {
            w.WriteStartArray();
            foreach (var a in actuals) { if (a is null) w.WriteNullValue(); else w.WriteStringValue(a); }
            w.WriteEndArray();
        }));
        var o = new Outcome(ResultStatus.Pass) { Expected = expected, Actual = actual, Source = ValueSource.Runtime };
        Verdict combined;
        if (c.Rows == RowsMode.All)
            combined = verdicts.Any(v => v == Verdict.Fail) ? Verdict.Fail : verdicts.All(v => v == Verdict.Pass) ? Verdict.Pass : verdicts.First(v => v != Verdict.Pass);
        else
            combined = verdicts.Any(v => v == Verdict.Pass) ? Verdict.Pass : verdicts.All(v => v == Verdict.Fail) ? Verdict.Fail : verdicts.First(v => v != Verdict.Fail);
        return ApplyVerdict(o, combined);
    }

    /// <summary>Vergleich einer Eigenschaft in einer Zeile (auch für defenderSetting).</summary>
    private (Verdict Verdict, string? Actual) RowVerdict(JsonElement row, string property, CheckOperator op, JsonElement? expected)
    {
        if (!JsonRead.TryGet(row, property, out var value))
            return (Verdict.TypeMismatch, null);
        if (value.ValueKind == JsonValueKind.Null)
            return op switch
            {
                CheckOperator.Exists => (Verdict.Fail, null),
                CheckOperator.NotExists => (Verdict.Pass, null),
                _ => (Verdict.TypeMismatch, null),
            };
        if (op is CheckOperator.AgeDaysLe or CheckOperator.AgeDaysGt)
        {
            if (value.ValueKind != JsonValueKind.String) return (Verdict.TypeMismatch, CanonicalValue.Render(value));
            return (ValueComparison.Compare(op, new Typed.Text(value.GetString()!), expected, false, CollectedAtUtc, Clock.Suspect), value.GetString());
        }
        if (ValueComparison.FromJson(value, expected) is not { } typed) return (Verdict.TypeMismatch, CanonicalValue.Render(value));
        return (ValueComparison.Compare(op, typed, expected), ValueComparison.Render(typed));
    }
}
