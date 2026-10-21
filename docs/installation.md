[Riegel](../README.md) › Installation

# Installation

Which file goes onto which machine, how to check it, how to roll out the analysis app, and how to run checks as
scheduled tasks.

**Contents:** [Which package for which machine](#which-package-for-which-machine) · [Requirements](#requirements) ·
[Downloads: latest and tag releases](#downloads-latest-and-tag-releases) · [Verifying the download](#verifying-the-download) ·
[SmartScreen and signing](#smartscreen-and-signing) · [MSI installer](#msi-installer) ·
[Deploying with Intune or Group Policy](#deploying-with-intune-or-group-policy) · [Single exe and ZIP](#single-exe-and-zip) ·
[Command line for Windows and Linux](#command-line-for-windows-and-linux) · [Scheduled tasks](#scheduled-tasks) ·
[Updates and uninstalling](#updates-and-uninstalling) · [Supported versions](#supported-versions)

> [!IMPORTANT]
> **The MSI is not the way onto OT or validated machines.** Installing software on a plant, lab or control-room PC is
> a change in itself. On those machines run the **portable collector** (`Riegel-Collector.zip`, installs nothing) or
> the **built-in commands** (`collect-builtin.cmd`), and evaluate the snapshot on an analysis PC –
> see [Portable collector](collector.md).

> [!WARNING]
> **Version 0.1, not code-signed yet.** Until code signing is set up for ICT-Baden, Windows SmartScreen may warn and
> AppLocker can only allow the collector by its hash. Check every download against `SHA256SUMS.txt`. No customer pilot
> on OT or validated machines with an unsigned collector.

## Which package for which machine

| Machine | Package | Why |
|---|---|---|
| Admin, engineering and auditor PCs, IT-managed clients and servers (Windows 10 1809 / Server 2019 or later) | `Riegel-x64.msi` (Intune, GPO, SCCM), or `Riegel.exe` without installation | the analysis app: plant folders, registers, reports, and *Check this PC* |
| Plant, lab and control-room PCs, designed for Windows 7 SP1 to 11 and Server 2008 R2 to 2025 (tested so far on Server 2022, Server 2025 and Windows 11) | `Riegel-Collector.zip` (portable collector, net35) – **from a tag release** | reads the configuration, writes one snapshot, installs nothing and changes no configuration (Windows records that a program ran, see [What it leaves behind](collector.md#what-it-leaves-behind)) |
| Machines where no foreign program may run, and Windows XP / Vista / Server 2003 / 2008 (best effort, not tested on these) | `collect-builtin.cmd` (in the collector ZIP) or the same commands typed by hand | only Windows' own `secedit`, `auditpol` and `reg` |
| Scripts, scheduled checks and FAT pipelines on Windows | `riegel-win-x64.exe` (single file) or `Riegel.Cli.exe` from the MSI or ZIP | the console program with reliable exit codes |
| Auditors and CI on Linux | `riegel-linux-x64`, `riegel-linux-arm64` | evaluate snapshots, `verify` evidence packs, `compare-snapshot` – no collection |

## Requirements

| Part | Requirement |
|---|---|
| Analysis app and `Riegel.Cli.exe` | 64-bit Windows 10 version 1809 (build 17763) or Windows Server 2019 or later. No .NET for the MSI, `Riegel.exe` and `riegel-win-x64.exe` (they carry the .NET 10 runtime); the framework ZIP needs the .NET 10 Desktop Runtime. |
| Portable collector | .NET Framework 4.x (preinstalled from Windows 8 / Server 2012) or 3.5.1 (on by default on Windows 7 SP1). On Server 2008 R2 the 3.5.1 feature must be enabled through the Server Manager; whether the collector starts without it is **unverified** ([Limitations](limitations.md)). |
| Linux command line | x64 or arm64, a glibc-based distribution (not Alpine/musl). The .NET runtime is inside the file, but it takes three things from the system: **OpenSSL** (`libssl`, needed for every command except the help), **ICU** (`libicu`, for localised dates and numbers) and, for PDF documents, a **font** – see [Linux packages](#linux-packages). arm64 is cross-built; CI runs the x64 file in `debian:12-slim`. |
| Network | none. Riegel opens no network connection; plant folders may lie on a share. |

## Downloads: latest and tag releases

- **[latest](https://github.com/ict-baden/riegel/releases/tag/latest)** is the newest build of `main`, replaced in
  place on every push, with fixed file names (version `<base version>-ci.<run>`). Use it to try Riegel and on analysis PCs.
- **Tag releases** (`v0.1.2` …) keep their files. Their names carry the version, e.g. `Riegel-0.1.2-x64.msi`,
  `Riegel-Collector-0.1.2.zip`, `riegel-0.1.2-linux-x64` (`Riegel.exe` keeps its name).
- **OT customers use tag releases, never `latest`:** the collector's hash, its AppLocker data and its read-only proof
  belong to exactly that release, and a change request names that version. The collector has its own version
  (currently 0.1.0, SHA-256 in the release notes); release notes say "Collector unchanged since <tag>" when its bytes
  did not change.

| File | Content |
|---|---|
| `Riegel-x64.msi` | app (`Riegel.exe`), console program `Riegel.Cli.exe`, `collector\` (the collector bundle), licences; per machine |
| `Riegel.exe` | the app as one file, self-contained – it does **not** contain the collector (use the ZIP or the MSI for *Copy collector to a drive*) |
| `Riegel-win-x64-framework.zip` | app + `Riegel.Cli.exe` + `collector\`, needs the .NET 10 Desktop Runtime |
| `riegel-win-x64.exe` | console program as one file |
| `riegel-linux-x64`, `riegel-linux-arm64` | console program for Linux, one file |
| `Riegel-Collector.zip` | `Riegel.Collector.exe` (+ `.exe.config`), `collect-builtin.cmd`, `README.txt` (en/de, with the .NET pre-check), `LICENSE`, `THIRD-PARTY-NOTICES.txt`, inner `SHA256SUMS.txt` |
| `Riegel-Collector-allowlist.zip` | AppLocker file information and a hash-rule XML for the collector (generated on the CI runner, never applied), SHA-256, README |
| `Riegel-Collector-ReadOnlyProof.txt` | the read-only proof of this collector build ([Portable collector › Verified and not verified](collector.md#verified-and-not-verified)) |
| `Riegel-sbom.cdx.json` | bill of materials (CycloneDX) of the shipped files; the collector has no packages |
| `SHA256SUMS.txt` | SHA-256 of every file above, LF line endings |

## Verifying the download

```powershell
(Get-FileHash .\Riegel-Collector.zip -Algorithm SHA256).Hash.ToLower()
Select-String Riegel-Collector.zip .\SHA256SUMS.txt
```

All files in the current folder at once:

```powershell
Get-Content .\SHA256SUMS.txt | ForEach-Object {
    $hash, $name = $_ -split '\s+', 2
    if (Test-Path $name) { '{0}  {1}' -f $(if ((Get-FileHash $name -Algorithm SHA256).Hash -eq $hash) { 'OK  ' } else { 'FAIL' }), $name }
}
```

On Linux: `sha256sum -c SHA256SUMS.txt --ignore-missing`. The collector ZIP contains its own `SHA256SUMS.txt` for the
files inside; `UEBERGABE.txt`, written by *Copy collector to a drive*, lists the same hashes for the hand-over.

## SmartScreen and signing

Until releases are signed, `Get-AuthenticodeSignature .\Riegel-x64.msi` shows `NotSigned`, and SmartScreen may show
*Windows protected your PC* for `Riegel.exe`, the MSI or `riegel-win-x64.exe`: check the checksum, then *More info* ›
*Run anyway*. On machines with application control, allow the collector by its **hash** (data in
`Riegel-Collector-allowlist.zip`); once releases are signed, a publisher rule replaces the hash rule – signing changes
the bytes, so the hash rule then changes with every release.

## MSI installer

The MSI installs per machine (administrator rights needed):

- `C:\Program Files\Riegel` with `Riegel.exe`, `Riegel.Cli.exe`, `collector\`, `LICENSE` and `THIRD-PARTY-NOTICES.txt`;
- one Start menu entry *Riegel* (with the app's AppUserModelID, so a pinned entry and the running window group);
- the registry value `HKLM\SOFTWARE\ICT-Baden\Riegel\StartMenuShortcut` that Windows Installer needs for the Start menu
  entry;
- **no** service, **no** autostart, **no** scheduled task, no other change. The installer dialogs are German.

```powershell
# As administrator
msiexec /i Riegel-x64.msi                    # interactive
msiexec /i Riegel-x64.msi /qn                # silent
msiexec /i Riegel-x64.msi /qn /l*v msi.log   # silent with a log
msiexec /x Riegel-x64.msi /qn                # uninstall
```

The installer closes a running Riegel before it replaces files. CI installs and uninstalls every MSI on a disposable
runner and checks `Riegel.Cli.exe --version` and `collector\`.

## Deploying with Intune or Group Policy

- **Intune:** add `Riegel-x64.msi` as a *line-of-business app*, or wrap it with the *Microsoft Win32 Content Prep Tool*
  as a *Windows app (Win32)* with `msiexec /i Riegel-x64.msi /qn` and an MSI detection rule. Assign it to devices.
- **Group Policy:** put the MSI on a share the computer accounts can read and assign it under *Computer Configuration ›
  Policies › Software Settings › Software installation*.
- **Presets:** `%ProgramData%\ICT-Baden\Riegel\defaults.json` sets defaults for every user (language, plant folder,
  thresholds); the app shows them as *vorgegeben* (preset). See [Settings](settings.md).
- Every build replaces the previous one (major upgrade); there is no need to uninstall first.

Deploy the MSI to analysis PCs only – never to plant machines.

## Single exe and ZIP

`Riegel.exe` is the whole app in one file (runtime included); copy it anywhere and start it. It cannot copy the
collector to a drive, because the collector is not inside it – use `Riegel-Collector.zip` directly. The framework ZIP
contains the app, `Riegel.Cli.exe` and `collector\` and needs the .NET 10 Desktop Runtime. All variants keep their
settings and logs in `%LocalAppData%\Riegel` ([Data and privacy](data-and-privacy.md)).

## Command line for Windows and Linux

`Riegel.exe` is a window program: a console or PowerShell does not wait for it, and `$LASTEXITCODE` is not reliable.
In scripts use **`Riegel.Cli.exe`** (MSI, ZIP) or **`riegel-win-x64.exe`** (single file), on Linux `riegel-linux-x64`:

```bash
chmod +x riegel-linux-x64
./riegel-linux-x64 --version
./riegel-linux-x64 check --snapshot HMI-01_20261016T073000Z.snapshot.json --brief
./riegel-linux-x64 verify Riegel-Evidence_Linie-3_2026-10-18.zip
```

Every command, option and exit code: [Command line](command-line.md).

### Linux packages

Desktop and server installations usually have all of them. Minimal and container images (`debian:12-slim`,
`ubuntu:24.04`) do not:

| Distribution | Install |
|---|---|
| Debian 12 | `apt-get install --no-install-recommends libssl3 libicu72 fonts-dejavu-core` |
| Ubuntu 24.04 | `apt-get install --no-install-recommends libssl3t64 libicu74 fonts-dejavu-core` |
| Ubuntu 22.04 | `apt-get install --no-install-recommends libssl3 libicu70 fonts-dejavu-core` |
| Fedora, RHEL 9 and derivatives | `dnf install openssl-libs libicu dejavu-sans-fonts` |

For Chinese PDF documents add a TrueType CJK font, e.g. `fonts-wqy-microhei` ([Reports](reports.md)).

What happens without them:

- **No OpenSSL:** every command except the help stops with `RGL-0018` and exit code 3 (with `--brief` also the
  `RIEGEL UNKNOWN` line), naming the package to install.
- **No ICU:** Riegel runs without culture data and says so on stderr (`RGL-0017`): texts are still translated (the
  language comes from `LC_ALL`, `LC_MESSAGES` or `LANG`, or from `RIEGEL_LANGUAGE`), but dates and numbers are not
  localised. Install libicu for localised documents.
- **No font:** `report` with PDF stops with `RGL-0501` and exit code 3; CSV, Excel, JSON, `check` and `verify` work.

Riegel 0.1.0 aborted with exit code 134 and a .NET message when OpenSSL or ICU was missing.

## Scheduled tasks

A daily check as SYSTEM into a plant folder on the same machine (run in an elevated console):

```bat
schtasks /Create /TN "Riegel Haertung" /SC DAILY /ST 06:00 /RU SYSTEM /RL HIGHEST ^
  /TR "\"C:\Program Files\Riegel\Riegel.Cli.exe\" check --plant D:\Riegel\Linie3"
```

- As SYSTEM the collection is complete (administrator rights); *Last Run Result* shows the exit code (0 OK,
  1 warning, 2 critical, 3 error). The exit codes report **changes** (worse than before, newly open high findings,
  expiring exceptions, overdue measures), not the standing state.
- **SYSTEM on a share:** in a domain, a task running as SYSTEM authenticates as the computer account
  (`DOMAIN\HMI-01$`); grant the computer accounts (best through a group) *Modify* on `snapshots\`, `runs\` and
  `imports\` of the plant folder and *Read* on the rest.
- **Workgroup machines** have no computer account a file server could grant rights to, so "SYSTEM writes to the
  share" is not possible. Keep the plant folder on the machine and collect it later, run the task under an account the
  file server can authenticate (it must be a local administrator for a complete snapshot; Task Scheduler stores its
  password), or use the stick workflow with the portable collector and a collection record. Never make the plant share
  writable for *Everyone*.
- Machines older than Windows 10 1809 / Server 2019 cannot run `Riegel.Cli.exe`: there the portable collector runs
  as the task (`Riegel.Collector.exe --out <folder or share>`) and `import` on an analysis PC takes the exports in.

Details and the Icinga example: [Command line › Scheduled tasks](command-line.md#scheduled-tasks) and
[SYSTEM and a plant share](command-line.md#system-and-a-plant-share).

## Updates and uninstalling

- **MSI:** install the newer MSI over the old one; builds of `main` replace each other, and a release with a higher
  version replaces them. Installing an older version over a newer one is refused.
- **Exe, ZIP, collector:** replace the files. A new collector version is a new allow-list entry and a new change
  request on OT machines.
- **Uninstalling** removes the program files, the Start menu entry and the registry value. Plant folders are never
  touched; `%LocalAppData%\Riegel` (settings, logs) of each user stays – delete it to remove it.
- Riegel does not check for updates. Watch the [releases](https://github.com/ict-baden/riegel/releases) on GitHub.

## Supported versions

Security fixes are provided for the newest released version ([SECURITY.md](../SECURITY.md)); help is free on GitHub
and guaranteed under a support contract ([SUPPORT.md](../SUPPORT.md)).
