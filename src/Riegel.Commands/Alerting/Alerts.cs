// SPDX-License-Identifier: MIT
namespace Riegel.Commands.Alerting;

/// <summary>Exitcode-Tokens von check und import (spec 7.7). Die Namen sind invariant.</summary>
internal enum AlertToken
{
    /// <summary>Verschlechtert gegenüber dem vorigen Lauf oder dem freigegebenen Stand (spec 2.7).</summary>
    Regression,

    /// <summary>Offener hoher Befund, der im vorigen Lauf nicht offen war (auch neue Regel, vorher nicht prüfbar).</summary>
    NewHigh,

    High,
    Medium,
    Low,

    /// <summary>Abgelaufene Ausnahme.</summary>
    Expired,

    /// <summary>Ausnahme läuft innerhalb von warnDays ab.</summary>
    Expiring,

    /// <summary>Ausnahme oder Zuschnitt beantragt, noch nicht (wieder) freigegeben.</summary>
    Pending,

    /// <summary>Maßnahme überfällig.</summary>
    Overdue,

    /// <summary>Prüfumfang unter 90 % oder nicht als Administrator erfasst.</summary>
    Incomplete,

    /// <summary>Export älter als staleDays.</summary>
    Stale,

    /// <summary>Uhr des Zielrechners unplausibel (ClockSuspect).</summary>
    Clock,
}

/// <summary>Alarmart (spec 7.7): Veränderung (Standard mit --plant) oder Zustand (Standard ohne).</summary>
internal enum AlertMode { Change, State }

/// <summary>Namen, Listen und Standardlisten der Tokens.</summary>
internal static class AlertTokens
{
    public static IReadOnlyList<AlertToken> All { get; } = Enum.GetValues<AlertToken>();

    /// <summary>Invarianter Name, z. B. „newhigh“.</summary>
    public static string Name(AlertToken token) => token.ToString().ToLowerInvariant();

    public static AlertToken? Parse(string name) =>
        All.Select(t => (AlertToken?)t).FirstOrDefault(t => string.Equals(Name(t!.Value), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Liste aus <c>--critical</c>/<c>--warning</c>: durch Kommas (oder Leerzeichen) getrennt; „none“ oder leer = keine.
    /// Unbekannte Namen: RGL-0452.
    /// </summary>
    public static IReadOnlyList<AlertToken> ParseList(string option, string value)
    {
        var parts = value.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts is ["none"] || parts.All(p => p.Equals("none", StringComparison.OrdinalIgnoreCase))) return [];
        var result = new List<AlertToken>();
        foreach (var part in parts)
        {
            var token = Parse(part) ?? throw RglCodesCli.Error(RglCodesCli.InvalidValue, option, part,
                "none, " + string.Join(", ", All.Select(Name)));
            if (!result.Contains(token)) result.Add(token);
        }
        return result;
    }

    /// <summary>Kritisch nach Alarmart (spec 7.7).</summary>
    public static IReadOnlyList<AlertToken> DefaultCritical(AlertMode mode) => mode == AlertMode.Change
        ? [AlertToken.Regression, AlertToken.NewHigh]
        : [AlertToken.Regression, AlertToken.High];

    /// <summary>Warnung nach Alarmart (spec 7.7). Bei „change“ bleiben stehende Befunde außen vor.</summary>
    public static IReadOnlyList<AlertToken> DefaultWarning(AlertMode mode) => mode == AlertMode.Change
        ? [AlertToken.Expired, AlertToken.Expiring, AlertToken.Pending, AlertToken.Overdue, AlertToken.Incomplete, AlertToken.Stale, AlertToken.Clock]
        : [AlertToken.Medium, AlertToken.Low, AlertToken.Expired, AlertToken.Expiring, AlertToken.Pending, AlertToken.Overdue,
           AlertToken.Incomplete, AlertToken.Stale, AlertToken.Clock];
}

/// <summary>Zählungen der Tokens eines Laufs; ein Token „feuert“, wenn seine Zahl größer 0 ist.</summary>
internal sealed class TokenCounts
{
    private readonly int[] _counts = new int[AlertTokens.All.Count];

    public int this[AlertToken token]
    {
        get => _counts[(int)token];
        set => _counts[(int)token] = Math.Max(0, value);
    }

    public bool Fired(AlertToken token) => this[token] > 0;

    /// <summary>Alle Tokens mit Zahl in fester Reihenfolge (JSON).</summary>
    public IEnumerable<(AlertToken Token, int Count)> Items => AlertTokens.All.Select(t => (t, this[t]));
}

/// <summary>Ergebnis der Bewertung: Exitcode und welche Tokens ihn ausgelöst haben.</summary>
internal sealed record AlertOutcome(int ExitCode, IReadOnlyList<AlertToken> FiredCritical, IReadOnlyList<AlertToken> FiredWarning)
{
    public static AlertOutcome Unknown { get; } = new(CommandApp.Unknown, [], []);

    /// <summary>Nagios-Statuswort (invariant): OK, WARNING, CRITICAL, UNKNOWN.</summary>
    public string State => StateWord(ExitCode);

    public static string StateWord(int exitCode) => exitCode switch
    {
        CommandApp.Ok => "OK",
        CommandApp.Warning => "WARNING",
        CommandApp.Critical => "CRITICAL",
        _ => "UNKNOWN",
    };
}

/// <summary>Kritisch- und Warnliste nach Alarmart; <c>--critical</c>/<c>--warning</c> ersetzen die Listen (spec 7.7).</summary>
internal sealed record AlertPolicy(AlertMode Mode, IReadOnlyList<AlertToken> Critical, IReadOnlyList<AlertToken> Warning)
{
    public static AlertPolicy For(AlertMode mode, IReadOnlyList<AlertToken>? critical = null, IReadOnlyList<AlertToken>? warning = null) =>
        new(mode, critical ?? AlertTokens.DefaultCritical(mode), warning ?? AlertTokens.DefaultWarning(mode));

    /// <summary>2, wenn ein kritisches Token feuert; 1, wenn ein Warn-Token feuert; sonst 0. Kritisch gewinnt.</summary>
    public AlertOutcome Evaluate(TokenCounts counts)
    {
        var critical = Critical.Where(counts.Fired).ToList();
        var warning = Warning.Where(t => counts.Fired(t) && !critical.Contains(t)).ToList();
        int exit = critical.Count > 0 ? CommandApp.Critical : warning.Count > 0 ? CommandApp.Warning : CommandApp.Ok;
        return new AlertOutcome(exit, critical, warning);
    }

    public string ModeName => Mode == AlertMode.Change ? "change" : "state";
}
