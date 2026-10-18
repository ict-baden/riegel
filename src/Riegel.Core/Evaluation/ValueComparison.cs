// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Core.Evaluation;

/// <summary>Ein Istwert in der Form, in der er verglichen wird (spec 2.4 Punkt 4).</summary>
internal abstract record Typed
{
    public sealed record Num(decimal Value) : Typed;

    public sealed record Text(string Value) : Typed;

    public sealed record Flag(bool Value) : Typed;

    public sealed record List(IReadOnlyList<Typed> Items) : Typed;

    public sealed record Bytes(byte[] Value) : Typed;
}

/// <summary>Ergebnis eines Vergleichs.</summary>
internal enum Verdict
{
    Pass,
    Fail,

    /// <summary>Istwert hat nicht den Typ, den Operator und Sollwert verlangen.</summary>
    TypeMismatch,

    /// <summary>Nicht entscheidbar (z. B. Uhr unplausibel, unlesbarer Zeitpunkt).</summary>
    Unknown,
}

/// <summary>
/// Die Operatoren je Werttyp (spec 2.4 Punkt 4): Zahlen als DWORD/QWORD (ohne Vorzeichen) bzw. Zahl, Texte ordinal ohne
/// Groß/klein und ohne Auflösen von Umgebungsvariablen, multi_sz als Menge oder Liste, Binärwerte bytegenau,
/// Wahrheitswerte, Zeitpunkte als Alter in ganzen Tagen relativ zu CollectedAtUtc.
/// </summary>
internal static class ValueComparison
{
    /// <summary>Vergleicht <paramref name="actual"/> mit dem Sollwert der Regel.</summary>
    /// <param name="listAsSequence">multi_sz: eq als Liste (Reihenfolge zählt) statt als Menge.</param>
    /// <param name="collectedAtUtc">Zeitbasis für ageDaysLe/ageDaysGt.</param>
    /// <param name="clockSuspect">Uhr unplausibel: Altersvergleiche sind dann nicht entscheidbar.</param>
    public static Verdict Compare(CheckOperator op, Typed actual, JsonElement? expected, bool listAsSequence = false,
        DateTimeOffset? collectedAtUtc = null, bool clockSuspect = false)
    {
        switch (op)
        {
            case CheckOperator.Exists: return Verdict.Pass;
            case CheckOperator.NotExists: return Verdict.Fail;
            case CheckOperator.AgeDaysLe:
            case CheckOperator.AgeDaysGt:
                return Age(op, actual, expected, collectedAtUtc, clockSuspect);
        }
        if (expected is not { } e) return Verdict.TypeMismatch;
        switch (op)
        {
            case CheckOperator.In:
            case CheckOperator.NotIn:
            {
                if (e.ValueKind != JsonValueKind.Array) return Verdict.TypeMismatch;
                bool any = false;
                foreach (var candidate in e.EnumerateArray())
                {
                    var r = Equal(actual, candidate, listAsSequence);
                    if (r is null) return Verdict.TypeMismatch;
                    if (r.Value) { any = true; break; }
                }
                return (any == (op == CheckOperator.In)) ? Verdict.Pass : Verdict.Fail;
            }
            case CheckOperator.Eq:
            case CheckOperator.Ne:
            {
                var r = Equal(actual, e, listAsSequence);
                if (r is null) return Verdict.TypeMismatch;
                return r.Value == (op == CheckOperator.Eq) ? Verdict.Pass : Verdict.Fail;
            }
            case CheckOperator.Lt:
            case CheckOperator.Le:
            case CheckOperator.Gt:
            case CheckOperator.Ge:
            {
                if (actual is not Typed.Num a || ExpectedNumber(e) is not { } b) return Verdict.TypeMismatch;
                bool ok = op switch
                {
                    CheckOperator.Lt => a.Value < b,
                    CheckOperator.Le => a.Value <= b,
                    CheckOperator.Gt => a.Value > b,
                    _ => a.Value >= b,
                };
                return ok ? Verdict.Pass : Verdict.Fail;
            }
            case CheckOperator.BitsSet:
            case CheckOperator.BitsClear:
            {
                if (actual is not Typed.Num a || !AsBits(a.Value, out var v) || ExpectedNumber(e) is not { } m || !AsBits(m, out var mask))
                    return Verdict.TypeMismatch;
                bool ok = op == CheckOperator.BitsSet ? (v & mask) == mask : (v & mask) == 0;
                return ok ? Verdict.Pass : Verdict.Fail;
            }
            case CheckOperator.ContainsAll:
            case CheckOperator.ContainsNone:
            {
                if (actual is not Typed.List list || e.ValueKind != JsonValueKind.Array) return Verdict.TypeMismatch;
                foreach (var wanted in e.EnumerateArray())
                {
                    bool found = false;
                    foreach (var item in list.Items)
                    {
                        var r = Equal(item, wanted, false);
                        if (r is null) return Verdict.TypeMismatch;
                        if (r.Value) { found = true; break; }
                    }
                    if (op == CheckOperator.ContainsAll && !found) return Verdict.Fail;
                    if (op == CheckOperator.ContainsNone && found) return Verdict.Fail;
                }
                return Verdict.Pass;
            }
        }
        return Verdict.TypeMismatch;
    }

    /// <summary>Gleich? null = Typen passen nicht zusammen.</summary>
    public static bool? Equal(Typed actual, JsonElement expected, bool listAsSequence)
    {
        switch (actual)
        {
            case Typed.Num n:
                return ExpectedNumber(expected) is { } d ? n.Value == d : null;
            case Typed.Text t:
                if (expected.ValueKind == JsonValueKind.String) return string.Equals(t.Value, expected.GetString(), StringComparison.OrdinalIgnoreCase);
                return null;
            case Typed.Flag f:
                return expected.ValueKind switch
                {
                    JsonValueKind.True => f.Value,
                    JsonValueKind.False => !f.Value,
                    _ => null,
                };
            case Typed.Bytes b:
                return expected.ValueKind == JsonValueKind.String && ParseHex(expected.GetString()!) is { } bytes ? b.Value.AsSpan().SequenceEqual(bytes) : null;
            case Typed.List l:
            {
                if (expected.ValueKind != JsonValueKind.Array) return null;
                var wanted = expected.EnumerateArray().ToList();
                if (listAsSequence)
                {
                    if (wanted.Count != l.Items.Count) return false;
                    for (int i = 0; i < wanted.Count; i++)
                    {
                        var r = Equal(l.Items[i], wanted[i], false);
                        if (r is null) return null;
                        if (!r.Value) return false;
                    }
                    return true;
                }
                // Als Menge: jedes Soll-Element vorhanden und jedes Ist-Element erwartet (Doppelte zählen nicht)
                foreach (var w in wanted)
                {
                    bool found = false;
                    foreach (var item in l.Items)
                    {
                        var r = Equal(item, w, false);
                        if (r is null) return null;
                        if (r.Value) { found = true; break; }
                    }
                    if (!found) return false;
                }
                foreach (var item in l.Items)
                {
                    bool found = false;
                    foreach (var w in wanted)
                    {
                        var r = Equal(item, w, false);
                        if (r is null) return null;
                        if (r.Value) { found = true; break; }
                    }
                    if (!found) return false;
                }
                return true;
            }
        }
        return null;
    }

    /// <summary>Sollwert als Zahl: JSON-Zahl, Dezimaltext oder 0x-Hex (0 und 0x0 sind gleich, spec 7.3); Wahrheitswerte als 1/0.</summary>
    public static decimal? ExpectedNumber(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Number:
                return e.TryGetDecimal(out var d) ? d : null;
            case JsonValueKind.String:
            {
                var text = e.GetString() ?? "";
                if (CanonicalValue.TryParseInteger(text, out var u)) return u;
                return decimal.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var s) ? s : null;
            }
            default:
                return null;
        }
    }

    /// <summary>Alter in ganzen Tagen bis <paramref name="collectedAtUtc"/>; mehr als 24 h in der Zukunft → nicht entscheidbar.</summary>
    public static int? AgeDays(DateTimeOffset time, DateTimeOffset collectedAtUtc)
    {
        var span = collectedAtUtc - time;
        if (span < TimeSpan.FromHours(-24)) return null;
        return span < TimeSpan.Zero ? 0 : (int)Math.Floor(span.TotalDays);
    }

    private static Verdict Age(CheckOperator op, Typed actual, JsonElement? expected, DateTimeOffset? collectedAtUtc, bool clockSuspect)
    {
        if (expected is not { } e || ExpectedNumber(e) is not { } limit) return Verdict.TypeMismatch;
        if (actual is not Typed.Text t) return Verdict.TypeMismatch;
        var time = JsonRead.Time(JsonValues.Text(t.Value));
        if (time is null) return Verdict.TypeMismatch;
        if (clockSuspect || collectedAtUtc is null) return Verdict.Unknown;
        if (AgeDays(time.Value, collectedAtUtc.Value) is not { } age) return Verdict.Unknown;
        bool ok = op == CheckOperator.AgeDaysLe ? age <= limit : age > limit;
        return ok ? Verdict.Pass : Verdict.Fail;
    }

    private static bool AsBits(decimal value, out ulong bits)
    {
        bits = 0;
        if (value < 0 || value > ulong.MaxValue || decimal.Truncate(value) != value) return false;
        bits = (ulong)value;
        return true;
    }

    /// <summary>Hex-Text („0a0b“, „hex:0a0b“, Leerzeichen und Kommas erlaubt) als Bytes, sonst null.</summary>
    public static byte[]? ParseHex(string text)
    {
        var t = text.StartsWith("hex:", StringComparison.OrdinalIgnoreCase) ? text[4..] : text;
        t = new string([.. t.Where(c => !char.IsWhiteSpace(c) && c != ',')]);
        if (t.Length % 2 != 0) return null;
        try { return Convert.FromHexString(t); }
        catch (FormatException) { return null; }
    }

    /// <summary>
    /// JSON-Wert aus WMI/API als vergleichbarer Wert (spec 7.3: Zahlen, uint64 als Text, Wahrheitswerte, Texte,
    /// Listen). null = fehlend (JSON null); Objekte sind nicht vergleichbar.
    /// </summary>
    public static Typed? FromJson(JsonElement v, JsonElement? expectedHint)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.Number:
                return v.TryGetDecimal(out var d) ? new Typed.Num(d) : null;
            case JsonValueKind.True: return new Typed.Flag(true);
            case JsonValueKind.False: return new Typed.Flag(false);
            case JsonValueKind.String:
            {
                var text = v.GetString()!;
                // uint64-Werte stehen als Dezimaltext im Export: als Zahl vergleichen, wenn der Sollwert eine Zahl ist
                if (expectedHint is { } h && WantsNumber(h) && CanonicalValue.TryParseInteger(text, out var u)) return new Typed.Num(u);
                return new Typed.Text(text);
            }
            case JsonValueKind.Array:
            {
                JsonElement? itemHint = expectedHint is { ValueKind: JsonValueKind.Array } a && a.GetArrayLength() > 0 ? a[0] : expectedHint;
                var items = new List<Typed>();
                foreach (var e in v.EnumerateArray())
                {
                    if (FromJson(e, itemHint) is not { } t) return null;
                    items.Add(t);
                }
                return new Typed.List(items);
            }
            default:
                return null;
        }
    }

    private static bool WantsNumber(JsonElement hint) => hint.ValueKind switch
    {
        JsonValueKind.Number => true,
        JsonValueKind.Array => hint.GetArrayLength() > 0 && hint[0].ValueKind == JsonValueKind.Number,
        _ => false,
    };

    /// <summary>Kanonische Form eines verglichenen Werts (spec 7.3).</summary>
    public static string Render(Typed value) => value switch
    {
        Typed.Num n => n.Value.ToString(CultureInfo.InvariantCulture),
        Typed.Text t => t.Value,
        Typed.Flag f => f.Value ? "true" : "false",
        Typed.Bytes b => CanonicalValue.Binary(b.Value),
        Typed.List l => CanonicalValue.Render(JsonValues.Build(w =>
        {
            w.WriteStartArray();
            foreach (var item in l.Items)
                switch (item)
                {
                    case Typed.Num n: w.WriteNumberValue(n.Value); break;
                    case Typed.Flag f: w.WriteBooleanValue(f.Value); break;
                    default: w.WriteStringValue(Render(item)); break;
                }
            w.WriteEndArray();
        }))!,
        _ => "",
    };
}
