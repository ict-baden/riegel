[Riegel](../README.md) › Collector

# Portable collector

**Contents:** [What it is](#what-it-is) · [Before you start: .NET](#before-you-start-is-net-framework-there) ·
[USB workflow](#usb-workflow) · [When USB is blocked](#when-usb-is-blocked-share-unc-and-transfer-station) ·
[Options and exit codes](#options-and-exit-codes) · [What it reads](#what-it-reads) ·
[What it leaves behind](#what-it-leaves-behind) · [Built-in commands](#built-in-commands) ·
[Allow-listing (AppLocker)](#allow-listing-applocker) · [Versions and releases](#versions-and-releases) ·
[From the app and the CLI](#from-the-app-and-the-command-line) · [Verified and not verified](#verified-and-not-verified) ·
[How the proof tells the collector from Windows](#how-the-proof-tells-the-collector-from-windows)

## What it is

`Riegel.Collector.exe` is a small program (about 125 KB) that reads the security configuration of one Windows
machine and writes one snapshot file next to itself or into the folder you name. It installs nothing, changes no
configuration and opens no network connection; Windows itself records that a program ran, and `gpresult` can make
Windows start the Group Policy Client service on demand ([What it leaves behind](#what-it-leaves-behind)). The snapshot is evaluated later, on another PC, with
Riegel (the MSI, `Riegel.exe` or the command line).

- **Runs on** Windows 7 SP1 to Windows 11 and Windows Server 2008 R2 SP1 to 2025, x86, x64 and Arm64, with the .NET
  Framework that ships with Windows: CLR 4 (.NET 4.5 and later, Windows 8 / Server 2012 and newer) or CLR 2
  (.NET 3.5.1, Windows 7 SP1). `Riegel.Collector.exe.config` lists `v4.0` first and `v2.0.50727` second; keep it
  next to the exe, otherwise Windows 8 and newer ask to install .NET 3.5.
- **Reads** registry values, WMI classes, a few documented Windows APIs, one event and the exports of `secedit` and
  `gpresult` – exactly the list in the [collection plan](collector-change-request.md#every-item-read), which is
  embedded in the exe. It never writes the registry, calls only read-only WMI methods and starts no program other
  than `secedit.exe` and `gpresult.exe` with fixed arguments (none with `--minimal`). Source-scan tests enforce
  this.
- **Writes** `<HOST>_<yyyyMMddTHHmmssZ>.snapshot.json` (UTF-8 JSON, [format](file-formats.md#snapshot-riegelsnapshot1))
  and `<file>.sha256` (sha256sum format) – nothing else. It creates the export file first: if the target is not
  writable it stops with exit code 3 and has collected nothing.
- **Secrets** are redacted while collecting: registry string values whose names contain `password`, `passwd`, `pwd`,
  `secret`, `token`, `credential`, `passphrase` or `community` (and `DefaultPassword`/`AltDefaultPassword` of any
  type) are written as "redacted" with only an empty/not-empty flag. Numeric switches with such names (for example
  `LocalAccountTokenFilterPolicy`, `LimitBlankPasswordUse`, `DisablePasswordSaving`) stay readable because rules
  check them; they hold no secret. SNMP community names under `…\SNMP\Parameters\ValidCommunities` (and the trap
  community subkeys under `…\SNMP\Parameters\TrapConfiguration`) become `redacted-<n>`.
- **Names become SIDs** on the target: user rights come as SIDs from the LSA, and the names that `secedit` writes into
  `[Privilege Rights]` are translated to `*S-…` before they leave the machine, so a German "Gast" and an English
  "Guest" give the same evidence.

## Before you start: is .NET Framework there?

Open a command prompt on the target and run:

```
reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP"
```

The collector can start if the output lists `v4` (with `Full` below it) or `v3.5` (on Windows 7 often together with
`v2.0.50727`). If neither is listed, do not use the collector on that machine – use the
[built-in commands](#built-in-commands) instead. Do not install .NET on a validated machine just for Riegel; that would
itself be a change.

- **Windows 7 SP1:** .NET 3.5.1 (CLR 2) is part of Windows and on by default, according to Microsoft's table
  [.NET Framework versions and dependencies](https://learn.microsoft.com/en-us/dotnet/framework/install/versions-and-dependencies)
  (seen 2026-10-18).
- **Windows Server 2008 R2 SP1:** the same table marks .NET 3.5.1 as a feature that "must be enabled through the
  Server Manager". Whether the collector, which references only .NET 2.0-layer assemblies, starts there without that
  feature is **unverified** (lab test, verification task V5). Until then: check with the command above; without
  `v3.5`, use the built-in commands.
- **Windows Embedded / IoT images** (WES7, POSReady, WE8S) may be built without .NET (**unverified**); the same check
  decides.

## USB workflow

1. **On the analysis PC:** in Riegel, *Copy collector to a drive* writes the collector, its `.exe.config`,
   `collect-builtin.cmd`, the README and `UEBERGABE.txt` (hand-over sheet with version, SHA-256 of every file, how
   to run it and what it leaves behind) to the stick. Without the app, unpack `Riegel-Collector-<version>.zip` from a
   **tag release** (see [versions](#versions-and-releases)).
2. **Check the files on the target** before running them:
   `certutil -hashfile Riegel.Collector.exe SHA256` must print the value on the hand-over sheet and in the
   release's `SHA256SUMS.txt`.
3. **Run it elevated:** open a command prompt with *Run as administrator*, change to the stick and run
   `Riegel.Collector.exe`. Without administrator rights it still runs, but about half of the areas (user rights,
   audit policy, security template, BitLocker, TPM, Defender exclusions, gpresult) are marked "not checkable" and the
   exit code is 1.
4. **Note** the export path and the SHA-256 that the collector prints, for example on the hand-over sheet.
5. **Back on the analysis PC:** import the file (`Riegel.Cli.exe import <file> --plant <folder>` or drag it into the
   app). Riegel checks the `.sha256` file and keeps the hash in the collection record.

The SHA-256 protects against accidental damage on the way, not against deliberate tampering by whoever carries the
stick; there is no key on the target to sign with. The stick is confidential (host names, local account names,
installed software): delete the exports after the import.

## When USB is blocked: share, UNC and transfer station

Hardened machines often block writing to USB storage (removable-storage policies). The collector then stops before
collecting with:

```
RGL-0102: Target not writable - use --out with a share or another folder. Nothing was collected.
```

- **Write to a share:** start the collector from the stick (reading is usually allowed) and write elsewhere:
  `Riegel.Collector.exe --out \\server\share\riegel`. The account needs write access to the share; the collector
  itself opens no connection – Windows does, for the path you give.
- **Start from a share:** copy the collector to a share the machine can read and run it from there with `--out`.
  Starting it from a UNC path has not been tested yet; under CLR 2 (Windows 7) it is an open verification task (V5).
  If it refuses to start, copy it to a local folder (for example `%TEMP%`) and run it from there.
- **Transfer station:** where the plant network has a dedicated transfer PC (data lock, "Datenschleuse"), bring the
  collector in through it like any other approved file and take the export out the same way; the `.sha256` file
  lets the receiving side check that the file arrived unchanged.
- **No foreign program at all:** use the [built-in commands](#built-in-commands).

## Options and exit codes

```
Riegel.Collector.exe [--out <folder>] [--gzip] [--minimal] [--deep] [--pseudonymize] [--quiet] [--nur-lesen] [--version] [--help]
```

| Option | Effect |
|---|---|
| `--out <folder>` | write the export there (default: the folder of `Riegel.Collector.exe`) |
| `--gzip` | write `.snapshot.json.gz` (GZip); Riegel reads both |
| `--minimal` | start no other program (no `secedit`, no `gpresult`), no Windows Update query, firewall profiles without the rule list; the affected rules become "not checkable" and comparisons show "scope changed" |
| `--deep` | also read the protection level of the LSASS process (`OpenProcess` with `PROCESS_QUERY_LIMITED_INFORMATION`); off by default because EDR products may report any LSASS handle |
| `--pseudonymize` | replace local user names, Winlogon user names, group member names and profile path segments with `user-<8 hex>` (HMAC-SHA256 keyed with the MachineGuid); SIDs stay. Pseudonymised, not anonymous: short names can be guessed. Checks that compare account names (for example "Administrator renamed") are not reliable on such snapshots. The Group Policy result (gpresult XML with computer, domain, OU, GPO and group names) is **not** pseudonymised – use `--minimal` to leave it out ([Data and privacy](data-and-privacy.md#what-a-snapshot-contains)) |
| `--quiet` | print only the export path and its SHA-256 |
| `--nur-lesen` | accepted so that scripts can pass the same read-only switch to every ICT-Baden tool; it changes nothing because the collector never writes configuration |
| `--version`, `--help` | print and exit with 0 |

| Exit code | Meaning |
|---|---|
| `0` | complete: elevated and no item denied or failed |
| `1` | written, but incomplete: not elevated, or an item was denied or failed (the export is still valid evidence for everything that was read) |
| `3` | nothing written: target not writable (`RGL-0102`), unknown option (`RGL-0101`), or an unexpected error (`RGL-0100`) |

Messages appear in the Windows display language (English, German, French, Spanish, Chinese); if the console code page
cannot show that language, the collector falls back to English. The process runs with *below normal* priority, and
`secedit`/`gpresult` inherit it.

Measured on a Windows 11 Pro 25H2 workstation (2026-10-18, read-only): elevated under CLR 4 9 s and about 2.1 MB
(about 195 KB with `--gzip`), elevated under CLR 2 7 s, as a standard user 13 s (BitLocker and TPM queries wait for
WMI's access-denied answer). Most of the size is the local firewall rule store; the
[runner proof](#verified-and-not-verified) records the times on the GitHub runners.

## What it reads

The full, generated list is in the [change-request pack](collector-change-request.md#every-item-read). In short:
registry policy roots and security keys (64-bit view, at most 50,000 values, depth 8; keys only SYSTEM may read are
listed as denied in the snapshot's coverage), WMI classes for the OS, services, features, updates, local accounts,
adapters, shares, Defender, Security Center, Device Guard, BitLocker, TPM and SMB, the password and lockout policy,
local accounts and group members, user rights, the audit policy, the firewall, the Windows Update history (only if the
Windows Update service is already running – the collector never starts it), the WinHTTP proxy setting, the Security
Center antivirus list, the Wininit event that proves LSA protection after the last boot, `Registry.pol`, and the
`secedit` and `gpresult` exports.

| Area | Without administrator rights |
|---|---|
| Registry (policies, LSA, SMB, RDP, firewall store, Defender …) | readable (a few subkeys are SYSTEM-only either way) |
| Password policy, local accounts, Administrators/Remote Desktop Users members, firewall, Defender status, Device Guard, Security Center, SMB, shares | readable |
| User rights, audit policy, `secedit`, `gpresult`, BitLocker, TPM, Defender exclusions, `EventLog\Security` key | denied → "not checkable" |

## What it leaves behind

"Ändert keine Konfiguration. Schreibt nur seinen Export (und die Prüfsummendatei) in den gewählten Ordner. Windows
selbst vermerkt, dass ein Programm lief." In detail:

- `secedit` and `gpresult` write their output into a fresh `%TEMP%\riegel-<random>` folder (path at most 127
  characters, as `gpresult` requires); the collector deletes the folder afterwards. `secedit` gets its own `/log`, so
  `%windir%\security\logs\scesrv.log` stays untouched.
- `gpresult` calls the Group Policy Client service. Where that service is trigger-started (Windows 11, for example),
  **Windows starts it on demand**, and it stops again when idle; its start mode does not change. The read-only proof
  lists such on-demand starts. `--minimal` avoids it. On the Server 2025 runner the trigger-started Remote Registry
  service was running after full collections and stopped again on idle; that `gpresult` is what makes Windows start
  it is likely but **unverified**. Its start mode does not change either. Demand-start services (start mode
  *Manual*, such as the Windows Update Medic Service on Server 2025) that Windows ends when idle during a run are listed
  in the proof as well and do not count as a change: a reading program cannot stop a service. A service that is
  *started* without a start trigger is measured again, every target three more times, and stays a failure if the start
  comes back ([how the proof tells the collector from Windows](#how-the-proof-tells-the-collector-from-windows)); any
  start-mode change and any change of `wuauserv` stay failures. The servicing
  services of Windows Update and Windows Installer (`WaaSMedicSvc`, `UsoSvc`, `TrustedInstaller`), which Windows starts
  and stops on its own schedule, are listed and not counted as long as their start mode does not change. For a
  changed registry export, security template or audit policy the proof names the changed lines.
- The .NET runtime (CLR 4) writes a usage log under `%LocalAppData%\Microsoft\CLR_v4.0\UsageLogs\`.
- Windows records that a program ran: Prefetch, AmCache/ShimCache and similar execution artefacts, and event 4688
  for the collector and its child processes if process-creation auditing is on.
- Security log: enumerating local group members can create event 4799 (and possibly 4798) when *Audit Security Group
  Management* is on, and enabling `SeSecurityPrivilege` for the audit-policy query can create privilege-use events.
  A machine hardened by Riegel (rule AUD-02) therefore records every collection. The proof records the event IDs that
  one run created on each runner.
- `gpresult` may build RSoP data, possibly in temporary WMI namespaces (**unverified**); a transient effect.

**SOC and EDR:** the collector does things that look like discovery – it reads the password policy, local
administrators and user rights, runs `secedit` and `gpresult`, and is started from removable media. Tell your SOC
before a collection round, and prefer `--minimal` or leave out `--deep` where an EDR product is strict.

## Built-in commands

For machines on which no foreign program may run, and for Windows XP, Vista and Server 2003/2008,
`collect-builtin.cmd` (in `Riegel-Collector-<version>.zip`) uses only Windows' own programs. It writes a new folder
`<HOST>_<yyyyMMdd>.riegel-builtin` next to itself or below the folder you give as the first argument, and nothing
else; every command's errorlevel goes into `exitcodes.txt`. Exit codes: 0 complete, 1 written but incomplete (run it
elevated; `secedit` and `auditpol` need administrator rights), 3 nothing written (target not writable `RGL-0102`, or
the folder exists already `RGL-0106`).

```
collect-builtin.cmd [target folder]
```

Before its first command the script resolves the target folder to a full path, changes to `%SystemRoot%\System32` and
sets `PATH` and `PATHEXT` to Windows' own folders. `cmd.exe` looks for a program without a path in the current folder
first; on a USB stick or a share, a foreign `reg.bat` or `find.exe` placed there would otherwise run with administrator
rights.

The same commands, for typing by hand (replace `<bundle>` with a new folder named `<HOST>_<yyyyMMdd>.riegel-builtin`,
given as a full path, create the subfolder `reg` first, and note each command's `%errorlevel%` in `exitcodes.txt` as
`<step>=<code>`). Change to the Windows folder first – do not type them with the stick or share as the current folder:

```
cd /d %SystemRoot%\System32
hostname > "<bundle>\hostname.txt"
ver > "<bundle>\ver.txt"
secedit /export /cfg "<bundle>\secedit.inf" /log "<bundle>\secedit.log" /quiet
auditpol /backup /file:"<bundle>\auditpol.csv"
reg export "HKLM\SOFTWARE\Policies" "<bundle>\reg\01-policies.reg"
reg export "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies" "<bundle>\reg\02-currentversion-policies.reg"
reg export "HKLM\SYSTEM\CurrentControlSet\Control\Lsa" "<bundle>\reg\03-lsa.reg"
reg export "HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders" "<bundle>\reg\04-securityproviders.reg"
reg export "HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters" "<bundle>\reg\05-lanmanserver.reg"
reg export "HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters" "<bundle>\reg\06-lanmanworkstation.reg"
reg export "HKLM\SYSTEM\CurrentControlSet\Services\NetBT\Parameters" "<bundle>\reg\07-netbt.reg"
reg export "HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy" "<bundle>\reg\08-firewallpolicy.reg"
reg export "HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server" "<bundle>\reg\09-terminalserver.reg"
reg export "HKLM\SYSTEM\CurrentControlSet\Services\USBSTOR" "<bundle>\reg\10-usbstor.reg"
reg export "HKLM\SYSTEM\CurrentControlSet\Services\mrxsmb10" "<bundle>\reg\11-mrxsmb10.reg"
reg export "HKLM\SYSTEM\CurrentControlSet\Control\ProductOptions" "<bundle>\reg\12-productoptions.reg"
reg export "HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" "<bundle>\reg\13-tcpip.reg"
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v CurrentBuild > "<bundle>\currentversion.txt"
for %V in (CurrentBuildNumber CurrentMajorVersionNumber CurrentMinorVersionNumber CurrentVersion UBR EditionID InstallationType DisplayVersion ReleaseId ProductName CSDVersion) do reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v %V >> "<bundle>\currentversion.txt"
reg query "HKLM\SYSTEM\CurrentControlSet\Services" /s /v Start > "<bundle>\services-start.txt"
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoAdminLogon > "<bundle>\winlogon.txt"
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultUserName >> "<bundle>\winlogon.txt"
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultDomainName >> "<bundle>\winlogon.txt"
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultPassword >nul 2>&1 && echo DefaultPassword=present>> "<bundle>\winlogon.txt" || echo DefaultPassword=absent>> "<bundle>\winlogon.txt"
```

- Skip `auditpol` on Windows XP and Server 2003; they have no `auditpol /backup`.
- Run the commands in a **64-bit** command prompt on 64-bit Windows. A 32-bit `cmd` (started from a 32-bit
  remote-maintenance tool or file manager) runs the 32-bit `reg.exe`, which reads `HKLM\SOFTWARE\WOW6432Node` – there
  `Winlogon\AutoAdminLogon` is missing, and a machine with automatic logon would look clean. `collect-builtin.cmd`
  therefore restarts itself through `%SystemRoot%\Sysnative\cmd.exe` and writes `wow64=0` (or `wow64=1` when that is
  not possible) to `exitcodes.txt`; typed by hand, add `wow64=0` only after checking that `%PROCESSOR_ARCHITEW6432%` is
  not set. Without `wow64=0`, a Winlogon value missing from `winlogon.txt` is "not collected", never "not set".
- **Never** `reg export` the whole `Services` tree or `Windows NT\CurrentVersion`: they contain vendor service
  credentials, SNMP community strings and, under `Winlogon`, a cleartext `DefaultPassword`. That is why services come
  only as their `Start` values and `CurrentVersion` only with its version values (`CurrentBuild`, `UBR`, `EditionID`,
  `InstallationType`, `DisplayVersion` …, each with `reg query /v`); `RegisteredOwner`, `ProductId` and
  `DigitalProductId` are not needed and not exported. In a batch file the `for` loop needs `%%V` instead of `%V`.
- `reg export` and `reg query` return 1 both for a missing and for an unreadable key; the batch therefore counts only
  `secedit`, `auditpol`, `currentversion` and `services-start` for its exit code.
- The bundle contains interface IP addresses (`Tcpip\Parameters\Interfaces`) and device instance IDs (`USBSTOR\Enum`),
  which the collector does not read. Riegel imports only what its rules need; treat the bundle as confidential and
  delete it after the import. The raw bundle never goes into an evidence pack.
- Coverage is lower: runtime states (Defender, VBS, BitLocker, firewall runtime) are "not checkable".

## Allow-listing (AppLocker)

Where AppLocker or App Control blocks unknown programs, an administrator must allow the collector first. Each release
publishes `Riegel-Collector-allowlist-<version>.zip`, computed on the CI runner (never on a customer machine):

- `AppLockerFileInformation.txt` – the output of `Get-AppLockerFileInformation` for `Riegel.Collector.exe`, including
  the hash as AppLocker sees it. AppLocker hash rules for programs use the Authenticode hash, not the plain file
  SHA-256 from `SHA256SUMS.txt` (**unverified**) – use the values from this file.
- `Riegel-Collector-AppLocker-HashRule.xml` – a hash rule generated with `New-AppLockerPolicy -RuleType Hash -Xml`. It
  was printed, not applied. Review it, then merge it into your own policy.
- `SHA256.txt` and a README.

Releases are unsigned until the signing account exists (owner item); then a publisher rule replaces the hash rule.
Signing changes the bytes, so a hash rule then changes with every release. A hash rule stays valid as long as the
collector's SHA-256 is unchanged: the release notes say "Collector unchanged since <version>".

## Versions and releases

- The collector has **its own version** (`src/Riegel.Collector/collector-version.txt`, now 0.1.0), independent of the
  app. It changes only when the collection code or the embedded collection plan changes, and every snapshot names it.
- The build is **byte-for-byte reproducible**; CI builds it twice and compares the SHA-256, and a hash lock fails the
  build if the bytes change without a version bump.
- **OT and validated machines use tag releases (`vX.Y.Z`), never `latest`.** `latest` is rebuilt nightly from
  `main`; a tag release is fixed, has its own `SHA256SUMS.txt`, read-only proof and AppLocker data, and can be named
  in a change request.
- No customer pilot on OT or validated machines with an unsigned collector (owner decision).

## From the app and the command line

The same collection code is compiled into Riegel itself, so the app and `Riegel.Cli.exe collect` produce the same
items (only the `collector` header and the origin `app` differ). On the analysis PC, `Riegel.Cli.exe collect --out
<folder>` in an elevated console collects this machine; it needs .NET 10 and Windows 10 1809 / Server 2019 or later.

In the app, *Check this PC* without administrator rights starts Riegel once more with *Run as administrator* (UAC). The
elevated process creates `%LocalAppData%\Riegel\handoff\<token>\` itself – refusing an existing folder and any link or
junction in the path – writes only the snapshot and its `.sha256` there and exits; the app then imports it. If the
collection fails, the elevated process writes only `error.txt` with the error code (one line, no content) instead, so
the app can name the cause next to `RGL-0115` and the exit code. If a
different administrator account confirms the UAC prompt ("over the shoulder"), that account has a different profile
and the hand-over cannot work: the app says so and points to `Riegel.Cli.exe collect` in an elevated console of that
account.

## Verified and not verified

Results of 2026-10-18 on the GitHub runners `windows-2022` (Server 2022, build 20348), `windows-2025` (Server 2025,
build 26100) and `windows-11-arm` (Windows 11 Enterprise 25H2 Arm64, build 26200), workflow `collector.yml`, and on a
Windows 11 Pro 25H2 x64 workstation (read-only). Every release repeats V1–V3 in `Riegel-Collector-ReadOnlyProof-<version>.txt`.

| # | Question | Status |
|---|---|---|
| V1 | Does the collector leave `wuauserv` untouched? | **Yes** where it could be measured: on `windows-2022` (service disabled) and `windows-2025` (service running, so the collector did read the update history) its start mode and state were unchanged by every measured run. On `windows-11-arm` Windows itself re-enabled and started the service during calibration, so V1 was not verifiable on that runner |
| V2 | Which Security-log events does one collection create? | Server 2022/2025, one elevated run: 4662 (34, an operation on an object), 4673 and 4674 (privileged service called, operation on a privileged object), 4688 (process creation: the collector, `secedit`, `gpresult`), 4798 (8 on Server 2025, 10 on Server 2022: a user's local group membership enumerated) and 4799 (2: a security-enabled local group's membership enumerated – the collector reads Administrators and Remote Desktop Users). `--minimal`: 4674 and 4688 on Server 2022; 4662, 4673, 4674, 4688 and 4798 on Server 2025; no 4799. Without the collector the runners logged 4624, 4634, 4672, 4674 and 4688 in the same time. Which events appear depends on the machine's audit policy: the Windows 11 Arm64 runner (no process-creation auditing) logged only 4798 (10) and 4799 (2) per run, 4798 (5) with `--minimal`, and nothing for `collect-builtin.cmd` |
| V3 | Does Defender flag the collector or `--deep`? | **No** detection on any of the three runners (Get-MpThreatDetection before and after, Defender in normal mode) |
| V4 | Is `AMRunningMode` present on all runner builds? | **Yes**, value `Normal` on all three |
| V5 | UNC start under CLR 2; 2008 R2 without the 3.5.1 feature | **unverified** – lab test before 1.0 |
| V7 | `NetUserEnum` level 2, `MSNdis_PhysicalMediumType`, `GetSecurityDescriptor` without admin; WSC API from net35; Wininit event 12 data | `NetUserEnum` level 2 and `MSNdis_PhysicalMediumType` work without administrator rights on all three runners and the workstation. `GetSecurityDescriptor` works without administrator rights for a normal share on the workstation; the runners have only administrative shares (`ADMIN$`, `C$`, `IPC$`), for which Windows has no share security object (WMI "not found", also for administrators). The WSC API works from the net35 build on Windows 11 (x64 and Arm64); on Server 2022/2025 the class is not registered and the item is "not collected", as Microsoft documents (no server support). Event 12 of Wininit carries one data value, the protection level (`4`, found on the Arm64 runner and the workstation); on the servers, without LSA protection, there is none after the last boot (`found: false`) |
| V8 | Does `HNetCfg.FwPolicy2` return GPO-deployed rules? | The rule count from COM equals the count in the local registry store on all three runners (314, 348, 502); there are no GPO firewall rules on runners (`SOFTWARE\Policies\Microsoft\WindowsFirewall\FirewallRules` absent). Whether COM returns rules deployed by a **domain** GPO remains **unverified** (no domain on runners; Limitations) |

Two runner facts the fixtures keep: on `windows-11-arm`, `gpresult` answers "Access Denied" even to an elevated
administrator, so an elevated collection there ends with exit code 1 (`raw:gpresult-xml` is an error); and without
administrator rights the `EventLog\Security` key is denied, in addition to the areas listed [above](#what-it-reads).

The Windows Security Center interfaces were checked against Microsoft Learn
([IWSCProductList](https://learn.microsoft.com/en-us/windows/win32/api/iwscapi/nn-iwscapi-iwscproductlist),
[IWscProduct](https://learn.microsoft.com/en-us/windows/win32/api/iwscapi/nn-iwscapi-iwscproduct), seen 2026-10-18:
Windows 8 clients and later, no server) and the `iwscapi.h`/`wscapi.h` headers of the Windows SDK 10.0.26100.0 for the
interface identifiers, the method order and the state values.

Not testable without real machines (see the limitations page): Windows 7, 8.1, 2008 R2, 2012 R2 and 2016 starts; UNC
start under CLR 2; real AppLocker/App Control enforcement; German, French and Chinese Windows; domain GPOs; domain
controllers; Server Core; third-party antivirus and EDR reactions; Windows XP built-in collection.

## How the proof tells the collector from Windows

A runner is a live Windows machine: services start and stop, scheduled tasks run, Windows Update and Defender do their
work. The proof must not count that as the collector's doing, and it must not explain away anything the collector
does. These are its rules:

1. **Configuration never changes.** Security template, audit policy, the registry exports, every service's start mode,
   the Defender preferences, the firewall profiles and rules, `Registry.pol`, the scheduled tasks with their enabled
   state and leftover `%TEMP%\riegel-*` folders are compared before and after the warm-up and every measured run – so
   every target is judged in two runs. Any difference fails the proof. The repetitions (rule 3) compare them too, run by
   run, and list every difference, but only their services decide: the longer a proof measures, the more often a one-off
   change by Windows falls into it (on Server 2025, for example, `RunAsPPL` and `IsPplAutoEnabled` under `Lsa` and the
   start mode of BITS). A value that changes on its
   own during the first calibration run is reported as a warning and left out of the measured comparison – except the
   firewall, which is never left out: a firewall change in a calibration run that is not one of the known changes below
   fails the proof, because it can be a late effect of the run before. The only changes by Windows that the proof accepts are on two short lists, and
   each needs its own record:
   - **Scheduled tasks:** `PrintJobCleanupTask` disabled by the task host (`taskhostw.exe`, event 4701). The Security
     log must name the client process (events 4698–4702) and its program (event 4688); its parent must be the Task
     Scheduler service, and neither may belong to a run's process tree (collected from a WMI process trace and from
     event 4688).
   - **Firewall rules:** rules that the firewall service writes itself, for packaged apps and Windows components
     (account `NT SERVICE\mpssvc`, program `svchost.exe`), and the `WinDefend` rules that a Defender platform update
     replaces (`MsMpEng.exe` under `C:\ProgramData\Microsoft\Windows Defender\Platform\` as SYSTEM). Only rule
     entries in the rule keys may have changed in the firewall store (compared key by key), and the Firewall log
     (events 2097, 2099, 2052) must name the program and account for every changed rule; a program that writes a rule
     appears there with its own path and account.
   - **Start modes:** BITS switches itself between *Manual* and *Automatic* when transfers (for example for Windows
     Update) are queued or done. The System log must record the change (event 7040) by SYSTEM; the runs of the proof
     run as an administrator, never as SYSTEM.
   - **Defender preferences:** a preference that did not exist before appears with a Defender platform update; it counts
     only when the Defender platform version changed in the same interval. A changed value of an existing preference
     always fails.

   Without such a record – auditing off, or an older Windows – the change fails. The proof names the originator in
   every case.
2. **Services Windows starts and stops by design** are listed, not counted, as long as their start mode stays the same:
   services with a start trigger (Windows starts them on demand, e.g. the Group Policy Client when `gpresult` calls it),
   the servicing services of Windows Update and Windows Installer, and demand-start services (start mode *Manual*) that
   Windows ends when idle.
3. **Any other change of a running state is measured again** – whether it happened during the warm-up, a measured run or
   one of the two calibration runs (a late effect of the run before shows up there; a service such as VSS, which ends
   only after three minutes idle, can also hide a repeat in the next run). Once every such service is back in its
   previous state, every target runs three more times; after each run the services are watched for 20 seconds, longer
   than a measured run takes until it reads the services. Any deviation from the previous state, also a late one that
   is first seen before the next run, fails the proof, and so does a service that does not return to its previous
   state within eight minutes. Only a change that no target repeats in three clean runs each is listed as a change by
   Windows. `wuauserv` is never repeated or excused (V1); where it changes on its own in a calibration run, V1 is
   reported as not verifiable on that runner.

Every listed change stays in `Riegel-Collector-ReadOnlyProof-<version>.txt` with its rule, so a reviewer can check it.
What the rules cannot exclude: a change that the collector causes only sometimes can pass three clean repetitions –
one that it causes in every second run with a probability of one in eight per target, one in every third run with
about three in ten. A configuration change that appears neither in the warm-up nor in the measured run of a target but
only during its repetitions is listed and does not fail the proof, and an effect that appears only after the proof has
ended is not seen at all.

**Why rules 1 and 4 exist (diagnosis of 2026-10-21).** The collector workflow on `main` failed once on `windows-2025`
with `svc:swprv:State` and `svc:VSS:State` changing from `Stopped` to `Running` during a measured collector run, and on
2026-10-20 with `\Microsoft\Windows\Printing\PrintJobCleanupTask` changing from enabled to disabled during a `--deep`
run; the collector exe was the same bytes as in every passing run. A diagnosis workflow on a separate branch measured
this on six Server 2025 runners (image 20261018.275.1):

- **VSS and the Microsoft Software Shadow Copy Provider (swprv) start on every runner by themselves, 11 to 13 minutes
  after boot** (671, 673, 676, 687, 687 and 758 s), through a COM activation by the DCOM launcher (the Service Control
  Manager logs the DcomLaunch host as the requesting process). On three runners nothing ran at all apart from a loop
  reading the event log – no checkout, no build, no collector. VSS ends after three minutes idle (event 8224), swprv
  three minutes later. Earlier proofs on `windows-2025` that showed the same two services starting during a calibration
  run fit this pattern, and so does the failed run.
- **The collector never started them:** in 21 collector runs with VSS and swprv stopped beforehand, in `--minimal` and
  in every source of the collector run on its own (each WMI class of the collection plan, including BitLocker's
  `Win32_EncryptableVolume` with its four methods, `secedit /export`, `gpresult`, `HNetCfg.FwPolicy2` and the Windows
  Update history) neither service started. The collector queries no shadow-copy or volume class (`Win32_ShadowCopy`,
  `Win32_Volume`, `MSFT_Volume`) and calls no VSS interface.
- **`PrintJobCleanupTask` disables itself:** the Security log records event 4701 (task disabled) from `taskhostw.exe`,
  the host that runs the task, right after the task's first run about five minutes after boot. The collector does not
  use the Task Scheduler at all.
- **Windows changes the firewall rules on its own (2026-10-22, two more runners):** about 11 minutes after boot a
  Defender platform update replaces Defender's own rules (Firewall log: `MsMpEng.exe` as SYSTEM); 12 and 17 minutes
  after boot Windows updates packaged apps (Windows Security, Narrator, the Windows Subsystem for Linux, Feedback Hub)
  and the firewall service adds their rules itself (`svchost.exe` as `NT SERVICE\mpssvc`). The Defender platform update
  also adds new preferences (`EnablePackageInspection`, `PromptEvidenceCollection`), and BITS switched its start mode
  from *Manual* to *Automatic* and back during the same proof. The revised proof failed
  twice on the pull request because of the Windows Security rules before rule 1 knew this case. The collector reads
  the rules through `HNetCfg.FwPolicy2` and writes none.

**Controls (2026-10-22, Server 2025 runners, final version of the proof).** The proof was run against stand-ins in
place of `collect-builtin.cmd`. A stand-in that starts VSS on every call (`vssadmin list writers`, a COM activation like
the one Windows itself makes) **fails** the proof in the first repetition round – the proof before this revision passed
such a stand-in, because the warm-up had started VSS and the measured run saw it already running. A stand-in that starts
VSS only on its first call **passes**: no target repeats the start in three clean runs each, and the proof lists it as a
change by Windows (the limit described above). A stand-in that creates a scheduled task with `schtasks` **fails**. Four
proofs with the real collector and all five targets between 7 and 25 minutes after boot **passed**, with Windows'
own VSS start (680 and 681 s after boot) inside them. Before the proof became part of a release, the same rules were run
against simulated runners for every case above (background start, start on every run, start during the warm-up only, *Start
Pending*, a late start, a service that does not return, a known and an unknown task change, a known firewall change, a
rule written by the collector, a rule change together with another firewall setting and an unknown rule).
