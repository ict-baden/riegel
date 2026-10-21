[Riegel](../README.md) › Evidence pack

# Evidence pack, verify and sample checks

**Contents:** [What the pack is for](#what-the-pack-is-for) · [Contents](#contents) · [manifest.json](#manifestjson) ·
[SHA256SUMS.txt](#sha256sumstxt) · [Customer baselines](#customer-baselines) · [verify](#verify) ·
[compare-snapshot](#compare-snapshot) · [What the hashes prove and do not prove](#what-the-hashes-prove-and-do-not-prove) ·
[Hostile packs](#hostile-packs) · [Excel and CSV](#excel-and-csv) · [Error codes](#error-codes) · [Sources](#sources)

## What the pack is for

`Riegel-Evidence_<plant>_<yyyy-MM-dd>.zip` (output O9, `report --evidence`) is the hand-over for auditors and the
information security officer. It holds everything needed to check a signed plant report without access to the
customer's plant folder: the report documents, the evaluated exports, the results, the registers as they were used,
the plant definition and the whole rule catalogue. An auditor can

1. check every file against `SHA256SUMS.txt` with standard tools,
2. run `verify` to re-evaluate every packed export with the packed catalogue and registers, and
3. collect one machine again (with a witness) and compare it with the packed export using `compare-snapshot`.

The pack is confidential: it contains host names, local account names, SIDs and the security configuration.

## Contents

All paths use `/`. Name parts come from Riegel's own sanitised values (host name, run ID, hash), never unchecked from
files in the plant folder.

| Path | Content |
|---|---|
| `manifest.json` | what is in the pack and how it was produced (`riegel.evidence/1`, see below) |
| `SHA256SUMS.txt` | SHA-256 of every file in the pack, including `manifest.json` (sha256sum format, LF) |
| `README.txt` | how to check the hashes, run `verify` and `compare-snapshot`, the integrity statement (report language, German and English) and the baseline note (UTF-8, CRLF) |
| `<report files>` | the PDF, Excel and CSV files of the report, in the root of the pack |
| `runs/<host>/<runId>.results.json` | the newest run of each machine (`riegel.run/1`), byte for byte as stored in the plant folder |
| `snapshots/<host>/<file>.snapshot.json` (+ `.sha256`) | the export each run evaluated; the SHA-256 must match the run, the sidecar is written in sha256sum format |
| `registers/<kind>_<sha8>.json` | `exceptions`, `tailoring` and `measures` in every version a packed run or the report used: the current file or the copy in `history/<kind>/<ts>_<sha8>.json`, checked by the full SHA-256 |
| `plant/riegel-plant.json` | the plant (groups, machines, ESU flags, settings); `verify` needs it to apply the registers |
| `catalogue/riegel-catalogue-<version>.json` | the whole catalogue as one `riegel.catalogue/1` document with rules, lifecycle, build, capability and data tables |
| `baselines/<id>.metadata.json` | imported customer baselines: metadata only (see [Customer baselines](#customer-baselines)) |
| `baselines/<id>.json`, `baselines/<id>.rules.json`, `baselines/LICENSE-WARNING.txt` | only with `--include-baseline-content` |
| `comparison/<host>.previous.json`, `comparison/<host>.approved.json` | comparison of the run with the previous run and with the approved state (`RunComparison`), where they exist |
| `imports/<id>.import.json` | import and collection records that concern a packed run or export |

**Never in the pack:** the raw files of a built-in-commands bundle (`*.riegel-builtin`: `secedit.inf`, `.reg` exports,
`winlogon.txt` …). The pack contains only the exports converted from them. A run whose export path points to a bundle,
to a folder, outside the plant folder, through a link (reparse point) or to a file whose SHA-256 differs from the run
stops the pack with `RGL-0567` instead of being packed.

Each ZIP entry carries the creation time of the pack. Entries are written in ordinal order of their paths.

## manifest.json

`riegel.evidence/1` ([schema](../schemas/evidence.schema.json)):
`{ schema, plantId, plantName, createdAtUtc, evaluationDate, tool, catalogue, catalogueFile, baselines, registers,
runs, operatorRole?, files }`.

- `tool`: name, version, commit and SHA-256 of the program that wrote the pack.
- `catalogue`: id, version, release date and the catalogue SHA-256 (the hash of its canonical form, as in every run);
  `catalogueFile`: path and file SHA-256 of the packed catalogue.
- `registers`: SHA-256 of the current register files (the ones the report's sign-off binds). Each run names the
  versions it used in its own `registers`.
- `runs`: per packed run its ID, host and the paths and SHA-256 of the results and the export.
- `baselines`: per imported baseline its `BaselineRef`, `contentIncluded` and `file` (the stored baseline when its
  content is included, otherwise the metadata file).
- `files`: every file of the pack except `manifest.json` and `SHA256SUMS.txt`, with SHA-256.

## SHA256SUMS.txt

One line per file, `<sha256>␠␠<path>`, sorted by path, UTF-8 without BOM, LF line ends, as GNU `sha256sum` writes it.
On Linux, in the unpacked folder:

```sh
sha256sum -c SHA256SUMS.txt
```

On Windows, per file: `Get-FileHash -Algorithm SHA256 <file>` and compare with the line.

## Customer baselines

Imported baselines (GPO backups, `riegel.baseline/1`) may contain third-party content whose terms forbid passing it
on (Microsoft Security Compliance Toolkit, BSI, CIS). By default the pack holds **metadata only**:
`baselines/<id>.metadata.json`

```json
{
  "schema": "riegel.evidence-baseline/1",
  "baseline": { "id": "…", "name": "…", "kind": "gpoBackup|riegelJson|csv", "sourceSha256": "…", "importedAtUtc": "…", "ruleCount": 4 },
  "contentIncluded": false,
  "rules":   [ { "id": "IMP-…", "type": "registry", "key": "hklm\\…|value" } ],
  "results": [ { "host": "…", "runId": "…", "ruleId": "IMP-…", "status": "pass", "expected": "…", "actual": "…" } ]
}
```

The rule `key` names what a rule checks (its identity: registry key and value, service, audit GUID, user right), not
the expected value; titles and texts of the baseline are not copied. `verify` reports the results of these baselines as
**"Baseline … content not included – not verifiable"** (finding `RGL-0562`, listed in `notVerifiable`; the text is
localised) and still checks every other result and the counts of each run.

With `--include-baseline-content` the pack also holds the stored baseline (`baselines/<id>.json`), the rules Riegel
formed from it (`baselines/<id>.rules.json`, `riegel.rules/1`, used by `verify`) and `baselines/LICENSE-WARNING.txt`;
`README.txt` carries the licence warning and the manifest marks the baseline with `contentIncluded: true`. Check the
licence terms before you hand such a pack to anyone.

## verify

```text
Riegel.Cli.exe verify <evidence.zip> [--format text|json]
riegel verify <evidence.zip>
```

`verify` reads the ZIP **in memory** (nothing is extracted to paths) and then:

1. checks that `SHA256SUMS.txt` is well-formed, that every listed file exists and has that hash, and that no file is
   missing from the list (`RGL-0556`, `RGL-0557`, `RGL-0558`);
2. reads `manifest.json` (`riegel.evidence/1` only) and checks every file reference in it the same way; a baseline
   entry must have a valid, unique baseline ID that is not the ID of Riegel's own catalogue (`RGL-0555`, error);
3. loads the packed catalogue through the catalogue loader and compares its SHA-256 and version with the manifest
   (`RGL-0559`), then with the catalogue **built into the Riegel version doing the check** or, for an earlier
   version, with the SHA-256 of that **released catalogue version**, which every later Riegel version carries: a
   known version with a different SHA-256 means the rules were changed (`RGL-0572`, mismatch); a version this Riegel
   does not know (newer, or made up) cannot be compared with a reference (`RGL-0573`, status `catalogueUnknown`,
   exit code 1, see below);
4. for every packed run: reads the export with the same export reader as an import (redaction, `parsed:*`
   recomputation, limits), takes the register versions the run names and the packed plant, and evaluates the export
   again with **the evaluation clock and date of the packed run** (so time-dependent flags such as `clockSuspect` and
   stale snapshots come out the same);
5. compares each result field by field: status, severity, expected, actual, value source and detail, evidence keys,
   exception, tailoring and measure IDs, reason code, flags and details (`RGL-0560`), and the history-independent
   counts and scores of the run (`RGL-0561`). With a customer baseline whose content is not in the pack, the counts are
   computed from the re-evaluated results plus the packed results of that baseline, so a changed summary is still
   found; the hardening score and the documented share count only Riegel's own catalogue and are fully recomputed.
   A packed result counted for such a baseline must belong to a baseline of its run and carry that baseline's rule ID
   (`IMP-<id>-…`); anything else is a mismatch (`RGL-0560`), so Riegel's own results cannot be hidden as baseline
   results.

Not compared, because a single export cannot reproduce them: the evaluation timestamp and the run ID, the path of the
export, the tool information, `previousRunId`, `approvedRunId`, `reEvaluationOf`, the flags `regression` and `newRule`
(they depend on the previous run and catalogue), the run flag `machineIdentityChanged` and the counts `regressions` and
`newlyOpen`. Detail texts can depend on the evaluation language, which a run does not record; when the only
differences are in details, `verify` evaluates again in the plant's report language and the other UI languages and
accepts the first exact match.

Result and exit code:

| Status | Exit | Meaning |
|---|---|---|
| `reproduced` | 0 | every hash matches, the packed catalogue is the one built into this Riegel version or a released catalogue it knows by its SHA-256, and every result was reproduced, except results of baselines without content (listed as not verifiable) |
| `catalogueUnknown` | 1 | **not verified**: the results follow from the packed catalogue, but this Riegel version does not know its version (`RGL-0573`), so its rules could not be compared with a reference. Verify with the Riegel version that wrote the pack (`tool.version` in the manifest) or a newer one; treat exit code 1 as "not verified" |
| `mismatch` | 2 | a file was changed, is missing or not listed, the catalogue differs from the manifest or from the built-in or released catalogue of the same version, a baseline result does not belong to its baseline, a result or count differs, or a run cannot be verified (other catalogue, register version not in the pack, unreadable export) |
| `error` | 3 | the pack cannot be read: not a ZIP, over a limit, unsafe entry names, `SHA256SUMS.txt` or `manifest.json` missing or invalid |

The text output reports **"Reproduced with engine X (packed: Y)."** (the line is localised): X is the version doing
the check, Y the version that wrote the pack. The next lines give the number of snapshots and results compared and
the catalogue: ID, version and full SHA-256, and whether it is identical to the catalogue built into this Riegel
version or to a released catalogue it knows. In JSON (`--format json`) the same is in `result.catalogue`,
`result.catalogueBuiltIn` and `result.catalogueReleased`.

Why the built-in catalogue matters: `verify` re-evaluates with the **packed** catalogue. Whoever changes a rule in
the packed catalogue, the results that follow from it and every checksum gets a pack that is consistent in itself.
Only the comparison with the catalogue shipped inside Riegel shows the change. Riegel carries one catalogue and the
SHA-256 of every catalogue version released before it (a catalogue version never changes its content), so the newest
Riegel verifies packs of all earlier versions. A pack whose catalogue version is newer than the Riegel doing the
check, or made up, is `catalogueUnknown`: get the Riegel version named in `tool.version` of the manifest, or a newer
one, from the tag releases.

A run evaluated with another catalogue than the packed one is "not verifiable" and makes the result `mismatch`:
re-evaluate the plant with the current catalogue before packing, or keep the older Riegel version for the audit.

## compare-snapshot

```text
Riegel.Cli.exe compare-snapshot <fresh.snapshot.json> --evidence <evidence.zip> [--host <h>] [--format text|json]
```

The auditor's sample check: collect a machine again, ideally with a witness, and compare the fresh export
with the packed export of the same host. The host is taken from the fresh export unless `--host` names another one;
of several packed runs of that host the newest is used. The pack is checked first (`SHA256SUMS.txt` and manifest); a
changed pack is not used as a basis (exit 3). The comparison uses the drift engine
(`IRunComparer.CompareSnapshots`): a normalised diff of all items without the volatile list (signature ages and
versions, timestamps, update history dates, uptime, durations).

| Status | Exit | Text |
|---|---|---|
| `consistent` | 0 | "consistent" |
| `different` | 1 | "differs in N points" with the list of differences |
| `error` | 3 | host not in the pack (`RGL-0564`), pack changed or unreadable, fresh export unreadable |

`compare-snapshot` checks the checksums of the pack and compares two exports. It does **not** re-evaluate, does not
check the packed catalogue, results or summaries and does not print a catalogue hash: run `verify` on the same pack
first.

## What the hashes prove and do not prove

The SHA-256 checksums show that a file has not been changed since the checksum was created. They do not prove that an
export came unchanged from the target machine: whoever carries the storage medium can replace export and checksum
together. That is what the collection record with a witness (O14) and the sample check with `compare-snapshot` are for.

In detail:

- **Proven:** each file in the pack is the one the pack lists; the packed results follow from the packed exports,
  catalogue, registers and plant with this engine (`verify`); the packed catalogue is the one built into this Riegel
  version or a released catalogue it knows (only when `verify` says so, exit code 0); a fresh collection of a machine
  matches the packed one (`compare-snapshot`).
- **Not proven:** that the packed export really came from that machine, that nobody changed the machine between the
  collection and the audit (only the sample shows that for the sampled machines), that the person who wrote the pack
  did not rebuild the whole pack with matching hashes (the pack is not signed in 0.1), the results of customer
  baselines whose content is not in the pack (they are counted as packed), and anything Riegel cannot read from a
  machine (MFA, backups, network segmentation).
- **No compliance statement.** A reproduced pack shows that the evaluation is traceable, not that the plant is secure.

## Hostile packs

Evidence packs come from outside and are treated as hostile input:

- size limits from `ReadLimits`: the ZIP file (512 MB), each entry (64 MB) and the sum of all unpacked entries
  (512 MB); the declared size is checked before reading and the real size while reading, so a lying header or a ZIP
  bomb stops at the limit (`RGL-0551`, `RGL-0552`); at most 20,000 entries (`RGL-0566`);
- entry names must be relative, separated by `/`, without `..`, `.`, empty parts, drive letters, `:`, backslashes or
  control characters, and unique ignoring case (`RGL-0553`);
- nothing is extracted. For the export reader, whose contract reads paths, the already hash-checked bytes of one export
  are written to a fresh temporary folder under a fixed name and deleted afterwards; the entry name is never used as a
  path;
- JSON is read with the closed vocabulary (unknown properties are errors) and the depth limit; every error is an `RGL`
  code, never a crash.

## Excel and CSV

The pack usually contains the workbook and `results.csv` of the report. Both are written by `ITabularWriter`:

- **`results.csv`** (O3): RFC 4180 (comma, CRLF, fields with comma, quote or line break in double quotes, quotes
  doubled), UTF-8 without BOM, invariant: codes instead of translated names (`fail`, `notCheckable`, `high`,
  `domainGpo`), flags as camelCase names joined with `;`, ISO 8601 dates. Columns: `plant` (the plant ID), `host`,
  `runId`, `evaluatedAtUtc` (the time stamp of the run ID, which is the evaluation time), `ruleSet`, `ruleId`,
  `category`, `severity`, `status`, `flags`, `expected`, `actual`, `valueSource`, `exceptionId`, `exceptionReviewBy`,
  `tailoringId`, `measureId`, `measureDue`, `title_en` (English title from the catalogue; imported baseline rules keep
  their author's title).
- **Workbook** (O7) and **Soll-Konfiguration** (O13): sheets in the report language, a bold header row, an auto filter
  over each table, a frozen header row (and first column on wide sheets), column widths from the content, dates as
  real date cells formatted `yyyy-mm-dd` and times as `yyyy-mm-ddThh:mm:ssZ` (UTC).
- **Formula injection guard:** a cell value from machine or register data that starts with `=`, `+`, `-`, `@`, TAB or
  CR is prefixed with `'` in the CSV and written as an explicit text cell in Excel (text format `@` and the quote
  prefix), so a spreadsheet never evaluates it. Control characters that XML or CSV readers cannot take are replaced by
  U+FFFD; Excel cells are cut at 32,767 characters.

## Error codes

`RGL-0550`–`RGL-0599` (`RglCodesDataOutputs`): 0550 not a ZIP · 0551 pack too large · 0552 entry or sum over the limit
· 0553 unsafe entry name · 0554 `SHA256SUMS.txt` missing or malformed · 0555 `manifest.json` missing or invalid · 0556
hash mismatch · 0557 listed file missing · 0558 file not listed · 0559 catalogue differs · 0560 result differs · 0561
counts differ · 0562 baseline content not included (note) · 0563 run not verifiable · 0564 host not in the pack · 0565
packed file unreadable · 0566 too many entries · 0567 run cannot be packed · 0568 report file cannot be packed · 0569
remediation entry not allowed · 0570 old value not in the rollback · 0571 run does not match the manifest · 0572
catalogue differs from the released catalogue of the same version · 0573 catalogue not known to this Riegel version
(note, exit code 1). The texts are in `DataOutputStrings` (five languages).

## Sources

- `SHA256SUMS.txt` and the `.sha256` sidecars use the sha256sum line format that the scaffold fixed for snapshot
  sidecars (`SnapshotFile.SidecarLine`, [file formats](file-formats.md)). Checked on 2026-10-18: a pack written by
  this package, unpacked into a scratch folder, passes `sha256sum -c SHA256SUMS.txt` (GNU coreutils 8.32, Git for
  Windows) for every file, and `sha256sum -c` accepts each snapshot sidecar.
- Integrity statement: German and English text in `CommonStrings.Integrity_Statement`.
- [MS-GPAC] and [MS-GPSB] for the remediation formats: see [Remediation](remediation.md#sources).
