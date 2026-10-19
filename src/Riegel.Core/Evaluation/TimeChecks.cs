// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Core.Evaluation;

/// <summary>Prüfarten lifecycle und updateAge (spec 2.3, 2.8).</summary>
internal sealed partial class EvaluationSession
{
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Unterstütztes Betriebssystem: erster Eintrag von rules/lifecycle/windows.json, dessen Art, Build, Editionen und
    /// Installationsart passen; Zeitbasis EvaluationDate; ESU zählt nur, wenn der Anlagenrechner esuCovered hat und
    /// Microsoft ein ESU-Ende nennt. Ein Build älter als alle Einträge seiner Art, deren Support schon endete, ist ein
    /// Befund; sonst ohne Eintrag „nicht prüfbar“ (Regelwerk ergänzen).
    /// </summary>
    private Outcome Lifecycle()
    {
        var today = Context.EvaluationDate;
        if (Facts.Kind is not { } kind || Facts.Build is not { } build)
            return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.ApplicabilityUnknown).With(T["Detail_OsUnknown"]);
        var products = Catalogue.Lifecycle.Products;
        if (products.Count == 0) return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.CatalogueDataMissing).With(T["Detail_NoLifecycleTable"]);
        LifecycleEntry? entry = null;
        foreach (var p in products)
        {
            if (!string.Equals(p.Os, kind, StringComparison.OrdinalIgnoreCase) || p.Build != build) continue;
            bool? match = true;
            if (p.Editions is { Count: > 0 } editions)
                match = Facts.Edition is { } e ? editions.Contains(e, StringComparer.OrdinalIgnoreCase) : null;
            if (match == true && p.InstallationTypes is { Count: > 0 } types)
                match = Facts.InstallationType is { } i ? types.Contains(i, StringComparer.OrdinalIgnoreCase) : null;
            if (match is null) return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.ApplicabilityUnknown).With(T["Detail_EditionUnknown"]);
            if (match == true) { entry = p; break; }
        }
        if (entry is null)
        {
            var sameKind = products.Where(p => string.Equals(p.Os, kind, StringComparison.OrdinalIgnoreCase)).ToList();
            if (sameKind.Count > 0 && build < sameKind.Min(p => p.Build))
            {
                int oldest = sameKind.Min(p => p.Build);
                var latestEnd = sameKind.Where(p => p.Build == oldest).Max(p => p.EsuEnd is { } esu && esu > p.EndOfSupport ? esu : p.EndOfSupport);
                if (today > latestEnd)
                    return new Outcome(ResultStatus.Fail) { Expected = Iso(latestEnd), Actual = build.ToString(CultureInfo.InvariantCulture) }
                        .With(T["Detail_OlderThanTable", build, oldest, Iso(latestEnd)]);
            }
            return new Outcome(ResultStatus.NotCheckable, RglCodesEngine.CatalogueDataMissing) { Actual = build.ToString(CultureInfo.InvariantCulture) }
                .With(T["Detail_NoLifecycleEntry", kind, build, Facts.Edition ?? "?"]);
        }
        var o = new Outcome(ResultStatus.Pass) { Expected = Iso(entry.EndOfSupport), Actual = entry.Id }
            .With(T["Detail_LifecycleSource", entry.Name, entry.Source, Iso(entry.Seen)]);
        if (today <= entry.EndOfSupport) return o.With(T["Detail_SupportedUntil", Iso(entry.EndOfSupport)]);
        if (Context.EsuCovered)
        {
            if (entry.EsuEnd is not { } esu) return (o with { Status = ResultStatus.Fail }).With(T["Detail_EsuNotOffered"]);
            if (today <= esu) return (o with { Expected = Iso(esu) }).With(T["Detail_EsuUntil", Iso(esu)]);
            return (o with { Status = ResultStatus.Fail, Expected = Iso(esu) }).With(T["Detail_EsuEnded", Iso(esu)]);
        }
        return (o with { Status = ResultStatus.Fail }).With(T["Detail_SupportEnded", Iso(entry.EndOfSupport)]);
    }

    /// <summary>
    /// Tage seit dem letzten kumulativen Update, gemessen an CollectedAtUtc: Veröffentlichungstag von Build + UBR aus
    /// rules/lifecycle/builds.json; eine UBR zwischen zwei Einträgen wird über die Spanne entschieden; neuer als die
    /// Tabelle → „Regelwerk zu alt“; älter als ihr ältester Eintrag → „mindestens N Tage“. Ohne Tabellenentscheidung
    /// können das neueste QFE-InstalledOn und der Update-Verlauf nur einen Befund belegen (beide nennen auch andere
    /// Updates, und das Installationsdatum ist nicht das Veröffentlichungsdatum).
    /// </summary>
    private Outcome UpdateAge(UpdateAgeCheck c)
    {
        int maxDays = c.MaxDays ?? Context.UpdateMaxDays;
        var expected = maxDays.ToString(CultureInfo.InvariantCulture);
        var o = new Outcome(ResultStatus.Fail) { Expected = expected };
        if (Clock.Suspect)
            return (o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.ClockSuspect }).WithFlags(ResultFlags.ClockSuspect).With(T["Detail_ClockSuspect"]);
        int AgeOf(DateOnly d) => ValueComparison.AgeDays(new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), CollectedAtUtc) ?? 0;
        string? tableNote = null;

        // 1. Build-Tabelle
        if (Facts.Build is { } build && Facts.Ubr is { } ubr && BuildLookup.Find(Catalogue.Builds, build, Facts.Kind) is { Releases.Count: > 0 } entry)
        {
            var (exact, lower, higher) = BuildLookup.Locate(entry, ubr);
            // Die Build-Tabelle entscheidet aus dem Rechnerkopf, ohne Eintrag des Exports
            var table = o.DecidedBy();
            if (exact is not null)
            {
                int age = AgeOf(exact.Date);
                return (table with { Status = age <= maxDays ? ResultStatus.Pass : ResultStatus.Fail, Actual = age.ToString(CultureInfo.InvariantCulture) })
                    .With(T["Detail_InstalledRelease", build, ubr, Iso(exact.Date), exact.Kb ?? "–"]);
            }
            if (higher is null)
                return (table with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.CatalogueDataMissing }).With(T["Detail_CatalogueTooOld", build, ubr]);
            int minAge = AgeOf(higher.Date);
            if (minAge > maxDays)
                return (table with { Actual = ">=" + minAge.ToString(CultureInfo.InvariantCulture) }).With(T["Detail_AtLeastDays", minAge, build, ubr]);
            if (lower is not null && AgeOf(lower.Date) <= maxDays)
                return (table with { Status = ResultStatus.Pass, Actual = "<=" + AgeOf(lower.Date).ToString(CultureInfo.InvariantCulture) })
                    .With(T["Detail_AtMostDays", AgeOf(lower.Date), build, ubr]);
            tableNote = T["Detail_UbrNotInTable", build, ubr];
        }

        // 2. Neuestes QFE-InstalledOn: belegt nur einen Befund (Review evidence-4). Win32_QuickFixEngineering nennt alle
        // CBS-Updates (auch Wartungsstapel und .NET), und InstalledOn ist der Installations-, nicht der
        // Veröffentlichungstag – ein altes kumulatives Update, gestern installiert, wäre sonst „aktuell“. Ist aber seit
        // mehr als maxDays nichts installiert worden, ist auch das letzte kumulative Update mindestens so alt.
        var qfe = Store.Item(Known.QuickFix);
        string? qfeNote = null;
        if (qfe.IsPresent && JsonRead.Rows(qfe.Value) is { Count: > 0 } rows)
        {
            var newest = rows.Select(r => JsonRead.Time(JsonRead.Get(r, "InstalledOn"))).Where(t => t is not null).Max();
            if (newest is { } installed && ValueComparison.AgeDays(installed, CollectedAtUtc) is { } age)
            {
                var date = installed.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (age > maxDays)
                    return (o with { Actual = ">=" + age.ToString(CultureInfo.InvariantCulture), Source = ValueSource.Runtime })
                        .DecidedBy(Known.QuickFix).With(tableNote).With(T["Detail_NewestHotfix", date]);
                qfeNote = T["Detail_HotfixNotProof", date];
            }
        }

        // 3. Update-Verlauf: nur ein Befund ist belegbar (erfolgreiche Installationen, WUA: resultCode 2/3, operation 1);
        // nicht, wenn QFE eine jüngere Installation zeigt (offline installiert, nicht im Verlauf)
        var history = Store.Item(ItemKeys.WindowsUpdateHistory);
        if (qfeNote is null && history.IsPresent && JsonRead.Get(history.Value, "entries") is { ValueKind: JsonValueKind.Array } entries)
        {
            var newest = entries.EnumerateArray()
                .Where(e => JsonRead.Integer(e, "resultCode") is 2 or 3 && JsonRead.Integer(e, "operation") is null or 1)
                .Select(e => JsonRead.Time(JsonRead.Get(e, "dateUtc"))).Where(t => t is not null).Max();
            if (newest is { } last && ValueComparison.AgeDays(last, CollectedAtUtc) is { } age && age > maxDays)
                return (o with { Actual = ">=" + age.ToString(CultureInfo.InvariantCulture), Source = ValueSource.Runtime })
                    .DecidedBy(ItemKeys.WindowsUpdateHistory).With(tableNote).With(T["Detail_NewestHistory", last.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)]);
        }
        var reason = tableNote is not null || Catalogue.Builds.Builds.Count > 0 ? RglCodesEngine.CatalogueDataMissing : (qfe.Reason ?? RglCodesEngine.CatalogueDataMissing);
        return (o with { Status = ResultStatus.NotCheckable, ReasonCode = reason }).With(tableNote ?? (qfeNote is null ? T["Detail_NoUpdateEvidence"] : null)).With(qfeNote);
    }
}
