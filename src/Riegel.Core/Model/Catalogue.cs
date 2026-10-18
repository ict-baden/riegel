// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.Json.Serialization;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Model;

// ---- Catalogue (spec 2.3, 7.2) ----

/// <summary>Ein geladenes Regelwerk: eigener Katalog, auf Wunsch zusammen mit importierten Kunden-Baselines.</summary>
public interface ICatalogue
{
    /// <summary>„riegel“ für den eigenen Katalog.</summary>
    string Id { get; }

    /// <summary>z. B. „2026.10“.</summary>
    string Version { get; }

    DateOnly ReleasedOn { get; }

    /// <summary>SHA-256 (klein, hex) über die kanonische Form des Katalogs (steht in Lauf, Bericht und Nachweispaket).</summary>
    string Sha256 { get; }

    IReadOnlyList<Rule> Rules { get; }

    /// <summary>Regel nach ID (Groß/klein egal), null wenn unbekannt.</summary>
    Rule? Find(string id);

    LifecycleTable Lifecycle { get; }

    BuildTable Builds { get; }

    CapabilityTable Capabilities { get; }

    /// <summary>rules/data/*.json (Muster für Fernwartungswerkzeuge, Office-Erkennung).</summary>
    CatalogueData Data { get; }
}

/// <summary>
/// Eine Regel (spec 2.3). Texte in en und de sind Pflicht, es/fr/zh-Hans fallen in 0.1.0 auf Englisch zurück.
/// </summary>
/// <param name="Id">&lt;PREFIX&gt;-&lt;NN&gt;, stabil und nie wiederverwendet; importierte Regeln IMP-&lt;baseline&gt;-&lt;id&gt;.</param>
/// <param name="Since">Katalogversion, mit der die Regel kam (Flag NewRule).</param>
/// <param name="Category">Einer der 17 Codes aus <see cref="RuleCategories"/>.</param>
/// <param name="RuleSetId">„riegel“ oder die ID einer importierten Baseline.</param>
public sealed record Rule(string Id, string Since, RuleState State, string Category, Severity Severity, RuleKind Kind,
    LocalizedText Title, LocalizedText Rationale, LocalizedText? OtNote, LocalizedText? Remedy,
    ReasonCategory? SuggestedReason, Applicability AppliesTo, bool NeedsAdmin, CheckSpec Check,
    RemediationSpec? Remediation, IReadOnlyList<RuleReference> References, IReadOnlyList<TextSource> TextSources,
    IReadOnlyList<VerificationNote> Verified, IReadOnlyList<RuleTestVector> Tests, string RuleSetId = "riegel")
{
    public IReadOnlyList<RuleReference> References { get; init; } = References ?? [];
    public IReadOnlyList<TextSource> TextSources { get; init; } = TextSources ?? [];
    public IReadOnlyList<VerificationNote> Verified { get; init; } = Verified ?? [];
    public IReadOnlyList<RuleTestVector> Tests { get; init; } = Tests ?? [];
}

/// <summary>Eigene Schwere-Skala (spec 2.6); Gewichte 1/2/3.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<Severity>))]
public enum Severity { Low, Medium, High }

/// <summary>check = Soll-/Ist-Vergleich; inventory = Bestand, Status immer info.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<RuleKind>))]
public enum RuleKind { Check, Inventory }

/// <summary>Zurückgezogene Regeln bleiben mit <see cref="Retired"/> in der Datei (ihre Ausnahmen bleiben gültig, markiert).</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<RuleState>))]
public enum RuleState { Active, Retired }

/// <summary>
/// Anwendbarkeit (spec 2.3). Unbekannte Fakten oder fehlende Kopf-Felder ergeben „nicht prüfbar“, nie „nicht anwendbar“.
/// </summary>
/// <param name="Os">„client“ und/oder „server“.</param>
/// <param name="When">Alle Bedingungen müssen gelten.</param>
/// <param name="Capability">Name aus der Fähigkeitstabelle (rules/capabilities/*.json).</param>
/// <param name="IfCapabilityAbsent">Ergebnis, wenn das Betriebssystem die Fähigkeit nicht hat (spec 2.4 Punkt 8).</param>
public sealed record Applicability(IReadOnlyList<string> Os, int? MinBuild, int? MaxBuild, DomainScope Domain,
    bool ExcludeDc, IReadOnlyList<FactCondition> When, string? Capability, AbsentCapability IfCapabilityAbsent)
{
    public IReadOnlyList<string> Os { get; init; } = Os ?? [];
    public IReadOnlyList<FactCondition> When { get; init; } = When ?? [];
}

[JsonConverter(typeof(CamelCaseEnumConverter<DomainScope>))]
public enum DomainScope { Any, Joined, Standalone }

/// <summary>Bedingung über einen Fakt (spec 2.5), <paramref name="Op"/> ist eq, ne, in oder notIn.</summary>
public sealed record FactCondition(string Fact, string Op, JsonElement Value);

[JsonConverter(typeof(CamelCaseEnumConverter<AbsentCapability>))]
public enum AbsentCapability { NotApplicable, Fail }

/// <summary>
/// Verweis auf eine Quelle (spec 2.2, 2.16). <c>src</c>: ACSC, MSB, MSDOCS-CCBY, MSLEARN-FACT, DISA, GSPP, ATTACK,
/// ENDOFLIFE. MSB-Verweise nennen nur Dokument und Einstellung, nie einen Wert (es gibt kein Wertfeld).
/// </summary>
/// <param name="Doc">Dokument mit Version, z. B. „Hardening Microsoft Windows 11 workstations (2026-01)“.</param>
/// <param name="Section">Abschnitt (ACSC).</param>
/// <param name="Setting">Name der Einstellung (MSB, DISA).</param>
/// <param name="Id">Eine Kennung (GSPP, STIG).</param>
/// <param name="Ids">Mehrere Kennungen (ATTACK).</param>
public sealed record RuleReference(string Src, string? Doc, string? Section, string? Setting, string? Id,
    IReadOnlyList<string>? Ids, string? Url, string? Version);

/// <summary>
/// Herkunft eines übernommenen Textes (ACSC-Texte dürfen mit Namensnennung angepasst werden, spec 2.2).
/// </summary>
/// <param name="Fields">Welche Felder betroffen sind: title, rationale, otNote, remedy.</param>
/// <param name="Changed">true = angepasst (Pflichtangabe nach CC BY 4.0).</param>
public sealed record TextSource(string Src, string? Doc, string? Section, string? Url, IReadOnlyList<string> Fields, bool Changed)
{
    public IReadOnlyList<string> Fields { get; init; } = Fields ?? [];
}

/// <summary>
/// Nachweis, gegen welche auslieferbare Quelle Pfad, Typ und Sollwert geprüft wurden (spec 2.2). Für check-Regeln
/// ist mindestens ein Eintrag mit <c>src</c> ACSC, MSDOCS-CCBY, MSLEARN-FACT oder DISA Pflicht.
/// </summary>
public sealed record VerificationNote(string Src, string? Doc, string? Url, DateOnly Date, string? Section, string? Note);

/// <summary>
/// Testvektor einer Regel (spec 2.3). Ohne <paramref name="Coverage"/> gilt volle Abdeckung des Plans: ein
/// fehlender Schlüssel ist dann KeyMissing.
/// </summary>
public sealed record RuleTestVector(string? Name, VectorMachine Machine, IReadOnlyList<CoverageEntry>? Coverage,
    IReadOnlyDictionary<string, JsonElement>? Facts, IReadOnlyDictionary<string, VectorItem> Items, ResultStatus Expect,
    IReadOnlyList<string>? ExpectFlags)
{
    public IReadOnlyDictionary<string, VectorItem> Items { get; init; } = Items ?? new Dictionary<string, VectorItem>();
}

/// <summary>Rechnerkopf eines Testvektors.</summary>
public sealed record VectorMachine(int Build, int? Ubr, string? Edition, string? InstallationType, int ProductType,
    int DomainRole, bool IsAdmin);

/// <summary>Eintrag eines Testvektors in der Kodierung der Snapshot-Einträge (spec 7.3).</summary>
public sealed record VectorItem(ItemStatus Status, JsonElement? Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Truncated = false);

/// <summary>Eine Regeldatei <c>rules/&lt;PREFIX&gt;.json</c> (<c>riegel.rules/1</c>).</summary>
public sealed record RuleFile(string Schema, string Prefix, IReadOnlyList<Rule> Rules)
{
    public IReadOnlyList<Rule> Rules { get; init; } = Rules ?? [];
}

/// <summary>
/// Katalogkopf <c>rules/catalogue.json</c> (<c>riegel.catalogue/1</c>). Dieselbe Form trägt im Nachweispaket den
/// ganzen Katalog (<c>catalogue/riegel-catalogue-&lt;version&gt;.json</c>): dann sind Regeln und Tabellen gefüllt.
/// </summary>
public sealed record CatalogueDocument(string Schema, string Id, string Version, DateOnly ReleasedOn,
    IReadOnlyList<Rule>? Rules, LifecycleTable? Lifecycle, BuildTable? Builds, CapabilityTable? Capabilities,
    CatalogueData? Data);

/// <summary>Die 17 Berichtskategorien (spec 2.1/2.3) in Berichtsreihenfolge.</summary>
public static class RuleCategories
{
    public static IReadOnlyList<string> All { get; } =
    [
        "lifecycle", "network", "credentials", "logon", "accounts", "userRights", "defender", "appControl", "encryption",
        "firewall", "services", "system", "audit", "powershell", "rdp", "media", "tls",
    ];

    /// <summary>Kategorie mit Deckelung im Gesamtwert (12 Gewichtspunkte, spec 2.6).</summary>
    public const string Audit = "audit";

    public static bool IsKnown(string category) => All.Contains(category, StringComparer.Ordinal);
}
