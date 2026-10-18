// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using Riegel.Core.Model;
using Riegel.Core.Parsing;

namespace Riegel.Core.Baselines;

/// <summary>
/// GPO-Sicherung als Baseline (spec 2.15): die Computerseite einer oder mehrerer gesicherter GPOs
/// (<c>{GUID}\DomainSysvol\GPO\Machine</c> mit <c>registry.pol</c>, <c>microsoft\windows nt\SecEdit\GptTmpl.inf</c> und
/// <c>microsoft\windows nt\Audit\audit.csv</c>) oder ein Ordner, der diese Dateien direkt enthält. So liegen die
/// GPO-Sicherungen in den Microsoft-Baselines (SCT), im BSI-GPO-Paket und in DISA-Paketen, die der Kunde selbst lädt.
/// Übersetzt wird nur in das geschlossene Vokabular registry, secedit, accountPolicy, auditSubcategory und userRight;
/// alles andere (Dienste, Dateirechte, Gruppen, **DelVals, **DeleteKeys, **SecureKey, reine Schlüssel) wird übergangen.
/// Verglichen wird auf Gleichheit mit dem Wert der GPO (eine GPO setzt genau diesen Wert).
/// </summary>
internal static class GpoBackupReader
{
    internal sealed record Result(IReadOnlyList<RuleDraft> Drafts, string Sha256);

    private const string MachineRelative = "DomainSysvol/GPO/Machine";
    private const int MaxGpos = 64;

    /// <summary>[System Access] → accountPolicy (feste Zuordnung aus docs/file-formats.md).</summary>
    private static readonly Dictionary<string, AccountPolicyField> AccountFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MinimumPasswordLength"] = AccountPolicyField.MinPwLen,
        ["MaximumPasswordAge"] = AccountPolicyField.MaxPwAgeDays,
        ["MinimumPasswordAge"] = AccountPolicyField.MinPwAgeDays,
        ["PasswordHistorySize"] = AccountPolicyField.PwHistLen,
        ["LockoutBadCount"] = AccountPolicyField.LockoutThreshold,
        ["LockoutDuration"] = AccountPolicyField.LockoutDurationMin,
        ["ResetLockoutCount"] = AccountPolicyField.LockoutWindowMin,
        ["PasswordComplexity"] = AccountPolicyField.PasswordComplexity,
        ["ClearTextPassword"] = AccountPolicyField.ClearTextPassword,
    };

    internal static Result Read(string folder, ReadLimits l)
    {
        var root = new DirectoryInfo(folder);
        if (root.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw RglCodesParsing.Error(RglCodesParsing.BaselineUnknownFormat, root.Name);
        var machines = FindMachineFolders(root);
        if (machines.Count == 0) throw RglCodesParsing.Error(RglCodesParsing.BaselineEmpty, root.Name);
        var drafts = new List<RuleDraft>();
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var machine in machines)
        {
            if (Find(machine, "registry.pol") is { } pol)
            {
                var bytes = TextFiles.ReadCapped(pol.FullName, l.MaxRegistryPolBytes);
                hashes[Relative(root, pol)] = Hashes.Sha256Hex(bytes);
                FromRegistryPol(RegistryPolParser.Parse(bytes, Relative(root, pol), l.MaxBaselineCsvRows), drafts, Relative(root, pol));
            }
            if ((Find(machine, "microsoft", "windows nt", "SecEdit", "GptTmpl.inf") ?? Find(machine, "GptTmpl.inf")) is { } inf)
            {
                var bytes = TextFiles.ReadCapped(inf.FullName, l.MaxTextFileBytes);
                hashes[Relative(root, inf)] = Hashes.Sha256Hex(bytes);
                var text = TextFiles.DetectBom(bytes) == TextFiles.Bom.Utf16Le ? TextFiles.DecodeUtf16WithBom(bytes, inf.Name) : TextFiles.DecodeConsoleText(bytes, inf.Name);
                FromSecurityTemplate(SeceditParser.Parse(text, Relative(root, inf), l.MaxStringChars), drafts, Relative(root, inf));
            }
            if ((Find(machine, "microsoft", "windows nt", "Audit", "audit.csv") ?? Find(machine, "audit.csv")) is { } csv)
            {
                var bytes = TextFiles.ReadCapped(csv.FullName, l.MaxTextFileBytes);
                hashes[Relative(root, csv)] = Hashes.Sha256Hex(bytes);
                var rows = AuditCsvParser.Parse(TextFiles.DecodeConsoleText(bytes, csv.Name), Relative(root, csv), l.MaxBaselineCsvRows, l.MaxStringChars, strict: false);
                foreach (var row in rows)
                    drafts.Add(new RuleDraft(null, row.Name.Length > 0 ? row.Name + " " + row.Guid : row.Guid, Severity.Medium, RuleCategories.Audit,
                        new AuditSubcategoryCheck(row.Guid, row.Setting switch
                        {
                            1 => AuditExpectation.Success,
                            2 => AuditExpectation.Failure,
                            3 => AuditExpectation.SuccessAndFailure,
                            _ => AuditExpectation.NoAuditing,
                        }), null, null, Relative(root, csv)));
            }
            if (drafts.Count > l.MaxBaselineCsvRows) throw RglCodesParsing.Error(RglCodesParsing.BaselineTooLarge, root.Name, l.MaxBaselineCsvRows);
        }
        if (hashes.Count == 0) throw RglCodesParsing.Error(RglCodesParsing.BaselineEmpty, root.Name);
        // Prüfsumme der Quelle: über die Liste „<sha256>  <Pfad>\n“ der gelesenen Dateien (wie SHA256SUMS)
        var listing = new StringBuilder();
        foreach (var (path, sha) in hashes) listing.Append(sha).Append("  ").Append(path).Append('\n');
        return new Result(drafts, Hashes.Sha256Hex(listing.ToString()));
    }

    // ---------------------------------------------------------------- Ordner

    /// <summary>
    /// Ordner mit Computereinstellungen: der Ordner selbst (enthält registry.pol, GptTmpl.inf oder audit.csv), eine
    /// Sicherung (<c>DomainSysvol\GPO\Machine</c>) oder ein Ordner mit mehreren Sicherungen (je Unterordner eine).
    /// </summary>
    private static List<DirectoryInfo> FindMachineFolders(DirectoryInfo root)
    {
        if (Find(root, "registry.pol") is not null || Find(root, "GptTmpl.inf") is not null || Find(root, "audit.csv") is not null
            || Find(root, "microsoft", "windows nt", "SecEdit", "GptTmpl.inf") is not null) return [root];
        if (FindDirectory(root, MachineRelative.Split('/')) is { } single) return [single];
        var result = new List<DirectoryInfo>();
        foreach (var sub in Subdirectories(root).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (FindDirectory(sub, MachineRelative.Split('/')) is { } machine) result.Add(machine);
            if (result.Count > MaxGpos) throw RglCodesParsing.Error(RglCodesParsing.BaselineTooLarge, root.Name, MaxGpos);
        }
        return result;
    }

    private static IEnumerable<DirectoryInfo> Subdirectories(DirectoryInfo dir) =>
        dir.EnumerateDirectories().Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint)).Take(10_000);

    /// <summary>Unterordner nach Namen ohne Groß/klein (GPO-Sicherungen mischen die Schreibweise; Linux unterscheidet sie).</summary>
    private static DirectoryInfo? FindDirectory(DirectoryInfo dir, params string[] parts)
    {
        var current = dir;
        foreach (var part in parts)
        {
            current = Subdirectories(current).FirstOrDefault(d => d.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (current is null) return null;
        }
        return current;
    }

    private static FileInfo? Find(DirectoryInfo dir, params string[] parts)
    {
        var folder = parts.Length == 1 ? dir : FindDirectory(dir, parts[..^1]);
        return folder?.EnumerateFiles().Take(10_000)
            .FirstOrDefault(f => f.Name.Equals(parts[^1], StringComparison.OrdinalIgnoreCase) && !f.Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    private static string Relative(DirectoryInfo root, FileInfo file) => Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/');

    // ---------------------------------------------------------------- Inhalte

    private static void FromRegistryPol(IReadOnlyList<PolEntry> entries, List<RuleDraft> drafts, string source)
    {
        foreach (var e in entries)
        {
            var key = e.MachineKey;
            switch (e.Kind)
            {
                case PolEntryKind.Value:
                    RegValue value;
                    try
                    {
                        value = e.Decode();
                    }
                    catch (FormatException)
                    {
                        throw RglCodesParsing.Error(RglCodesParsing.RegistryPolInvalid, source, key + "\\" + e.ValueName);
                    }
                    if (ValueType(value.Type) is not { } type || value.ExpectedJson() is not { } expected) continue;
                    drafts.Add(new RuleDraft(null, key + "\\" + e.ValueName, Severity.Medium, "system",
                        new RegistryCheck(key, e.ValueName, type, null, CheckOperator.Eq, expected, WhenMissing.Fail, null, null,
                            type == RegValueType.MultiSz ? MultiSzMode.List : null), null, null, source));
                    break;
                case PolEntryKind.DeleteValue:
                case PolEntryKind.DeleteValues:
                    // Die Gruppenrichtlinie löscht den Wert: Soll ist „nicht vorhanden“ (whenMissing gilt für notExists nicht)
                    foreach (var target in e.Targets)
                        drafts.Add(new RuleDraft(null, key + "\\" + target, Severity.Medium, "system",
                            new RegistryCheck(key, target, ValueType(RegTypes.Name(e.Type)) ?? RegValueType.Sz, null, CheckOperator.NotExists, null,
                                WhenMissing.Fail, null, null, null), null, null, source));
                    break;
            }
        }
    }

    private static void FromSecurityTemplate(IReadOnlyList<InfSection> sections, List<RuleDraft> drafts, string source)
    {
        foreach (var section in sections)
        {
            foreach (var (key, raw) in section.Entries)
            {
                if (section.Name.Equals(SeceditParser.SystemAccess, StringComparison.OrdinalIgnoreCase))
                {
                    if (AccountFields.TryGetValue(key, out var field))
                    {
                        if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) continue;
                        drafts.Add(new RuleDraft(null, "[" + SeceditParser.SystemAccess + "] " + key, Severity.Medium, "accounts",
                            new AccountPolicyCheck(field, CheckOperator.Eq, JsonValues.Number(n)), null, null, source));
                    }
                    else
                    {
                        drafts.Add(Secedit(SeceditParser.SystemAccess, key, raw, "accounts", source));
                    }
                }
                else if (section.Name.Equals(SeceditParser.EventAudit, StringComparison.OrdinalIgnoreCase))
                {
                    drafts.Add(Secedit(SeceditParser.EventAudit, key, raw, RuleCategories.Audit, source));
                }
                else if (section.Name.Equals(SeceditParser.RegistryValues, StringComparison.OrdinalIgnoreCase))
                {
                    if (RegistryValue(key, raw, source) is { } draft) drafts.Add(draft);
                }
                else if (section.Name.Equals(SeceditParser.PrivilegeRights, StringComparison.OrdinalIgnoreCase))
                {
                    if (UserRight(key, raw, source) is { } draft) drafts.Add(draft);
                }
            }
        }
    }

    private static RuleDraft Secedit(string section, string key, string raw, string category, string source)
    {
        bool number = long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n);
        return new RuleDraft(null, "[" + section + "] " + key, Severity.Medium, category,
            new SeceditCheck(section, key, CheckOperator.Eq, number ? JsonValues.Number(n) : JsonValues.Text(raw),
                number ? SeceditValueKind.Number : SeceditValueKind.String, WhenMissing.Fail), null, null, source);
    }

    /// <summary>„MACHINE\System\…\Lsa\NoLMHash=4,1“ → registry-Prüfung auf HKLM\System\…\Lsa, Wert NoLMHash.</summary>
    private static RuleDraft? RegistryValue(string path, string raw, string source)
    {
        if (!path.StartsWith(@"MACHINE\", StringComparison.OrdinalIgnoreCase)) return null;
        int cut = path.LastIndexOf('\\');
        if (cut <= 8 || SeceditParser.SplitRegistryValue(raw) is not { } rv) return null;
        var key = @"HKLM\" + path[8..cut];
        var name = path[(cut + 1)..];
        var data = rv.Value.Trim();
        RegValueType type;
        System.Text.Json.JsonElement expected;
        switch ((uint)rv.Type)
        {
            case RegTypes.Dword:
                if (!CanonicalValue.TryParseInteger(data, out var d) || d > uint.MaxValue) return null;
                type = RegValueType.Dword;
                expected = JsonValues.Number(d);
                break;
            case RegTypes.Sz:
            case RegTypes.ExpandSz:
                type = rv.Type == RegTypes.Sz ? RegValueType.Sz : RegValueType.ExpandSz;
                expected = JsonValues.Text(Unquote(data));
                break;
            case RegTypes.MultiSz:
                type = RegValueType.MultiSz;
                expected = JsonValues.Texts(data.Length == 0 ? [] : data.Split(',').Select(Unquote));
                break;
            default:
                return null;
        }
        return new RuleDraft(null, key + "\\" + name, Severity.Medium, "system",
            new RegistryCheck(key, name, type, null, CheckOperator.Eq, expected, WhenMissing.Fail, null, null,
                type == RegValueType.MultiSz ? MultiSzMode.List : null), null, null, source);
    }

    /// <summary>
    /// [Privilege Rights]: die GPO setzt genau diese Liste. Leer → mode empty; sonst allOf(mustInclude, onlyFrom) =
    /// „genau diese“. Einträge ohne „*“ sind Namen statt SIDs und lassen sich nicht vergleichen → übergangen.
    /// </summary>
    private static RuleDraft? UserRight(string right, string raw, string source)
    {
        if (!BaselineImporter.UserRightPattern().IsMatch(right)) return null;
        var entries = raw.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (entries.Count == 0)
            return new RuleDraft(null, right, Severity.Medium, "userRights", new UserRightCheck(right, UserRightMode.Empty, []), null, null, source);
        if (entries.Any(e => !e.StartsWith('*'))) return null;
        var sids = entries.Select(e => e[1..]).ToList();
        if (sids.Any(s => !BaselineImporter.SidPattern().IsMatch(s))) return null;
        return new RuleDraft(null, right, Severity.Medium, "userRights",
            new AllOfCheck([new UserRightCheck(right, UserRightMode.MustInclude, sids), new UserRightCheck(right, UserRightMode.OnlyFrom, sids)]),
            null, null, source);
    }

    private static string Unquote(string text)
    {
        var t = text.Trim();
        return t.Length >= 2 && t[0] == '"' && t[^1] == '"' ? t[1..^1] : t;
    }

    private static RegValueType? ValueType(string snapshotType) => snapshotType switch
    {
        "dword" => RegValueType.Dword,
        "qword" => RegValueType.Qword,
        "sz" => RegValueType.Sz,
        "expand_sz" => RegValueType.ExpandSz,
        "multi_sz" => RegValueType.MultiSz,
        "binary" => RegValueType.Binary,
        _ => null,
    };
}
