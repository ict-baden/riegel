[Riegel](../README.md) › Command line

# Command line

Everything Riegel does is also available without a window: evaluate snapshots, keep a plant folder up to date, write
the documents for sign-off, maintain the registers and verify evidence packs. Commands end with a Nagios-style exit
code, so they fit scheduled tasks, Icinga and CI pipelines.

**Contents:** [Programs](#programs) · [Overview](#overview) · [Conventions](#conventions) ·
[Global options](#global-options) · [check](#check) · [Exit codes and tokens](#exit-codes-and-tokens) ·
[Icinga and Nagios](#icinga-and-nagios) · [collect](#collect) · [import](#import) · [Documents](#documents) ·
[compare and approve](#compare-and-approve) · [Registers](#registers) · [baseline](#baseline) ·
[remediate](#remediate) · [rules](#rules) · [Auditors: verify and compare-snapshot](#auditors-verify-and-compare-snapshot) ·
[plant init and collector export](#plant-init-and-collector-export) · [Scheduled tasks](#scheduled-tasks) ·
[SYSTEM and a plant share](#system-and-a-plant-share) · [FAT pipelines](#fat-pipelines) ·
[Auditors on Linux](#auditors-on-linux) · [Riegel.exe does not wait](#riegelexe-does-not-wait) ·
[Output, encoding and JSON](#output-encoding-and-json) · [Error codes](#error-codes)

## Programs

| Program | Where | Use it for |
|---|---|---|
| `Riegel.Cli.exe` | in the MSI and the framework zip, next to `Riegel.exe` | scripts, scheduled tasks, Icinga on Windows |
| `riegel-<version>-win-x64.exe` | release asset, single file | the same program without installation (USB stick, FAT bench) |
| `riegel-<version>-linux-x64`, `riegel-<version>-linux-arm64` | release assets, single file | auditors and CI on Linux |
| `Riegel.exe <command>` | the window program | still works for compatibility – but see [Riegel.exe does not wait](#riegelexe-does-not-wait) |

All of them run the same commands with the same options and exit codes. `collect`, `collector export` and
`check --this-pc` need Windows; on Linux they end with exit code 3 and `RGL-0455`, which names the alternative
(`riegel check --snapshot <file>`). `Riegel.Cli.exe` needs Windows 10 1809 / Server 2019 or later; older machines
use the portable collector (`Riegel-Collector-<version>.zip`) and their exports are imported elsewhere.

The examples use `Riegel.Cli.exe`; on Linux write `riegel` (the downloaded file renamed, or a symlink).

## Overview

```text
Riegel.Cli.exe check [--plant <folder>] [--snapshot <file>…|--this-pc] [--brief] [--alert change|state]
                     [--critical <list>] [--warning <list>] [--no-store] [--format text|json]
Riegel.Cli.exe collect [--out <folder>] [--gzip] [--minimal] [--deep] [--pseudonymize]
Riegel.Cli.exe import <file|folder>… --plant <folder> [--collected-by-role <role>] [--witness-role <role>] [--record] [--reevaluate]
Riegel.Cli.exe report --plant <folder> [--machine <host>…] [--format pdf,xlsx,csv,json] [--per-machine] [--no-appendix]
                      [--preset audit|as-delivered] [--evidence] [--include-baseline-content] [--pdfa]
                      [--language <code>] [--out <folder>]
Riegel.Cli.exe summary --plant <folder> [--plant <folder>…] [--out <folder>] [--language <code>]
Riegel.Cli.exe target-config --plant <folder> [--group <id>] [--out <folder>] [--language <code>]
Riegel.Cli.exe collection-record --plant <folder> [--import <id>|--latest] [--out <folder>] [--language <code>]
Riegel.Cli.exe compare --plant <folder> --machine <host> [--against previous|approved] [--format text|json]
Riegel.Cli.exe compare-snapshot <fresh-snapshot> --evidence <zip> [--host <host>] [--format text|json]
Riegel.Cli.exe approve --plant <folder> --machine <host>… [--run <id>] --role "<role>" --record "<reference>"
                       [--name "<name>"] [--date <yyyy-MM-dd>]
Riegel.Cli.exe exceptions validate|list|expiring|approval-sheet|approve|import-template --plant <folder> …
Riegel.Cli.exe tailoring list|validate|approve|import-template --plant <folder> …
Riegel.Cli.exe measures list|overdue|add|close --plant <folder> …
Riegel.Cli.exe remediate --plant <folder> --machine <host> [--run <id>] [--out <folder>] [--language <code>]
Riegel.Cli.exe baseline import <path> --plant <folder> --name <name> | baseline list | baseline remove <id>
Riegel.Cli.exe rules [--id <ID>] [--format text|json|md]
Riegel.Cli.exe verify <evidence.zip> [--format text|json]
Riegel.Cli.exe collector export <folder>
Riegel.Cli.exe plant init <folder> --name <name>
Riegel.Cli.exe --help | --hilfe | --version | --info | --licenses | --lizenzen
```

`<command> --help` shows the options of one command. The window program additionally knows `Riegel.exe --demo`,
`--screenshots <folder> [--theme light|dark|both]` and two hidden switches (`--demo-report <folder>`, and
`collect --handoff <token>`, the elevated side of "Als Administrator prüfen"); they are not for scripts.

## Conventions

- Commands and options are English and not case-sensitive. Values keep their spelling.
- `--option value` and `--option=value` are the same. Options that take several values (`--snapshot`, `--machine`,
  `--ids`, `--plant` for `summary`) take everything up to the next `--option`, and can be repeated; `--machine` and
  `--ids` also accept commas (`--ids EX-0002,EX-0004`).
- `--machine` must name a machine of the plant (an entry of the plant or a machine with runs; case does not matter).
  An unknown name in `report`, `remediate` or `measures add` ends with `RGL-0452`, which lists the known machines, and
  exit code 3 – a typo no longer produces an empty report for sign-off.
- Dates are `yyyy-MM-dd`. "Today" is the UTC date of the machine.
- An unknown command or option, a missing value or a missing required option ends with a message on stderr
  (`RGL-0450` … `RGL-0454`) and exit code 3. Nothing is written in that case.
- Messages and help follow the user interface language (`RIEGEL_LANGUAGE`, then the user setting, then
  `defaults.json`, then Windows; en, de, es, fr, zh-Hans). Option values, tokens, JSON and the performance data of
  `--brief` are invariant.
- `--plant <folder>` is a plant folder (`riegel-plant.json` with `snapshots/`, `runs/`, registers …; see
  [File formats](file-formats.md)). Documents go to `reports/<yyyy-MM-ddTHHmmssZ>/` in the plant folder unless `--out`
  is given.

## Global options

| Option | Does |
|---|---|
| `--help`, `--hilfe` (also `-h`, `/?`) | overview, or the help of one command when it follows a command |
| `--version` | `Riegel 1.0.0 (build abc1234) · collector 0.1.0 · catalogue 2026.10.1` – give this when you report a problem |
| `--info` | product, version, collector and catalogue, copyright, publisher and licence, links (website, Impressum, source, support, privacy), data folder, exit codes, and "This software sends no data to ICT-Baden." |
| `--licenses`, `--lizenzen` | the MIT licence of Riegel and `THIRD-PARTY-NOTICES.txt` (packages and rule-source attributions) |
| `--nur-lesen`, `--read-only` | read-only mode (auditor mode): nothing is written into any plant folder. Allowed anywhere on the line. Commands that would write there (`import`, `approve`, register changes, `baseline import/remove`, `plant init`) end with `RGL-0457`; document commands need `--out` (`RGL-0458`); `check --plant` evaluates, compares and prints, but stores nothing. |

## check

Evaluates snapshots against the catalogue.

- **Without `--plant`**: evaluate and print only. Nothing is written. Alert mode `state`.
- **With `--plant`**: apply the plant's registers (exceptions, tailoring, measures), store the run, compare it with the
  previous run of the machine and with its approved state. Alert mode `change`. If the folder does not exist yet,
  `check` creates the plant. A snapshot from outside the plant folder is taken in like `import` does (copy under
  `snapshots/<host>/`, `.sha256`, import record), so run and export stay together in the evidence. `check` never edits
  `riegel-plant.json` or the registers; new machines appear through their run folders.
- **Input**: `--snapshot <file>…` takes exports (`.snapshot.json`, `.snapshot.json.gz`) and builtin bundle folders
  (`<HOST>_<date>.riegel-builtin`). Without `--snapshot`, on Windows, `check` collects this PC first (`--this-pc`,
  read-only, with the rights of the calling account); a complete snapshot needs administrator rights.
- **Integrity**: if a `.sha256` file lies next to an export and does not match, `check` stops with `RGL-0465` and exit
  code 3. A missing `.sha256` file gives a note on stderr only.

| Option | Meaning |
|---|---|
| `--plant <folder>` | plant folder (created when missing, unless `--no-store` or read-only) |
| `--snapshot <file>…` | one or more exports or builtin bundle folders |
| `--this-pc` | collect this PC first (Windows; the default there without `--snapshot`) |
| `--no-store` | with `--plant`: evaluate and compare, but write nothing |
| `--brief` | one line per machine for Icinga/Nagios ([below](#icinga-and-nagios)) |
| `--format text\|json` | text (default) or JSON ([below](#output-encoding-and-json)) |
| `--alert change\|state` | alert mode, see [Exit codes and tokens](#exit-codes-and-tokens) |
| `--critical <list>` | tokens that give exit code 2; replaces the list of the alert mode; `none` = no token |
| `--warning <list>` | tokens that give exit code 1; replaces the list of the alert mode |

```text
> Riegel.Cli.exe check --plant D:\Riegel\Linie3 --snapshot E:\HMI-L3-01_20261018T061500Z.snapshot.json
HMI-L3-01 (E:\HMI-L3-01_20261018T061500Z.snapshot.json)
  Run 20261018T090012Z-3f9a1c, stored in the plant folder
  153 rules: 98 met, 12 not met, 3 deliberately open, 2 out of scope, 29 not applicable, 6 not checkable, 3 info
  Open findings: 1 high, 8 medium, 3 low
  Worse since the previous run or the approved state: 1 (NET-08)
  Newly open with severity high: 0 (–)
  Exceptions: 1 expiring, 0 expired, 0 pending · measures overdue: 1
  Hardening score: 81 % · documented: 84 % · checked share: 96 %
  Compared with the previous run 20261011T090009Z-77d0e2 (collected 10/11/2026 6:15 AM UTC)
  Compared with the approved state (run 20260904T130211Z-c01b5e)
  Result: CRITICAL (exit code 2) – regression, expiring, overdue

Alert mode change: critical regression,newhigh; warning expired,expiring,pending,overdue,incomplete,stale,clock
```

(Example values; the counts come from the catalogue and your machine.)

## Exit codes and tokens

`check` and `import` use the Nagios convention, as Frist does:

| Code | State | Meaning |
|---|---|---|
| `0` | OK | no token of the critical or warning list fired |
| `1` | WARNING | a token of the warning list fired |
| `2` | CRITICAL | a token of the critical list fired (critical wins) |
| `3` | UNKNOWN | usage error, unreadable or corrupt snapshot, checksum mismatch, catalogue error, I/O error, cancelled |

With several snapshots the worst code counts.

**Tokens** (invariant names):

| Token | Fires when |
|---|---|
| `regression` | a rule got worse since the previous run of a *different* snapshot or since the approved state: it was met, deliberately open, not applicable or out of scope and is now not met. Not a regression: a fail caused only by an expired exception, a rule new in the catalogue, a rule that was not checkable before |
| `newhigh` | an open finding of severity high that was not open in the previous run (also new rules and formerly not checkable); on the first run of a machine every open high finding is new |
| `high`, `medium`, `low` | open findings of that severity (standing state) |
| `expired` | an exception has expired; the finding is open again |
| `expiring` | an exception expires within the plant's `warnDays` (default 30) |
| `pending` | an exception or tailoring entry is requested, or changed after its approval, and not (re)approved |
| `overdue` | a planned measure is past its due date and the rule still fails |
| `incomplete` | less than 90 % of the rules were checkable, or the snapshot was not collected as administrator |
| `stale` | the snapshot is older than the plant's `staleDays` (default 30) |
| `clock` | the target's clock was implausible at collection |

**Alert modes:**

| Mode | Default | Critical | Warning |
|---|---|---|---|
| `change` | with `--plant` | `regression,newhigh` | `expired,expiring,pending,overdue,incomplete,stale,clock` |
| `state` | without `--plant` (FAT pipelines, one-off checks) | `regression,high` | `medium,low,expired,expiring,pending,overdue,incomplete,stale,clock` |

`change` alerts on what changed and on what needs a decision; standing findings stay out of the alert, so a known and
documented state does not page anybody every day. `--critical` and `--warning` replace a list completely
(`--critical high,regression --warning none`). In Windows PowerShell 5.1 an empty string argument is dropped, so write
`none` rather than `""`.

**Other commands:** `verify` 0 reproduced, 1 not verified (catalogue unknown to this Riegel version), 2 mismatch, 3
error · `compare-snapshot` 0 consistent, 1 differs, 3 error ·
`exceptions validate`/`tailoring validate` 0 valid, 1 warnings, 2 invalid, 3 error · `collect` 0 complete, 1 written but
incomplete, 3 nothing written (as the portable collector) · all others 0 success, 3 error.

## Icinga and Nagios

`check --brief` prints exactly one line per machine:

```text
RIEGEL WARNING - HMI-01: 0 worse, 3 open (1 high), 1 exception expiring, 1 measure overdue | score=72%;;;0;100 open=3 high=1 regressions=0 expiring=1 overdue=1 notcheckable=4
```

- `RIEGEL`, the state word and the performance data after `|` are invariant; the text in between follows the language.
- Further parts appear only when they count: `1 newly open high`, `2 exceptions expired`, `1 approval pending`,
  `incomplete check`, `snapshot stale`, `clock suspect` – so the line always explains its exit code.
- Performance data: `score` (hardening score in %, `U` when not rated, e.g. on a domain controller), `open`, `high`,
  `regressions`, `expiring`, `overdue`, `notcheckable`.
- On an error the line is `RIEGEL UNKNOWN - <file or command>: RGL-nnnn: <message>` and the exit code is 3.
- With several snapshots there is one line per machine, the worst first (Icinga shows the first line as status).

A check on the Windows machine itself, through the Icinga agent (the hardening state changes slowly – once a day is
plenty):

```text
object CheckCommand "riegel" {
  command = [ "C:\\Program Files\\Riegel\\Riegel.Cli.exe", "check", "--brief" ]
  arguments = {
    "--plant" = "$riegel_plant$"
    "--alert" = "$riegel_alert$"
  }
  vars.riegel_plant = "D:\\Riegel\\Linie3"
}

apply Service "riegel-haertung" {
  check_command = "riegel"
  command_endpoint = host.name
  check_interval = 24h
  retry_interval = 1h
  assign where host.vars.os == "Windows" && "linie3" in host.groups
}
```

The check collects this PC with the rights of the agent's account. Without administrator rights the snapshot is
incomplete and the line says `incomplete check` (token `incomplete`, WARNING). `--plant` keeps the history needed for
`change`; without it Riegel alerts on the standing state.

## collect

```bat
Riegel.Cli.exe collect --out E:\Riegel
```

Windows only. Writes `<HOST>_<yyyyMMddTHHmmssZ>.snapshot.json` (or `.json.gz` with `--gzip`) and its `.sha256` file
into `--out` (default: the current folder) and prints path and SHA-256 for the hand-over note. It reads only; the
collection code is the same as the portable collector's. Run it in an elevated console for a complete snapshot.

| Option | Meaning |
|---|---|
| `--out <folder>` | target folder |
| `--gzip` | compressed export |
| `--minimal` | no child processes (no `secedit`, no `gpresult`), no Windows Update query, firewall profiles only; affected rules become not checkable |
| `--deep` | also query the LSASS process protection |
| `--pseudonymize` | replace host, account and path names |

Exit codes: 0 complete, 1 written but incomplete (not elevated, or items denied), 3 nothing written.

## import

```bat
Riegel.Cli.exe import E:\Exporte E:\PG-ALT-01_20261010.riegel-builtin --plant D:\Riegel\Linie3 ^
  --collected-by-role "Servicetechniker" --witness-role "Anlagenverantwortlicher" --record
```

Adds exports and builtin bundles to the plant folder: checks every `.sha256` file first (one mismatch stops the whole
import before anything is written), copies the exports to `snapshots/<host>/`, writes the import record
`imports/<ts>-<6hex>.import.json`, evaluates and stores a run per export, and compares each with the previous run and
the approved state. `--record` also writes the collection record PDF (Erhebungsprotokoll) for signing.
`--brief`, `--format`, `--alert`, `--critical` and `--warning` work as for `check`; the default alert mode is `change`.
A folder argument is a builtin bundle when its name ends in `.riegel-builtin` or it holds `hostname.txt` or
`secedit.inf`; any other folder stands for the exports in it (`*.snapshot.json`, `*.snapshot.json.gz`, not
recursive) – so the folder of `collect --out` can be passed as it is. A folder with neither is `RGL-0470`. Files can
be listed one by one as well; cmd does not expand `*`.

An export that is already in the plant with a run (same SHA-256) is **skipped**: no new run, no new import record,
no new alert – the text output names it ("already in the plant with a run"). So a share folder can be imported again
and again; only new exports count. If every export is known, nothing is written and the exit code is 0; `--brief`
then prints one line, `RIEGEL OK - <plant>: no new exports (N already in the plant) | imported=0 skipped=N`, and
`--format json` has `"import": null` (there is no import record) and the names in `skipped`.
`--reevaluate` evaluates known exports again (for example after an update of the catalogue), as Riegel 0.1.0 did on
every import. The app skips known exports the same way; *Evaluate again* on the machines page re-evaluates.

## Documents

All document commands read the newest run of each machine. Without `--out` they write into
`reports/<yyyy-MM-ddTHHmmssZ>/` in the plant folder; in read-only mode `--out` is required.
`--language` takes en, de, es, fr or zh-Hans (default: the plant's report language, then the user interface).

| Command | Writes |
|---|---|
| `report` | `Riegel-Report_<plant>_<date>.pdf` (preset `audit` or `as-delivered`), per machine `…_<host>.pdf` with `--per-machine`, `Riegel-Overview_<plant>_<date>.xlsx`, `results.csv`, the runs as `runs/<host>/<runId>.results.json` with `json`, and `Riegel-Evidence_<plant>_<date>.zip` with `--evidence`. Default formats: `pdf,xlsx` |
| `summary` | `Riegel-Summary_<date>.pdf`: one page across all `--plant` folders; written to `--out` or the current folder |
| `target-config` | `Riegel-Soll-Konfiguration_<plant>_<date>.pdf` and `.xlsx` (target configuration), for all groups or `--group <id>` |
| `collection-record` | `Riegel-Erhebungsprotokoll_<plant>_<importId>.pdf` for `--import <id>` or the latest import |

`report` options: `--machine <host>…` (only these machines), `--no-appendix` (no per-machine appendix), `--pdfa`
(only if this build can write PDF/A – see [reports](reports.md)), `--include-baseline-content` (with `--evidence`:
pack the content of imported baselines; Riegel warns, because Microsoft, BSI or CIS terms may forbid passing them on).

## compare and approve

```bat
Riegel.Cli.exe compare --plant D:\Riegel\Linie3 --machine HMI-L3-02 --against approved
Riegel.Cli.exe approve --plant D:\Riegel\Linie3 --machine HMI-L3-01 HMI-L3-02 --role "Qualitätssicherung" --record "CHG-2026-0042"
```

`compare` shows status changes (worse, better, exception added/removed/expired, tailoring added/removed, new rule,
scope changed) and raw-value drift between the newest run and the previous run (default) or the approved state.
Without an earlier run it says so and ends with 0; `--against approved` without an approved state ends with
`RGL-0460` and 3.

`approve` records the newest run (or `--run <id>`, one machine only) of each machine as its approved state
(`approved/<host>.json` with run ID, snapshot and results SHA-256, catalogue version and register hashes).
`--role` and `--record` (your change, QMS or approval reference) are required, `--name` is optional customer data,
`--date` defaults to today. All machines are checked before anything is written.

## Registers

```bat
Riegel.Cli.exe exceptions validate --plant D:\Riegel\Linie3
Riegel.Cli.exe exceptions expiring --plant D:\Riegel\Linie3 --days 60
Riegel.Cli.exe exceptions approval-sheet --plant D:\Riegel\Linie3 --pending --out D:\Freigabe
Riegel.Cli.exe exceptions approve --plant D:\Riegel\Linie3 --ids EX-0002 EX-0005 --role "Informationssicherheitsbeauftragter" ^
  --record "Genehmigungsblatt GB-12 vom 2026-10-20" --review-by 2027-10-01
Riegel.Cli.exe tailoring import-template hmi --plant D:\Riegel\Linie3
Riegel.Cli.exe measures add --plant D:\Riegel\Linie3 --rule ENC-01 --action "BitLocker im Wartungsfenster aktivieren" ^
  --owner-role "Anlagenverantwortlicher" --due 2026-12-15 --machine ENG-L3-01 --ticket TICKET-311
Riegel.Cli.exe measures close M-0003 --plant D:\Riegel\Linie3
```

| Subcommand | Does |
|---|---|
| `exceptions validate`, `tailoring validate` | check the register; exit 0 valid, 1 warnings, 2 invalid |
| `exceptions list`, `tailoring list` | entries with their state: valid, pending, changed after approval, expiring, expired |
| `exceptions expiring [--days <n>]` | expiring and expired exceptions (default: the plant's `warnDays`) |
| `exceptions approval-sheet [--ids <id>…\|--pending] [--out <folder>]` | the approval sheet PDF for signing; default: all pending entries (exceptions and tailoring) |
| `exceptions approve`, `tailoring approve` | enter a signed approval: `--ids`, `--role`, `--record` required, `--date` (default today), `--name` optional, `--review-by <date>` required unless the entry already has a review date |
| `exceptions import-template`, `tailoring import-template` | add a template's entries as **pending** entries (never approved automatically); the argument is a template file or the name of a built-in template – tailoring: `hmi`, `engineering-station`, `historian-server`, `jump-host`; exceptions: `beispiel-anlagenbau` (an unknown name is `RGL-0471`) |
| `measures list`, `measures overdue` | the remediation plan; planned measures past their due date |
| `measures add` | `--rule`, `--action`, `--owner-role`, `--due` required; scope `--machine …`, `--group …` or `--all-machines` (default); `--ticket`, `--comment`, `--created-by-role` optional |
| `measures close <id> [--state done\|dropped] [--comment …]` | close a measure (default: done) |

Every change goes through the register service: the file is locked, compared with the version that was read, written
atomically, and a copy goes to `history/`. Unknown IDs (`RGL-0462`) and missing review dates (`RGL-0466`) change
nothing. `--format json` lists the entries in the register format.

## baseline

```bat
Riegel.Cli.exe baseline import D:\Kunde\GPO-Backup --plant D:\Riegel\Linie3 --name "Kundenvorgaben Linie 3"
Riegel.Cli.exe baseline list --plant D:\Riegel\Linie3
Riegel.Cli.exe baseline remove kundenvorgaben-linie-3 --plant D:\Riegel\Linie3
```

Imports a customer baseline – a GPO backup folder, an authored `riegel.baseline/1` JSON or the CSV template (see
[baselines](baselines.md)) – and keeps it as `baselines/<id>.json` with the source SHA-256 and the import time. Its
rules are named `IMP-<id>-…` and get their own score section. Evidence packs contain only baseline metadata unless
`report --evidence --include-baseline-content` is used.

## remediate

```bat
Riegel.Cli.exe remediate --plant D:\Riegel\Linie3 --machine HMI-L3-01 --out D:\Vorschlag --language de
```

Writes a remediation proposal for the newest run (or `--run <id>`) as files only: `riegel-remediation.reg`,
`riegel-rollback.reg`, `GptTmpl.inf`, `audit.csv` and `README.txt` under `remediation/<host>/<runId>/` in `--out` or in
the plant folder. Riegel never applies anything; every file says so, and each change has to be agreed with the
machine vendor first. Rules with a valid or pending exception and tailored rules are left out (listed in the README).

## rules

```bat
Riegel.Cli.exe rules
Riegel.Cli.exe rules --id NET-08
Riegel.Cli.exe rules --format md > rules.md
```

Lists the embedded catalogue (ID, severity, category, title in the user interface language) or one rule with
rationale, OT note, remedy and references. `--format json` gives the rules in the catalogue format.

## Auditors: verify and compare-snapshot

```bash
riegel verify Riegel-Evidence_Linie-3_2026-10-18.zip
riegel compare-snapshot HMI-L3-01_20261102T101500Z.snapshot.json --evidence Riegel-Evidence_Linie-3_2026-10-18.zip
```

`verify` checks every hash in the pack, compares the packed catalogue with the one built into this Riegel version (or
with the released catalogue of an earlier version), re-evaluates the packed snapshots with the packed catalogue and
registers and compares the results and counts with the packed ones: "Reproduced with engine X (packed: Y)" and 0, a
mismatch with 2 and the differing results, an error with 3. It prints the catalogue ID, version and SHA-256 and
whether it is the built-in or a known released one; a catalogue version this Riegel does not know is "not verified"
with exit code 1 (`RGL-0573`, status `catalogueUnknown`): verify it with the Riegel version that wrote it or a newer
one. Results of imported baselines whose content was not packed are listed as not verifiable; the counts of their runs
are still checked. `compare-snapshot` does not re-evaluate anything – run `verify` first
([Evidence pack](evidence.md#verify)).

`compare-snapshot` is the sampling step: an auditor collects one machine again and compares that fresh snapshot with
the packed one of the same host, without volatile values (signature ages, uptime, time stamps). 0 consistent, 1 differs
in N points (listed), 3 error. `--host` picks the machine when the host name of the fresh snapshot differs.

## plant init and collector export

```bat
Riegel.Cli.exe plant init \\fileserver\riegel$\Linie3 --name "Linie 3"
Riegel.Cli.exe collector export E:\
```

`plant init` creates the plant folder with `riegel-plant.json` (an existing plant is never overwritten).
`collector export` (Windows) copies the portable collector bundle with `UEBERGABE.txt` (hashes, version, date, how to
run it, what it leaves behind) to a stick or share – see [collector](collector.md).

## Scheduled tasks

Every morning at 6:00 as SYSTEM, collecting this PC into a plant folder on the same machine:

```bat
schtasks /Create /TN "Riegel Haertung" /SC DAILY /ST 06:00 /RU SYSTEM /RL HIGHEST ^
  /TR "\"C:\Program Files\Riegel\Riegel.Cli.exe\" check --plant D:\Riegel\Linie3"
```

- Run `schtasks /Create` in an elevated console. As SYSTEM the collection is complete (administrator rights).
- Task Scheduler waits for `Riegel.Cli.exe` and shows the exit code as *Last Run Result*: `0x0` OK, `0x1` warning,
  `0x2` critical, `0x3` error.
- To keep the console output, call a small `.cmd` file from the task:

  ```bat
  @echo off
  rem C:\Riegel\riegel-taeglich.cmd
  "C:\Program Files\Riegel\Riegel.Cli.exe" check --plant D:\Riegel\Linie3 --brief >> D:\Riegel\riegel.log 2>&1
  exit /b %ERRORLEVEL%
  ```

- A weekly report for the plant owner, as a second task on the machine that holds the plant folder:
  `Riegel.Cli.exe report --plant D:\Riegel\Linie3 --format pdf,xlsx`.
- Machines older than Windows 10 1809 / Server 2019 cannot run `Riegel.Cli.exe`. There the portable collector runs as
  the scheduled task (`Riegel.Collector.exe --out <share>`), and `import` on an analysis PC takes the exports in.

## SYSTEM and a plant share

Many machines can write into one plant folder on a share at the same time: snapshots, runs and import records are
unique per host and time, and `check` never edits `riegel-plant.json` or the registers. What a task needs depends on
how the machine reaches the share:

- **Domain members.** A task running as SYSTEM acts as the computer on the network and presents the computer's
  credentials to the file server (Microsoft Learn, *LocalSystem Account*,
  https://learn.microsoft.com/en-us/windows/win32/services/localsystem-account, seen 2026-10-18). The share therefore
  sees `DOMAIN\HMI-L3-01$`. Grant the computer accounts – best through a dedicated group such as `Riegel-Writers-Linie3`
  – *Modify* on `snapshots\`, `runs\` and `imports\` of the plant folder and *Read* on the rest, both on the share and
  in NTFS. They need no right on `riegel-plant.json`, the registers, `approved\` or `history\`; those stay with the
  people who maintain the plant.
- **Workgroup machines** (common on OT networks). There is no domain computer account that the file server could grant
  rights to, so "give SYSTEM access to the share" is not possible. Choose one of:
  1. a plant folder on the machine itself (`D:\Riegel\<plant>`), collected later by copying the folder's `snapshots\`
     or by the portable collector on a stick, then `import` on the analysis PC;
  2. a task under an account that the file server can authenticate, instead of SYSTEM. A complete snapshot needs
     administrator rights on the machine, so that account must be a local administrator there; its password is
     stored by Task Scheduler. Weigh this against option 1 together with your IT;
  3. no share at all: the hand-over workflow with stick and collection record (see [collector](collector.md)).
  Never make the plant share writable for *Everyone* or anonymous users: the folder is the customer's evidence record.
- `check --plant` on a share creates the plant if the folder is missing – create it beforehand with `plant init`, so a
  typo in the path does not create a second plant.

## FAT pipelines

A machine builder checks the delivered configuration in a factory acceptance test, on a Linux runner, against the
customer's authored baseline. Without `--plant` the alert mode is `state`: every open high finding and every
regression fails the pipeline.

```bash
riegel plant init build/fat --name "FAT X-100 SN-0001"
riegel baseline import customer/riegel.baseline.json --plant build/fat --name "Customer requirements"
riegel check --plant build/fat --snapshot fat/HMI-01_20261018T080000Z.snapshot.json --alert state --format json > build/check.json
status=$?
riegel report --plant build/fat --preset as-delivered --format pdf,json --evidence --out build/report
exit $status
```

- `--alert state` because the pipeline plant is new every time (in `change` every finding of a first run is new).
- To fail only on high findings, add `--warning none`; to tolerate a known gap, record it as an exception template
  (`createdByRole` "Hersteller") that the operator approves, instead of loosening the pipeline.
- Exit code 3 (corrupt snapshot, checksum mismatch) must fail the pipeline too: do not treat "not 2" as success.

## Auditors on Linux

An auditor gets the evidence pack and, if needed, the plant folder (read-only) on a stick:

```bash
riegel verify /media/usb/Riegel-Evidence_Linie-3_2026-10-18.zip
riegel --read-only report --plant /media/usb/Linie3 --format pdf,csv --out ~/audit/linie3
riegel --read-only check --plant /media/usb/Linie3 --snapshot /media/usb/Linie3/snapshots/HMI-L3-01/HMI-L3-01_20261018T061500Z.snapshot.json
riegel compare-snapshot ~/audit/HMI-L3-01_20261102T101500Z.snapshot.json --evidence /media/usb/Riegel-Evidence_Linie-3_2026-10-18.zip
```

`--read-only` makes sure nothing is written into the customer's folder. The Linux binaries contain the same engine and
catalogue as the Windows programs; `riegel --version` names both.

The Linux file carries the .NET runtime but needs OpenSSL, ICU and, for PDF, a font from the system. On a minimal
Debian 12 (for example `debian:12-slim`):

```bash
apt-get install --no-install-recommends libssl3 libicu72 fonts-dejavu-core
```

Without OpenSSL every command except the help ends with `RGL-0018` and exit code 3; without ICU Riegel warns
(`RGL-0017`) and formats dates and numbers invariantly; without a font, PDF output fails with `RGL-0501`. Other
distributions: [Installation › Linux packages](installation.md#linux-packages).

## Riegel.exe does not wait

`Riegel.exe` is a Windows (GUI) program that attaches to the calling console for its output. cmd and PowerShell do
**not** wait for it, the prompt comes back at once, and `%ERRORLEVEL%` / `$LASTEXITCODE` do not carry its exit code.
`Riegel.Cli.exe` is a console program: the shell waits and the exit code arrives.

| Shell | Use | If you must call `Riegel.exe` |
|---|---|---|
| cmd | `Riegel.Cli.exe check …` then `%ERRORLEVEL%` | `start /wait Riegel.exe check …` |
| PowerShell | `& 'C:\Program Files\Riegel\Riegel.Cli.exe' check …` then `$LASTEXITCODE` | `Riegel.exe check … \| Out-Default` |

Inside a `.cmd` file cmd waits on its own for console programs. Task Scheduler always waits. All examples in this
documentation therefore use `Riegel.Cli.exe`. The CI checks this on every build: a test runs the built
`Riegel.Cli.exe` from PowerShell and from cmd and asserts exit codes 0, 1, 2 and 3.

## Output, encoding and JSON

- **Text** goes to stdout, notes (such as a missing `.sha256` file) and errors to stderr. Numbers and dates in the
  text follow the Windows regional settings; files, JSON and `--brief` use invariant formats and UTC.
- **Encoding**: in a console window Riegel writes Unicode without changing the console's code page; redirected output
  (file, pipe, Icinga, Task Scheduler) and all Linux output are UTF-8 without byte order mark. PowerShell decodes the
  output of a native program with `[Console]::OutputEncoding`; to capture umlauts correctly set
  `[Console]::OutputEncoding = [Text.Encoding]::UTF8` first.
- **JSON** (`--format json`) is UTF-8, indented with two spaces and LF, with camelCase names, and contains no
  translated text other than the messages inside contract objects. Contract objects (runs, comparisons, register
  entries, rules) look exactly as in the files ([File formats](file-formats.md)). The envelopes:

| Command | `schema` | Main fields |
|---|---|---|
| `check` | `riegel.cli.check/1` | `status` (ok/warning/critical), `exitCode`, `alert` {`mode`, `critical`, `warning`}, `machines[]` {`host`, `source`, `runId`, `stored`, `status`, `exitCode`, `firedCritical`, `firedWarning`, `tokens` (count per token), `open`, `regressions[]` {`ruleSetId`, `ruleId`, `severity`, `basis`}, `newHigh[]`, `previousRunId`, `approvedRunId`, `run` (riegel.run/1), `comparisons` {`previous`, `approved`}} |
| `import` | `riegel.cli.import/1` | as `check`, plus `import` (the import record, `null` when nothing new was imported) and `skipped[]` (file names already in the plant) |
| `compare` | `riegel.cli.compare/1` | `host`, `against`, `toRunId`, `fromRunId`, `comparison` |
| `compare-snapshot` | `riegel.cli.compare-snapshot/1` | `exitCode`, `result` (status, comparison) |
| `verify` | `riegel.cli.verify/1` | `exitCode`, `result` (status, engine versions, findings, `catalogue` {`id`, `version`, `releasedOn`, `sha256`}, `catalogueBuiltIn`, `catalogueReleased`) |
| `exceptions/tailoring validate` | `riegel.cli.validate/1` | `register`, `status` (valid/warnings/invalid), `exitCode`, `issues[]` |
| `exceptions/tailoring/measures list` | `riegel.cli.register/1` | `register`, `today`, `entries[]` {`state` or `overdue`, `entry`} |
| `baseline list` | `riegel.cli.baselines/1` | `baselines[]` {`id`, `name`, `rules`, `sourceFileName`, `sourceSha256`, `importedAtUtc`} |
| `rules` | `riegel.cli.rules/1` | `catalogue`, `version`, `sha256`, `rules[]` |

## Error codes

Every error is printed as `RGL-nnnn: <message>` on stderr and ends with exit code 3. The code stays the same in every
language. The command line uses `RGL-0450` to `RGL-0499`; codes from other areas (for example `RGL-0002` file not
found, `RGL-0007` I/O error, `RGL-0014` read-only mode, `RGL-04xx` from the plant folder) pass through unchanged.

| Code | Meaning |
|---|---|
| `RGL-0450` | unknown command, option or argument |
| `RGL-0451` | an option needs a value |
| `RGL-0452` | invalid value (format, alert mode, token, date `yyyy-MM-dd`, language) |
| `RGL-0453` | a required option is missing |
| `RGL-0454` | options that exclude each other, or an option given twice |
| `RGL-0455` | the command needs Windows (`collect`, `collector export`, `check --this-pc`) |
| `RGL-0456` | invalid hand-over token (`collect --handoff`) |
| `RGL-0457` | read-only mode: the command would write into the plant folder |
| `RGL-0458` | read-only mode: give an output folder with `--out` |
| `RGL-0459` | no run for this machine in the plant |
| `RGL-0460` | no approved state for this machine |
| `RGL-0461` | no (such) import record |
| `RGL-0462` | IDs not in the register – nothing changed |
| `RGL-0463` | unknown rule ID |
| `RGL-0464` | baseline not in this plant folder |
| `RGL-0465` | the export does not match its `.sha256` file – Riegel does not use it |
| `RGL-0466` | approval without a review date – give `--review-by` |
| `RGL-0467` | `--run` belongs to another machine |
| `RGL-0468` | no plant folder (`riegel-plant.json` missing) |
| `RGL-0469` | cancelled with Ctrl+C |
