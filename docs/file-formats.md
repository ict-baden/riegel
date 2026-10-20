[Riegel](../README.md) › File formats

# File formats and contracts

**Contents:** [Conventions](#conventions) · [Snapshot](#snapshot-riegelsnapshot1) · [Coverage and evidence](#coverage-and-what-missing-means) ·
[Item encodings](#item-encodings) · [Collection plan](#collection-plan-riegelcollection-plan1) · [Built-in bundle](#built-in-commands-bundle) ·
[Rule catalogue](#rule-catalogue) · [Check vocabulary](#check-vocabulary) · [Test vectors](#test-vectors) ·
[Canonical values](#canonical-value-rendering) · [Account policy mapping](#accountpolicy--secedit-system-access) ·
[Catalogue tables](#catalogue-tables) · [Plant folder](#plant-folder) · [Registers](#registers) · [Runs](#runs-riegelrun1) ·
[Approved state and imports](#approved-state-and-import-records) · [Baselines](#customer-baselines-riegelbaseline1) ·
[Evidence manifest](#evidence-manifest-riegelevidence1) · [Output file names](#output-file-names) · [Settings](#settings) ·
[Collector message tables](#collector-message-tables) · [Services and factories](#services-and-factories) ·
[Error codes](#error-codes) · [Contract reference](#contract-reference)

This page is the reference for every file Riegel reads or writes and for the public contract types in
`src/Riegel.Core/Model` and `src/Riegel.Core/Services`. The binding product specification is the source; this page
records the concrete formats the scaffold fixed. JSON Schemas (2020-12) for every format are in [`schemas/`](../schemas/).
Changes to a contract go through an integration request and update this page and the schema together.

## Conventions

- **Encoding:** UTF-8 without BOM, LF line endings, a final newline. JSON is indented with two spaces. Umlauts and
  other non-ASCII characters are written as they are (not as `\u` escapes), so registers diff cleanly in git and DMS.
  A UTF-8 BOM at the start of a file Riegel *reads* is tolerated.
- **Names:** properties are camelCase; enums are camelCase strings (`"keyMissing"`, `"notCheckable"`), never numbers.
  Registry value types keep their snapshot spelling (`"expand_sz"`, `"multi_sz"`).
- **Flags** (`ResultFlags`, `RunFlags`, `RegisterFlags`) are arrays of camelCase names in ascending bit order:
  `["regression", "fromDefault"]`; no flags = `[]`.
- **Time:** timestamps are ISO 8601 with a zone; Riegel writes UTC with `Z` (`2026-10-18T09:00:00Z`, fractional
  seconds only when present). A timestamp without a zone is invalid. Dates are `yyyy-MM-dd`. All numbers and dates
  are culture-invariant.
- **Absent values:** a property whose value is `null` is omitted on writing; absent and `null` mean the same. Lists are
  written even when empty.
- **Order:** properties follow the declaration order of the contract types (deterministic; the same input gives the
  same bytes). Dictionaries Riegel writes are sorted ordinally.
- **Closed vocabulary:** unknown properties and unknown check types are errors (`RGL-0004`). The discriminator
  `"type"` of a check may appear anywhere in its object.
- **Hashes:** SHA-256, lower-case hex, over the exact file bytes. Short form (`sha8`) = first 8 characters.
- **Limits** for hostile input are in `ReadLimits` (defaults from spec 7.12): snapshot 64 MB decompressed, 500,000
  items, strings 1 MB, JSON depth 64, registers 10 MB / 10,000 entries, `registry.pol` and INF/CSV/`.reg`/`reg query`
  16 MB each, baseline CSV 10 MB / 50,000 rows, evidence ZIP 512 MB (64 MB per entry), logo 1 MB, plant files 10 MB,
  catalogue 64 MB, collector sub-process timeout 60 s.
- **Reading and writing** go through `RiegelJson` (`Serialize`, `SerializeToUtf8Bytes`, `SerializeCompact`,
  `Deserialize`, `ReadFile`, `WriteFile`). Do not use `RiegelJsonContext.Default` directly: only `RiegelJson` applies
  the readable encoding, and hashes depend on it. `WriteFile` writes atomically (temporary file, then replace) and
  returns the SHA-256 of the written bytes.

## Snapshot (`riegel.snapshot/1`)

One file per collection, written by the portable collector, `Riegel.Cli.exe collect`, the app (origin `app`) or
converted from a built-in bundle (origin `builtin`). Raw facts only, no evaluation.

| Field | Type | Meaning |
|---|---|---|
| `schema` | string | `riegel.snapshot/1` |
| `collector` | object | `name`, `version`, `exeSha256?` (SHA-256 of the collector exe), `clr` (e.g. `4.0.30319.42000`, `2.0.50727.8000`), `is64BitProcess` |
| `collectedAtUtc` | timestamp | clock of the target machine at collection (time base for ages, spec 2.8) |
| `isAdmin` | bool | collected with an elevated token |
| `origin` | enum | `collector`, `app`, `builtin`, `recorded`, `synthetic` |
| `options` | object | `minimal`, `deep`, `pseudonymized` |
| `machine` | object | `hostName`, `dnsDomain?`, `domainRole?` (0–5, 4/5 = DC), `productType?` (1 client, 2 DC, 3 server), `build?`, `ubr?`, `editionId?`, `installationType?`, `displayVersion?`, `architecture?`, `machineGuid?`, `osLanguage?`, `lastBootUtc?` |
| `coverage` | array | one entry per registry plan entry (see below) |
| `items` | array | `{ key, status, value?, errorCode?, error?, durationMs, truncated? }` |

`status` is one of `ok`, `valueMissing`, `keyMissing`, `denied`, `notApplicable`, `namespaceMissing`, `notCollected`,
`error`. `errorCode` is the Win32 error or HRESULT (as a signed 32-bit integer), `error` a short text without any
names or values of the machine. `truncated: true` marks a value cut at a limit; it counts as unavailable evidence.
Item keys are compared case-insensitively and are unique within a snapshot.

File name: `<HOST>_<yyyyMMddTHHmmssZ>.snapshot.json` (`.snapshot.json.gz` when gzipped), host name sanitised and
upper-cased. Next to it `<file>.sha256` in sha256sum format: `<64 hex>␠␠<file name>\n` (`SnapshotFile`).

**Secrets** are redacted at collection and again at import: value names matching (case-insensitive) `*password*`,
`*passwd*`, `*pwd*`, `*secret*`, `*token*`, `*credential*`, `*passphrase*`, `*community*`, plus `DefaultPassword` and
`AltDefaultPassword`, are written as `{ "t": "<type>", "redacted": true, "empty": <bool> }`. Under any key path
containing `\SNMP\Parameters\ValidCommunities` the value *names* become `redacted-<n>`.

**Pseudonymisation** (`--pseudonymize`, opt-in): local user names, `Winlogon\DefaultUserName`, group member names and
profile path segments are replaced by `user-<first 8 hex of HMAC-SHA256(key = MachineGuid bytes, lower-invariant
name)>`; SIDs stay; `options.pseudonymized` is `true`. Pseudonymised is not anonymous.

**`parsed:*` items** are produced by the snapshot reader from raw items. In files of any origin other than
`synthetic` they are rejected as hostile input and recomputed; synthetic fixtures may contain them directly.

## Coverage and what "missing" means

A coverage entry is `{ root, recursive, maxDepth, complete, deniedSubkeys[], truncatedAt? }`, one per entry of the
collection plan's registry section:

- `root` is the plan path (`HKLM\…`). `recursive: true` for `recursive` and `children` plan entries (`maxDepth` 8 or
  1), `false` (`maxDepth` 0) for single keys.
- `complete: false` when the value limit (50,000 values in total) was reached or the root could not be read;
  `truncatedAt` then names the first key that was not read. `deniedSubkeys` lists full paths without read access.

Evidence classes (spec 2.4), decided by the engine from items and coverage:

| Class | When |
|---|---|
| **Present** | item `ok` and the value exists |
| **NotSet** | item `keyMissing`, or `ok`/`valueMissing` with the value absent, **and** the key lies inside a covered, complete, non-denied part: a recursive root that contains it within `maxDepth`, or exactly the single key |
| **Unavailable** | everything else: `denied`, `error`, `notCollected`, `namespaceMissing`, `truncated`, a key outside coverage, under a denied subkey or a truncated root |

Inside a complete recursive root, a key that has no item is `keyMissing` without an explicit item. A single planned
key that does not exist is written as an item with status `keyMissing`. A root that does not exist at all is written
as an item `keyMissing` for the root plus a complete coverage entry. Only NotSet reaches `whenMissing`.

**Value filter:** plan entries with a `values` list (e.g. `SOFTWARE\Microsoft\Cryptography`: `MachineGuid` only;
`Uninstall\*`: `DisplayName`, `DisplayVersion`, `Publisher`) are collected with only those values. The item's value
then carries `"valueFilter": [ … ]`; a value outside the filter is **not collected** (Unavailable), never NotSet.

## Item encodings

| Key pattern | Value |
|---|---|
| `reg:HKLM\<path>` | `{ "values": { "<name>": { "t": "dword\|qword\|sz\|expand_sz\|multi_sz\|binary\|none\|unknown", "v": …, "redacted"?, "empty"?, "truncated"?, "len"? } }, "subkeys": [ … ], "valueFilter"?: [ … ] }`. `""` is the default value. DWORD as a JSON number (unsigned), QWORD as a decimal string, `multi_sz` as an array, binary as base64 (≤ 4096 bytes, else the first 4096 + `truncated` + `len`). Names are matched case-insensitively. One item per existing key; the 64-bit view is read, the 32-bit view through the `WOW6432Node` paths in the plan. |
| `wmi:<ns>:<Class>` | `{ "rows": [ { "<Prop>": value } ] }`; integers as numbers (uint64 as string), booleans, strings, CIM datetimes as ISO 8601 UTC (unparsable: raw text under `"<Prop>@raw"`), arrays, `null`. Per-row methods nest in each row as `"<Method>": { "ReturnValue": n, … }` (e.g. `GetKeyProtectors` with `VolumeKeyProtectorID` and `KeyProtectorTypes`; `GetSecurityDescriptor` with `"Aces": [ { "Sid", "AccessMask", "AceType" } ]`). |
| `wmi:<ns>:<Class>.<Method>` | static method: `{ "out": { "<Prop>": value } }` |
| `api:netusermodals` | `{ minPwLen, maxPwAgeDays (null = unlimited), minPwAgeDays, pwHistLen, lockoutThreshold, lockoutDurationMin, lockoutWindowMin }` |
| `api:netuserenum` | `[ { name, sid, rid, disabled, passwordNotRequired, passwordNeverExpires, lockedOut, lastLogonDate (date only, null = never), passwordAgeDays } ]` |
| `api:lsa-rights` | `{ "<Right>": [ "<SID>", … ] }` for every right of the plan's list (empty array = nobody) |
| `api:localgroup:<SID>` | `[ { sid, name, kind: user\|group\|wellKnown\|deletedAccount\|unknown, domain } ]` for S-1-5-32-544 and S-1-5-32-555 |
| `api:audit-policy` | `{ "{GUID}": 0..3 }` (1 success, 2 failure, 3 both) |
| `api:lsass-protection` | `{ protectionByte, type: none\|light\|full, signer }`, only with `--deep` |
| `evt:system:wininit-12` | `{ found, timeCreatedUtc, eventData: [ … ], bootTimeUtc }`, newest event 12 of Wininit after the last boot |
| `api:firewall` | `{ currentProfiles, profiles: { domain\|private\|public: { enabled, defaultInbound, defaultOutbound } }, rulesCollected, rules: [ { name, enabled, direction, action, protocol, localPorts, remoteAddresses, profiles, application, service } ] }` |
| `api:windows-update-history` | `{ serviceWasRunning: true, entries: [ { dateUtc, resultCode, operation, title } ] }`, newest 100, only if `wuauserv` already runs, else `notCollected` |
| `api:winhttp-proxy` | `{ accessType, proxy, bypass }` |
| `api:wsc-av` | `[ { name, state: on\|off\|snoozed\|expired, signature: upToDate\|outOfDate } ]`, else `notCollected` |
| `raw:secedit-inf` | `{ "text": … }` (secedit export, UTF-16 converted to UTF-8; not with `--minimal`) |
| `raw:registry-pol` | `{ "base64": …, "sha256": … }` |
| `raw:gpresult-xml` | `{ "text": … }` (≤ 4 MB; not with `--minimal`) |
| `parsed:secedit` | `{ "sections": { "<Section>": { "<key>": "<raw value>" } } }`; `[Privilege Rights]` values as arrays split on `,`; `[Registry Values]` as `{ "type": <n>, "value": "<raw>" }` |
| `parsed:registry-pol` | `[ { key, value, t, v } ]` |
| `parsed:gpresult` | `{ gpos: [ { name, guid } ], registry: [ { key, value, gpo } ], security: [ { name, gpo } ] }` |
| `parsed:firewall-rules-registry` | `[ { store: gpo\|local, name, action, active, dir, protocol, lports, ra, profiles, app, svc } ]` |

The constants for these keys are in `ItemKeys` (`ItemKeys.Registry(path)`, `ItemKeys.Wmi(ns, class)`,
`ItemKeys.WmiMethod(ns, class, method)`, `ItemKeys.LocalGroup(sid)`).

**Encoding details** (as the collector writes them and the engine reads them; fixed in the integration step):

- **Absent means not collected.** A WMI property that is missing from a row, a subcategory GUID missing from
  `api:audit-policy` and a right missing from `api:lsa-rights` are **not collected** (Unavailable, spec 2.4.1) – never
  "no auditing", "nobody" or "no value". An admin-only WMI property that could not be read is left out and marked
  `"<Prop>@denied": true` (for example `ExclusionPath@denied` in `MSFT_MpPreference` without admin); the engine never
  reads that as "no exclusions". WMI properties that an older OS does not have are left out as well. A right the OS
  does not know gives an empty list in `api:lsa-rights`.
- **Denied subkeys** inside a recursive root appear only in `coverage.deniedSubkeys`, without an item.
- **Per-row WMI methods:** a failed call is written as `{ "ReturnValue": null, "ErrorCode": <HRESULT> }`;
  `"Aces": null` in `GetSecurityDescriptor` means the share has no DACL. `GetKeyProtectors.KeyProtectorTypes` is aligned
  with `VolumeKeyProtectorID` (derived through `GetKeyProtectors(KeyProtectorType = 1…10)`; 0 = unknown).
- **Dates:** `Win32_QuickFixEngineering.InstalledOn` is converted to ISO 8601 (raw text under `InstalledOn@raw`);
  `api:netusermodals.lockoutDurationMin` `null` means "until an administrator unlocks".
- **Values:** `api:firewall` `defaultInbound`/`defaultOutbound` are `"block"` or `"allow"`; firewall rules also carry
  `group` (the unresolved grouping resource). `api:lsass-protection.signer` is the signer number (4 = Lsa,
  `protectionByte >> 4`). `evt:system:wininit-12.eventData` is the event's data as an array of strings (`["4"]`).
- **Secrets:** DWORD and QWORD values with a secret-like name (`LimitBlankPasswordUse`, `EnablePlainTextPassword`,
  `DisablePasswordSaving`, `fPromptForPassword` …) stay readable – they are switches that rules check, not secrets
  (decision of the integration step on the parsing and collector requests; a deviation from the literal rule of spec
  7.3). `DefaultPassword` and `AltDefaultPassword` are always redacted, whatever their type. Under
  `…\SNMP\Parameters\TrapConfiguration` the subkey names become `redacted-<n>` as the value names under
  `ValidCommunities` do.
- **Derived items** (`parsed:*`, computed by the reader): `parsed:gpresult` may carry `policies: [ { name, gpo } ]`
  (administrative-template settings by name; gpresult gives no registry path for them); `parsed:firewall-rules-registry`
  entries carry `id` (the registry value name, which is the rule ID) and `invalid: true` for rule strings that break the
  [MS-GPFAS] grammar; `parsed:registry-pol` lists only value entries; `[Registry Values]` entries of `parsed:secedit`
  with a secret-like name are `{ type, value: "redacted", redacted: true }`. `raw:registry-pol` may carry
  `redacted: true` (base64 without the secret data, `sha256` of the original file). The reader also derives missing
  `parsed:*` items in synthetic files and passes the raw item's status on (a denied `raw:secedit-inf` gives a denied
  `parsed:secedit`).
- **Limits:** `raw:*` item strings may be up to `ReadLimits.MaxTextFileBytes` (16 MB); every other string is limited
  to 1 MB.

## Collection plan (`riegel.collection-plan/1`)

[`contracts/collection-plan.json`](../contracts/collection-plan.json) is the contract between collector and rules. It
is embedded into `Riegel.Collector.exe`; the collection code reads nothing outside it. A change to the plan is an
integration request and bumps the collector version (`collector-version.txt`).

| Section | Content |
|---|---|
| `limits` | `maxValuesTotal` 50,000, `recursiveMaxDepth` 8, `maxBinaryBytes` 4096, `toolTimeoutSeconds` 60, `maxRawTextBytes` 4 MB, `windowsUpdateHistoryEntries` 100, `tempPathMaxChars` 127, `maxStringChars` 1,048,576 |
| `registry.roots` | `{ path, kind: recursive, maxDepth, needsAdmin, mode, note? }`: SOFTWARE\Policies, CurrentVersion\Policies, PolicyManager\current\device, Control\Lsa, Control\SecurityProviders, Control\Terminal Server, Control\DeviceGuard, NetBT\Parameters, FirewallPolicy, Windows Defender, the LAPS roots, Control\CI |
| `registry.keys` | single keys (`kind: key`) and one-level children (`kind: children`, e.g. Uninstall), with `values` filters where the spec limits them; 32-bit views as `WOW6432Node` paths |
| `wmi` | `{ key, namespace, class, where?, properties, adminOnlyProperties?, methods: [ { name, kind: static\|perRow, viaClass?, keyProperty?, outProperties? } ], needsAdmin, mode, note? }` |
| `api` | `{ key, api, needsAdmin, mode, rights?, sid?, condition?, note? }`; `api:lsa-rights` lists 45 rights |
| `events` | `{ key, log, provider, eventId, xpath, afterLastBoot, needsAdmin, mode }` |
| `raw` | `{ key, kind: tool\|file, tool?, arguments?, file?, maxBytes, needsAdmin, mode }`; `{out}`/`{log}` are files in the collector's temp workspace |
| `allowedWmiMethods` | `GetConfiguration`, `GetConversionStatus`, `GetEncryptionMethod`, `GetKeyProtectors`, `GetProtectionStatus`, `GetSecurityDescriptor` – the only WMI methods a collector may call |
| `sources` | evidence for lists in the plan |

`mode`: `always`; `skipInMinimal` (not with `--minimal`: no child processes, no Windows Update COM; the firewall rule
list is also skipped, profiles stay); `deepOnly` (only with `--deep`: the LSASS handle). `needsAdmin` marks items that
return data only with an elevated token.

The right list of `api:lsa-rights` contains every constant of Microsoft's
[Privilege Constants](https://learn.microsoft.com/en-us/windows/win32/secauthz/privilege-constants) and
[Account Rights Constants](https://learn.microsoft.com/en-us/windows/win32/secauthz/account-rights-constants) pages
(seen 2026-10-18), except `SeUnsolicitedInputPrivilege`, which the page lists with "User Right: Not applicable".

## Built-in-commands bundle

Folder `<HOST>_<yyyyMMdd>.riegel-builtin\`, written by `collect-builtin.cmd` or by hand (spec 7.5):

| File | Content and encoding |
|---|---|
| `secedit.inf`, `secedit.log` | `secedit /export /cfg … /log … /quiet`; UTF-16 LE with BOM, CRLF |
| `auditpol.csv` | `auditpol /backup /file:…` (not on XP); ANSI CSV with header `Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value`; numeric `Setting Value`; `Option:*` rows ignored |
| `reg\<nn>-<name>.reg` | `reg export` of the fixed key list in `docs/collector.md`; UTF-16 LE with BOM, `Windows Registry Editor Version 5.00` (REGEDIT4 is accepted too) |
| `currentversion.txt` | `reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion"` without `/s`: the values of exactly this key (build, UBR, edition). A `reg export` of the key would be recursive and contain `Winlogon\DefaultPassword` in clear text. Older bundles with `reg\10-currentversion.reg` are still read. |
| `services-start.txt` | `reg query HKLM\SYSTEM\CurrentControlSet\Services /s /v Start` (console code page, CRLF); type tokens like `REG_DWORD` are not localised; the localised footer line is ignored |
| `winlogon.txt` | `reg query … /v AutoAdminLogon`, `/v DefaultUserName`, `/v DefaultDomainName`, then one line `DefaultPassword=present` or `DefaultPassword=absent` – never the value |
| `hostname.txt`, `ver.txt` | outputs of `hostname` and `ver` |
| `exitcodes.txt` | one line `<step>=<errorlevel>` per command, e.g. `secedit=0`, `reg-03=0`, `currentversion=0`, `services-start=0`; `reg-NN=1` means the key is missing or unreadable, and that file is skipped. `wow64=0` records that the commands ran in the 64-bit registry view (`wow64=1`: a 32-bit `cmd` on 64-bit Windows without `Sysnative`, whose `reg` sees `WOW6432Node`; Winlogon is then not used). A Winlogon value missing from `winlogon.txt` counts as not set only with `wow64=0` and `winlogon-<value>=1` (`reg query` "value not found"); otherwise it is not collected |

The reader converts a bundle into a snapshot with `origin: builtin`, a coverage list for exactly the exported keys
(everything else `notCollected`), redaction re-applied and per-file size limits:

- **Coverage:** the top keys of each `.reg` file are recursive roots without a depth limit (`maxDepth` 512);
  `Services` is covered one level deep from `services-start.txt` with the value filter `[Start]`; `Winlogon` and
  `CurrentVersion` are single keys (Winlogon with a value filter); parent keys that a file leaves out become items with
  `valueFilter: []` (the key exists, its values are unknown).
- **Header:** collector `collect-builtin/1`; `collectedAtUtc` is the date in the folder name at 00:00Z (the time of day
  is unknown); `isAdmin` is true when `secedit` or `auditpol` succeeded; `productType` only from `WinNT` (client) or the
  installation type; `domainRole` is always `null` (rules that need it are not checkable).
- **Errors:** a step with a non-zero exit code is skipped (its items become errors with `RGL-0267`); everything the
  bundle does not contain is not collected (`RGL-0268`); a file that cannot be decoded or parsed completely is an
  `RGL` error, never a partial read. The raw bundle never goes into an
evidence pack. The synthetic fixtures `PG-ALT-01_20261010.riegel-builtin` (Windows 7) and
`XP-HMI-01_20261010.riegel-builtin` (Windows XP) are handcrafted; the XP `reg query` format (header
`! REG.EXE VERSION 3.0`, tab separators) is written from memory and **unverified**. `collect-builtin.cmd` reports
`RGL-0106` when the bundle folder already exists.

## Rule catalogue

| File | Format |
|---|---|
| `rules/catalogue.json` | `riegel.catalogue/1` header: `{ schema, id: "riegel", version: "2026.10", releasedOn }` |
| `rules/<PREFIX>.json` | `riegel.rules/1`: `{ schema, prefix, rules: [ Rule ] }`, one file per prefix (20 files, 153 rules in 2026.10) |
| `rules/lifecycle/windows.json` | `riegel.lifecycle/1` (see [catalogue tables](#catalogue-tables)) |
| `rules/lifecycle/builds.json` | `riegel.builds/1` |
| `rules/capabilities/<package>.json` | `riegel.capabilities/1` (`system.json`, `protection.json`, `logging.json`); a capability name may exist only once across all files |
| `rules/data/<name>.json` | `riegel.data/1` (`remote-access-tools.json`, `office-detection.json`) |
| `templates/**/*.json` | `riegel.exceptions-template/1` and `riegel.tailoring-template/1` (registers without approval; `templates/tailoring/` holds the role templates) |

All of them are embedded into `Riegel.Core` with stable logical names `rules/<path>` and `templates/<path>` (always
with `/`). In an evidence pack the whole catalogue travels as one `riegel.catalogue/1` document with `rules`,
`lifecycle`, `builds`, `capabilities` and `data` filled (`catalogue/riegel-catalogue-<version>.json`).

**Rule fields:** `id` (`<PREFIX>-<NN>`; imported baseline rules `IMP-<baseline>-<id>`), `since`, `state`
(`active`/`retired`), `category` (one of the 17 codes in `RuleCategories.All`), `severity` (`low`/`medium`/`high`),
`kind` (`check`/`inventory`), `title`, `rationale`, `otNote?`, `remedy?` (localised texts `{ "en": …, "de": … }`;
en and de are required, other languages fall back to English), `suggestedReason?`, `appliesTo`, `needsAdmin`,
`check`, `remediation?`, `references`, `textSources`, `verified`, `tests`, `ruleSetId?` (default `riegel`).

`appliesTo`: `{ os: ["client"|"server", …], minBuild?, maxBuild?, domain: any|joined|standalone, excludeDc, when: [ {
fact, op: eq|ne|in|notIn, value } ], capability?, ifCapabilityAbsent?: notApplicable|fail }`. An unknown fact or a
null machine field the rule depends on gives `notCheckable`.

`references`: `{ src, doc?, section?, setting?, id?, ids?, url?, version? }` with `src` one of `ACSC`, `MSB`,
`MSDOCS-CCBY`, `MSLEARN-FACT`, `DISA`, `GSPP`, `ATTACK`, `ENDOFLIFE`. **MSB references name the document and the
setting only; there is no value field.** GS++ references carry identifiers only (`KONF.2.1`), with one of the
Grundschutz++ group prefixes (ARCH, ASST, BER, BES, DET, DEV, DLS, GC, GEB, KONF, NOT, PERF, PERS, REA, RISK, SENS,
STM, TEST, UMS, VRB).

`textSources`: `{ src, doc?, section?, url?, fields: [title|rationale|otNote|remedy], changed }` for adapted texts
(ACSC, CC BY 4.0). `verified`: `{ src, doc?, url?, date, section?, note? }`; every active `check` rule needs at least
one entry with `src` ACSC, MSDOCS-CCBY, MSLEARN-FACT or DISA (`RuleSources.ValueBacking`).

`remediation`: `{ registry?: [ { key, value, type, data } | { key, value, delete: true } ], securityTemplate?: [ {
section, key, value } | { section: "Privilege Rights", key: <right>, sids: [ … ] } ], audit?: [ { guid, subcategory,
setting: none|success|failure|successAndFailure } ], manual?: { en, de } }`. Remediation is only ever written to
files.

The catalogue invariant tests (`tests/Riegel.Tests/Infrastructure/CatalogueInvariantTests.cs`) enforce: schema
validity, unique IDs, prefix = file name, the IDs and counts of spec 2.1 per existing file, en+de texts, value
provenance, cited `osDefaults`, `whenMissing: pass` only with `missingPass` and an entry in
`tests/Riegel.Tests/Rules/{A,B,C}/when-missing-pass.txt` (one rule ID per line, `#` comments), no CIS, SiSyPHuS or
IT-Grundschutz module IDs, and vectors for pass, fail, a `notCollected` item and a `keyMissing`/`valueMissing` item
for every active check rule. `CatalogueComplete` is set to `true` by the integration step.

## Check vocabulary

The closed set of spec 2.3. Every check has `"type"`; the fields are exactly these (`?` = optional):

| `type` | Fields |
|---|---|
| `registry` | `key`, `value`, `valueType` (`dword qword sz expand_sz multi_sz binary`), `acceptTypes?`, `op`, `expected?`, `whenMissing` (`fail notCheckable osDefault pass`), `osDefaults?` [ `{ minBuild?, maxBuild?, editions?, installationTypes?, domainJoined?, value, source, seen }` ], `missingPass?` `{ source, seen, note }`, `multiSz?` (`set` default, `list`), `configuredOnly?` (`true`: a met comparison is only configuration whose effect depends on something not collected – not checkable with flag `ConfiguredOnly`, never pass) |
| `registryEffective` | `sources` [ `{ key, value, origin: policy\|mdm\|local\|other }` ], `selection?` (`value` default: the first source that has the value; `root`: the first source key that has any value is the active policy and a value missing there is not set, as Windows LAPS selects its policy root) plus the registry comparison fields |
| `secedit` | `section`, `key`, `op`, `expected?`, `valueKind` (`number string registryValue`), `whenMissing` |
| `accountPolicy` | `field` (`minPwLen maxPwAgeDays minPwAgeDays pwHistLen lockoutThreshold lockoutDurationMin lockoutWindowMin passwordComplexity clearTextPassword`), `op`, `expected?` |
| `auditSubcategory` | `guid` (`{…}`), `expected` (`success failure successAndFailure includesSuccess includesFailure noAuditing`) |
| `userRight` | `right`, `mode` (`mustInclude mustNotInclude onlyFrom empty`), `sids` (well-known SIDs or `*-<RID>`) |
| `service` | `name`, `startMode?` (`Boot System Auto Manual Disabled`), `state?`, `ifAbsent` (`fail pass notApplicable`) |
| `feature` | `name`, `installState` (1 enabled, 2 disabled, 3 absent), `ifAbsent` |
| `wmiProperty` | `item`, `property` (a per-row method output as `<Method>.<Output>`, e.g. `GetKeyProtectors.KeyProtectorTypes`), `rowFilter?` `{ property, op, value }`, `rows` (`any all first`), `op`, `expected?`, `ifNoRows` (`notCheckable fail notApplicable pass`) |
| `defenderSetting` | `source` (`status preference`), `property`, `op`, `expected?` |
| `asrRule` | `guid`, `accept?` (`block` default, `blockOrWarn`, `blockOrAudit`) |
| `firewallProfile` | `profile` (`domain private public`), `property` (`enabled defaultInboundBlock logDropped allowLocalRules`), `expected` (bool) |
| `firewallExposure` | `ports` [ `{ protocol: tcp\|udp, port }` ], `profiles` (`active all`) |
| `lifecycle`, `lsaProtection` | – |
| `updateAge` | `maxDays?` |
| `deviceGuard` | `service` (`credentialGuard hvci vbs`), `requireRunning?` (default true) |
| `avStatus` | `maxSignatureAgeDays?` |
| `localAccount` | `rid`, `expect` (`disabled`) |
| `localAccounts` | `condition` (`passwordNeverExpires unusedDays passwordNotRequired`), `excludeRids?`, `enabledOnly?` (default true) |
| `localGroup` | `sid`, `maxMembers?`, `allowedSids?` |
| `inventory` | `renderer` (`localAdmins remoteAccessTools rdpState defenderExclusions shares adminShares networkFacts appControlState wireless`), `items` |
| `firstOf`, `allOf`, `anyOf`, `noneOf` | `checks` |
| `not` | `check` |

Operators (`op`): `eq ne lt le gt ge in notIn bitsSet bitsClear containsAll containsNone exists notExists ageDaysLe
ageDaysGt`; `expected` is required except for `exists`/`notExists`. Their semantics per value type, the three-valued
composites and `firstOf` precedence are defined in spec 2.4 and documented by the engine package in
`docs/evaluation.md`.

## Test vectors

`tests` in each rule: `{ name?, machine: { build, ubr?, edition?, installationType?, productType, domainRole, isAdmin },
coverage?, facts?, items: { "<key>": { status, value?, truncated? } }, expect, expectFlags? }`.

- `items` use the snapshot item encoding above; the key is the item key (`reg:HKLM\…`, `wmi:…`).
- Without `coverage`, a vector has full plan coverage, so a key that is absent from `items` but inside a plan root is
  `keyMissing`. With `coverage`, only those entries count.
- Like an import, the reader derives the `parsed:*` items from the raw items a vector lists (for example the
  registry rule stores for FW-11) and redacts secret-named values.
- **Time base:** a vector is collected at `2026-10-15T08:00:00Z` (the `SnapshotBuilder` default) and evaluated at
  `2026-10-18T09:00:00Z` with evaluation date 2026-10-18, plant defaults `updateMaxDays` 35 and `unusedAccountDays` 90,
  no ESU, origin `synthetic`. Age vectors (LIFE-02, LIFE-04, ACCT-17, DEF-18) use this base. The machine header has
  architecture `AMD64`.
- `facts` override derived facts (`{ "defender.mode": "passive" }`); unknown = `null`.
- `expect` is a result status; `expectFlags` are result flag names (`fromDefault`, `configuredOnly` …).
- Every active check rule has vectors for pass, fail, missing (`keyMissing` or `valueMissing`) and not collected,
  plus n/a where the rule has applicability conditions.
- **Proven absence that passes** must be listed in the package's allow-list `tests/Riegel.Tests/Rules/{A,B,C}/
  when-missing-pass.txt`: `whenMissing: pass` (with `missingPass`), and every rule that may pass because something is
  proven absent – `notExists`, a `service`/`feature` with `ifAbsent: pass` reached through registry absence, and
  `not`/`noneOf` over a part that fails when missing. The mutation test (spec 8.3) allows a pass from a `keyMissing`
  item only for these rules or with a cited OS default.

## Canonical value rendering

Shared by `expected`, `actual` and `acceptedValue` (`CanonicalValue`, spec 7.3):

| Value | Canonical text |
|---|---|
| DWORD, QWORD, integers | decimal (`0` and `0x0` are the same value; `AreEqual` compares integers numerically, decimal or `0x` hex) |
| booleans | `true`, `false` |
| strings (`sz`, `expand_sz`) | verbatim, no environment expansion |
| `multi_sz`, arrays | compact JSON: `["a","b"]` |
| objects | compact JSON with keys sorted ordinally |
| binary | `hex:<first 32 bytes, lower case>` plus `…` when longer or truncated |
| redacted | `redacted` or `redacted:empty` |
| missing | `null` in JSON; displayed as "(not set)" / "(nicht gesetzt)" (`CanonicalValue.Display`) |

## accountPolicy ↔ secedit [System Access]

`accountPolicy` reads `api:netusermodals` first, then `parsed:secedit` section `[System Access]`:

| `field` | `api:netusermodals` | secedit `[System Access]` key | Unit and notes |
|---|---|---|---|
| `minPwLen` | `minPwLen` | `MinimumPasswordLength` | characters |
| `maxPwAgeDays` | `maxPwAgeDays` | `MaximumPasswordAge` | days; `null` in the API item = unlimited |
| `minPwAgeDays` | `minPwAgeDays` | `MinimumPasswordAge` | days |
| `pwHistLen` | `pwHistLen` | `PasswordHistorySize` | remembered passwords |
| `lockoutThreshold` | `lockoutThreshold` | `LockoutBadCount` | attempts; `0` = never locks |
| `lockoutDurationMin` | `lockoutDurationMin` | `LockoutDuration` | minutes |
| `lockoutWindowMin` | `lockoutWindowMin` | `ResetLockoutCount` | minutes |
| `passwordComplexity` | – | `PasswordComplexity` | secedit only (`0`/`1`) |
| `clearTextPassword` | – | `ClearTextPassword` | secedit only (`0`/`1`, reversible encryption) |

How secedit encodes "never expires" for `MaximumPasswordAge` is for the engine package to verify against a recorded
export; this table fixes only the key mapping.

## Catalogue tables

**`rules/lifecycle/windows.json`** (`riegel.lifecycle/1`): `{ schema, products: [ { id, name, os: client|server,
build, editions?, installationTypes?, channel?, mainstreamEnd?, endOfSupport, esuEnd?, source, seen, crossCheck?,
note? } ] }`. The first entry whose build, editions (EditionID) and installation types match a machine applies.
`endOfSupport` is the last day with security updates without ESU; `esuEnd` the end of Extended Security Updates where
Microsoft lists it. Every entry cites a Microsoft Lifecycle page and the date it was seen; `crossCheck` may cite
endoflife.date.

**`rules/lifecycle/builds.json`** (`riegel.builds/1`): `{ schema, builds: [ { build, name, os?: client|server,
releases: [ { ubr, date, kb?, kind?: B|D|OOB } ], source, seen } ] }`, releases in ascending UBR. A build appears once
without `os`, or once per `os` when a client and a server release share the build number but have their own UBR
sequence (Windows 11 24H2 and Windows Server 2025 both use 26100). The engine picks the entry by build and the
machine's OS kind (`ProductType` 1 = client, 2/3 = server; `BuildTable.Find(build, osKind)`); with an unknown kind and
only `os`-specific entries there is no match and the update age is not checkable.

**`rules/capabilities/*.json`** (`riegel.capabilities/1`): `{ schema, capabilities: [ { capability, exists: [ { os?,
minBuild?, maxBuild?, minUbr?, editions?, installationTypes?, note? } ], source, seen, note? } ] }`. A capability exists
on a machine when any `exists` entry matches (all of its given conditions hold); `minUbr` requires at least that
revision (UBR) of the build, for features that arrived with a cumulative update – an unknown UBR makes the entry
unknown. Duplicate names across files are a load
error. Every entry cites a Microsoft source.

**`rules/data/*.json`** (`riegel.data/1`): `{ schema, kind: remoteAccessTools|officeDetection, remoteAccessTools?: [ {
product, serviceNames, displayNameContains, source, seen, note? } ], officeDetection?: { clickToRunKeys: [ { key,
value?, source?, seen?, note? } ], installRootKeys: [ … ], uninstallDisplayNamePrefixes: [ { pattern, source?, seen?,
note? } ] } }`. Service names match exactly, display names as substrings, both case-insensitive – never as regular
expressions.

## Plant folder

Any folder; layout and write rules in spec 7.6:

```
<plant>/
  riegel-plant.json                 riegel.plant/1
  exceptions.json                   riegel.exceptions/1
  tailoring.json                    riegel.tailoring/1
  measures.json                     riegel.measures/1
  baselines/<id>.json               riegel.baseline/1 (with source and import time)
  snapshots/<host>/<file>.snapshot.json (+ .sha256)
  imports/<yyyyMMddTHHmmssZ>-<6hex>.import.json   riegel.import/1
  runs/<host>/<runId>.results.json  riegel.run/1
  approved/<host>.json              riegel.approved/1
  history/{exceptions,tailoring,measures,plant}/<ts>_<sha8>.json, history/approved/<host>/<ts>.json
  reports/<yyyy-MM-ddTHHmmssZ>/     generated documents
  remediation/<host>/<runId>/
  .riegel.lock
```

`riegel-plant.json`: `{ schema, id, name, description?, groups: [ { id, name, role: other|hmi|engineeringStation|
historian|server|office|jumpHost } ], machines: [ { hostName, groupId?, label?, esuCovered, notes?, identity?: {
machineType?, serialNumber?, orderNumber?, deliveryDate?, plantSoftware?, builder?, location? } } ], settings: {
warnDays, staleDays, updateMaxDays, unusedAccountDays, confidentiality?, signOff: [ { step: created|reviewed|approved|
accepted, role } ], reportLanguage?, reportPreset: audit|asDelivered, performedBy?, coverLogoFile?,
riskAcceptanceRoles, operatorRole? } }`. Defaults (`PlantSettings.Default`): 30/30/35/90 days, preset `audit`, the
three default sign-off roles in the current UI language. The in-memory properties `Plant.Folder`, `Plant.ReadOnly`
and `Plant.FileSha256` are set by the plant store and never written.

Host names in file and folder names are sanitised by `FileNames.Host`: only `[A-Za-z0-9._-]`, at most 63 characters,
umlauts transliterated, reserved device names (`CON`, `NUL`, `COM1` …) prefixed with `_`.

## Registers

`exceptions.json`: `{ schema: "riegel.exceptions/1", plantId, exceptions: [ { id: "EX-0001", ruleId, scope: {
machines, groups, allMachines }, category: vendorRequirement|technicalNecessity|compensatingControl|riskAccepted|
notRelevantHere, justification, compensatingMeasure?, reference?, acceptedValue?, approval: { approvedByRole?,
approvedByName?, approvedOn?, reviewBy?, approvalRecord?, approvedContentSha256? }, createdOn, createdByRole?,
modifiedOn?, catalogueVersion?, comment? } ] }`.

`tailoring.json`: `{ schema: "riegel.tailoring/1", plantId, entries: [ { id: "TL-0001", ruleId, scope, reason,
approval, createdOn, createdByRole?, modifiedOn?, comment? } ] }`.

`measures.json`: `{ schema: "riegel.measures/1", plantId, measures: [ { id: "M-0001", ruleId, scope, action,
ownerRole, dueDate, ticket?, state: planned|done|dropped, createdOn, createdByRole?, modifiedOn?, comment? } ] }`.

Entries are sorted by `id`. **Content hash** (`ContentHashes.Of`): SHA-256 over the compact canonical JSON of the
entry with an empty `approval` object; it is stored as `approvedContentSha256` at approval, so any later edit (also of
`modifiedOn`) is detected. Templates (`*.exceptions-template.json`, `*.tailoring-template.json`) have the same entries
with `approval: {}`; importing one creates pending entries and needs the required fields of every entry (otherwise
`RGL-0004` with file, entry and field). When a register file lacks a required field of an entry, the entry is read
with an empty value (empty scope, empty text, no approval) and fails validation instead of making the plant
unreadable; a plant file without `settings` uses the defaults. The validity rules are spec 2.11–2.13 (workflow
package, `docs/exceptions.md`).

## Runs (`riegel.run/1`)

`runs/<host>/<runId>.results.json`: `{ schema, id, hostName, evaluatedAtUtc, evaluationDate, collectedAtUtc,
importedAtUtc?, snapshot: { path, sha256 }, tool: { name, version, commit?, exeSha256? }, catalogue: { id, version,
releasedOn, sha256 }, baselines: [ BaselineRef ], registers: { exceptions, tailoring, measures } (SHA-256 or ""),
isAdmin, options, results: [ RuleResult ], summary: RunSummary, categories: [ CategoryScore ], facts: { "<fact>":
value|null }, previousRunId?, approvedRunId?, reEvaluationOf?, flags: [ staleSnapshot|clockSuspect|
machineIdentityChanged|incomplete|domainController ] }`.

`snapshot.path` is relative to the plant folder (`snapshots/<host>/<file>`). Run files are hostile input (spec 7.12):
whoever reads a run's export again – remediation and its rollback, `compare`, re-evaluation, the comparison basis
of a new run, report data – takes it only through `IPlantStore.RunSnapshotPath`, which rejects absolute paths, drive
letters, UNC paths, `..` out of the folder, links (reparse points) and folders (`RGL-0415`) and a file whose SHA-256
differs from `snapshot.sha256` (`RGL-0416`). `remediate` and the target of `compare` fail with that code; a
comparison basis or the previous run of a monitoring `check` treats the export as missing and prints a note.

`RuleResult`: `{ ruleSetId, ruleId, status: pass|fail|exception|outOfScope|notApplicable|notCheckable|info, severity,
expected?, actual?, source: unknown|domainGpo|localPolicy|mdm|directlySet|osDefault|runtime|notSet, sourceDetail?,
evidenceKeys, exceptionId?, tailoringId?, measureId?, flags, reasonCode?, details? }`.

**Run IDs** (`RunIds.Create`): `<yyyyMMddTHHmmssZ>-<6 hex>`; the time stamp is the evaluation time, the hex the first
six characters of SHA-256 over the UTF-8 text of the snapshot SHA-256, the catalogue SHA-256, the exceptions,
tailoring and measures SHA-256s and the evaluation time (as written in JSON: `yyyy-MM-ddTHH:mm:ssZ`, fractional
seconds only when present), joined with line feeds. The same inputs give the same ID and the same bytes.

## Approved state and import records

`approved/<host>.json` (`riegel.approved/1`): `{ schema, hostName, runId, snapshotSha256, resultsSha256,
catalogueVersion, registers, approval }` (`approval.approvalRecord` is required for validity).

`imports/<ts>-<6hex>.import.json` (`riegel.import/1`): `{ schema, id, importedAtUtc, collectedByRole?, witnessRole?,
operatorRole?, files: [ { fileName, sha256, sidecarOk, hostName, collectedAtUtc, collectorName, collectorVersion,
collectorExeSha256?, isAdmin, options, origin, runId? } ] }`.

## Customer baselines (`riegel.baseline/1`)

Authorable JSON: `{ schema, id ([a-z0-9-], ≤ 40), name, description?, sourceFileName?, sourceSha256?, importedAtUtc?,
rules: [ { id?, title, severity?, category?, check, rationale?, reference? } ] }`. `check` uses the closed vocabulary
above. On import the rules get IDs `IMP-<id>-<authorId>` or `IMP-<id>-<sha1-8 of normalised key + check type>` (spec
2.15) and `ruleSetId` = baseline id; the stored copy under `baselines/<id>.json` records the source SHA-256 and the
import time. The CSV template (`docs/assets/baseline-template.csv`) and the GPO-backup mapping are described in
[Customer baselines](baselines.md).

## Evidence manifest (`riegel.evidence/1`)

`manifest.json` in `Riegel-Evidence_<plant>_<yyyy-MM-dd>.zip`: `{ schema, plantId, plantName, createdAtUtc,
evaluationDate, tool, catalogue, catalogueFile: { path, sha256 }, baselines: [ { baseline, contentIncluded, file? } ],
registers, runs: [ { runId, hostName, results: FileRef, snapshot: FileRef } ], operatorRole?, files: [ FileRef ] }`.
`files` lists every file of the pack except `manifest.json` and `SHA256SUMS.txt` (LF, sha256sum format). Paths inside
the pack use `/`.

Layout: the report documents at the root, `README.txt`, `runs/<host>/<runId>.results.json`,
`snapshots/<host>/<file>.snapshot.json` (+ `.sha256`; never a raw built-in bundle), `registers/<kind>_<sha8>.json` (the
register versions the runs used, from the current file or the history), `plant/riegel-plant.json`,
`catalogue/riegel-catalogue-<version>.json`, `comparison/<host>.<previous|approved>.json`, `imports/<id>.import.json`
and per customer baseline `baselines/<id>.metadata.json` (`riegel.evidence-baseline/1`,
[schema](../schemas/evidence-baseline.schema.json): baseline reference, `contentIncluded`, the rule identities and the
results); with `--include-baseline-content` also `baselines/<id>.json`, `baselines/<id>.rules.json` and
`baselines/LICENSE-WARNING.txt`. Details: [Evidence pack](evidence.md).

## Output file names

From `ReportFileNames` (spec 5; plant names sanitised with `FileNames.Token`):

| Output | Name |
|---|---|
| O1 snapshot | `<HOST>_<yyyyMMddTHHmmssZ>.snapshot.json` (+ `.sha256`) |
| O2 results | `runs/<host>/<runId>.results.json` |
| O3 CSV | `results.csv` with the columns `plant, host, runId, evaluatedAtUtc, ruleSet, ruleId, category, severity, status, flags, expected, actual, valueSource, exceptionId, exceptionReviewBy, tailoringId, measureId, measureDue, title_en` |
| O4 plant report | `Riegel-Report_<plant>_<yyyy-MM-dd>.pdf` |
| O5 machine report | `Riegel-Report_<plant>_<yyyy-MM-dd>_<host>.pdf` |
| O6 management summary | `Riegel-Summary_<yyyy-MM-dd>.pdf` |
| O7 workbook | `Riegel-Overview_<plant>_<yyyy-MM-dd>.xlsx` |
| O9 evidence pack | `Riegel-Evidence_<plant>_<yyyy-MM-dd>.zip` |
| O10 remediation | `remediation/<host>/<runId>/` with `riegel-remediation.reg`, `riegel-rollback.reg`, `GptTmpl.inf`, `audit.csv`, `README.txt` |
| O12 approval sheet | `Riegel-Ausnahmen-zur-Genehmigung_<plant>_<date>.pdf` |
| O13 target configuration | `Riegel-Soll-Konfiguration_<plant>_<date>.pdf` and `.xlsx` |
| O14 collection record | `Riegel-Erhebungsprotokoll_<plant>_<importId>.pdf` |
| O15 collector hand-over | `UEBERGABE.txt` next to the copied collector |

## Settings

| File | Location | Content |
|---|---|---|
| `settings.json` | `%LocalAppData%\Riegel` (Windows), `~/.local/share/riegel` (Linux, `$XDG_DATA_HOME` honoured), `~/Library/Application Support/Riegel` (macOS); `RIEGEL_DATA_DIR` (absolute path) overrides | `{ language?, theme?, defaultPlantFolder?, warnDays?, staleDays?, updateMaxDays?, unusedAccountDays?, reportLanguage?, recentPlants? }` |
| `defaults.json` | `%ProgramData%\ICT-Baden\Riegel` (Windows), `/etc/ictbaden/riegel` (Linux); read only | same keys (presets for GPO/Intune/SOTI distribution) |
| `logs/riegel-<yyyy-MM-dd>.log` | data folder | 7 days, at most 1 MB per day; never setting values, host names, accounts or paths |

Effective value = user setting, else machine preset (shown as "vorgegeben"), else built-in default
(`SettingsStore.Resolve`). An unreadable file is ignored (`RGL-0010`/`RGL-0011` in the log), never a crash.

Language order (`UiLanguage.Apply`): `RIEGEL_LANGUAGE` → user setting → `defaults.json` → operating system → English.
Supported: `en`, `de`, `es`, `fr`, `zh-Hans`; reviewed report languages (`UiLanguage.ReviewedReportLanguages`): `en`,
`de` – documents in other languages carry "Machine pre-translation – not reviewed".

## Collector message tables

`src/Riegel.Collector/messages.<lang>.txt` (en, de, es, fr, zh-Hans), embedded with their file names: UTF-8 without
BOM, one entry per line `Key=Text`, `\n` inside the text is a line break, lines starting with `#` are comments, `{0}`
placeholders as in .NET. Missing keys fall back to English. The localisation test requires the same keys and
placeholders in every language and no empty text.

## Services and factories

All services are interfaces in `Riegel.Core.Services` and reachable through `RiegelServices.CreateDefault(IPlatform)`
(or `RiegelServices.Create(IPlatform, TimeProvider)` with a fixed clock). Each implementation is created by the
factory in its area folder, `public static I… Create(RiegelServices services)`, so a service can use the others; the
signatures stay fixed.

| Factory (folder) | Interface | Owner | Behaviour |
|---|---|---|---|
| `CatalogueFactory` (Catalogue/) | `ICatalogueProvider` | engine | loads the embedded `rules/**` (and a `riegel.catalogue/1` stream) with size and depth limits, checks the closed vocabulary and the semantics (RGL-0201–0212), combines customer baselines; SHA-256 over the canonical document |
| `FactsFactory` (Facts/) | `IFactsDeriver` | engine | every fact of spec 2.5; unknown is `null`; `defender.mode`, `av.thirdPartyActive` (WSC before SecurityCenter2, then flag Heuristic), `rdp.enabled` (policy before the local value), network and capability facts |
| `EvaluationFactory` (Evaluation/) | `IEvaluator` | engine | all 27 check types, evidence classes, three-valued logic, Defender gating, value source, flags; never crashes – a program error makes that rule not checkable (RGL-0239) |
| `SnapshotsFactory` (Snapshots/) | `ISnapshotReader` | parsing | reads `.snapshot.json`/`.json.gz` with every limit of spec 7.12, checks the sidecar, redacts again, recomputes `parsed:*`, reads built-in bundles |
| `AttributionFactory` (Attribution/) | `IValueSourceAttributor` | parsing | `domainGpo` (experimental), `localPolicy`, `mdm`, `directlySet` (only as admin with gpresult), otherwise `unknown` |
| `BaselinesFactory` (Baselines/) | `IBaselineImporter` | parsing | GPO backups, `riegel.baseline/1` JSON and the CSV template; stable `IMP-` IDs, collision detection, source SHA-256 |
| `PlantsFactory` (Plants/) | `IPlantStore` | workflow | plant folder with atomic writes, `.riegel.lock`, optimistic concurrency, history, import records, read-only mode |
| `RegistersFactory` (Registers/) | `IRegisterService` | workflow | load/save sorted with history, validation RGL-0420–0447, apply (most specific scope wins), approve, templates |
| `ComparisonFactory` (Comparison/) | `IRunComparer` | workflow | status changes of every kind, raw-value drift without the volatile list, sampling |
| `ScoringFactory` (Scoring/) | `IScoring` | workflow | spec 2.6 with the audit cap, no score for domain controllers |
| `RunsFactory` (Runs/) | `IRunService` | workflow | read → facts → evaluate → registers → score → flags → store; byte-identical results for the same inputs |
| `ReportDataFactory` (ReportData/, namespace `Riegel.Core.ReportBuilding`) | `IReportDataBuilder` | workflow | report, summary, target configuration, approval sheet and collection record data |
| `PdfFactory` (Reports/Pdf/) | `IPdfWriter` | pdf | O4/O5, O6, O12, O13, O14 with PDFsharp-MigraDoc 6.2.4; CJK from a system TrueType face or English with RGL-0510 |
| `TabularFactory` (Reports/Tabular/) | `ITabularWriter` | data-outputs | O7 and O13 workbooks, O3 CSV with the formula-injection guard |
| `EvidenceFactory` (Evidence/) | `IEvidencePack` | data-outputs | O9 pack, `Verify` (re-evaluation in memory) and `CompareSample` |
| `RemediationFactory` (Remediation/) | `IRemediationWriter` | data-outputs | O10 files only (`.reg` and rollback, `GptTmpl.inf`, `audit.csv`, README) |
| `PlatformFactory` (Riegel.Platform.Windows) | `IPlatform` | collector | collection through the linked collector code, elevation hand-off, shell, collector bundle export |

`NullPlatform` (Linux, tests) has `CanCollect = false`; every Windows-only call fails with `RGL-0012`.

The command line's public entry is `Riegel.Commands.CommandApp.Run(string[] args, IPlatform platform, TextWriter
output, TextWriter error)` → exit code. Test support for packages: `SnapshotBuilder`, `RuleBuilder`, `FixedClock`,
`TempPlant`, `TempFolder`, `SchemaValidator`, `PdfDom`, `PdfLayout`, `SampleSets` (in `tests/Riegel.Tests/TestSupport`),
and `Riegel.Core.Samples.SampleData` for deterministic report data.

## Error codes

Codes are `RGL-nnnn`; texts are localised, the code stays (guideline 13). Ranges (spec 7.13), each with its class:

| Range | Class (folder) | Owner |
|---|---|---|
| 0001–0099 | `RglCodesCommon` (Riegel.Core/Model) | scaffold |
| 0100–0199 | `RglCodesCollector` (Riegel.Collector/Collection) | collector |
| 0200–0249 | `RglCodesEngine` (Riegel.Core/Evaluation) | engine |
| 0250–0299 | `RglCodesParsing` (Riegel.Core/Parsing) | parsing |
| 0300–0399 | `RglCodesCatalogue` (tools/RuleTool) | rules-a/b/c |
| 0400–0449 | `RglCodesWorkflow` (Riegel.Core/Plants) | workflow |
| 0450–0499 | `RglCodesCli` (Riegel.Commands) | cli |
| 0500–0549 | `RglCodesPdf` (Riegel.Core/Reports/Pdf) | pdf |
| 0550–0599 | `RglCodesDataOutputs` (Riegel.Core/Evidence) | data-outputs |
| 0600–0699 | `RglCodesApp` (Riegel) | app |

Common codes: `RGL-0001` unexpected, `0002` not found, `0003` not available yet, `0004` invalid JSON, `0005`
unsupported schema, `0006` limit exceeded, `0007` I/O, `0008` access denied, `0009` hash mismatch, `0010` settings
unreadable, `0011` defaults unreadable, `0012` platform not supported, `0013` invalid value, `0014` read-only mode,
`0015` diagnostics package failed, `0016` already exists. Every code of every range with its English text:
[Error codes](error-codes.md) (generated from these classes; a test keeps it current).

## Contract reference

Generated from the public types of `Riegel.Core` (2026-10-18). Records are listed with their properties in declaration
order (which is also the JSON order); `?` marks optional values. A test keeps this list complete.

### `Riegel.Core.Model`

- **AbsentCapability** (enum): NotApplicable, Fail
- **AbsentOutcome** (enum): Fail, Pass, NotApplicable
- **AccountPolicyCheck** (record : CheckSpec): `AccountPolicyField Field`, `CheckOperator Op`, `JsonElement? Expected`
- **AccountPolicyField** (enum): MinPwLen, MaxPwAgeDays, MinPwAgeDays, PwHistLen, LockoutThreshold, LockoutDurationMin, LockoutWindowMin, PasswordComplexity, ClearTextPassword
- **AllOfCheck** (record : CheckSpec): `IReadOnlyList<CheckSpec> Checks`
- **AnyOfCheck** (record : CheckSpec): `IReadOnlyList<CheckSpec> Checks`
- **ApiPlanItem** (record): `string Key`, `string Api`, `bool NeedsAdmin`, `PlanMode Mode`, `IReadOnlyList<string>? Rights`, `string? Sid`, `string? Condition`, `string? Note`
- **Applicability** (record): `int? MinBuild`, `int? MaxBuild`, `DomainScope Domain`, `bool ExcludeDc`, `string? Capability`, `AbsentCapability IfCapabilityAbsent`, `IReadOnlyList<string> Os`, `IReadOnlyList<FactCondition> When`
- **Approval** (record): `string? ApprovedByRole`, `string? ApprovedByName`, `DateOnly? ApprovedOn`, `DateOnly? ReviewBy`, `string? ApprovalRecord`, `string? ApprovedContentSha256`, `static Approval None`
- **ApprovedState** (record): `string Schema`, `string HostName`, `string RunId`, `string SnapshotSha256`, `string ResultsSha256`, `string CatalogueVersion`, `RegisterHashes Registers`, `Approval Approval`
- **AsrAccept** (enum): Block, BlockOrWarn, BlockOrAudit
- **AsrRuleCheck** (record : CheckSpec): `string Guid`, `AsrAccept Accept`
- **AttributionMap** (record): `bool Determinable`, `IReadOnlyDictionary<string, ValueAttribution> Entries`, `static AttributionMap Empty`, `static string KeyOf(string registryKey, string valueName)`, `ValueAttribution Find(string registryKey, string valueName)`
- **AuditAction** (record): `string Guid`, `string Subcategory`, `AuditSetting Setting`
- **AuditExpectation** (enum): Success, Failure, SuccessAndFailure, IncludesSuccess, IncludesFailure, NoAuditing
- **AuditSetting** (enum): None, Success, Failure, SuccessAndFailure
- **AuditSubcategoryCheck** (record : CheckSpec): `string Guid`, `AuditExpectation Expected`
- **AvStatusCheck** (record : CheckSpec): `int? MaxSignatureAgeDays`
- **BaselineFile** (record): `string Schema`, `string Id`, `string Name`, `string? Description`, `string? SourceFileName`, `string? SourceSha256`, `DateTimeOffset? ImportedAtUtc`, `IReadOnlyList<BaselineRule> Rules`
- **BaselineRef** (record): `string Id`, `string Name`, `BaselineSourceKind Kind`, `string SourceSha256`, `string? SourceFileName`, `DateTimeOffset ImportedAtUtc`, `int RuleCount`
- **BaselineRule** (record): `string? Id`, `string Title`, `Severity? Severity`, `string? Category`, `CheckSpec Check`, `string? Rationale`, `string? Reference`
- **BaselineSourceKind** (enum): GpoBackup, RiegelJson, Csv
- **BuildEntry** (record): `int Build`, `string Name`, `string Source`, `DateOnly Seen`, `string? Os`, `IReadOnlyList<BuildRelease> Releases`
- **BuildRelease** (record): `int Ubr`, `DateOnly Date`, `string? Kb`, `string? Kind`
- **BuildTable** (record): `string Schema`, `IReadOnlyList<BuildEntry> Builds`, `static BuildTable Empty`, `BuildEntry? Find(int build, string? osKind)`, `static string? OsKindOf(int? productType)`
- **CanonicalValue** (static class): `const string Redacted`, `const string RedactedEmpty`, `const int BinaryPrefixBytes`, `static string Render(JsonElement? value)`, `static string RenderRegistry(JsonElement registryValue)`, `static string RenderRegistry(string? type, JsonElement v, bool truncated = false)`, `static string Binary(ReadOnlySpan<byte> bytes, bool truncated = false)`, `static bool AreEqual(string? a, string? b)`, `static bool TryParseInteger(string text, out ulong value)`, `static string Display(string? canonical)`
- **CapabilityCondition** (record): `string? Os`, `int? MinBuild`, `int? MaxBuild`, `IReadOnlyList<string>? Editions`, `IReadOnlyList<string>? InstallationTypes`, `string? Note`, `int? MinUbr`
- **CapabilityEntry** (record): `string Capability`, `string Source`, `DateOnly Seen`, `string? Note`, `IReadOnlyList<CapabilityCondition> Exists`
- **CapabilityFile** (record): `string Schema`, `IReadOnlyList<CapabilityEntry> Capabilities`
- **CapabilityTable** (record): `IReadOnlyList<CapabilityEntry> Capabilities`, `static CapabilityTable Empty`
- **CatalogueData** (record): `OfficeDetection? OfficeDetection`, `IReadOnlyList<RemoteAccessTool> RemoteAccessTools`, `static CatalogueData Empty`
- **CatalogueDocument** (record): `string Schema`, `string Id`, `string Version`, `DateOnly ReleasedOn`, `IReadOnlyList<Rule>? Rules`, `LifecycleTable? Lifecycle`, `BuildTable? Builds`, `CapabilityTable? Capabilities`, `CatalogueData? Data`
- **CatalogueRef** (record): `string Id`, `string Version`, `DateOnly ReleasedOn`, `string Sha256`
- **CategoryScore** (record): `string Category`, `int Pass`, `int Fail`, `int Exception`, `int? Score`, `double Weight`, `bool Capped`
- **CheckOperator** (enum): Eq, Ne, Lt, Le, Gt, Ge, In, NotIn, BitsSet, BitsClear, ContainsAll, ContainsNone, Exists, NotExists, AgeDaysLe, AgeDaysGt
- **CheckSpec** (record): `string Type` (not in JSON)
- **CheckTypes** (static class): `static IReadOnlyList<string> All`, `const string Registry`, `const string RegistryEffective`, `const string Secedit`, `const string AccountPolicy`, `const string AuditSubcategory`, `const string UserRight`, `const string Service`, `const string Feature`, `const string WmiProperty`, `const string DefenderSetting`, `const string AsrRule`, `const string FirewallProfile`, `const string FirewallExposure`, `const string Lifecycle`, `const string UpdateAge`, `const string LsaProtection`, `const string DeviceGuard`, `const string AvStatus`, `const string LocalAccount`, `const string LocalAccounts`, `const string LocalGroup`, `const string Inventory`, `const string FirstOf`, `const string AllOf`, `const string AnyOf`, `const string NoneOf`, `const string Not`
- **CollectOptions** (record): `bool Minimal`, `bool Deep`, `bool Pseudonymize`
- **CollectionOptions** (record): `bool Minimal`, `bool Deep`, `bool Pseudonymized`
- **CollectionPlan** (record): `string Schema`, `int Version`, `PlanLimits Limits`, `RegistryPlan Registry`, `IReadOnlyList<WmiPlanItem> Wmi`, `IReadOnlyList<ApiPlanItem> Api`, `IReadOnlyList<EventPlanItem> Events`, `IReadOnlyList<RawPlanItem> Raw`, `IReadOnlyList<string> AllowedWmiMethods`, `IReadOnlyList<PlanSource> Sources`
- **CollectorInfo** (record): `string Name`, `string Version`, `string? ExeSha256`, `string Clr`, `bool Is64BitProcess`
- **ComparisonBasis** (enum): Previous, Approved, Sample
- **ContentHashes** (static class): `static string Of(ExceptionEntry e)`, `static string Of(TailoringEntry e)`
- **CoverageEntry** (record): `string Root`, `bool Recursive`, `int MaxDepth`, `bool Complete`, `string? TruncatedAt`, `IReadOnlyList<string> DeniedSubkeys`
- **DataFile** (record): `string Schema`, `DataKind Kind`, `IReadOnlyList<RemoteAccessTool>? RemoteAccessTools`, `OfficeDetection? OfficeDetection`
- **DataKind** (enum): RemoteAccessTools, OfficeDetection
- **DefenderSettingCheck** (record : CheckSpec): `DefenderSource Source`, `string Property`, `CheckOperator Op`, `JsonElement? Expected`
- **DefenderSource** (enum): Status, Preference
- **DetectionKey** (record): `string Key`, `string? Value`, `string? Source`, `DateOnly? Seen`, `string? Note`
- **DetectionPattern** (record): `string Pattern`, `string? Source`, `DateOnly? Seen`, `string? Note`
- **DeviceGuardCheck** (record : CheckSpec): `DeviceGuardService Service`, `bool RequireRunning`
- **DeviceGuardService** (enum): CredentialGuard, Hvci, Vbs
- **DomainScope** (enum): Any, Joined, Standalone
- **DriftKind** (enum): Added, Removed, Changed
- **EffectiveSelection** (enum): Value, Root
- **EffectiveSource** (record): `string Key`, `string Value`, `ValueOrigin Origin`
- **EvaluationContext** (record): `DateOnly EvaluationDate`, `DateTimeOffset EvaluatedAtUtc`, `DateTimeOffset? ImportedAtUtc`, `int UpdateMaxDays`, `int UnusedAccountDays`, `bool EsuCovered`, `string Language`
- **EventPlanItem** (record): `string Key`, `string Log`, `string Provider`, `int EventId`, `string Xpath`, `bool AfterLastBoot`, `bool NeedsAdmin`, `PlanMode Mode`, `string? Note`
- **EvidenceBaseline** (record): `BaselineRef Baseline`, `bool ContentIncluded`, `FileRef? File`
- **EvidenceManifest** (record): `string Schema`, `string PlantId`, `string PlantName`, `DateTimeOffset CreatedAtUtc`, `DateOnly EvaluationDate`, `ToolInfo Tool`, `CatalogueRef Catalogue`, `FileRef CatalogueFile`, `RegisterHashes Registers`, `string? OperatorRole`, `IReadOnlyList<EvidenceBaseline> Baselines`, `IReadOnlyList<EvidenceRun> Runs`, `IReadOnlyList<FileRef> Files`
- **EvidenceRequest** (record): `Plant Plant`, `IReadOnlyList<string>? Hosts`, `DateOnly EvaluationDate`, `bool IncludeBaselineContent`, `string? OperatorRole`, `string Language`, `string OutDir`, `IReadOnlyList<string> Documents`
- **EvidenceRun** (record): `string RunId`, `string HostName`, `FileRef Results`, `FileRef Snapshot`
- **ExceptionEntry** (record): `string Id`, `string RuleId`, `RegisterScope Scope`, `ReasonCategory Category`, `string Justification`, `string? CompensatingMeasure`, `string? Reference`, `string? AcceptedValue`, `Approval Approval`, `DateOnly CreatedOn`, `string? CreatedByRole`, `DateOnly? ModifiedOn`, `string? CatalogueVersion`, `string? Comment`
- **ExceptionFile** (record): `string Schema`, `string PlantId`, `IReadOnlyList<ExceptionEntry> Exceptions`
- **FactCondition** (record): `string Fact`, `string Op`, `JsonElement Value`
- **FeatureCheck** (record : CheckSpec): `string Name`, `AbsentOutcome IfAbsent`, `IReadOnlyList<int> InstallState`
- **FileNames** (static class): `const int MaxLength`, `static string Host(string hostName)`, `static string Token(string? text, string fallback = "x")`, `static string Timestamp(DateTimeOffset value)`, `static string Date(DateOnly value)`
- **FileRef** (record): `string Path`, `string Sha256`
- **FirewallExposureCheck** (record : CheckSpec): `ProfileScope Profiles`, `IReadOnlyList<PortSpec> Ports`
- **FirewallProfileCheck** (record : CheckSpec): `FirewallProfileName Profile`, `FirewallProfileProperty Property`, `bool Expected`
- **FirewallProfileName** (enum): Domain, Private, Public
- **FirewallProfileProperty** (enum): Enabled, DefaultInboundBlock, LogDropped, AllowLocalRules
- **FirstOfCheck** (record : CheckSpec): `IReadOnlyList<CheckSpec> Checks`
- **Frameworks** (static class): `static IReadOnlyList<string> SupportedEvidence`
- **HandoffResult** (record): `bool Success`, `string? SnapshotPath`, `int ExitCode`, `string? ErrorCode`, `string? Message`, `bool DifferentUserProfile`
- **HashCheck** (record): `HashCheckStatus Status`, `string? Expected`, `string Actual`, `string? SidecarPath`
- **HashCheckStatus** (enum): Ok, Mismatch, Missing, Malformed
- **Hashes** (static class): `static string Sha256Hex(ReadOnlySpan<byte> data)`, `static string Sha256Hex(string text)`, `static string Sha256File(string path)`, `static string Short(string sha256)`, `static bool IsSha256(string? text)`
- **ICatalogue** (interface): `string Id`, `string Version`, `DateOnly ReleasedOn`, `string Sha256`, `IReadOnlyList<Rule> Rules`, `LifecycleTable Lifecycle`, `BuildTable Builds`, `CapabilityTable Capabilities`, `CatalogueData Data`, `Rule Find(string id)`
- **ImportOptions** (record): `string? CollectedByRole`, `string? WitnessRole`, `string? OperatorRole`, `bool Evaluate`, `ReadLimits? Limits`, `bool SkipKnown` (skip exports already in the plant with a run; if all are known, no import record is written and the returned record has no files)
- **ImportRecord** (record): `string Schema`, `string Id`, `DateTimeOffset ImportedAtUtc`, `string? CollectedByRole`, `string? WitnessRole`, `string? OperatorRole`, `IReadOnlyList<ImportedFile> Files`
- **ImportedBaseline** (record): `BaselineRef Baseline`, `bool ContentIncluded`, `IReadOnlyList<Rule> Rules`
- **ImportedFile** (record): `string FileName`, `string Sha256`, `bool SidecarOk`, `string HostName`, `DateTimeOffset CollectedAtUtc`, `string CollectorName`, `string CollectorVersion`, `string? CollectorExeSha256`, `bool IsAdmin`, `CollectionOptions Options`, `SnapshotOrigin Origin`, `string? RunId`
- **InventoryCheck** (record : CheckSpec): `InventoryRenderer Renderer`, `IReadOnlyList<string> Items`
- **InventoryRenderer** (enum): LocalAdmins, RemoteAccessTools, RdpState, DefenderExclusions, Shares, AdminShares, NetworkFacts, AppControlState, Wireless
- **IssueSeverity** (enum): Warning, Error
- **ItemKeys** (static class): `const string NetUserModals`, `const string NetUserEnum`, `const string LsaRights`, `const string AuditPolicy`, `const string LsassProtection`, `const string WininitEvent12`, `const string Firewall`, `const string WindowsUpdateHistory`, `const string WinHttpProxy`, `const string WscAntivirus`, `const string RawSeceditInf`, `const string RawRegistryPol`, `const string RawGpresultXml`, `const string ParsedSecedit`, `const string ParsedRegistryPol`, `const string ParsedGpresult`, `const string ParsedFirewallRulesRegistry`, `const string LocalAdministrators`, `const string LocalRemoteDesktopUsers`, `static string Registry(string path)`, `static string Wmi(string ns, string className)`, `static string WmiMethod(string ns, string className, string method)`, `static string LocalGroup(string sid)`, `static bool Same(string a, string b)`
- **ItemStatus** (enum): Ok, ValueMissing, KeyMissing, Denied, NotApplicable, NamespaceMissing, NotCollected, Error
- **JsonValues** (static class): `static JsonElement Null`, `static JsonElement True`, `static JsonElement False`, `static JsonElement Text(string value)`, `static JsonElement Number(long value)`, `static JsonElement Number(ulong value)`, `static JsonElement Bool(bool value)`, `static JsonElement Texts(IEnumerable<string> values)`, `static JsonElement Parse(string json)`, `static JsonElement Build(Action<Utf8JsonWriter> write)`
- **LifecycleCheck** (record : CheckSpec): –
- **LifecycleEntry** (record): `string Id`, `string Name`, `string Os`, `int Build`, `IReadOnlyList<string>? Editions`, `IReadOnlyList<string>? InstallationTypes`, `string? Channel`, `DateOnly? MainstreamEnd`, `DateOnly EndOfSupport`, `DateOnly? EsuEnd`, `string Source`, `DateOnly Seen`, `string? CrossCheck`, `string? Note`
- **LifecycleTable** (record): `string Schema`, `IReadOnlyList<LifecycleEntry> Products`, `static LifecycleTable Empty`
- **LocalAccountCheck** (record : CheckSpec): `int Rid`, `LocalAccountExpectation Expect`
- **LocalAccountCondition** (enum): PasswordNeverExpires, UnusedDays, PasswordNotRequired
- **LocalAccountExpectation** (enum): Disabled
- **LocalAccountsCheck** (record : CheckSpec): `LocalAccountCondition Condition`, `IReadOnlyList<int>? ExcludeRids`, `bool EnabledOnly`
- **LocalGroupCheck** (record : CheckSpec): `string Sid`, `int? MaxMembers`, `IReadOnlyList<string>? AllowedSids`
- **LocalizedText** (record): `IReadOnlyDictionary<string, string> Values`, `const string FallbackLanguage`, `string Get(string? language)`, `bool Has(string language)`, `static LocalizedText Of(params (string, string)[] values)`
- **LsaProtectionCheck** (record : CheckSpec): –
- **MachineGroup** (record): `string Id`, `string Name`, `MachineRole Role`
- **MachineIdentity** (record): `string? MachineType`, `string? SerialNumber`, `string? OrderNumber`, `DateOnly? DeliveryDate`, `string? PlantSoftware`, `string? Builder`, `string? Location`
- **MachineInfo** (record): `string HostName`, `string? DnsDomain`, `int? DomainRole`, `int? ProductType`, `int? Build`, `int? Ubr`, `string? EditionId`, `string? InstallationType`, `string? DisplayVersion`, `string? Architecture`, `string? MachineGuid`, `string? OsLanguage`, `DateTimeOffset? LastBootUtc`
- **MachineRole** (enum): Other, Hmi, EngineeringStation, Historian, Server, Office, JumpHost
- **MeasureEntry** (record): `string Id`, `string RuleId`, `RegisterScope Scope`, `string Action`, `string OwnerRole`, `DateOnly DueDate`, `string? Ticket`, `MeasureState State`, `DateOnly CreatedOn`, `string? CreatedByRole`, `DateOnly? ModifiedOn`, `string? Comment`
- **MeasureFile** (record): `string Schema`, `string PlantId`, `IReadOnlyList<MeasureEntry> Measures`
- **MeasureState** (enum): Planned, Done, Dropped
- **MissingPass** (record): `string Source`, `DateOnly Seen`, `string Note`
- **MultiSzMode** (enum): Set, List
- **NetProtocol** (enum): Tcp, Udp
- **NoRowsOutcome** (enum): NotCheckable, Fail, NotApplicable, Pass
- **NoneOfCheck** (record : CheckSpec): `IReadOnlyList<CheckSpec> Checks`
- **NotCheck** (record : CheckSpec): `CheckSpec Check`
- **OfficeDetection** (record): `IReadOnlyList<DetectionKey> ClickToRunKeys`, `IReadOnlyList<DetectionKey> InstallRootKeys`, `IReadOnlyList<DetectionPattern> UninstallDisplayNamePrefixes`
- **OsDefault** (record): `int? MinBuild`, `int? MaxBuild`, `IReadOnlyList<string>? Editions`, `IReadOnlyList<string>? InstallationTypes`, `bool? DomainJoined`, `JsonElement Value`, `string Source`, `DateOnly Seen`
- **PdfOptions** (record): `string OutDir`, `bool PdfA`, `bool PerMachine`, `bool IncludeAppendix`
- **PlanLimits** (record): `int MaxValuesTotal`, `int RecursiveMaxDepth`, `int MaxBinaryBytes`, `int ToolTimeoutSeconds`, `long MaxRawTextBytes`, `int WindowsUpdateHistoryEntries`, `int TempPathMaxChars`, `int MaxStringChars`
- **PlanMode** (enum): Always, SkipInMinimal, DeepOnly
- **PlanSource** (record): `string Topic`, `string Url`, `DateOnly Seen`
- **Plant** (record): `string Schema`, `string Id`, `string Name`, `string? Description`, `PlantSettings Settings`, `IReadOnlyList<MachineGroup> Groups`, `IReadOnlyList<PlantMachine> Machines`, `string? Folder` (not in JSON), `bool ReadOnly` (not in JSON), `string? FileSha256` (not in JSON)
- **PlantMachine** (record): `string HostName`, `string? GroupId`, `string? Label`, `bool EsuCovered`, `string? Notes`, `MachineIdentity? Identity`
- **PlantSettings** (record): `int WarnDays`, `int StaleDays`, `int UpdateMaxDays`, `int UnusedAccountDays`, `string? Confidentiality`, `string? ReportLanguage`, `ReportPreset ReportPreset`, `string? PerformedBy`, `string? CoverLogoFile`, `string? OperatorRole`, `IReadOnlyList<SignOffRole> SignOff`, `IReadOnlyList<string> RiskAcceptanceRoles`, `static PlantSettings Default`
- **PortSpec** (record): `NetProtocol Protocol`, `int Port`
- **ProfileScope** (enum): Active, All
- **RawKind** (enum): Tool, File
- **RawPlanItem** (record): `string Key`, `RawKind Kind`, `string? Tool`, `IReadOnlyList<string>? Arguments`, `string? File`, `long MaxBytes`, `bool NeedsAdmin`, `PlanMode Mode`, `string? Note`
- **ReadLimits** (record): `long MaxSnapshotBytes`, `int MaxItems`, `int MaxStringChars`, `int MaxJsonDepth`, `long MaxRegisterBytes`, `int MaxRegisterEntries`, `long MaxRegistryPolBytes`, `long MaxTextFileBytes`, `long MaxBaselineCsvBytes`, `int MaxBaselineCsvRows`, `long MaxEvidenceZipBytes`, `long MaxEvidenceEntryBytes`, `long MaxLogoBytes`, `long MaxPlantFileBytes`, `long MaxCatalogueBytes`, `int ToolTimeoutSeconds`, `static ReadLimits Default`
- **ReasonCategory** (enum): VendorRequirement, TechnicalNecessity, CompensatingControl, RiskAccepted, NotRelevantHere
- **RegValueType** (enum): Dword, Qword, Sz, ExpandSz ("expand_sz"), MultiSz ("multi_sz"), Binary
- **RegisterHashes** (record): `string Exceptions`, `string Tailoring`, `string Measures`, `static RegisterHashes Empty`
- **RegisterKind** (enum): Exceptions, Tailoring, Measures
- **RegisterScope** (record): `bool AllMachines`, `IReadOnlyList<string> Machines`, `IReadOnlyList<string> Groups`, `static RegisterScope Empty`
- **RegisterSet** (record): `ExceptionFile Exceptions`, `TailoringFile Tailoring`, `MeasureFile Measures`, `RegisterHashes Hashes`
- **RegistryAction** (record): `string Key`, `string Value`, `RegValueType? Type`, `JsonElement? Data`, `bool Delete`
- **RegistryCheck** (record : CheckSpec): `string Key`, `string Value`, `RegValueType ValueType`, `IReadOnlyList<RegValueType>? AcceptTypes`, `CheckOperator Op`, `JsonElement? Expected`, `WhenMissing WhenMissing`, `IReadOnlyList<OsDefault>? OsDefaults`, `MissingPass? MissingPass`, `MultiSzMode? MultiSz`
- **RegistryEffectiveCheck** (record : CheckSpec): `RegValueType ValueType`, `IReadOnlyList<RegValueType>? AcceptTypes`, `CheckOperator Op`, `JsonElement? Expected`, `WhenMissing WhenMissing`, `IReadOnlyList<OsDefault>? OsDefaults`, `MissingPass? MissingPass`, `MultiSzMode? MultiSz`, `IReadOnlyList<EffectiveSource> Sources`, `EffectiveSelection? Selection`
- **RegistryPlan** (record): `IReadOnlyList<RegistryPlanEntry> Roots`, `IReadOnlyList<RegistryPlanEntry> Keys`
- **RegistryPlanEntry** (record): `string Path`, `RegistryPlanKind Kind`, `int MaxDepth`, `IReadOnlyList<string>? Values`, `bool NeedsAdmin`, `PlanMode Mode`, `string? Note`
- **RegistryPlanKind** (enum): Recursive, Key, Children
- **RemediationExclusion** (record): `string RuleId`, `RemediationExclusionReason Reason`, `string? EntryId`
- **RemediationExclusionReason** (enum): ValidException, PendingException, Tailored, NoRemediation, NotFailing
- **RemediationOutput** (record): `string Directory`, `IReadOnlyList<string> Files`, `IReadOnlyList<string> IncludedRuleIds`, `IReadOnlyList<RemediationExclusion> Excluded`, `IReadOnlyList<string> ManualRuleIds`
- **RemediationSpec** (record): `IReadOnlyList<RegistryAction>? Registry`, `IReadOnlyList<TemplateAction>? SecurityTemplate`, `IReadOnlyList<AuditAction>? Audit`, `LocalizedText? Manual`
- **RemoteAccessTool** (record): `string Product`, `string Source`, `DateOnly Seen`, `string? Note`, `IReadOnlyList<string> ServiceNames`, `IReadOnlyList<string> DisplayNameContains`
- **ReportFileNames** (static class): `static IReadOnlyList<string> RemediationFiles`, `const string ResultsCsv`, `const string CollectorHandover`, `static string Report(string plantName, DateOnly date)`, `static string MachineReport(string plantName, DateOnly date, string host)`, `static string Summary(DateOnly date)`, `static string Workbook(string plantName, DateOnly date)`, `static string Evidence(string plantName, DateOnly date)`, `static string ApprovalSheet(string plantName, DateOnly date)`, `static string TargetConfigPdf(string plantName, DateOnly date)`, `static string TargetConfigWorkbook(string plantName, DateOnly date)`, `static string CollectionRecord(string plantName, string importId)`, `static string RunFile(string host, string runId)`, `static string RemediationFolder(string host, string runId)`
- **ReportPreset** (enum): Audit, AsDelivered
- **ResultFlags** (enum [Flags]): None, Regression, NewRule, ExceptionExpired, ExceptionExpiring, ExceptionPending, ExceptionValueMismatch, ExceptionChangedAfterApproval, TailoringPending, MeasurePlanned, MeasureOverdue, FromDefault, ConfiguredOnly, Heuristic, Derived, StaleSnapshot, ClockSuspect
- **ResultStatus** (enum): Pass, Fail, Exception, OutOfScope, NotApplicable, NotCheckable, Info
- **RglCodesCommon** (static class): `static IReadOnlyList<string> All`, `const string Unexpected`, `const string NotFound`, `const string NotImplemented`, `const string InvalidJson`, `const string UnsupportedSchema`, `const string LimitExceeded`, `const string IoError`, `const string AccessDenied`, `const string HashMismatch`, `const string SettingsUnreadable`, `const string DefaultsUnreadable`, `const string PlatformNotSupported`, `const string InvalidArgument`, `const string ReadOnlyMode`, `const string DiagnosticsFailed`, `const string AlreadyExists`, `const string IcuMissing`, `const string OpenSslMissing`, `static string Message(string code, params object[] args)`
- **RiegelException** (class : Exception): `string Code`, `static RiegelException Common(string code, params object[] args)`
- **RowFilter** (record): `string Property`, `CheckOperator Op`, `JsonElement Value`
- **RowsMode** (enum): Any, All, First
- **Rule** (record): `string Id`, `string Since`, `RuleState State`, `string Category`, `Severity Severity`, `RuleKind Kind`, `LocalizedText Title`, `LocalizedText Rationale`, `LocalizedText? OtNote`, `LocalizedText? Remedy`, `ReasonCategory? SuggestedReason`, `Applicability AppliesTo`, `bool NeedsAdmin`, `CheckSpec Check`, `RemediationSpec? Remediation`, `string RuleSetId`, `IReadOnlyList<RuleReference> References`, `IReadOnlyList<TextSource> TextSources`, `IReadOnlyList<VerificationNote> Verified`, `IReadOnlyList<RuleTestVector> Tests`
- **RuleCategories** (static class): `static IReadOnlyList<string> All`, `const string Audit`, `static bool IsKnown(string category)`
- **RuleFile** (record): `string Schema`, `string Prefix`, `IReadOnlyList<Rule> Rules`
- **RuleKind** (enum): Check, Inventory
- **RuleReference** (record): `string Src`, `string? Doc`, `string? Section`, `string? Setting`, `string? Id`, `IReadOnlyList<string>? Ids`, `string? Url`, `string? Version`
- **RuleResult** (record): `string RuleSetId`, `string RuleId`, `ResultStatus Status`, `Severity Severity`, `string? Expected`, `string? Actual`, `ValueSource Source`, `string? SourceDetail`, `string? ExceptionId`, `string? TailoringId`, `string? MeasureId`, `ResultFlags Flags`, `string? ReasonCode`, `IReadOnlyList<string>? Details`, `IReadOnlyList<string> EvidenceKeys`
- **RuleSources** (static class): `static IReadOnlyList<string> ValueBacking`, `static IReadOnlyList<SourceAttribution> All`
- **RuleState** (enum): Active, Retired
- **RuleTestVector** (record): `string? Name`, `VectorMachine Machine`, `IReadOnlyList<CoverageEntry>? Coverage`, `IReadOnlyDictionary<string, JsonElement>? Facts`, `ResultStatus Expect`, `IReadOnlyList<string>? ExpectFlags`, `IReadOnlyDictionary<string, VectorItem> Items`
- **Run** (record): `string Schema`, `string Id`, `string HostName`, `DateTimeOffset EvaluatedAtUtc`, `DateOnly EvaluationDate`, `DateTimeOffset CollectedAtUtc`, `DateTimeOffset? ImportedAtUtc`, `FileRef Snapshot`, `ToolInfo Tool`, `CatalogueRef Catalogue`, `RegisterHashes Registers`, `bool IsAdmin`, `CollectionOptions Options`, `RunSummary Summary`, `string? PreviousRunId`, `string? ApprovedRunId`, `string? ReEvaluationOf`, `RunFlags Flags`, `IReadOnlyList<BaselineRef> Baselines`, `IReadOnlyList<RuleResult> Results`, `IReadOnlyList<CategoryScore> Categories`, `IReadOnlyDictionary<string, JsonElement> Facts`
- **RunComparison** (record): `string HostName`, `string? FromRunId`, `string ToRunId`, `ComparisonBasis Basis`, `bool MachineIdentityChanged`, `IReadOnlyList<StatusChange> StatusChanges`, `IReadOnlyList<ValueDrift> Drift`
- **RunFlags** (enum [Flags]): None, StaleSnapshot, ClockSuspect, MachineIdentityChanged, Incomplete, DomainController
- **RunIds** (static class): `static string Create(DateTimeOffset evaluatedAtUtc, string snapshotSha256, string catalogueSha256, RegisterHashes registers)`, `static bool IsValid(string? id)`
- **RunInfo** (record): `string Id`, `string HostName`, `DateTimeOffset EvaluatedAtUtc`, `DateOnly EvaluationDate`, `DateTimeOffset CollectedAtUtc`, `string SnapshotSha256`, `string CatalogueVersion`, `string? ReEvaluationOf`, `RunFlags Flags`, `int? Score`, `int OpenHigh`
- **RunOptions** (record): `DateOnly? EvaluationDate`, `DateTimeOffset? ImportedAtUtc`, `string Language`, `bool Store`, `ReadLimits? Limits`, `ICatalogue? Catalogue`, `RegisterSet? Registers`, `string? ImportId`, `Plant? Plant`
- **RunSummary** (record): `int Pass`, `int Fail`, `int Exception`, `int OutOfScope`, `int NotApplicable`, `int NotCheckable`, `int Info`, `int OpenHigh`, `int OpenMedium`, `int OpenLow`, `int Regressions`, `int NewlyOpen`, `int ExceptionsExpiring`, `int ExceptionsExpired`, `int ExceptionsPending`, `int MeasuresOverdue`, `int? Score`, `int? Documented`, `int CheckedShare`
- **SampleComparison** (record): `string HostName`, `bool Consistent`, `string PackedSnapshotSha256`, `string FreshSnapshotSha256`, `DateTimeOffset PackedCollectedAtUtc`, `DateTimeOffset FreshCollectedAtUtc`, `IReadOnlyList<ValueDrift> Differences`
- **SampleResult** (record): `SampleStatus Status`, `SampleComparison? Comparison`, `string? ErrorCode`, `string? Message`, `int ExitCode` (not in JSON)
- **SampleStatus** (enum): Consistent, Different, Error
- **SchemaIds** (static class): `const string Snapshot`, `const string Run`, `const string Exceptions`, `const string Tailoring`, `const string Measures`, `const string Plant`, `const string Approved`, `const string Import`, `const string Baseline`, `const string Evidence`, `const string Catalogue`, `const string CollectionPlan`, `const string Rules`, `const string Lifecycle`, `const string Builds`, `const string Capabilities`, `const string Data`, `const string ExceptionsTemplate`, `const string TailoringTemplate`
- **SeceditCheck** (record : CheckSpec): `string Section`, `string Key`, `CheckOperator Op`, `JsonElement? Expected`, `SeceditValueKind ValueKind`, `WhenMissing WhenMissing`
- **SeceditValueKind** (enum): Number, String, RegistryValue
- **ServiceCheck** (record : CheckSpec): `string Name`, `IReadOnlyList<string>? StartMode`, `IReadOnlyList<string>? State`, `AbsentOutcome IfAbsent`
- **Severity** (enum): Low, Medium, High
- **SignOffRole** (record): `string Step`, `string Role`
- **SignOffSteps** (static class): `const string Created`, `const string Reviewed`, `const string Approved`, `const string Accepted`
- **Snapshot** (record): `string Schema`, `CollectorInfo Collector`, `DateTimeOffset CollectedAtUtc`, `bool IsAdmin`, `SnapshotOrigin Origin`, `CollectionOptions Options`, `MachineInfo Machine`, `IReadOnlyList<CoverageEntry> Coverage`, `IReadOnlyList<SnapshotItem> Items`
- **SnapshotFile** (static class): `const string Extension`, `const string GzipExtension`, `const string SidecarExtension`, `static string FileName(string hostName, DateTimeOffset collectedAtUtc, bool gzip = false)`, `static string SidecarPath(string snapshotPath)`, `static string SidecarLine(string sha256, string fileName)`, `static bool TryParseSidecar(string text, out string sha256, out string fileName)`, `static string Write(Snapshot snapshot, string directory, bool gzip = false)`, `static HashCheck Verify(string snapshotPath)`
- **SnapshotItem** (record): `string Key`, `ItemStatus Status`, `JsonElement? Value`, `int? ErrorCode`, `string? Error`, `int DurationMs`, `bool Truncated`
- **SnapshotOrigin** (enum): Collector, App, Builtin, Recorded, Synthetic
- **StatusChange** (record): `string RuleSetId`, `string RuleId`, `StatusChangeKind Kind`, `ResultStatus? From`, `ResultStatus? To`, `ResultFlags FromFlags`, `ResultFlags ToFlags`, `string? Detail`
- **StatusChangeKind** (enum): Worse, Better, ExceptionAdded, ExceptionRemoved, ExceptionExpired, TailoringAdded, TailoringRemoved, NewRule, ScopeChanged, Other
- **TailoringEntry** (record): `string Id`, `string RuleId`, `RegisterScope Scope`, `string Reason`, `Approval Approval`, `DateOnly CreatedOn`, `string? CreatedByRole`, `DateOnly? ModifiedOn`, `string? Comment`
- **TailoringFile** (record): `string Schema`, `string PlantId`, `IReadOnlyList<TailoringEntry> Entries`
- **TemplateAction** (record): `string Section`, `string Key`, `string? Value`, `IReadOnlyList<string>? Sids`
- **TextSource** (record): `string Src`, `string? Doc`, `string? Section`, `string? Url`, `bool Changed`, `IReadOnlyList<string> Fields`
- **ToolInfo** (record): `string Name`, `string Version`, `string? Commit`, `string? ExeSha256`
- **UpdateAgeCheck** (record : CheckSpec): `int? MaxDays`
- **UserRightCheck** (record : CheckSpec): `string Right`, `UserRightMode Mode`, `IReadOnlyList<string> Sids`
- **UserRightMode** (enum): MustInclude, MustNotInclude, OnlyFrom, Empty
- **ValidationIssue** (record): `RegisterKind Register`, `string? EntryId`, `IssueSeverity Severity`, `string Code`, `string? Field`, `string Message`
- **ValueAttribution** (record): `ValueSource Source`, `string? Detail`, `bool Experimental`
- **ValueDrift** (record): `string Key`, `string? Path`, `string? From`, `string? To`, `DriftKind Kind`
- **ValueOrigin** (enum): Policy, Mdm, Local, Other
- **ValueSource** (enum): Unknown, DomainGpo, LocalPolicy, Mdm, DirectlySet, OsDefault, Runtime, NotSet
- **VectorItem** (record): `ItemStatus Status`, `JsonElement? Value`, `bool Truncated`
- **VectorMachine** (record): `int Build`, `int? Ubr`, `string? Edition`, `string? InstallationType`, `int ProductType`, `int DomainRole`, `bool IsAdmin`
- **VerificationNote** (record): `string Src`, `string? Doc`, `string? Url`, `DateOnly Date`, `string? Section`, `string? Note`
- **VerifyFinding** (record): `string Code`, `string? File`, `string? HostName`, `string? RuleId`, `string Message`
- **VerifyResult** (record): `VerifyStatus Status`, `string EngineVersion`, `string? PackedEngineVersion`, `int SnapshotsChecked`, `int ResultsCompared`, `IReadOnlyList<VerifyFinding> Findings`, `IReadOnlyList<string> NotVerifiable`, `CatalogueRef? Catalogue` (the packed catalogue), `bool CatalogueBuiltIn` (identical to the catalogue built into this Riegel version), `int ExitCode` (not in JSON; 0 reproduced, 1 reproduced with a catalogue that is not the built-in one, 2 mismatch, 3 error)
- **VerifyStatus** (enum): Reproduced, Mismatch, Error
- **WhenMissing** (enum): NotCheckable, Fail, OsDefault, Pass
- **WmiMethodKind** (enum): Static, PerRow
- **WmiPlanItem** (record): `string Key`, `string Namespace`, `string Class`, `string? Where`, `IReadOnlyList<string>? AdminOnlyProperties`, `bool NeedsAdmin`, `PlanMode Mode`, `string? Note`, `IReadOnlyList<string> Properties`, `IReadOnlyList<WmiPlanMethod> Methods`
- **WmiPlanMethod** (record): `string Name`, `WmiMethodKind Kind`, `string? ViaClass`, `string? KeyProperty`, `IReadOnlyList<string>? OutProperties`
- **WmiPropertyCheck** (record : CheckSpec): `string Item`, `string Property`, `RowFilter? RowFilter`, `RowsMode Rows`, `CheckOperator Op`, `JsonElement? Expected`, `NoRowsOutcome IfNoRows`

### `Riegel.Core.Model.Reports`

- **AggregatedFinding** (record): `string RuleSetId`, `string RuleId`, `string Title`, `string Category`, `Severity Severity`, `int OpenCount`, `int ApplicableCount`, `bool MoreHosts`, `MeasureSummary? Measure`, `ResultFlags Flags`, `IReadOnlyList<string> Hosts`
- **ApprovalSheetData** (record): `string PlantId`, `string PlantName`, `string Language`, `DateOnly Date`, `DateTimeOffset GeneratedAtUtc`, `ToolInfo Tool`, `string SheetId`, `string ExceptionsSha256`, `string TailoringSha256`, `string Confidentiality`, `bool PreTranslated`, `IReadOnlyList<ApprovalSheetEntry> Entries`, `IReadOnlyList<SignOffRole> Approvers`
- **ApprovalSheetEntry** (record): `RegisterKind Kind`, `string Id`, `string RuleId`, `string RuleTitle`, `Severity? Severity`, `RegisterScope Scope`, `ReasonCategory? Category`, `string Text`, `string? CompensatingMeasure`, `string? Reference`, `string? AcceptedValue`, `string? Measure`, `string ContentSha256`, `RegisterEntryState State`
- **Banner** (record): `BannerKind Kind`, `int? CheckedShare`, `int? AgeDays`, `IReadOnlyList<string> Hosts`, `IReadOnlyList<string> Areas`
- **BannerKind** (enum): IncompleteCheck, StaleSnapshot, StaleCatalogue, PreTranslation, ExperimentalGpoAttribution, ClockSuspect, DomainController
- **BaselineSection** (record): `BaselineRef Baseline`, `int Pass`, `int Fail`, `int Exception`, `int NotCheckable`, `int? Score`, `bool ContentIncluded`
- **CollectionInfo** (record): `DateTimeOffset CollectedAtUtc`, `int AgeDays`, `DateTimeOffset? ImportedAtUtc`, `bool IsAdmin`, `CollectionOptions Options`, `SnapshotOrigin Origin`, `string CollectorName`, `string CollectorVersion`, `string? CollectorExeSha256`, `string SnapshotSha256`, `string? ImportId`
- **CollectionRecordData** (record): `string PlantId`, `string PlantName`, `string Language`, `DateTimeOffset GeneratedAtUtc`, `ToolInfo Tool`, `ImportRecord Import`, `string Confidentiality`, `bool PreTranslated`
- **DriftSummary** (record): `int Total`, `IReadOnlyList<ValueDrift> Top`, `static DriftSummary None`
- **ExceptionView** (record): `ExceptionEntry Entry`, `RegisterEntryState State`, `RegisterFlags Flags`, `string RuleTitle`, `Severity? Severity`, `string ContentSha256`, `IReadOnlyList<string> AffectedHosts`
- **FactView** (record): `string Name`, `string? Value`
- **HostChange** (record): `string HostName`, `ComparisonBasis Basis`, `string? FromRunId`, `string ToRunId`, `string Title`, `Severity Severity`, `StatusChange Change`
- **InsurerFactsRow** (record): `string HostName`, `string? OsName`, `int? Build`, `DateOnly? SupportEnd`, `bool? Supported`, `string? AvProduct`, `string? AvState`, `int? SignatureAgeDays`, `int? DaysSinceUpdate`, `string? PatchMonth`, `int? LocalAdminCount`, `bool? RdpEnabled`, `bool? RdpNla`, `bool? Smb1`, `bool? BitLocker`, `bool? FirewallOn`, `NetworkFacts Network`
- **InventoryView** (record): `string RuleId`, `string Title`, `InventoryRenderer Renderer`, `IReadOnlyList<string> Items`
- **LifecycleView** (record): `string? ProductName`, `DateOnly? EndOfSupport`, `DateOnly? EsuEnd`, `bool EsuCovered`, `bool? Supported`
- **MachineReport** (record): `string HostName`, `string? Label`, `string? GroupId`, `string? GroupName`, `MachineRole? Role`, `MachineIdentity? Identity`, `MachineInfo Machine`, `CollectionInfo Collection`, `string RunId`, `RunSummary Summary`, `DriftSummary Drift`, `LifecycleView? Lifecycle`, `RunFlags Flags`, `IReadOnlyList<CategoryScore> Categories`, `IReadOnlyList<RuleResultView> Results`, `IReadOnlyList<HostChange> Worse`, `IReadOnlyList<FactView> Facts`, `IReadOnlyList<FactView> NetworkFacts`, `IReadOnlyList<InventoryView> Inventory`, `bool IsDomainController` (not in JSON)
- **MeasureSummary** (record): `string MeasureId`, `string OwnerRole`, `DateOnly DueDate`, `bool Overdue`
- **MeasureView** (record): `MeasureEntry Entry`, `bool Overdue`, `RegisterFlags Flags`, `string RuleTitle`, `Severity? Severity`, `int OpenCount`, `IReadOnlyList<string> AffectedHosts`
- **MethodInfo** (record): `int RulesTotal`, `int CheckRules`, `int InventoryRules`, `ProvenanceCounts Provenance`, `bool ExperimentalGpoAttribution`, `IReadOnlyList<SourceAttribution> Sources`, `IReadOnlyList<string> Frameworks`
- **NetworkFacts** (record): `bool? GatewayConfigured`, `int? ActiveAdapters`, `bool? Wireless`, `bool? IpForwarding`, `bool? ProxyConfigured`
- **ProvenanceCounts** (record): `int Total`, `int Acsc`, `int MsDocs`, `int MsLearnFacts`, `int Disa`, `int AlsoInMsBaseline`
- **RegisterEntryState** (enum): Valid, Pending, ChangedAfterApproval, Expiring, Expired, Invalid
- **RegisterFlags** (enum [Flags]): None, Unused, Overlap, RuleRetired, MeasureDoneButFailing, ValueMismatch
- **ReportCounts** (record): `int Machines`, `int MachinesScored`, `int OpenHigh`, `int OpenMedium`, `int OpenLow`, `int Regressions`, `int NewlyOpen`, `int NotCheckable`, `int ExceptionsValid`, `int ExceptionsExpiring`, `int ExceptionsExpired`, `int ExceptionsPending`, `int TailoringEntries`, `int MeasuresPlanned`, `int MeasuresOverdue`, `int UnsupportedOs`, `int StaleSnapshots`, `int DomainControllers`
- **ReportData** (record): `ReportHeader Header`, `ReportCounts Counts`, `ScoreView Score`, `SignOffData SignOff`, `MethodInfo Method`, `IReadOnlyList<Banner> Banners`, `IReadOnlyList<CategoryScore> Categories`, `IReadOnlyList<AggregatedFinding> Findings`, `IReadOnlyList<HostChange> Changes`, `IReadOnlyList<ExceptionView> ExpiredExceptions`, `IReadOnlyList<ExceptionView> Exceptions`, `IReadOnlyList<TailoringView> Tailoring`, `IReadOnlyList<MeasureView> Measures`, `IReadOnlyList<MachineReport> Machines`, `IReadOnlyList<InsurerFactsRow> InsurerFacts`, `IReadOnlyList<BaselineSection> Baselines`
- **ReportFormat** (enum): Pdf, Xlsx, Csv, Json
- **ReportHeader** (record): `string PlantId`, `string PlantName`, `string? PlantDescription`, `string ReportId`, `ReportPreset Preset`, `string Language`, `DateOnly EvaluationDate`, `DateTimeOffset GeneratedAtUtc`, `ToolInfo Tool`, `CatalogueRef Catalogue`, `RegisterHashes Registers`, `string? PerformedBy`, `string? LogoPath`, `string Confidentiality`, `bool PreTranslated`, `IReadOnlyList<BaselineRef> Baselines`, `IReadOnlyList<string> Hosts`
- **ReportRequest** (record): `Plant Plant`, `IReadOnlyList<string>? Hosts`, `DateOnly EvaluationDate`, `string Language`, `ReportPreset Preset`, `bool PerMachine`, `bool IncludeAppendix`, `bool Evidence`, `bool IncludeBaselineContent`, `bool PdfA`, `string OutDir`, `IReadOnlyList<ReportFormat> Formats`
- **RuleResultView** (record): `RuleResult Result`, `string Title`, `string Category`, `RuleKind Kind`, `string? Rationale`, `string? OtNote`, `string? Remedy`, `IReadOnlyList<string> References`
- **ScoreView** (record): `int? Score`, `int? Documented`, `int CheckedShare`, `bool Greyed`, `bool AuditCapped`
- **SignOffData** (record): `string ExceptionsSha256`, `int ExceptionCount`, `int ExceptionValidCount`, `string TailoringSha256`, `int TailoringCount`, `string MeasuresSha256`, `int MeasureCount`, `string ReportId`, `IReadOnlyList<SignOffRole> Roles`, `IReadOnlyList<string> RunIds`
- **SourceAttribution** (record): `string Src`, `string Name`, `string Licence`, `string Attribution`, `string? Url`
- **SummaryData** (record): `DateOnly Date`, `string Language`, `DateTimeOffset GeneratedAtUtc`, `ToolInfo Tool`, `string Confidentiality`, `bool PreTranslated`, `IReadOnlyList<SummaryPlantRow> Plants`, `IReadOnlyList<AggregatedFinding> TopFindings`, `IReadOnlyList<Banner> Banners`
- **SummaryPlantRow** (record): `string PlantId`, `string PlantName`, `int Machines`, `int? SupportedOsPercent`, `int OpenHigh`, `int Regressions`, `int ExceptionsExpiring`, `int MeasuresOverdue`, `int? Score`, `int? Documented`, `int CheckedShare`, `bool Incomplete`, `IReadOnlyList<TrendPoint> Trend`
- **TailoringView** (record): `TailoringEntry Entry`, `RegisterEntryState State`, `RegisterFlags Flags`, `string RuleTitle`, `Severity? Severity`, `string ContentSha256`, `IReadOnlyList<string> AffectedHosts`
- **TargetConfigData** (record): `string PlantId`, `string PlantName`, `string Language`, `DateOnly Date`, `DateTimeOffset GeneratedAtUtc`, `ToolInfo Tool`, `CatalogueRef Catalogue`, `ProvenanceCounts Provenance`, `string TailoringSha256`, `string Confidentiality`, `bool PreTranslated`, `IReadOnlyList<TargetConfigGroup> Groups`, `IReadOnlyList<BaselineRef> Baselines`, `IReadOnlyList<SignOffRole> SignOff`
- **TargetConfigGroup** (record): `string? GroupId`, `string Name`, `MachineRole? Role`, `IReadOnlyList<string> Hosts`, `IReadOnlyList<TargetConfigRule> Rules`, `IReadOnlyList<TailoringView> Tailoring`
- **TargetConfigRule** (record): `string RuleSetId`, `string RuleId`, `string Title`, `string Category`, `Severity Severity`, `RuleKind Kind`, `string Expectation`, `bool AlsoInMsBaseline`, `string? AppliesTo`, `IReadOnlyList<string> Sources`
- **TrendPoint** (record): `DateOnly MonthEnd`, `int? OpenHigh`

### `Riegel.Core.Model.Json`

- **CamelCaseEnumConverter`1** (class : JsonStringEnumConverter<TEnum>): –
- **FlagsArrayConverter`1** (class : JsonConverter<TEnum>): `TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)`, `void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)`, `static IReadOnlyList<string> Names(TEnum value)`
- **LocalizedTextConverter** (class : JsonConverter<LocalizedText>): `LocalizedText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)`, `void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options)`
- **RegisterFlagsConverter** (class : FlagsArrayConverter<RegisterFlags>): –
- **ResultFlagsConverter** (class : FlagsArrayConverter<ResultFlags>): –
- **RiegelJson** (static class): `static JsonSerializerOptions Options`, `static RiegelJsonContext Context`, `static JsonTypeInfo<T> TypeInfo<T>()`, `static string Serialize<T>(T? value)`, `static byte[] SerializeToUtf8Bytes<T>(T? value)`, `static byte[] SerializeCompact<T>(T? value)`, `static T Deserialize<T>(ReadOnlySpan<byte> utf8)`, `static T Deserialize<T>(string json)`, `static T ReadFile<T>(string path, long maxBytes)`, `static string WriteFile<T>(string path, T? value)`
- **RiegelJsonContext** (class : JsonSerializerContext): source-generated metadata for every contract type; read and write only through `RiegelJson`
- **RunFlagsConverter** (class : FlagsArrayConverter<RunFlags>): –
- **UtcDateTimeOffsetConverter** (class : JsonConverter<DateTimeOffset>): `DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)`, `static bool TryParse(string text, out DateTimeOffset value)`, `void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)`, `static string Format(DateTimeOffset value)`

### `Riegel.Core.Services`

- **IBaselineImporter** (interface): `ImportedBaseline Import(string path, string name, ReadLimits l)`
- **ICatalogueProvider** (interface): `ICatalogue LoadEmbedded()`, `ICatalogue Load(Stream catalogueJson, ReadLimits l)`, `ICatalogue Combine(ICatalogue core, IEnumerable<ImportedBaseline> b)`
- **IEvaluator** (interface): `IReadOnlyList<RuleResult> Evaluate(Snapshot s, ICatalogue c, EvaluationContext ctx)`
- **IEvidencePack** (interface): `string Write(EvidenceRequest r)`, `VerifyResult Verify(string zipPath, ReadLimits l)`, `SampleResult CompareSample(string freshSnapshotPath, string zipPath, string? host, ReadLimits l)`
- **IFactsDeriver** (interface): `IReadOnlyDictionary<string, JsonElement> Derive(Snapshot s, ICatalogue c)`
- **IPdfWriter** (interface): `string WriteReport(ReportData d, PdfOptions o)`, `string WriteSummary(SummaryData d, PdfOptions o)`, `string WriteTargetConfig(TargetConfigData d, PdfOptions o)`, `string WriteApprovalSheet(ApprovalSheetData d, PdfOptions o)`, `string WriteCollectionRecord(CollectionRecordData d, PdfOptions o)`
- **IPlantStore** (interface): `Plant Create(string dir, string name)`, `Plant Open(string dir, bool readOnly)`, `void SavePlant(Plant p, string expectedSha256)`, `ImportRecord Import(Plant p, IReadOnlyList<string> paths, ImportOptions o)`, `void SaveRun(Plant p, Run r)`, `IReadOnlyList<RunInfo> ListRuns(Plant p, string host)`, `Run LoadRun(Plant p, string runId)`, `string RunSnapshotPath(Plant p, Run r)`, `ApprovedState LoadApproved(Plant p, string host)`, `void SaveApproved(Plant p, ApprovedState a)`
- **IPlatform** (interface): `bool CanCollect`, `bool IsElevated`, `Snapshot CollectThisMachine(CollectOptions o, CancellationToken ct)`, `string CreateHandoffDirectory(string token)`, `HandoffResult RunElevatedCollect(string token, CancellationToken ct)`, `void OpenWithShell(string path)`, `string ExportCollectorBundle(string targetDir)`
- **IRegisterService** (interface): `RegisterSet Load(Plant p)`, `void Save(Plant p, RegisterSet r, RegisterHashes expected)`, `IReadOnlyList<RuleResult> Apply(IReadOnlyList<RuleResult> raw, RegisterSet r, Plant p, string host, DateOnly today)`, `IReadOnlyList<ValidationIssue> Validate(RegisterSet r, ICatalogue c, Plant p, DateOnly today)`, `RegisterSet Approve(RegisterSet r, RegisterKind kind, IReadOnlyList<string> ids, Approval a)`, `RegisterSet ImportTemplate(RegisterSet r, string templatePath, ReadLimits l)`
- **IRemediationWriter** (interface): `RemediationOutput Write(Run r, Snapshot s, ICatalogue c, RegisterSet reg, string outDir, string lang)`
- **IReportDataBuilder** (interface): `ReportData Build(ReportRequest r)`, `SummaryData BuildSummary(IReadOnlyList<Plant> plants, DateOnly d, string lang)`, `TargetConfigData BuildTargetConfig(Plant p, string? groupId, string lang)`, `ApprovalSheetData BuildApprovalSheet(Plant p, IReadOnlyList<string>? ids, string lang)`, `CollectionRecordData BuildCollectionRecord(Plant p, string importId, string lang)`
- **IRunComparer** (interface): `RunComparison Compare(Run? from, Snapshot? fromSnapshot, Run to, Snapshot toSnapshot, ComparisonBasis b)`, `SampleComparison CompareSnapshots(Snapshot packed, Snapshot fresh)`
- **IRunService** (interface): `Run EvaluateAndStore(Plant p, string snapshotPath, RunOptions o)`, `Run EvaluateOnly(Snapshot s, RunOptions o)`
- **IScoring** (interface): `(RunSummary, IReadOnlyList<CategoryScore>) Score(IReadOnlyList<RuleResult> r, ICatalogue c, Run? previous, bool isDc)`
- **ISnapshotReader** (interface): `Snapshot Read(string path, ReadLimits l)`, `HashCheck VerifySidecar(string path)`, `Snapshot FromBuiltinBundle(string folder, ReadLimits l)`
- **ITabularWriter** (interface): `string WriteWorkbook(ReportData d, string outDir)`, `string WriteTargetConfigWorkbook(TargetConfigData d, string outDir)`, `string WriteCsv(ReportData d, string outDir)`
- **IValueSourceAttributor** (interface): `AttributionMap Build(Snapshot s)`
- **NullPlatform** (class): `bool CanCollect`, `bool IsElevated`, `Snapshot CollectThisMachine(CollectOptions o, CancellationToken ct)`, `string CreateHandoffDirectory(string token)`, `HandoffResult RunElevatedCollect(string token, CancellationToken ct)`, `void OpenWithShell(string path)`, `string ExportCollectorBundle(string targetDir)`
- **RiegelServices** (class): `IPlatform Platform`, `TimeProvider Clock`, `DateOnly Today`, `ICatalogueProvider Catalogue`, `IFactsDeriver Facts`, `IEvaluator Evaluator`, `ISnapshotReader Snapshots`, `IValueSourceAttributor Attribution`, `IBaselineImporter Baselines`, `IPlantStore Plants`, `IRegisterService Registers`, `IRunComparer Comparison`, `IScoring Scoring`, `IRunService Runs`, `IReportDataBuilder ReportData`, `IPdfWriter Pdf`, `ITabularWriter Tabular`, `IEvidencePack Evidence`, `IRemediationWriter Remediation`, `static RiegelServices CreateDefault(IPlatform platform)`, `static RiegelServices Create(IPlatform platform, TimeProvider clock)`

### `Riegel.Core.Settings`

- **AppPaths** (static class): `static string DataFolder`, `static string SettingsFile`, `static string LogsFolder`, `static string HandoffFolder`, `static string MachineDefaultsFile`, `const string DataDirVariable`
- **EffectiveSettings** (record): `string Language`, `string? Theme`, `string? DefaultPlantFolder`, `int WarnDays`, `int StaleDays`, `int UpdateMaxDays`, `int UnusedAccountDays`, `string? ReportLanguage`, `IReadOnlyList<string> RecentPlants`, `IReadOnlySet<string> Preset`, `bool IsPreset(string name)`
- **RiegelSettings** (record): `string? Language`, `string? Theme`, `string? DefaultPlantFolder`, `int? WarnDays`, `int? StaleDays`, `int? UpdateMaxDays`, `int? UnusedAccountDays`, `string? ReportLanguage`, `IReadOnlyList<string>? RecentPlants`, `static RiegelSettings Empty`
- **SettingsStore** (static class): `const long MaxBytes`, `static RiegelSettings Load(string? path = null)`, `static RiegelSettings LoadMachineDefaults(string? path = null)`, `static void Save(RiegelSettings settings, string? path = null)`, `static EffectiveSettings Resolve(RiegelSettings user, RiegelSettings machine)`

### `Riegel.Core.Localization`

- **Plural** (static class): `static string Category(string language, long n)`, `static string Format(ResourceManager resources, string key, long n, params object?[] args)`, `static bool Has(ResourceManager resources, string key)` – texts with a count come as `<key>_one` / `<key>_other` (CLDR: en, de, es exactly 1; fr 0 and 1; zh-Hans always other)
- **UiLanguage** (static class): `static IReadOnlyList<string> Supported`, `static IReadOnlyList<string> ReviewedReportLanguages`, `static CultureInfo System`, `const string EnvironmentVariable`, `const string Fallback`, `static string Normalize(string? setting)`, `static string Resolve(CultureInfo system, params string[] choices)`, `static CultureInfo Apply(string? userSetting, string? machineDefault = null)`, `static bool IsReviewedReportLanguage(string language)`, `static string NativeName(string code)`

### `Riegel.Core.Diagnostics`

- **Anonymizer** (class): `string Apply(string text)`
- **DiagnosticsPackage** (static class): `static string SuggestedFileName(DateTime now)`, `static void Write(string zipPath, IReadOnlyList<KeyValuePair<string, string>> info, bool anonymize, IEnumerable<string>? knownHosts = null)`
- **Log** (static class): `static string? Folder`, `static string DefaultFolder`, `const int KeepDays`, `const long MaxBytesPerDay`, `static void Start(string folder)`, `static void Info(string message)`, `static void Warning(string message)`, `static void Error(string message, Exception? exception = null, bool withStack = false)`, `static IReadOnlyList<string> Files(int days = 7)`

### `Riegel.Core.About`

- **AppInfo** (static class): `static string Version`, `static string? Commit`, `static string VersionWithBuild`, `static string CollectorVersion`, `static string Publisher`, `static string Copyright`, `static string? ExeSha256`, `const string Name`, `const string RepositoryUrl`, `const string SupportUrl`, `const string PrivacyUrl`, `const string LicenseUrl`, `const string ImpressumUrl`, `const string WebsiteUrl`, `static ToolInfo Tool()`
- **LicenseTexts** (static class): `const string LicenseFile`, `const string NoticesFile`, `static string License()`, `static string ThirdPartyNotices()`

### `Riegel.Core.Samples`

- **SampleData** (static class): `static DateOnly Date`, `static DateTimeOffset Now`, `static IReadOnlyList<string> Hosts`, `static ToolInfo Tool`, `static CatalogueRef Catalogue`, `static RegisterHashes RegisterHashes`, `static BaselineRef Baseline`, `const string PlantId`, `const string PlantName`, `const string Hmi`, `const string Engineering`, `const string Operations`, `const string DomainController`, `const string Historian`, `static Plant Plant()`, `static MachineIdentity Identity()`, `static RegisterSet Registers()`, `static IReadOnlyList<ExceptionEntry> Exceptions()`, `static IReadOnlyList<TailoringEntry> Tailoring()`, `static IReadOnlyList<MeasureEntry> Measures()`, `static IReadOnlyList<RuleResult> Results(string host)`, `static IReadOnlyList<Run> Runs()`, `static Run Run(string host)`, `static IReadOnlyDictionary<string, JsonElement> Facts(string host)`, `static Snapshot Snapshot(string host)`, `static ReportData Report()`, `static SummaryData Summary()`, `static TargetConfigData TargetConfig()`, `static ApprovalSheetData ApprovalSheet()`, `static CollectionRecordData CollectionRecord()`

### `Riegel.Core.Attribution`

- **AttributionFactory** (static class): `static IValueSourceAttributor Create(RiegelServices services)`

### `Riegel.Core.Baselines`

- **BaselinesFactory** (static class): `static IBaselineImporter Create(RiegelServices services)`

### `Riegel.Core.Catalogue`

- **CatalogueFactory** (static class): `static ICatalogueProvider Create(RiegelServices services)`

### `Riegel.Core.Comparison`

- **ComparisonFactory** (static class): `static IRunComparer Create(RiegelServices services)`

### `Riegel.Core.Evaluation`

- **EvaluationFactory** (static class): `static IEvaluator Create(RiegelServices services)`
- **RglCodesEngine** (static class): `static IReadOnlyList<string> All`, `const int First`, `const int Last`, one `const string <Name>` per code of the range (all listed in [Error codes](error-codes.md)) (`CatalogueInvalid` … `Retired`, RGL-0200–0248), `static string Message(string code, params object?[] args)`, `static string Message(CultureInfo culture, string code, params object?[] args)`, `static RiegelException Exception(string code, params object?[] args)`

### `Riegel.Core.Evidence`

- **EvidenceFactory** (static class): `static IEvidencePack Create(RiegelServices services)`
- **RglCodesDataOutputs** (static class): `static IReadOnlyList<string> All`, `const int First`, `const int Last`, one `const string <Name>` per code of the range (all listed in [Error codes](error-codes.md)) (`ZipUnreadable` … `CatalogueNotReference`, RGL-0550–0573), `static string Message(string code, params object?[] args)`, `static string Message(CultureInfo culture, string code, params object?[] args)`

### `Riegel.Core.Facts`

- **FactsFactory** (static class): `static IFactsDeriver Create(RiegelServices services)`

### `Riegel.Core.Parsing`

- **RglCodesParsing** (static class): `static IReadOnlyList<string> All`, `const int First`, `const int Last`, one `const string <Name>` per code of the range (all listed in [Error codes](error-codes.md)) (RGL-0250–0286), `static string Message(string code, params object?[] args)`

### `Riegel.Core.Plants`

- **PlantsFactory** (static class): `static IPlantStore Create(RiegelServices services)`
- **RglCodesWorkflow** (static class): `static IReadOnlyList<string> All`, `const int First`, `const int Last`, `const string ChangedByOthers`, `const string LockTimeout`, one `const string <Name>` per code of the range (all listed in [Error codes](error-codes.md)) (`ReadOnlyPlant` … `InvalidNotApproved`, RGL-0401–0448), `static string Message(string code, params object?[] args)`

### `Riegel.Core.Registers`

- **RegistersFactory** (static class): `static IRegisterService Create(RiegelServices services)`
- **RegisterApproval** (static class): `static void ThrowIfInvalid(IEnumerable<ValidationIssue> issuesAfterApproval, RegisterKind kind, IEnumerable<string> ids)` (RGL-0448: only valid entries are approved), `static IReadOnlySet<string> InvalidIds(IEnumerable<ValidationIssue> issues, RegisterKind kind)`

### `Riegel.Core.Remediation`

- **RemediationFactory** (static class): `static IRemediationWriter Create(RiegelServices services)`

### `Riegel.Core.ReportBuilding`

- **ReportDataFactory** (static class): `static IReportDataBuilder Create(RiegelServices services)`

### `Riegel.Core.Reports.Pdf`

- **PdfFactory** (static class): `static IPdfWriter Create(RiegelServices services)`
- **RglCodesPdf** (static class): `static IReadOnlyList<string> All`, `const int First`, `const int Last`, one `const string <Name>` per code of the range (all listed in [Error codes](error-codes.md)) (RGL-0500–0520), `static string Message(string code, params object?[] args)`, `static string Message(CultureInfo culture, string code, params object?[] args)`

### `Riegel.Core.Reports.Tabular`

- **TabularFactory** (static class): `static ITabularWriter Create(RiegelServices services)`

### `Riegel.Core.Resources`

- **CommonStrings** (class): generated from `src/Riegel.Core/Resources/CommonStrings.resx` (en, de, es, fr, zh-Hans); one property per text
- **DataOutputStrings** (class): generated from `src/Riegel.Core/Resources/DataOutputStrings.resx` (en, de, es, fr, zh-Hans); one property per text
- **EngineStrings** (class): generated from `src/Riegel.Core/Resources/EngineStrings.resx` (en, de, es, fr, zh-Hans); one property per text
- **ParsingStrings** (class): generated from `src/Riegel.Core/Resources/ParsingStrings.resx` (en, de, es, fr, zh-Hans); one property per text
- **PdfStrings** (class): generated from `src/Riegel.Core/Resources/PdfStrings.resx` (en, de, es, fr, zh-Hans); one property per text
- **WorkflowStrings** (class): generated from `src/Riegel.Core/Resources/WorkflowStrings.resx` (en, de, es, fr, zh-Hans); one property per text

### `Riegel.Core.Runs`

- **RunsFactory** (static class): `static IRunService Create(RiegelServices services)`

### `Riegel.Core.Scoring`

- **ScoringFactory** (static class): `static IScoring Create(RiegelServices services)`

### `Riegel.Core.Snapshots`

- **SnapshotsFactory** (static class): `static ISnapshotReader Create(RiegelServices services)`
