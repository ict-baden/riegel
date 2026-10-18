[Riegel](../README.md) › Exceptions, tailoring and measures

# Exceptions, tailoring and measures

**Contents:** [Three registers](#three-registers) · [Exceptions](#exceptions-bewusst-offen) · [Tailoring](#tailoring-nicht-im-prüfumfang) ·
[Measures](#measures-maßnahmenplan) · [Which entry applies](#which-entry-applies) · [Approval workflow](#approval-workflow) ·
[Approval binding](#approval-binding) · [History and concurrent edits](#history-and-concurrent-edits) · [Templates](#templates) ·
[Validation](#validation) · [Register flags in reports](#register-flags-in-reports)

Riegel never turns a failed rule into "fine" on its own. A plant has three registers that record decisions made by
people: *this finding is deliberately open* (exception), *this rule is not part of the target configuration for this
role* (tailoring) and *this finding will be fixed by …* (measure). They are the customer's documents under the
customer's change control; Riegel reads them, applies them to the raw results and binds approvals to their content.

## Three registers

| File in the plant folder | Schema | Entry IDs | Needs approval |
|---|---|---|---|
| `exceptions.json` | `riegel.exceptions/1` | `EX-0001` … | yes |
| `tailoring.json` | `riegel.tailoring/1` | `TL-0001` … | yes |
| `measures.json` | `riegel.measures/1` | `M-0001` … | no – the sign-off of the plant report covers the plan |

All three are UTF-8 without BOM, indented with two spaces, LF line endings, a fixed key order and entries sorted by
`id`, so `git diff` and DMS comparisons stay clean. The field lists are in [File formats](file-formats.md#registers).
A missing file is an empty register (its SHA-256 is recorded as `""`).

Every entry has a **scope**: `machines` (host names, compared without case), `groups` (group IDs of
`riegel-plant.json`) and/or `allMachines`. An empty scope is invalid.

## Exceptions ("bewusst offen")

An exception says: the rule fails on these machines, and that is accepted for a documented reason.

| Field | Rule |
|---|---|
| `ruleId` | a rule of the catalogue or of an imported baseline (`IMP-<baseline>-<id>`) |
| `category` | `vendorRequirement` (needs `reference`), `technicalNecessity`, `compensatingControl` (needs `compensatingMeasure`), `riskAccepted`, `notRelevantHere` |
| `justification` | required, at least 20 characters |
| `acceptedValue` | required for `check` rules: the canonical actual value the exception covers (see below) |
| `approval` | `approvedByRole`, `approvedOn`, `reviewBy`, `approvalRecord` (change, QMS or approval-sheet reference) and `approvedContentSha256`; `approvedByName` is optional customer data |
| `createdOn`, `createdByRole`, `modifiedOn`, `catalogueVersion`, `comment` | bookkeeping; a template from a machine builder keeps `createdByRole` (e.g. "Hersteller") |

**Accepted value.** The value is compared with the result's canonical actual value (the same rendering as in the
report: DWORD/QWORD in decimal, `true`/`false`, strings verbatim, `multi_sz` as `["a","b"]`). Integers compare
numerically (`0` equals `0x0`), text without case. Two special values exist:

- `*` accepts any value. It is allowed, but validation warns: a later change of the value would not be noticed.
- `(not set)` (or `(nicht gesetzt)`) accepts exactly "value not set".

If the actual value later differs from the accepted one, the finding is open again with the flag
`ExceptionValueMismatch` – the machine changed, the exception no longer describes it.

**States** (evaluated on the evaluation date of the run or the report):

| State | Condition | Effect on a failing result |
|---|---|---|
| valid | approval complete, bound to the content, approved on or before today, `reviewBy` not passed, no validation error | status `exception` |
| expiring | valid, and `reviewBy` is at most `warnDays` (plant setting, default 30) away | status `exception`, flag `ExceptionExpiring` |
| expired | today is after `reviewBy` (the `reviewBy` day itself is still valid) | stays `fail`, flag `ExceptionExpired`; listed in its own report block, not as a regression |
| pending | approval fields missing or incomplete, approval without content hash, or `approvedOn` in the future | stays `fail`, flag `ExceptionPending` ("Ausnahme beantragt") |
| changed after approval | the content hash differs from `approvedContentSha256` | stays `fail`, flags `ExceptionPending` and `ExceptionChangedAfterApproval` |
| invalid | a validation error: justification too short, missing reference or compensating measure, missing `acceptedValue`, `reviewBy` before `approvedOn` or more than 36 months after it, risk acceptance by a role that may not accept it, duplicate ID | stays `fail`, flag `ExceptionPending` |

**Risk acceptance.** If the plant setting `riskAcceptanceRoles` is not empty, a `riskAccepted` exception for a rule of
severity `high` is valid only if `approvedByRole` is one of these roles (compared without case).

**Review period.** `reviewBy` must not be more than 36 months after `approvedOn` (invalid); more than 12 months gives a
warning (a yearly review is recommended).

Exceptions apply only to results with status `fail`. A rule that passes, is not applicable or not checkable is not
changed; an exception for a rule that now passes everywhere is marked `unused` in the report ("Ausnahme nicht mehr
nötig?"). An exception for an inventory rule has no effect (inventory rules are always `info`); validation warns.

## Tailoring ("nicht im Prüfumfang")

Tailoring is a baseline decision: the rule is not part of the target configuration for this role, e.g. cloud
protection on air-gapped HMIs. It is kept apart from exceptions so that design-intended scope does not read as open
risk.

- Fields: `id`, `ruleId`, `scope`, `reason` (at least 20 characters), `approval` (as for exceptions, `reviewBy` at most
  36 months after `approvedOn`, no 12-month warning), `createdOn`, `createdByRole`, `modifiedOn`, `comment`.
- **Valid** → status `outOfScope`: the rule counts in neither score and is listed in the report section "Zuschnitt" and
  in the *Soll-Konfiguration*. Tailoring takes precedence over an exception for the same rule.
- **Pending, changed after approval, expired or invalid** → the rule is evaluated normally, flag `TailoringPending`.
- Tailoring a rule of severity `high` gives a warning ("hohe Schwere zuschneiden statt begründen?").
- In the *Soll-Konfiguration*, a rule is removed from a group only by a valid entry that covers the whole group
  (`allMachines` or the group itself); machine-level tailoring is listed but the rule stays in the group's target
  configuration.

## Measures (Maßnahmenplan)

A measure records the plan to fix a finding: `action`, `ownerRole`, `dueDate`, optional `ticket`, `state`
(`planned`, `done`, `dropped`).

- Planned, and the finding is still `fail` → flag `MeasurePlanned` ("Behebung geplant bis …").
- Due date passed (`dueDate` before today) and still failing → flag `MeasureOverdue` instead (exit-code token `overdue`).
- `done` but the rule still fails on an affected machine → register flag `measureDoneButFailing`.
- `dropped` measures are ignored when results are evaluated; they stay in the register and the report.
- The result carries the measure's ID also when the finding is covered by an exception or not checkable, so the plan is
  visible; the flags are set only on open findings.

## Which entry applies

For each result, Riegel looks for entries with the same rule ID whose scope contains the machine:

1. **The most specific scope wins:** machine, then group (the machine's group in `riegel-plant.json`), then the whole
   plant.
2. **Among equals, the latest `approvedOn` wins**; entries without approval come last; then the ID decides, so the
   result is always the same.
3. Measures: planned before done (dropped never), then the most specific scope, then the earliest due date.

The winning entry decides – also when it is pending or its accepted value does not match, even if a less specific
entry would have covered the finding. This is deliberate: a more specific entry expresses the intent for this machine.
Several entries for the same rule with overlapping scopes are reported as `overlap` warnings.

Entries for retired rules are kept and marked `ruleRetired`.

## Approval workflow

1. Create or change entries (app, editor or `exceptions import-template`). New and changed entries are pending; the
   finding stays open.
2. Produce the approval sheet (`exceptions approval-sheet`, PDF *Ausnahmen zur Genehmigung*). By default it lists every
   pending and changed exception and tailoring entry with its rule, scope, category, justification, measure, reference,
   accepted value and **content hash**, plus the register SHA-256 at sheet time and signature lines for the approving
   roles (the `approved` roles of the plant's sign-off; for risk acceptance of a high-severity rule also the
   `riskAcceptanceRoles`). The sheet ID (`FB-<yyyyMMdd>-<6 hex>`) is derived from the register hashes and the entries, so
   the same register state gives the same ID.
3. After signing, enter the approval for the listed IDs (`exceptions approve --ids … --role … --record …`, or
   "Genehmigung eintragen" in the app). Role and record are required; `approvedOn` defaults to today; `reviewBy`
   defaults to the entry's proposed date if it lies after the approval, otherwise to 12 months after the approval (the
   longest period without a warning). Cite the sheet as the record, e.g. "Freigabeblatt FB-20261018-a1b2c3".
4. Riegel stores the content hash with the approval. The next `check` or report shows the entry as valid.

## Approval binding

`approvedContentSha256` is the SHA-256 of the entry's compact canonical JSON with an empty `approval` object (the
same function for exceptions and tailoring, `ContentHashes.Of`). Any later change of the entry – justification, scope,
accepted value, reference, `modifiedOn` – changes the hash. The entry is then **pending again** with the flag
`ExceptionChangedAfterApproval`, and the finding is open until the changed content is approved anew. Approval fields
typed into the file by hand, without the hash, do not make an entry valid ("approval not bound").

Because an edited exception no longer covers the finding, the next run reports the finding as open; compared with the
previous run, where it was covered, it counts as a regression. Only expiry is reported separately (see
[Scoring and comparison](scoring-and-comparison.md#regressions-new-rules-and-newly-open-findings)).

The plant report's sign-off block binds the registers: "Mit der Freigabe werden die Ausnahmen im Register `<sha8>`
(n Einträge, davon m gültig) und der Zuschnitt `<sha8>` bestätigt." Every run records the SHA-256 of each register, so
"which exceptions were valid at run X" is reproducible from the history alone.

## History and concurrent edits

- Every save writes the new file and an append-only copy `history/<register>/<yyyyMMddTHHmmssZ>_<sha8>.json` with
  exactly the same bytes. `riegel-plant.json` has its own history (`history/plant/`), approved machine states have
  `history/approved/<host>/<yyyyMMddTHHmmssZ>.json`.
- Saves are atomic (temporary file next to the target, then replace) and run inside the lock `.riegel.lock`
  (`FileShare.None`, wait up to 10 s, else `RGL-0404`). Inside the lock, Riegel compares the file's SHA-256 with the
  hash the editor loaded; if someone else saved in between, the save is refused with `RGL-0403` and nothing is
  written. A register the editor did not change is left alone, even if someone else changed it.
- On Windows (also on SMB shares) the lock is a sharing lock and the file is deleted when it is released. On Linux, .NET
  uses an advisory `flock`; there the empty lock file stays, because deleting it on release would let two waiting
  processes lock different files.
- **Auditor mode** (`--nur-lesen` / `--read-only`, app "Prüfermodus"): the plant folder is opened read-only and every
  write is refused with `RGL-0401`; outputs go only to `--out`.

## Templates

`*.exceptions-template.json` (`riegel.exceptions-template/1`) and `*.tailoring-template.json`
(`riegel.tailoring-template/1`) have the same entries as the registers with an empty `approval` object. Machine builders
ship them for known deviations of their plant software; role templates for tailoring (HMI, engineering station,
historian/server, jump host) ship as suggestions.

Importing a template (`exceptions import-template`, `tailoring import-template`, or the app) adds its entries to the
register as **pending** entries: approval fields are cleared (also if a template contains some), `createdOn` is today,
`createdByRole` is kept. An ID that already exists in the register gets the next free number. Templates are never
applied automatically – the imported entries need the normal approval.

## Validation

`exceptions validate` and `tailoring validate` (and the app's inline checks) report every issue with an RGL code. Exit
codes: `0` valid, `1` warnings only, `2` at least one error, `3` the files could not be read.

| Code | Severity | Issue |
|---|---|---|
| RGL-0420 | error | ID used more than once |
| RGL-0421 | error | ID does not have the form `EX-0001`, `TL-0001`, `M-0001` |
| RGL-0422 | error | rule not in the catalogue or an imported baseline |
| RGL-0423 | warning | rule retired (entry kept, marked) |
| RGL-0424 | error | empty scope |
| RGL-0425 | error | group does not exist in the plant |
| RGL-0426 | warning | machine not known (no entry in `riegel-plant.json`, no runs) |
| RGL-0427 | error | justification or reason shorter than 20 characters |
| RGL-0428 | error | `vendorRequirement` without `reference` |
| RGL-0429 | error | `compensatingControl` without `compensatingMeasure` |
| RGL-0430 | error | `acceptedValue` missing for a check rule |
| RGL-0431 | warning | `acceptedValue` is `*` |
| RGL-0432 | warning | not approved yet (lists the missing approval fields) |
| RGL-0433 | warning | approval fields without content hash |
| RGL-0434 | warning | changed after approval |
| RGL-0435 | error | `reviewBy` more than 36 months after `approvedOn` |
| RGL-0436 | warning | `reviewBy` more than 12 months after `approvedOn` (exceptions only) |
| RGL-0437 | error | `reviewBy` before `approvedOn` |
| RGL-0438 | warning | expired |
| RGL-0439 | warning | expires within `warnDays` |
| RGL-0440 | error | risk acceptance of a high-severity rule by a role outside `riskAcceptanceRoles` |
| RGL-0441 | warning | overlaps with another entry for the same rule (also exception vs. tailoring) |
| RGL-0442 | warning | tailoring of a high-severity rule |
| RGL-0443 | warning | measure overdue |
| RGL-0444 | error | required field empty (`ruleId`, `action`, `ownerRole`) |
| RGL-0445 | warning | `plantId` of the file differs from the plant |
| RGL-0446 | warning | `approvedOn` lies in the future (not in force yet) |
| RGL-0447 | warning | exception for an inventory rule (no effect) |

## Register flags in reports

The register sections of the plant report and the Excel sheets show each entry with its state and these flags:

| Flag | Meaning |
|---|---|
| `unused` | the rule passes on every affected machine: "Ausnahme nicht mehr nötig?" |
| `overlap` | another exception or tailoring entry for the same rule overlaps |
| `ruleRetired` | the rule is retired in the current catalogue |
| `valueMismatch` | on at least one machine the actual value differs from `acceptedValue` |
| `measureDoneButFailing` | a measure is `done`, but the rule still fails on an affected machine |

Expired exceptions are listed in their own block. A measure is shown as overdue when it is planned, its due date has
passed and the finding is still open on at least one affected machine.
