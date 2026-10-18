// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Plants;
using Riegel.Tests.Plants;
using static Riegel.Tests.Plants.WorkflowKit;

namespace Riegel.Tests.Registers;

/// <summary>Wirkung der Register auf Rohergebnisse (spec 2.7, 2.11–2.13), fester Tag 2026-10-18.</summary>
public sealed class RegisterApplyTests
{
    private static RuleResult Apply(RuleResult raw, RegisterSet r, string host = "HMI-TEST", Plant? plant = null, DateOnly? today = null) =>
        Services().Registers.Apply([raw], r, plant ?? WorkflowKit.Plant(), host, today ?? Today).Single();

    private static readonly RuleResult Failing = Result("NET-08", ResultStatus.Fail, actual: "0");

    [Fact]
    public void Gueltige_Ausnahme_mit_passendem_Wert_ergibt_bewusst_offen()
    {
        var result = Apply(Failing, Set([Exception("EX-0001", "NET-08", Machines("HMI-TEST")).Approve()]));
        Assert.Equal(ResultStatus.Exception, result.Status);
        Assert.Equal("EX-0001", result.ExceptionId);
        Assert.Equal(ResultFlags.None, result.Flags);
    }

    [Fact]
    public void Anderer_Istwert_ergibt_offen_mit_ValueMismatch()
    {
        var result = Apply(Failing with { Actual = "2" }, Set([Exception("EX-0001", "NET-08", All, accepted: "0").Approve()]));
        Assert.Equal(ResultStatus.Fail, result.Status);
        Assert.Equal(ResultFlags.ExceptionValueMismatch, result.Flags);
        Assert.Equal("EX-0001", result.ExceptionId);
    }

    [Theory]
    [InlineData("*", "7", true)]
    [InlineData("(not set)", null, true)]
    [InlineData("(nicht gesetzt)", null, true)]
    [InlineData("(not set)", "0", false)]
    [InlineData("0x0", "0", true)]
    [InlineData("running", "Running", true)]
    [InlineData("1", null, false)]
    public void Akzeptierter_Wert_kanonisch_verglichen(string accepted, string? actual, bool matches)
    {
        var result = Apply(Failing with { Actual = actual }, Set([Exception("EX-0001", "NET-08", All, accepted: accepted).Approve()]));
        Assert.Equal(matches ? ResultStatus.Exception : ResultStatus.Fail, result.Status);
    }

    [Fact]
    public void Beantragte_Ausnahme_laesst_den_Befund_offen()
    {
        var result = Apply(Failing, Set([Exception("EX-0001", "NET-08", All)]));
        Assert.Equal(ResultStatus.Fail, result.Status);
        Assert.Equal(ResultFlags.ExceptionPending, result.Flags);
        Assert.Equal("EX-0001", result.ExceptionId);
    }

    [Fact]
    public void Aenderung_nach_Freigabe_macht_die_Ausnahme_wieder_beantragt()
    {
        var services = Services();
        var registers = Set([Exception("EX-0001", "NET-08", All)]);
        var approved = services.Registers.Approve(registers, RegisterKind.Exceptions, ["EX-0001"],
            new Approval("Informationssicherheitsbeauftragter", null, Today.AddDays(-3), Today.AddMonths(6), "FB-12", null));
        Assert.Equal(ResultStatus.Exception, Apply(Failing, approved).Status);

        var entry = approved.Exceptions.Exceptions[0];
        var edited = approved with
        {
            Exceptions = approved.Exceptions with { Exceptions = [entry with { CompensatingMeasure = "Zusatz nach der Freigabe" }] },
        };
        var result = Apply(Failing, edited);
        Assert.Equal(ResultStatus.Fail, result.Status);
        Assert.Equal(ResultFlags.ExceptionPending | ResultFlags.ExceptionChangedAfterApproval, result.Flags);
    }

    [Fact]
    public void Ablauf_am_Tag_nach_reviewBy_und_Warnung_binnen_warnDays()
    {
        var entry = Exception("EX-0001", "NET-08", All);
        var expiresToday = Set([entry.Approve(Today.AddMonths(-11), Today)]);
        var onLastDay = Apply(Failing, expiresToday);
        Assert.Equal(ResultStatus.Exception, onLastDay.Status);
        Assert.Equal(ResultFlags.ExceptionExpiring, onLastDay.Flags);

        var dayAfter = Apply(Failing, expiresToday, today: Today.AddDays(1));
        Assert.Equal(ResultStatus.Fail, dayAfter.Status);
        Assert.Equal(ResultFlags.ExceptionExpired, dayAfter.Flags);

        var in30 = Apply(Failing, Set([entry.Approve(Today.AddMonths(-11), Today.AddDays(30))]));
        Assert.Equal(ResultFlags.ExceptionExpiring, in30.Flags);
        var in31 = Apply(Failing, Set([entry.Approve(Today.AddMonths(-11), Today.AddDays(31))]));
        Assert.Equal(ResultFlags.None, in31.Flags);
        // warnDays aus der Anlage
        var plant = WorkflowKit.Plant(PlantSettings.Default with { WarnDays = 60 });
        Assert.Equal(ResultFlags.ExceptionExpiring, Apply(Failing, Set([entry.Approve(Today.AddMonths(-11), Today.AddDays(45))]), plant: plant).Flags);
    }

    [Fact]
    public void Ungueltige_Ausnahme_gilt_nie()
    {
        var plant = WorkflowKit.Plant(PlantSettings.Default with { RiskAcceptanceRoles = ["Geschäftsführung"] });
        var high = Result("NET-01", ResultStatus.Fail, Severity.High);
        var risk = Exception("EX-0001", "NET-01", All, category: ReasonCategory.RiskAccepted).Approve(role: "Anlagenverantwortlicher");
        Assert.Equal(ResultFlags.ExceptionPending, Apply(high, Set([risk]), plant: plant).Flags);
        Assert.Equal(ResultStatus.Exception, Apply(high, Set([risk])).Status);
        var tooLong = Exception("EX-0001", "NET-08", All).Approve(Today.AddMonths(-1), Today.AddMonths(40));
        Assert.Equal(ResultStatus.Fail, Apply(Failing, Set([tooLong])).Status);
    }

    [Fact]
    public void Genauester_Bereich_gewinnt_bei_Gleichstand_die_juengste_Freigabe()
    {
        var plantWide = Exception("EX-0001", "NET-08", All, accepted: "*").Approve();
        var group = Exception("EX-0002", "NET-08", Groups("g-hmi"), accepted: "*").Approve();
        var machine = Exception("EX-0003", "NET-08", Machines("hmi-test"), accepted: "5").Approve();
        // Rechner vor Gruppe vor Anlage – der genaueste entscheidet, auch wenn sein Wert nicht passt
        var both = Apply(Failing, Set([plantWide, group, machine]));
        Assert.Equal("EX-0003", both.ExceptionId);
        Assert.Equal(ResultFlags.ExceptionValueMismatch, both.Flags);
        Assert.Equal("EX-0002", Apply(Failing, Set([plantWide, group])).ExceptionId);
        Assert.Equal("EX-0001", Apply(Failing, Set([plantWide, group]), host: "W11-TEST").ExceptionId);

        var older = Exception("EX-0004", "NET-08", All, accepted: "*").Approve(Today.AddMonths(-3));
        var newer = Exception("EX-0005", "NET-08", All, accepted: "*").Approve(Today.AddMonths(-2));
        Assert.Equal("EX-0005", Apply(Failing, Set([newer, older])).ExceptionId);
        Assert.Equal("EX-0005", Apply(Failing, Set([older, newer])).ExceptionId);
    }

    [Fact]
    public void Ausnahmen_wirken_nur_auf_nicht_erfuellte_Ergebnisse()
    {
        var registers = Set([Exception("EX-0001", "NET-08", All, accepted: "*").Approve()]);
        foreach (var status in new[] { ResultStatus.Pass, ResultStatus.NotCheckable, ResultStatus.NotApplicable, ResultStatus.Info })
        {
            var result = Apply(Failing with { Status = status }, registers);
            Assert.Equal(status, result.Status);
            Assert.Null(result.ExceptionId);
        }
        Assert.Equal(ResultStatus.Fail, Apply(Result("PS-02", ResultStatus.Fail), registers).Status);
    }

    [Fact]
    public void Zuschnitt_gueltig_ergibt_outOfScope_beantragt_oder_abgelaufen_TailoringPending()
    {
        var tailoring = Tailoring("TL-0001", "NET-08", Groups("g-hmi"));
        var valid = Apply(Failing, Set(tailoring: [tailoring.Approve()]));
        Assert.Equal(ResultStatus.OutOfScope, valid.Status);
        Assert.Equal("TL-0001", valid.TailoringId);
        Assert.Equal(ResultStatus.Fail, Apply(Failing, Set(tailoring: [tailoring.Approve()]), host: "W11-TEST").Status);

        var pending = Apply(Failing, Set(tailoring: [tailoring]));
        Assert.Equal(ResultStatus.Fail, pending.Status);
        Assert.Equal(ResultFlags.TailoringPending, pending.Flags);

        var expired = Apply(Failing with { Status = ResultStatus.Pass }, Set(tailoring: [tailoring.Approve(Today.AddMonths(-13), Today.AddDays(-1))]));
        Assert.Equal(ResultStatus.Pass, expired.Status);
        Assert.Equal(ResultFlags.TailoringPending, expired.Flags);

        // Zuschnitt geht vor Ausnahme
        var both = Apply(Failing, Set([Exception("EX-0001", "NET-08", All, accepted: "*").Approve()], [tailoring.Approve()]));
        Assert.Equal(ResultStatus.OutOfScope, both.Status);
        Assert.Null(both.ExceptionId);
    }

    [Fact]
    public void Massnahmen_geplant_ueberfaellig_erledigt_verworfen()
    {
        var planned = Apply(Failing, Set(measures: [Measure("M-0001", "NET-08", All, Today)]));
        Assert.Equal(ResultFlags.MeasurePlanned, planned.Flags);
        Assert.Equal("M-0001", planned.MeasureId);

        var overdue = Apply(Failing, Set(measures: [Measure("M-0001", "NET-08", All, Today.AddDays(-1))]));
        Assert.Equal(ResultFlags.MeasureOverdue, overdue.Flags);

        var done = Apply(Failing, Set(measures: [Measure("M-0001", "NET-08", All, Today.AddDays(-1), MeasureState.Done)]));
        Assert.Equal(ResultFlags.None, done.Flags);
        Assert.Equal("M-0001", done.MeasureId);

        var dropped = Apply(Failing, Set(measures: [Measure("M-0001", "NET-08", All, Today.AddDays(-1), MeasureState.Dropped)]));
        Assert.Null(dropped.MeasureId);

        // geplant vor erledigt, genauester Bereich, früheste Fälligkeit
        var chosen = Apply(Failing, Set(measures:
        [
            Measure("M-0001", "NET-08", All, Today.AddDays(5), MeasureState.Done),
            Measure("M-0002", "NET-08", All, Today.AddDays(9)),
            Measure("M-0003", "NET-08", Machines("HMI-TEST"), Today.AddDays(20)),
        ]));
        Assert.Equal("M-0003", chosen.MeasureId);

        // bewusst offen: Maßnahme sichtbar, aber nicht „geplant“ markiert
        var excepted = Apply(Failing, Set([Exception("EX-0001", "NET-08", All, accepted: "*").Approve()], measures: [Measure("M-0001", "NET-08", All, Today.AddDays(-1))]));
        Assert.Equal(ResultStatus.Exception, excepted.Status);
        Assert.Equal("M-0001", excepted.MeasureId);
        Assert.Equal(ResultFlags.None, excepted.Flags);
    }

    [Fact]
    public void Erneutes_Anwenden_ist_stabil_und_Engine_Flags_bleiben()
    {
        var services = Services();
        var registers = Set([Exception("EX-0001", "NET-08", All, accepted: "*").Approve()], measures: [Measure("M-0001", "PS-02", All, Today.AddDays(-1))]);
        IReadOnlyList<RuleResult> raw =
        [
            Result("PS-02", ResultStatus.Fail, flags: ResultFlags.FromDefault), Failing,
        ];
        var once = services.Registers.Apply(raw, registers, WorkflowKit.Plant(), "HMI-TEST", Today);
        var twice = services.Registers.Apply(once, registers, WorkflowKit.Plant(), "HMI-TEST", Today);
        Assert.Equal(ToJson(once.ToList()), ToJson(twice.ToList()));
        Assert.Equal(["NET-08", "PS-02"], once.Select(x => x.RuleId));
        Assert.Equal(ResultFlags.FromDefault | ResultFlags.MeasureOverdue, once[1].Flags);

        // Zuschnitt fällt weg: Rohstatus unbekannt → nicht prüfbar mit Grund
        var tailored = services.Registers.Apply([Failing], Set(tailoring: [Tailoring("TL-0001", "NET-08", All).Approve()]), WorkflowKit.Plant(), "HMI-TEST", Today);
        var after = services.Registers.Apply(tailored, Set(), WorkflowKit.Plant(), "HMI-TEST", Today).Single();
        Assert.Equal(ResultStatus.NotCheckable, after.Status);
        Assert.Equal(RglCodesWorkflow.RawStatusUnknown, after.ReasonCode);
    }
}
