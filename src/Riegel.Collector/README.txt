Riegel collector / Riegel-Sammler
=================================

English below. Deutsch weiter unten.

What it is
----------
Riegel.Collector.exe reads the security configuration of this Windows machine and writes one
snapshot file (*.snapshot.json) and its checksum file (*.sha256) next to itself or into the
folder you name. It installs nothing, changes no configuration and opens no network connection;
what Windows itself records is listed under "What it leaves behind". Evaluate the snapshot on
another PC with Riegel (Riegel-x64.msi, Riegel.exe or riegel-win-x64.exe).

Designed for Windows 7 SP1 to Windows 11 and Server 2008 R2 to 2025, with the .NET Framework
that ships with Windows (4.x or 3.5). Tested so far on Windows Server 2022 and 2025 and on
Windows 11; Windows 7, 8.1, Server 2008 R2 and 2012 R2 are not yet tested on real machines.
Keep Riegel.Collector.exe.config next to the exe.

Before you start: is .NET Framework there?
------------------------------------------
Open a command prompt and run:

    reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP"

The collector can start if the output lists a key "v4" (with "Full" below it) or "v3.5"
(on Windows 7 often together with v2.0.50727). If the key does not exist or lists neither,
do not use the collector on this machine: use the built-in commands instead
(collect-builtin.cmd). Do not install .NET on a validated machine just for Riegel.
Server 2008 R2: .NET 3.5.1 is a feature there; whether the collector starts without it is not
verified yet - check with the command above.

Check the files
---------------
SHA256SUMS.txt lists the SHA-256 of every file in this package. On Windows 7 and later:

    certutil -hashfile Riegel.Collector.exe SHA256

How to run
----------
1. Open a command prompt with "Run as administrator" (without administrator rights about half
   of the areas are marked "not checkable").
2. Change to this folder and run:

       Riegel.Collector.exe

   If this drive is write-protected: Riegel.Collector.exe --out \\server\share\folder
3. Note the export path and the SHA-256 it prints. Take both files back for the evaluation.

Options: --out <folder>, --gzip, --minimal (start no other program), --deep (also the LSASS
protection level), --pseudonymize (user names as pseudonyms), --quiet, --nur-lesen, --version,
--help.
Exit codes: 0 complete, 1 written but incomplete, 3 nothing written.

What it leaves behind
---------------------
It changes no configuration and writes only its export and the checksum file. Windows itself
records that a program ran: temporary files of secedit and gpresult in a fresh
%TEMP%\riegel-<random> folder (deleted afterwards), a CLR usage log, execution traces
(Prefetch, AmCache) and possibly Security log events (4688, 4798, 4799, privilege use).
gpresult may make Windows start the Group Policy Client service on demand (--minimal avoids
it). Tell your SOC before a collection round.

Built-in commands
-----------------
collect-builtin.cmd [folder] collects with Windows' own programs only (secedit, auditpol, reg)
into a new folder <HOST>_<yyyyMMdd>.riegel-builtin. Use it where no foreign program may run and
on Windows XP, Vista and Server 2003/2008 (best effort, not tested on those systems). The bundle
is confidential; delete it after import.

Documentation: https://github.com/ict-baden/riegel/blob/main/docs/collector.md
Licence: MIT (LICENSE). Third-party notices: THIRD-PARTY-NOTICES.txt.
Project: https://github.com/ict-baden/riegel


Deutsch
=======

Was er macht
------------
Riegel.Collector.exe liest die Sicherheitskonfiguration dieses Windows-Rechners und schreibt eine
Exportdatei (*.snapshot.json) und ihre Prüfsummendatei (*.sha256) neben sich oder in den
angegebenen Ordner. Er installiert nichts, ändert keine Konfiguration und baut keine
Netzwerkverbindung auf; was Windows selbst vermerkt, steht unter „Was er hinterlässt“.
Ausgewertet wird auf einem anderen PC mit Riegel (Riegel-x64.msi, Riegel.exe oder
riegel-win-x64.exe).

Ausgelegt für Windows 7 SP1 bis Windows 11 und Server 2008 R2 bis 2025, mit dem .NET Framework,
das Windows mitbringt (4.x oder 3.5). Getestet bisher auf Windows Server 2022 und 2025 und auf
Windows 11; Windows 7, 8.1, Server 2008 R2 und 2012 R2 sind auf echten Rechnern noch nicht
getestet. Riegel.Collector.exe.config neben der Exe lassen.

Vorher prüfen: Ist .NET Framework vorhanden?
--------------------------------------------
In einer Eingabeaufforderung:

    reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP"

Der Sammler kann starten, wenn die Ausgabe einen Schlüssel „v4“ (darunter „Full“) oder „v3.5“
nennt (unter Windows 7 oft zusammen mit v2.0.50727). Fehlt der Schlüssel oder steht keiner von
beiden darin, den Sammler auf diesem Rechner nicht verwenden, sondern die Bordmittel
(collect-builtin.cmd). Auf einem validierten Rechner nicht eigens für Riegel .NET installieren.
Server 2008 R2: Dort ist .NET 3.5.1 ein Feature; ob der Sammler ohne es startet, ist noch nicht
geprüft – mit dem Befehl oben prüfen.

Dateien prüfen
--------------
SHA256SUMS.txt nennt den SHA-256 jeder Datei dieses Pakets. Ab Windows 7:

    certutil -hashfile Riegel.Collector.exe SHA256

Aufruf
------
1. Eingabeaufforderung mit „Als Administrator ausführen“ öffnen (ohne Administratorrechte
   wird etwa die Hälfte der Bereiche als „nicht prüfbar“ markiert).
2. In diesen Ordner wechseln und aufrufen:

       Riegel.Collector.exe

   Ist dieser Datenträger schreibgeschützt: Riegel.Collector.exe --out \\Server\Freigabe\Ordner
3. Pfad und SHA-256 notieren, die er ausgibt. Beide Dateien zur Auswertung mitnehmen.

Optionen: --out <Ordner>, --gzip, --minimal (kein anderes Programm starten), --deep (zusätzlich
die LSASS-Schutzstufe), --pseudonymize (Benutzernamen als Pseudonyme), --quiet, --nur-lesen,
--version, --help.
Exitcodes: 0 vollständig, 1 geschrieben, aber unvollständig, 3 nichts geschrieben.

Was er hinterlässt
------------------
Er ändert keine Konfiguration und schreibt nur seinen Export und die Prüfsummendatei. Windows
selbst vermerkt, dass ein Programm lief: temporäre Dateien von secedit und gpresult in einem
frischen Ordner %TEMP%\riegel-<zufällig> (danach gelöscht), ein CLR-Nutzungsprotokoll,
Ausführungsspuren (Prefetch, AmCache) und möglicherweise Ereignisse im Sicherheitsprotokoll
(4688, 4798, 4799, Rechteverwendung). gpresult kann dazu führen, dass Windows den Dienst
Gruppenrichtlinienclient bei Bedarf startet (--minimal vermeidet das). Vor einer Erhebungsrunde
das SOC informieren.

Bordmittel
----------
collect-builtin.cmd [Ordner] erhebt nur mit Windows-eigenen Programmen (secedit, auditpol, reg)
in einen neuen Ordner <HOST>_<yyyyMMdd>.riegel-builtin. Für Rechner, auf denen kein fremdes
Programm laufen darf, und für Windows XP, Vista und Server 2003/2008 (ohne Gewähr, auf diesen
Systemen nicht getestet). Das Paket ist vertraulich; nach dem Import löschen.

Dokumentation: https://github.com/ict-baden/riegel/blob/main/docs/collector.md
Lizenz: MIT (LICENSE). Hinweise zu Software Dritter: THIRD-PARTY-NOTICES.txt.
Projekt: https://github.com/ict-baden/riegel
