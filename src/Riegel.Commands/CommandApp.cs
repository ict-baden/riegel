// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Commands.Alerting;
using Riegel.Commands.Commands;
using Riegel.Commands.Parsing;
using Riegel.Commands.Resources;
using Riegel.Core.Model;
using Riegel.Core.Services;

namespace Riegel.Commands;

/// <summary>
/// Öffentlicher Einstieg aller Befehle (spec 7.7). Riegel.Cli (Windows und Linux) und die Konsolen-Anbindung von
/// Riegel.exe rufen nur <see cref="Run(string[], IPlatform, TextWriter, TextWriter)"/> auf. Schreibt nur in die beiden
/// Ausgaben, liefert einen Exitcode nach Nagios (0 OK, 1 WARNING, 2 CRITICAL, 3 UNKNOWN) und stürzt nie ab: jeder
/// Fehler wird zu einem RGL-Code auf stderr und Exitcode 3.
/// </summary>
public static class CommandApp
{
    /// <summary>Exitcodes nach Nagios (spec 7.7).</summary>
    public const int Ok = 0, Warning = 1, Critical = 2, Unknown = 3;

    private delegate int Handler(CommandContext ctx, IReadOnlyList<string> args);

    /// <summary>Alle Befehle aus spec 7.7 (Namen invariant Englisch).</summary>
    private static readonly IReadOnlyDictionary<string, Handler> Commands = new Dictionary<string, Handler>(StringComparer.OrdinalIgnoreCase)
    {
        ["check"] = CheckCommand.Run,
        ["collect"] = CollectCommand.Run,
        ["import"] = ImportCommand.Run,
        ["report"] = DocumentCommands.Report,
        ["summary"] = DocumentCommands.Summary,
        ["target-config"] = DocumentCommands.TargetConfig,
        ["collection-record"] = DocumentCommands.CollectionRecord,
        ["compare"] = CompareCommands.Compare,
        ["compare-snapshot"] = CompareCommands.CompareSnapshot,
        ["approve"] = ApproveCommand.Run,
        ["exceptions"] = RegisterCommands.Exceptions,
        ["tailoring"] = RegisterCommands.Tailoring,
        ["measures"] = RegisterCommands.Measures,
        ["remediate"] = MiscCommands.Remediate,
        ["baseline"] = BaselineCommands.Run,
        ["rules"] = MiscCommands.Rules,
        ["verify"] = CompareCommands.Verify,
        ["collector"] = MiscCommands.Collector,
        ["plant"] = MiscCommands.Plant,
    };

    /// <summary>Namen aller Befehle (Hilfe, Doku-Test).</summary>
    internal static IReadOnlyList<string> CommandNames => [.. Commands.Keys];

    /// <summary>Führt einen Befehl aus und gibt den Exitcode zurück; schreibt nur in <paramref name="output"/> und <paramref name="error"/>.</summary>
    /// <param name="platform">Windows: aus Riegel.Platform.Windows.PlatformFactory; Linux und Tests: <see cref="NullPlatform"/>.</param>
    public static int Run(string[] args, IPlatform platform, TextWriter output, TextWriter error) =>
        Run(args, platform, output, error, CancellationToken.None);

    /// <summary>Wie oben, mit Abbruch (Strg+C beim Erfassen dieses PCs).</summary>
    public static int Run(string[] args, IPlatform platform, TextWriter output, TextWriter error, CancellationToken cancel) =>
        Run(new CommandServices(RiegelServices.CreateDefault(platform)), args, output, error, cancel);

    /// <summary>Wie <see cref="Run(string[], IPlatform, TextWriter, TextWriter)"/>, mit eigenen Diensten (Tests: feste Uhr).</summary>
    public static int Run(string[] args, RiegelServices services, TextWriter output, TextWriter error) =>
        Run(new CommandServices(services), args, output, error, CancellationToken.None);

    /// <summary>Einstieg mit austauschbaren Diensten (Tests).</summary>
    internal static int Run(CommandServices services, string[] args, TextWriter output, TextWriter error, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        // --nur-lesen/--read-only gelten überall und an jeder Stelle (Richtlinie 09, spec 7.6)
        bool readOnly = args.Any(IsReadOnlySwitch);
        var rest = args.Where(a => !IsReadOnlySwitch(a)).ToList();
        var ctx = new CommandContext(services, output, error, readOnly, cancel);
        bool brief = rest.Any(a => string.Equals(a, "--brief", StringComparison.OrdinalIgnoreCase));
        try
        {
            if (rest.Count == 0 || ArgumentParser.IsHelp(rest[0])) return InfoCommands.Overview(ctx);
            var command = rest[0].Trim();
            var tail = rest.Skip(1).ToList();
            if (Is(command, "--version")) return Alone(tail, command, () => InfoCommands.Version(ctx));
            if (Is(command, "--info")) return Alone(tail, command, () => InfoCommands.Info(ctx));
            if (Is(command, "--licenses", "--lizenzen")) return Alone(tail, command, () => InfoCommands.Licenses(ctx));
            // Nur die Oberfläche (Riegel.exe) kennt diese; --demo-report ohne Ordner kommt von dort als Aufruffehler an
            if (Is(command, "--demo-report")) throw RglCodesCli.Error(RglCodesCli.MissingValue, command);
            if (Is(command, "--demo", "--screenshots"))
                throw new RiegelException(RglCodesCli.UsageError, string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_GuiOnly, command));

            if (!Commands.TryGetValue(command, out var handler)) throw RglCodesCli.Error(RglCodesCli.UsageError, command);
            if (tail.Any(ArgumentParser.IsHelp)) return InfoCommands.Command(ctx, command);
            return handler(ctx, tail);
        }
        catch (RiegelException e)
        {
            return Fail(ctx, brief, rest, e.Code, e.Message);
        }
        catch (OperationCanceledException)
        {
            return Fail(ctx, brief, rest, RglCodesCli.Cancelled, RglCodesCli.Message(RglCodesCli.Cancelled));
        }
        catch (IOException e)
        {
            return Fail(ctx, brief, rest, RglCodesCommon.IoError, RglCodesCommon.Message(RglCodesCommon.IoError, e.Message));
        }
        catch (UnauthorizedAccessException e)
        {
            return Fail(ctx, brief, rest, RglCodesCommon.AccessDenied, RglCodesCommon.Message(RglCodesCommon.AccessDenied, e.Message));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Programmfehler: Typ und Text, kein Stapel (keine Pfade oder Daten des Kunden in Ausgaben)
            return Fail(ctx, brief, rest, RglCodesCommon.Unexpected,
                RglCodesCommon.Message(RglCodesCommon.Unexpected) + $" ({e.GetType().Name}: {e.Message})");
        }
    }

    /// <summary>Fehler ausgeben: „RGL-nnnn: Text“ auf stderr; mit --brief zusätzlich die UNKNOWN-Zeile für Icinga auf stdout.</summary>
    private static int Fail(CommandContext ctx, bool brief, IReadOnlyList<string> args, string code, string message)
    {
        if (brief) ctx.Out.WriteLine(BriefLine.Unknown(BriefSubject(args), code, message));
        ctx.Err.WriteLine($"{code}: {message}");
        return Unknown;
    }

    /// <summary>Gegenstand der UNKNOWN-Zeile: der Export bei --snapshot, sonst der Befehl.</summary>
    private static string BriefSubject(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count - 1; i++)
            if (string.Equals(args[i], "--snapshot", StringComparison.OrdinalIgnoreCase)) return Path.GetFileName(args[i + 1]);
        return args.Count > 0 ? args[0] : "riegel";
    }

    /// <summary>--version, --info und --licenses stehen allein.</summary>
    private static int Alone(IReadOnlyList<string> tail, string command, Func<int> action)
    {
        if (tail.Count > 0) throw RglCodesCli.Error(RglCodesCli.UsageError, $"{command} {tail[0]}");
        return action();
    }

    private static bool IsReadOnlySwitch(string arg) => Is(arg.Trim(), "--nur-lesen", "--read-only");

    private static bool Is(string arg, params string[] names) =>
        names.Any(n => string.Equals(arg, n, StringComparison.OrdinalIgnoreCase));
}
