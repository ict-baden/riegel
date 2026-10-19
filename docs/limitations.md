[Riegel](../README.md) › Limitations

# Limitations

What Riegel 0.1.0 does not do, what is only verified on CI runners and fixtures, and what can only be tested later on
real machines. When in doubt Riegel reports *nicht prüfbar* (not checkable) instead of a pass or a fail.

**Contents:** [Scope](#scope) · [How it was tested](#how-it-was-tested) · [Verification tasks V1–V8](#verification-tasks-v1v8) ·
[Only testable later, on real machines](#only-testable-later-on-real-machines) · [Collector](#collector) ·
[Built-in-commands bundles](#built-in-commands-bundles) · [Rules](#rules) · [Evaluation](#evaluation) ·
[Reports and outputs](#reports-and-outputs) · [Languages](#languages) · [Releases and signing](#releases-and-signing)

## Scope

- **Windows machines only.** Linux targets (edge devices, Linux-based HMIs) are not checked; use Lynis or OpenSCAP
  there. The Linux command line evaluates Windows snapshots only.
- **Machine settings only**, no per-user policies.
- **Domain controllers** are detected; catalogue 2026.10 is not designed for them, rules marked `excludeDc` do not
  apply there, and DCs get no hardening score.
- **No manual attestation** (BIOS password, boot order, locked cabinet) in 0.1.0; reserved for a later catalogue.
- **Not a compliance statement.** Riegel supports evidence for NIS2/BSIG, ISO 27001 A.8.9, IEC 62443, TISAX and GMP
  periodic reviews; it is not GMP-validated and does not cover MFA, backups or network segmentation.
- Riegel never applies anything: remediation exists only as files for the plant vendor.

## How it was tested

- **Rules:** every rule has test vectors (pass, fail, missing, not collected, and not applicable where it has
  conditions; 930+ vectors) that run against the real engine; a mutation test sets every evidence item of every rule to
  denied, not collected, truncated, error or missing and checks that none gives a pass without a cited default (more
  than 10,000 evaluations).
- **Independent oracle:** a snapshot of a Windows 11 Pro 25H2 workstation written from research measurements, with the
  expected result of all 153 rules for an elevated and a non-elevated collection; the real engine matches. Changes to
  the oracle during integration are documented with their reasons in `tests/Riegel.Tests/Oracle/README.md`.
- **Recorded fixtures** from GitHub runners `windows-2022`, `windows-2025` and `windows-11-arm`, anonymised, plus
  synthetic fixtures for Windows 7 SP1, Server 2012 R2, Server 2016, Windows 10 LTSC 2019, a German-locale machine, a
  DC, Defender passive with third-party antivirus and built-in bundles of Windows 7 and XP.
- **Read-only proof** on every collector build on the three runners (before/after of security policy, audit policy,
  policy registry keys, every service's start mode and state, Defender preferences, scheduled tasks, firewall and
  `Registry.pol`).
- No test changes the configuration of the machine it runs on.

## Verification tasks V1–V8

| # | Question | Result |
|---|---|---|
| V1 | Does the collector leave `wuauserv` untouched? | **Yes** on `windows-2022` (service disabled) and `windows-2025` (running; the update history was read). On `windows-11-arm` Windows itself changed the service during calibration, so V1 was not verifiable there. |
| V2 | Which Security-log events does one collection create? | Server 2022/2025: 4662, 4673, 4674, 4688 (collector, `secedit`, `gpresult`), 4798, 4799; fewer with `--minimal`. Depends on the audit policy; recorded per build in `Riegel-Collector-ReadOnlyProof.txt`. |
| V3 | Does Defender flag the collector or `--deep`? | **No** detection on any runner. |
| V4 | Is `AMRunningMode` present? | **Yes**, `Normal` on all three runners. |
| V5 | Does the collector start from a UNC share under CLR 2, and on Server 2008 R2 without the 3.5.1 feature? | **Unverified** – lab test before 1.0. |
| V6 | PDF/A with PDFsharp-MigraDoc 6.2.4? | **No**: the output is untagged, so PDF/A is not offered (`--pdfa` fails with RGL-0520). |
| V7 | `NetUserEnum`, `MSNdis_PhysicalMediumType`, `GetSecurityDescriptor` without admin; WSC API from net35; Wininit event 12 | The first two work without admin on all runners. `GetSecurityDescriptor` without admin was verified only on a workstation: the runners have only administrative shares, which have no share security object. The WSC API works from net35 on Windows 11 x64 and Arm64 and is not registered on servers (as Microsoft documents). Event 12 carries the protection level (`4`). |
| V8 | Does `HNetCfg.FwPolicy2` return GPO-deployed rules? | The COM rule count equals the local store on all runners, which have no GPO rules. Rules from a **domain** GPO: **unverified** (no domain in CI). |

## Only testable later, on real machines

These are open tasks for a lab and a real OT environment (gate for version 1.0):

- Starts on Windows 7, 8.1, Server 2008 R2, 2012 R2, Windows 10 2016 LTSB and Embedded/IoT images; the UNC start under
  CLR 2 (V5).
- Real AppLocker and App Control enforcement against the collector, and the signer identity once releases are signed.
- German, French and Chinese Windows (account and group names are matched by SID; localised tool output only matters
  for built-in bundles).
- Value-source attribution `domainGpo` in a real domain (**experimental**: the report says "Quelle: Domänen-GPO (nicht
  in echter Domäne getestet)"), domain controllers, Server Core.
- Third-party antivirus, Defender passive mode and EDR block mode, and EDR reactions to a collection (the collector does
  discovery-like things: tell the SOC before a collection round).
- FW-11 with firewall rules deployed by a domain GPO (V8).
- Conflicts with real plant software, and Windows Update agent behaviour (the collector queries the update history only
  when `wuauserv` already runs).
- Temporary RSoP WMI namespaces created by `gpresult` (**unverified**).
- PDF with Chinese text on Chinese Windows and on Linux distributions whose CJK fonts are CFF-only.
- SmartScreen behaviour with signed releases.
- Built-in collection on Windows XP (best effort; the XP `reg query` format in the fixture is written from memory).
- The Linux arm64 command line at runtime (it is cross-built and not run in CI).

## Collector

- **net35 needs consent.** The collector targets .NET Framework 3.5/4.x, a deviation from the ICT-Baden guideline that
  needs the management's approval; no customer use before that.
- `windows-11-arm`: `gpresult /x` answers "Access Denied" even to an elevated administrator, so an elevated collection
  there exits with 1 (`raw:gpresult-xml` is an error and value sources stay unknown).
- `--deep` is needed for the LSASS protection level; without it CRED-06 relies on Wininit event 12 and is *not
  checkable* when the event is not found (the System log may have wrapped).
- Without administrator rights the security template, audit policy, user rights, BitLocker, TPM, Defender exclusions
  and `gpresult` are denied; those rules are not checkable and reports carry "Incomplete check" (*Unvollständige Prüfung*).
- The Windows Update history date is treated as UTC (**unverified**). Microsoft Learn describes the WSC interfaces as
  derived from `IUnknown`, the SDK header from `IDispatch`; the collector uses the header's dual interface.
- The Security-log 4688 cross-check of the read-only proof works only where process-creation auditing is on.
- Demand-start services that Windows ends when idle during a run are listed in the proof but not counted as a change.

## Built-in-commands bundles

- `collectedAtUtc` is the date in the folder name at 00:00 UTC: ages can be off by less than a day.
- No domain role and no runtime state (Defender, VBS, BitLocker, firewall): those rules are not checkable, LIFE-01
  fails on unsupported systems.
- `ServerNT`/`LanmanNT` product types give no client/server decision (the cited Microsoft page does not separate server
  and DC); `WinNT` meaning client is documented for XP only.

## Rules

- **Inventory instead of checks** where no shippable source sets an expected value: NET-29, SVC-04, RDP-08 and
  TLS-06/07/08/11 ([Rule sources › Deviations](rule-sources.md#deviations-from-the-planned-catalogue)).
- **TLS-02 to TLS-05** on Windows 11 25H2 (build 26200) are not checkable when the values are not set: Microsoft's
  Schannel table ends at 24H2 / Server 2025.
- **APP-01** recognises App Control (WDAC) user-mode enforcement only; AppLocker enforcement cannot be tested with the
  closed vocabulary yet – machines using AppLocker or a third-party allow-listing product need an exception.
- **ENC-04** checks the pre-boot PIN policy, not the actual key protectors; **ENC-06** checks the DMA policy, not the
  runtime Kernel DMA Protection state.
- **DEF-17** and **MED-06** do not recognise settings made only through MDM; the ASR rules read the effective state from
  Defender first, and a value only in the Group Policy store is "configured, effect not proven".
- **Office detection** (ASR-05) and **remote-access tools** (APP-06) match documented keys and product names; display
  names that differ from the vendor's product name are missed.
- ACCT-14 recognises Windows LAPS only (legacy Microsoft LAPS value names are not documented on Microsoft Learn); Entra
  join is not detected.
- Expected values follow ACSC, Microsoft's CC BY documentation or DISA; where they disagree the choice is stated in the
  rule text (for example ACCT-01 ≥ 14, ACCT-06 1–5, CRED-10 ≤ 4, ASR-13 block).
- STIG references are empty for the audit, PowerShell, RDP, removable-media and TLS rules.

## Evaluation

- On the **first import** of a machine there is no previous run, so every open high finding counts as "newly open
  high": with `--alert change` the first check is CRITICAL.
- AMRunningMode values other than `Normal`, `Passive` and `EDR Block Mode` give an unknown Defender mode (the form
  `Passive Mode` is accepted as well). The SecurityCenter2 `productState` encoding is not documented by Microsoft and is
  used only as heuristic evidence.
- How `secedit` encodes "password never expires" is unverified (read as a maximum age of 0 or −1).
- A catalogue older than six months gives a banner; lifecycle and build tables need a new catalogue for new Windows
  releases (UBR newer than the table: *not checkable*, "catalogue too old").

## Reports and outputs

- **No PDF/A** (V6) and no electronic signatures; the PDF is for signing on paper.
- Chinese needs a TrueType system font (Microsoft YaHei on Windows, WenQuanYi Micro Hei on Linux); without one the
  document is written in English with RGL-0510. Bold Chinese on Linux is simulated. Chinese text inside a non-Chinese
  document may show missing glyphs.
- Long German compound words are split mid-word in narrow table columns (no hyphenation).
- The evidence pack is not signed: its hashes prove that files were not changed after packing, not where they came
  from. The collection record with a witness and `compare-snapshot` close that gap.
- A pack can be verified only with the catalogue it was evaluated with; re-evaluate before packing after a catalogue
  update.
- Remediation `.reg` files were checked against the documented syntax but never imported by Riegel's tests
  (Riegel never changes a machine); `audit.csv` needs its first comment line removed before a GPO import.
- *Check this PC* leaves its snapshot in the data folder (`handoff\`) or `%TEMP%` after the import
  ([Data and privacy](data-and-privacy.md#on-the-analysis-pc)).
- On Linux the empty `.riegel.lock` file stays in the plant folder after use (flock semantics).

## Languages

- English and German are complete. Spanish, French and Chinese are machine pre-translations, not yet reviewed by
  native speakers: every language file says so at the top ("Maschinell vorübersetzt – TODO-übersetzen"), entries
  that are especially uncertain also carry `TODO-übersetzen`, and documents carry "Machine pre-translation – not
  reviewed". Rule texts are English in those languages.
- German remedies quote the English Group Policy names.

## Releases and signing

- **0.1.0 is not code-signed.** SmartScreen may warn; allow-listing works by hash only. Signing is a hard gate before
  customer pilots on OT or validated machines.
- The trademark check of the name (including "RIEGL") is still open.
- "Auditors can read the code and download the matching version for `verify`" holds only once the repository and its
  tag releases are public.
