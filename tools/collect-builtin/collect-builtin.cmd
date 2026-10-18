@echo off
rem SPDX-License-Identifier: MIT
rem ---------------------------------------------------------------------------------------------
rem Riegel - Erhebung nur mit Windows-Bordmitteln (spec 7.5), fuer Rechner, auf denen kein fremdes
rem Programm laufen darf, und fuer Windows XP/Vista/2008. Startet nur Programme von Windows
rem (secedit, auditpol, reg, hostname, ver, find, optional wmic oder powershell fuer das Datum) und
rem schreibt nur in den neuen Ordner <HOST>_<yyyyMMdd>.riegel-builtin neben dieser Datei oder
rem unter dem Ordner im ersten Argument. Liest nur: es setzt und konfiguriert nichts und fuegt keine
rem Registry-Werte hinzu.
rem Jeder Befehl landet mit seinem errorlevel in exitcodes.txt.
rem
rem Aufruf:   collect-builtin.cmd [Zielordner]
rem Exitcode: 0 alles gelesen, 1 geschrieben, aber unvollstaendig, 3 nichts geschrieben
rem Dieselben Befehle zum Abtippen: docs/collector.md, Abschnitt "Built-in commands".
rem ---------------------------------------------------------------------------------------------
setlocal EnableExtensions DisableDelayedExpansion

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=%~dp0"
if "%TARGET:~-1%"=="\" set "TARGET=%TARGET:~0,-1%"

rem Datum yyyyMMdd unabhaengig von der Sprache: wmic, sonst PowerShell, sonst %DATE% (Ersatz)
set "STAMP="
set "DATESOURCE=wmic"
for /f "tokens=2 delims==" %%A in ('wmic os get LocalDateTime /value 2^>nul ^| find "="') do set "LDT=%%A"
if defined LDT set "STAMP=%LDT:~0,8%"
if defined STAMP goto havedate
set "DATESOURCE=powershell"
for /f %%A in ('powershell -NoProfile -NonInteractive -Command "Get-Date -Format yyyyMMdd" 2^>nul') do set "STAMP=%%A"
if defined STAMP goto havedate
set "DATESOURCE=fallback"
set "STAMP=%DATE: =%"
set "STAMP=%STAMP:/=%"
set "STAMP=%STAMP:.=%"
set "STAMP=%STAMP:-=%"
:havedate

set "BUNDLE=%TARGET%\%COMPUTERNAME%_%STAMP%.riegel-builtin"
if exist "%BUNDLE%\" goto exists
mkdir "%BUNDLE%" 2>nul
if not exist "%BUNDLE%\" goto notwritable
mkdir "%BUNDLE%\reg" 2>nul
set "CODES=%BUNDLE%\exitcodes.txt"
set "FAILED=0"
>"%CODES%" echo date=%DATESOURCE%
if not exist "%CODES%" goto notwritable

echo Riegel built-in collection (read-only) / Bordmittel-Erhebung (nur lesend)
echo Folder / Ordner: %BUNDLE%

rem Windows XP und Server 2003 (Version 5.x) kennen kein "auditpol /backup"
set "OLDWIN=0"
ver | find /i "Version 5." >nul
if not errorlevel 1 set "OLDWIN=1"

hostname >"%BUNDLE%\hostname.txt" 2>nul
call :rec hostname %ERRORLEVEL%
ver >"%BUNDLE%\ver.txt" 2>nul
call :rec ver %ERRORLEVEL%

rem Sicherheitsvorlage; /log haelt %windir%\security\logs\scesrv.log unberuehrt
secedit /export /cfg "%BUNDLE%\secedit.inf" /log "%BUNDLE%\secedit.log" /quiet
call :rec secedit %ERRORLEVEL% required

if "%OLDWIN%"=="1" goto noauditpol
auditpol /backup /file:"%BUNDLE%\auditpol.csv" >nul 2>&1
call :rec auditpol %ERRORLEVEL% required
goto regexports
:noauditpol
>>"%CODES%" echo auditpol=skipped

:regexports
reg export "HKLM\SOFTWARE\Policies" "%BUNDLE%\reg\01-policies.reg" >nul 2>&1
call :rec reg-01 %ERRORLEVEL%
reg export "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies" "%BUNDLE%\reg\02-currentversion-policies.reg" >nul 2>&1
call :rec reg-02 %ERRORLEVEL%
reg export "HKLM\SYSTEM\CurrentControlSet\Control\Lsa" "%BUNDLE%\reg\03-lsa.reg" >nul 2>&1
call :rec reg-03 %ERRORLEVEL%
reg export "HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders" "%BUNDLE%\reg\04-securityproviders.reg" >nul 2>&1
call :rec reg-04 %ERRORLEVEL%
reg export "HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters" "%BUNDLE%\reg\05-lanmanserver.reg" >nul 2>&1
call :rec reg-05 %ERRORLEVEL%
reg export "HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters" "%BUNDLE%\reg\06-lanmanworkstation.reg" >nul 2>&1
call :rec reg-06 %ERRORLEVEL%
reg export "HKLM\SYSTEM\CurrentControlSet\Services\NetBT\Parameters" "%BUNDLE%\reg\07-netbt.reg" >nul 2>&1
call :rec reg-07 %ERRORLEVEL%
reg export "HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy" "%BUNDLE%\reg\08-firewallpolicy.reg" >nul 2>&1
call :rec reg-08 %ERRORLEVEL%
reg export "HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server" "%BUNDLE%\reg\09-terminalserver.reg" >nul 2>&1
call :rec reg-09 %ERRORLEVEL%
reg export "HKLM\SYSTEM\CurrentControlSet\Services\USBSTOR" "%BUNDLE%\reg\10-usbstor.reg" >nul 2>&1
call :rec reg-10 %ERRORLEVEL%
reg export "HKLM\SYSTEM\CurrentControlSet\Services\mrxsmb10" "%BUNDLE%\reg\11-mrxsmb10.reg" >nul 2>&1
call :rec reg-11 %ERRORLEVEL%
reg export "HKLM\SYSTEM\CurrentControlSet\Control\ProductOptions" "%BUNDLE%\reg\12-productoptions.reg" >nul 2>&1
call :rec reg-12 %ERRORLEVEL%
reg export "HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" "%BUNDLE%\reg\13-tcpip.reg" >nul 2>&1
call :rec reg-13 %ERRORLEVEL%

rem CurrentVersion nur mit den Versionswerten (Build, UBR, Edition, Installationstyp), einzeln abgefragt:
rem "reg export" waere rekursiv und enthielte Winlogon mit DefaultPassword im Klartext; RegisteredOwner,
rem ProductId und DigitalProductId braucht Riegel nicht. Fehlt ein Wert (aeltere Versionen), zaehlt das nicht.
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v CurrentBuild >"%BUNDLE%\currentversion.txt" 2>nul
call :rec currentversion %ERRORLEVEL% required
for %%V in (CurrentBuildNumber CurrentMajorVersionNumber CurrentMinorVersionNumber CurrentVersion UBR EditionID InstallationType DisplayVersion ReleaseId ProductName CSDVersion) do reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v %%V >>"%BUNDLE%\currentversion.txt" 2>nul

rem Dienste: nur der Wert Start jedes Dienstes - der ganze Services-Baum enthielte Zugangsdaten von
rem Herstellerdiensten und SNMP-Communities
reg query "HKLM\SYSTEM\CurrentControlSet\Services" /s /v Start >"%BUNDLE%\services-start.txt" 2>nul
call :rec services-start %ERRORLEVEL% required

rem Winlogon: drei Werte einzeln; von DefaultPassword nur "present" oder "absent", nie der Wert
set "WL=HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
reg query "%WL%" /v AutoAdminLogon >"%BUNDLE%\winlogon.txt" 2>nul
call :rec winlogon-autoadminlogon %ERRORLEVEL%
reg query "%WL%" /v DefaultUserName >>"%BUNDLE%\winlogon.txt" 2>nul
call :rec winlogon-defaultusername %ERRORLEVEL%
reg query "%WL%" /v DefaultDomainName >>"%BUNDLE%\winlogon.txt" 2>nul
call :rec winlogon-defaultdomainname %ERRORLEVEL%
reg query "%WL%" /v DefaultPassword >nul 2>&1 && (>>"%BUNDLE%\winlogon.txt" echo DefaultPassword=present) || (>>"%BUNDLE%\winlogon.txt" echo DefaultPassword=absent)
>>"%CODES%" echo winlogon-defaultpassword=0

echo.
echo Done / Fertig: %BUNDLE%
if "%FAILED%"=="0" (
  echo Complete / Vollstaendig.
  endlocal & exit /b 0
)
echo Incomplete: see exitcodes.txt (secedit and auditpol need "Run as administrator").
echo Unvollstaendig: siehe exitcodes.txt (secedit und auditpol brauchen "Als Administrator ausfuehren").
endlocal & exit /b 1

rem ---- errorlevel eines Schritts festhalten (Name, Code, "required" = Pflichtschritt) ----
rem reg export/query liefern 1 sowohl fuer "Schluessel fehlt" als auch fuer "verweigert": nur die
rem Pflichtschritte (secedit, auditpol, CurrentVersion, Dienste) machen die Erhebung unvollstaendig.
:rec
>>"%CODES%" echo %~1=%~2
if "%~3"=="required" if not "%~2"=="0" set "FAILED=1"
exit /b 0

:exists
echo RGL-0106: The folder already exists / Der Ordner existiert schon: %BUNDLE%
endlocal & exit /b 3

:notwritable
echo RGL-0102: Target not writable / Ziel nicht beschreibbar: %TARGET%
endlocal & exit /b 3
