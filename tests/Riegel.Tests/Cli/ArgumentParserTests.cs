// SPDX-License-Identifier: MIT
using Riegel.Commands;
using Riegel.Commands.Alerting;
using Riegel.Commands.Parsing;
using Riegel.Core.Model;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Cli;

/// <summary>Zerlegen der Argumente ohne fremde Bibliothek: Schreibweisen, Fehler mit RGL-Code, Listen und Hilfe überall.</summary>
public sealed class ArgumentParserTests
{
    private static readonly OptionSpec[] Options =
    [
        new("--plant", OptionKind.Value),
        new("--brief", OptionKind.Flag),
        new("--machine", OptionKind.List, SplitCommas: true),
        new("--snapshot", OptionKind.List),
    ];

    private static string CodeOf(Action action) => Assert.Throws<RiegelException>(action).Code;

    [Fact]
    public void Werte_Schalter_Listen_und_freie_Argumente()
    {
        var a = ArgumentParser.Parse("x", ["frei", "--PLANT=C:\\Anlage", "--brief", "--machine", "A,B", "C", "--snapshot", "a b.json", "c,d.json"],
            Options, maxPositional: 1);
        Assert.Equal("C:\\Anlage", a.Value("--plant"));
        Assert.True(a.Has("--brief"));
        Assert.Equal(["A", "B", "C"], a.Values("--machine"));
        Assert.Equal(["a b.json", "c,d.json"], a.Values("--snapshot"));
        Assert.Equal(["frei"], a.Positional);
        Assert.Null(a.Value("--gibtsnicht"));
    }

    [Fact]
    public void Listen_sind_wiederholbar_und_enden_an_der_naechsten_Option()
    {
        var a = ArgumentParser.Parse("x", ["--machine", "A", "--brief", "--machine", "B"], Options);
        Assert.Equal(["A", "B"], a.Values("--machine"));
        Assert.True(a.Has("--brief"));
    }

    [Fact]
    public void Fehler_haben_Codes_aus_dem_Bereich_der_Befehlszeile()
    {
        Assert.Equal(RglCodesCli.UsageError, CodeOf(() => ArgumentParser.Parse("x", ["--unbekannt"], Options)));
        Assert.Equal(RglCodesCli.UsageError, CodeOf(() => ArgumentParser.Parse("x", ["zuviel"], Options)));
        Assert.Equal(RglCodesCli.UsageError, CodeOf(() => ArgumentParser.Parse("x", ["--brief=ja"], Options)));
        Assert.Equal(RglCodesCli.MissingValue, CodeOf(() => ArgumentParser.Parse("x", ["--plant"], Options)));
        Assert.Equal(RglCodesCli.MissingValue, CodeOf(() => ArgumentParser.Parse("x", ["--plant", "--brief"], Options)));
        Assert.Equal(RglCodesCli.MissingValue, CodeOf(() => ArgumentParser.Parse("x", ["--machine", "--brief"], Options)));
        Assert.Equal(RglCodesCli.ConflictingOptions, CodeOf(() => ArgumentParser.Parse("x", ["--plant", "a", "--plant", "b"], Options)));
        var a = ArgumentParser.Parse("x", ["--plant", "a"], Options);
        Assert.Equal(RglCodesCli.MissingOption, CodeOf(() => a.Required("--machine", "<host>")));
    }

    [Fact]
    public void Datum_Zahl_und_Auswahl_werden_geprueft()
    {
        var specs = new OptionSpec[] { new("--date", OptionKind.Value), new("--days", OptionKind.Value), new("--alert", OptionKind.Value) };
        var ok = ArgumentParser.Parse("x", ["--date", "2026-12-31", "--days", "14", "--alert", "STATE"], specs);
        Assert.Equal(new DateOnly(2026, 12, 31), ok.Date("--date"));
        Assert.Equal(14, ok.Int("--days", 0, 3650));
        Assert.Equal("state", ok.Choice("--alert", "change", "state"));
        var bad = ArgumentParser.Parse("x", ["--date", "31.12.2026", "--days", "-1", "--alert", "immer"], specs);
        Assert.Equal(RglCodesCli.InvalidValue, CodeOf(() => bad.Date("--date")));
        Assert.Equal(RglCodesCli.InvalidValue, CodeOf(() => bad.Int("--days", 0, 3650)));
        Assert.Equal(RglCodesCli.InvalidValue, CodeOf(() => bad.Choice("--alert", "change", "state")));
    }

    [Theory]
    [InlineData("regression,newhigh", "regression,newhigh")]
    [InlineData(" High , LOW ", "high,low")]
    [InlineData("medium;medium", "medium")]
    [InlineData("none", "")]
    [InlineData("", "")]
    public void Tokenlisten(string value, string expected) =>
        Assert.Equal(expected, string.Join(",", AlertTokens.ParseList("--critical", value).Select(AlertTokens.Name)));

    [Fact]
    public void Hilfe_an_beliebiger_Stelle_und_nur_lesen_an_beliebiger_Stelle()
    {
        var help = Cli.Run("report", "--plant", "x", "--hilfe");
        Assert.Equal(0, help.Exit);
        Assert.Contains("report --plant", help.Out, StringComparison.Ordinal);
        Assert.Equal(0, Cli.Run("exceptions", "approve", "-h").Exit);
        Assert.Equal(0, Cli.Run("/?").Exit);

        using var temp = new TempFolder();
        var target = Path.Combine(temp.Path, "werk");
        Assert.Equal(3, Cli.Run("plant", "init", target, "--name", "x", "--NUR-LESEN").Exit);
        Assert.False(Directory.Exists(target));
    }
}
