// SPDX-License-Identifier: MIT
using Riegel.Commands;
using Riegel.Commands.Alerting;
using Riegel.Core.Model;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Cli;

/// <summary>
/// Exitcode-Matrix (Paket cli, Abnahme): jedes Token in beiden Alarmarten, Ersetzen der Listen und die
/// Standard-Alarmart mit und ohne Anlage. Die Erwartung kommt aus einer eigenen Abschrift von spec 7.7 hier im Test,
/// nicht aus dem Code.
/// </summary>
public sealed class ExitCodeMatrixTests
{
    /// <summary>spec 7.7, abgeschrieben.</summary>
    private static readonly Dictionary<string, (string[] Critical, string[] Warning)> Spec = new()
    {
        ["change"] = (["regression", "newhigh"], ["expired", "expiring", "pending", "overdue", "incomplete", "stale", "clock"]),
        ["state"] = (["regression", "high"], ["medium", "low", "expired", "expiring", "pending", "overdue", "incomplete", "stale", "clock"]),
    };

    private static readonly string[] Tokens =
        ["regression", "newhigh", "high", "medium", "low", "expired", "expiring", "pending", "overdue", "incomplete", "stale", "clock"];

    private static int Expected(string mode, IEnumerable<string> fired)
    {
        var (critical, warning) = Spec[mode];
        var list = fired.ToList();
        return list.Any(critical.Contains) ? 2 : list.Any(warning.Contains) ? 1 : 0;
    }

    public static TheoryData<string, string> TokenAndMode()
    {
        var data = new TheoryData<string, string>();
        foreach (var token in Tokens)
            foreach (var mode in Spec.Keys)
                data.Add(token, mode);
        return data;
    }

    [Fact]
    public void Die_Tokennamen_sind_genau_die_aus_spec_7_7() =>
        Assert.Equal(Tokens, AlertTokens.All.Select(AlertTokens.Name));

    [Theory]
    [MemberData(nameof(TokenAndMode))]
    public void Ein_einzelnes_Token_ergibt_den_Exitcode_seiner_Liste(string token, string mode)
    {
        var counts = new TokenCounts { [AlertTokens.Parse(token)!.Value] = 1 };
        var policy = AlertPolicy.For(mode == "change" ? AlertMode.Change : AlertMode.State);
        Assert.Equal(Expected(mode, [token]), policy.Evaluate(counts).ExitCode);
    }

    [Theory]
    [MemberData(nameof(TokenAndMode))]
    public void Check_endet_mit_dem_Exitcode_des_Tokens(string token, string mode)
    {
        var scenario = Scenario(token);
        using var plant = new TempPlant();
        foreach (var previous in scenario.Previous) TestRuns.Store(plant, previous);
        using var temp = new TempFolder();
        var snapshot = SnapshotBuilder.Windows11Workgroup(Host).WriteTo(temp.Path);
        var cli = Cli.With(runs: new FakeRunService(scenario.Run));

        var result = Cli.Run(cli, "check", "--plant", plant.Folder, "--snapshot", snapshot, "--no-store", "--alert", mode, "--format", "json");

        Assert.True(Expected(mode, scenario.Fired) == result.Exit, result.ToString());
        var machine = result.Json().GetProperty("machines")[0];
        var fired = machine.GetProperty("tokens").EnumerateObject().Where(p => p.Value.GetInt32() > 0).Select(p => p.Name).Order().ToList();
        Assert.Equal(scenario.Fired.Order(), fired);
        Assert.False(machine.GetProperty("stored").GetBoolean());
    }

    [Fact]
    public void Critical_und_warning_ersetzen_die_Listen()
    {
        // offener mittlerer Befund ohne vorigen Lauf: feuert nur „medium“
        Assert.Equal(0, RunCheck(Medium(), "--alert", "change"));
        Assert.Equal(1, RunCheck(Medium(), "--alert", "state"));
        Assert.Equal(2, RunCheck(Medium(), "--alert", "change", "--critical", "medium"));
        Assert.Equal(1, RunCheck(Medium(), "--alert", "change", "--warning", "low,medium"));
        Assert.Equal(0, RunCheck(Medium(), "--alert", "state", "--critical", "none", "--warning", "none"));
        Assert.Equal(0, RunCheck(Medium(), "--alert", "state", "--warning", ""));
        // kritisch gewinnt, wenn ein Token in beiden Listen steht
        Assert.Equal(2, RunCheck(Medium(), "--critical", "medium", "--warning", "medium"));
    }

    [Fact]
    public void Unbekanntes_Token_ist_ein_Aufruffehler()
    {
        var result = Cli.Run("check", "--snapshot", "x.json", "--critical", "regression,gibtsnicht");
        Assert.Equal(3, result.Exit);
        Assert.Contains(RglCodesCli.InvalidValue, result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_Anlage_gilt_state_mit_Anlage_change()
    {
        using var culture = new UiCulture("en");
        // stehender hoher Befund, im vorigen Lauf schon offen: Zustand → kritisch, Veränderung → OK
        var scenario = Scenario("high");
        using var plant = new TempPlant();
        foreach (var previous in scenario.Previous) TestRuns.Store(plant, previous);
        using var temp = new TempFolder();
        var snapshot = SnapshotBuilder.Windows11Workgroup(Host).WriteTo(temp.Path);

        var withPlant = Cli.Run(Cli.With(runs: new FakeRunService(scenario.Run)), "check", "--plant", plant.Folder, "--snapshot", snapshot, "--no-store");
        Assert.True(withPlant.Exit == 0, withPlant.ToString());
        Assert.Contains("Alert mode change", withPlant.Out, StringComparison.Ordinal);

        var withoutPlant = Cli.Run(Cli.With(runs: new FakeRunService(scenario.Run)), "check", "--snapshot", snapshot);
        Assert.True(withoutPlant.Exit == 2, withoutPlant.ToString());
    }

    [Fact]
    public void Mehrere_Exporte_ergeben_den_schlechtesten_Exitcode()
    {
        using var temp = new TempFolder();
        var a = SnapshotBuilder.Windows11Workgroup("A-01").WriteTo(temp.Sub("a"));
        var b = SnapshotBuilder.Windows11Workgroup("B-01").WriteTo(temp.Sub("b"));
        var services = Cli.With(runs: new FakeRunService(
            TestRuns.Make("A-01", [TestRuns.Result("SYS-01", ResultStatus.Pass)]),
            TestRuns.Make("B-01", [TestRuns.Result("SYS-01", ResultStatus.Fail, Severity.Low)])));
        var result = Cli.Run(services, "check", "--snapshot", a, b);
        Assert.True(result.Exit == 1, result.ToString());
        Assert.Contains("A-01", result.Out, StringComparison.Ordinal);
        Assert.Contains("B-01", result.Out, StringComparison.Ordinal);
    }

    // ================= Szenarien: ein Lauf, der genau ein Token (und, wo unvermeidlich, die Schwere) auslöst =================

    private const string Host = "HMI-TEST";

    private sealed record ScenarioData(Run Run, Run[] Previous, string[] Fired);

    private static ScenarioData Scenario(string token)
    {
        var earlier = TestRuns.Collected.AddDays(-7);
        switch (token)
        {
            case "regression":
            {
                // vorher erfüllt, jetzt nicht erfüllt (mittel): Verschlechterung aus dem Vergleich
                var previous = TestRuns.Make(Host, [TestRuns.Result("NET-01", ResultStatus.Pass)], collected: earlier);
                var run = TestRuns.Make(Host, [TestRuns.Result("NET-01", ResultStatus.Fail, Severity.Medium)], previousRunId: previous.Id);
                return new(run, [previous], ["regression", "medium"]);
            }
            case "newhigh":
            {
                // vorher nicht prüfbar, jetzt offen und hoch: neu offen, aber keine Verschlechterung (spec 2.7)
                var previous = TestRuns.Make(Host, [TestRuns.Result("CRED-01", ResultStatus.NotCheckable, Severity.High)], collected: earlier);
                var run = TestRuns.Make(Host, [TestRuns.Result("CRED-01", ResultStatus.Fail, Severity.High)], previousRunId: previous.Id);
                return new(run, [previous], ["newhigh", "high"]);
            }
            case "high":
            {
                var previous = TestRuns.Make(Host, [TestRuns.Result("CRED-01", ResultStatus.Fail, Severity.High)], collected: earlier);
                var run = TestRuns.Make(Host, [TestRuns.Result("CRED-01", ResultStatus.Fail, Severity.High)], previousRunId: previous.Id);
                return new(run, [previous], ["high"]);
            }
            case "medium":
                return new(Medium(), [], ["medium"]);
            case "low":
                return new(TestRuns.Make(Host, [TestRuns.Result("SYS-09", ResultStatus.Fail, Severity.Low)]), [], ["low"]);
            case "expired":
                return new(TestRuns.Make(Host, [TestRuns.Result("SYS-01", ResultStatus.Pass)], s => s with { ExceptionsExpired = 1 }), [], ["expired"]);
            case "expiring":
                return new(TestRuns.Make(Host, [TestRuns.Result("RDP-03", ResultStatus.Exception, Severity.Medium, ResultFlags.ExceptionExpiring)]), [],
                    ["expiring"]);
            case "pending":
                return new(TestRuns.Make(Host, [TestRuns.Result("SYS-01", ResultStatus.Pass)], s => s with { ExceptionsPending = 1 }), [], ["pending"]);
            case "overdue":
                return new(TestRuns.Make(Host, [TestRuns.Result("SYS-01", ResultStatus.Pass)], s => s with { MeasuresOverdue = 2 }), [], ["overdue"]);
            case "incomplete":
                return new(TestRuns.Make(Host, [TestRuns.Result("SYS-01", ResultStatus.Pass)], flags: RunFlags.Incomplete), [], ["incomplete"]);
            case "stale":
                return new(TestRuns.Make(Host, [TestRuns.Result("SYS-01", ResultStatus.Pass)], flags: RunFlags.StaleSnapshot), [], ["stale"]);
            case "clock":
                return new(TestRuns.Make(Host, [TestRuns.Result("SYS-01", ResultStatus.Pass)], flags: RunFlags.ClockSuspect), [], ["clock"]);
            default:
                throw new ArgumentOutOfRangeException(nameof(token), token, null);
        }
    }

    private static Run Medium() => TestRuns.Make(Host, [TestRuns.Result("LOGON-02", ResultStatus.Fail, Severity.Medium)]);

    private static int RunCheck(Run run, params string[] options)
    {
        using var temp = new TempFolder();
        var snapshot = SnapshotBuilder.Windows11Workgroup(Host).WriteTo(temp.Path);
        var services = Cli.With(runs: new FakeRunService(run));
        var result = Cli.Run(services, ["check", "--snapshot", snapshot, .. options]);
        Assert.True(result.Exit != CommandApp.Unknown, result.ToString());
        return result.Exit;
    }
}
