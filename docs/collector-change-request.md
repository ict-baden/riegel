[Riegel](../README.md) › [Collector](collector.md) › Change-request pack

# Change-request pack for the portable collector

**Contents:** [Intended use](#intended-use-and-non-use) · [Every item read](#every-item-read) ·
[Processes started](#every-process-started) · [Temporary files](#temporary-files) ·
[Traces Windows records](#traces-windows-itself-records) · [Footprint](#cpu-time-and-size-footprint) ·
[Identity of the program](#sha-256-and-applocker-data) · [Read-only proof](#read-only-proof)

Plants that run validated or change-managed machines usually need a change request before any program may run there.
This page collects what such a request asks for. Use the values of the **tag release** you deploy (never `latest`),
and attach that release's `Riegel-Collector-ReadOnlyProof-<version>.txt` and `Riegel-Collector-allowlist-<version>.zip`.

## Intended use and non-use

- **Use:** read the security configuration of one Windows machine once, on request, and write it to one file for an
  evaluation elsewhere (hardening evidence, periodic review, audit, insurer questionnaire). Started by a person,
  ideally from a command prompt with *Run as administrator*; it ends by itself within seconds to a minute.
- **Non-use:** it is not an agent, service, scheduled task or monitoring tool; it does not change, repair or harden
  anything; it opens no network connection and sends nothing anywhere; it does not read files of users or of the
  plant software, no event logs other than the one Wininit event below, and no passwords (values with secret-like
  names are redacted before writing). It is not intended to monitor the behaviour or performance of employees.

## Every item read

The list below is generated from the collection plan embedded in the exe (`contracts/collection-plan.json`); a test
keeps it in sync. Nothing outside this list is read.

<!-- plan-items:start (generated from contracts/collection-plan.json by CollectorDocsTests; do not edit) -->
Collection plan `riegel.collection-plan/1` version 1. Limits: at most 50,000 registry values in total, recursion depth 8, binary values up to 4,096 bytes, tool timeout 60 s, raw texts up to 4 MB, the newest 100 Windows Update history entries.

**Registry (read through the 64-bit view, `KEY_READ` only)**

| Key | Scope | Values | Admin |
|---|---|---|---|
| `HKLM\SOFTWARE\Policies` | key and subkeys, depth 8 | all | no |
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies` | key and subkeys, depth 8 | all | no |
| `HKLM\SOFTWARE\Microsoft\PolicyManager\current\device` | key and subkeys, depth 8 | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Control\Lsa` | key and subkeys, depth 8 | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders` | key and subkeys, depth 8 | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server` | key and subkeys, depth 8 | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard` | key and subkeys, depth 8 | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Services\NetBT\Parameters` | key and subkeys, depth 8 | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy` | key and subkeys, depth 8 | all | no |
| `HKLM\SOFTWARE\Microsoft\Windows Defender` | key and subkeys, depth 8 | all | no |
| `HKLM\SOFTWARE\Microsoft\Policies\LAPS` | key and subkeys, depth 8 | all | no |
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\LAPS` | key and subkeys, depth 8 | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Control\CI` | key and subkeys, depth 8 | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters` | this key only | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters` | this key only | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Services\mrxsmb10` | this key only | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Services\USBSTOR` | this key only | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters` | this key only | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters` | this key only | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Services\EventLog\Security` | this key only | all | yes |
| `HKLM\SYSTEM\CurrentControlSet\Services\EventLog\System` | this key only | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Services\EventLog\Application` | this key only | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State` | this key only | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager` | this key only | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel` | this key only | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Control\ProductOptions` | this key only | all | no |
| `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion` | this key only | `CurrentBuild`, `CurrentBuildNumber`, `CurrentMajorVersionNumber`, `CurrentMinorVersionNumber`, `CurrentVersion`, `UBR`, `EditionID`, `InstallationType`, `DisplayVersion`, `ReleaseId`, `ProductName`, `CSDVersion` | no |
| `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon` | this key only | all | no |
| `HKLM\SOFTWARE\Microsoft\.NETFramework\v4.0.30319` | this key only | all | no |
| `HKLM\SOFTWARE\Microsoft\.NETFramework\v2.0.50727` | this key only | all | no |
| `HKLM\SOFTWARE\WOW6432Node\Microsoft\.NETFramework\v4.0.30319` | this key only | all | no |
| `HKLM\SOFTWARE\WOW6432Node\Microsoft\.NETFramework\v2.0.50727` | this key only | all | no |
| `HKLM\SOFTWARE\Microsoft\Cryptography` | this key only | `MachineGuid` | no |
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` | key and one level of subkeys | `DisplayName`, `DisplayVersion`, `Publisher` | no |
| `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall` | key and one level of subkeys | `DisplayName`, `DisplayVersion`, `Publisher` | no |
| `HKLM\SOFTWARE\Microsoft\Office\ClickToRun\Configuration` | this key only | all | no |
| `HKLM\SOFTWARE\Microsoft\Office\16.0\Common\InstallRoot` | this key only | all | no |
| `HKLM\SOFTWARE\WOW6432Node\Microsoft\Office\16.0\Common\InstallRoot` | this key only | all | no |
| `HKLM\SOFTWARE\Microsoft\Office\15.0\Common\InstallRoot` | this key only | all | no |
| `HKLM\SOFTWARE\WOW6432Node\Microsoft\Office\15.0\Common\InstallRoot` | this key only | all | no |
| `HKLM\SOFTWARE\Microsoft\Office\14.0\Common\InstallRoot` | this key only | all | no |
| `HKLM\SOFTWARE\WOW6432Node\Microsoft\Office\14.0\Common\InstallRoot` | this key only | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Services\W32Time\Parameters` | this key only | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Services\W32Time\TimeProviders\NtpClient` | this key only | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Policies\Microsoft\FVE` | this key only | all | no |
| `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` | this key only | `PROCESSOR_ARCHITECTURE` | no |
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\WinHttp` | this key only | `DisableWpad` | no |
| `HKLM\SYSTEM\CurrentControlSet\Services\RemoteRegistry` | this key only | `Start` | no |
| `HKLM\SYSTEM\CurrentControlSet\Services\Spooler` | this key only | `Start` | no |
| `HKLM\SYSTEM\CurrentControlSet\Services\W32Time` | this key only | `Start` | no |
| `HKLM\SYSTEM\CurrentControlSet\Services\wuauserv` | this key only | `Start` | no |

**WMI (queries and the allowed read-only methods)**

| Namespace and class | Filter | Properties | Methods | Admin |
|---|---|---|---|---|
| `root\cimv2:Win32_ComputerSystem` | – | Name, DomainRole, PartOfDomain, Domain, Workgroup, PCSystemType, PCSystemTypeEx, Manufacturer, Model | – | no |
| `root\cimv2:Win32_OperatingSystem` | – | Caption, Version, BuildNumber, OSArchitecture, ProductType, OperatingSystemSKU, OSLanguage, LastBootUpTime, InstallDate | – | no |
| `root\cimv2:Win32_Service` | – | Name, DisplayName, State, StartMode, StartName, PathName | – | no |
| `root\cimv2:Win32_OptionalFeature` | – | Name, InstallState | – | no |
| `root\cimv2:Win32_QuickFixEngineering` | – | HotFixID, InstalledOn | – | no |
| `root\cimv2:Win32_UserAccount` | `LocalAccount=TRUE` | Name, SID, Disabled, Lockout, PasswordRequired, PasswordExpires | – | no |
| `root\cimv2:Win32_NetworkAdapterConfiguration` | `IPEnabled=TRUE` | SettingID, TcpipNetbiosOptions, DefaultIPGateway, DHCPEnabled | – | no |
| `root\cimv2:Win32_NetworkAdapter` | `PhysicalAdapter=TRUE` | Name, NetConnectionStatus, AdapterTypeID, GUID, NetEnabled | – | no |
| `root\wmi:MSNdis_PhysicalMediumType` | – | InstanceName, NdisPhysicalMediumType, Active | – | no |
| `root\cimv2:Win32_Share` | – | Name, Path, Type | `GetSecurityDescriptor` (on `Win32_LogicalShareSecuritySetting`) | no |
| `root\Microsoft\Windows\Defender:MSFT_MpComputerStatus` | – | AMRunningMode, AMServiceEnabled, AntivirusEnabled, RealTimeProtectionEnabled, BehaviorMonitorEnabled, IoavProtectionEnabled, IsTamperProtected, TamperProtectionSource, AntivirusSignatureLastUpdated, AntivirusSignatureAge, AMProductVersion | – | no |
| `root\Microsoft\Windows\Defender:MSFT_MpPreference` | – | AttackSurfaceReductionRules_Ids, AttackSurfaceReductionRules_Actions, MAPSReporting, SubmitSamplesConsent, PUAProtection, EnableNetworkProtection, EnableControlledFolderAccess, DisableRealtimeMonitoring, DisableBehaviorMonitoring, ExclusionPath, ExclusionExtension, ExclusionProcess, ExclusionIpAddress | – | no |
| `root\SecurityCenter2:AntivirusProduct` | – | displayName, productState, timestamp | – | no |
| `root\Microsoft\Windows\DeviceGuard:Win32_DeviceGuard` | – | VirtualizationBasedSecurityStatus, SecurityServicesConfigured, SecurityServicesRunning, CodeIntegrityPolicyEnforcementStatus, UsermodeCodeIntegrityPolicyEnforcementStatus | – | no |
| `root\cimv2\Security\MicrosoftVolumeEncryption:Win32_EncryptableVolume` | – | DriveLetter, VolumeType, ProtectionStatus, ConversionStatus, EncryptionMethod | `GetConversionStatus`, `GetEncryptionMethod`, `GetKeyProtectors`, `GetProtectionStatus` | yes |
| `root\cimv2\Security\MicrosoftTpm:Win32_Tpm` | – | IsEnabled_InitialValue, IsActivated_InitialValue, IsOwned_InitialValue, SpecVersion | – | yes |
| `root\Microsoft\Windows\SMB:MSFT_SmbServerConfiguration` | – | EnableSMB1Protocol, EnableSMB2Protocol, RequireSecuritySignature, EnableSecuritySignature, EncryptData, RejectUnencryptedAccess | `GetConfiguration` | no |
| `root\Microsoft\Windows\SMB:MSFT_SmbClientConfiguration` | – | RequireSecuritySignature, EnableSecuritySignature, EnableInsecureGuestLogons | `GetConfiguration` | no |

**Windows APIs**

| Item | Source | When | Admin |
|---|---|---|---|
| `api:netusermodals` | `NetUserModalsGet` levels 0 and 3 | always | no |
| `api:netuserenum` | `NetUserEnum` level 2 (names, SIDs, flags, last logon as a date only) | always | no |
| `api:lsa-rights` | `LsaOpenPolicy` + `LsaEnumerateAccountsWithUserRight` for 45 rights (SIDs only) | always | yes |
| `api:localgroup:S-1-5-32-544` | `NetLocalGroupGetMembers` of `S-1-5-32-544` (found by SID) | always | no |
| `api:localgroup:S-1-5-32-555` | `NetLocalGroupGetMembers` of `S-1-5-32-555` (found by SID) | always | no |
| `api:audit-policy` | `AuditQuerySystemPolicy` (after enabling `SeSecurityPrivilege` in the collector's own token) | always | yes |
| `api:lsass-protection` | `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `NtQueryInformationProcess` (protection level of LSASS) | only with `--deep` | yes |
| `api:firewall` | `HNetCfg.FwPolicy2` (profiles; the rule list not with `--minimal`) | always | no |
| `api:windows-update-history` | `Microsoft.Update.Session` `QueryHistory`, only if `wuauserv` is already running | not with `--minimal` | no |
| `api:winhttp-proxy` | `WinHttpGetDefaultProxyConfiguration` | always | no |
| `api:wsc-av` | Windows Security Center API `IWSCProductList` (Windows 8 and later clients) | always | no |

**Event log**

- `evt:system:wininit-12`: newest event 12 of `Microsoft-Windows-Wininit` in the System log after the last boot, `EvtQuery` with `*[System[Provider[@Name='Microsoft-Windows-Wininit'] and (EventID=12)]]`

**Raw data**

- `raw:secedit-inf`: `secedit.exe /export /cfg {out} /log {log} /quiet` in the collector's temporary folder; not with `--minimal`; up to 16 MB
- `raw:registry-pol`: the file `%SystemRoot%\System32\GroupPolicy\Machine\Registry.pol` (read, never changed); always; up to 16 MB
- `raw:gpresult-xml`: `gpresult.exe /scope computer /x {out} /f` in the collector's temporary folder; not with `--minimal`; up to 4 MB

Allowed WMI methods (all read-only): `GetConfiguration`, `GetConversionStatus`, `GetEncryptionMethod`, `GetKeyProtectors`, `GetProtectionStatus`, `GetSecurityDescriptor`.
<!-- plan-items:end -->

## Every process started

| Program | Exact arguments | When |
|---|---|---|
| `%SystemRoot%\System32\secedit.exe` | `/export /cfg <temp>\secedit.inf /log <temp>\secedit.log /quiet` | always, except with `--minimal` |
| `%SystemRoot%\System32\gpresult.exe` | `/scope computer /x <temp>\gpresult.xml /f` | always, except with `--minimal` |

`<temp>` is the collector's own temporary folder (below). Both run without a window, without the shell (so no UAC
prompt), with *below normal* priority and a 60-second timeout. A 32-bit process on 64-bit Windows uses `Sysnative`
instead of `System32`. **With `--minimal` the collector starts no process at all**; the read-only proof checks that.
No other program can be started: one class holds the only process start, with these two fixed argument lists, and a
source-scan test enforces it.

`collect-builtin.cmd` (the built-in-commands path) starts only `secedit`, `auditpol`, `reg`, `hostname`, `ver`, `find`
and, for the date, `wmic` or `powershell -Command Get-Date`; its exact commands are listed in
[collector.md](collector.md#built-in-commands).

## Temporary files

- `%TEMP%\riegel-<8 hex>\` – created fresh for each run (path at most 127 characters, as `gpresult` requires; if
  `%TEMP%` is too long, its 8.3 short form). It receives `secedit.inf`, `secedit.log` and `gpresult.xml` and is
  deleted with its content at the end of the run. The collector deletes nothing else, anywhere.
- The export `<HOST>_<yyyyMMddTHHmmssZ>.snapshot.json` (or `.json.gz`) and `<file>.sha256` in the chosen folder are
  the only files that remain.

## Traces Windows itself records

The collector changes no configuration. Windows records that a program ran:

- the CLR 4 usage log under `%LocalAppData%\Microsoft\CLR_v4.0\UsageLogs\`;
- Prefetch, AmCache/ShimCache and similar execution artefacts;
- event 4688 for the collector and its child processes if process-creation auditing is on;
- event 4799 (local group membership enumerated, Windows 10 / Server 2016 and later, *Audit Security Group
  Management*) and possibly 4798, and privilege-use events for `SeSecurityPrivilege` (audit-policy query);
- an on-demand start of the Group Policy Client service by Windows when `gpresult` calls it (where the service has a
  start trigger; its start mode does not change; not with `--minimal`);
- possibly RSoP data in temporary WMI namespaces built by `gpresult` (**unverified**).

The read-only proof lists, per runner, the Security-log event IDs one run created, every on-demand service start and
every demand-start service that Windows ended when idle during a run (start mode unchanged).

## CPU, time and size footprint

- Program: `Riegel.Collector.exe` about 125 KB plus a 400-byte `.exe.config`; no installation, no dependencies
  beyond the .NET Framework that ships with Windows.
- Priority *below normal*; one thread does the work; `secedit` and `gpresult` take one to five seconds each.
- Measured on a Windows 11 Pro 25H2 workstation (2026-10-18): 7–9 s elevated, about 13 s as a standard user; the
  export about 2.1 MB (about 195 KB with `--gzip`). The runner times are in the read-only proof.

## SHA-256 and AppLocker data

- `SHA256SUMS.txt` of the release (and `UEBERGABE.txt` when copied from the app) give the SHA-256 of every file; check
  on the target with `certutil -hashfile Riegel.Collector.exe SHA256`.
- `Riegel-Collector-allowlist-<version>.zip` contains the `Get-AppLockerFileInformation` output and an AppLocker hash-rule XML
  for this exact build (see [Allow-listing](collector.md#allow-listing-applocker)).
- The collector has its own version; the snapshot header names it and the SHA-256 of the exe that wrote it, so every
  export can be traced to the reviewed binary.

## Read-only proof

`Riegel-Collector-ReadOnlyProof-<version>.txt` of each release records, for this exact build on a disposable GitHub runner,
the before/after comparison of the security template (`secedit /export`), the audit policy (`auditpol /backup`), the
registry keys `HKLM\SOFTWARE\Policies`, `…\Control\Lsa`, `…\LanmanServer\Parameters` and `…\FirewallPolicy`, the
start mode and running state of every service (including `wuauserv`), the Defender preferences, the scheduled tasks
(path, name, state), the firewall profiles and rules, and the hash of `Registry.pol`; a calibration run without the
collector first; the Security-log event IDs and Defender detections during the run; and the result. Any difference
fails the release build. The workflow `collector.yml` runs the same proof on Windows Server 2022, Windows Server 2025
and Windows 11 Arm64 for the collector, `--deep`, `--minimal`, `Riegel.Cli.exe collect` and `collect-builtin.cmd`.
