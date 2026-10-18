// SPDX-License-Identifier: MIT
using System.Text.RegularExpressions;
using Riegel.Core.Model;

namespace Riegel.Tests.Rules.A;

/// <summary>
/// Lebenszyklus-, Build- und Fähigkeitstabelle des Pakets rules-a: jeder Eintrag hat Quelle (https) und Datum, Daten
/// sind gültig, Bereiche überschneiden sich nicht, und die geforderten Produkte und Builds sind vorhanden.
/// </summary>
public sealed partial class TableTests
{
    private static readonly DateOnly Seen = new(2026, 10, 18);

    [Fact]
    public void Lebenszyklus_Eintraege_haben_Quelle_Datum_und_stimmige_Termine()
    {
        var problems = new List<string>();
        var table = RuleData.Lifecycle;
        Assert.Equal(SchemaIds.Lifecycle, table.Schema);
        foreach (var p in table.Products)
        {
            if (!p.Source.StartsWith("https://learn.microsoft.com/", StringComparison.Ordinal)) problems.Add($"{p.Id}: Quelle ist keine Microsoft-Learn-Seite ({p.Source})");
            if (p.Seen != Seen) problems.Add($"{p.Id}: gesehen {p.Seen}");
            if (p.CrossCheck is { } cross && !cross.StartsWith("https://", StringComparison.Ordinal)) problems.Add($"{p.Id}: crossCheck ohne https");
            if (p.MainstreamEnd is { } main && main > p.EndOfSupport) problems.Add($"{p.Id}: Mainstream-Ende nach Supportende");
            if (p.EsuEnd is { } esu && esu <= p.EndOfSupport) problems.Add($"{p.Id}: ESU-Ende nicht nach Supportende");
            if (p.Os is not ("client" or "server")) problems.Add($"{p.Id}: os {p.Os}");
            if (p.Build <= 0) problems.Add($"{p.Id}: Build {p.Build}");
            if (p.EndOfSupport.Year is < 2000 or > 2040) problems.Add($"{p.Id}: Supportende {p.EndOfSupport} unplausibel");
        }
        foreach (var dup in table.Products.GroupBy(p => p.Id).Where(g => g.Count() > 1)) problems.Add($"{dup.Key}: doppelt");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Lebenszyklus_Eintraege_ueberschneiden_sich_nicht()
    {
        // Je (os, build): jede Edition höchstens einmal, höchstens ein Eintrag ohne Editionen, und der steht als Rückfall
        // hinter den Einträgen mit Editionen (der erste passende Eintrag gilt).
        var problems = new List<string>();
        foreach (var group in RuleData.Lifecycle.Products.Select((p, i) => (p, i)).GroupBy(x => (x.p.Os, x.p.Build)))
        {
            var entries = group.ToList();
            var editions = entries.SelectMany(e => e.p.Editions ?? []).GroupBy(e => e, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (editions.Count > 0) problems.Add($"{group.Key}: Editionen mehrfach: {string.Join(", ", editions)}");
            var fallbacks = entries.Where(e => e.p.Editions is null || e.p.Editions.Count == 0).ToList();
            if (fallbacks.Count > 1) problems.Add($"{group.Key}: mehrere Einträge ohne Editionen");
            if (fallbacks.Count == 1 && entries.Any(e => e.p.Editions is { Count: > 0 } && e.i > fallbacks[0].i))
                problems.Add($"{group.Key}: Rückfall-Eintrag {fallbacks[0].p.Id} steht vor einem Eintrag mit Editionen");
            // Der Rückfall (Home/Pro) darf nie später enden als die Einträge mit Editionen derselben Version (nie ein zu spätes Datum)
            if (fallbacks.Count == 1)
                foreach (var e in entries.Where(e => e.p.Editions is { Count: > 0 }))
                    if (fallbacks[0].p.EndOfSupport > e.p.EndOfSupport) problems.Add($"{group.Key}: Rückfall endet nach {e.p.Id}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Theory]
    [InlineData("client", 7601, null, "2020-01-14")]   // Windows 7 SP1
    [InlineData("client", 9600, null, "2023-01-10")]   // Windows 8.1
    [InlineData("client", 14393, "EnterpriseS", "2026-10-13")]   // Windows 10 2016 LTSB
    [InlineData("client", 17763, "EnterpriseS", "2029-01-09")]   // Windows 10 LTSC 2019
    [InlineData("client", 17763, "Professional", "2020-11-10")]  // Windows 10 1809 Pro
    [InlineData("client", 19044, "EnterpriseS", "2027-01-12")]   // Windows 10 LTSC 2021
    [InlineData("client", 19044, "IoTEnterpriseS", "2032-01-13")]   // Windows 10 IoT Enterprise LTSC 2021
    [InlineData("client", 19045, "Professional", "2025-10-14")]  // Windows 10 22H2
    [InlineData("client", 22631, "Enterprise", "2026-11-10")]    // Windows 11 23H2 Enterprise
    [InlineData("client", 22631, "Professional", "2025-11-11")]  // Windows 11 23H2 Pro
    [InlineData("client", 26100, "Professional", "2026-10-13")]  // Windows 11 24H2 Pro
    [InlineData("client", 26100, "Enterprise", "2027-10-12")]    // Windows 11 24H2 Enterprise
    [InlineData("client", 26100, "EnterpriseS", "2029-10-09")]   // Windows 11 Enterprise LTSC 2024
    [InlineData("client", 26100, "IoTEnterpriseS", "2034-10-10")]   // Windows 11 IoT Enterprise LTSC 2024
    [InlineData("client", 26200, "Education", "2028-10-10")]     // Windows 11 25H2 Education
    [InlineData("client", 26200, "Professional", "2027-10-12")]  // Windows 11 25H2 Pro
    [InlineData("server", 7601, null, "2020-01-14")]   // Windows Server 2008 R2 SP1
    [InlineData("server", 9200, null, "2023-10-10")]   // Windows Server 2012
    [InlineData("server", 9600, null, "2023-10-10")]   // Windows Server 2012 R2
    [InlineData("server", 14393, null, "2027-01-12")]  // Windows Server 2016
    [InlineData("server", 17763, null, "2029-01-09")]  // Windows Server 2019
    [InlineData("server", 20348, null, "2031-10-14")]  // Windows Server 2022
    [InlineData("server", 26100, null, "2034-11-14")]  // Windows Server 2025
    public void Gefordertes_Produkt_ist_mit_Microsofts_Supportende_eingetragen(string os, int build, string? edition, string endOfSupport)
    {
        var entry = RuleData.Lifecycle.Products.FirstOrDefault(p => p.Os == os && p.Build == build &&
            (p.Editions is null || p.Editions.Count == 0 || p.Editions.Contains(edition ?? "", StringComparer.OrdinalIgnoreCase)));
        Assert.NotNull(entry);
        Assert.Equal(DateOnly.Parse(endOfSupport, System.Globalization.CultureInfo.InvariantCulture), entry.EndOfSupport);
    }

    [Theory]
    [InlineData("ws2008r2-sp1", "2023-01-10")]
    [InlineData("ws2012", "2026-10-13")]
    [InlineData("ws2012r2", "2026-10-13")]
    [InlineData("win7-sp1", "2023-01-10")]
    [InlineData("win10-22h2", "2028-10-10")]
    public void ESU_Ende_steht_wo_Microsoft_es_nennt(string id, string esu)
    {
        var entry = RuleData.Lifecycle.Products.Single(p => p.Id == id);
        Assert.Equal(DateOnly.Parse(esu, System.Globalization.CultureInfo.InvariantCulture), entry.EsuEnd);
    }

    [Fact]
    public void Build_Tabelle_hat_Quelle_Datum_und_aufsteigende_Revisionen()
    {
        var problems = new List<string>();
        var table = RuleData.Builds;
        Assert.Equal(SchemaIds.Builds, table.Schema);
        foreach (var b in table.Builds)
        {
            var label = $"{b.Build} ({b.Name})";
            if (!b.Source.StartsWith("https://learn.microsoft.com/en-us/windows/release-health/", StringComparison.Ordinal)) problems.Add($"{label}: Quelle {b.Source}");
            if (b.Seen != Seen) problems.Add($"{label}: gesehen {b.Seen}");
            if (b.Releases.Count == 0) problems.Add($"{label}: keine Updates");
            for (var i = 1; i < b.Releases.Count; i++)
            {
                if (b.Releases[i].Ubr <= b.Releases[i - 1].Ubr) problems.Add($"{label}: UBR {b.Releases[i].Ubr} nicht aufsteigend");
                if (b.Releases[i].Date < b.Releases[i - 1].Date) problems.Add($"{label}: Datum von UBR {b.Releases[i].Ubr} vor dem Vorgänger");
            }
            foreach (var r in b.Releases)
            {
                if (r.Kb is { } kb && !KbPattern().IsMatch(kb)) problems.Add($"{label}: KB {kb}");
                if (r.Kind is { } kind && kind is not ("B" or "D" or "OOB")) problems.Add($"{label}: Art {kind}");
                if (r.Date > Seen) problems.Add($"{label}: UBR {r.Ubr} liegt in der Zukunft");
            }
        }
        foreach (var dup in table.Builds.GroupBy(b => (b.Build, b.Name)).Where(g => g.Count() > 1)) problems.Add($"{dup.Key}: doppelt");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Build_Tabelle_deckt_die_letzten_24_Monate_ohne_Luecke_ab()
    {
        // Unterstützte Builds: von Oktober 2024 (oder dem ersten Update des Builds) bis zum Patchday 2026-10-13, ohne
        // eine Lücke von mehr als einem Patchzyklus (62 Tage zwischen zwei Einträgen).
        var problems = new List<string>();
        var windowStart = new DateOnly(2024, 10, 18);
        foreach (var build in new[] { 14393, 17763, 19044, 19045, 20348, 22631, 26100, 26200 })
        {
            foreach (var b in RuleData.Builds.Builds.Where(b => b.Build == build))
            {
                var label = $"{b.Build} ({b.Name})";
                if (b.Releases[^1].Date != new DateOnly(2026, 10, 13)) problems.Add($"{label}: letztes Update {b.Releases[^1].Date}, erwartet 2026-10-13");
                var dates = b.Releases.Select(r => r.Date).OrderBy(d => d).ToList();
                if (dates[0] > windowStart && build is 14393 or 17763 or 19044 or 19045 or 20348 or 22631)
                    problems.Add($"{label}: erstes Update {dates[0]} – weniger als 24 Monate");
                for (var i = 1; i < dates.Count; i++)
                    if (dates[i].DayNumber - dates[i - 1].DayNumber > 62) problems.Add($"{label}: Lücke {dates[i - 1]} bis {dates[i]}");
            }
            Assert.Contains(RuleData.Builds.Builds, b => b.Build == build);
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Jeder_Build_der_Build_Tabelle_hat_einen_Lebenszyklus_Eintrag()
    {
        var builds = RuleData.Lifecycle.Products.Select(p => p.Build).ToHashSet();
        var missing = RuleData.Builds.Builds.Where(b => !builds.Contains(b.Build)).Select(b => b.Build).ToList();
        Assert.True(missing.Count == 0, "Ohne Lebenszyklus-Eintrag: " + string.Join(", ", missing));
    }

    [Fact]
    public void Faehigkeiten_haben_Microsoft_Quelle_und_gueltige_Bereiche()
    {
        var problems = new List<string>();
        var file = RuleData.Capabilities;
        Assert.Equal(SchemaIds.Capabilities, file.Schema);
        foreach (var c in file.Capabilities)
        {
            if (!c.Source.StartsWith("https://learn.microsoft.com/", StringComparison.Ordinal)) problems.Add($"{c.Capability}: Quelle {c.Source}");
            if (c.Seen != Seen) problems.Add($"{c.Capability}: gesehen {c.Seen}");
            if (c.Exists.Count == 0) problems.Add($"{c.Capability}: keine Bedingungen");
            foreach (var e in c.Exists)
                if (e.MinBuild is { } min && e.MaxBuild is { } max && min > max) problems.Add($"{c.Capability}: minBuild > maxBuild");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Jede_Faehigkeit_der_Regeln_steht_in_der_Tabelle()
    {
        var names = RuleData.Capabilities.Capabilities.Select(c => c.Capability).ToHashSet(StringComparer.Ordinal);
        var used = RuleData.Rules.Where(r => r.AppliesTo.Capability is not null).Select(r => (r.Id, Cap: r.AppliesTo.Capability!)).ToList();
        Assert.NotEmpty(used);
        foreach (var (id, cap) in used) Assert.True(names.Contains(cap), $"{id}: Fähigkeit {cap} fehlt in rules/capabilities/system.json");
    }

    [GeneratedRegex(@"^KB\d{6,8}$")]
    private static partial Regex KbPattern();
}
