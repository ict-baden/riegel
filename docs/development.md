[Riegel](../README.md) › Development

# Development

**Contents:** [Requirements](#requirements) · [Build](#build) · [Test](#test) · [Never change this PC](#never-change-this-pc) ·
[Repository layout](#repository-layout) · [Architecture](#architecture) · [How the code was built](#how-the-code-was-built) ·
[Contracts and services](#contracts-and-services) · [Collector](#collector) ·
[Rules and generated documents](#rules-and-generated-documents) ·
[Demo, screenshots and report pages](#demo-screenshots-and-report-pages) · [Recorded fixtures](#recorded-fixtures) ·
[Continuous integration](#continuous-integration) · [Releases](#releases) · [Rules for this repository](#rules-for-this-repository)

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
dotnet run --project src/Riegel -c Release -- --demo-report out/demo  # writes every document O1–O15 of the demo plant
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

Synthetic fixtures are in `tests/Riegel.Tests/Fixtures/synthetic` (see the README there); recorded fixtures from the CI
runners in `Fixtures/recorded` ([Recorded fixtures](#recorded-fixtures)).

Gates that guard the evaluation:

- **Rule vectors** (`RuleVectors/`): every vector of the embedded catalogue and of the engine's own test catalogue
  (`TestRules/`, written by `generate-test-rules.py`) against the real engine, with the time base of
  [File formats › Test vectors](file-formats.md#test-vectors).
- **Mutation test** (`Mutation/`): every evidence item of every rule set to denied, not collected, truncated, error or
  missing must never give a pass – except proven absence listed in `Rules/{A,B,C}/when-missing-pass.txt` or a cited OS
  default (flagged *FromDefault*). A second pass checks precedence between configuration sources: in a `firstOf`, a
  registry part that is made unavailable must never be skipped so that a lower-ranked registry part carries a pass (a
  policy value would take precedence); a runtime part before a registry part may fall away as intended.
- **Oracle** (`Oracle/`): expected results of all 153 rules for a workstation snapshot written from research
  measurements; only the oracle's author or a reviewer changes `expected-results.json`, with the reason in the commit
  message. `Compare-LocalRecording.ps1` compares a local recording of your own PC (kept outside the repository).
- **End to end** (`EndToEnd/`): the command line through import, documents, evidence pack, `verify` and
  `compare-snapshot`, and the demo plant with every output O1–O15.

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
tools/RuleTool                   rules-doc, verify-sources, coverage (developer tool, not shipped)
tools/SnapshotAnonymizer         anonymises runner recordings into fixtures
tools/collect-builtin            collect-builtin.cmd (built-in commands)
tools/readonly-proof             read-only proof (CI runners only)
tools/screenshots, tools/banner  documentation images
installer/                       MSI (WiX 5)
```

## Architecture

- **`Riegel.Core`** (`net10.0`, `CA1416` as an error): everything that decides – catalogue loader and validator
  (`Catalogue/`), facts (`Facts/`), the evaluator (`Evaluation/`), the snapshot reader and parsers (`Snapshots/`,
  `Parsing/`), value-source attribution (`Attribution/`), customer baselines (`Baselines/`), the plant folder, runs,
  registers, comparison, scoring and report data (`Plants/`, `Runs/`, `Registers/`, `Comparison/`, `Scoring/`,
  `ReportData/`), PDF (`Reports/Pdf/`, PDFsharp-MigraDoc 6.2.4), Excel and CSV (`Reports/Tabular/`, ClosedXML), the
  evidence pack (`Evidence/`) and remediation files (`Remediation/`). No process start, no network, no Windows API.
- **`Riegel.Commands`** (`net10.0`): every command of the command line, argument parsing, text and JSON output, exit
  codes. **`Riegel.Cli`** is its console host for Windows (`Riegel.Cli.exe`) and Linux (`riegel`).
- **`Riegel.Platform.Windows`**: `IPlatform` – collection of this machine through the linked collector code, the
  elevation hand-off, the shell, the collector bundle export.
- **`Riegel.Collector`** (net35): the portable collector; its collection code (`Collection/**`) compiles for net35 and
  `net10.0-windows` and reads only what `contracts/collection-plan.json` names.
- **`Riegel`** (WPF): the app; all logic it uses comes from the services, so an Avalonia or web front end stays
  possible.

Flow: snapshot (collector or bundle) → `ISnapshotReader` (limits, redaction, `parsed:*`) → `IFactsDeriver` →
`IEvaluator` (raw statuses) → `IRegisterService.Apply` (exceptions, tailoring, measures) → `IScoring` → run file →
`IRunComparer` (previous run, approved state, drift) → `IReportDataBuilder` → PDF, Excel, CSV, evidence pack,
remediation.

## How the code was built

Release 0.1.0 was built in work packages that each owned their folders and were merged in the order scaffold →
parsing → engine → collector → rules-a → rules-b → rules-c → oracle → workflow → data-outputs → pdf → cli → app →
integration. The ownership table below still says who wrote what; after the integration every file can be changed
together with its tests.

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

## Contracts and services

The contract types (`src/Riegel.Core/Model/**`) and the service interfaces (`src/Riegel.Core/Services/Interfaces.cs`)
are documented in [file-formats.md](file-formats.md); a test keeps the type list there complete. Every service is
reachable through `RiegelServices.CreateDefault(IPlatform)` (or `RiegelServices.Create(IPlatform, TimeProvider)` with a
fixed clock); each area has a factory `public static I… Create(RiegelServices services)`
([Services and factories](file-formats.md#services-and-factories)). A change to a contract type, a file format or the
collection plan changes the schema in `schemas/`, `docs/file-formats.md` and, for the plan, the collector version and
its hash.

Public names packages build on: `Riegel.Commands.CommandApp.Run`, `Riegel.Core.Services.RiegelServices.CreateDefault`,
`Riegel.Tests.TestSupport.SnapshotBuilder`, `Riegel.Core.Samples.SampleData`.

## Collector

`Riegel.Collector.exe` targets .NET Framework 3.5 with `supportedRuntime` v4.0 then v2.0.50727, so the same exe runs
on Windows 7 SP1 to 11 and Server 2008 R2 to 2025 with whatever .NET Framework is installed.

- **Own version:** `src/Riegel.Collector/collector-version.txt`. The project ignores the CI version
  (`TreatAsLocalProperty`).
- **Deterministic build and hash lock:** the CI builds the collector twice and compares the SHA-256 with
  `src/Riegel.Collector/collector.sha256`; a different build fails the CI ("Sammler-Bytes geändert ohne
  Versionssprung"). After a change to the collector or the collection plan: raise `collector-version.txt` (once the
  version was released), build twice and set the new hash:

  ```powershell
  foreach ($d in 'a', 'b') { dotnet build src/Riegel.Collector/Riegel.Collector.csproj -c Release --no-incremental -o out/collector-$d }
  (Get-FileHash out/collector-a/Riegel.Collector.exe -Algorithm SHA256).Hash.ToLower()   # equal for a and b
  ```

  The bytes are the same on a development PC and on the runners (pinned compiler and reference assemblies,
  `Deterministic`, `PathMap`).
- **Limits:** only 2.0-layer references, at most 150 KB, no network APIs, only the WMI methods in the plan's
  `allowedWmiMethods` (source-scan tests).
- Try it read-only: `src\Riegel.Collector\bin\Debug\net35\Riegel.Collector.exe --version`.

## Rules and generated documents

- **Rules** live in `rules/<PREFIX>.json` (one file per prefix), tables in `rules/lifecycle/`, `rules/capabilities/`,
  `rules/data/`; they are embedded into `Riegel.Core`. Every check rule needs a `verified` entry (ACSC, MSDOCS-CCBY,
  MSLEARN-FACT or DISA) and vectors for pass, fail, missing and not collected ([Rule sources](rule-sources.md)).
- `dotnet tools/RuleTool/bin/Release/net10.0/RuleTool.dll rules-doc` regenerates [docs/rules.md](rules.md); a test (also
  on Linux) fails when it is out of date. `RuleTool coverage` lists evidence keys the collection plan does not read
  (must be none); `RuleTool verify-sources <folder>` checks references against local copies of the sources.
- `RIEGEL_UPDATE_DOCS=1 dotnet test Riegel.sln -c Release --filter "FullyQualifiedName~DocsTests|FullyQualifiedName~ErrorCodesDocTests"`
  regenerates
  [docs/error-codes.md](error-codes.md) (from the `RglCodes*` classes) and the item list of
  [docs/collector-change-request.md](collector-change-request.md) (from the collection plan); without the variable the
  same tests fail when the pages are out of date.
- The engine's test catalogue: `python tests/Riegel.Tests/RuleVectors/TestRules/generate-test-rules.py .`.

## Demo, screenshots and report pages

- **Demo plant** "Musterwerk – Linie 3" (`src/Riegel/Demo`): twelve synthetic snapshots derived from the synthetic
  fixtures by `python src/Riegel/Demo/generate-demo.py .`, registers with roles only and fictitious references in
  `DemoData.cs`. It is built at start-up through the real services and evaluated with the real engine and the
  embedded catalogue (`Riegel.exe --demo`, `--screenshots`, `--demo-report`).
- **Screenshots and banner:** `tools/screenshots/make-assets.ps1` builds the app, renders every page and the two
  dialogs in light and dark (English, own data folder) into `docs/assets/screenshots`, and the banner from
  `tools/banner/banner.html` with the installed Chrome through `playwright-core` (`npm install` in `tools/banner`, or
  `NODE_PATH` pointing at an existing installation). `-Review` renders all five languages into a temporary folder and
  prints layout findings (`[layout]`: single-line texts that are cut off, and list column headers narrower than their
  title – list columns grow to their header in every language); `-SkipBanner` skips the banner. WPF needs an interactive desktop session.
- **Report pages:** `Riegel.exe --demo-report <folder>` (with `RIEGEL_LANGUAGE=en`, `de`, `zh-Hans`), then render the
  pages with [pypdfium2](https://pypi.org/project/pypdfium2/) in a virtual environment (licence BSD-3-Clause and
  Apache-2.0 plus the PDFium dependency licences, per its package metadata; never PyMuPDF, which is AGPL) into
  `docs/assets/reports` – see [Reports › Example pages](reports.md#example-pages).

## Recorded fixtures

`tests/Riegel.Tests/Fixtures/recorded` holds anonymised snapshots and raw exports of the three CI runners. To re-record
after a change to the collection plan: run the workflow `Collector` with `record=true`
(`gh workflow run collector.yml -f record=true`), download the artifact (`gh run download <run>`), review every file
for real data (host and user names, SIDs, GUIDs, addresses, vendor names) and replace the files. Never commit a
snapshot of a workstation or a customer machine; your own PC is compared only locally
(`tests/Riegel.Tests/Oracle/Compare-LocalRecording.ps1`).

## Continuous integration

`.github/workflows/build.yml` runs on pushes to `main`, tags `v*`, pull requests and by hand. A newer push to the same
branch cancels the running build (`concurrency`); tag builds always finish.

- **linux:** build and test `tests/Riegel.Tests -f net10.0` (with RuleTool), with free fonts for the PDF tests
  (DejaVu, Liberation, Noto CJK for the CFF test, WenQuanYi Micro Hei for Chinese); publish `riegel` for linux-x64 and
  let it run `--version`, `check` (also `--brief`) on synthetic fixtures, a plant with `import`, `report --evidence`
  and `verify`, which must reproduce.
- **windows** (after linux): build and test the whole solution; build the collector twice and check the hash lock;
  publish the app (framework, self-contained, single file), `Riegel.Cli.exe`, `riegel-win-x64.exe` and the Linux CLIs
  (cross-published); pack the collector reproducibly; read-only proof (before the MSI test, which wakes services and
  the installer); MSI build and an install/uninstall test on the runner; AppLocker data; demo report with every
  document O1–O15; SBOM; checksums; releases.
- **Collector** (`collector.yml`, on changes to the collector, the platform, the built-in commands, the proof, the
  anonymiser, the plan or the snapshot schema): on `windows-2022`, `windows-2025` and `windows-11-arm`, build twice,
  run elevated and as a Basic User, compare the item sets of net35 and `Riegel.Cli.exe collect`, validate against the
  schema, run the read-only proof for `collector`, `--deep`, `--minimal`, `Riegel.Cli.exe collect` and
  `collect-builtin.cmd`, and record V1–V4, V7 and V8. Windows minutes count double: push sparingly.

Signing steps run only when the organisation variable `SIGNING_ACCOUNT` is set (and not for pull requests); build
provenance only for a public repository.

Releases never depend on artifacts (the organisation's artifact storage may be full): the Windows job publishes
`dist/` directly. Artifact uploads are tiny, kept 7 days and never fail the build. On `main` the rolling release
[`latest`](https://github.com/ict-baden/riegel/releases/tag/latest) is replaced with exactly these files:
`Riegel-x64.msi`, `Riegel.exe`, `Riegel-win-x64-framework.zip`, `riegel-win-x64.exe`, `riegel-linux-x64`,
`riegel-linux-arm64`, `Riegel-Collector.zip`, `Riegel-Collector-allowlist.zip`, `Riegel-Collector-ReadOnlyProof.txt`,
`Riegel-sbom.cdx.json`, `SHA256SUMS.txt`. A tag `v*` creates a versioned release.

## Releases

1. `CHANGELOG.md`: rename "Unveröffentlicht" to `## [x.y.z] – <date>`; `Directory.Build.props` `<Version>`; the
   catalogue's `releasedOn` in `rules/catalogue.json` when the catalogue changes; the collector version and hash when
   the collector changes.
2. Push `main`, wait for a green build (it updates `latest`), check both asset lists.
3. `git tag vX.Y.Z && git push origin vX.Y.Z`: the tag build creates the release with the versioned file names.
4. Follow the release checklist of the ICT-Baden guidelines (`CHECKLISTE-RELEASE.md`).

## Rules for this repository

- Commit messages in German; code comments German; README and `docs/` English; `CLAUDE.md` German.
- Every `.cs` file starts with `// SPDX-License-Identifier: MIT`.
- UI texts only in `.resx` (en, de, es, fr, zh-Hans). es, fr and zh-Hans files start with the comment "Maschinell
  vorübersetzt – TODO-übersetzen …" until a native speaker has reviewed them; especially uncertain entries carry the
  comment `TODO-übersetzen` in all three (`TranslationMarkingTests`). Rule texts in the
  catalogue (en, de); collector texts in `messages.<lang>.txt`.
- No person names or GitHub handles anywhere, also not in demo data, fixtures or screenshots.
- Dependencies only with permissive licences, pinned exactly, listed in `THIRD-PARTY-NOTICES.txt`; see the licence
  pins in `CLAUDE.md` and `.github/dependabot.yml`.
- No CIS, BSI SiSyPHuS or IT-Grundschutz content; no Microsoft Security Baseline files or values.
