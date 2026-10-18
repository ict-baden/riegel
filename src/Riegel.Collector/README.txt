Riegel collector / Riegel-Sammler
=================================

English below. Deutsch weiter unten.

STATUS: work in progress. This build of Riegel.Collector.exe is a placeholder: it prints its
version and help and collects nothing yet.

What it is
----------
Riegel.Collector.exe reads the security configuration of this Windows machine and writes one
snapshot file (*.snapshot.json) next to itself or into the folder you name. It installs nothing,
starts no service, changes no setting and opens no network connection. Evaluate the snapshot on
another PC with Riegel (Riegel-x64.msi, Riegel.exe or riegel-win-x64.exe).

Runs on Windows 7 SP1 to Windows 11 and Server 2008 R2 to 2025, with .NET Framework 4.x or 3.5.

Before you start: is .NET Framework there?
------------------------------------------
Open a command prompt and run:

    reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP"

The collector can start if the output lists a key "v4" (with "Full" below it) or "v3.5"
(on Windows 7 often together with v2.0.50727). If the key does not exist or lists neither,
do not use the collector on this machine: use the built-in commands instead
(collect-builtin.cmd, or the same commands typed by hand; see docs/collector.md in the
Riegel repository). Do not install .NET on a validated machine just for Riegel.

Check the files
---------------
SHA256SUMS.txt lists the SHA-256 of every file in this package. On Windows 7 and later:

    certutil -hashfile Riegel.Collector.exe SHA256

Commands
--------
    Riegel.Collector.exe --version
    Riegel.Collector.exe --help

Licence: MIT (LICENSE). Third-party notices: THIRD-PARTY-NOTICES.txt.
Project: https://github.com/ict-baden/riegel


Deutsch
=======

STAND: in Arbeit. Dieser Build von Riegel.Collector.exe ist ein Platzhalter: Er zeigt Version und
Hilfe und erhebt noch nichts.

Was er macht
------------
Riegel.Collector.exe liest die Sicherheitskonfiguration dieses Windows-Rechners und schreibt eine
Exportdatei (*.snapshot.json) neben sich oder in den angegebenen Ordner. Er installiert nichts,
startet keinen Dienst, ändert keine Einstellung und baut keine Netzwerkverbindung auf. Ausgewertet
wird auf einem anderen PC mit Riegel (Riegel-x64.msi, Riegel.exe oder riegel-win-x64.exe).

Läuft auf Windows 7 SP1 bis Windows 11 und Server 2008 R2 bis 2025, mit .NET Framework 4.x oder 3.5.

Vorher prüfen: Ist .NET Framework vorhanden?
--------------------------------------------
In einer Eingabeaufforderung:

    reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP"

Der Sammler kann starten, wenn die Ausgabe einen Schlüssel "v4" (darunter "Full") oder "v3.5"
nennt (unter Windows 7 oft zusammen mit v2.0.50727). Fehlt der Schlüssel oder steht keiner von
beiden darin, den Sammler auf diesem Rechner nicht verwenden, sondern die Bordmittel
(collect-builtin.cmd oder dieselben Befehle von Hand, siehe docs/collector.md im Riegel-Repo).
Auf einem validierten Rechner nicht eigens für Riegel .NET installieren.

Dateien prüfen
--------------
SHA256SUMS.txt nennt den SHA-256 jeder Datei dieses Pakets. Ab Windows 7:

    certutil -hashfile Riegel.Collector.exe SHA256

Befehle
-------
    Riegel.Collector.exe --version
    Riegel.Collector.exe --hilfe

Lizenz: MIT (LICENSE). Hinweise zu Software Dritter: THIRD-PARTY-NOTICES.txt.
Projekt: https://github.com/ict-baden/riegel
