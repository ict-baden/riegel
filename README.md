<p align="center">
  <img src="docs/assets/logo.svg" alt="Riegel" width="96">
</p>

<h1 align="center">Riegel</h1>

<p align="center">
  <b>Hardened where it can be. Documented where it can't.</b><br>
  Checks Windows machines in plants, labs and control rooms against a hardening baseline – read-only and offline –
  records justified exceptions and produces an evidence report for sign-off.
</p>

<p align="center">
  <img alt="Status: in Arbeit" src="https://img.shields.io/badge/Status-in%20Arbeit-E3A008">
  <img alt="Licence: MIT" src="https://img.shields.io/badge/Licence-MIT-blue">
  <a href="https://github.com/ict-baden/riegel/actions/workflows/build.yml"><img alt="Build" src="https://github.com/ict-baden/riegel/actions/workflows/build.yml/badge.svg"></a>
</p>

<p align="center">
  <a href="#status">Status</a>
  &nbsp;·&nbsp; <a href="#how-riegel-gets-onto-a-machine">Packages</a>
  &nbsp;·&nbsp; <a href="#documentation">Documentation</a>
</p>

> [!NOTE]
> **Deutsch:** Riegel (Härtungs-Check) prüft Windows-Rechner in Anlagen, Laboren und Leitständen lesend und offline
> gegen ein eigenes Härtungs-Regelwerk. Was nicht gehärtet werden kann, wird als begründete Ausnahme mit Freigabe
> festgehalten; am Ende steht ein Nachweis zum Abzeichnen (PDF, Excel, JSON). Riegel ändert auf den geprüften
> Rechnern nichts. **Die MSI ist nicht der Weg auf OT-Rechner:** Dort läuft nur der portable Sammler, ohne
> Installation. Stand: **in Arbeit** – noch keine Version zum Einsatz.

## Status

**In Arbeit (work in progress).** This repository currently contains the scaffold: the solution, the file formats
and contracts, the collection plan, the JSON schemas, test support with synthetic fixtures, and the build pipeline.
The rule catalogue, the evaluation, the collector and the reports are being built in separate work packages. Builds
on the rolling [`latest`](https://github.com/ict-baden/riegel/releases/tag/latest) release are placeholders: they
start, print their version and say "not implemented yet" – they do not check anything.

## How Riegel gets onto a machine

- **Analysis app** (`Riegel-x64.msi`, `Riegel.exe`, framework ZIP): for admin, engineering and auditor PCs and
  IT-managed clients and servers. It evaluates snapshots, keeps the plant folder (exceptions, tailoring, measures)
  and writes the reports.
- **Portable collector** (`Riegel-Collector.zip`): a single small program for OT and older machines (Windows 7 SP1
  to 11). It reads the security configuration and writes one snapshot file. It installs nothing and changes nothing.
- **Built-in commands** (`collect-builtin.cmd`): for machines where no foreign program may run.
- **Command line** for Windows (`riegel-win-x64.exe`, `Riegel.Cli.exe`) and Linux (`riegel-linux-x64`,
  `riegel-linux-arm64`) for auditors and CI.

**The MSI is not the way onto OT or validated machines.** Installing software on such a machine is a change in
itself. Use the portable collector or the built-in commands there, and evaluate the snapshot elsewhere.

## Documentation

| Page | Content |
|---|---|
| [File formats](docs/file-formats.md) | Every file Riegel reads or writes, the collection plan, the check vocabulary and the contract types |
| [Development](docs/development.md) | Build, test, work packages, rules for this repository |
| [Changelog](CHANGELOG.md) | Changes per version |

More pages (installation, collector, usage, command line, exceptions, evaluation, reports, limitations) follow with
the work packages.

---

<sub>
Riegel is free and open source software by <b>ICT-Baden</b>. &nbsp;·&nbsp;
Licence: <a href="LICENSE">MIT</a> &nbsp;·&nbsp;
Third-party licences: <a href="THIRD-PARTY-NOTICES.txt">THIRD-PARTY-NOTICES.txt</a> &nbsp;·&nbsp;
Support: free via GitHub, guaranteed with a <a href="SUPPORT.md">support contract</a> &nbsp;·&nbsp;
<a href="SECURITY.md">Security</a> &nbsp;·&nbsp;
<!-- ⚠ Impressum-Adresse offen (richtlinien OFFEN.md): https://ict-baden.de/impressum lieferte am 2026-10-18 HTTP 404. -->
<a href="https://ict-baden.de/impressum">Impressum</a>
</sub>
