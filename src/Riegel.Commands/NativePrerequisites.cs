// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using Riegel.Commands.Alerting;
using Riegel.Commands.Parsing;
using Riegel.Core.Localization;
using Riegel.Core.Model;

namespace Riegel.Commands;

/// <summary>
/// Bibliotheken, die die .NET-Laufzeit unter Linux vom System nimmt, auch im eigenständigen Einzeldatei-Programm:
/// ICU (Kulturdaten) und OpenSSL (SHA-256). Fehlen sie (z. B. in debian:12-slim), bricht die Laufzeit sonst beim ersten
/// Zugriff ab – Exitcode 134, kein Nagios-Code, keine verständliche Meldung (Prüfung vom 2026-10-20).
/// <list type="bullet">
/// <item>ICU fehlt: <see cref="Prepare"/> schaltet vor dem ersten Zugriff auf Kulturdaten in den invarianten Modus. Texte
/// bleiben übersetzt (Sprache aus LANG, <see cref="LanguageFromEnvironment"/>), Datum und Zahlen erscheinen in
/// invarianter Form; Hinweis RGL-0017 auf stderr.</item>
/// <item>OpenSSL fehlt: Fehler RGL-0018 mit den Paketnamen, Exitcode 3 (außer bei der Hilfe; schon --version lädt den
/// Katalog und bildet seinen SHA-256).</item>
/// </list>
/// </summary>
public static class NativePrerequisites
{
    /// <summary>Was fehlt.</summary>
    public sealed record State(bool IcuMissing, bool SslMissing)
    {
        public static State None { get; } = new(false, false);
    }

    /// <summary>
    /// Bibliotheksnamen, unter denen die Laufzeit ICU sucht (libicuuc.so.&lt;Version&gt;, 50 bis 99). Die Namen entstehen
    /// ohne Zahlenformatierung – die bräuchte schon Kulturdaten.
    /// </summary>
    internal static IEnumerable<string> IcuNames()
    {
        for (int v = 99; v >= 50; v--) yield return "libicuuc.so." + new string([(char)('0' + v / 10), (char)('0' + v % 10)]);
        yield return "libicuuc.so";
    }

    /// <summary>Bibliotheksnamen, unter denen die Laufzeit OpenSSL sucht.</summary>
    internal static readonly string[] SslNames = ["libssl.so.3", "libssl.so.1.1", "libssl.so.1.0.2", "libssl.so.1.0.0", "libssl.so.10", "libssl.so"];

    /// <summary>
    /// Nur unter Linux, als Erstes in Main (vor jedem Zugriff auf CultureInfo): prüft ICU und OpenSSL und schaltet bei
    /// fehlendem ICU in den invarianten Modus. Ist der invariante Modus schon gesetzt (DOTNET_SYSTEM_GLOBALIZATION_INVARIANT)
    /// oder eine ICU-Version vorgegeben (CLR_ICU_VERSION_OVERRIDE), bleibt ICU ungeprüft.
    /// </summary>
    public static State Prepare()
    {
        if (!OperatingSystem.IsLinux()) return State.None;
        bool icuGiven = Environment.GetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT") is { Length: > 0 }
                        || Environment.GetEnvironmentVariable("CLR_ICU_VERSION_OVERRIDE") is { Length: > 0 };
        var state = Probe(CanLoad, checkIcu: !icuGiven);
        if (state.IcuMissing)
        {
            AppContext.SetSwitch("System.Globalization.Invariant", true);
            // Sonst ließen sich im invarianten Modus keine Kulturen wie „de“ anlegen – die braucht die Sprachwahl
            AppContext.SetSwitch("System.Globalization.PredefinedCulturesOnly", false);
        }
        return state;
    }

    /// <summary>Prüft mit <paramref name="canLoad"/>, ob sich eine der bekannten Bibliotheken laden lässt.</summary>
    internal static State Probe(Func<string, bool> canLoad, bool checkIcu = true) =>
        new(checkIcu && !IcuNames().Any(canLoad), !SslNames.Any(canLoad));

    /// <summary>
    /// Nach der Sprachwahl: Hinweis bei fehlendem ICU, Fehler bei fehlendem OpenSSL. Gibt 3 zurück, wenn der Befehl
    /// nicht laufen kann (mit --brief zusätzlich die UNKNOWN-Zeile für Icinga), sonst null.
    /// </summary>
    public static int? Report(State state, IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(args);
        if (state.IcuMissing) error.WriteLine($"{RglCodesCommon.IcuMissing}: {RglCodesCommon.Message(RglCodesCommon.IcuMissing)}");
        if (!state.SslMissing) return null;
        // Die Hilfe braucht keine Prüfsummen; so lässt sich das Programm auch ohne OpenSSL erkunden
        var first = args.FirstOrDefault(a => !string.Equals(a, "--read-only", StringComparison.OrdinalIgnoreCase)
                                             && !string.Equals(a, "--nur-lesen", StringComparison.OrdinalIgnoreCase));
        if (first is null || args.Any(ArgumentParser.IsHelp)) return null;
        var message = RglCodesCommon.Message(RglCodesCommon.OpenSslMissing);
        if (args.Any(a => string.Equals(a, "--brief", StringComparison.OrdinalIgnoreCase)))
            output.WriteLine(BriefLine.Unknown(first, RglCodesCommon.OpenSslMissing, message));
        error.WriteLine($"{RglCodesCommon.OpenSslMissing}: {message}");
        return CommandApp.Unknown;
    }

    /// <summary>
    /// Ohne ICU kennt .NET die Sprache des Systems nicht. Dann gilt LC_ALL, sonst LC_MESSAGES, sonst LANG
    /// („de_DE.UTF-8“ → „de“, „zh_CN.UTF-8“ → „zh-Hans“); null bei C/POSIX oder einer Sprache ohne Sprachdatei.
    /// </summary>
    public static string? LanguageFromEnvironment(Func<string, string?>? variable = null)
    {
        variable ??= Environment.GetEnvironmentVariable;
        foreach (var name in new[] { "LC_ALL", "LC_MESSAGES", "LANG" })
        {
            var value = variable(name);
            if (string.IsNullOrEmpty(value)) continue;
            var tag = value.Split('.', '@')[0].Replace('_', '-');
            var parts = tag.Split('-');
            if (string.Equals(parts[0], "zh", StringComparison.OrdinalIgnoreCase))
                return parts.Length > 1 && (parts[1] is "CN" or "SG" or "Hans" or "cn" or "sg" or "hans") ? "zh-Hans" : null;
            return UiLanguage.Supported.FirstOrDefault(s => string.Equals(s, parts[0], StringComparison.OrdinalIgnoreCase));
        }
        return null;
    }

    private static bool CanLoad(string name)
    {
        if (!NativeLibrary.TryLoad(name, out var handle)) return false;
        NativeLibrary.Free(handle);
        return true;
    }
}
