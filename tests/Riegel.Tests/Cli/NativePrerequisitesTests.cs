// SPDX-License-Identifier: MIT
using Riegel.Commands;
using Riegel.Core.Model;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Cli;

/// <summary>
/// Linux-Befehlszeile ohne ICU oder OpenSSL (Prüfung vom 2026-10-20: Abbruch mit 134 in debian:12-slim). Die Prüfung
/// selbst läuft in der CI in einem debian:12-slim-Container; hier die Entscheidungen.
/// </summary>
public sealed class NativePrerequisitesTests
{
    [Fact]
    public void Erkennt_fehlende_und_vorhandene_Bibliotheken()
    {
        Assert.Equal(new NativePrerequisites.State(true, true), NativePrerequisites.Probe(_ => false));
        Assert.Equal(NativePrerequisites.State.None, NativePrerequisites.Probe(n => n is "libicuuc.so.72" or "libssl.so.3"));
        Assert.Equal(new NativePrerequisites.State(false, true), NativePrerequisites.Probe(n => n == "libicuuc.so.74"));
        Assert.Equal(new NativePrerequisites.State(true, false), NativePrerequisites.Probe(n => n == "libssl.so.1.1"));
        // ICU ist vorgegeben (invarianter Modus oder feste Version): nicht prüfen
        Assert.False(NativePrerequisites.Probe(_ => false, checkIcu: false).IcuMissing);
        // Namen ohne Zahlenformatierung, alle Versionen der Laufzeit
        var names = NativePrerequisites.IcuNames().ToList();
        Assert.Contains("libicuuc.so.50", names);
        Assert.Contains("libicuuc.so.72", names);
        Assert.Contains("libicuuc.so.99", names);
    }

    [Fact]
    public void Ohne_OpenSSL_RGL_0018_und_Exitcode_3_ausser_bei_der_Hilfe()
    {
        using var _ = new UiCulture("en");
        var missing = new NativePrerequisites.State(false, true);
        var (code, output, error) = Report(missing, "check", "--snapshot", "x.snapshot.json", "--brief");
        Assert.Equal(3, code);
        Assert.StartsWith("RIEGEL UNKNOWN", output, StringComparison.Ordinal);
        Assert.Contains(RglCodesCommon.OpenSslMissing + ": OpenSSL (libssl) was not found", error, StringComparison.Ordinal);
        Assert.Contains("libssl3", error, StringComparison.Ordinal);
        Assert.Equal(3, Report(missing, "--version").Code);
        Assert.Null(Report(missing).Code);
        Assert.Null(Report(missing, "--help").Code);
        Assert.Null(Report(missing, "verify", "--help").Code);
        Assert.Null(Report(NativePrerequisites.State.None, "check").Code);
    }

    [Fact]
    public void Ohne_ICU_ein_Hinweis_und_der_Befehl_laeuft()
    {
        using var _ = new UiCulture("de");
        var (code, output, error) = Report(new NativePrerequisites.State(true, false), "check");
        Assert.Null(code);
        Assert.Equal("", output);
        Assert.StartsWith(RglCodesCommon.IcuMissing + ": ICU (libicu) wurde nicht gefunden", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null, "de_DE.UTF-8", "de")]
    [InlineData("fr_FR.UTF-8", null, "de_DE.UTF-8", "fr")]
    [InlineData(null, "es_ES", "de_DE.UTF-8", "es")]
    [InlineData(null, null, "zh_CN.UTF-8", "zh-Hans")]
    [InlineData(null, null, "zh_TW.UTF-8", null)]
    [InlineData(null, null, "C.UTF-8", null)]
    [InlineData(null, null, "POSIX", null)]
    [InlineData(null, null, "pt_BR.UTF-8", null)]
    [InlineData(null, null, null, null)]
    public void Sprache_aus_LANG_wenn_ICU_fehlt(string? lcAll, string? lcMessages, string? lang, string? expected)
    {
        string? Variable(string name) => name switch { "LC_ALL" => lcAll, "LC_MESSAGES" => lcMessages, "LANG" => lang, _ => null };
        Assert.Equal(expected, NativePrerequisites.LanguageFromEnvironment(Variable));
    }

    private static (int? Code, string Output, string Error) Report(NativePrerequisites.State state, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = NativePrerequisites.Report(state, args, output, error);
        return (code, output.ToString(), error.ToString());
    }
}
