# SPDX-License-Identifier: MIT
# Erzeugt hostile-registry.snapshot.json: ein synthetischer Export (riegel.snapshot/1) mit feindlichen Registry-Werten
# für die Abhilfe-Tests (spec 7.12, 8.8): Anführungszeichen, Backslashes, Zeilenumbrüche, NUL, "$()", sehr lange
# Texte, falsche Typen, geschwärzte und abgeschnittene Werte sowie Wertnamen, die eine .reg-Datei sprengen würden.
# Alle Angaben sind erfunden. Aufruf: python -I generate-hostile.py  (schreibt neben das Skript, bytegleich)
import json
import os

LANMAN = r"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters"
WINLOGON = r"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
SYSTEM_POLICIES = r"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
LSA = r"HKLM\SYSTEM\CurrentControlSet\Control\Lsa"
POLICIES_TEST = r"HKLM\SOFTWARE\Policies\Riegel-Test"
DENIED = r"HKLM\SOFTWARE\Policies\Riegel-Test\Gesperrt"


def reg(values, subkeys=None):
    return {"values": values, "subkeys": subkeys or []}


def item(key, status="ok", value=None, **extra):
    d = {"key": "reg:" + key, "status": status}
    if value is not None:
        d["value"] = value
    d["durationMs"] = 1
    d.update(extra)
    return d


snapshot = {
    "schema": "riegel.snapshot/1",
    "collector": {"name": "Riegel.Collector", "version": "0.1.0", "clr": "4.0.30319.42000", "is64BitProcess": True},
    "collectedAtUtc": "2026-10-15T08:00:00Z",
    "isAdmin": True,
    "origin": "synthetic",
    "options": {"minimal": False, "deep": False, "pseudonymized": False},
    "machine": {
        "hostName": "HMI-FEIND-01", "domainRole": 0, "productType": 1, "build": 17763, "ubr": 7792,
        "editionId": "EnterpriseS", "installationType": "Client", "architecture": "AMD64",
        "machineGuid": "00000000-0000-4000-8000-0000000000ff", "osLanguage": "de-DE",
    },
    "coverage": [
        {"root": r"HKLM\SOFTWARE\Policies", "recursive": True, "maxDepth": 8, "complete": True, "deniedSubkeys": [DENIED]},
        {"root": r"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies", "recursive": True, "maxDepth": 8, "complete": True, "deniedSubkeys": []},
        {"root": LSA, "recursive": True, "maxDepth": 8, "complete": True, "deniedSubkeys": []},
        {"root": LANMAN, "recursive": False, "maxDepth": 0, "complete": True, "deniedSubkeys": []},
        {"root": WINLOGON, "recursive": False, "maxDepth": 0, "complete": True, "deniedSubkeys": []},
    ],
    "items": [
        item(LANMAN, value=reg({
            "RequireSecuritySignature": {"t": "dword", "v": 0},
            # Falscher Typ: DWORD als Text mit angehängtem Befehl
            "EnableSecuritySignature": {"t": "dword", "v": "1; reg add HKLM\\Evil /f"},
            # Wertname, der in einer .reg-Datei einen neuen Schlüssel eröffnen würde (nicht im Katalog)
            "\"]\r\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\Evil]\r\n\"x": {"t": "sz", "v": "evil"},
        })),
        item(WINLOGON, value=reg({
            # Anführungszeichen, Zeilenumbruch und ein eingeschleuster Schlüssel
            "AutoAdminLogon": {"t": "sz", "v": "1\"\r\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\Evil]\r\n\"x\"=\"y"},
            # Geschwärzt bei der Erfassung
            "DefaultPassword": {"t": "sz", "redacted": True, "empty": False},
            # Harmlos, aber mit Backslash, Anführungszeichen und $() – muss exakt zurückkommen
            "LegalNoticeCaption": {"t": "sz", "v": "C:\\Temp\\\"q\" $(Invoke-Expression 'calc') `whoami` %PATH%"},
            # NUL mitten im Text
            "LegalNoticeText": {"t": "sz", "v": "vorher\u0000nachher"},
            # Sehr lang (über der Grenze von 16.384 Zeichen)
            "Shell": {"t": "sz", "v": "explorer.exe " + "A" * 100000},
            # Abgeschnitten bei der Erfassung
            "Userinit": {"t": "sz", "v": "C:\\Windows\\system32\\userinit.exe,", "truncated": True},
            # Unicode, Zeilen-/Absatztrenner
            "ShutdownWithoutLogon": {"t": "sz", "v": "a\u2028b"},
            "PowerdownAfterShutdown": {"t": "sz", "v": "Grüße – 中文 ✓"},
        })),
        item(SYSTEM_POLICIES, value=reg({
            "EnableLUA": {"t": "dword", "v": 0},
            "ConsentPromptBehaviorAdmin": {"t": "qword", "v": "18446744073709551615"},
            "LegalNotice": {"t": "expand_sz", "v": "%SystemRoot%\\system32\\\"x\".txt"},
            "DisableCAD": {"t": "binary", "v": "AAECAwQFBgcICQ=="},
            "NoConnectedUser": {"t": "unknown", "v": None},
        })),
        item(LSA, value=reg({
            "Security Packages": {"t": "multi_sz", "v": ["kerberos", "msv1_0", "schannel", "wdigest", "tspkg", "pku2u"]},
            "Notification Packages": {"t": "multi_sz", "v": ["scecli", "mit\nZeilenumbruch"]},
            "Authentication Packages": {"t": "multi_sz", "v": ["msv1_0", ""]},
            "LmCompatibilityLevel": {"t": "dword", "v": 3},
        })),
        item(POLICIES_TEST, value=reg({"Existing": {"t": "dword", "v": 7}}, ["Gesperrt"])),
        # Unterschlüssel ohne Leserecht: alter Wert unbekannt
        item(DENIED, status="denied", errorCode=5, error="Access denied"),
        # Nachweislich nicht vorhandener Schlüssel
        item(r"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", status="keyMissing"),
        {"key": "api:netuserenum", "status": "ok", "durationMs": 1, "value": [
            {"name": "Administrator", "sid": "S-1-5-21-1000-2000-3000-500", "rid": 500, "disabled": True, "passwordNotRequired": False,
             "passwordNeverExpires": False, "lockedOut": False, "lastLogonDate": None, "passwordAgeDays": 10},
            {"name": "Gast", "sid": "S-1-5-21-1000-2000-3000-501", "rid": 501, "disabled": True, "passwordNotRequired": True,
             "passwordNeverExpires": True, "lockedOut": False, "lastLogonDate": None, "passwordAgeDays": 0},
        ]},
    ],
}

here = os.path.dirname(os.path.abspath(__file__))
with open(os.path.join(here, "hostile-registry.snapshot.json"), "w", encoding="utf-8", newline="\n") as f:
    f.write(json.dumps(snapshot, ensure_ascii=True, indent=2) + "\n")
