// SPDX-License-Identifier: MIT
namespace Riegel.Core.Model;

// ---- Customer baselines (spec 2.15) ----

/// <summary>Eine importierte Baseline als Regelsatz für <c>ICatalogueProvider.Combine</c>.</summary>
/// <param name="Rules">Regeln mit <see cref="Rule.RuleSetId"/> = Baseline-ID und IDs IMP-&lt;baseline&gt;-&lt;…&gt;.</param>
/// <param name="ContentIncluded">Ob der Inhalt im Nachweispaket stehen darf (nur mit --include-baseline-content).</param>
public sealed record ImportedBaseline(BaselineRef Baseline, IReadOnlyList<Rule> Rules, bool ContentIncluded = false)
{
    public IReadOnlyList<Rule> Rules { get; init; } = Rules ?? [];
}

/// <summary>
/// <c>riegel.baseline/1</c>: von Kunden oder Anlagenbauern geschriebene Vorgaben im geschlossenen Vokabular, und
/// dieselbe Form gespeichert unter <c>baselines/&lt;id&gt;.json</c> (dann mit Quelle und Importzeit).
/// </summary>
/// <param name="Id">Kurzname [a-z0-9-], Teil der Regel-IDs IMP-&lt;id&gt;-….</param>
/// <param name="SourceSha256">SHA-256 der Quelldatei (gesetzt beim Import).</param>
public sealed record BaselineFile(string Schema, string Id, string Name, string? Description, string? SourceFileName,
    string? SourceSha256, DateTimeOffset? ImportedAtUtc, IReadOnlyList<BaselineRule> Rules)
{
    public IReadOnlyList<BaselineRule> Rules { get; init; } = Rules ?? [];
}

/// <summary>Eine Vorgabe einer Baseline.</summary>
/// <param name="Id">ID des Autors (bleibt erhalten); ohne ID bildet der Import eine aus Schlüssel und Prüfart.</param>
/// <param name="Title">Titel in der Sprache des Autors.</param>
/// <param name="Severity">Ohne Angabe medium.</param>
/// <param name="Category">Einer der 17 Codes; ohne Angabe „system“.</param>
public sealed record BaselineRule(string? Id, string Title, Severity? Severity, string? Category, CheckSpec Check,
    string? Rationale, string? Reference);
