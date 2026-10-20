// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Commands.Output;

namespace Riegel.Commands.Alerting;

/// <summary>
/// Eine Zeile für Icinga/Nagios (O11, <c>check … --brief</c>):
/// <c>RIEGEL WARNING - HMI-01: 0 worse, 3 open (1 high), 1 exception expiring, 1 measure overdue | score=72%;;;0;100
/// open=3 high=1 regressions=0 expiring=1 overdue=1 notcheckable=4</c>.
/// „RIEGEL“, das Statuswort und die Leistungsdaten (nach „|“) sind invariant, der Text dazwischen ist übersetzt.
/// Weitere Teile („1 newly open high“, „incomplete check“ …) erscheinen nur, wenn ihr Token zählt – so erklärt die
/// Zeile jeden Exitcode; das Beispiel aus O11 bleibt Zeichen für Zeichen gleich.
/// </summary>
internal static class BriefLine
{
    public static string Format(RunAssessment a)
    {
        var inv = CultureInfo.InvariantCulture;
        var c = a.Counts;
        int open = a.Open;
        int high = c[AlertToken.High];
        int worse = c[AlertToken.Regression];
        var parts = new List<string>
        {
            Plural.Format("Brief_Worse", worse, inv),
            Plural.Format("Brief_Open", open, inv, Plural.Format("Brief_High", high, inv)),
            Plural.Format("Brief_Expiring", c[AlertToken.Expiring], inv),
            Plural.Format("Brief_Overdue", c[AlertToken.Overdue], inv),
        };
        if (c.Fired(AlertToken.NewHigh)) parts.Add(Plural.Format("Brief_NewHigh", c[AlertToken.NewHigh], inv));
        if (c.Fired(AlertToken.Expired)) parts.Add(Plural.Format("Brief_Expired", c[AlertToken.Expired], inv));
        if (c.Fired(AlertToken.Pending)) parts.Add(Plural.Format("Brief_Pending", c[AlertToken.Pending], inv));
        if (c.Fired(AlertToken.Incomplete)) parts.Add(Resources.CliStrings.Brief_Incomplete);
        if (c.Fired(AlertToken.Stale)) parts.Add(Resources.CliStrings.Brief_Stale);
        if (c.Fired(AlertToken.Clock)) parts.Add(Resources.CliStrings.Brief_Clock);

        var s = a.Run.Summary;
        var score = s.Score is { } value ? value.ToString(inv) + "%" : "U";
        var perf = string.Create(inv,
            $"score={score};;;0;100 open={open} high={high} regressions={worse} expiring={c[AlertToken.Expiring]} overdue={c[AlertToken.Overdue]} notcheckable={s.NotCheckable}");
        return $"RIEGEL {a.Outcome.State} - {a.Run.HostName}: {string.Join(", ", parts)} | {perf}";
    }

    /// <summary>
    /// <c>import --brief</c> ohne neue Exporte (alle schon mit Lauf in der Anlage, wiederkehrender Import eines Ordners):
    /// <c>RIEGEL OK - &lt;Anlage&gt;: no new exports (2 already in the plant) | imported=0 skipped=2</c> – sonst bekäme
    /// Icinga/Nagios eine leere Ausgabe (unabhängige Prüfung vom 2026-10-20).
    /// </summary>
    public static string NothingNew(string plant, int skipped)
    {
        var inv = CultureInfo.InvariantCulture;
        return string.Create(inv, $"RIEGEL OK - {Clean(plant)}: {Plural.Format("Brief_NothingNew", skipped, inv)} | imported=0 skipped={skipped}");
    }

    /// <summary>Zeile bei einem Fehler (Exitcode 3): <c>RIEGEL UNKNOWN - &lt;Gegenstand&gt;: RGL-nnnn: Text</c>.</summary>
    public static string Unknown(string subject, string code, string message) =>
        // „|“ leitet bei Icinga die Leistungsdaten ein und hat im Text nichts zu suchen
        $"RIEGEL UNKNOWN - {subject}: {code}: {Clean(message)}";

    private static string Clean(string text) => text.Replace('|', '/').ReplaceLineEndings(" ");
}
