// SPDX-License-Identifier: MIT
using Riegel.Core.Facts;
using Riegel.Core.Model;

namespace Riegel.Core.Evaluation;

/// <summary>Ist die Uhr des Zielrechners plausibel (spec 2.8 ClockSuspect)? Mit den Gründen für die Details.</summary>
internal sealed record ClockAssessment(bool Suspect, IReadOnlyList<string> Reasons)
{
    public static ClockAssessment Plausible { get; } = new(false, []);
}

/// <summary>
/// Zeitbasis der Auswertung (spec 2.8). ClockSuspect, wenn CollectedAtUtc mehr als 24 h nach dem Import oder nach
/// der Auswertung liegt, vor dem Veröffentlichungstag des installierten Builds + UBR (ein Tag Spielraum für
/// Zeitzonen) oder vor dem letzten Start. Altersprüfungen werden dann „nicht prüfbar“.
/// Auch für das Paket workflow (RunFlags.ClockSuspect): <see cref="IsSuspect"/>.
/// </summary>
internal static class EvaluationClock
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromHours(24);

    public static bool IsSuspect(Snapshot s, ICatalogue c, EvaluationContext ctx) =>
        Assess(s, c, ctx, FactsDeriver.DeriveDetailed(new EvidenceStore(s), c)).Suspect;

    public static ClockAssessment Assess(Snapshot s, ICatalogue c, EvaluationContext ctx, FactSet facts)
    {
        var collected = s.CollectedAtUtc;
        var reasons = new List<string>();
        if (ctx.ImportedAtUtc is { } imported && collected > imported + Tolerance) reasons.Add("afterImport");
        if (collected > ctx.EvaluatedAtUtc + Tolerance) reasons.Add("afterEvaluation");
        if (facts.Build is { } build && facts.Ubr is { } ubr && BuildLookup.Find(c.Builds, build, facts.Kind) is { } entry
            && BuildLookup.Locate(entry, ubr).Lower is { } lower)
        {
            var released = new DateTimeOffset(lower.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            if (collected < released - Tolerance) reasons.Add("beforeRelease");
        }
        if (facts.LastBootUtc is { } boot && collected < boot) reasons.Add("beforeBoot");
        return reasons.Count == 0 ? ClockAssessment.Plausible : new ClockAssessment(true, reasons);
    }
}

/// <summary>Suche in rules/lifecycle/builds.json.</summary>
internal static class BuildLookup
{
    public static BuildEntry? Find(BuildTable table, int build, string? osKind) => table.Find(build, osKind);

    /// <summary>
    /// Wo liegt <paramref name="ubr"/> in der (aufsteigenden) Liste? Exact = gleiche UBR; Lower = größte UBR darunter
    /// (oder gleich); Higher = kleinste UBR darüber.
    /// </summary>
    public static (BuildRelease? Exact, BuildRelease? Lower, BuildRelease? Higher) Locate(BuildEntry entry, int ubr)
    {
        BuildRelease? exact = null, lower = null, higher = null;
        foreach (var r in entry.Releases.OrderBy(r => r.Ubr))
        {
            if (r.Ubr == ubr) exact = r;
            if (r.Ubr <= ubr) lower = r;
            else if (higher is null) higher = r;
        }
        return (exact, lower, higher);
    }
}
