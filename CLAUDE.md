# CLAUDE.md – Riegel

Riegel: Checks Windows machines in plants, labs and control rooms against a hardening baseline – read-only and offline – records justified exceptions and produces an evidence report for sign-off.

Projekt von ICT-Baden. Es gelten die Richtlinien in [`ict-baden/richtlinien`](https://github.com/ict-baden/richtlinien). Stand und Zuständigkeit stehen in `ict-baden/ict-baden-projekte` (`projekte.json`, Eintrag `haertungs-check`, `notizen/haertungs-check.md`, `in-arbeit.md`).

## Bauen und testen

```powershell
dotnet build Riegel.sln
dotnet test Riegel.sln                           # beide Zielplattformen; Windows-Tests nur unter Windows
dotnet test tests/Riegel.Tests -f net10.0        # nur der plattformneutrale Teil (so läuft es in der CI unter Linux)
dotnet run --project src/Riegel.Cli -f net10.0 -- --version
dotnet run --project src/Riegel -- --demo-report out/demo
```

Mehr in [`docs/development.md`](docs/development.md).

## Aufbau

- `src/Riegel` – Oberfläche (WPF), startet ohne Argumente die Oberfläche, sonst die Befehle aus `Riegel.Commands`
- `src/Riegel.Cli` – Konsolenprogramm `Riegel.Cli.exe` (Windows) und `riegel` (Linux), dieselben Befehle
- `src/Riegel.Commands` – Befehlszeile (`CommandApp.Run`), plattformneutral
- `src/Riegel.Core` – Logik ohne Oberfläche und ohne Systemaufrufe (`net10.0`): Verträge (`Model/`, `Services/`), Katalog, Auswertung, Register, Vergleich, Berichte, Nachweispaket
- `src/Riegel.Platform.Windows` – Windows-Anbindung (`IPlatform`), bindet den Erhebungscode des Sammlers ein
- `src/Riegel.Collector` – portabler Sammler `Riegel.Collector.exe` (net35)
- `contracts/collection-plan.json` – was der Sammler liest (Vertrag zwischen Sammler und Regeln)
- `rules/`, `templates/` – Regelwerk und Vorlagen (eingebettet in `Riegel.Core`); `schemas/` – JSON-Schemas aller Formate
- `tests/Riegel.Tests` – Tests, Testhilfen (`TestSupport/`), synthetische Exporte (`Fixtures/synthetic/`)
- `tools/RuleTool`, `tools/SnapshotAnonymizer`, `tools/banner`
- `installer/` – MSI (WiX 5)
- `docs/` – Doku (Englisch), Bilder in `docs/assets/`; die Formate in `docs/file-formats.md`
- `.github/` – CI (`workflows/build.yml`), Release „latest“ (`scripts/release-latest.ps1`), Stückliste (`scripts/sbom-shipped.ps1`), reproduzierbare ZIPs (`scripts/zip-deterministic.ps1`), Issue-Formulare, Dependabot

## Besonderheiten

- **Windows-gebunden** (Richtlinie 01): Sicherheitsrichtlinie, Überwachungsrichtlinie, Registry-Richtlinien, Defender, BitLocker und Firewall lassen sich nur unter Windows erfassen. Deshalb WPF (Richtlinie 02, Fall 3: Rechteerhöhung für „Diesen PC prüfen“, Explorer-Integration, dateizentrierter Ablauf). Auswertung, Register, Vergleich und Berichte bleiben plattformneutral in `Riegel.Core`; die Befehlszeile gibt es auch für Linux x64 und arm64. Die CI testet den neutralen Teil unter Linux.
- **Sammler als net35-Exe** (`Riegel.Collector.exe`, `supportedRuntime` v4.0 vor v2.0.50727): .NET 10 startet nicht auf Windows 7, 8.1 und 2008 R2, und eine Installation auf validierten Rechnern wäre selbst eine Änderung. Abweichung von Richtlinie 02, **freigegeben am 2026-10-19**: Wo .NET 10 auf dem Zielsystem nicht läuft, geht dieser Teil auf eine ältere .NET-Version. Der Sammler hat eine **eigene Version** (`src/Riegel.Collector/collector-version.txt`, die CI-Version greift nicht: `TreatAsLocalProperty`) und wird **bytegleich** gebaut; die CI baut ihn zweimal und vergleicht mit `collector.sha256` (Hash-Sperre, aktiv seit der Integration). Jede Änderung an Sammler-Code oder Erhebungsplan: Version anheben (sobald die bisherige veröffentlicht ist), `collector.sha256` neu setzen und die Runner-Fixtures neu aufzeichnen (`docs/development.md`). Nur Referenzen der 2.0-Schicht (mscorlib, System, System.Management), keine Netzwerk-APIs, nur die WMI-Methoden aus `allowedWmiMethods` – ein Quelltext-Test prüft das.
- **Tests nur mit Fixtures oder lesenden Abfragen.** Nie die Konfiguration dieses oder eines anderen Rechners ändern: kein `secedit /configure`, kein `auditpol /set`, keine Registry-Schreibzugriffe unter HKLM oder HKCU, kein `Set-MpPreference`, keine Änderungen an Firewall, Diensten, Features, BitLocker oder Benutzerrechten, kein `gpupdate`, kein LGPO-Import, keine UAC-Abfrage auslösen, die MSI nie auf einem Arbeitsplatz installieren (der Installationstest läuft nur auf dem CI-Runner). `check --this-pc --plant` nie gegen einen echten Anlagenordner. Abhilfe wird nur als Dateien geschrieben, nie angewendet.
- **Lizenzen:** PDFsharp/MigraDoc bleibt bei 6.2.x (MIT) – ab 6.5.0 AGPL-3.0. SixLabors.Fonts bei 1.x (Apache-2.0) – ab 2.0 Six Labors Split License. WiX bei 5.x (MS-RL) – ab 6.0 OSMF-EULA. JsonSchema.Net (nur Tests) bei 8.x (MIT) – ab 9.0 OSMF-EULA. Dependabot ignoriert diese Sprünge (`.github/dependabot.yml`). Jede neue oder aktualisierte Abhängigkeit in `THIRD-PARTY-NOTICES.txt` eintragen; ein Test vergleicht.
- **Keine CIS- oder BSI-Inhalte:** keine Texte, Werte oder IDs aus CIS Benchmarks, BSI SiSyPHuS oder IT-Grundschutz-Bausteinen, keine Dateien oder Werte aus Microsoft Security Baselines (MSB-Verweise nennen Dokument und Einstellung, nie den Wert). Sollwerte nur mit ACSC (CC BY 4.0), Microsoft-Server-Doku (CC BY 4.0), Microsoft-Learn-Fakten oder DISA; Grundschutz++ nur als Kennung. Der Katalog-Test prüft das.
- **Konsolenprogramm:** `Riegel.exe` ist eine WinExe mit angehängter Konsole (wie Zutritt) – in Skripten und geplanten Aufgaben `Riegel.Cli.exe` nehmen, dort ist `$LASTEXITCODE` verlässlich. Exitcodes nach Nagios-Art (0 OK, 1 WARNING, 2 CRITICAL, 3 UNKNOWN); sie melden Veränderungen, nicht den Dauerzustand (spec 7.7).
- **Verträge und Gates:** Verträge (`Model/`, `Services/`), Dateiformate, Schemas und Erhebungsplan ändern sich nur zusammen mit `docs/file-formats.md`, den Schemas und den Tests. Gates, die grün bleiben müssen: Regelvektoren und Mutationstest mit der echten Engine, Orakel (`tests/Riegel.Tests/Oracle`, Erwartungen nur mit Begründung im Commit ändern), Ende-zu-Ende-Tests, `docs/rules.md` und `docs/error-codes.md` aktuell (RuleTool bzw. `RIEGEL_UPDATE_DOCS=1`), Hash-Sperre des Sammlers. Die Factory-Signaturen (`<Bereich>Factory.Create(RiegelServices)`) bleiben fest.
- **Ablage als Anlagenordner mit JSON-Dateien** statt SQLite (Abweichung von Richtlinie 02, freigegeben am 2026-10-19): der Ordner ist der Nachweis des Kunden. Exporte, Register und Berichte sind vertraulich – nie in Issues oder ins Repo (nur synthetische Fixtures).
- **Regeltexte im Katalog-JSON und Sammler-Texte in `messages.<Sprache>.txt`** statt `.resx` (Abweichung von Richtlinie 13 Regel 1, freigegeben am 2026-10-19): Regeln sind versionierte Daten, der Sammler läuft unter CLR 2.
- **Windows-Projekte auf `net10.0-windows` ohne SDK-Version** (Abweichung von Richtlinie 02, freigegeben am 2026-10-19): Riegel nutzt keine WinRT-API; mit `net10.0-windows10.0.19041.0` käme `Microsoft.Windows.SDK.NET.dll` (Lizenz des Windows SDK, nicht MIT) in jedes Windows-Paket. CI und `ThirdPartyNoticesTests` prüfen, dass sie nicht ausgeliefert wird. Mindestens Windows 10 1809 / Server 2019 bleibt die Vorgabe (Doku).
- **MSI:** `UpgradeCode` (B5657095-F1E6-418A-8F3E-9578C4EB1B13) und die GUID der Startmenü-Komponente (577DFE8C-6CB0-4127-9B0A-F86608882BE5) nie ändern. Die MSI ist nicht der Weg auf OT-Rechner.
- **Herausgeber:** „ICT Baden GmbH“ (wie im Impressum) in `LICENSE`, `Directory.Build.props`, MSI-`Manufacturer`, SBOM und `AppInfo`. Der Sammler 0.1.0 behält in seinen Dateieigenschaften „ICT-Baden“ (feste Werte in seiner csproj, Hash-Sperre); beim nächsten Versionssprung des Sammlers angleichen. „ICT-Baden“ bleibt in technischen Kennungen (AppUserModelID, Mutex, `%ProgramData%\ICT-Baden\Riegel`, Registry). Impressum `https://ict-baden.de/impressum/`, Datenschutz `https://ict-baden.de/datenschutz/riegel/`, Produktseite `https://ict-baden.de/produkte/riegel/`, keine E-Mail-Adressen. Signieren: die Schritte überspringen sich, solange `vars.SIGNING_ACCOUNT` leer ist.
- **Keine Personennamen** in Oberfläche, Demo-Daten, Fixtures und Doku; Rollen statt Namen.

## Regeln

- Commit-Nachrichten auf Deutsch.
- Kein Text fest im Code: jeden neuen Text in alle Sprachdateien eintragen (en, de, es, fr, zh-Hans), Fachbegriffe aus `ict-baden/richtlinien/sprachen/glossar.csv`. Unsichere Übersetzung: englischen Text eintragen, mit `TODO-übersetzen` markieren. Regeln in Richtlinie 13.
- Code-Kommentare Deutsch, README und `docs/` Englisch; jede `.cs`-Datei beginnt mit `// SPDX-License-Identifier: MIT`.
- Nach Arbeit an diesem Projekt: Verlauf in `ict-baden-projekte/notizen/haertungs-check.md` und Logbuch dort ergänzen (macht die Hauptsitzung; Agenten schreiben dort nie).
- Vor jedem Release: Abschnitt „Unveröffentlicht“ im `CHANGELOG.md` pflegen.
