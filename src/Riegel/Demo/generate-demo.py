# Erzeugt die eingebetteten Exporte der Demo-Anlage „Musterwerk – Linie 3“ (spec 7.9) unter src/Riegel/Demo/snapshots.
# Grundlage sind die synthetischen Fixtures des Gerüsts (tests/Riegel.Tests/Fixtures/synthetic): je Rechner zwei
# Erhebungen, damit der Vergleich eine Verschlechterung und Abweichungen zeigt. Alles ist erfunden: Rechnernamen,
# Konten, SIDs (S-1-5-21-1000-2000-3000-<rid>), Domäne musterwerk.example (RFC 2606), Hersteller „Beispiel-Anlagenbau“.
# Ausgabe wird committet. Aufruf (aus dem Repo-Stamm): python src/Riegel/Demo/generate-demo.py .
import copy, hashlib, json, os, sys

repo = sys.argv[1] if len(sys.argv) > 1 else "."
fixtures = os.path.join(repo, "tests", "Riegel.Tests", "Fixtures", "synthetic")
out = os.path.join(repo, "src", "Riegel", "Demo", "snapshots")
os.makedirs(out, exist_ok=True)
for name in os.listdir(out):
    if name.endswith(".snapshot.json"):
        os.remove(os.path.join(out, name))

CCS = "SYSTEM\\CurrentControlSet"
MACHINE_SID = "S-1-5-21-1000-2000-3000"
ORDER = ["key", "status", "value", "errorCode", "error", "durationMs"]


def load(name):
    with open(os.path.join(fixtures, name + ".snapshot.json"), encoding="utf-8") as f:
        return json.load(f)


def find(snap, key):
    for it in snap["items"]:
        if it["key"].lower() == key.lower():
            return it
    return None


def put(snap, key, status="ok", value=None, error=None, ms=1):
    """Eintrag setzen oder anlegen (Reihenfolge der Felder wie im Vertrag)."""
    it = find(snap, key)
    if it is None:
        it = {"key": key}
        snap["items"].append(it)
    it.clear()
    it["key"] = key
    it["status"] = status
    if value is not None:
        it["value"] = value
    if error is not None:
        it["error"] = error
    it["durationMs"] = ms


def drop(snap, prefix):
    snap["items"] = [it for it in snap["items"] if not it["key"].lower().startswith(prefix.lower())]


def reg_values(snap, key):
    return find(snap, key)["value"]["values"]


def rows(snap, key):
    return find(snap, key)["value"]["rows"]


def dword(v): return {"t": "dword", "v": v}
def sz(v): return {"t": "sz", "v": v}


def guid_for(host):
    return "00000000-0000-4000-8000-0000" + hashlib.sha256(("demo:" + host).encode()).hexdigest()[:8]


def rename(snap, host, domain=None):
    """Hostname, MachineGuid und Hostname-Werte auf den Demo-Rechner umstellen."""
    old = snap["machine"]["hostName"]
    m = snap["machine"]
    m["hostName"] = host
    m["machineGuid"] = guid_for(host)
    if domain is not None:
        m["dnsDomain"] = domain
    for row in rows(snap, "wmi:root\\cimv2:Win32_ComputerSystem"):
        row["Name"] = host
        if domain is not None:
            row["Domain"] = domain
    tcpip = find(snap, f"reg:HKLM\\{CCS}\\Services\\Tcpip\\Parameters")
    if tcpip is not None:
        tcpip["value"]["values"]["Hostname"] = sz(host)
    crypto = find(snap, "reg:HKLM\\SOFTWARE\\Microsoft\\Cryptography")
    if crypto is not None:
        crypto["value"]["values"]["MachineGuid"] = sz(guid_for(host))
    group = find(snap, "api:localgroup:S-1-5-32-544")
    if group is not None and isinstance(group.get("value"), list):
        for member in group["value"]:
            if member.get("domain") == old:
                member["domain"] = host


def at(snap, collected, boot):
    snap["collectedAtUtc"] = collected
    snap["machine"]["lastBootUtc"] = boot
    for row in rows(snap, "wmi:root\\cimv2:Win32_OperatingSystem"):
        row["LastBootUpTime"] = boot
    evt = find(snap, "evt:system:wininit-12")
    if evt is not None and evt.get("value") is not None:
        evt["value"]["bootTimeUtc"] = boot
        if evt["value"].get("found"):
            evt["value"]["timeCreatedUtc"] = boot


def updates(snap, *entries):
    put(snap, "wmi:root\\cimv2:Win32_QuickFixEngineering",
        value={"rows": [{"HotFixID": kb, "InstalledOn": date} for kb, date in entries]}, ms=820)


def edition(snap, edition_id, caption, product_name):
    snap["machine"]["editionId"] = edition_id
    for row in rows(snap, "wmi:root\\cimv2:Win32_OperatingSystem"):
        row["Caption"] = caption
    cv = reg_values(snap, "reg:HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion")
    cv["EditionID"] = sz(edition_id)
    cv["ProductName"] = sz(product_name)


def software(snap, version):
    key = "reg:HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\BeispielLeitsystem"
    it = find(snap, key)
    if it is not None:
        it["value"]["values"]["DisplayVersion"] = sz(version)


def service(snap, name, state=None, start=None):
    for row in rows(snap, "wmi:root\\cimv2:Win32_Service"):
        if row["Name"].lower() == name.lower():
            if state is not None: row["State"] = state
            if start is not None: row["StartMode"] = start
            return
    raise KeyError(name)


def cloud_protection_off(snap):
    for row in rows(snap, "wmi:root\\Microsoft\\Windows\\Defender:MSFT_MpPreference"):
        row["MAPSReporting"] = 0
        row["SubmitSamplesConsent"] = 2
    spynet = find(snap, "reg:HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Spynet")
    if spynet is not None:
        spynet["value"]["values"]["SpynetReporting"] = dword(0)


def finish(snap):
    snap["items"] = sorted(snap["items"], key=lambda x: x["key"].lower())
    for it in snap["items"]:
        ordered = {k: it[k] for k in ORDER if k in it}
        it.clear()
        it.update(ordered)
    host = snap["machine"]["hostName"]
    stamp = snap["collectedAtUtc"].replace("-", "").replace(":", "")
    path = os.path.join(out, f"{host}_{stamp}.snapshot.json")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(snap, indent=2, ensure_ascii=False) + "\n")
    print(path, len(snap["items"]))


# ---------------------------------------------------------------- HMI-L3-01: Windows 10 IoT LTSC 2019
# Automatische Anmeldung (Ausnahme), RDP ohne NLA (Maßnahme geplant), Cloud-Schutz aus (Zuschnitt der Rolle HMI)
def hmi(host, collected, boot, smb1=False, extra_admin=False, autologon=True, version="4.2.0", kbs=()):
    s = load("win10-ltsc2019-hmi")
    rename(s, host)
    edition(s, "IoTEnterpriseS", "Microsoft Windows 10 IoT Enterprise LTSC 2019", "Windows 10 IoT Enterprise LTSC 2019")
    at(s, collected, boot)
    cloud_protection_off(s)
    software(s, version)
    updates(s, *kbs)
    if not autologon:
        put(s, "reg:HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon",
            value={"values": {"AutoAdminLogon": sz("0"), "DefaultUserName": sz("operator"), "Shell": sz("explorer.exe")}, "subkeys": []})
    if smb1:
        put(s, "wmi:root\\Microsoft\\Windows\\SMB:MSFT_SmbServerConfiguration.GetConfiguration", value={"out": {
            "EnableSMB1Protocol": True, "EnableSMB2Protocol": True, "RequireSecuritySignature": True, "EnableSecuritySignature": True,
            "EncryptData": False, "RejectUnencryptedAccess": True}})
        put(s, "wmi:root\\cimv2:Win32_OptionalFeature", value={"rows": [
            {"Name": "SMB1Protocol", "InstallState": 1}, {"Name": "SMB1Protocol-Server", "InstallState": 1},
            {"Name": "MicrosoftWindowsPowerShellV2Root", "InstallState": 1}, {"Name": "MicrosoftWindowsPowerShellV2", "InstallState": 1}]}, ms=480)
        reg_values(s, f"reg:HKLM\\{CCS}\\Services\\LanmanServer\\Parameters")["SMB1"] = dword(1)
        service(s, "RemoteRegistry", state="Running", start="Auto")
    if extra_admin:
        group = find(s, "api:localgroup:S-1-5-32-544")
        group["value"].append({"sid": f"{MACHINE_SID}-1002", "name": "wartung", "kind": "user", "domain": host})
        netusers = find(s, "api:netuserenum")["value"]
        netusers.append({"name": "wartung", "sid": f"{MACHINE_SID}-1002", "rid": 1002, "disabled": False, "passwordNotRequired": False,
                         "passwordNeverExpires": True, "lockedOut": False, "lastLogonDate": "2026-10-15", "passwordAgeDays": 9})
        rows(s, "wmi:root\\cimv2:Win32_UserAccount").append({"Name": "wartung", "SID": f"{MACHINE_SID}-1002", "Disabled": False, "Lockout": False,
                                                             "PasswordRequired": True, "PasswordExpires": False})
    return s


finish(hmi("HMI-L3-01", "2026-09-17T07:30:00Z", "2026-09-10T05:00:00Z", kbs=[("KB0000101", "2026-09-10T00:00:00Z"), ("KB0000100", "2026-08-13T00:00:00Z")]))
finish(hmi("HMI-L3-01", "2026-10-16T07:30:00Z", "2026-10-15T05:00:00Z", version="4.2.1",
           kbs=[("KB0000102", "2026-10-14T00:00:00Z"), ("KB0000101", "2026-09-10T00:00:00Z")]))

# ---------------------------------------------------------------- HMI-L3-02: freigegebener Stand, danach Verschlechterung
# Erste Erhebung = freigegebener Stand (SAT). In der zweiten ist SMBv1 wieder an, die Remoteregistrierung läuft und ein
# neues lokales Administratorkonto ist dazugekommen (Verschlechterung gegenüber der Freigabe, Abweichungen der Rohwerte).
finish(hmi("HMI-L3-02", "2026-09-17T07:45:00Z", "2026-09-10T05:10:00Z", autologon=False,
           kbs=[("KB0000101", "2026-09-10T00:00:00Z"), ("KB0000100", "2026-08-13T00:00:00Z")]))
finish(hmi("HMI-L3-02", "2026-10-16T07:45:00Z", "2026-10-15T05:10:00Z", smb1=True, extra_admin=True, autologon=False,
           kbs=[("KB0000102", "2026-10-14T00:00:00Z"), ("KB0000101", "2026-09-10T00:00:00Z")]))


# ---------------------------------------------------------------- ENG-L3-01: Windows 11 24H2, Defender passiv
# Drittanbieter-Virenschutz aktiv, Defender passiv; BitLocker aus (Maßnahme überfällig)
def eng(collected, boot, tool=False):
    s = load("win11-defender-passive-thirdparty-av")
    rename(s, "ENG-L3-01")
    edition(s, "Enterprise", "Microsoft Windows 11 Enterprise", "Windows 11 Enterprise")
    at(s, collected, boot)
    put(s, "wmi:root\\cimv2\\Security\\MicrosoftVolumeEncryption:Win32_EncryptableVolume", value={"rows": [{
        "DriveLetter": "C:", "VolumeType": 0, "ProtectionStatus": 0, "ConversionStatus": 0, "EncryptionMethod": 0,
        "GetConversionStatus": {"ReturnValue": 0, "ConversionStatus": 0, "EncryptionPercentage": 0},
        "GetEncryptionMethod": {"ReturnValue": 0, "EncryptionMethod": 0},
        "GetKeyProtectors": {"ReturnValue": 0, "VolumeKeyProtectorID": [], "KeyProtectorTypes": []},
        "GetProtectionStatus": {"ReturnValue": 0, "ProtectionStatus": 0}}]}, ms=88)
    updates(s, ("KB0000202", "2026-10-14T00:00:00Z" if tool else "2026-09-10T00:00:00Z"), ("KB0000201", "2026-08-13T00:00:00Z"))
    if tool:
        uninstall = find(s, "reg:HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall")
        uninstall["value"]["subkeys"] = ["BeispielEngineering", "BeispielLeitsystem"]
        put(s, "reg:HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\BeispielEngineering",
            value={"values": {"DisplayName": sz("Beispiel-Engineering-Werkzeug"), "DisplayVersion": sz("2.0.0"), "Publisher": sz("Beispiel-Anlagenbau")},
                   "subkeys": [], "valueFilter": ["DisplayName", "DisplayVersion", "Publisher"]})
    return s


finish(eng("2026-09-15T13:05:00Z", "2026-09-14T06:00:00Z"))
finish(eng("2026-10-15T13:05:00Z", "2026-10-14T06:00:00Z", tool=True))


# ---------------------------------------------------------------- HIST-01: Server 2016, veraltete Erfassung
# Supportende nahe; letzte Erhebung im August (bei Auswertung im Oktober veraltet), Updates seit Mai nicht eingespielt
def hist(collected, boot):
    s = load("server2016-historian")
    rename(s, "HIST-01")
    at(s, collected, boot)
    updates(s, ("KB0000301", "2026-05-12T00:00:00Z"), ("KB0000300", "2026-04-14T00:00:00Z"))
    service(s, "Spooler", state="Stopped", start="Disabled")
    return s


finish(hist("2026-07-06T06:15:00Z", "2026-06-20T02:00:00Z"))
finish(hist("2026-08-03T06:15:00Z", "2026-07-20T02:00:00Z"))


# ---------------------------------------------------------------- PG-ALT-01: Windows 7 SP1, Bordmittel-Paket
# Wie Core ein Bordmittel-Paket (collect-builtin.cmd, spec 7.5) ablegt: Herkunft builtin, Abdeckung nur der exportierten
# Schlüssel, Sicherheitstemplate als Rohtext, Überwachung aus auditpol.csv, Dienste nur mit Starttyp. Alles andere fehlt
# (nicht erfasst) – deshalb viele Regeln „nicht prüfbar“. Werte wie im Fixture-Paket PG-ALT-01_20261010.riegel-builtin.
AUDIT = {"Logon": "{0CCE9215-69AE-11D9-BED3-505054503030}", "Logoff": "{0CCE9216-69AE-11D9-BED3-505054503030}",
         "Credential Validation": "{0CCE923F-69AE-11D9-BED3-505054503030}", "User Account Management": "{0CCE9235-69AE-11D9-BED3-505054503030}",
         "Security Group Management": "{0CCE9237-69AE-11D9-BED3-505054503030}", "Process Creation": "{0CCE922B-69AE-11D9-BED3-505054503030}",
         "Audit Policy Change": "{0CCE922F-69AE-11D9-BED3-505054503030}"}

SECEDIT_INF = """[Unicode]
Unicode=yes
[System Access]
MinimumPasswordAge = 0
MaximumPasswordAge = 42
MinimumPasswordLength = 0
PasswordComplexity = 0
PasswordHistorySize = 0
LockoutBadCount = 0
RequireLogonToChangePassword = 0
ForceLogoffWhenHourExpire = 0
NewAdministratorName = "Administrator"
NewGuestName = "Guest"
ClearTextPassword = 0
LSAAnonymousNameLookup = 0
EnableAdminAccount = 1
EnableGuestAccount = 0
[Event Audit]
AuditSystemEvents = 0
AuditLogonEvents = 3
AuditObjectAccess = 0
AuditPrivilegeUse = 0
AuditPolicyChange = 1
AuditAccountManage = 1
AuditProcessTracking = 0
AuditDSAccess = 0
AuditAccountLogon = 1
[Registry Values]
MACHINE\\System\\CurrentControlSet\\Control\\Lsa\\LimitBlankPasswordUse=4,1
MACHINE\\System\\CurrentControlSet\\Control\\Lsa\\NoLMHash=4,0
MACHINE\\System\\CurrentControlSet\\Control\\Lsa\\RestrictAnonymous=4,0
MACHINE\\System\\CurrentControlSet\\Services\\LanmanServer\\Parameters\\RequireSecuritySignature=4,0
MACHINE\\System\\CurrentControlSet\\Services\\LanmanWorkstation\\Parameters\\EnablePlainTextPassword=4,0
[Privilege Rights]
SeNetworkLogonRight = *S-1-1-0,*S-1-5-32-544,*S-1-5-32-545,*S-1-5-32-551
SeDebugPrivilege = *S-1-5-32-544
SeDenyNetworkLogonRight = Guest
SeRemoteInteractiveLogonRight = *S-1-5-32-544,*S-1-5-32-555
[Version]
signature="$CHICAGO$"
Revision=1
"""


def builtin(collected, autorun):
    host = "PG-ALT-01"
    reg = lambda values=None, subkeys=None: {"values": values or {}, "subkeys": subkeys or []}
    pol = "HKLM\\SOFTWARE\\Policies"
    cvp = "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies"
    keys = [
        (pol, reg(subkeys=["Microsoft"])),
        (pol + "\\Microsoft", reg(subkeys=["Windows NT"])),
        (pol + "\\Microsoft\\Windows NT", reg(subkeys=["DNSClient"])),
        (pol + "\\Microsoft\\Windows NT\\DNSClient", reg({"EnableMulticast": dword(0)})),
        (cvp, reg(subkeys=["Explorer", "System"])),
        (cvp + "\\Explorer", reg({"NoDriveTypeAutoRun": dword(autorun)})),
        (cvp + "\\System", reg({"EnableLUA": dword(1), "ConsentPromptBehaviorAdmin": dword(5), "legalnoticetext": {"t": "multi_sz", "v": ["Beispiel"]}})),
        (f"HKLM\\{CCS}\\Control\\Lsa", reg({"LmCompatibilityLevel": dword(3), "NoLMHash": dword(0), "RestrictAnonymous": dword(0),
                                            "Security Packages": {"t": "multi_sz", "v": ["kerberos"]}}, ["MSV1_0"])),
        (f"HKLM\\{CCS}\\Control\\Lsa\\MSV1_0", reg({"NTLMMinClientSec": dword(0)})),
        (f"HKLM\\{CCS}\\Control\\SecurityProviders", reg({"SecurityProviders": sz("credssp.dll")}, ["WDigest"])),
        (f"HKLM\\{CCS}\\Control\\SecurityProviders\\WDigest", reg({"Debuglevel": dword(0)})),
        (f"HKLM\\{CCS}\\Services\\LanmanServer\\Parameters", reg({"RequireSecuritySignature": dword(0), "EnableSecuritySignature": dword(0),
                                                                  "NullSessionShares": {"t": "multi_sz", "v": []}})),
        (f"HKLM\\{CCS}\\Services\\LanmanWorkstation\\Parameters", reg({"EnablePlainTextPassword": dword(0), "RequireSecuritySignature": dword(0)})),
        (f"HKLM\\{CCS}\\Services\\NetBT\\Parameters", reg({"NodeType": dword(8)})),
        (f"HKLM\\{CCS}\\Services\\SharedAccess\\Parameters\\FirewallPolicy", reg(subkeys=["DomainProfile", "StandardProfile"])),
        (f"HKLM\\{CCS}\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\StandardProfile", reg({"EnableFirewall": dword(1)})),
        (f"HKLM\\{CCS}\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\DomainProfile", reg({"EnableFirewall": dword(0)})),
        (f"HKLM\\{CCS}\\Control\\Terminal Server", reg({"fDenyTSConnections": dword(0)}, ["WinStations"])),
        (f"HKLM\\{CCS}\\Control\\Terminal Server\\WinStations", reg(subkeys=["RDP-Tcp"])),
        (f"HKLM\\{CCS}\\Control\\Terminal Server\\WinStations\\RDP-Tcp", reg({"UserAuthentication": dword(0), "SecurityLayer": dword(1), "PortNumber": dword(3389)})),
        ("HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", reg({"CurrentBuild": sz("7601"), "CSDVersion": sz("Service Pack 1"), "EditionID": sz("Professional"),
                                                                     "InstallationType": sz("Client"), "ProductName": sz("Windows 7 Professional")})),
        (f"HKLM\\{CCS}\\Services\\USBSTOR", reg({"Start": dword(3), "Type": dword(1)})),
        (f"HKLM\\{CCS}\\Services\\mrxsmb10", reg({"Start": dword(2)})),
        (f"HKLM\\{CCS}\\Control\\ProductOptions", reg({"ProductType": sz("WinNT")})),
        (f"HKLM\\{CCS}\\Services\\Tcpip\\Parameters", reg({"IPEnableRouter": dword(0), "Hostname": sz(host), "SearchList": sz("")})),
        ("HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon",
         reg({"AutoAdminLogon": sz("0"), "DefaultUserName": sz("operator"), "DefaultDomainName": sz(host)})),
    ]
    items = [{"key": "reg:" + k, "status": "ok", "value": v, "durationMs": 0} for k, v in keys]
    for name, start in [("Spooler", 2), ("RemoteRegistry", 3), ("TermService", 3), ("W32Time", 2), ("wuauserv", 2), ("lanmanserver", 2)]:
        items.append({"key": f"reg:HKLM\\{CCS}\\Services\\{name}", "status": "ok", "value": reg({"Start": dword(start)}), "durationMs": 0})
    items.append({"key": "raw:secedit-inf", "status": "ok", "value": {"text": SECEDIT_INF}, "durationMs": 0})
    audit = {g: 0 for g in sorted(AUDIT.values())}
    for name, value in [("Logon", 3), ("Logoff", 1), ("Credential Validation", 1), ("User Account Management", 1),
                        ("Security Group Management", 1), ("Audit Policy Change", 1)]:
        audit[AUDIT[name]] = value
    items.append({"key": "api:audit-policy", "status": "ok", "value": {k: audit[k] for k in sorted(audit)}, "durationMs": 0})
    coverage = [{"root": k, "recursive": False, "maxDepth": 0, "complete": True, "deniedSubkeys": []} for k, _ in keys]
    coverage += [{"root": f"HKLM\\{CCS}\\Services\\{name}", "recursive": False, "maxDepth": 0, "complete": True, "deniedSubkeys": []}
                 for name in ["Spooler", "RemoteRegistry", "TermService", "W32Time", "wuauserv", "lanmanserver"]]
    snap = {"schema": "riegel.snapshot/1",
            "collector": {"name": "collect-builtin.cmd", "version": "0.1.0", "clr": "", "is64BitProcess": False},
            "collectedAtUtc": collected, "isAdmin": True, "origin": "builtin",
            "options": {"minimal": False, "deep": False, "pseudonymized": False},
            "machine": {"hostName": host, "productType": 1, "build": 7601, "editionId": "Professional", "installationType": "Client"},
            "coverage": sorted(coverage, key=lambda c: c["root"].lower()),
            "items": items}
    return snap


finish(builtin("2026-09-10T09:00:00Z", 0x91))
finish(builtin("2026-10-10T09:00:00Z", 0xFF))


# ---------------------------------------------------------------- OPS-01: Server 2022, Domänenmitglied
# Domänen-GPO in parsed:gpresult (experimentelle Zuordnung); Druckwarteschlange läuft (Ausnahme läuft bald ab)
def ops(collected, boot, larger_log=False):
    s = load("server2022-domain-member")
    rename(s, "OPS-01", domain="musterwerk.example")
    at(s, collected, boot)
    updates(s, ("KB0000402", "2026-10-14T00:00:00Z" if larger_log else "2026-09-10T00:00:00Z"), ("KB0000401", "2026-08-13T00:00:00Z"))
    if larger_log:
        reg_values(s, f"reg:HKLM\\{CCS}\\Services\\EventLog\\Security")["MaxSize"] = dword(402653184)
    return s


finish(ops("2026-09-14T10:00:00Z", "2026-09-13T03:00:00Z"))
finish(ops("2026-10-14T10:00:00Z", "2026-10-13T03:00:00Z", larger_log=True))
