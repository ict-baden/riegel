// SPDX-License-Identifier: MIT
using Riegel.Core.Evaluation;
using Riegel.Core.Model;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Evaluation;

/// <summary>
/// Operatoren je Werttyp (spec 2.4 Punkt 4), einmal direkt am Vergleich und einmal über eine Registry-Regel mit den
/// Beweisfällen pass, fail, fehlend, nicht erfasst, verweigert, abgeschnitten, falscher Typ und n/a.
/// </summary>
public sealed class OperatorTests
{
    private static Typed N(decimal v) => new Typed.Num(v);
    private static Typed S(string v) => new Typed.Text(v);
    private static Typed B(bool v) => new Typed.Flag(v);
    private static Typed L(params string[] v) => new Typed.List([.. v.Select(x => (Typed)new Typed.Text(x))]);

    public static TheoryData<string, CheckOperator, string, string> Cases => new()
    {
        // Zahlen (DWORD/QWORD)
        { "n:5", CheckOperator.Eq, "5", "Pass" }, { "n:5", CheckOperator.Eq, "6", "Fail" }, { "n:0", CheckOperator.Eq, "\"0x0\"", "Pass" },
        { "n:5", CheckOperator.Ne, "6", "Pass" }, { "n:5", CheckOperator.Ne, "5", "Fail" },
        { "n:5", CheckOperator.Lt, "6", "Pass" }, { "n:6", CheckOperator.Lt, "6", "Fail" },
        { "n:6", CheckOperator.Le, "6", "Pass" }, { "n:7", CheckOperator.Le, "6", "Fail" },
        { "n:7", CheckOperator.Gt, "6", "Pass" }, { "n:6", CheckOperator.Gt, "6", "Fail" },
        { "n:6", CheckOperator.Ge, "6", "Pass" }, { "n:5", CheckOperator.Ge, "6", "Fail" },
        { "n:2", CheckOperator.In, "[1,2,3]", "Pass" }, { "n:4", CheckOperator.In, "[1,2,3]", "Fail" },
        { "n:4", CheckOperator.NotIn, "[1,2,3]", "Pass" }, { "n:2", CheckOperator.NotIn, "[1,2,3]", "Fail" },
        { "n:7", CheckOperator.BitsSet, "5", "Pass" }, { "n:6", CheckOperator.BitsSet, "5", "Fail" },
        { "n:2", CheckOperator.BitsClear, "5", "Pass" }, { "n:3", CheckOperator.BitsClear, "5", "Fail" },
        { "n:18446744073709551615", CheckOperator.Eq, "\"18446744073709551615\"", "Pass" },
        { "n:5", CheckOperator.Eq, "\"fünf\"", "TypeMismatch" }, { "n:5", CheckOperator.ContainsAll, "[\"a\"]", "TypeMismatch" },
        // Texte: ordinal ohne Groß/klein, keine Umgebungsvariablen
        { "s:Running", CheckOperator.Eq, "\"running\"", "Pass" }, { "s:Running", CheckOperator.Eq, "\"Stopped\"", "Fail" },
        { "s:Running", CheckOperator.Ne, "\"Stopped\"", "Pass" }, { "s:a", CheckOperator.In, "[\"A\",\"b\"]", "Pass" },
        { "s:c", CheckOperator.NotIn, "[\"A\",\"b\"]", "Pass" }, { "s:%SystemRoot%", CheckOperator.Eq, "\"C:\\\\Windows\"", "Fail" },
        { "s:a", CheckOperator.Lt, "\"b\"", "TypeMismatch" }, { "s:a", CheckOperator.Eq, "1", "TypeMismatch" },
        // Wahrheitswerte
        { "b:true", CheckOperator.Eq, "true", "Pass" }, { "b:true", CheckOperator.Eq, "false", "Fail" },
        { "b:false", CheckOperator.Ne, "true", "Pass" }, { "b:true", CheckOperator.Eq, "1", "TypeMismatch" },
        // multi_sz / Listen
        { "l:a,b", CheckOperator.Eq, "[\"B\",\"a\"]", "Pass" }, { "l:a,b", CheckOperator.Eq, "[\"a\"]", "Fail" },
        { "l:a,b", CheckOperator.Ne, "[\"a\"]", "Pass" }, { "l:a,b,c", CheckOperator.ContainsAll, "[\"c\",\"a\"]", "Pass" },
        { "l:a,b", CheckOperator.ContainsAll, "[\"c\"]", "Fail" }, { "l:a,b", CheckOperator.ContainsNone, "[\"c\"]", "Pass" },
        { "l:a,b", CheckOperator.ContainsNone, "[\"B\"]", "Fail" },
        // Vorhandensein (die Beweisklasse prüft die Regel, hier: vorhanden)
        { "n:1", CheckOperator.Exists, "null", "Pass" }, { "n:1", CheckOperator.NotExists, "null", "Fail" },
        // Alter in ganzen Tagen relativ zu CollectedAtUtc (2026-10-18T09:00:00Z)
        { "s:2026-10-11T09:00:00Z", CheckOperator.AgeDaysLe, "7", "Pass" }, { "s:2026-10-10T08:59:59Z", CheckOperator.AgeDaysLe, "7", "Fail" },
        { "s:2026-09-01", CheckOperator.AgeDaysGt, "30", "Pass" }, { "s:2026-10-17T00:00:00Z", CheckOperator.AgeDaysGt, "30", "Fail" },
        { "s:2026-10-25T00:00:00Z", CheckOperator.AgeDaysLe, "7", "Unknown" }, { "s:gestern", CheckOperator.AgeDaysLe, "7", "TypeMismatch" },
    };

    private static Typed Parse(string spec) => spec[0] switch
    {
        'n' => N(decimal.Parse(spec[2..], System.Globalization.CultureInfo.InvariantCulture)),
        's' => S(spec[2..]),
        'b' => B(spec[2..] == "true"),
        _ => L(spec[2..].Split(',')),
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Operator_je_Werttyp(string actual, CheckOperator op, string expected, string verdict) =>
        Assert.Equal(Enum.Parse<Verdict>(verdict), ValueComparison.Compare(op, Parse(actual), JsonValues.Parse(expected), false, FixedClock.Default, clockSuspect: false));

    [Fact]
    public void Alter_bei_unplausibler_Uhr_ist_nicht_entscheidbar() =>
        Assert.Equal(Verdict.Unknown, ValueComparison.Compare(CheckOperator.AgeDaysLe, S("2026-10-17T00:00:00Z"), JsonValues.Number(7), false, FixedClock.Default, clockSuspect: true));

    [Fact]
    public void Liste_als_Folge_beachtet_die_Reihenfolge()
    {
        Assert.Equal(Verdict.Pass, ValueComparison.Compare(CheckOperator.Eq, L("a", "b"), JsonValues.Parse("[\"A\",\"b\"]"), listAsSequence: true));
        Assert.Equal(Verdict.Fail, ValueComparison.Compare(CheckOperator.Eq, L("a", "b"), JsonValues.Parse("[\"b\",\"a\"]"), listAsSequence: true));
    }

    // ---------------------------------------------------------------- jeder Operator über eine Registry-Regel, acht Fälle

    private const string Key = @"HKLM\SOFTWARE\Policies\Riegel-Test\Operators";

    /// <summary>Operator, Werttyp, Sollwert, Wert für pass, Wert für fail, Wert falschen Typs.</summary>
    public static TheoryData<CheckOperator, RegValueType, object?, object, object, (RegValueType, object)> RegistryCases => new()
    {
        { CheckOperator.Eq, RegValueType.Dword, 1, 1, 0, (RegValueType.Sz, "1") },
        { CheckOperator.Ne, RegValueType.Dword, 1, 0, 1, (RegValueType.Sz, "0") },
        { CheckOperator.Lt, RegValueType.Dword, 10, 9, 10, (RegValueType.Qword, 9) },
        { CheckOperator.Le, RegValueType.Dword, 10, 10, 11, (RegValueType.Sz, "10") },
        { CheckOperator.Gt, RegValueType.Qword, 10, 11, 10, (RegValueType.Dword, 11) },
        { CheckOperator.Ge, RegValueType.Qword, 10, 10, 9, (RegValueType.Sz, "10") },
        { CheckOperator.In, RegValueType.Dword, new[] { 1, 2 }, 2, 3, (RegValueType.Sz, "2") },
        { CheckOperator.NotIn, RegValueType.Dword, new[] { 1, 2 }, 3, 2, (RegValueType.Sz, "3") },
        { CheckOperator.BitsSet, RegValueType.Dword, 6, 7, 5, (RegValueType.Sz, "7") },
        { CheckOperator.BitsClear, RegValueType.Dword, 6, 1, 3, (RegValueType.Sz, "1") },
        { CheckOperator.Eq, RegValueType.Sz, "Ja", "ja", "nein", (RegValueType.Dword, 1) },
        { CheckOperator.In, RegValueType.ExpandSz, new[] { "a", "b" }, "B", "c", (RegValueType.Sz, "b") },
        { CheckOperator.ContainsAll, RegValueType.MultiSz, new[] { "a" }, new[] { "a", "b" }, new[] { "b" }, (RegValueType.Sz, "a") },
        { CheckOperator.ContainsNone, RegValueType.MultiSz, new[] { "x" }, new[] { "a" }, new[] { "x" }, (RegValueType.Dword, 0) },
        { CheckOperator.Eq, RegValueType.Binary, "hex:0102", new byte[] { 1, 2 }, new byte[] { 2, 1 }, (RegValueType.Dword, 258) },
        { CheckOperator.Exists, RegValueType.Dword, null, 0, 0, (RegValueType.Sz, "x") },
        { CheckOperator.NotExists, RegValueType.Dword, null, 0, 0, (RegValueType.Sz, "x") },
        { CheckOperator.AgeDaysLe, RegValueType.Sz, 7, "2026-10-15T00:00:00Z", "2026-09-01T00:00:00Z", (RegValueType.Dword, 1) },
        { CheckOperator.AgeDaysGt, RegValueType.Sz, 30, "2026-09-01T00:00:00Z", "2026-10-15T00:00:00Z", (RegValueType.Dword, 1) },
    };

    [Theory]
    [MemberData(nameof(RegistryCases))]
    public void Jeder_Operator_mit_allen_Beweisfaellen(CheckOperator op, RegValueType type, object? expected, object good, object bad, (RegValueType Type, object Value) wrong)
    {
        var rule = RuleBuilder.Check("TOPR-01").Os("client").Registry(Key, "V", type, op, expected, WhenMissing.Fail).Build();
        Snapshot With(Action<SnapshotBuilder> change)
        {
            var b = SnapshotBuilder.Windows11Workgroup();
            change(b);
            return b.CollectedAt(FixedClock.Default).Build();
        }
        ResultStatus Status(Snapshot s) => EngineHarness.Eval(rule, s).Status;
        bool presence = op is CheckOperator.Exists or CheckOperator.NotExists;

        var pass = Status(With(b => b.RegistryValue(Key, "V", type, good)));
        var fail = Status(With(b => b.RegistryValue(Key, "V", type, bad)));
        var missing = Status(With(b => b.RegistryKey(Key)));
        var notCollected = Status(With(b => b.NotCollected(ItemKeys.Registry(Key))));
        var denied = Status(With(b => b.Denied(ItemKeys.Registry(Key))));
        var truncated = Status(With(b => b.RegistryValue(Key, "V", type, good).Truncated(ItemKeys.Registry(Key))));
        var wrongType = Status(With(b => b.RegistryValue(Key, "V", wrong.Type, wrong.Value)));
        var notApplicable = EngineHarness.Eval(rule, SnapshotBuilder.Server2022Member().RegistryValue(Key, "V", type, good).Build()).Status;

        Assert.Equal(op == CheckOperator.NotExists ? ResultStatus.Fail : ResultStatus.Pass, pass);
        if (!presence) Assert.Equal(ResultStatus.Fail, fail);
        Assert.Equal(op == CheckOperator.NotExists ? ResultStatus.Pass : ResultStatus.Fail, missing); // whenMissing fail bzw. notExists
        Assert.Equal(ResultStatus.NotCheckable, notCollected);
        Assert.Equal(ResultStatus.NotCheckable, denied);
        Assert.Equal(ResultStatus.NotCheckable, truncated);
        // falscher Werttyp ist ein Befund; exists/notExists fragen nur nach dem Vorhandensein
        Assert.Equal(presence ? (op == CheckOperator.Exists ? ResultStatus.Pass : ResultStatus.Fail) : ResultStatus.Fail, wrongType);
        Assert.Equal(ResultStatus.NotApplicable, notApplicable);
    }
}
