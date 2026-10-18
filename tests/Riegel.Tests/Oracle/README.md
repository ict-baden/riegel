[Riegel](../../../README.md) › [Development](../../../docs/development.md) › Oracle

# Independent oracle

**Contents:** [Purpose](#purpose) · [Files](#files) · [How the research is encoded](#how-the-research-is-encoded) ·
[Provenance of every value](#provenance-of-every-value) · [Evidence keys: NotSet or not collected](#evidence-keys-notset-or-not-collected) ·
[Expectations](#expectations) · [The 'any' list](#the-any-list) · [Tests and the integration gate](#tests-and-the-integration-gate) ·
[Comparing a local recording](#comparing-a-local-recording) · [Who may change expectations](#who-may-change-expectations) ·
[Open points](#open-points)

## Purpose

An independent check of the engine and the rules (spec 8.4). The oracle describes one real machine whose facts are
known from measurements – the research PC of `research-technik.md` – once with an elevated token and once with a
restricted token, and states for each of the 153 rule IDs of catalogue `2026.10` (spec 2.1) which result a correct
engine with correct rules must give.

**Independence.** The oracle was written only from the spec (sections 2.1, 2.3, 2.4, 2.5, 2.7, 2.8, 7.2, 7.3, 8.4,
`docs/file-formats.md` for the contract encodings), `research-technik.md` (§2 test machine, §3 table, §3.1–3.3, §4.3
non-admin run; §1 only where it restates these measurements) and the candidate list in the appendix of
`research-regeln.md`. It did **not** read
`src/Riegel.Core/Catalogue`, `Evaluation` or `Facts`, `rules/*.json`, or any other work package's branch. The raw
probe outputs of the research (`raw/`, with real names) were not used either.

Citations in the files: **R** = `research-technik.md`, **S** = spec, **C** = candidate list (`research-regeln.md`
appendix). "R§3 Defender" means the Defender row of the §3 table.

## Files

| File | Content |
|---|---|
| `oracle-admin.snapshot.json` | `riegel.snapshot/1`, origin `synthetic`, host `ORACLE-PC`, elevated, `--deep` |
| `oracle-nonadmin.snapshot.json` | the same machine with a restricted token (`runas /trustlevel:0x20000`, R§3/R§4.3) |
| `expected-results.json` | per snapshot and rule ID: status, optional flags, one-line justification |
| `OracleTests.cs`, `OracleExpectations.cs` | schema and placeholder checks, the comparison, the integration gate |
| `Compare-LocalRecording.ps1` | read-only helper for the integration step |

Both snapshots are canonical (`RiegelJson` round trip without change), schema-valid and contain only placeholders:
SIDs `S-1-5-21-1000-2000-3000-<rid>`, account names `local-user-1001`/`local-user-1003`, exclusion paths
`C:\Platzhalter\…`, NetBT interface GUIDs `{00000000-0000-4000-8000-0000000000a1…a7}`. The built-in account names
(`Administrator`, `Guest`, `DefaultAccount`, `WDAGUtilityAccount`) are Windows names from R§3, not persons.

## How the research is encoded

The research printed a few values per area, not whole exports. A snapshot that declared the usual full plan coverage
would claim that every unlisted value is absent ("not set") – that would invent facts. The oracle therefore uses only
encodings of the contract (spec 2.4, 7.3, `docs/file-formats.md`) that claim exactly what R measured:

| Situation in R | Encoding | Evidence class (S2.4.1) |
|---|---|---|
| a value is reported (`RunAsPPL = 2`) | `ok` item for the key, the value in `values`, and a **`valueFilter`** naming only the values R reports for that key | Present |
| a value is reported as not set (`LmCompatibilityLevel <not set>`) | the name in `valueFilter`, absent from `values` | NotSet |
| any other value of that key | not in `valueFilter` → "a value outside the filter is not collected" (file-formats › Coverage) | Unavailable |
| R says the key is missing (`DNSClient <key missing>`, "all keys missing", "no policy keys") | `keyMissing` item plus a recursive, complete coverage entry for exactly that key | NotSet (key and everything below) |
| a key with reported values | non-recursive, complete coverage entry for exactly that key | as above |
| everything else, including all other keys under `SOFTWARE\Policies` and `CurrentVersion\Policies` | no coverage | Unavailable |
| plan items R does not report | explicit `notCollected` item with a short reason | Unavailable |
| a raw export R quotes only in excerpts (`secedit`) | `parsed:secedit` with the excerpts and **`truncated: true`** | Unavailable (a key missing there is never NotSet) |
| WMI classes with a few named rows (services, optional features, accounts) | only those rows; properties R does not give are left out | see the 'any' list (LIFE-03, APP-06) |

Two contract points this relies on are open as integration requests (see [Open points](#open-points)): a GUID absent
from `api:audit-policy` and a right absent from `api:lsa-rights` count as not collected, not as "no auditing" or
"nobody".

## Provenance of every value

Values are the same in both snapshots unless the last column says otherwise. "Placeholder" values carry no
information about the machine. "Derived" values follow from a measured value as stated.

**Header and machine**

| Field | Value | Source |
|---|---|---|
| `origin` | `synthetic` | task; S7.3 lets synthetic fixtures carry `parsed:*` |
| `collector` | `Riegel.Oracle` 1.0.0, CLR `4.0.30319.42000`, 64-bit process | R§4.3 option A (CLR 4.0.30319.42000 on this PC), R§3.3 (64-bit process on a 64-bit OS) |
| `collectedAtUtc` | `2026-10-18T08:00:00Z` (non-admin `08:05:00Z`) | research date 2026-10-18 (R header); time of day placeholder, before the fixed evaluation time 09:00Z |
| `isAdmin` | `true` / `false` | R§3 "Admin" = elevated vs restricted token |
| `options.deep` | `true` | R§3 LSA protection: the runtime protection level was measured (the `--deep` item, S7.4) |
| `machine.hostName` | `ORACLE-PC` | placeholder |
| `domainRole`, `productType` | 0, 1 | R§2 Domain (WORKGROUP, DomainRole 0), R§2 OS (ProductType 1) |
| `build`, `ubr` | 26200, 9711 | R§2 OS |
| `editionId`, `displayVersion` | `Professional`, `25H2` | R§2 Registry ProductName row |
| `installationType` | `Client` | derived: R§3 "OS / support status" reads InstallationType (Client/Server/Server Core); ProductType 1 is a client |
| `osLanguage` | `en-US` | R§2 OS |
| `lastBootUtc` | `2026-10-16T06:00:00Z` | derived: Wininit event 12 is written at boot and R§3 dates it 2026-10-16 (QFE newest also 2026-10-16); time placeholder |
| `architecture`, `machineGuid`, `dnsDomain` | left out | R gives "64-bit" only (not x64 vs Arm64), no MachineGuid, no DNS domain |

**Registry**

| Key | Content | Source |
|---|---|---|
| `Policies\Microsoft\Windows NT\DNSClient` | key missing | R§3.1, R§3 LLMNR |
| `Policies\Microsoft\Windows NT\Terminal Services` | key missing | R§3 RDP/NLA: "no policy" |
| `Policies\Microsoft\Windows NT\Printers` | key missing | R§3 Print Spooler: "policy keys missing" (RegisterSpoolerRemoteRpcEndPoint, PointAndPrint) |
| `Policies\Microsoft\Windows\PowerShell\{ScriptBlockLogging,ModuleLogging,Transcription}`, `Policies\Microsoft\PowerShellCore\{…}` | keys missing | R§3 PowerShell logging: "all keys missing" |
| `Policies\Microsoft\Windows\RemovableStorageDevices` | key missing | R§3 USB storage: "no policy keys" |
| `Policies\Microsoft\Windows Defender\SmartScreen` | key missing | R§3 SmartScreen: "no policy" |
| `Policies\Microsoft Services\AdmPwd`, `SOFTWARE\Microsoft\Policies\LAPS`, `CurrentVersion\Policies\LAPS` | keys missing | R§3 LAPS: CSP/GPO/legacy missing |
| `CurrentVersion\LAPS\Config` | exists, no values | R§3 LAPS: "exists, empty" |
| `Policies\Microsoft\Windows\System` | `EnableSmartScreen` not set | R§3 SmartScreen |
| `Policies\Microsoft\Windows\Explorer` | `NoAutoplayfornonVolume` not set | R§3 AutoRun: "none set" |
| `Policies\Microsoft\Windows\DeviceGuard` | `EnableVirtualizationBasedSecurity` = 0 | R§3.1, R§3.3 |
| `CurrentVersion\Policies\System` | `EnableLUA` 1, `ConsentPromptBehaviorAdmin` 5, `PromptOnSecureDesktop` 1, `LocalAccountTokenFilterPolicy` 1 | R§3 UAC |
| `CurrentVersion\Policies\Explorer` | exists; `NoDriveTypeAutoRun`, `NoAutorun` not set | R§3 AutoRun "none set"; key exists per R§3.2 (Registry.pol entry for this key) |
| `Windows NT\CurrentVersion` | `CurrentBuild` 26200, `UBR` 9711, `ProductName` "Windows 10 Pro", `DisplayVersion` 25H2, `EditionID` Professional, `CompositionEditionID` Enterprise, `InstallationType` Client | R§2 (InstallationType derived as above) |
| `.NETFramework\v4.0.30319`, `v2.0.50727` (64- and 32-bit view) | `SchUseStrongCrypto`, `SystemDefaultTlsVersions` not set | R§3 TLS: ".NET values not set" (read as: in all four plan keys) |
| `Control\Lsa` | `RunAsPPL` 2; `LmCompatibilityLevel` not set | R§3.1, R§3 NTLM/LM, R§3 LSA protection |
| `Control\Lsa\MSV1_0` | `NTLMMinClientSec` and `NTLMMinServerSec` 536870912; `RestrictSendingNTLMTraffic` not set | R§3 NTLM/LM |
| `SecurityProviders\WDigest` | `UseLogonCredential` not set | R§3 WDigest |
| `SecurityProviders\SCHANNEL\Protocols` (recursive) | only `TLS 1.2\Client` with `Enabled` 1, `DisabledByDefault` 0 | R§3 TLS: "only TLS 1.2\Client …; everything else = OS default" |
| `Control\Terminal Server` | `fDenyTSConnections` 0 | R§3 RDP/NLA |
| `…\WinStations\RDP-Tcp` | `UserAuthentication` 0, `SecurityLayer` 2, `PortNumber` 3389 | R§3 RDP/NLA |
| `Control\SecureBoot\State` | `UEFISecureBootEnabled` 1 | R§3 Secure Boot |
| `Control\CI\Config`, `Control\CI\Policy` | `VulnerableDriverBlocklistEnable` 0, `VerifiedAndReputablePolicyState` 0 | R§3 Code integrity |
| `Services\LanmanServer\Parameters` | `RequireSecuritySignature` not set | R§1.4/§3.3, R§3.2 Default row |
| `Services\USBSTOR` | `Start` 3 | R§3 USB storage |
| `Services\NetBT\Parameters`, `…\Interfaces` | 7 interface keys, each `NetbiosOptions` 0 (GUIDs placeholder) | R§3 NetBIOS |
| `FirewallPolicy\{Domain,Standard,Public}Profile` | `DefaultInboundAction` not set | R§3.2: the persistent store shows `DefaultInboundAction NotConfigured` (read as: all three profiles) |

**WMI, API, events, raw data**

| Item | Content | Source |
|---|---|---|
| `Win32_ComputerSystem` | Name `ORACLE-PC`, DomainRole 0, PartOfDomain false, Domain WORKGROUP | R§2 Domain (`dsregcmd`: DomainJoined NO) |
| `Win32_OperatingSystem` | Caption "Microsoft Windows 11 Pro", Version 10.0.26200, BuildNumber 26200, OSArchitecture 64-bit, ProductType 1, SKU 48, LastBootUpTime as above | R§2 OS |
| `Win32_Service` | Spooler Running/Auto, RemoteRegistry Stopped/Disabled, WinRM Running/Auto, TermService Running/Manual, sshd Running/Auto, AppIDSvc Stopped (start mode not given) – 6 of 310 rows | R§3 Services, R§3 Code integrity |
| `Win32_OptionalFeature` | SMB1Protocol, -Client, -Server InstallState 2; no PowerShell 2.0 rows | R§3 Optional features |
| `Win32_UserAccount` | Administrator (-500), Guest (-501), DefaultAccount (-503), WDAGUtilityAccount (-504) disabled; -1001 and -1003 without a Disabled value | R§3 Guest/local accounts, R§3 Local administrators |
| `Win32_NetworkAdapterConfiguration` | 2 rows, TcpipNetbiosOptions 0 (SettingID placeholder) | R§3 NetBIOS |
| `MSFT_MpComputerStatus` | AMRunningMode Normal, RealTimeProtectionEnabled true, IsTamperProtected true, TamperProtectionSource UI, AntivirusSignatureAge 0, AntivirusSignatureLastUpdated 2026-10-18T06:00:00Z, AMProductVersion 4.18.26090.9 | R§3 Defender; AMRunningMode **derived** from S8.4 ("no ASR rules with Defender active"; "active" is AMRunningMode Normal per S2.5); signature date from R§3 Hotfixes (newest Defender signature 2026-10-18) and "0 days old", time placeholder |
| `MSFT_MpPreference` | ASR Ids/Actions null, PUAProtection 1, EnableNetworkProtection 0, EnableControlledFolderAccess 0, ExclusionPath two placeholder paths (**admin only**) | R§3 Defender ("no ASR rules", R§3.3 the value is `$null`; "2 exclusion paths") |
| `SecurityCenter2:AntivirusProduct` | Windows Defender, productState 397568 | R§3 Third-party AV |
| `Win32_DeviceGuard` | VBS status 2, configured [2], running [2, 7] | R§3 VBS/HVCI/Credential Guard |
| `Win32_EncryptableVolume` | C: and D: – ProtectionStatus 0, ConversionStatus 0, EncryptionMethod 0, no key protectors; VolumeType 0 on C: (**admin only**) | R§3 BitLocker ("fully decrypted, 0 protectors"); VolumeType 0 on C: is a representative choice – both volumes are unprotected, so no expectation depends on which one holds the OS |
| `Win32_Tpm` | enabled, activated, owned, SpecVersion "2.0, 0, 1.38" (**admin only**) | R§3 TPM |
| SMB `GetConfiguration` server / client | server EnableSMB1Protocol false, RequireSecuritySignature true, EncryptData false; client RequireSecuritySignature true, EnableInsecureGuestLogons false | R§3 SMB |
| `api:netusermodals` | minPwLen 0, maxPwAgeDays 42, pwHistLen 0, lockout 10 / 10 min / 10 min (minPwAgeDays not given) | R§3 Account policy |
| `api:lsa-rights` | SeDebugPrivilege [-544], SeRemoteInteractiveLogonRight [-544, -555], SeDenyNetworkLogonRight [machine -501], SeTcbPrivilege [] (**admin only**) | R§3 User rights |
| `api:localgroup:S-1-5-32-544` | -500 Administrator, -1001, -1003 | R§3 Local administrators |
| `api:audit-policy` | only Logon `{0CCE9215-69AE-11D9-BED3-505054503030}` = 3 (**admin only**) | R§3 Audit policy (R prints the GUID prefix; the full form is the Windows subcategory GUID, also used by the scaffold fixtures) |
| `api:lsass-protection` | protectionByte 65 (0x41), type light (PPL), signer 4 (**admin only**) | R§3 LSA protection ("0x41 = PPL, signer 4 (Lsa)") |
| `evt:system:wininit-12` | found, eventData ["4"], 2026-10-16 after boot | R§3 LSA protection ("started as a protected process with level: 4", 2026-10-16); times placeholder |
| `api:firewall` | current profile 2 (private); all profiles enabled, inbound block; `rulesCollected: false` | R§3 Firewall; the 751 rules are not reproduced. `"block"` follows the encoding of the scaffold fixtures |
| `parsed:secedit` | PasswordComplexity 0, EnableGuestAccount 0, `Lsa\FullPrivilegeAuditing` = 3,0, SeDenyNetworkLogonRight = `Guest` (unresolved name); `truncated` (**admin only**) | R§3 secedit row, R§3.1, R§3.3 Localised names |
| `parsed:gpresult` | only "Local Group Policy" (LocalGPO), no registry or security entries (**admin only**) | R§3.2 Domain GPO / Security policy rows |

**Non-admin differences** (R§1.2, R§3 "Without admin?", R§4.3): `api:lsa-rights` denied (5), `api:audit-policy` error
(1314 = 0x522), `raw:secedit-inf` denied (740), `raw:gpresult-xml` denied, `api:lsass-protection` denied (5),
`Win32_EncryptableVolume` and `Win32_Tpm` denied, no `parsed:*` items, no Exclusion properties in `MSFT_MpPreference`.
Everything else is byte-identical (a test checks this).

`durationMs` uses the times R gives (firewall 47 ms, Device Guard 23 ms, …), otherwise 0; no rule reads it.

## Evidence keys: NotSet or not collected

| Evidence | Decision | Reason |
|---|---|---|
| keys R calls missing (table above) | **KeyMissing inside coverage** → NotSet | R states the key is missing; recursive coverage of exactly that key |
| values R calls "not set" | **NotSet** via `valueFilter` | R states the value is absent in an existing key |
| other values in a reported key | **not collected** (outside `valueFilter`) | R does not list the key's other values |
| `SOFTWARE\Policies`, `CurrentVersion\Policies` (everything not listed above), `PolicyManager\current\device` | **not collected** (no coverage; PolicyManager as item) | R measured 80 policy values but did not list them; R§3.2: MDM holds only "knobs" |
| `SCHANNEL\Ciphers`, `SCHANNEL\Hashes` | **not collected** (no coverage) | R§3 TLS measured only `Protocols` and the .NET values |
| `Winlogon`, `Session Manager(\kernel)`, `EventLog\*`, `Tcpip(6)\Parameters`, `LanmanWorkstation\Parameters`, `mrxsmb10`, `ProductOptions`, `Cryptography`, `Uninstall`, Office keys, `Windows Defender`, `Control\DeviceGuard` | **not collected** (explicit items) | not reported in R§2/§3 |
| `Win32_QuickFixEngineering` | **not collected** | R: 7 entries, only the newest date; the list cannot be reproduced |
| `Win32_NetworkAdapter`, `MSNdis_PhysicalMediumType`, `Win32_Share`, `api:netuserenum`, `api:localgroup:S-1-5-32-555`, `api:winhttp-proxy`, `api:wsc-av`, `api:windows-update-history` | **not collected** | not measured in R (NetUserEnum is V7) |
| audit subcategories other than Logon | **not collected** (GUID absent) | R gives only counts (47/8/5) and the Logon value |
| user rights other than the four in R | **not collected** (right absent) | R gives four rights |
| `raw:secedit-inf`, `raw:gpresult-xml`, `raw:registry-pol` (admin) | **not collected** | raw text/bytes not reproduced; the parsed excerpts carry what R reports |
| service, feature and account rows not listed | absent rows | R lists only these rows; rules that look for other rows are in the 'any' list |

## Expectations

`expected-results.json` has, per snapshot, every rule ID of spec 2.1 (exactly 153) with:

- `status`: `pass`, `fail`, `notApplicable`, `notCheckable`, `info` or `any`;
- `allowed` (only with `any`): the statuses that remain possible, e.g. "fail or notCheckable, never pass";
- `flags` / `absentFlags`: flags that must / must not be set when the status matches (e.g. `fromDefault` must not be
  set when a measured value decides; `configuredOnly` never on CRED-06/08/09; `heuristic` never on DEF-18);
- `researchGap: true`: `notCheckable` only because R does not give the value – a local recording will decide it;
- `why`: a one-line justification citing R, S or C.

`globalAbsentFlags` (`staleSnapshot`, `clockSuspect`) must be absent on every result: the snapshot is collected on
the evaluation day, after the last boot and after the release of 26200.9711.

Counts (admin / non-admin): pass 26/23, fail 25/24, notApplicable 11/11, info 5/4, notCheckable 61/67, any 25/24.
The integration step (2026-10-18) changed NET-29, FW-01, FW-04, FW-07, FW-10, AUD-30, RDP-08 and TLS-01 after the
first run against the real engine and catalogue; each entry's `why` starts with "Integration 2026-10-18" and gives the
reason (rule decisions of rules-a/b/c with their sources, and the partial service list of this fixture).
Most `notCheckable` results are research gaps: the research PC was measured for feasibility, not for every rule.
The decisive part of the oracle is the set of fixed pass/fail/notApplicable/info expectations and every
`notCheckable` that a sloppy engine would turn into a pass or a fail (values outside the filter, truncated secedit,
denied items without admin).

All expectations from spec 8.4 are included: RDP-01 fail (RDP on, NLA off), CRED-18 fail, NET-08 pass from the
runtime (no `fromDefault`), CRED-07 fail and CRED-08 pass, CRED-06 pass (as admin from the runtime level, without
admin from Wininit event 12), ASR rules fail with Defender active (DEF-09, ASR-01/02/08/09/13/15; ASR-05 is
`notCheckable` because it applies only where Office is detected and R has no Office data), ENC-01 fail, ACCT-01 fail,
ACCT-14 notApplicable.

## The 'any' list

25 of 153 (16.3 %) for admin, 24 of 153 (15.7 %) for non-admin – below the 20 % limit (a test enforces it).

| Rule | Allowed | Why neither R nor S pins it |
|---|---|---|
| LIFE-03 | pass, fail, notCheckable | no Windows Update policy or `wuauserv` data; the service list is partial |
| LIFE-05 | pass, notCheckable | R gives "64-bit" but not x64 vs Arm64; depends on whether the rule reads OSArchitecture or `os.arch` |
| NET-04 | fail, notCheckable | NetBIOS not off on any adapter, but NodeType (P-node alternative) not measured |
| NET-23 | fail, notCheckable | policy not set; the non-policy Remote Assistance value is outside the plan |
| CRED-02 | fail, notCheckable | LmCompatibilityLevel not set; R does not give the OS default |
| CRED-05 | pass, fail, notCheckable | WDigest not set; pass only if the rule cites the default |
| ACCT-06 | pass, fail | threshold 10; the verified limit is the rule author's (S2.2) |
| DEF-17 | pass, fail, notCheckable | no SmartScreen policy and no SmartScreenEnabled value; default vs policy required |
| APP-01 | fail, notCheckable, info | AppLocker empty, SAC off; CiTool data is outside the plan; may ship as inventory |
| APP-06 | info, notCheckable | inventory on 6 of 310 services and no Uninstall data |
| ENC-02 (admin) | fail, notCheckable, notApplicable | method of an unencrypted volume |
| SVC-02 | pass, fail, notCheckable | Point and Print policy missing; default vs policy required |
| SVC-04 | pass, info | check or inventory (S2.1 names it as an inventory candidate) |
| PS-01 | pass, notApplicable | PowerShell 2.0 absent: R§3.3 "not present" vs C "n/a on 24H2+" |
| RDP-02, RDP-04, RDP-06 | fail, notCheckable | policy missing, the RDP-Tcp value not measured |
| MED-05 | fail, info | USBSTOR 3 fails as a check; C lists no source, so it may ship as inventory |
| TLS-02, TLS-05, TLS-09, TLS-10 | pass, fail, notCheckable | values not set; R§3.3 per-build SCHANNEL defaults are open |
| AUD-30 | fail, notCheckable | the rule also needs the W32Time service row, which the partial service list lacks (integration) |
| TLS-03, TLS-04 | fail, notCheckable | TLS 1.0/1.1 not set; R§3.3: still on in in-market builds, first build with them off unverified |

## Tests and the integration gate

`OracleTests` (both target frameworks, Linux too):

- the snapshots are schema-valid (`SchemaValidator`, the scaffold's schema test helper), readable through
  `ISnapshotReader` and canonical; the non-admin snapshot differs from the admin one only in the denied items;
- `expected-results.json` covers exactly the 153 IDs of spec 2.1 for both snapshots, each with a justification, with
  valid statuses and flags, and less than 20 % `any`; the spec 8.4 examples are present;
- no SID other than the placeholders, no other account names, no user-profile paths;
- `Compare-LocalRecording.ps1` calls only `check … --format json --no-store` and contains no writing or
  configuration-changing command;
- the comparison logic finds every kind of difference (tested with constructed results);
- **`Admin_…` / `Nichtadmin_Export_ergibt_die_erwarteten_Ergebnisse`**: read each snapshot through
  `ISnapshotReader`, evaluate with `RiegelServices.CreateDefault(new NullPlatform())`, the embedded catalogue and
  `EvaluationContext` 2026-10-18 (09:00Z, 35/90 days, no ESU), and compare status and flags per rule.

The last two are skipped with "Oracle wartet auf Katalog und Engine (Integrationsgate)" while the evaluator is the
stub (every result `notCheckable`) or a rule ID of spec 2.1 is missing from the catalogue. **The integration step
removes the skip** by setting `OracleGate.Entfernt = true` in `OracleExpectations.cs`; from then on a stub or a missing
rule fails the tests instead of skipping them.

## Comparing a local recording

The integration step also records this PC with the real collector (read-only, admin and `runas /trustlevel:0x20000`,
spec 8.4) and keeps the recording **outside the repository**. Then:

```powershell
dotnet build Riegel.sln -c Release
pwsh -File tests/Riegel.Tests/Oracle/Compare-LocalRecording.ps1 -SnapshotPath <scratchpad>\<HOST>_<time>.snapshot.json
```

The script refuses a path inside the repository, picks the expectation set from `isAdmin` (or `-Role`), runs
`Riegel.Cli.exe check --snapshot <path> --format json --no-store` once and prints, per rule, the expected and actual
status, the kind of difference and the justification. It collects nothing, writes no file and changes nothing
(the CLI itself only writes its usual log under the Riegel data folder). Kinds: *Widerspruch* (a fixed expectation
differs), *any außerhalb*, *Flag*, *fehlt*, and *Lücke entschieden* – a `researchGap` rule that the full recording now
decides; that is information, not an error. Exit code 0 = no blocking difference, 1 = differences, 3 = could not run.
The CLI evaluates with today's date, so LIFE-01/LIFE-02 may differ from the 2026-10-18 expectations later. The script
reads the `riegel.cli.check/1` wrapper of the command line (the run is `machines[0].run`) and parses JSON with
`-AsHashtable`, because real snapshots contain registry default values named `""`.

**Result of the integration step (2026-10-18)**, collector 0.1.0 on the research workstation, admin and
`runas /trustlevel:0x20000`; the recordings stayed in the scratchpad. Apart from research gaps that the full recording
decides (61 as admin, 40 without admin; information only), there was **one** difference in both roles: **AUD-30**
passed, where the oracle allows fail or not checkable. The cause is the oracle snapshot, not the engine: its
`Win32_Service` list is deliberately partial and lacks W32Time, so on the snapshot the rule fails on the missing
service; the real machine has the service and a valid time source, so pass is correct there.

## Who may change expectations

Only the **oracle author** or the **integration reviewer** may change `expected-results.json` or the snapshots, and
every change states its reason in the commit message (which measurement or spec rule makes the old expectation wrong).
A failing oracle test is first a question to the engine or the rule – the expectation is changed only when the
research or the spec says otherwise, never to make a test green.

## Open points

- **Integration requests (contract, `docs/file-formats.md`):** state that a subcategory GUID absent from
  `api:audit-policy` and a right absent from `api:lsa-rights` are *not collected* (Unavailable), not "no auditing" or
  "nobody"; state that absent WMI properties in a row are Unavailable. The oracle relies on this.
- **Encodings fixed elsewhere:** `api:firewall` `defaultInbound` uses `"block"` (as the scaffold fixtures);
  `api:lsass-protection` `signer` is the number 4; `evt:system:wininit-12` `eventData` is `["4"]`. If the collector
  package fixes other encodings, the snapshots follow (oracle author or reviewer).
- **Derived values:** `installationType` Client (from ProductType 1), AMRunningMode Normal (from S8.4 "Defender
  active"), `lastBootUtc` and the other times of day (dates from R, times placeholders), VolumeType 0 on C: (no
  expectation depends on it). Each is explained above.
- **Unverified here:** the full research export and the raw probe files were not used; a local recording with the
  real collector will turn many `researchGap` entries into decided results – that comparison is the integration step.
