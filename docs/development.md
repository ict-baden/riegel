[Riegel](../README.md) › Development

# Development

**Contents:** [Requirements](#requirements) · [Build](#build) · [Test](#test) · [Never change this PC](#never-change-this-pc) ·
[Repository layout](#repository-layout) · [Work packages and ownership](#work-packages-and-ownership) ·
[Contracts and stubs](#contracts-and-stubs) · [Collector](#collector) · [Continuous integration](#continuous-integration) ·
[Rules for this repository](#rules-for-this-repository)

## Requirements

- .NET SDK 10.0.100 or later in the 10.0 band (`global.json`, `rollForward: latestFeature`).
- Windows 10 1809 / Server 2019 or later for the whole solution (WPF, Windows platform, net35 collector). The
  platform-neutral part (`Riegel.Core`, `Riegel.Commands`, the Linux CLI and most tests) builds and runs on Linux.
- The net35 collector needs no installed .NET Framework 3.5 to build: the reference assemblies
  (`Microsoft.NETFramework.ReferenceAssemblies.net35`) and the compiler (`Microsoft.Net.Compilers.Toolset`) come from
  NuGet. To *run* it locally, .NET Framework 4.x (always present on Windows 10/11) is enough.

## Build

```powershell
dotnet build Riegel.sln                                   # everything, Debug
dotnet build Riegel.sln -c Release
dotnet run --project src/Riegel.Cli -f net10.0 -- --version
dotnet run --project src/Riegel.Cli -f net10.0 -- check --snapshot tests/Riegel.Tests/Fixtures/synthetic/win11-24h2-workgroup-admin.snapshot.json --format json
dotnet run --project src/Riegel -- --demo-report out/demo  # writes every document from SampleData (placeholders until the packages land)
```

On Linux:

```bash
dotnet build tests/Riegel.Tests -f net10.0
dotnet run --project src/Riegel.Cli -f net10.0 -- --version
```

Release packages are built only by the CI (see below); `out/` and `dist/` are ignored by git.

## Test

```powershell
dotnet test Riegel.sln                          # both target frameworks; Windows-only tests run only on Windows
dotnet test tests/Riegel.Tests -f net10.0       # the platform-neutral part, as in the Linux CI job
```

The test project targets `net10.0-windows10.0.19041.0` (everything, incl. the WPF app, the Windows platform and the
collector exe) and `net10.0` (no `Windows/` folder). Tests that need Windows use `[WindowsFact]`.

The infrastructure tests in `tests/Riegel.Tests/Infrastructure` guard the repository as a whole: third-party notices
against the shipped packages, version pins, localisation (every resx key in five languages, collector message tables),
schemas and fixtures, source scans (SPDX header, forbidden APIs, collector references and size), catalogue
invariants, service reachability, the placeholder grep and the mandatory files.

Test support for every package is in `tests/Riegel.Tests/TestSupport`:

| Helper | Use |
|---|---|
| `SnapshotBuilder` | builds snapshots in code: presets (`Windows11Workgroup`, `Windows10Ltsc2019`, `Server2022Member`, `Server2016`, `Windows7`, `DomainController`), registry, WMI, API, raw and parsed items, item status, coverage (full plan coverage by default) |
| `RuleBuilder` | builds catalogue rules in code |
| `FixedClock` | a `TimeProvider` with a fixed time (default 2026-10-18 09:00 UTC) |
| `TempPlant`, `TempFolder` | a throw-away plant folder or directory, deleted on dispose |
| `SampleSets` | the `SampleData` documents, runs and registers as theory data and as JSON |
| `SchemaValidator` | validates JSON text against the schema in `schemas/` that its `schema` field names (or a given one) |
| `PdfDom`, `PdfLayout` | read text, fonts and positions from generated PDFs (from Zutritt) |
| `RepoPaths` | the repository root and its folders (also the fixtures) |

`Riegel.Core.Samples.SampleData` provides deterministic report data (plant "Musterwerk – Linie 3", five machines,
registers, runs) for the PDF, Excel, evidence and app packages.

Synthetic fixtures are in `tests/Riegel.Tests/Fixtures/synthetic` (see the README there). Recorded fixtures from real
machines are anonymised and owned by the collector package (`Fixtures/recorded`).

## Never change this PC

Development and tests are **read-only** towards the machine they run on. Never run, in a test, a script or by hand
while working on Riegel:

- `secedit /configure`, `auditpol /set`, LGPO imports, `gpupdate`;
- registry writes under `HKLM` or `HKCU` (also not "just to set up a test");
- `Set-MpPreference`, firewall, service, Windows feature, BitLocker or user-rights changes;
- anything that raises a UAC prompt ("Als Administrator prüfen");
- installing the MSI (the install test runs only on the disposable CI runner);
- `check --this-pc --plant` against a real plant folder.

Running the collector or `Riegel.Cli.exe collect` on your own machine is allowed: they only read. Remediation is only
ever written to files and never applied.

## Repository layout

```
contracts/collection-plan.json   what the collector reads (the contract between collector and rules)
rules/                           catalogue.json, <PREFIX>.json, lifecycle/, capabilities/, data/  (embedded)
templates/                       exception and tailoring templates (embedded)
schemas/                         JSON schemas of every file format
src/Riegel.Core/                 platform-neutral logic; Model/ and Services/ are the contracts
src/Riegel.Commands/             command line (CommandApp.Run)
src/Riegel.Cli/                  console host Riegel.Cli.exe / riegel (net10.0 and net10.0-windows)
src/Riegel.Platform.Windows/     IPlatform on Windows; links ../Riegel.Collector/Collection/**
src/Riegel.Collector/            portable collector (net35)
src/Riegel/                      WPF app
tests/Riegel.Tests/              tests, TestSupport, Fixtures
tools/RuleTool, tools/SnapshotAnonymizer, tools/banner
installer/                       MSI (WiX 5)
```

## Work packages and ownership

The work is split into packages; each package changes only the folders it owns and keeps CI green against the
scaffold's stubs. Merge order: scaffold → parsing → engine → collector → rules-a → rules-b → rules-c → oracle →
workflow → data-outputs → pdf → cli → app → integration.

| Package | Owns |
|---|---|
| scaffold | root files, `.github/**` (except `collector.yml`), `installer/**`, `schemas/**`, `contracts/**`, every project file, `src/Riegel.Core/{Model,Services,Samples,Settings,Localization,Diagnostics,About}/**`, `Resources/CommonStrings*`, the first version of every `<Area>Factory.cs`, the `RglCodes*` shells, `rules/catalogue.json`, `tests/Riegel.Tests/{TestSupport,Fixtures/synthetic,Infrastructure}/**`, icon assets, `docs/file-formats.md` |
| collector | `src/Riegel.Collector/**` and `src/Riegel.Platform.Windows/**` (except project files), `tools/{collect-builtin,readonly-proof,SnapshotAnonymizer}/**`, `.github/workflows/collector.yml`, `tests/Riegel.Tests/Windows/Collector/**`, `Fixtures/recorded/**` |
| parsing | `src/Riegel.Core/{Snapshots,Parsing,Attribution,Baselines}/**`, `Resources/ParsingStrings*` and their tests |
| engine | `src/Riegel.Core/{Catalogue,Evaluation,Facts}/**`, `Resources/EngineStrings*` and their tests |
| rules-a / rules-b / rules-c | their `rules/<PREFIX>.json` files, tables and `tests/Riegel.Tests/Rules/{A,B,C}/**`; rules-a also `templates/**` and `tools/RuleTool/**` |
| oracle | `tests/Riegel.Tests/Oracle/**` |
| workflow | `src/Riegel.Core/{Plants,Runs,Registers,Comparison,Scoring,ReportData}/**`, `Resources/WorkflowStrings*` |
| cli | `src/Riegel.Commands/**`, `src/Riegel.Cli/**` (except project files) |
| pdf | `src/Riegel.Core/Reports/Pdf/**`, `Resources/PdfStrings*` |
| data-outputs | `src/Riegel.Core/{Reports/Tabular,Evidence,Remediation}/**`, `Resources/DataOutputStrings*` |
| app | `src/Riegel/**` (except project file and icon assets), `tools/{screenshots,banner}/**` |
| integration | everything, after all packages |

A change outside the own folders – a contract type, a package reference, the solution, a workflow, the collection
plan, the notices – goes into the package's final report as an **integration request** (file, exact change, reason).

## Contracts and stubs

The contract types (`src/Riegel.Core/Model/**`) and the service interfaces (`src/Riegel.Core/Services/Interfaces.cs`)
are fixed by the scaffold and documented in [file-formats.md](file-formats.md). Every service is reachable through
`RiegelServices.CreateDefault(IPlatform)`; each area has a factory `public static I… Create(RiegelServices services)`
whose body the owning package replaces. The scaffold's bodies are honest placeholders: they never report a pass, and
what they cannot do yet fails with `RGL-0003` ("not available yet"). The table of placeholder behaviours is in
[file-formats.md › Services and factories](file-formats.md#services-and-factories).

Public names packages build on: `Riegel.Commands.CommandApp.Run`, `Riegel.Core.Services.RiegelServices.CreateDefault`,
`Riegel.Tests.TestSupport.SnapshotBuilder`, `Riegel.Core.Samples.SampleData`.

## Collector

`Riegel.Collector.exe` targets .NET Framework 3.5 with `supportedRuntime` v4.0 then v2.0.50727, so the same exe runs
on Windows 7 SP1 to 11 and Server 2008 R2 to 2025 with whatever .NET Framework is installed.

- **Own version:** `src/Riegel.Collector/collector-version.txt`. The project ignores the CI version
  (`TreatAsLocalProperty`).
- **Deterministic build and hash lock:** the CI builds the collector twice and compares the SHA-256; when
  `src/Riegel.Collector/collector.sha256` holds a hash (not `unset`), a different build fails the CI. Change the
  collector or the collection plan → raise the version and set the new hash.
- **Limits:** only 2.0-layer references, at most 150 KB, no network APIs, only the WMI methods in the plan's
  `allowedWmiMethods` (source-scan tests).
- Try it read-only: `src\Riegel.Collector\bin\Debug\net35\Riegel.Collector.exe --version`.

## Continuous integration

`.github/workflows/build.yml` runs on pushes to `main`, tags `v*`, pull requests and by hand. A newer push to the same
branch cancels the running build (`concurrency`); tag builds always finish.

- **linux:** build and test `tests/Riegel.Tests -f net10.0`, with free fonts for the PDF tests (DejaVu, Liberation,
  Noto CJK, plus WenQuanYi Micro Hei as a TrueType CJK candidate that may be missing), and a smoke test of
  `riegel check --snapshot` on a synthetic fixture (exit 0–2 accepted).
- **windows** (after linux): build and test the whole solution; build the collector twice and compare; publish the
  app (framework, self-contained, single file), `Riegel.Cli.exe`, `riegel-win-x64.exe` and the Linux CLIs
  (cross-published); pack the collector reproducibly; read-only proof, AppLocker data and demo report; MSI build and
  an install/uninstall test on the runner; SBOM; checksums; releases.

Steps that later packages fill skip themselves with a notice while their input is missing:

| Step | Active when |
|---|---|
| Linux smoke test | the synthetic fixture exists (it does) |
| Collector hash lock | `collector.sha256` holds a hash instead of `unset` |
| `collect-builtin.cmd` in the collector zip | `tools/collect-builtin/collect-builtin.cmd` exists (collector package) |
| Read-only proof | `tools/readonly-proof/Invoke-ReadOnlyProof.ps1` exists (collector package); until then a placeholder text is released |
| Signing | the organisation variable `SIGNING_ACCOUNT` is set (and not for pull requests) |
| Build provenance | the repository is public |

Releases never depend on artifacts (the organisation's artifact storage may be full): the Windows job publishes
`dist/` directly. Artifact uploads are tiny, kept 7 days and never fail the build. On `main` the rolling release
[`latest`](https://github.com/ict-baden/riegel/releases/tag/latest) is replaced with exactly these files:
`Riegel-x64.msi`, `Riegel.exe`, `Riegel-win-x64-framework.zip`, `riegel-win-x64.exe`, `riegel-linux-x64`,
`riegel-linux-arm64`, `Riegel-Collector.zip`, `Riegel-Collector-allowlist.zip`, `Riegel-Collector-ReadOnlyProof.txt`,
`Riegel-sbom.cdx.json`, `SHA256SUMS.txt`. A tag `v*` creates a versioned release.

## Rules for this repository

- Commit messages in German; code comments German; README and `docs/` English; `CLAUDE.md` German.
- Every `.cs` file starts with `// SPDX-License-Identifier: MIT`.
- UI texts only in `.resx` (en, de, es, fr, zh-Hans; unsure translations marked `TODO-übersetzen`); rule texts in the
  catalogue (en, de); collector texts in `messages.<lang>.txt`.
- No person names or GitHub handles anywhere, also not in demo data, fixtures or screenshots.
- Dependencies only with permissive licences, pinned exactly, listed in `THIRD-PARTY-NOTICES.txt`; see the licence
  pins in `CLAUDE.md` and `.github/dependabot.yml`.
- No CIS, BSI SiSyPHuS or IT-Grundschutz content; no Microsoft Security Baseline files or values.
