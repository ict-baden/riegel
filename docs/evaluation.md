[Riegel](../README.md) › Evaluation rules

# How Riegel decides: evidence rules for auditors

**Contents:** [In one paragraph](#in-one-paragraph) · [Statuses](#statuses) · [Evidence classes](#evidence-classes) ·
[Missing values and defaults](#missing-values-and-defaults) · [Strict types](#strict-types) · [Operators](#operators) ·
[Runtime before configuration](#runtime-before-configuration) · [Combining checks](#combining-checks) ·
[Applicability](#applicability) · [Microsoft Defender gating](#microsoft-defender-gating) · [Check types](#check-types) ·
[Time base](#time-base) · [Flags](#flags) · [Value source](#value-source) · [Reason codes](#reason-codes) ·
[Known traps](#known-traps) · [How this is tested](#how-this-is-tested) · [Limits and unverified points](#limits-and-unverified-points)

This page explains, without program code, how the Riegel engine turns a snapshot (the raw facts a collector wrote on a
machine) into one result per rule. It is written for auditors, quality assurance and anyone who has to sign a Riegel
report. The binding product specification is section 2.3–2.8 of the project specification; this page describes how
the engine implements it. File formats are in [file-formats.md](file-formats.md).

## In one paragraph

A rule is **erfüllt (pass)** only when the snapshot contains positive evidence that meets the expected value – or when
the value is proven to be absent and the rule cites a published default for exactly this kind of machine (then the
result carries the flag **"Standardwert angenommen"**). Everything the engine could not read – access denied, an error,
not collected, a truncated value, a key outside the collected area – is **nicht prüfbar (not checkable)** with a reason
code, never a pass and never a silent fail. Where Windows has a runtime state (a running service, the firewall as it
is active, Microsoft Defender's own status), that runtime state decides before any configured registry value. A
mutation test proves these rules for every rule of the catalogue (see [How this is tested](#how-this-is-tested)).

## Statuses

The engine produces raw statuses only. Exceptions, tailoring, regressions and the score are applied later (see
[exceptions.md](exceptions.md) and [scoring-and-comparison.md](scoring-and-comparison.md)).

| Status | When the engine gives it |
|---|---|
| `pass` – erfüllt | the value meets the expectation, based on present evidence or a cited default (flagged) |
| `fail` – nicht erfüllt | the value does not meet the expectation, or a value is proven missing and the rule says missing is a finding |
| `notCheckable` – nicht prüfbar | evidence unavailable, default unknown, applicability unknown, configuration only, heuristic only, implausible clock |
| `notApplicable` – nicht anwendbar | the rule does not apply: operating system kind, build range, domain membership, domain controller, a fact condition, a feature the OS lacks, a third-party antivirus |
| `info` | inventory rules (lists such as local administrators); never a finding on its own |

Every rule of the catalogue gets exactly one result. A retired rule gets `notApplicable` with reason RGL-0248. If the
evaluation of a single rule fails because of a program error, that rule becomes `notCheckable` with RGL-0239 – the run
never aborts and an error never turns into a pass.

## Evidence classes

For every item a check reads, the engine first decides which class the evidence belongs to:

| Class | Meaning | Examples |
|---|---|---|
| **Present** | the item was read (`ok`) and the value exists | `RequireSecuritySignature = 1` |
| **NotSet** | proven absent: the key or value is missing **and** it lies inside a collected area that is complete and not denied | a policy key under `HKLM\SOFTWARE\Policies` that does not exist |
| **Unavailable** | everything else | `denied`, `error`, `notCollected`, `namespaceMissing`, a truncated value, a key outside the collected area, under a denied subkey, under a truncated root, or a value outside the collector's value filter |

The collected areas come from the snapshot's `coverage` list. Inside a complete recursive area (for example
`HKLM\SOFTWARE\Policies`, eight levels deep) a key without an item is proven absent. For a single planned key, absence
is proven only by an explicit `keyMissing` item or, for a value, by the key's own item. Two items with the same key are a
contradiction; neither counts as evidence.

Only **NotSet** ever reaches the missing-value rules below. **Unavailable** always gives `notCheckable` with the reason
code of the item status (RGL-0220–0226).

## Missing values and defaults

What a proven-missing value means is part of each registry rule (`whenMissing`):

| `whenMissing` | Result for a proven-missing value |
|---|---|
| `fail` | `fail`, detail "Wert nicht gesetzt" |
| `notCheckable` | `notCheckable`, RGL-0227 |
| `osDefault` | the first `osDefaults` entry that matches the machine (build range, edition, installation type, domain membership) supplies the value, which is then compared. A pass carries **FromDefault** ("Standardwert angenommen"), the value source is `osDefault`, and the details name the default, its source URL and the date it was seen. No matching entry → `notCheckable` "Standardwert unbekannt" (RGL-0228). If a machine field the entry needs is unknown, the search stops: a later entry may not replace an earlier one that might have matched. |
| `pass` | allowed only with a `missingPass` record (source URL, date seen, note) and an entry in the rules package's allow-list file; the pass carries **FromDefault** |

Every `osDefaults` and `missingPass` entry must cite an `https://` source and a date; the catalogue refuses to load
otherwise (RGL-0204). `secedit` checks know only `fail` and `notCheckable`.

## Strict types

A registry value whose type is neither the rule's `valueType` nor one of its `acceptTypes` is a **fail** with the detail
"falscher Werttyp" – Windows usually ignores a policy value of the wrong type. With `acceptTypes: ["sz"]` a DWORD rule
reads the string as an invariant decimal number; anything unparsable is again a fail. `none`/`unknown` types are always
wrong. For values that come from WMI or an API (not from the registry) a value of an unexpected JSON type is a defect in
the raw data, not a machine setting: the result is `notCheckable` (RGL-0233).

## Operators

| Value type | Operators | Notes |
|---|---|---|
| DWORD, QWORD, numbers | `eq ne lt le gt ge in notIn bitsSet bitsClear` | unsigned; QWORD is stored as a decimal string; `0` and `0x0` are equal; `bitsSet`: (v & m) = m, `bitsClear`: (v & m) = 0 |
| strings (`sz`, `expand_sz`) | `eq ne in notIn` | ordinal, case-insensitive, no expansion of `%variables%` |
| `multi_sz` | `eq ne containsAll containsNone` | `eq` compares as a set (order and duplicates ignored) unless the rule says `multiSz: list` |
| binary | `eq ne` | byte for byte; expected values are written as hex (`hex:0a0b`) |
| booleans (WMI) | `eq ne` | |
| any | `exists notExists` | decided on the evidence class: Present → exists; NotSet → not exists; Unavailable → not checkable |
| date/time | `ageDaysLe ageDaysGt` | age in whole days relative to the collection time (see [Time base](#time-base)); a time more than 24 hours after collection is not decidable |

The catalogue refuses an operator that is not defined for the value type or an expected value of the wrong JSON type
(RGL-0203).

## Runtime before configuration

Configured values can differ from what Windows actually does: a VBS policy of 0 while VBS runs because memory integrity
is enabled elsewhere, an SMB signing value that is missing while the server requires signing by default, an LSA
protection value without proof that LSASS really started protected. Therefore:

- Rules combine sources with `firstOf`, runtime first. The first part that gives pass or fail decides; parts that are
  not checkable or not applicable are skipped. A runtime "not required" therefore beats a cited default "required":
  the result is fail.
- Several check types have the order built in (see [Check types](#check-types)): the firewall's enabled state and
  default inbound action come from a Group Policy value of the profile when one is set (it overrides the local
  setting in both directions), otherwise from the firewall service (`HNetCfg.FwPolicy2`), which reports only the local
  setting – Microsoft: FirewallEnabled "indicates whether a firewall is enabled locally (the effective result may differ
  due to group policy settings)"; Defender settings from Defender's own status;
  VBS, memory integrity and Credential Guard only from the list of **running** security services.
- `anyOf` is never used to combine a runtime source with a configured or default value, because a cited default would
  then override a runtime "not required".

## Combining checks

| Combination | pass | fail | otherwise |
|---|---|---|---|
| `firstOf` | first decisive part passes | first decisive part fails | not checkable (no part decided) |
| `allOf` | all parts pass | any part fails | all not applicable → not applicable; else not checkable |
| `anyOf` | any part passes | all (applicable) parts fail | all not applicable → not applicable; else not checkable |
| `noneOf` | all (applicable) parts fail | any part passes | as `anyOf` |
| `not` | inner fails | inner passes | unchanged |

The flags of the deciding part travel with the result (FromDefault, Heuristic, ConfiguredOnly, Derived, ClockSuspect);
when all parts decide together (`allOf` pass, `anyOf` all fail), their flags are combined. Note that `not` and `noneOf`
turn a "missing value is a finding" (`whenMissing: fail`) into a pass. The mutation test reports every such rule as a
pass from missing evidence unless the rule is on its package's allow-list – so this pattern cannot slip in unnoticed.

## Applicability

A rule declares where it applies (`appliesTo`). The engine checks, in this order of precedence:

1. A definite "does not apply" wins: wrong operating system kind (client/server), build outside `minBuild`–`maxBuild`,
   domain membership (`joined`/`standalone`), a domain controller for rules marked `excludeDc`, a `when` condition on a
   fact that is false, or a capability the operating system lacks when the rule says `ifCapabilityAbsent:
   notApplicable`.
2. Otherwise, any of these that cannot be determined gives `notCheckable` "Anwendbarkeit nicht bestimmbar"
   (RGL-0229) – never `notApplicable` and never pass. A built-in-commands bundle without a domain role, for example,
   cannot be evaluated against domain-only rules.
3. Otherwise, a missing capability with `ifCapabilityAbsent: fail` is a **fail**: for protection rules (encryption,
   antivirus, credential isolation, application control) the absence of the protection is the gap, with an exception
   route ("Verschlüsselung durch Drittprodukt").

Facts (operating system, domain, Defender mode, Office, network …) are derived from the snapshot; each is a value or
unknown. When a capability exists on the operating system but its data source is missing (a WMI namespace or class),
the evidence is Unavailable and the result `notCheckable` – the anomaly is never turned into "not applicable".

Domain controllers are detected from `DomainRole` 4 or 5. Catalogue 2026.10 is not designed for domain controllers;
rules marked `excludeDc` are not applicable there.

## Microsoft Defender gating

Defender settings (`defenderSetting`) and attack surface reduction rules (`asrRule`) are evaluated only when they can
take effect. The Defender mode is read from `MSFT_MpComputerStatus.AMRunningMode`, which Microsoft documents with the
values **Normal**, **Passive** and **EDR Block Mode** ("Microsoft Defender Antivirus compatibility with other security
products", https://learn.microsoft.com/en-us/defender-endpoint/microsoft-defender-antivirus-compatibility, seen
2026-10-18). Without a readable mode the engine derives it, flagged **Derived**: antivirus or the antimalware service
disabled → off; antivirus and real-time protection on and no other antivirus registered → active; anything else
unknown. A missing Defender WMI namespace means Defender is absent.

| Defender mode | Third-party antivirus active | Defender settings (DEF-01/02/03/12, LIFE-04) | ASR rules and "ASR configured" (DEF-09) |
|---|---|---|---|
| active | any | evaluated | evaluated; pass only with real-time protection on |
| passive, EDR block, off | yes | not applicable "Drittanbieter-Virenschutz aktiv" | not applicable |
| passive, EDR block, off | no | fail "kein aktiver Virenschutz" | fail "ASR konfiguriert, aber nicht wirksam" or "nicht konfiguriert" |
| absent | yes | not applicable | not applicable |
| absent | no | fail | DEF-09 fail; a single ASR rule not applicable |
| unknown, or third party unknown | – | not checkable | not checkable |

"Off" with an active third-party antivirus is not in the specification's table; it is treated like passive. Whether a
third-party antivirus is active comes from the documented Windows Security Center API first; only when that is not
available from the `root\SecurityCenter2` WMI class, whose `productState` encoding Microsoft does not document – such
results carry the flag **Heuristic**. The rule for the ASR distinction: an ASR check fails on an absent Defender only
if its rule treats the missing capability as a gap (`ifCapabilityAbsent: fail`, as DEF-09 does); otherwise it is not
applicable, because DEF-09 already reports the gap.

ASR rule modes: 0 off, 1 block, 2 audit, 5 not configured, 6 warn. `accept` allows block (default), block or warn, or
block or audit. The effective list in `MSFT_MpPreference` decides. Only if it cannot be read does the engine look at
the Group Policy store (`…\Windows Defender Exploit Guard\ASR\Rules`) and then the MDM store; a matching mode found
there is "konfiguriert, Wirkung nicht nachgewiesen" (not checkable, ConfiguredOnly), a non-matching mode is a fail.

## Check types

Evidence is read in the order shown; the first source that can decide does so.

| Check type | Evidence (in order) | Rules of decision |
|---|---|---|
| `registry` | `reg:` item | evidence classes, `whenMissing`, strict types |
| `registryEffective` | the listed sources, e.g. policy before MDM before local | first present source decides; an unavailable source before it → not checkable; all missing → `whenMissing` |
| `secedit` | `parsed:secedit` | a key missing from the exported template → `whenMissing`; `[Registry Values]` entries are `type,value` (4 DWORD, 1 SZ, 2 EXPAND_SZ, 7 MULTI_SZ) and strictly typed |
| `accountPolicy` | `api:netusermodals`, then `parsed:secedit` `[System Access]` | password age "unlimited" (API null, secedit ≤ 0) fails every upper bound; lockout threshold 0 ("never locks") satisfies no bound; lockout duration 0 or unlimited = until an administrator unlocks |
| `auditSubcategory` | `api:audit-policy` (GUID → 0–3) | numbers only, never localised text; a GUID that is absent from the item was not collected |
| `userRight` | `api:lsa-rights` (SIDs), then `parsed:secedit` `[Privilege Rights]` | a right missing from the template is held by nobody; names without `*` are unresolved: not checkable for `mustNotInclude`/`onlyFrom`/`empty` unless a violation is already proven by SIDs; `mustInclude` can pass on SIDs alone; `*-<RID>` means a local account of this machine (machine SID from the local accounts) |
| `service` | `Win32_Service`, then `Services\<name>\Start` | absent service → `ifAbsent`; without `Win32_Service` (built-in bundles) the running state is unknown, so a `state` expectation is not checkable |
| `feature` | `Win32_OptionalFeature` | absent row → `ifAbsent` |
| `wmiProperty` | the named `wmi:` item (rows or method `out`) | row filter, then `any`/`all`/`first`; no rows → `ifNoRows`; JSON null counts as "not set" only for `exists`/`notExists` |
| `defenderSetting` | `MSFT_MpComputerStatus` or `MSFT_MpPreference` | [Defender gating](#microsoft-defender-gating) first |
| `asrRule` | `MSFT_MpPreference`, then policy store, then MDM | gating, modes, real-time protection |
| `firewallProfile` | enabled/default inbound: a set Group Policy value (`EnableFirewall`, `DefaultInboundAction` 1 = block, 0 = allow) decides, otherwise the local setting from the firewall service; policy store, then local store for logging and local-rule merge | policy store not readable, or no policy and no service state → not checkable; "not configured" logging/merge is a fail when the rule asks for the hardened setting, otherwise not checkable |
| `firewallExposure` | effective profile state (as `firewallProfile`), runtime rule list, `parsed:firewall-rules-registry` (GPO and local store), merge setting per profile | fail if a profile is off, allows inbound by default, or an enabled inbound allow rule opens a listed port to any address (`*`, `Any`, empty) on a considered profile; local rules do not count where merging is off; pass only with the runtime list **and** the registry stores; anything unclear → not checkable |
| `lifecycle` | machine header, `rules/lifecycle/windows.json`, plant `esuCovered` | first matching product; evaluation date ≤ end of support → pass; with ESU covered and an ESU end listed by Microsoft → pass until that date; a build older than every entry of its kind whose support has ended → fail; otherwise not checkable "Regelwerk ergänzen" |
| `updateAge` | `rules/lifecycle/builds.json`, then newest hotfix `InstalledOn`, then Windows Update history | release day of build + UBR; between two listed updates decided by the span; newer than the table → not checkable "Regelwerk zu alt"; older than the oldest entry → fail "mindestens N Tage" when that already exceeds the limit; the update history can only prove a finding (it also contains signature updates) |
| `lsaProtection` | `api:lsass-protection` (only with `--deep`), then Wininit event 12 after the last start, then `RunAsPPL` | registry alone is never pass: configured → not checkable with ConfiguredOnly; not configured → not checkable (a UEFI-locked or default-on protection is not visible in the registry) |
| `deviceGuard` | `Win32_DeviceGuard` | running services only (1 Credential Guard, 2 memory integrity; VBS status 2 = running); configured but not running → fail "konfiguriert, läuft nicht" (with `requireRunning: false` not checkable, ConfiguredOnly) |
| `avStatus` | Defender status, then Windows Security Center API, then SecurityCenter2 | Defender active with real-time protection and current signatures → pass; another product on and current (Security Center API) → pass; heuristic evidence only → not checkable with Heuristic; no antivirus at all → fail |
| `localAccount` | `Win32_UserAccount` (local accounts), then `api:netuserenum` | by RID, never by name; account not found → not checkable |
| `localAccounts` | `api:netuserenum` | any matching (enabled) account fails and is listed; "unused" = last logon older than the plant's `unusedAccountDays`, or never logged on (as DISA counts "Never") |
| `localGroup` | `api:localgroup:<SID>` | `maxMembers`, `allowedSids` (well-known SIDs or `*-<RID>`); a member without SID → not checkable |
| `inventory` | the listed items | `info` with a list; not checkable if none of the items could be read |

## Time base

- **Ages** (days since the last update, signature age, unused accounts, `ageDays` operators) are measured against
  `CollectedAtUtc`, the target machine's clock at collection.
- **Calendar facts** (end of support) use the evaluation date.
- **ClockSuspect:** the collection time lies more than 24 hours after the import or the evaluation time, more than one
  day before the release day of the installed build + UBR (one day of slack for time zones), or before the last
  start. Then every age-based result is `notCheckable` with the flag ClockSuspect (RGL-0232); other results are not
  affected.

## Flags

| Flag | Shown in German reports as | Meaning |
|---|---|---|
| FromDefault | Standardwert angenommen | pass based on a cited default or a documented "missing is safe" |
| ConfiguredOnly | Konfiguriert, Wirkung nicht nachgewiesen | only configuration evidence; never a pass |
| Heuristic | Nur heuristischer Nachweis | based on the undocumented Security Center `productState`; never a pass on its own |
| Derived | Abgeleitet | Defender mode derived from other properties |
| ClockSuspect | Uhr unplausibel | target clock implausible, ages not checkable |

The other flags (regression, exceptions, tailoring, measures, stale snapshot) are set after the engine; see
[exceptions.md](exceptions.md) and [scoring-and-comparison.md](scoring-and-comparison.md).

## Value source

Each result names where the deciding value came from: for registry values the attribution of the parsing step (domain
GPO – experimental until tested in a real domain –, local policy, MDM, directly set, or unknown without administrator
rights and gpresult), `osDefault` for cited defaults, `notSet` for proven-missing values, `runtime` for values read from
the running system (WMI, APIs, events). Without an attribution the source is `unknown`.

## Reason codes

`notCheckable` and `notApplicable` results carry a reason code; the texts are localised, the code stays the same.

| Code | Meaning |
|---|---|
| RGL-0220 | access to the evidence was denied |
| RGL-0221 | the evidence could not be collected (error) |
| RGL-0222 | the evidence was not collected |
| RGL-0223 | WMI namespace or class missing |
| RGL-0224 | evidence truncated or collection area incomplete |
| RGL-0225 | the collector marked the evidence as not applicable |
| RGL-0226 | absence cannot be proven (outside the collected area) |
| RGL-0227 | value not set; the rule assumes no default |
| RGL-0228 | value not set, default value unknown |
| RGL-0229 | applicability cannot be determined |
| RGL-0230 | configured, effect not proven |
| RGL-0231 | heuristic evidence only |
| RGL-0232 | target clock implausible |
| RGL-0233 | unexpected value in the raw data |
| RGL-0234 | redacted value cannot be compared |
| RGL-0235 | catalogue data missing or too old |
| RGL-0236 | Defender state cannot be determined |
| RGL-0237 | no decisive evidence |
| RGL-0238 | account names not resolved to SIDs |
| RGL-0239 | evaluation of the rule failed |
| RGL-0240–0247 | not applicable: OS kind, build, domain, domain controller, condition, missing feature, third-party antivirus, absent service/feature/data |
| RGL-0248 | rule retired |

Loading a catalogue that breaks these rules fails with RGL-0200–0212: unknown check type (0201) or field (0202),
operator and type mismatch (0203), defaults without source or date (0204), incomplete missing-value rules (0205), unknown
capability (0206) or fact (0207), duplicate capability (0208) or rule ID (0209), invalid rule (0210), invalid table or
file (0211), customer baseline conflict (0212). An invalid catalogue is never used.

## Known traps

- **Automatic logon:** `AutoAdminLogon = 1` is a finding whether or not `DefaultPassword` is in the registry; the
  password may be stored as an LSA secret. The engine adds "Kennwort in Registry" when the (redacted) value is present,
  otherwise "Ablage des Kennworts nicht prüfbar".
- **LSA protection:** configuration alone is never a pass (see the check type table). Microsoft names Wininit event 12
  "LSASS.exe was started as a protected process" as the way to verify it ("Configure added LSA protection",
  https://learn.microsoft.com/en-us/windows-server/security/credentials-protection-and-management/configuring-additional-lsa-protection,
  seen 2026-10-18).
- **Localised names:** user rights and accounts are compared by SID and RID only; a German "Gast" or "Administratoren"
  never decides anything.
- **Antivirus heuristics:** the Security Center `productState` alone never gives a pass.
- **Firewall exposure:** the rule list of a minimal collection (`--minimal`) is missing, so exposure is not checkable.

## How this is tested

- **Rule vectors:** every test vector of every catalogue rule is evaluated by the real engine. The engine also carries
  its own test catalogue (74 invented rules, about 500 vectors) that covers every check type with pass, fail, missing,
  not collected, denied, truncated, wrong type and not applicable.
- **Mutation test:** for every rule and every item it reads, the item is set to denied, not collected, truncated or
  error – one at a time and all together. The result may never be a pass that rests on the mutated item. Set to
  "key missing", a pass may remain only with a cited default (FromDefault) or for rules on the allow-list. Starting
  points are every test vector and every synthetic fixture. A negative control proves the test catches a pass from
  missing evidence.
- **Fixtures:** synthetic Windows 7, Server 2012 R2, Server 2016, Windows 10 LTSC, a German-language machine, a domain
  member, a domain controller and a machine with a third-party antivirus are evaluated against expectations derived
  from their recorded values.
- **Determinism:** the same snapshot and catalogue give the same result list byte for byte, independent of the order
  of items and rules.

## Limits and unverified points

- `AMRunningMode` "Passive Mode" (as written by the synthetic fixtures) is accepted as passive; Microsoft's page names
  "Passive". Other values such as "SxS Passive Mode" are treated as unknown. *(unverified against a real passive
  Defender)*
- The Security Center `productState` decoding (bit 0x1000 = enabled, bits 0x00F0 = signatures out of date) is a common
  community decoding, not documented by Microsoft; it is therefore only ever heuristic.
- The firewall registry stores follow DISA STIG "Microsoft Windows Defender Firewall with Advanced Security"
  (V-241995, V-242000, seen 2026-10-18 via https://www.stigviewer.com): policy
  `SOFTWARE\Policies\Microsoft\WindowsFirewall\{Domain,Private,Public}Profile`, local
  `…\SharedAccess\Parameters\FirewallPolicy\{Domain,Standard,Public}Profile`. That `AllowLocalPolicyMerge` also exists in
  the local store is *(unverified)*; when it is not set, merging counts as allowed, which can only add rules.
- The firewall profile state is the Group Policy value when set, else the local setting from `INetFwPolicy2`. An MDM
  (Firewall CSP) setting is not read separately, and the effective state of the firewall service itself
  (`MSFT_NetFirewallProfile` in the active store, Windows 8 / Server 2012 and later) is not collected yet; that would be
  a collector change. *(open)*
- Firewall port keywords such as `RPC` or `RPC-EPMap` denote dynamic or service ports and are not treated as the fixed
  ports of a rule. Block rules are not considered (an allow rule may be overridden by a block rule; that can only cause
  a fail, never a pass).
- The ASR MDM store is read as the value `AttackSurfaceReductionRules` ("GUID=mode|…") under
  `PolicyManager\current\device\Defender`. *(unverified against an MDM-managed machine)*
- secedit's encoding of "password never expires" is read as a maximum password age of 0 or −1. *(unverified against a
  recorded export)*
- Wireless adapters: NDIS physical medium 1 (WirelessLan) and 9 (Native802_11), counted in the declaration order on
  Microsoft's OID_GEN_PHYSICAL_MEDIUM page, and `Win32_NetworkAdapter.AdapterTypeID` 9 (Wireless).
- Windows Update history: result codes 2/3 (succeeded, succeeded with errors) and operation 1 (installation) are taken
  as successful installations. *(unverified)*
