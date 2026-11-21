[Riegel](../README.md) › Data and privacy

# Data and privacy

What Riegel reads, which files it writes, where they live, what personal data they contain and how to delete them.

**Contents:** [In short](#in-short) · [No network](#no-network) · [On the checked machine](#on-the-checked-machine) ·
[Built-in-commands bundle](#built-in-commands-bundle) · [Plant folder](#plant-folder) ·
[What a snapshot contains](#what-a-snapshot-contains) · [Reports, evidence pack and remediation](#reports-evidence-pack-and-remediation) ·
[On the analysis PC](#on-the-analysis-pc) · [Pseudonymisation](#pseudonymisation) · [Deleting data](#deleting-data) ·
[Works council and co-determination](#works-council-and-co-determination) · [Who is responsible](#who-is-responsible)

## In short

- Riegel **reads** the security configuration of a Windows machine and **changes no configuration** there. It writes
  only its export, and Windows itself records that a program ran (execution traces, event log entries – see
  [On the checked machine](#on-the-checked-machine) and [What it leaves behind](collector.md#what-it-leaves-behind)).
- It writes only: the snapshot export (and its checksum file) where you choose, the plant folder you open, the output
  folder you choose and its own data folder `%LocalAppData%\Riegel`. *Check this PC* passes its snapshot through a
  temporary folder (`%TEMP%\Riegel-Erfassung-<8 hex>` or `handoff\<token>` in the data folder) and deletes it after the
  import; the demo plant lives in `%TEMP%` while Riegel runs ([On the analysis PC](#on-the-analysis-pc)).
- It opens **no network connection** and sends **no data to ICT-Baden**: no telemetry, no update check, no crash
  reports.
- Snapshots, registers and reports describe the security configuration of the customer's machines and contain host
  names, local account names and SIDs. Treat them as **confidential**; the PDF marks every page "Confidential – contains
  security configuration" (in the report language) by default.

## No network

App, command line and collector contain no network code (a source-scan test fails on `HttpClient`, `WebRequest`,
`Socket`, `TcpClient` and `Dns` in the shipped projects). Links on the *About* page (website, Impressum, source,
support, privacy) open only `http`/`https` addresses in your browser, through the shell, when you click them. *Privacy*
opens Riegel's privacy notice on the ICT-Baden website, <https://ict-baden.de/datenschutz/riegel/>; this page holds the
details behind it. A plant folder
may lie on a share; Windows then reads and writes it over SMB as for any file. Files from snapshots, imports or
baselines are never started.

## On the checked machine

| What | Where | Content | Removed by |
|---|---|---|---|
| Snapshot `<HOST>_<yyyyMMddTHHmmssZ>.snapshot.json` (or `.json.gz`) and `.sha256` | the folder given with `--out`, else next to `Riegel.Collector.exe` | see [What a snapshot contains](#what-a-snapshot-contains) | you – move it to the plant folder (`import`) and delete it from the stick |
| Temporary output of `secedit` and `gpresult` | `%TEMP%\riegel-<8 hex>` | the security template and the Group Policy result of this run | the collector, right after reading |
| CLR usage log (CLR 4) | `%LocalAppData%\Microsoft\CLR_v4.0\UsageLogs\` | that `Riegel.Collector.exe` ran (written by .NET, not by Riegel) | Windows / the administrator |
| Execution traces | Prefetch, AmCache/ShimCache | that a program ran (written by Windows) | Windows |
| Security log events | Windows Security log | 4688 (process creation, if audited), 4798/4799 (group memberships enumerated), privilege use – the IDs per runner are in the read-only proof | the log's retention |

The collector writes nothing else, starts only `secedit` and `gpresult` (none with `--minimal`), and never reads
passwords: values with secret-like names are written as "redacted" ([File formats › Item encodings](file-formats.md#item-encodings)).

## Built-in-commands bundle

`<HOST>_<yyyyMMdd>.riegel-builtin\` from `collect-builtin.cmd` holds raw exports of Windows tools: `secedit.inf`,
`auditpol.csv`, `.reg` files of the policy and security keys, the `Start` values of all services, the version values of
`CurrentVersion`, `winlogon.txt` (the automatic-logon user name, and for `DefaultPassword` only `present` or `absent`),
host name and version. The `.reg` files also contain interface IP addresses (`Tcpip\Parameters\Interfaces`) and device
instance IDs (`USBSTOR\Enum`) that Riegel does not use. **Treat the bundle as confidential and delete it after the
import.** Riegel converts it into a snapshot at import; the raw bundle never goes into an evidence pack.

## Plant folder

The plant folder is the customer's evidence record. Riegel writes into it only on an explicit action
(import, check with `--plant`, saving a register, approving, a report into `reports\`) and never in auditor mode
(`--nur-lesen` / *Prüfermodus*).

| File | Content | Personal data |
|---|---|---|
| `riegel-plant.json` | plant, groups, machines, machine identity (type, serial and order number, delivery date, builder, location), settings | names only if someone types them into free-text fields |
| `exceptions.json`, `tailoring.json`, `measures.json` | registers: rule, scope, reason, justification, reference, approving **role**, date, approval record; measures with owner role, due date, ticket | `approvedByName` is optional and empty unless entered; free texts |
| `history\…` | append-only copies of every saved register, plant file and approved state | as above |
| `snapshots\<host>\*.snapshot.json` + `.sha256` | the imported exports | see below |
| `imports\*.import.json` | import record: time, file hashes, collecting role, witness role, operator role | roles only |
| `runs\<host>\*.results.json` | one result per rule with expected and actual value, value source, evidence keys | actual values can name accounts (e.g. members of the local Administrators group) |
| `approved\<host>.json` | approved state: run, hashes, approving role, date, approval record | `approvedByName` if entered |
| `baselines\<id>.json` | imported customer baselines | none |
| `reports\<timestamp>\`, `remediation\<host>\<run>\` | generated documents | as the reports |
| `.riegel.lock` | lock file while registers are written | none |

## What a snapshot contains

Only what the [collection plan](../contracts/collection-plan.json) names (every item is listed in
[Change-request pack](collector-change-request.md)): security policy and audit policy, policy registry keys, LSA and
SMB settings, services (name, display name, state, start mode, account, path), optional features, hotfix IDs and dates,
**local user accounts** (name, SID, disabled, lockout, password flags, **date of the last logon**, password age),
members of the local Administrators and Remote Desktop Users groups, Defender status and preferences (including
**exclusion paths**, only with administrator rights), firewall profiles and rules (including **program paths**),
shares and their permissions, network adapters (no host IP addresses, but the **default gateway address**), the
WinHTTP proxy (proxy server and bypass list, which can name hosts or addresses), installed software (display name,
version, publisher), BitLocker and TPM state, the Wininit event 12, the host name, domain, machine GUID and the version
values of `CurrentVersion` (not the registered owner or product IDs).

Personal data in it: **host names, local account names and SIDs, last-logon dates of local accounts, the
automatic-logon user name, user-profile path segments, names of local group members** – and whatever the customer's
software writes into paths or display names.

**The Group Policy result** (`raw:gpresult-xml`, from `gpresult /scope computer /x`, not with `--minimal`) is stored as
the complete XML that Windows produces. It contains the computer name and domain, the site, the distinguished name of
the computer's OU, the names and links of applied and filtered GPOs, the domain controller that served them, the
security groups of the computer account and the settings the GPOs set (including registry values and file and
service settings). On a domain member that is a map of the domain's policy structure. `--pseudonymize` does **not**
change this XML (it changes the security template and the account items only); if that is not acceptable, collect with
`--minimal` (the rules that need gpresult become "not checkable"). Riegel uses it for the value source of policy
settings ([Evaluation](evaluation.md)). This has been checked with the synthetic fixtures; a capture from a real
domain member is still open. Network data in it: the **default gateway address** of each adapter, the
**WinHTTP proxy** settings and, with administrator rights, Defender's **IP address exclusions**
(`ExclusionIpAddress`) – infrastructure addresses rather than personal data, but confidential like the rest.

## Reports, evidence pack and remediation

- **PDF and Excel** contain host names, findings with actual values (which can name accounts), the registers and the
  machine identity. The default confidentiality marking is on every page.
- **The evidence pack** (`Riegel-Evidence_<plant>_<date>.zip`) contains the newest snapshot and run per machine, the
  registers as used, the plant file, the catalogue and the documents – it is as confidential as the plant folder.
  Customer baselines go in only as metadata unless `--include-baseline-content` is given.
- **Remediation files** contain old values of the machine for the rollback `.reg`; they never contain values that were
  redacted.
- **The diagnostics package** (*About* › *Create diagnostics package*) contains version, system information and the
  log of the last days – with machine, account, domain and server names, paths, SIDs and addresses replaced. It never
  contains snapshots, registers, runs, reports or setting values.

## On the analysis PC

| What | Where | Content |
|---|---|---|
| `settings.json` | `%LocalAppData%\Riegel` (Linux `~/.local/share/riegel`; `RIEGEL_DATA_DIR` overrides) | language, theme, default plant folder, thresholds, recently opened plant folders (paths) |
| `logs\riegel-<date>.log` | same folder | 7 days, at most 1 MB per day; never setting values, host names, accounts or paths (paths, host, user and domain names, accounts, SIDs and addresses in error messages are replaced before a line is written) |
| `handoff\<token>\` | same folder | the snapshot and checksum of *Check as administrator* (or only `error.txt` with an error code); deleted after the import, and also when collection, hand-over or import fail (the plant folder holds the imported copy). If the elevated account's files cannot be deleted, it stays; Riegel deletes such leftovers older than one day at its next start |
| `Riegel-Erfassung-<8 hex>\` | `%TEMP%` | the snapshot of *Check this PC* without elevation; deleted after the import, and also when collection or import fail. Riegel deletes leftovers older than one day at its next start (versions before 1.0.0 kept them) |
| `defaults.json` | `%ProgramData%\ICT-Baden\Riegel` | presets of the administrator, read only |
| Demo plant | `%TEMP%\Riegel-Demo-<8 hex>` | fictitious data; deleted when Riegel closes (older leftovers after a day) |

## Pseudonymisation

`Riegel.Collector.exe --pseudonymize` (and `Riegel.Cli.exe collect --pseudonymize`) replaces local user names, the
Winlogon user name, names of group members and user-profile path segments with `user-<first 8 hex of HMAC-SHA256>`,
keyed with the machine GUID; SIDs stay, and the header records `options.pseudonymized = true`. This is
**pseudonymised, not anonymous**: short or common names can be guessed by trying them. Checks that compare account
names (for example "built-in Administrator renamed") are not reliable on such snapshots. Use it when exports leave the
site and the account names are not needed.

## Deleting data

- **Snapshots on sticks:** delete them after the import; the plant folder keeps the imported copy.
- **A plant:** delete the plant folder (and its copies in evidence packs). Riegel keeps no other copy.
- **One machine:** delete `snapshots\<host>\`, `runs\<host>\`, `approved\<host>.json` and `history\approved\<host>\`,
  and remove it from `riegel-plant.json` in the app.
- **App data:** delete `%LocalAppData%\Riegel`. Uninstalling the MSI leaves it in place.
- **Registers** are the customer's change-controlled documents; their `history\` is append-only by design, so delete
  it only together with the plant.

## Works council and co-determination

Riegel checks the configuration of machines; it is **not intended to monitor the behaviour or performance of
employees**. Snapshots do contain data that relate to people – local account names and the dates of their last logon
(rule ACCT-17 flags accounts unused for a set number of days). Whether introducing and using Riegel is subject to
co-determination under **§ 87 Abs. 1 Nr. 6 BetrVG** (introduction and use of technical devices that are designed to
monitor the behaviour or performance of employees;
[gesetze-im-internet.de/betrvg/__87.html](https://www.gesetze-im-internet.de/betrvg/__87.html)) is for the company to
assess, together with its works council and its data protection officer where they exist. This is not legal advice.
`--pseudonymize` and limiting who can open plant folders reduce what can be seen.

## Who is responsible

The customer is the controller of the data in snapshots, plant folders and reports; ICT-Baden receives none of it
through the software. When ICT-Baden or a partner collects and evaluates on site, the service agreement governs that
processing. Security issues: [SECURITY.md](../SECURITY.md); support: [SUPPORT.md](../SUPPORT.md).
