# Mitmachen / Contributing

Danke für Ihr Interesse! Thank you for your interest!

## Vor größeren Änderungen / Before larger changes

Bitte erst ein Issue anlegen und den Plan kurz beschreiben. So vermeiden wir doppelte Arbeit.
Please open an issue first and describe your plan.

Änderungen am Erhebungsplan (`contracts/collection-plan.json`) und am Sammler brauchen eine eigene Versionsnummer des Sammlers und eine Begründung, was gelesen wird und warum – der Sammler läuft auf Rechnern, die nicht verändert werden dürfen.
Changes to the collection plan or the collector need a new collector version and a reason for every item read.

## Pull Requests

- Ein Thema pro Pull Request, mit Tests.
- `dotnet build Riegel.sln` und `dotnet test Riegel.sln` müssen grün sein; unter Linux `dotnet test tests/Riegel.Tests -f net10.0`. Einzelheiten in [`docs/development.md`](docs/development.md).
- **Tests nur mit Fixtures oder lesenden Abfragen.** Kein Test und kein Skript darf die Konfiguration des Rechners ändern (kein `secedit /configure`, `auditpol /set`, `reg add` unter Richtlinienschlüsseln, `Set-MpPreference`, Firewall-, Dienst-, Feature-, BitLocker- oder Benutzerrechte-Änderungen, `gpupdate`, LGPO).
- Regeln: Sollwerte nur mit einer Quelle, die wir ausliefern dürfen (ACSC, Microsoft-Server-Doku unter CC BY 4.0, Microsoft-Learn-Fakten, DISA). **Keine Inhalte aus CIS Benchmarks, BSI SiSyPHuS oder IT-Grundschutz-Bausteinen**, keine Dateien oder Werte aus Microsoft Security Baselines.
- Keine neuen Abhängigkeiten ohne Rücksprache. Nur freizügige Lizenzen (MIT, BSD, Apache-2.0, ISC), keine GPL/AGPL. Jede neue oder aktualisierte Abhängigkeit gehört mit Version, Quelle, Copyright und Lizenztext in [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt).
- Texte der Oberfläche nur in `.resx` (en, de, es, fr, zh-Hans), Regeltexte im Katalog (en, de).
- Keine Passwörter, internen Hostnamen oder Kundendaten in Code, Tests, Fixtures oder Screenshots. Demo-Daten ohne echte Personennamen.

## Developer Certificate of Origin (DCO)

Mit jedem Commit bestätigen Sie, dass Sie den Beitrag unter der Lizenz dieses Projekts (MIT) einbringen dürfen ([developercertificate.org](https://developercertificate.org/)). Dafür jeden Commit mit `-s` erstellen:

By contributing you certify the [DCO](https://developercertificate.org/). Sign off every commit:

```
git commit -s -m "Fehler beim Lesen leerer Dateien behoben"
```

Das fügt `Signed-off-by: Name <E-Mail>` hinzu.
