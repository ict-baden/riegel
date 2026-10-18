// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Parsing;
using Riegel.Core.Resources;
using Riegel.Core.Services;

namespace Riegel.Core.Baselines;

/// <summary>Eine Vorgabe vor der Vergabe der ID.</summary>
/// <param name="AuthorId">ID des Autors (bleibt erhalten), sonst null.</param>
/// <param name="Line">Zeile oder Position in der Quelle (für Fehlermeldungen).</param>
internal sealed record RuleDraft(string? AuthorId, string Title, Severity Severity, string Category, CheckSpec Check, string? Rationale,
    string? Reference, string Line);

/// <summary>
/// Import von Kunden-Baselines (spec 2.15): GPO-Sicherung (Ordner mit registry.pol, GptTmpl.inf, audit.csv),
/// <c>riegel.baseline/1</c> (JSON) und die CSV-Vorlage. Ergebnis ist ein Regelsatz für
/// <see cref="ICatalogueProvider.Combine"/> mit IDs <c>IMP-&lt;baseline&gt;-&lt;Autor-ID&gt;</c> oder
/// <c>IMP-&lt;baseline&gt;-&lt;SHA-1 (8) von Schlüssel und Prüfart&gt;</c>; Kollisionen sind ein Fehler. Schwere ohne Angabe
/// medium, SHA-256 der Quelle im <see cref="BaselineRef"/>, Grenzen aus <see cref="ReadLimits"/>, nie Codeausführung.
/// </summary>
internal sealed partial class BaselineImporter(TimeProvider clock) : IBaselineImporter
{
    /// <summary>Höchstens so tief verschachtelte zusammengesetzte Prüfungen (allOf, firstOf …).</summary>
    internal const int MaxCheckDepth = 16;

    public ImportedBaseline Import(string path, string name, ReadLimits l)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(l);
        if (Directory.Exists(path))
        {
            var gpo = GpoBackupReader.Read(path, l);
            var id = Slug(string.IsNullOrWhiteSpace(name) ? new DirectoryInfo(path).Name : name);
            return Build(id, Display(name, new DirectoryInfo(path).Name), BaselineSourceKind.GpoBackup, gpo.Sha256, new DirectoryInfo(path).Name, gpo.Drafts, l);
        }
        if (!File.Exists(path)) throw RiegelException.Common(RglCodesCommon.NotFound, path);
        var extension = Path.GetExtension(path);
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = TextFiles.ReadCapped(path, l.MaxBaselineCsvBytes, RglCodesParsing.BaselineTooLarge);
            var file = RiegelJson.Deserialize<BaselineFile>(bytes);
            if (!string.Equals(file.Schema, SchemaIds.Baseline, StringComparison.Ordinal)) throw RiegelException.Common(RglCodesCommon.UnsupportedSchema, file.Schema);
            if (!BaselineIdPattern().IsMatch(file.Id)) throw RglCodesParsing.Error(RglCodesParsing.BaselineIdInvalid, file.Id);
            if (file.Rules.Count > l.MaxBaselineCsvRows) throw RglCodesParsing.Error(RglCodesParsing.BaselineTooLarge, Path.GetFileName(path), l.MaxBaselineCsvRows);
            var drafts = new List<RuleDraft>();
            for (int i = 0; i < file.Rules.Count; i++)
            {
                var r = file.Rules[i];
                var where = "rules[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                if (string.IsNullOrWhiteSpace(r.Title)) throw RglCodesParsing.Error(RglCodesParsing.BaselineInvalid, where, "title");
                if (r.Category is { } c && !RuleCategories.IsKnown(c)) throw RglCodesParsing.Error(RglCodesParsing.BaselineInvalid, where, "category");
                CheckText(r.Title, l, where);
                if (r.Rationale is not null) CheckText(r.Rationale, l, where);
                if (r.Reference is not null) CheckText(r.Reference, l, where);
                ValidateCheck(r.Check, where, 0);
                drafts.Add(new RuleDraft(r.Id, r.Title.Trim(), r.Severity ?? Severity.Medium, r.Category ?? "system", r.Check, r.Rationale, r.Reference, where));
            }
            return Build(file.Id, Display(name, file.Name), BaselineSourceKind.RiegelJson, Hashes.Sha256Hex(bytes), Path.GetFileName(path), drafts, l);
        }
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = TextFiles.ReadCapped(path, l.MaxBaselineCsvBytes, RglCodesParsing.BaselineTooLarge);
            var drafts = BaselineCsvReader.Read(bytes, Path.GetFileName(path), l);
            var id = Slug(string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name);
            return Build(id, Display(name, Path.GetFileNameWithoutExtension(path)), BaselineSourceKind.Csv, Hashes.Sha256Hex(bytes), Path.GetFileName(path), drafts, l);
        }
        throw RglCodesParsing.Error(RglCodesParsing.BaselineUnknownFormat, Path.GetFileName(path));
    }

    /// <summary>IDs vergeben, Kollisionen erkennen, Regeln bauen.</summary>
    internal ImportedBaseline Build(string baselineId, string displayName, BaselineSourceKind kind, string sourceSha256, string? sourceFileName,
        IReadOnlyList<RuleDraft> drafts, ReadLimits l)
    {
        if (!BaselineIdPattern().IsMatch(baselineId)) throw RglCodesParsing.Error(RglCodesParsing.BaselineIdInvalid, baselineId);
        if (drafts.Count > l.MaxBaselineCsvRows) throw RglCodesParsing.Error(RglCodesParsing.BaselineTooLarge, displayName, l.MaxBaselineCsvRows);
        var rules = new List<Rule>(drafts.Count);
        var byId = new Dictionary<string, (RuleDraft Draft, string Identity, string Content)>(StringComparer.OrdinalIgnoreCase);
        foreach (var draft in drafts)
        {
            var identity = RuleIdentity.Of(draft.Check);
            var content = RuleIdentity.Content(draft.Check);
            string suffix;
            if (draft.AuthorId is { } authorId)
            {
                if (!RuleIdPattern().IsMatch(authorId)) throw RglCodesParsing.Error(RglCodesParsing.BaselineIdInvalid, authorId);
                suffix = authorId;
                if (byId.ContainsKey(suffix)) throw RglCodesParsing.Error(RglCodesParsing.BaselineIdCollision, RuleId(baselineId, suffix), draft.Line);
            }
            else
            {
                suffix = RuleIdentity.Hash8(identity);
                if (byId.TryGetValue(suffix, out var existing))
                {
                    // Dieselbe Vorgabe zweimal (z. B. in zwei GPOs gleich gesetzt): einmal übernehmen
                    if (existing.Draft.AuthorId is null && existing.Identity == identity && existing.Content == content) continue;
                    throw RglCodesParsing.Error(RglCodesParsing.BaselineIdCollision, RuleId(baselineId, suffix), draft.Line);
                }
            }
            byId[suffix] = (draft, identity, content);
            rules.Add(ToRule(baselineId, displayName, suffix, draft));
        }
        var reference = new BaselineRef(baselineId, displayName, kind, sourceSha256, sourceFileName, clock.GetUtcNow(), rules.Count);
        return new ImportedBaseline(reference, rules);
    }

    internal static string RuleId(string baselineId, string suffix) => "IMP-" + baselineId + "-" + suffix;

    private static Rule ToRule(string baselineId, string displayName, string suffix, RuleDraft draft)
    {
        var rationale = draft.Rationale is { Length: > 0 } own
            ? LocalizedText.Of(("en", own))
            : Localized("Baseline_Rationale", displayName);
        if (draft.Reference is { Length: > 0 } reference)
            rationale = new LocalizedText(rationale.Values.ToDictionary(p => p.Key, p => p.Value + " (" + reference + ")", StringComparer.Ordinal));
        return new Rule(RuleId(baselineId, suffix), "0", RuleState.Active, draft.Category, draft.Severity, RuleKind.Check,
            LocalizedText.Of(("en", draft.Title)), rationale, null, null, null,
            new Applicability(["client", "server"], null, null, DomainScope.Any, false, [], null, AbsentCapability.NotApplicable),
            NeedsAdmin(draft.Check), draft.Check, null, [], [], [], [], baselineId);
    }

    /// <summary>Braucht die Prüfung Admin-Rechte beim Erfassen (secedit, Benutzerrechte, Überwachung)?</summary>
    internal static bool NeedsAdmin(CheckSpec check) => check switch
    {
        SeceditCheck or UserRightCheck or AuditSubcategoryCheck => true,
        AccountPolicyCheck a => a.Field is AccountPolicyField.PasswordComplexity or AccountPolicyField.ClearTextPassword,
        FirstOfCheck c => c.Checks.Any(NeedsAdmin),
        AllOfCheck c => c.Checks.Any(NeedsAdmin),
        AnyOfCheck c => c.Checks.Any(NeedsAdmin),
        NoneOfCheck c => c.Checks.Any(NeedsAdmin),
        NotCheck n => NeedsAdmin(n.Check),
        _ => false,
    };

    /// <summary>Text aus ParsingStrings in allen fünf Sprachen.</summary>
    internal static LocalizedText Localized(string key, params object[] args)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var language in Localization.UiLanguage.Supported)
        {
            var text = ParsingStrings.ResourceManager.GetString(key, CultureInfo.GetCultureInfo(language)) ?? key;
            values[language] = string.Format(CultureInfo.InvariantCulture, text, args);
        }
        return new LocalizedText(values);
    }

    // ---------------------------------------------------------------- Prüfen

    private static void ValidateCheck(CheckSpec check, string where, int depth)
    {
        if (depth > MaxCheckDepth) throw RglCodesParsing.Error(RglCodesParsing.BaselineInvalid, where, "depth");
        switch (check)
        {
            case RegistryCheck r:
                if (!r.Key.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase)) throw RglCodesParsing.Error(RglCodesParsing.BaselineInvalid, where, "key");
                break;
            case RegistryEffectiveCheck r:
                if (r.Sources.Count == 0 || r.Sources.Any(s => !s.Key.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase)))
                    throw RglCodesParsing.Error(RglCodesParsing.BaselineInvalid, where, "sources");
                break;
            case UserRightCheck u:
                if (!UserRightPattern().IsMatch(u.Right) || u.Sids.Any(s => !SidPattern().IsMatch(s)))
                    throw RglCodesParsing.Error(RglCodesParsing.BaselineInvalid, where, "userRight");
                break;
            case AuditSubcategoryCheck a:
                if (AuditCsvParser.NormalizeGuid(a.Guid) is null) throw RglCodesParsing.Error(RglCodesParsing.BaselineInvalid, where, "guid");
                break;
            case FirstOfCheck c: Children(c.Checks); break;
            case AllOfCheck c: Children(c.Checks); break;
            case AnyOfCheck c: Children(c.Checks); break;
            case NoneOfCheck c: Children(c.Checks); break;
            case NotCheck n: ValidateCheck(n.Check, where, depth + 1); break;
        }

        void Children(IReadOnlyList<CheckSpec> checks)
        {
            if (checks.Count == 0) throw RglCodesParsing.Error(RglCodesParsing.BaselineInvalid, where, check.Type);
            foreach (var child in checks) ValidateCheck(child, where, depth + 1);
        }
    }

    private static void CheckText(string text, ReadLimits l, string where)
    {
        if (text.Length > l.MaxStringChars || TextFiles.HasControlCharacters(text))
            throw RglCodesParsing.Error(RglCodesParsing.BaselineInvalid, where, "text");
    }

    private static string Display(string? name, string fallback) => string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

    /// <summary>Kurzname [a-z0-9-] (≤ 40, schemas/baseline.schema.json) aus freiem Text; Umlaute umschrieben.</summary>
    internal static string Slug(string text)
    {
        var token = FileNames.Token(text, "baseline").ToLowerInvariant().Replace('.', '-').Replace('_', '-');
        var sb = new StringBuilder();
        foreach (var c in token)
        {
            if (c == '-' && (sb.Length == 0 || sb[^1] == '-')) continue;
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') sb.Append(c);
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        return slug.Length == 0 ? "baseline" : slug;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    internal static partial Regex BaselineIdPattern();

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,40}$")]
    internal static partial Regex RuleIdPattern();

    [GeneratedRegex("^Se[A-Za-z]+$")]
    internal static partial Regex UserRightPattern();

    [GeneratedRegex(@"^(S-1(-\d+)+|\*-\d+)$")]
    internal static partial Regex SidPattern();
}
