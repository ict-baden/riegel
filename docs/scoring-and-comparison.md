[Riegel](../README.md) › Scoring and comparison

# Scoring and comparison

**Contents:** [Counts first](#counts-first) · [Härtungsgrad and Dokumentiert](#härtungsgrad-and-dokumentiert) ·
[Audit cap](#audit-cap) · [Category scores and checked share](#category-scores-and-checked-share) · [Plant values](#plant-values) ·
[Runs](#runs) · [Regressions, new rules and newly open findings](#regressions-new-rules-and-newly-open-findings) ·
[Run flags](#run-flags) · [Status changes](#status-changes) · [Raw-value drift](#raw-value-drift) ·
[Volatile list](#volatile-list) · [Auditor sampling](#auditor-sampling) · [Management summary trend](#management-summary-trend) ·
[Determinism](#determinism)

## Counts first

Every summary starts with counts, percentages come second: open findings by severity ("offene Befunde: 3 hoch, 12
mittel, 7 niedrig"), regressions, newly open findings, expiring, expired and pending exceptions, overdue measures. Open
means status `fail` only; `exception`, `outOfScope`, `notApplicable`, `notCheckable` and `info` are not open. Counts
include the rules of imported customer baselines.

## Härtungsgrad and Dokumentiert

Weights by severity: high 3, medium 2, low 1. Only results with a decision count: `pass`, `fail` and `exception`.
Excluded: `info`, `notApplicable`, `outOfScope` (tailoring) and `notCheckable`.

- **Härtungsgrad** = round(100 × Σw(pass) / Σw(pass + fail + exception))
- **Dokumentiert** = round(100 × Σw(pass + exception) / Σw(pass + fail + exception))

An exception never raises the Härtungsgrad, only "Dokumentiert". Tailoring removes the rule from both. Arithmetic is
decimal and rounds once at the end, half away from zero (37.5 % → 38 %). The checked share alone is truncated to whole
percent (89.5 % → 89 %), so that a share below the 90 % threshold never shows as 90 % without the incomplete banner.

Both values cover Riegel's own catalogue (`ruleSetId` `riegel`). Each imported customer baseline has its own section
with its own pass/fail/exception/not-checkable counts and score, so a customer list does not change the comparable
Härtungsgrad.

**Domain controllers** (`DomainRole` 4 or 5) get no Härtungsgrad and no category scores ("nicht bewertet –
Domänencontroller"); catalogue 2026.10.1 is not designed for them. Their counts are still shown.

## Audit cap

The `audit` category has 27 rules, which fail together on almost every standalone machine. Without a cap they would
dominate the value. Its weights are therefore scaled so that the category contributes at most **12 weight points** (four
high rules): f = min(1, 12 / Σw(audit, considered)).

Example: 27 audit rules of medium severity fail (54 points), five high rules elsewhere pass (15 points). Without the cap
the Härtungsgrad would be 15 / 69 = 22 %; with the cap the audit rules count 12 points and it is 15 / 27 = 56 %. The
report marks the audit category as capped and explains it in a footnote.

## Category scores and checked share

- **Category scores** (17 report categories, in report order) are computed within the category, unscaled: round(100 ×
  Σw(pass) / Σw(pass + fail + exception)) of that category. The category weight shown is its contribution to the total
  (capped for `audit`).
- **Checked share** (German *Prüfabdeckung*) = checkable / (applicable − outOfScope − info), where applicable excludes
  `notApplicable` and checkable means `pass`, `fail` or `exception`, truncated to whole percent. It is shown next to
  every score.
- Below **90 %**, or when the snapshot was not collected as administrator, the run is `Incomplete`: the PDF carries the
  banner "Incomplete check" (*Unvollständige Prüfung*) with the share and the missing areas (categories with not-checkable results), and the
  Härtungsgrad is greyed out ("only X % checkable").

## Plant values

The plant report and the management summary use, per machine, the newest snapshot (by collection time) evaluated on or
before the report date, and of that snapshot the newest evaluation. Plant values add the machines' weighted sums **after
capping each machine**, so the audit cap keeps its meaning in a 50-machine plant; domain controllers are left out of the
values but counted. The plant's checked share includes every machine. The plant score is greyed out when any machine's run
is incomplete.

## Runs

A run (`runs/<host>/<runId>.results.json`) is one snapshot evaluated with a catalogue, the plant's registers and
baselines on an evaluation date. Run IDs are `<yyyyMMddTHHmmssZ>-<6 hex>` from the evaluation time and the SHA-256s of
snapshot, catalogue and registers, so batch imports and re-evaluations never collide.

- **Previous run:** the newest run of a *different* snapshot of the same machine whose collection time is not later
  than this snapshot's (ordered by `CollectedAtUtc`, then evaluation time). A batch import of old exports in any order
  therefore finds the right predecessor.
- **Re-evaluation:** evaluating the same snapshot again (same SHA-256, e.g. with a new catalogue or changed registers)
  creates a run with `reEvaluationOf` = the first run of that snapshot. It is not a new state of the machine: its
  previous run is still the run of the preceding snapshot.
- **Approved state:** `approve` marks a run as the approved state of a machine (`approved/<host>.json` with run ID,
  snapshot and results SHA-256, catalogue version, register SHA-256s and the approval with its required record; every
  approval is also kept in `history/approved/<host>/`). Missing hashes are filled in from the run; given hashes must
  match it (`RGL-0410`).
- **Comparison basis** for the flags of a run: the previous run; without one, the approved state.

## Regressions, new rules and newly open findings

| Basis result | Now | Flag | Counted as |
|---|---|---|---|
| `pass`, `exception`, `notApplicable`, `outOfScope` | `fail` | `Regression` | regression ("Verschlechtert") |
| `exception` | `fail` because the exception **expired** | `ExceptionExpired` | not a regression; own block "abgelaufene Ausnahmen" |
| – (rule had no result in the basis, e.g. new in the catalogue) | any | `NewRule` | newly open if `fail`; never a regression |
| `notCheckable` | `fail` | – | newly open ("neu offen"), not a regression |
| `fail` | `fail` | – | standing finding |

An exception that stops covering a finding for any other reason – edited after approval, pending, value no longer
matching – makes the finding open again, and that is a regression: the documented state got worse. Because the flags
refer to the previous run, a standing deviation alerts once, not on every run (`--alert change`).

## Run flags

| Flag | Condition |
|---|---|
| `StaleSnapshot` | evaluation date − collection date > `staleDays` (plant setting, default 30). Results with a decision also carry the flag; the PDF machine header says "Collection status: … (N days ago) – outdated" (*Stand der Erfassung*) |
| `ClockSuspect` | the target's clock is implausible: collected more than 24 h after the import or the evaluation time, before the machine's last boot, or before the release date of its installed build + UBR (build table of the catalogue); or the engine flagged an age-based result |
| `MachineIdentityChanged` | the `MachineGuid` differs from the snapshot of the comparison basis: "Rechner neu aufgesetzt?" |
| `Incomplete` | checked share below 90 % or not collected as administrator |
| `DomainController` | `DomainRole` 4 or 5 |

## Status changes

`compare` and the report section "Verschlechtert seit dem letzten Lauf / seit der Freigabe" list status changes per
rule:

| Kind | Change |
|---|---|
| `worse` | `pass`/`notApplicable` → `fail`, or `exception` → `fail` because the value no longer matches |
| `better` | `fail`/`exception` → `pass`/`notApplicable` |
| `exceptionAdded` | → `exception` |
| `exceptionRemoved` | `exception` → `fail` (exception pending, changed, invalid or removed) |
| `exceptionExpired` | `exception` → `fail` because the exception expired |
| `tailoringAdded` / `tailoringRemoved` | → `outOfScope` / `outOfScope` → anything else |
| `newRule` | the rule has no result in the basis (reports list it when it fails) |
| `scopeChanged` | "scope of check changed" (*Erhebung geändert*): a change to or from `notCheckable` while the collection scope differs (administrator vs. not, `--minimal`, `--deep`) |
| `other` | any other change, e.g. `notCheckable` → `fail` without a scope change, or a rule that is no longer evaluated |

The report shows changes against the previous run first, then against the approved state.

## Raw-value drift

Besides the status, Riegel compares the raw snapshot items of the two states – the configuration audit for EU-GMP
Annex 11 14.2. Each item is broken down into paths with canonical values:

| Item | Path |
|---|---|
| `reg:HKLM\…` | value name (`(default)` for the default value); subkeys as `\<name>`. A type change with the same value shows as `1 (dword)` → `1 (sz)` |
| `wmi:…` rows | `<row>/<property>`, the row identified by `Name`, `SID`, `GUID`, `HotFixID`, `SettingID`, `InstanceName` or `DriveLetter` (else its position) |
| `wmi:….Method` | the output property |
| `api:localgroup:<SID>` | the member SID |
| other `api:`, `evt:` and `parsed:` items | the JSON structure; lists of objects by SID, key and value name, GUID, store and name, or name; lists of plain values as sets |
| machine header | `machine` / field (host name, domain, domain role, product type, build, UBR, edition, installation type, display version, architecture, MachineGuid, OS language) |

An item that exists on one side only gives one line (added/removed with its status). When an item's status changes and
one side has no value (e.g. now `denied`), only the status change is listed. `raw:*` items are skipped – their parsed
form (`parsed:secedit`, `parsed:registry-pol`, `parsed:gpresult`) is compared instead. The report shows the number of
differences and the first 20 per machine; Excel and JSON carry all of them.

## Volatile list

These values change on every healthy machine and are never reported as drift:

- the whole item `api:windows-update-history` (a list of events; the patch level is visible through UBR and the
  installed updates `Win32_QuickFixEngineering`);
- registry values under `HKLM\SOFTWARE\Microsoft\Windows Defender\Signature Updates`;
- properties and values named `AntivirusSignatureLastUpdated`, `AntivirusSignatureAge`, `AntivirusSignatureVersion`,
  `AntispywareSignatureLastUpdated`, `AntispywareSignatureAge`, `AntispywareSignatureVersion`, `NISSignatureLastUpdated`,
  `NISSignatureAge`, `NISSignatureVersion`, `AMProductVersion`, `AMEngineVersion`, `AMServiceVersion`,
  `NISEngineVersion`, `SignaturesLastUpdated`, `ASSignatureVersion`, `AVSignatureVersion`, `EngineVersion`,
  `SignatureUpdateLastAttempted`, `LastBootUpTime`, `timestamp`, `timeCreatedUtc`, `bootTimeUtc`, `lastLogonDate`,
  `passwordAgeDays` and `durationMs`;
- the machine header's last boot time; item durations are never compared.

## Auditor sampling

`compare-snapshot <fresh> --evidence <zip>` compares a fresh collection with the packed snapshot of the same host using
the same drift engine and volatile list, and reports "konsistent" or "weicht in N Punkten ab" with the list, including a
changed `MachineGuid`. Collect the fresh snapshot in the same mode (administrator, `--minimal`, `--deep`) as the packed
one; otherwise the differences in what could be read are listed too.

## Management summary trend

The one-page summary shows, per plant, the open high findings at the **last four month-ends** up to the summary date
(the date itself counts if it is a month-end). For each month-end it adds, per machine, the open high findings of the
machine's latest run evaluated on or before that day; a month-end before any run shows no value. The same rule – the
latest run up to the date – gives the current state, so runs evaluated after the summary date are ignored.

## Determinism

The same snapshot, catalogue, registers, baselines, evaluation date and evaluation clock produce a byte-identical
`results.json`: results sorted by rule set and rule ID, facts sorted by name, fixed key order, invariant formatting,
UTF-8 with LF. Report data built twice from the same plant folder with the same clock is identical as well.
