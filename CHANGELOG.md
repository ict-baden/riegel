# Änderungen

Alle wichtigen Änderungen an Riegel. Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [Semantic Versioning](https://semver.org/lang/de/).

## [Unveröffentlicht]

### Sicherheit
- Rücknahme (`remediate`), Vergleich, erneute Auswertung und die Vergleichsbasis eines Laufs lesen den Export eines gespeicherten Laufs nur noch aus dem Anlagenordner (kein absoluter Pfad, kein UNC-Pfad, kein „..“, keine Verknüpfung) und nur mit dem SHA-256 des Laufs (`RGL-0415`, `RGL-0416`). Vorher konnte ein manipulierter Lauf eine Rücknahme aus dem Export eines anderen Rechners oder von außerhalb der Anlage erzeugen.

### Behoben
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

## [0.1.0] – 2026-10-18

Erste Version. Sammler 0.1.0, Regelwerk 2026.10 (153 Regeln). Noch nicht signiert; kein Kundenpilot auf OT- oder validierten Rechnern mit dieser Version.

### Neu
- **Portabler Sammler** `Riegel.Collector.exe` (net35, läuft unter CLR 2 und CLR 4, rund 130 KB, bytegleich gebaut, Hash-Sperre in der CI): liest nur, was `contracts/collection-plan.json` nennt, schreibt nur seinen Export und die Prüfsummendatei; `--minimal`, `--deep`, `--pseudonymize`, `--gzip`; Exitcodes 0/1/3; Meldungen in fünf Sprachen. Bordmittel-Weg `collect-builtin.cmd` für Rechner ohne fremde Programme. Nur-Lesen-Nachweis und AppLocker-Daten für jeden Build.
- **Regelwerk 2026.10** mit 153 Regeln in 17 Kategorien, eigene Texte in Englisch und Deutsch, Sollwerte belegt durch ACSC, die Microsoft-Server-Dokumentation (CC BY 4.0), Microsoft-Learn-Fakten oder DISA; Lebenszyklus- und Build-Tabellen, Fähigkeitstabellen, Erkennungslisten für Fernwartungswerkzeuge und Office; Rollenvorlagen für den Zuschnitt und eine Ausnahmen-Vorlage.
- **Auswertung ohne falsches „erfüllt“:** geschlossenes Prüfvokabular mit 27 Prüfarten, Belegklassen aus Einträgen und Abdeckung, Laufzeit vor Konfiguration, Standardwerte nur mit Quelle („Standardwert angenommen“), Defender-Sperre, Herkunft der Werte (Domänen-GPO experimentell), Testvektoren für jede Regel, Mutationstest und ein unabhängiges Orakel.
- **Export-Leser** mit allen Grenzen aus spec 7.12, Prüfsummen-Abgleich, erneutem Schwärzen und Neuberechnung von `parsed:*`; Leser für Bordmittel-Pakete; Parser für secedit, Registry.pol, gpresult und Firewall-Regeln; **Kunden-Baselines** aus GPO-Sicherungen, `riegel.baseline/1` und CSV-Vorlage.
- **Anlagenordner** mit Läufen, Import- und Erhebungsprotokollen, freigegebenem Stand und den Registern **Ausnahmen**, **Zuschnitt** und **Maßnahmenplan** (Freigabe an den Inhalt gebunden, unveränderlicher Verlauf, Vorlagen); Vergleich mit dem vorigen Lauf und dem freigegebenen Stand, Abweichungen der Rohwerte, Stichprobe; Wertung mit Härtungsgrad, Dokumentiert, Prüfumfang und Deckel für die Überwachungsregeln.
- **Dokumente:** Anlagenbericht und Rechnerberichte zum Abzeichnen (Voreinstellung *Auslieferungszustand* für Maschinenbauer), Management-Übersicht, Soll-Konfiguration, Ausnahmen zur Genehmigung, Erhebungsprotokoll; Excel-Arbeitsmappe mit Versicherer-Fakten, CSV, Nachweispaket mit `verify` und `compare-snapshot`, Abhilfe-Dateien (`.reg` mit Rücknahme, `GptTmpl.inf`, `audit.csv`, README) – nur als Dateien, nie angewendet.
- **Befehlszeile** `Riegel.Cli.exe` (Windows) und `riegel` (Linux x64 und arm64) mit allen Befehlen aus spec 7.7, Exitcodes nach Nagios-Art, die Veränderungen melden, Icinga-Zeile `--brief`, JSON-Ausgabe, Prüfermodus.
- **Oberfläche** (WPF, hell und dunkel, ganz mit der Tastatur bedienbar, fünf Sprachen) mit allen Seiten, „Diesen PC prüfen“ mit Übergabe bei Rechteerhöhung, Demo-Anlage „Musterwerk – Linie 3“ über die echte Engine, Screenshots, Info-Seite mit Diagnosepaket.
- **MSI** für die Auswerte-App (Intune, GPO), Einzeldatei, Framework-ZIP, Stückliste (CycloneDX), `SHA256SUMS.txt`; CI unter Linux und Windows mit Installationstest der MSI und `check`/`verify` der Linux-Befehlszeile.
- Dokumentation in `docs/` (Englisch), darunter Installation, Sammler mit Änderungsantrag, Auswertung, Datenschutz und Grenzen; `docs/error-codes.md` und `docs/rules.md` werden aus dem Quelltext erzeugt.

### Geändert (gegenüber der Planung, mit Begründung in `docs/rule-sources.md`)
- NET-29, SVC-04, RDP-08 und TLS-06/07/08/11 sind Bestandsregeln statt Prüfungen: keine weitergebbare Quelle legt dort einen Sollwert fest.
- Erhebungsplan um die Schlüssel für AUD-30, ENC-07, LIFE-05, NET-05 und den Rückfall der Dienstprüfung ergänzt; `CurrentVersion` nur noch mit den Versionswerten (nicht RegisteredOwner, ProductId, DigitalProductId).

### Sicherheit
- Geheimnisse werden beim Erfassen und beim Import geschwärzt (`DefaultPassword` immer); SNMP-Community-Namen werden ersetzt; kein Netzwerkzugriff in App, Befehlszeile und Sammler; feindliche Eingaben (Exporte, Pakete, Register, Baselines, Nachweis-ZIPs, Logos) mit Größen- und Tiefengrenzen; erzeugte Dateien (`.reg`, CSV, Excel) gegen Einschleusung gehärtet.
