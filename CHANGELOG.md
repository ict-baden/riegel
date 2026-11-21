# Änderungen

Alle wichtigen Änderungen an Riegel. Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [Semantic Versioning](https://semver.org/lang/de/).

Jeder Versionsabschnitt endet mit „English“: Das ist der Text des Releases auf GitHub (`.github/scripts/release-notes.ps1`).

## [Unveröffentlicht]

## [1.0.0] – 2026-11-21

Erste öffentliche Version. Sie enthält alles, was als 0.1.3 vorgesehen war (nie veröffentlicht), und die Korrekturen danach. Sammler unverändert (0.1.0, dieselben Bytes wie seit v0.1.0), Regelwerk 2026.10.1.

### Behoben
- **Gruppen:** Eine neue Gruppe bekam die ID einer gelöschten wieder (`g-` + Anzahl), und Rechner sowie freigegebene Ausnahmen und Zuschnitte der gelöschten Gruppe galten dann still für die neue. Neue IDs liegen jetzt über allen, die in Anlage, Rechnern und Registern vorkommen (ohne Groß-/Kleinschreibung).
- **Import mehrerer Exporte:** Je Rechner wird in Erfassungsreihenfolge ausgewertet, nicht in der Reihenfolge der Argumente oder der Auswahl im Dateidialog. Vorher bekam ein neuerer Export vor einem älteren desselben Imports den falschen Vorlauf, mit falschen Regressionen, neuen Regeln und „neu hoch“.
- **Vergleich gegen den freigegebenen Stand:** „Ausnahme entfernt“ (auch nur beantragt oder nach der Freigabe geändert) und „Zuschnitt entfernt“ nach „nicht erfüllt“ zählen als Verschlechterung (Token `regression`), wie die Kennzeichen am Ergebnis.
- **Lauf-Flag `clockSuspect`:** dieselbe Uhrprüfung wie die Auswertung (ein Tag Spielraum, nächstniedrigere UBR, Fakten statt nur Kopf). Vorher konnte der Lauf „Uhr unplausibel“ tragen, während die Altersregeln normal bewertet waren, und umgekehrt. Ein `verify` von Nachweispaketen aus 0.1.x kann in solchen Grenzfällen eine Abweichung der Lauf-Flags melden.
- **Oberfläche während einer Arbeit:** Navigation und Seiten sind gesperrt, auch für Tastatur, Kürzel und Ablegen von Dateien; die Anzeige „läuft …“ bleibt bis zum Ende der letzten Arbeit. Vorher ließen sich parallel zweite Schreibvorgänge im Anlagenordner starten.
- **„Diesen PC prüfen“:** Der Übergabeordner mit dem Export wird auch gelöscht, wenn Erfassung, Übergabe oder Import scheitern; ältere Reste räumt Riegel beim Start auf. Scheitert die Erfassung im erhöhten Prozess, nennt RGL-0115 jetzt Exitcode und Ursache (der erhöhte Prozess hinterlegt nur den Code in `error.txt`).
- **`collect-builtin.cmd`** wechselt vor dem ersten Programmaufruf nach `%SystemRoot%\System32` und setzt `PATH` und `PATHEXT` fest. Vorher hätte ein fremdes `reg.bat` oder `find.exe` im aktuellen Ordner (Stick, Freigabe) mit Administratorrechten laufen können.
- **Vergleich:** Ein gerade nicht lesbarer Export (Freigabe weg, Datei gesperrt) wird auf der Seite gemeldet, statt einen halb zurückgesetzten Stand zu lassen.
- **Demo:** Jede Erzeugung bekommt wieder einen eigenen Ausgabeordner (die feste Demo-Uhr legte alles in denselben); in derselben Sekunde mit „-2“, „-3“ …
- **Sprung zu einer Ausnahme** bei aktiver Suche zeigt den Eintrag (Suche wird zurückgesetzt) statt eines leeren Details.
- **Import-Dialog:** Ein Ordner ohne Exporte (RGL-0470) oder ein nicht lesbarer Ordner wird im Dialog gemeldet statt still ignoriert.

### Nur-Lesen-Nachweis
- **Rote Läufe auf `windows-2025` geklärt:** Windows startet VSS und swprv auf jedem Server-2025-Runner 11 bis 13 Minuten nach dem Boot selbst; `PrintJobCleanupTask` schaltet sich nach ihrem ersten Lauf selbst ab. Der Sammler startete VSS in keinem von 21 Läufen. Belege und Regeln in `docs/collector.md` („How the proof tells the collector from Windows“).
- **Strenger:** Jeder Wechsel eines Dienst-Laufzustands, den Windows nicht nach seiner Bauart selbst vornimmt, wird wiederholt gemessen (jedes Ziel noch dreimal, danach 20 s Beobachtung); jede erneute Abweichung und jeder Dienst, der nicht zurückkehrt, lässt den Nachweis scheitern. Kontrolle: Ein Ersatz-Ziel, das bei jedem Aufruf VSS startet, bestand den alten Nachweis und scheitert am neuen.
- Änderungen durch Windows werden nur mit Beleg entschuldigt: geänderte geplante Aufgaben nur von einer festen Liste und mit dem Urheber aus dem Sicherheitsprotokoll, Firewall-Regeln nur mit dem Firewall-Dienst oder Defender als Urheber im Firewall-Protokoll, der Starttyp von BITS über das Systemprotokoll, neue Defender-Einstellungen nur mit einem Plattform-Update im selben Zeitraum.

### Geändert
- **Herausgeber ist die ICT Baden GmbH** (wie im Impressum): Copyright, Lizenz, MSI („Hersteller“), Stückliste und Dateieigenschaften der Programme. Die Info-Seite nennt den Herausgeber und führt auf Impressum, Datenschutzerklärung (`https://ict-baden.de/datenschutz/riegel/`) und Produktseite (`https://ict-baden.de/produkte/riegel/`); `--info` ebenso. Der Sammler 0.1.0 behält in seinen Dateieigenschaften „ICT-Baden“, damit seine Bytes (Hash, AppLocker-Daten) gleich bleiben.
- **Releases:** Der Text eines Tag-Releases ist der englische Teil des CHANGELOG-Abschnitts mit Sammler-Hash und Dateiliste. README, Doku, `THIRD-PARTY-NOTICES.txt` und RGL-0130 nennen die Dateien der Tag-Releases mit Version (`Riegel-Collector-<version>.zip` …); ein Test hält die Beispiele aktuell.
- **MSI:** Die Programme tragen die Dateiversion mit Build-Nummer (`<Version>.<Lauf>`, z. B. `1.0.0.<Lauf>`), auch im Tag-Release. So ist ein Release neuer als jeder `latest`-Build davor, und Windows Installer ersetzt die Dateien bei einem Update sicher. Die CI prüft die MSI zusätzlich als Update über `latest` und über das neueste Tag-Release.
- **CI:** Pushs und PRs bauen nur den plattformneutralen Teil unter Linux; Windows-Build, Pakete und `latest` gibt es nachts (wenn `main` neu ist), bei Tags, von Hand, mit `[voll]` in der Commit-Nachricht und bei PRs mit Label `voll-bauen` oder Änderungen an Windows-Teilen, Erhebungsplan, Snapshot-Schema oder Projektdateien. Die Hash-Sperre des Sammlers ist Pflicht (kein „unset“) und gilt auch in `collector.yml`; eine schon veröffentlichte Sammler-Version mit anderem Hash bricht den Build ab. Ein gescheiterter Sammler-Lauf ohne Adminrechte macht den Job rot. Ein endgültiges Release aus einem privaten Repo bricht ab (ohne Herkunftsnachweis). Die Syntax aller PowerShell-Schritte und -Skripte wird bei jedem Lauf geprüft. Actions: `checkout` 7.0.1, `setup-dotnet` 6.0.0, `upload-artifact` 7.0.2, `attest-build-provenance` 4.2.2 (Node 24, per Commit-SHA gepinnt). Dependabot schlägt den festen Compiler des Sammlers und .NET-Hauptversionen nicht mehr vor.
- **Tests:** Anonymisierung der aufgezeichneten Fixtures, Anonymisierer und die Fehlercodes von Core und Befehlen laufen auch unter Linux; Tests mit RuleTool und Anonymisierer scheitern in der CI, statt still übersprungen zu werden.
- **Doku:** öffentliche Dateien ohne Verweise auf die interne Spezifikation, die internen Richtlinien und interne Dateien; Release-Schritte für das erste öffentliche Release; Kopfvermerk der es/fr/zh-Hans-Sprachdateien ohne veraltete Hinweise.

### Bekannte Einschränkung
- **Labortests auf echten Rechnern sind offen vor dem ersten Kundenpiloten** auf OT- oder validierten Rechnern, wie das Signieren: V5 (Start von einer UNC-Freigabe unter CLR 2, Server 2008 R2 ohne 3.5.1), Starts auf Windows 7, 8.1, Server 2008 R2 und 2012 R2, echtes AppLocker/App Control, Domänen-GPO, Drittanbieter-Virenschutz und EDR, die Linux-arm64-Befehlszeile zur Laufzeit (`docs/limitations.md`).
- Die Programme sind noch nicht signiert.
- Spanisch, Französisch und Chinesisch sind maschinell vorübersetzt und noch nicht von Muttersprachlern geprüft.
- Der Sammler (0.1.0, unverändert) lässt bei einem Schreibfehler nach dem Anlegen (Stick voll) eine leere oder halbe `*.snapshot.json` ohne `.sha256` zurück, obwohl er „3 nichts geschrieben“ meldet. Die Behebung braucht eine neue Sammler-Version; bis dahin steht es in `docs/limitations.md`.

### English

First public release. It contains everything planned as 0.1.3 (never published) and the fixes after it. Collector unchanged (0.1.0, the same bytes since v0.1.0), rule catalogue 2026.10.1.

#### Fixed
- **Groups:** a new group could get the ID of a deleted one again (`g-` + count), and machines as well as approved exceptions and tailoring of the deleted group then silently applied to the new one. New IDs are now above every ID used in the plant, the machines and the registers (case-insensitive).
- **Importing several exports:** each machine is evaluated in collection order, not in the order of the arguments or of the selection in the file dialog. Before, a newer export ahead of an older one of the same machine in one import got the wrong previous run, with wrong regressions, new rules and "newly high".
- **Comparison with the approved state:** "exception removed" (also when only requested or changed after approval) and "tailoring removed" followed by "not met" count as worse (token `regression`), like the flags on the result.
- **Run flag `clockSuspect`:** the same clock check as the evaluation (one day of tolerance, next lower UBR, facts rather than only the header). Before, a run could say "clock implausible" while the age rules were evaluated normally, and the other way round. `verify` of evidence packs from 0.1.x may report a difference in the run flags in such edge cases.
- **The app while work is running:** navigation and pages are locked, also for the keyboard, shortcuts and dropped files; "running …" stays until the last piece of work ends. Before, a second write to the plant folder could be started in parallel.
- **"Check this PC":** the hand-over folder with the export is also deleted when collection, hand-over or import fail; Riegel cleans up older leftovers at start. When collection fails in the elevated process, RGL-0115 now names the exit code and the cause.
- **`collect-builtin.cmd`** changes to `%SystemRoot%\System32` before the first program call and sets `PATH` and `PATHEXT`. Before, a foreign `reg.bat` or `find.exe` in the current folder (stick, share) could have run with administrator rights.
- **Comparison:** an export that cannot be read right now (share gone, file locked) is reported on the page instead of leaving a half-reset state.
- **Demo:** every generation gets its own output folder again (the fixed demo clock put everything into the same one); within the same second with "-2", "-3" …
- **Jumping to an exception** while a search is active shows the entry (the search is reset) instead of an empty detail.
- **Import dialog:** a folder without exports (RGL-0470) or a folder that cannot be read is reported in the dialog instead of being ignored silently.

#### Read-only proof
- **Red runs on `windows-2025` explained:** Windows itself starts VSS and swprv on every Server 2025 runner 11 to 13 minutes after boot; `PrintJobCleanupTask` disables itself after its first run. The collector started VSS in none of 21 runs. Evidence and rules: `docs/collector.md` ("How the proof tells the collector from Windows").
- **Stricter:** every change of a service's running state that Windows does not make by design is measured again (every target three more times, then 20 s of watching); any repeated deviation and any service that does not return fails the proof. Control: a stand-in target that starts VSS on every call passed the old proof and fails the new one.
- Changes by Windows are excused only with a record: changed scheduled tasks only from a fixed list and with the originator from the Security log, firewall rules only with the firewall service or Defender as originator in the Firewall log, the start mode of BITS through the System log, new Defender preferences only with a platform update in the same interval.

#### Changed
- **The publisher is ICT Baden GmbH** (as in the Impressum): copyright, licence, MSI manufacturer, bill of materials and the file properties of the programs. The *About* page names the publisher and links to the Impressum, the privacy notice (`https://ict-baden.de/datenschutz/riegel/`) and the product page (`https://ict-baden.de/produkte/riegel/`); so does `--info`. Collector 0.1.0 keeps "ICT-Baden" in its file properties so that its bytes (hash, AppLocker data) stay the same.
- **Releases:** the notes of a tag release are the English part of the changelog section with the collector hash and the list of files. README, documentation, `THIRD-PARTY-NOTICES.txt` and RGL-0130 name the files of tag releases with their version (`Riegel-Collector-<version>.zip` …); a test keeps the examples current.
- **MSI:** the programs carry the file version with the build number (`<version>.<run>`, e.g. `1.0.0.<run>`), also in the tag release. A release is therefore newer than every `latest` build before it, and Windows Installer reliably replaces the files on an update. CI also tests the MSI as an update over `latest` and over the newest tag release.
- **CI:** pushes and pull requests build only the platform-neutral part on Linux; the Windows build, the packages and `latest` come nightly (when `main` has changed), for tags, when started by hand, with `[voll]` in the commit message and for pull requests with the label `voll-bauen` or changes to Windows parts, the collection plan, the snapshot schema or project files. The collector's hash lock is mandatory (no "unset") and also applies in `collector.yml`; a collector version already published with another hash fails the build. A failed collector run without administrator rights turns the job red. A final release from a private repository stops (no build provenance). The syntax of every PowerShell step and script is checked on every run. Actions: `checkout` 7.0.1, `setup-dotnet` 6.0.0, `upload-artifact` 7.0.2, `attest-build-provenance` 4.2.2 (Node 24, pinned by commit SHA). Dependabot no longer proposes the collector's fixed compiler or .NET major versions.
- **Tests:** the anonymisation check of the recorded fixtures, the anonymiser and the error codes of Core and the commands also run on Linux; tests with RuleTool and the anonymiser fail in CI instead of being skipped silently.
- **Documentation:** public files without references to the internal specification, internal guidelines and internal files; release steps for the first public release; the header note of the es/fr/zh-Hans language files without outdated remarks.

#### Known limitations
- **Lab tests on real machines are open before the first customer pilot** on OT or validated machines, like code signing: V5 (start from a UNC share under CLR 2, Server 2008 R2 without 3.5.1), starts on Windows 7, 8.1, Server 2008 R2 and 2012 R2, real AppLocker/App Control, domain GPO, third-party antivirus and EDR, the Linux arm64 command line at run time (`docs/limitations.md`).
- The programs are not code-signed yet.
- Spanish, French and Chinese are machine pre-translations, not yet reviewed by native speakers.
- The collector (0.1.0, unchanged) leaves an empty or partial `*.snapshot.json` without `.sha256` behind after a write error once the file exists (stick full), although it reports "3 nothing written". The fix needs a new collector version; until then it is listed in `docs/limitations.md`.

## [0.1.2] – 2026-10-21

Release-Bereinigung. Sammler unverändert (0.1.0, dieselben Bytes), Regelwerk 2026.10.1.

### Regelwerk 2026.10.1
- **NET-25** (nicht authentifizierte RPC-Clients einschränken) gilt nicht mehr für Domänencontroller; der OT-Hinweis nennt die Folgen auf Servern (OPC DA, ältere Sicherungs- und Überwachungsagenten), empfiehlt „Authentifiziert“ (1) im Wartungsfenster und rät von „Authentifiziert ohne Ausnahmen“ (2) ab.

### Behoben
- **Nachweispaket:** mit `report --format json --evidence` lagen die Läufe zusätzlich zu `runs/<host>/` flach in der Wurzel des Pakets. Jetzt nur noch unter `runs/`, wie in `docs/evidence.md` beschrieben.

### Geändert
- Spanisch, Französisch und Chinesisch: die als unsicher markierten Einträge (je 170) geprüft und bereinigt, Begriffe vereinheitlicht. Die Sprachauswahl nennt diese drei Sprachen „maschinelle Übersetzung“ (in der Sprache selbst); Gegenlesen durch Muttersprachler steht weiter aus.
- Impressum unter `https://ict-baden.de/impressum/`, Webseite `https://ict-baden.de/produkte/riegel/` (Info-Seite, `--info`).
- README: Download führt auf das neueste Tag-Release; `latest` ist als Vorabversion für Tests genannt, nicht für Anlagen.
- CI: alle Actions per Commit-SHA gepinnt; Release-Texte auf Englisch mit Liste der Dateien.

### English

Release clean-up. Collector unchanged (0.1.0, same bytes), rule catalogue 2026.10.1.

#### Rule catalogue 2026.10.1
- **NET-25** (restrict unauthenticated RPC clients) no longer applies to domain controllers; the OT note names the consequences on servers (OPC DA, older backup and monitoring agents), recommends "Authenticated" (1) in a maintenance window and advises against "Authenticated without exceptions" (2).

#### Fixed
- **Evidence pack:** with `report --format json --evidence` the runs were also stored flat in the root of the pack, besides `runs/<host>/`. Now only below `runs/`, as `docs/evidence.md` describes.

#### Changed
- Spanish, French and Chinese: the entries marked as uncertain (170 each) were reviewed and cleaned up, terms made consistent. The language selection calls these three languages "machine translation" (in the language itself); a review by native speakers is still pending.
- Impressum at `https://ict-baden.de/impressum/`, website `https://ict-baden.de/produkte/riegel/` (*About* page, `--info`).
- README: the download link leads to the newest tag release; `latest` is named as a pre-release for testing, not for plants.
- CI: all actions pinned by commit SHA; release notes in English with the list of files.

## [0.1.1] – 2026-10-21

Korrekturen nach der zweiten unabhängigen Prüfung (2026-10-20). Die Sammler-Exe ist unverändert (0.1.0, dieselben Bytes, Hash-Sperre unverändert; im Sammlerpaket ändert sich nur README.txt), das Regelwerk bleibt 2026.10. 0.1.1 ersetzt 0.1.0 vor allem, weil 0.1.0 noch `Microsoft.Windows.SDK.NET.dll` ausliefert.

### Nachweis und verify
- **verify vergleicht den gepackten Katalog mit dem eingebauten** bzw. mit der veröffentlichten Fassung seiner Version. Bisher meldete verify „reproduziert“ (Exitcode 0) für ein Paket, dessen Katalog zusammen mit den Ergebnissen und allen Prüfsummen geändert war, weil die erneute Auswertung den gepackten Katalog nimmt. Jetzt: bekannte Version, anderer SHA-256 → `RGL-0572`, abweichend (Exitcode 2); eine Version, die diese Riegel-Version nicht kennt → `RGL-0573`, neuer Status `catalogueUnknown` („nicht nachgeprüft“, Exitcode 1). verify nennt Katalog-ID, Version und SHA-256 (Text und JSON: `catalogue`, `catalogueBuiltIn`, `catalogueReleased`).
- **Eine Katalogversion gehört zu genau einem Inhalt.** Riegel führt den SHA-256 jeder veröffentlichten Katalogversion; ein Test verlangt bei jeder Änderung am Regelwerk eine neue Version (`JJJJ.MM` oder `JJJJ.MM.N`). So prüft eine neuere Riegel-Version auch Pakete älterer Versionen nach, statt sie nach der nächsten Regeländerung als gefälscht zu melden.
- **verify rechnet Zählungen und Härtungsgrad auch mit einer Kunden-Baseline ohne Inhalt nach** (Voreinstellung). Bisher ging eine geschönte Zusammenfassung in einem solchen Paket als „reproduziert“ durch. Eine Baseline im Manifest mit ungültiger oder reservierter ID (etwa „riegel“) macht das Manifest ungültig (`RGL-0555`), und Ergebnisse einer Baseline ohne Inhalt müssen deren Regel-ID `IMP-<id>-…` tragen (`RGL-0560`) – sonst ließen sich die eigenen Ergebnisse als „nicht nachprüfbar“ dem Vergleich entziehen.
- Doku: was `compare-snapshot` nicht prüft (es wertet nicht neu aus).

### Behoben
- **Register-Vorlagen ohne Pflichtfelder** (z. B. ohne `scope`) werden beim Import mit `RGL-0004` (Datei, Eintrag, Feld) abgelehnt. Bisher wurden sie übernommen und machten die Anlage unbrauchbar (RGL-0001 in list, validate, report …, UNKNOWN bei check, Oberfläche ließ sich nicht öffnen). Register und Anlagen, in denen solche Einträge schon stehen, öffnen wieder; der Eintrag gilt als ungültig (auch `null` als Eintrag). Unlesbare Register und Läufe nennen die Datei.
- **Linux-Befehlszeile ohne OpenSSL oder ICU** (z. B. `debian:12-slim`): statt Abbruch mit Exitcode 134 eine klare Meldung (`RGL-0018`, Exitcode 3) bzw. ein Hinweis (`RGL-0017`, Betrieb ohne Kulturdaten). Die nötigen Pakete stehen in `docs/installation.md`; die CI prüft das in `debian:12-slim`.
- **Freigabe nur für gültige Einträge:** `exceptions/tailoring approve` und die Oberfläche lehnen Einträge ab, die auch freigegeben noch Fehler haben (`RGL-0448`); `list` zeigt sie als „invalid“.
- **`--machine` wird geprüft** (report, remediate, measures add): ein unbekannter Rechner ergibt `RGL-0452` mit der Liste der bekannten statt eines leeren Berichts mit Exitcode 0.
- **Wiederholter Import** desselben Ordners überspringt Exporte, die schon mit Lauf in der Anlage liegen (keine doppelten Läufe, Importnachweise und Alarme); `import --reevaluate` wertet sie erneut aus. Ist nichts neu, gibt `--brief` trotzdem eine Zeile aus (`RIEGEL OK - <Anlage>: no new exports …`), JSON hat `"import": null` und `skipped`.
- Berichtsseite: mit „Einstellungen in der Anlage speichern“ entstand der Bericht über alle Rechner im Standardordner statt für die Auswahl.
- „Diesen PC prüfen“ löscht den Übergabeordner (`%TEMP%\Riegel-Erfassung-…`, `handoff\<token>`) nach dem Import; der Info-Text nennt ihn.
- Export-Leser: fehlende Pflichtfelder und einzelne Ersatzzeichen ergeben `RGL-0257` statt RGL-0001.
- Start neben einer Instanz mit Administratorrechten stürzt nicht mehr ab und endet nicht still, sondern mit einem Hinweis; Prüfermodus lehnt Nachbarordner wie „Linie3-Berichte“ nicht mehr ab.
- Die Oberfläche importiert einen Ordner mit Exporten wie die Befehlszeile (bisher RGL-0260); Ziehen und Ablegen nimmt ihn an und liest dabei jeden Ordner nur bis zum ersten Export (ein unlesbarer Ordner ergibt keine Fehlermeldung mehr).
- PDF-Schriften werden auch unter macOS gefunden (Entwicklung, Prüfer).

### Geändert
- Windows-Projekte zielen auf `net10.0-windows` statt `net10.0-windows10.0.19041.0`. Damit liegt `Microsoft.Windows.SDK.NET.dll` (Lizenz des Windows SDK, nicht MIT) nicht mehr in den Windows-Paketen; Riegel nutzt keine WinRT-API. Offener Punkt legal-4 aus der Prüfung damit erledigt.
- Sammler als net35-Exe, Anlagenordner als JSON und Regeltexte im Katalog sind als Abweichungen von den Richtlinien freigegeben.
- Läufe eines Rechners werden beim Auflisten nur noch mit ihren Kopfangaben gelesen (bei vielen Läufen etwa dreimal schneller).
- Impressum unter `https://ict-baden.de/index.html?page=imprint` (die bisherige Adresse lieferte 404).
- Doku: Linux-Pakete, Inhalt des Gruppenrichtlinienergebnisses im Export (von `--pseudonymize` nicht verändert), „ausgelegt für / getestet auf“ statt unbelegter Zusagen zu Windows-Versionen, englische Zitate in der englischen Doku, Basisversion nach jedem Tag anheben, Sprach-Badge „en, de (+ es, fr, zh preview)“ statt „5 languages“, jede Änderung am Regelwerk mit neuer Katalogversion (`docs/development.md`).
- CI: Zeitlimit für den Schritt „AppLocker-Daten des Sammlers“ (hing einmal 33 Minuten); Dependabot stößt keine Rebase-Läufe mehr an; Test der Linux-Befehlszeile in `debian:12-slim`.

### English

Corrections after the second independent review (2026-10-20). The collector executable is unchanged (0.1.0, same bytes, hash lock unchanged; in the collector package only README.txt changes), the rule catalogue stays 2026.10. 0.1.1 replaces 0.1.0 mainly because 0.1.0 still shipped `Microsoft.Windows.SDK.NET.dll`.

#### Evidence and verify
- **verify compares the packed catalogue with the built-in one** or with the published version of its release. A known version with a different SHA-256 → `RGL-0572`, deviating (exit code 2); a version this Riegel release does not know → `RGL-0573`, new status `catalogueUnknown` ("not re-checked", exit code 1). verify names catalogue ID, version and SHA-256.
- **A catalogue version belongs to exactly one content.** Riegel keeps the SHA-256 of every published catalogue version; a test requires a new version (`YYYY.MM` or `YYYY.MM.N`) for every change to the rules.
- **verify recomputes counts and hardening score also with a customer baseline without content** (default). A baseline with an invalid or reserved ID makes the manifest invalid (`RGL-0555`); results of a baseline without content must carry its rule ID `IMP-<id>-…` (`RGL-0560`).

#### Fixed
- **Register templates without mandatory fields** are rejected on import with `RGL-0004` (file, entry, field) instead of making the plant unusable; plants that already contain such entries open again, the entry counts as invalid.
- **Linux command line without OpenSSL or ICU** (e.g. `debian:12-slim`): a clear message (`RGL-0018`, exit code 3) or a notice (`RGL-0017`) instead of an abort with exit code 134; CI checks this in `debian:12-slim`.
- **Approval only for valid entries** (`RGL-0448`); **`--machine` is checked** (`RGL-0452`); **a repeated import** skips exports that are already in the plant (`import --reevaluate` re-evaluates them).
- *Reports* page, *Check this PC* hand-over folder, export reader (`RGL-0257`), start next to an elevated instance, the auditor mode, importing a folder in the app and by drag and drop, PDF fonts on macOS.

#### Changed
- Windows projects target `net10.0-windows` instead of `net10.0-windows10.0.19041.0`, so `Microsoft.Windows.SDK.NET.dll` (Windows SDK licence, not MIT) is no longer in the Windows packages; Riegel uses no WinRT API.
- The collector as a net35 executable, the plant folder as JSON and the rule texts in the catalogue are approved deviations from the ICT-Baden guidelines.
- Listing the runs of a machine reads only their headers (about three times faster with many runs).
- Documentation: Linux packages, the Group Policy result in the export, "designed for / tested on" instead of unproven claims about Windows versions, raising the base version after every tag, every rule change with a new catalogue version.
- CI: time limit for the AppLocker step, no Dependabot rebase runs, test of the Linux command line in `debian:12-slim`.

## [0.1.0] – 2026-10-19

Erste Version. Sammler 0.1.0, Regelwerk 2026.10 (153 Regeln). Noch nicht signiert; kein Kundenpilot auf OT- oder validierten Rechnern mit dieser Version.

Vor der Veröffentlichung unabhängig geprüft: vier Prüfer ohne Entwicklungswissen (feindliche Eingaben und Sicherheit, falsches „erfüllt“ und Nachweislogik, Lizenzen, Richtlinien und Aussagen, Doku, Oberfläche und Texte), jeder Befund von einem zweiten Prüfer gegengeprüft. **35 Befunde, 35 bestätigt, 0 verworfen, 34 behoben.** Bei zweien bleibt ein Teil, der eine neue Sammler-Version braucht (wirksamer Zustand der Firewall, Kernel-DMA-Schutz der Firmware; [Grenzen](docs/limitations.md)). Offen als Entscheidung der Eigentümer: `Microsoft.Windows.SDK.NET.dll` (Lizenzbedingungen des Windows SDK) in den Windows-Paketen – sie entfällt nur mit dem Zielframework `net10.0-windows`, das Richtlinie 02 nicht vorsieht.

### Neu
- **Portabler Sammler** `Riegel.Collector.exe` (net35, läuft unter CLR 2 und CLR 4, rund 130 KB, bytegleich gebaut, Hash-Sperre in der CI): liest nur, was `contracts/collection-plan.json` nennt, schreibt nur seinen Export und die Prüfsummendatei; `--minimal`, `--deep`, `--pseudonymize`, `--gzip`; Exitcodes 0/1/3; Meldungen in fünf Sprachen. Bordmittel-Weg `collect-builtin.cmd` für Rechner ohne fremde Programme. Nur-Lesen-Nachweis und AppLocker-Daten für jeden Build.
- **Regelwerk 2026.10** mit 153 Regeln in 17 Kategorien, eigene Texte in Englisch und Deutsch, Sollwerte belegt durch ACSC, die Microsoft-Server-Dokumentation (CC BY 4.0), Microsoft-Learn-Fakten oder DISA; Lebenszyklus- und Build-Tabellen, Fähigkeitstabellen, Erkennungslisten für Fernwartungswerkzeuge und Office; Rollenvorlagen für den Zuschnitt und eine Ausnahmen-Vorlage.
- **Auswertung ohne falsches „erfüllt“:** geschlossenes Prüfvokabular mit 27 Prüfarten, Belegklassen aus Einträgen und Abdeckung, Laufzeit vor Konfiguration, Standardwerte nur mit Quelle („Standardwert angenommen“), Defender-Sperre, Herkunft der Werte (Domänen-GPO experimentell), Testvektoren für jede Regel, Mutationstest und ein unabhängiges Orakel.
- **Export-Leser** mit allen Grenzen für feindliche Eingaben, Prüfsummen-Abgleich, erneutem Schwärzen und Neuberechnung von `parsed:*`; Leser für Bordmittel-Pakete; Parser für secedit, Registry.pol, gpresult und Firewall-Regeln; **Kunden-Baselines** aus GPO-Sicherungen, `riegel.baseline/1` und CSV-Vorlage.
- **Anlagenordner** mit Läufen, Import- und Erhebungsprotokollen, freigegebenem Stand und den Registern **Ausnahmen**, **Zuschnitt** und **Maßnahmenplan** (Freigabe an den Inhalt gebunden, unveränderlicher Verlauf, Vorlagen); Vergleich mit dem vorigen Lauf und dem freigegebenen Stand, Abweichungen der Rohwerte, Stichprobe; Wertung mit Härtungsgrad, Dokumentiert, Prüfabdeckung und Deckel für die Überwachungsregeln.
- **Dokumente:** Anlagenbericht und Rechnerberichte zum Abzeichnen (Voreinstellung *Auslieferungszustand* für Maschinenbauer), Management-Übersicht, Soll-Konfiguration, Ausnahmen zur Genehmigung, Erhebungsprotokoll; Excel-Arbeitsmappe mit Versicherer-Fakten, CSV, Nachweispaket mit `verify` und `compare-snapshot`, Abhilfe-Dateien (`.reg` mit Rücknahme, `GptTmpl.inf`, `audit.csv`, README) – nur als Dateien, nie angewendet.
- **Befehlszeile** `Riegel.Cli.exe` (Windows) und `riegel` (Linux x64 und arm64) mit allen Befehlen, Exitcodes nach Nagios-Art, die Veränderungen melden, Icinga-Zeile `--brief`, JSON-Ausgabe, Prüfermodus.
- **Oberfläche** (WPF, hell und dunkel, ganz mit der Tastatur bedienbar, fünf Sprachen) mit allen Seiten, „Diesen PC prüfen“ mit Übergabe bei Rechteerhöhung, Demo-Anlage „Musterwerk – Linie 3“ über die echte Engine, Screenshots, Info-Seite mit Diagnosepaket.
- **MSI** für die Auswerte-App (Intune, GPO), Einzeldatei, Framework-ZIP, Stückliste (CycloneDX), `SHA256SUMS.txt`; CI unter Linux und Windows mit Installationstest der MSI und `check`/`verify` der Linux-Befehlszeile.
- Dokumentation in `docs/` (Englisch), darunter Installation, Sammler mit Änderungsantrag, Auswertung, Datenschutz und Grenzen; `docs/error-codes.md` und `docs/rules.md` werden aus dem Quelltext erzeugt.

### Geändert (gegenüber der Planung, mit Begründung in `docs/rule-sources.md`)
- NET-29, SVC-04, RDP-08 und TLS-06/07/08/11 sind Bestandsregeln statt Prüfungen: keine weitergebbare Quelle legt dort einen Sollwert fest.
- Erhebungsplan um die Schlüssel für AUD-30, ENC-07, LIFE-05, NET-05 und den Rückfall der Dienstprüfung ergänzt; `CurrentVersion` nur noch mit den Versionswerten (nicht RegisteredOwner, ProductId, DigitalProductId).

### Geändert nach der unabhängigen Prüfung
- Lizenzen: die Drittanbieter-Hinweise der mitgelieferten .NET-Laufzeit und von WPF (Stand v10.0.13) liegen jetzt als `THIRD-PARTY-NOTICES-dotnet-runtime.txt` und `THIRD-PARTY-NOTICES-dotnet-wpf.txt` neben den Programmen und sind in `--licenses` und der Info-Seite enthalten; vorher nur ein Link auf den Zweig main. Die CI vergleicht den Text mit den veröffentlichten Runtime-Paketen.
- Stückliste (CycloneDX): enthält jetzt auch die Pakete von `Riegel.Cli.exe` für Windows (System.CodeDom) und die WiX-Erweiterung `WixToolset.Util.wixext` (MS-RL), deren Custom Action in der MSI steckt.
- Doku: der Sammler „ändert keine Konfiguration, schreibt nur seinen Export, und Windows vermerkt, dass ein Programm lief“ (statt „ändert nichts“), mit Verweis auf die Spuren; data-and-privacy.md nennt Gateway-Adresse, WinHTTP-Proxy und Defender-IP-Ausnahmen im Export.
- Einheitliche Begriffe: der prüfbare Anteil heißt auf Deutsch „Prüfabdeckung“ (Befehlszeile, Excel, PDF, Oberfläche), „Prüfumfang“ bleibt für die Regeln im Zuschnitt, die Änderungsart scopeChanged heißt „Erhebung geändert“; ein Name für das Blatt, „Genehmigungsblatt“ (Kennung GB-…), die Seite „Rechner“, der Prüfermodus „schreibgeschützt“; Spanisch „planta“ und Chinesisch 装置 für die Anlage; das englische PDF sagt „Hardening score“. Die englische Doku zitiert die englischen Texte, das Deutsche in Klammern.
- Sammlerpaket: der deutsche Teil von README.txt mit Umlauten (vorher „Pruefsummendatei“, „aendert“); README.txt und die Hilfe von `collect` sagen „ändert keine Konfiguration“ statt „startet keinen Dienst, ändert nichts“ und verweisen auf die Spuren, die Windows selbst vermerkt.
- Sprachdateien: eine Kennzeichnung der Vorübersetzung für es, fr und zh-Hans – oben in jeder Datei „Maschinell vorübersetzt – TODO-übersetzen“ (wie beim Sammler), besonders unsichere Einträge mit `TODO-übersetzen` in allen drei Sprachen; ein Test hält das einheitlich.

### Behoben nach der unabhängigen Prüfung
- FW-01…06 und FW-11: ein gesetzter Wert der Gruppenrichtlinie (EnableFirewall, DefaultInboundAction) entscheidet vor der lokalen Einstellung, die der Firewall-Dienst (INetFwPolicy2) meldet; eine per GPO abgeschaltete Firewall wurde vorher als „erfüllt“ gemeldet. Nicht lesbarer Richtlinienspeicher → „nicht prüfbar“.
- ACCT-14 (Windows LAPS): wie Windows LAPS gilt der erste Richtlinienbereich mit irgendeiner Einstellung als aktiv (CSP, Gruppenrichtlinie, lokale Konfiguration, altes Microsoft LAPS); ein dort fehlendes BackupDirectory heißt „deaktiviert“. Neue Fähigkeit `windowsLaps` (Windows 11/10 und Server 2019/2022 mit dem Update vom 11.04.2023, Server 2025): auf Windows Server 2016 und älter schlägt die Regel fehl statt „erfüllt“. Dafür `registryEffective` mit `selection: root` und `minUbr` in den Fähigkeitstabellen.
- Bordmittel-Weg: `collect-builtin.cmd` startet sich aus einem 32-Bit-`cmd` über `Sysnative` als 64-Bit-`cmd` neu und vermerkt `wow64` in `exitcodes.txt`; ein fehlendes Winlogon\AutoAdminLogon gilt nur bei `wow64=0` und errorlevel 1 als „nicht gesetzt“. Vorher ergab ein Lauf in der 32-Bit-Ansicht LOGON-10 (hoch) „erfüllt“.
- LIFE-02: ohne Eintrag in der Build-Tabelle (z. B. Windows 7, 8.1, Server 2012 R2) belegt der neueste Hotfix nur noch einen Befund (seit mehr als `updateMaxDays` nichts installiert); ein junges Installationsdatum (auch eines Wartungsstapel- oder .NET-Updates) ergibt „nicht prüfbar“ statt „erfüllt“.
- ASR-08 und DEF-09: die drei ASR-Regeln, die laut Microsoft den Cloud-Schutz brauchen (Verbreitung/Alter, verschleierte Skripte, erweiterter Ransomware-Schutz), sind nur mit MAPSReporting 1 oder 2 „erfüllt“; mit 0 „konfiguriert, aber nicht wirksam“.
- ENC-04: verlangt neben der PIN-Richtlinie eine Schutzvorrichtung TPM und PIN (Typ 4 oder 6) auf dem Betriebssystemlaufwerk (`GetKeyProtectors`); ein früher nur mit dem TPM verschlüsseltes oder unverschlüsseltes Laufwerk war vorher „erfüllt“. Ohne Liste der Schutzvorrichtungen (ohne Admin) „nicht prüfbar“.
- ENC-06: die gesetzte DMA-Richtlinie („Block all“) heißt jetzt „konfiguriert, Wirkung nicht nachgewiesen“ (nicht prüfbar), weil sie nur mit Kernel-DMA-Schutz der Firmware wirkt und Riegel diesen Zustand nicht erhebt; vorher „erfüllt“. Dafür `configuredOnly` an Registry-Prüfungen.
- CRED-17: mit RestrictRemoteSamAuditOnlyMode = 1 (Nur-Überwachen-Modus, jeder Aufruf wird zugelassen) schlägt die Regel fehl; vorher „erfüllt“ mit dem Standard-SDDL.
- SVC-01: verlangt den Starttyp „Deaktiviert“ der Druckwarteschlange statt nur „beendet“; eine beendete Warteschlange mit Starttyp Automatisch war „erfüllt“, startet aber beim nächsten Neustart wieder. Auch auf Bordmittel-Paketen damit prüfbar.
- AUD-23: der lokale Wert der Protokollgröße zählt nur, wo die Größe der Gruppenrichtlinie nachweislich nicht gesetzt ist; ein nicht lesbarer Richtlinienschlüssel ergibt „nicht prüfbar“ statt „erfüllt“ aus dem lokalen Wert. Der Mutationstest prüft jetzt auch den Vorrang zwischen Konfigurationsquellen.
- Prüfabdeckung: wird auf ganze Prozent abgeschnitten statt gerundet; 89,5 % erschienen vorher als 90 % ohne Hinweis „Unvollständige Prüfung“, ohne Graustufe und ohne Lauf-Flag `Incomplete`.
- Defender ohne Admin: meldet die WSC-API Microsoft Defender als eingeschaltet, wird aus den Platzhaltern von MSFT_MpComputerStatus (eingeschränktes Token) nicht mehr „aus“ hergeleitet; DEF-01/02/12, LIFE-04 und ASR-xx sind dann „nicht prüfbar“ statt „kein aktiver Virenschutz“.
- docs/rules.md nennt bei allen DISA-Verweisen die STIG-Kennungen (V-ID und Regel-ID); bei 33 Regeln stand vorher nur „DISA: “.
- Konsolenprogramm: `import <Ordner>` nimmt die Exporte eines Ordners auf (wie im README, vorher RGL-0260), und `tailoring/exceptions import-template <Name>` erreicht die eingebauten Vorlagen (`hmi`, `engineering-station`, `historian-server`, `jump-host`), die in keinem Paket als Datei liegen; neue Codes RGL-0470 und RGL-0471.
- Mehrzahl in Oberfläche, Konsolenprogramm und Registermeldungen über `_one`/`_other` nach den Regeln der Sprache (vorher „1 findings“, „1 exports added“, „1 errors“, „in 1 days“); Daten in den Meldungen der Registerprüfung folgen in der Oberfläche dem Gebietsschema, in JSON bleiben sie ISO 8601.
- Entwicklerdoku: der Befehl zum Neuerzeugen von docs/error-codes.md schließt den Fehlercode-Test ein.
- Oberfläche: Listenspalten werden nie schmaler als ihr Kopf in der gewählten Sprache („Geltungsbereich“, „Verantwortlich (Rolle)“, „Responsable (función)“ wurden abgeschnitten); die Layout-Prüfung der Screenshots meldet abgeschnittene Spaltenköpfe.
- Zuschnitt: die Schaltflächen der Rollenvorlagen zeigen den Rollennamen in der Sprache der Oberfläche (z. B. „Bedienstation (HMI)“) statt des Dateinamens („hmi“, „jump-host“).

### Sicherheit
- Geheimnisse werden beim Erfassen und beim Import geschwärzt (`DefaultPassword` immer); SNMP-Community-Namen werden ersetzt; kein Netzwerkzugriff in App, Befehlszeile und Sammler; feindliche Eingaben (Exporte, Pakete, Register, Baselines, Nachweis-ZIPs, Logos) mit Größen- und Tiefengrenzen; erzeugte Dateien (`.reg`, CSV, Excel) gegen Einschleusung gehärtet.
- Rücknahme (`remediate`), Vergleich, erneute Auswertung und die Vergleichsbasis eines Laufs lesen den Export eines gespeicherten Laufs nur noch aus dem Anlagenordner (kein absoluter Pfad, kein UNC-Pfad, kein „..“, keine Verknüpfung) und nur mit dem SHA-256 des Laufs (`RGL-0415`, `RGL-0416`). Vorher konnte ein manipulierter Lauf eine Rücknahme aus dem Export eines anderen Rechners oder von außerhalb der Anlage erzeugen.
- Lizenz: `PdfSharp.WPFonts.dll` (sechs Segoe-WP-Schriften von Microsoft, „All Rights Reserved“, ohne Recht zur Weitergabe) sowie `PdfSharp.Snippets.dll` und `PdfSharp.Quality.dll` werden nicht mehr ausgeliefert; die Drittanbieter-Hinweise nannten die Schriften fälschlich MIT.
- Protokoll: Pfade (auch aus IO-Fehlermeldungen), Rechner-, Benutzer- und Domänennamen, Konten, SIDs und Adressen werden vor dem Schreiben ersetzt; vorher standen Benutzerprofil- und Anlagenordner-Namen im Protokoll.

### English

First version. Collector 0.1.0, rule catalogue 2026.10 (153 rules). Not code-signed; no customer pilot on OT or validated machines with this version. Superseded by 0.1.1: the 0.1.0 packages contained `Microsoft.Windows.SDK.NET.dll`, which is not under the MIT licence, and were removed.

Reviewed independently before publication: four reviewers without development knowledge, every finding cross-checked by a second reviewer. **35 findings, 35 confirmed, 34 fixed**; two leave a part that needs a new collector version (effective firewall state, firmware Kernel DMA Protection; [Limitations](docs/limitations.md)).

#### New
- **Portable collector** `Riegel.Collector.exe` (net35, runs on CLR 2 and CLR 4, about 130 KB, deterministic build, hash lock in CI): reads only what `contracts/collection-plan.json` lists and writes only its export and the checksum file; `--minimal`, `--deep`, `--pseudonymize`, `--gzip`; messages in five languages. `collect-builtin.cmd` for machines where no foreign program may run. Read-only proof and AppLocker data for every build.
- **Rule catalogue 2026.10** with 153 rules in 17 categories, own texts in English and German, expected values backed by ACSC, the Microsoft server documentation (CC BY 4.0), Microsoft Learn facts or DISA.
- **Evaluation without a false "pass":** a closed check vocabulary, evidence classes from items and coverage, runtime before configuration, defaults only with a source ("Default value assumed"), test vectors for every rule, a mutation test and an independent oracle.
- **Snapshot reader** with limits for hostile input, checksum check, redaction and recomputation of `parsed:*`; readers for built-in-command bundles, secedit, Registry.pol, gpresult and firewall rules; **customer baselines** from GPO backups, `riegel.baseline/1` and CSV.
- **Plant folder** with runs, import and collection records, approved state and the registers **exceptions**, **tailoring** and **measures** (approval bound to the content, immutable history, templates); comparison with the previous run and the approved state; hardening score, documented share and coverage.
- **Documents:** plant and machine reports to sign, management summary, target configuration, exceptions for approval, collection record; Excel workbook, CSV, evidence pack with `verify` and `compare-snapshot`, remediation files (`.reg` with undo, `GptTmpl.inf`, `audit.csv`) – only as files, never applied.
- **Command line** `Riegel.Cli.exe` (Windows) and `riegel` (Linux x64 and arm64) with Nagios-style exit codes that report changes, an Icinga line (`--brief`), JSON output and an auditor mode.
- **App** (WPF, light and dark, fully keyboard-operable, five languages), *Check this PC* with hand-over on elevation, demo plant "Musterwerk – Linie 3".
- **MSI** for the analysis app (Intune, GPO), single file, framework ZIP, SBOM (CycloneDX), `SHA256SUMS.txt`; documentation in `docs/`.

#### Fixed after the independent review
- Firewall, Windows LAPS, built-in-command bundles from a 32-bit `cmd`, update age, ASR rules that need cloud protection, BitLocker TPM and PIN, DMA protection, remote SAM audit-only mode, Print Spooler start type, event log size and the rounding of coverage no longer give a false "pass".
- Defender without administrator rights, STIG IDs in `docs/rules.md`, `import <folder>`, built-in register templates, plural forms, column widths and role names in the app.

#### Security
- Secrets are redacted on collection and import; no network access in app, command line and collector; hostile input with size and depth limits; generated files hardened against injection.
- Undo, comparison and re-evaluation read a stored run's export only from the plant folder and only with the run's SHA-256 (`RGL-0415`, `RGL-0416`).
- `PdfSharp.WPFonts.dll` (fonts without a right to redistribute) is no longer shipped; the log replaces paths, machine, user and domain names, accounts, SIDs and addresses.
