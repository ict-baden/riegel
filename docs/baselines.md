[Riegel](../README.md) › Customer baselines

# Customer baselines

**Contents:** [What you can import](#what-you-can-import) · [Licence note](#licence-note) ·
[Rule IDs and re-imports](#rule-ids-and-re-imports) · [GPO backups](#gpo-backups) ·
[Authoring a riegel.baseline/1 file](#authoring-a-riegelbaseline1-file) · [CSV template](#csv-template) ·
[Limits and errors](#limits-and-errors) · [Sources](#sources)

Riegel checks its own catalogue (`2026.10`). A **customer baseline** adds requirements on top: the plant operator's
golden GPO, a machine builder's delivery requirements, or a security baseline the customer downloaded and is allowed to
use. Each imported baseline becomes its own rule set (`ruleSetId` = the baseline ID) with rule IDs `IMP-<baseline>-…`,
is evaluated in the same run as the catalogue and gets its own score section in the reports.

## What you can import

| Source | What Riegel reads | Rule types it produces |
|---|---|---|
| **GPO backup folder** (from the Group Policy Management Console, or the GPO backups inside the Microsoft Security Compliance Toolkit, BSI or DISA packages you downloaded yourself) | the computer side only: `registry.pol`, `microsoft\windows nt\SecEdit\GptTmpl.inf`, `microsoft\windows nt\Audit\audit.csv` | `registry`, `secedit`, `accountPolicy`, `auditSubcategory`, `userRight` |
| **`riegel.baseline/1` JSON** you write yourself | every rule in the file | the whole closed check vocabulary ([file formats › Check vocabulary](file-formats.md#check-vocabulary)) |
| **CSV template** ([`assets/baseline-template.csv`](assets/baseline-template.csv)) | one requirement per row | `registry`, `service`, `auditSubcategory`, `userRight` |

Policy Analyzer `.PolicyRules` files and HardeningKitty lists are not imported in 0.1; they are planned for 0.2.

Riegel never runs anything from a baseline. Every rule uses the closed vocabulary; unknown fields or check types are
errors, not ignored.

## Licence note

Riegel ships **no baseline content**: no files, values or texts from Microsoft security baselines, CIS Benchmarks,
BSI SiSyPHuS or IT-Grundschutz. You import content you hold the rights to, for your own machines. Some of those
sources restrict passing their content on. Therefore an evidence pack contains only the **metadata** of an imported
baseline by default (name, source SHA-256, rule IDs and keys, results); the full content is included only on request
(`--include-baseline-content`) and with a licence warning. Whether you may pass a baseline on is your decision; this
page is not legal advice.

## Rule IDs and re-imports

- **Baseline ID:** the `id` of a `riegel.baseline/1` file; for GPO backups and CSV files it is derived from the name
  you give the import (or the folder or file name): lower case, `[a-z0-9-]`, at most 40 characters, umlauts written
  out (`Vorgaben Kunde Ä` → `vorgaben-kunde-ae`).
- **Rule ID:** `IMP-<baseline>-<id>` when the author gave an ID (kept as written, `[A-Za-z0-9_.-]`, at most 40
  characters), otherwise `IMP-<baseline>-<hash>`: the first 8 hex characters of SHA-1 over the rule's **identity** in
  lower case. The identity is the check type plus the normalised key, **without the expected value**:

  | Check type | Identity |
  |---|---|
  | `registry` | `registry\|<key without trailing \>\|<value name>` |
  | `secedit` | `secedit\|<section>\|<key>` |
  | `accountPolicy` | `accountPolicy\|<field>` |
  | `auditSubcategory` | `auditSubcategory\|<GUID without braces>` |
  | `userRight` | `userRight\|<right>` |
  | `service`, `feature` | type and name |
  | `firstOf`, `allOf`, `anyOf`, `noneOf`, `not` | type and the identities of the parts, in order |
  | other types | type and the fields that name what is checked (item and property, profile, GUID, RID, SID, renderer) |

  Importing the same source again gives the same IDs. A changed expectation keeps the ID, so the report shows
  "baseline expectation changed" and existing exceptions still apply.
- **Collisions** are errors (`RGL-0283`): two rules with the same author ID, or two rules with the same identity but
  different content (for example the same registry value with two different expected values in two GPOs of one
  backup set). The exact same rule twice is taken once.
- **Severity** comes from the source; without one it is `medium`. **Category** comes from the source; without one
  it is `system` (CSV: from the type, see below).
- The import records the source SHA-256 (for a folder: SHA-256 over the `sha256sum`-style list of the files it read),
  the source file name, the import time and the rule count (`BaselineRef`). The plant folder keeps the import as
  `baselines/<id>.json`.

## GPO backups

Point the import at one of:

- a GPO backup folder (`{GUID}` with `DomainSysvol\GPO\Machine` inside),
- a folder of several GPO backups (each `{GUID}` subfolder is read; their rules are combined),
- a folder that contains `registry.pol`, `GptTmpl.inf` or `audit.csv` directly.

Folder and file names are matched without regard to case. Only the computer side is read; user settings, scripts,
preferences and `gpreport.xml` are not used.

A GPO sets exactly the configured value, so imported GPO rules compare for **equality** with that value. If you need
"at least 14 characters" instead of "exactly 14", author a `riegel.baseline/1` rule with `ge`.

| GPO content | Becomes |
|---|---|
| `registry.pol` value (REG_DWORD, REG_QWORD, REG_SZ, REG_EXPAND_SZ, REG_MULTI_SZ, REG_BINARY) | `registry` on `HKLM\<key>`, `eq`, `whenMissing: fail` (`multi_sz` compared as a list) |
| `registry.pol` `**Del.<value>` and `**DeleteValues` | `registry` with `notExists` for each named value |
| `registry.pol` `**DelVals`, `**DeleteKeys`, `**SecureKey`, other `**` names, key-only entries, REG_NONE and other types | skipped (not a value Riegel can check) |
| `GptTmpl.inf` `[System Access]` `MinimumPasswordLength`, `MaximumPasswordAge`, `MinimumPasswordAge`, `PasswordHistorySize`, `LockoutBadCount`, `LockoutDuration`, `ResetLockoutCount`, `PasswordComplexity`, `ClearTextPassword` | `accountPolicy` with the matching field ([file formats › accountPolicy](file-formats.md#accountpolicy--secedit-system-access)) |
| other `[System Access]` keys and `[Event Audit]` | `secedit` on that section and key (number or the raw text, e.g. `"Guest"` with its quotes) |
| `[Registry Values]` `MACHINE\…\<value>=<type>,<data>` | `registry` on `HKLM\…` (types 1 REG_SZ, 2 REG_EXPAND_SZ, 4 REG_DWORD, 7 REG_MULTI_SZ; others skipped) |
| `[Privilege Rights]` with SIDs (`*S-1-…`) | `userRight`: exactly these SIDs (`allOf` of `mustInclude` and `onlyFrom`); an empty assignment becomes `empty` |
| `[Privilege Rights]` with account names instead of SIDs | skipped: names cannot be compared across languages and machines |
| other sections (`[Service General Setting]`, `[Registry Keys]`, `[File Security]`, `[Group Membership]`, `[Kerberos Policy]`, event log sections) | skipped |
| `audit.csv` rows with a subcategory GUID and `Setting Value` 0–3 | `auditSubcategory` with exactly that setting (1 success, 2 failure, 3 both, 0 no auditing); option rows and other values are skipped |

Titles of GPO rules are technical and language neutral (`HKLM\…\EnableMulticast`, `[System Access] LockoutBadCount`,
`SeDebugPrivilege`, `<subcategory> {GUID}`), because a GPO backup has no names for registry values.

**Registry.pol format.** Riegel reads `registry.pol` as described in Microsoft's
[Registry Policy File Format](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/policy/registry-policy-file-format)
(seen 2026-10-18): a header of two DWORDs (signature `0x67655250`, version 1) and entries `[key;value;type;size;data]`
with the special value names `**DeleteValues`, `**Del.<value>`, `**DelVals`, `**DeleteKeys` and `**SecureKey`; an entry
whose value, type, size or data is missing or zero only creates the key. The page does not state the character
encoding. Riegel reads the brackets, separators, key and value name as UTF-16 LE (key and value terminated by NUL) and
type and size as 32-bit little-endian numbers, as measured on a test machine. Anything that does not follow this
layout is an error (`RGL-0270`), not a partial read.

## Authoring a riegel.baseline/1 file

A UTF-8 JSON file (schema [`schemas/baseline.schema.json`](../schemas/baseline.schema.json)):

```json
{
  "schema": "riegel.baseline/1",
  "id": "example-builder-hmi",
  "name": "Example builder – HMI requirements",
  "description": "Delivery requirements for the example HMI line (fictitious).",
  "rules": [
    {
      "id": "HMI-01",
      "title": "Remote configuration of the example HMI suite is switched off",
      "severity": "high",
      "category": "system",
      "check": { "type": "registry", "key": "HKLM\\SOFTWARE\\ExampleVendor\\HmiSuite\\Remote",
                 "value": "EnableRemoteConfig", "valueType": "dword", "op": "eq", "expected": 0, "whenMissing": "fail" },
      "rationale": "The line is configured locally only.",
      "reference": "Delivery specification 4.2"
    },
    {
      "title": "Minimum password length at least 14",
      "check": { "type": "accountPolicy", "field": "minPwLen", "op": "ge", "expected": 14 }
    }
  ]
}
```

- `schema`, `id` (`[a-z0-9-]`, at most 40), `name` and `rules` are required; each rule needs `title` and `check`.
- `id` per rule is optional (see [Rule IDs](#rule-ids-and-re-imports)); `severity` (`low`, `medium`, `high`),
  `category` (one of the 17 report categories), `rationale` and `reference` are optional. The reference is appended
  to the rationale in the report.
- `check` uses the closed vocabulary of [file formats › Check vocabulary](file-formats.md#check-vocabulary). Registry
  keys must start with `HKLM\`; user rights take SIDs (`S-1-5-32-544`) or `*-<RID>` for local accounts, never names;
  composites may be nested at most 16 levels.
- `sourceFileName`, `sourceSha256` and `importedAtUtc` are written by Riegel into the stored copy; leave them out when
  you author a file.

## CSV template

[`assets/baseline-template.csv`](assets/baseline-template.csv) has the columns
`id,title,severity,type,key,value,op,expected,valueType` and two example rows with fictitious values. Save it as
**CSV UTF-8**; a comma or a semicolon (German Excel) as the separator both work. Lists in `expected` are separated by
`;` – put such a field in quotes when `;` is also the column separator.

| `type` | `key` | `value` | `op` | `expected` | `valueType` | Category |
|---|---|---|---|---|---|---|
| `registry` | `HKLM\…` (or `HKEY_LOCAL_MACHINE\…`) | value name (empty = default value) | `eq` (default), `ne`, `lt`, `le`, `gt`, `ge`, `in`, `notIn`, `bitsSet`, `bitsClear`, `containsAll`, `containsNone`, `exists`, `notExists` | the value; numbers decimal or `0x…`; lists with `;`; empty for `exists`/`notExists` | `dword`, `qword`, `sz`, `expand_sz`, `multi_sz` | `system` |
| `service` | service name | `startMode` (default) or `state` | `in` (default) or `eq` | allowed start modes (`Boot`, `System`, `Auto`, `Manual`, `Disabled`) or states (`Running`, `Stopped` …), with `;` | empty | `services` |
| `auditSubcategory` | subcategory GUID (with or without braces) | ignored | empty or `eq` | `success`, `failure`, `successAndFailure`, `includesSuccess`, `includesFailure`, `noAuditing` | empty | `audit` |
| `userRight` | right, e.g. `SeDebugPrivilege` | ignored | `mustInclude`, `mustNotInclude`, `onlyFrom`, `empty` | SIDs with `;` (`*S-1-…` is accepted); empty for `empty` | empty | `userRights` |

- A missing registry value is a finding (`whenMissing: fail`).
- A service that is not installed satisfies a requirement that allows only `Disabled` (or only `Stopped`); for any
  other requirement it is a finding.
- `severity` is `low`, `medium` or `high`; empty means `medium`. `id` is optional.
- Every invalid row stops the import with `RGL-0284` and names the line and the column – there is no half-imported
  baseline.

## Limits and errors

Baselines are treated as hostile input (spec 7.12): no code execution, a closed vocabulary and these limits
(`ReadLimits`):

| Limit | Value |
|---|---|
| JSON or CSV file | 10 MB |
| rules or CSV rows | 50,000 |
| `registry.pol` | 16 MB |
| `GptTmpl.inf`, `audit.csv` | 16 MB each |
| text in a field | 1 MB |
| GPO backups in one folder | 64 |

| Code | Meaning |
|---|---|
| `RGL-0262` | wrong or invalid encoding (CSV must be UTF-8; `GptTmpl.inf` is UTF-16 LE with BOM) |
| `RGL-0264`, `RGL-0265`, `RGL-0270` | `GptTmpl.inf`, `audit.csv` or `registry.pol` is broken |
| `RGL-0280` | not a GPO backup folder, `.json` or `.csv` |
| `RGL-0281` | invalid content in a `riegel.baseline/1` file (key, category, nesting, SIDs) |
| `RGL-0282` | invalid baseline or rule ID |
| `RGL-0283` | rule ID collision |
| `RGL-0284` | invalid CSV row (line and column in the message) |
| `RGL-0285` | file too large or too many rules |
| `RGL-0286` | the GPO backup has no computer settings |

## Sources

- Microsoft, *Registry Policy File Format*,
  https://learn.microsoft.com/en-us/previous-versions/windows/desktop/policy/registry-policy-file-format (page dated
  2018-05-31, seen 2026-10-18).
- Product specification, sections 2.3 (check vocabulary) and 2.15 (customer baselines).
