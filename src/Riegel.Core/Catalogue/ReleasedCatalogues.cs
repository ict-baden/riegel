// SPDX-License-Identifier: MIT
using System.Text.RegularExpressions;
using Riegel.Core.Scoring;

namespace Riegel.Core.Catalogue;

/// <summary>
/// Jede veröffentlichte Fassung des eigenen Katalogs mit dem SHA-256 seiner kanonischen Form (unabhängige Prüfung vom
/// 2026-10-20). Eine Version gehört zu genau einem Inhalt: <c>verify</c> erkennt damit auch Pakete, die eine ältere
/// Riegel-Version geschrieben hat, und meldet einen Katalog, der sich als eine dieser Versionen ausgibt und einen anderen
/// Inhalt hat (RGL-0572). Jeder Push auf main ist über „latest“ veröffentlicht – deshalb braucht jede Änderung am
/// Katalog (Regeln, Tabellen, Kopf) eine neue <c>version</c> in rules/catalogue.json und hier eine neue Zeile.
/// Vorhandene Zeilen nie ändern oder löschen, sobald sie auf main sind (CatalogueLockTests, docs/development.md).
/// </summary>
internal static partial class ReleasedCatalogues
{
    /// <summary>Version → SHA-256 (kanonische Form, klein geschrieben) des Katalogs „riegel“.</summary>
    public static IReadOnlyDictionary<string, string> Riegel { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // v0.1.0 (2026-10-19); rules/ ist seitdem unverändert. Builds von „latest“ vor dem Tag hatten unter 2026.10 noch
        // andere Regeln – deren Pakete meldet verify als RGL-0572 (mit v0.1.0 oder neuer neu erstellen).
        ["2026.10"] = "17afec3c0c9ea3c0da569dd156b2ee7dcbae361ffb88c39ffdcd4fb5126142dc",
    };

    /// <summary>SHA-256 der veröffentlichten Fassung <paramref name="id"/> <paramref name="version"/>; null, wenn unbekannt.</summary>
    public static string? Sha256(string id, string version) =>
        id == ScoreCalculator.OwnRuleSet && Riegel.TryGetValue(version, out var sha) ? sha : null;

    /// <summary>Format einer Katalogversion: JJJJ.MM, bei einer weiteren Änderung im selben Monat JJJJ.MM.N.</summary>
    [GeneratedRegex(@"^\d{4}\.\d{2}(\.[1-9]\d{0,2})?$")]
    internal static partial Regex VersionPattern();
}
