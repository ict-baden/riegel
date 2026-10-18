// SPDX-License-Identifier: MIT
namespace Riegel.Tools.RuleTool;

/// <summary>
/// Codes für Katalog-Inhalt und RuleTool: RGL-0300 bis RGL-0399 (spec 7.13; Pakete rules-a/b/c). Die Texte sind
/// bewusst nur Englisch, weil RuleTool ein Entwicklerwerkzeug ist und nicht ausgeliefert wird.
/// </summary>
public static class RglCodesCatalogue
{
    public const int First = 300;
    public const int Last = 399;

    /// <summary>Aufruf falsch: unbekannter Befehl, fehlendes oder unbekanntes Argument.</summary>
    public const string Usage = "RGL-0300";

    /// <summary>Repository nicht gefunden (keine Riegel.sln oberhalb des Arbeitsordners, --repo falsch).</summary>
    public const string RepositoryNotFound = "RGL-0301";

    /// <summary>Regeldatei nicht lesbar oder nicht im Format riegel.rules/1.</summary>
    public const string RuleFileInvalid = "RGL-0302";

    /// <summary>docs/rules.md weicht von der erzeugten Fassung ab (rules-doc --check).</summary>
    public const string RulesDocOutdated = "RGL-0303";

    /// <summary>Quellordner für verify-sources nicht gefunden.</summary>
    public const string SourceFolderNotFound = "RGL-0304";

    /// <summary>Ein Verweis einer Regel wurde in den lokalen Quellkopien nicht gefunden.</summary>
    public const string ReferenceNotFound = "RGL-0305";

    /// <summary>Ein Belegschlüssel einer Regel liegt nicht im Erfassungsplan.</summary>
    public const string EvidenceNotInPlan = "RGL-0306";

    /// <summary>Erfassungsplan nicht lesbar.</summary>
    public const string PlanInvalid = "RGL-0307";

    /// <summary>Eine Quelldatei war zu groß oder nicht lesbar und wurde übersprungen.</summary>
    public const string SourceFileSkipped = "RGL-0308";

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Usage, RepositoryNotFound, RuleFileInvalid, RulesDocOutdated, SourceFolderNotFound, ReferenceNotFound,
        EvidenceNotInPlan, PlanInvalid, SourceFileSkipped,
    ];

    /// <summary>Englischer Kurztext je Code (Ausgabe des Werkzeugs).</summary>
    public static string Message(string code) => code switch
    {
        Usage => "Invalid command line.",
        RepositoryNotFound => "Repository not found (no Riegel.sln above the working directory; use --repo).",
        RuleFileInvalid => "Rule file cannot be read as riegel.rules/1.",
        RulesDocOutdated => "docs/rules.md differs from the generated version; run 'RuleTool rules-doc'.",
        SourceFolderNotFound => "Source folder not found.",
        ReferenceNotFound => "Reference not found in the local source copies.",
        EvidenceNotInPlan => "Evidence key is not covered by contracts/collection-plan.json.",
        PlanInvalid => "Collection plan cannot be read.",
        SourceFileSkipped => "Source file skipped (too large or unreadable).",
        _ => code,
    };
}

/// <summary>Fehler des Werkzeugs mit RGL-Code.</summary>
public sealed class RuleToolException(string code, string detail) : Exception($"{code}: {RglCodesCatalogue.Message(code)} {detail}".TrimEnd())
{
    public string Code { get; } = code;
}
