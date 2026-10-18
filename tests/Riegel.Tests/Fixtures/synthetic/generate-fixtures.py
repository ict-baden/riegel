# Erzeugt die synthetischen Fixtures (spec 8.1) unter tests/Riegel.Tests/Fixtures/synthetic. Alle Werte sind erfunden
# (Hostnamen, Konten, SIDs S-1-5-21-1000-2000-3000-<rid>, Adressen aus RFC 5737, Domäne corp.example nach RFC 2606).
# Die GUIDs der Überwachungs-Unterkategorien stammen aus „auditpol /list /subcategory:* /v“ (Windows-Ausgabe, nur gelesen).
# Ausgabe wird committet. Aufruf (aus dem Repo-Stamm): python tests/Riegel.Tests/Fixtures/synthetic/generate-fixtures.py .
import base64, copy, hashlib, json, os, sys

def guid_tail(host): return hashlib.sha256(host.encode()).hexdigest()[:4]
POL_BYTES = b"PReg\x01\x00\x00\x00"

repo = sys.argv[1]
out = os.path.join(repo, "tests", "Riegel.Tests", "Fixtures", "synthetic")
os.makedirs(out, exist_ok=True)

MACHINE_SID = "S-1-5-21-1000-2000-3000"
DOMAIN_SID = "S-1-5-21-4000-5000-6000"
CCS = "SYSTEM\\CurrentControlSet"

AUDIT = {  # Name → GUID (auditpol /list /subcategory:* /v)
    "Security State Change": "{0CCE9210-69AE-11D9-BED3-505054503030}", "Security System Extension": "{0CCE9211-69AE-11D9-BED3-505054503030}",
    "System Integrity": "{0CCE9212-69AE-11D9-BED3-505054503030}", "Other System Events": "{0CCE9214-69AE-11D9-BED3-505054503030}",
    "Logon": "{0CCE9215-69AE-11D9-BED3-505054503030}", "Logoff": "{0CCE9216-69AE-11D9-BED3-505054503030}",
    "Account Lockout": "{0CCE9217-69AE-11D9-BED3-505054503030}", "Special Logon": "{0CCE921B-69AE-11D9-BED3-505054503030}",
    "Other Logon/Logoff Events": "{0CCE921C-69AE-11D9-BED3-505054503030}", "Group Membership": "{0CCE9249-69AE-11D9-BED3-505054503030}",
    "File Share": "{0CCE9224-69AE-11D9-BED3-505054503030}", "Removable Storage": "{0CCE9245-69AE-11D9-BED3-505054503030}",
    "Other Object Access Events": "{0CCE9227-69AE-11D9-BED3-505054503030}", "Sensitive Privilege Use": "{0CCE9228-69AE-11D9-BED3-505054503030}",
    "Process Creation": "{0CCE922B-69AE-11D9-BED3-505054503030}", "Plug and Play Events": "{0CCE9248-69AE-11D9-BED3-505054503030}",
    "Audit Policy Change": "{0CCE922F-69AE-11D9-BED3-505054503030}", "Authentication Policy Change": "{0CCE9230-69AE-11D9-BED3-505054503030}",
    "MPSSVC Rule-Level Policy Change": "{0CCE9232-69AE-11D9-BED3-505054503030}", "User Account Management": "{0CCE9235-69AE-11D9-BED3-505054503030}",
    "Security Group Management": "{0CCE9237-69AE-11D9-BED3-505054503030}", "Credential Validation": "{0CCE923F-69AE-11D9-BED3-505054503030}",
}

PRIVILEGES = json.load(open(os.path.join(repo, "contracts", "collection-plan.json"), encoding="utf-8"))
RIGHTS = next(a for a in PRIVILEGES["api"] if a["key"] == "api:lsa-rights")["rights"]

def dword(v): return {"t": "dword", "v": v}
def sz(v): return {"t": "sz", "v": v}
def msz(*v): return {"t": "multi_sz", "v": list(v)}
def reg(values=None, subkeys=None):
    return {"values": values or {}, "subkeys": subkeys or []}
def item(key, status="ok", value=None, error_code=None, error=None, ms=1):
    d = {"key": key, "status": status}
    if value is not None: d["value"] = value
    if error_code is not None: d["errorCode"] = error_code
    if error is not None: d["error"] = error
    d["durationMs"] = ms
    return d
def rows(*r): return {"rows": list(r)}

PLAN = PRIVILEGES
def coverage(complete=True, denied=()):
    cov = []
    for e in PLAN["registry"]["roots"] + PLAN["registry"]["keys"]:
        recursive = e["kind"] != "key"
        cov.append({"root": e["path"], "recursive": recursive, "maxDepth": e["maxDepth"], "complete": complete,
                    "deniedSubkeys": [d for d in denied if d.startswith(e["path"] + "\\")]})
    return cov

def services(spec):
    out = []
    for name, display, state, start, account in spec:
        out.append({"Name": name, "DisplayName": display, "State": state, "StartMode": start, "StartName": account,
                    "PathName": "C:\\Windows\\System32\\svchost.exe -k netsvcs" if account == "LocalSystem" else "C:\\Windows\\System32\\svchost.exe -k LocalService"})
    return out

STD_SERVICES = [
    ("Spooler", "Druckwarteschlange", "Running", "Auto", "LocalSystem"),
    ("RemoteRegistry", "Remoteregistrierung", "Stopped", "Disabled", "NT AUTHORITY\\LocalService"),
    ("WinRM", "Windows-Remoteverwaltung (WS-Verwaltung)", "Stopped", "Manual", "NT AUTHORITY\\NetworkService"),
    ("TermService", "Remotedesktopdienste", "Stopped", "Manual", "NT AUTHORITY\\NetworkService"),
    ("wuauserv", "Windows Update", "Stopped", "Manual", "LocalSystem"),
    ("WinDefend", "Microsoft Defender Antivirus Service", "Running", "Auto", "LocalSystem"),
    ("mpssvc", "Windows Defender Firewall", "Running", "Auto", "NT AUTHORITY\\LocalService"),
    ("AppIDSvc", "Anwendungsidentität", "Stopped", "Manual", "NT AUTHORITY\\LocalService"),
    ("LanmanServer", "Server", "Running", "Auto", "LocalSystem"),
    ("LanmanWorkstation", "Arbeitsstationsdienst", "Running", "Auto", "NT AUTHORITY\\NetworkService"),
    ("W32Time", "Windows-Zeitgeber", "Running", "Manual", "NT AUTHORITY\\LocalService"),
]

def users(host, german=False, extra=()):
    admin = "Administrator"
    guest = "Gast" if german else "Guest"
    base = [
        {"Name": admin, "SID": f"{MACHINE_SID}-500", "Disabled": True, "Lockout": False, "PasswordRequired": True, "PasswordExpires": False},
        {"Name": guest, "SID": f"{MACHINE_SID}-501", "Disabled": True, "Lockout": False, "PasswordRequired": False, "PasswordExpires": False},
        {"Name": "DefaultAccount", "SID": f"{MACHINE_SID}-503", "Disabled": True, "Lockout": False, "PasswordRequired": False, "PasswordExpires": False},
        {"Name": "operator", "SID": f"{MACHINE_SID}-1001", "Disabled": False, "Lockout": False, "PasswordRequired": True, "PasswordExpires": False},
    ]
    return base + list(extra)

def netusers(german=False, never_expires_operator=True):
    return [
        {"name": "Administrator", "sid": f"{MACHINE_SID}-500", "rid": 500, "disabled": True, "passwordNotRequired": False,
         "passwordNeverExpires": True, "lockedOut": False, "lastLogonDate": None, "passwordAgeDays": 410},
        {"name": "Gast" if german else "Guest", "sid": f"{MACHINE_SID}-501", "rid": 501, "disabled": True, "passwordNotRequired": True,
         "passwordNeverExpires": True, "lockedOut": False, "lastLogonDate": None, "passwordAgeDays": 0},
        {"name": "DefaultAccount", "sid": f"{MACHINE_SID}-503", "rid": 503, "disabled": True, "passwordNotRequired": True,
         "passwordNeverExpires": True, "lockedOut": False, "lastLogonDate": None, "passwordAgeDays": 0},
        {"name": "operator", "sid": f"{MACHINE_SID}-1001", "rid": 1001, "disabled": False, "passwordNotRequired": False,
         "passwordNeverExpires": never_expires_operator, "lockedOut": False, "lastLogonDate": "2026-10-14", "passwordAgeDays": 120},
    ]

def lsa_rights(german=False, overrides=None):
    r = {right: [] for right in RIGHTS}
    r.update({
        "SeDebugPrivilege": ["S-1-5-32-544"],
        "SeBackupPrivilege": ["S-1-5-32-544", "S-1-5-32-551"],
        "SeRestorePrivilege": ["S-1-5-32-544", "S-1-5-32-551"],
        "SeNetworkLogonRight": ["S-1-1-0", "S-1-5-32-544", "S-1-5-32-545", "S-1-5-32-551"],
        "SeInteractiveLogonRight": ["S-1-5-32-544", "S-1-5-32-545", "S-1-5-32-551"],
        "SeRemoteInteractiveLogonRight": ["S-1-5-32-544", "S-1-5-32-555"],
        "SeDenyNetworkLogonRight": [f"{MACHINE_SID}-501"],
        "SeDenyInteractiveLogonRight": [f"{MACHINE_SID}-501"],
        "SeShutdownPrivilege": ["S-1-5-32-544", "S-1-5-32-545", "S-1-5-32-551"],
        "SeChangeNotifyPrivilege": ["S-1-1-0", "S-1-5-19", "S-1-5-20", "S-1-5-32-544", "S-1-5-32-545", "S-1-5-32-551"],
        "SeImpersonatePrivilege": ["S-1-5-19", "S-1-5-20", "S-1-5-32-544", "S-1-5-6"],
        "SeServiceLogonRight": ["S-1-5-80-0"],
        "SeSecurityPrivilege": ["S-1-5-32-544"],
        "SeTakeOwnershipPrivilege": ["S-1-5-32-544"],
        "SeLoadDriverPrivilege": ["S-1-5-32-544"],
        "SeSystemtimePrivilege": ["S-1-5-19", "S-1-5-32-544"],
    })
    if overrides: r.update(overrides)
    return {k: r[k] for k in sorted(r)}

def audit_policy(level="hardened"):
    if level == "none": return {g: 0 for g in sorted(AUDIT.values())}
    p = {g: 0 for g in AUDIT.values()}
    for name in ["Credential Validation", "Security Group Management", "User Account Management", "Logon", "Special Logon",
                 "Audit Policy Change", "Sensitive Privilege Use", "Security State Change", "System Integrity"]:
        p[AUDIT[name]] = 3
    for name in ["Logoff", "Process Creation", "Plug and Play Events", "Group Membership", "Authentication Policy Change", "Security System Extension"]:
        p[AUDIT[name]] = 1
    p[AUDIT["Account Lockout"]] = 2
    return {k: p[k] for k in sorted(p)}

def secedit(min_len=12, complexity=1, lockout=10, german=False, extra_rights=None):
    guest = "Gast" if german else f"*{MACHINE_SID}-501"
    rights = {"SeDebugPrivilege": ["*S-1-5-32-544"], "SeDenyNetworkLogonRight": [guest], "SeDenyInteractiveLogonRight": [guest],
              "SeRemoteInteractiveLogonRight": ["*S-1-5-32-544", "*S-1-5-32-555"], "SeBackupPrivilege": ["*S-1-5-32-544", "*S-1-5-32-551"]}
    if german: rights["SeShutdownPrivilege"] = ["Administratoren", "Benutzer", "Sicherungs-Operatoren"]
    if extra_rights: rights.update(extra_rights)
    return {"sections": {
        "System Access": {"MinimumPasswordAge": "1", "MaximumPasswordAge": "365", "MinimumPasswordLength": str(min_len),
                          "PasswordComplexity": str(complexity), "PasswordHistorySize": "24", "LockoutBadCount": str(lockout),
                          "ResetLockoutCount": "15", "LockoutDuration": "15", "RequireLogonToChangePassword": "0", "ForceLogoffWhenHourExpire": "0",
                          "NewAdministratorName": "\"Administrator\"", "NewGuestName": "\"Gast\"" if german else "\"Guest\"",
                          "ClearTextPassword": "0", "LSAAnonymousNameLookup": "0", "EnableAdminAccount": "0", "EnableGuestAccount": "0"},
        "Event Audit": {"AuditSystemEvents": "0", "AuditLogonEvents": "0", "AuditObjectAccess": "0", "AuditPrivilegeUse": "0",
                        "AuditPolicyChange": "0", "AuditAccountManage": "0", "AuditProcessTracking": "0", "AuditDSAccess": "0", "AuditAccountLogon": "0"},
        "Registry Values": {
            "MACHINE\\System\\CurrentControlSet\\Control\\Lsa\\LimitBlankPasswordUse": {"type": 4, "value": "1"},
            "MACHINE\\System\\CurrentControlSet\\Control\\Lsa\\NoLMHash": {"type": 4, "value": "1"},
            "MACHINE\\System\\CurrentControlSet\\Control\\Lsa\\RestrictAnonymousSAM": {"type": 4, "value": "1"},
            "MACHINE\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\\EnableLUA": {"type": 4, "value": "1"},
        },
        "Privilege Rights": rights,
        "Version": {"signature": "\"$CHICAGO$\"", "Revision": "1"},
    }}

def base_items(host, build=26100, ubr=6899, edition="Professional", display="24H2", caption="Microsoft Windows 11 Pro", product_type=1,
               domain_role=0, domain=None, admin=True, german=False, collected="2026-10-15T08:00:00Z", boot="2026-10-12T06:30:00Z"):
    i = []
    pol = "HKLM\\SOFTWARE\\Policies"
    i += [
        item("reg:" + pol, value=reg(subkeys=["Microsoft"])),
        item(f"reg:{pol}\\Microsoft", value=reg(subkeys=["Windows", "Windows NT", "Windows Defender"])),
        item(f"reg:{pol}\\Microsoft\\Windows", value=reg(subkeys=["Explorer", "PowerShell", "System"])),
        item(f"reg:{pol}\\Microsoft\\Windows\\Explorer", value=reg({"NoAutoplayfornonVolume": dword(1)})),
        item(f"reg:{pol}\\Microsoft\\Windows\\PowerShell", value=reg(subkeys=["ScriptBlockLogging"])),
        item(f"reg:{pol}\\Microsoft\\Windows\\PowerShell\\ScriptBlockLogging", value=reg({"EnableScriptBlockLogging": dword(1)})),
        item(f"reg:{pol}\\Microsoft\\Windows\\System", value=reg({"EnableSmartScreen": dword(1)})),
        item(f"reg:{pol}\\Microsoft\\Windows NT", value=reg(subkeys=["DNSClient", "Terminal Services"])),
        item(f"reg:{pol}\\Microsoft\\Windows NT\\DNSClient", value=reg({"EnableMulticast": dword(0)})),
        item(f"reg:{pol}\\Microsoft\\Windows NT\\Terminal Services",
             value=reg({"fDisableCdm": dword(1), "UserAuthentication": dword(1), "MinEncryptionLevel": dword(3), "SecurityLayer": dword(2),
                        "fPromptForPassword": dword(1), "DisablePasswordSaving": dword(1), "fEncryptRPCTraffic": dword(1), "MaxIdleTime": dword(900000)})),
        item(f"reg:{pol}\\Microsoft\\Windows Defender", value=reg(subkeys=["Spynet"])),
        item(f"reg:{pol}\\Microsoft\\Windows Defender\\Spynet", value=reg({"SpynetReporting": dword(2)})),
    ]
    cvp = "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies"
    i += [
        item("reg:" + cvp, value=reg(subkeys=["Explorer", "System"])),
        item(f"reg:{cvp}\\Explorer", value=reg({"NoDriveTypeAutoRun": dword(255), "NoAutorun": dword(1)})),
        item(f"reg:{cvp}\\System", value=reg({"EnableLUA": dword(1), "ConsentPromptBehaviorAdmin": dword(2), "PromptOnSecureDesktop": dword(1),
                                              "FilterAdministratorToken": dword(1), "InactivityTimeoutSecs": dword(900)})),
        item("reg:HKLM\\SOFTWARE\\Microsoft\\PolicyManager\\current\\device", value=reg(subkeys=["Update"])),
        item("reg:HKLM\\SOFTWARE\\Microsoft\\PolicyManager\\current\\device\\Update", value=reg({"AllowAutoUpdate": dword(1)})),
    ]
    lsa = f"HKLM\\{CCS}\\Control\\Lsa"
    i += [
        item("reg:" + lsa, value=reg({"RunAsPPL": dword(2), "LmCompatibilityLevel": dword(5), "NoLMHash": dword(1), "RestrictAnonymous": dword(1),
                                      "RestrictAnonymousSAM": dword(1), "EveryoneIncludesAnonymous": dword(0), "LimitBlankPasswordUse": dword(1),
                                      "SCENoApplyLegacyAuditPolicy": dword(1), "RestrictRemoteSAM": sz("O:BAG:BAD:(A;;RC;;;BA)")}, ["MSV1_0"])),
        item(f"reg:{lsa}\\MSV1_0", value=reg({"NTLMMinClientSec": dword(537395200), "NTLMMinServerSec": dword(537395200)})),
    ]
    sp = f"HKLM\\{CCS}\\Control\\SecurityProviders"
    i += [
        item("reg:" + sp, value=reg(subkeys=["SCHANNEL", "WDigest"])),
        item(f"reg:{sp}\\WDigest", value=reg({"UseLogonCredential": dword(0)})),
        item(f"reg:{sp}\\SCHANNEL", value=reg(subkeys=["Protocols"])),
        item(f"reg:{sp}\\SCHANNEL\\Protocols", value=reg(subkeys=["TLS 1.2"])),
        item(f"reg:{sp}\\SCHANNEL\\Protocols\\TLS 1.2", value=reg(subkeys=["Client"])),
        item(f"reg:{sp}\\SCHANNEL\\Protocols\\TLS 1.2\\Client", value=reg({"Enabled": dword(1), "DisabledByDefault": dword(0)})),
    ]
    ts = f"HKLM\\{CCS}\\Control\\Terminal Server"
    i += [
        item("reg:" + ts, value=reg({"fDenyTSConnections": dword(1)}, ["WinStations"])),
        item(f"reg:{ts}\\WinStations", value=reg(subkeys=["RDP-Tcp"])),
        item(f"reg:{ts}\\WinStations\\RDP-Tcp", value=reg({"UserAuthentication": dword(1), "SecurityLayer": dword(2), "PortNumber": dword(3389)})),
        item(f"reg:HKLM\\{CCS}\\Control\\DeviceGuard", value=reg({"EnableVirtualizationBasedSecurity": dword(1)}, ["Scenarios"])),
        item(f"reg:HKLM\\{CCS}\\Control\\DeviceGuard\\Scenarios", value=reg(subkeys=["HypervisorEnforcedCodeIntegrity"])),
        item(f"reg:HKLM\\{CCS}\\Control\\DeviceGuard\\Scenarios\\HypervisorEnforcedCodeIntegrity", value=reg({"Enabled": dword(1)})),
        item(f"reg:HKLM\\{CCS}\\Services\\NetBT\\Parameters", value=reg({"NodeType": dword(2)}, ["Interfaces"])),
        item(f"reg:HKLM\\{CCS}\\Services\\NetBT\\Parameters\\Interfaces", value=reg(subkeys=["Tcpip_{00000000-0000-4000-8000-0000000000b1}"])),
        item(f"reg:HKLM\\{CCS}\\Services\\NetBT\\Parameters\\Interfaces\\Tcpip_{{00000000-0000-4000-8000-0000000000b1}}", value=reg({"NetbiosOptions": dword(2)})),
    ]
    fw = f"HKLM\\{CCS}\\Services\\SharedAccess\\Parameters\\FirewallPolicy"
    i += [item("reg:" + fw, value=reg(subkeys=["DomainProfile", "FirewallRules", "PublicProfile", "StandardProfile"]))]
    for profile in ["DomainProfile", "StandardProfile", "PublicProfile"]:
        i += [item(f"reg:{fw}\\{profile}", value=reg({"EnableFirewall": dword(1), "DefaultInboundAction": dword(1)}, ["Logging"])),
              item(f"reg:{fw}\\{profile}\\Logging", value=reg({"LogDroppedPackets": dword(1), "LogFileSize": dword(16384)}))]
    i += [item(f"reg:{fw}\\FirewallRules", value=reg({
        "RemoteDesktop-UserMode-In-TCP": sz("v2.31|Action=Allow|Active=FALSE|Dir=In|Protocol=6|LPort=3389|App=%SystemRoot%\\system32\\svchost.exe|Svc=termservice|Name=@FirewallAPI.dll,-28775|"),
        "Beispiel-Leitsystem-In": sz("v2.31|Action=Allow|Active=TRUE|Dir=In|Protocol=6|LPort=4840|RA4=192.0.2.0/24|Name=Beispiel-Leitsystem OPC UA|"),
    }))]
    i += [
        item("reg:HKLM\\SOFTWARE\\Microsoft\\Windows Defender", value=reg({"ProductStatus": dword(0)}, ["Features"])),
        item("reg:HKLM\\SOFTWARE\\Microsoft\\Windows Defender\\Features", value=reg({"TamperProtection": dword(5)})),
        item("reg:HKLM\\SOFTWARE\\Microsoft\\Policies\\LAPS", "keyMissing"),
        item("reg:HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\LAPS", value=reg(subkeys=["Config"])),
        item("reg:HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\LAPS\\Config", value=reg()),
        item(f"reg:HKLM\\{CCS}\\Control\\CI", value=reg(subkeys=["Config", "Policy"])),
        item(f"reg:HKLM\\{CCS}\\Control\\CI\\Config", value=reg({"VulnerableDriverBlocklistEnable": dword(1)})),
        item(f"reg:HKLM\\{CCS}\\Control\\CI\\Policy", value=reg({"VerifiedAndReputablePolicyState": dword(0)})),
    ]
    # Einzelne Schlüssel
    i += [
        item(f"reg:HKLM\\{CCS}\\Services\\LanmanServer\\Parameters", value=reg({"RequireSecuritySignature": dword(1), "EnableSecuritySignature": dword(1),
                                                                                  "SMB1": dword(0), "RestrictNullSessAccess": dword(1)})),
        item(f"reg:HKLM\\{CCS}\\Services\\LanmanWorkstation\\Parameters", value=reg({"RequireSecuritySignature": dword(1), "EnableSecuritySignature": dword(1),
                                                                                       "AllowInsecureGuestAuth": dword(0), "EnablePlainTextPassword": dword(0)})),
        item(f"reg:HKLM\\{CCS}\\Services\\mrxsmb10", "keyMissing"),
        item(f"reg:HKLM\\{CCS}\\Services\\USBSTOR", value=reg({"Start": dword(3), "Type": dword(1)})),
        item(f"reg:HKLM\\{CCS}\\Services\\Tcpip\\Parameters", value=reg({"IPEnableRouter": dword(0), "Hostname": sz(host)})),
        item(f"reg:HKLM\\{CCS}\\Services\\Tcpip6\\Parameters", value=reg({"DisabledComponents": dword(0)})),
        item(f"reg:HKLM\\{CCS}\\Services\\EventLog\\Security", value=reg({"MaxSize": dword(201326592), "Retention": dword(0)})),
        item(f"reg:HKLM\\{CCS}\\Services\\EventLog\\System", value=reg({"MaxSize": dword(33554432)})),
        item(f"reg:HKLM\\{CCS}\\Services\\EventLog\\Application", value=reg({"MaxSize": dword(33554432)})),
        item(f"reg:HKLM\\{CCS}\\Control\\SecureBoot\\State", value=reg({"UEFISecureBootEnabled": dword(1)})),
        item(f"reg:HKLM\\{CCS}\\Control\\Session Manager", value=reg({"SafeDllSearchMode": dword(1), "ProtectionMode": dword(1)})),
        item(f"reg:HKLM\\{CCS}\\Control\\Session Manager\\kernel", value=reg({"DisableExceptionChainValidation": dword(0)})),
        item(f"reg:HKLM\\{CCS}\\Control\\ProductOptions", value=reg({"ProductType": sz("WinNT" if product_type == 1 else "ServerNT")})),
        item("reg:HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion",
             value=reg({"CurrentBuild": sz(str(build)), "UBR": dword(ubr), "EditionID": sz(edition), "InstallationType": sz("Client" if product_type == 1 else "Server"),
                        "DisplayVersion": sz(display), "ProductName": sz(caption.replace("Microsoft ", ""))})),
        item("reg:HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon",
             value=reg({"AutoAdminLogon": sz("0"), "DefaultUserName": sz("operator"), "Shell": sz("explorer.exe")})),
        item("reg:HKLM\\SOFTWARE\\Microsoft\\.NETFramework\\v4.0.30319", value=reg({"SchUseStrongCrypto": dword(1), "SystemDefaultTlsVersions": dword(1)})),
        item("reg:HKLM\\SOFTWARE\\Microsoft\\.NETFramework\\v2.0.50727", value=reg()),
        item("reg:HKLM\\SOFTWARE\\WOW6432Node\\Microsoft\\.NETFramework\\v4.0.30319", value=reg({"SchUseStrongCrypto": dword(1), "SystemDefaultTlsVersions": dword(1)})),
        item("reg:HKLM\\SOFTWARE\\WOW6432Node\\Microsoft\\.NETFramework\\v2.0.50727", value=reg()),
        item("reg:HKLM\\SOFTWARE\\Microsoft\\Cryptography", value={**reg({"MachineGuid": sz("00000000-0000-4000-8000-00000000" + guid_tail(host))}), "valueFilter": ["MachineGuid"]}),
        item("reg:HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall", value={**reg(subkeys=["BeispielLeitsystem"]), "valueFilter": ["DisplayName", "DisplayVersion", "Publisher"]}),
        item("reg:HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\BeispielLeitsystem",
             value={**reg({"DisplayName": sz("Beispiel-Leitsystem"), "DisplayVersion": sz("4.2.0"), "Publisher": sz("Beispiel-Anlagenbau")}), "valueFilter": ["DisplayName", "DisplayVersion", "Publisher"]}),
        item("reg:HKLM\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall", value={**reg(), "valueFilter": ["DisplayName", "DisplayVersion", "Publisher"]}),
        item("reg:HKLM\\SOFTWARE\\Microsoft\\Office\\ClickToRun\\Configuration", "keyMissing"),
    ]
    for ver in ["16.0", "15.0", "14.0"]:
        i.append(item(f"reg:HKLM\\SOFTWARE\\Microsoft\\Office\\{ver}\\Common\\InstallRoot", "keyMissing"))
        i.append(item(f"reg:HKLM\\SOFTWARE\\WOW6432Node\\Microsoft\\Office\\{ver}\\Common\\InstallRoot", "keyMissing"))
    # WMI
    cim = "root\\cimv2"
    i += [
        item(f"wmi:{cim}:Win32_ComputerSystem", value=rows({"Name": host, "DomainRole": domain_role, "PartOfDomain": domain is not None,
                                                           "Domain": domain or "WORKGROUP", "Workgroup": None if domain else "WORKGROUP",
                                                           "PCSystemType": 1, "PCSystemTypeEx": 1, "Manufacturer": "Beispiel-Hersteller", "Model": "Industrie-PC X1"}), ms=40),
        item(f"wmi:{cim}:Win32_OperatingSystem", value=rows({"Caption": caption, "Version": f"10.0.{build}" if build >= 10240 else f"6.{1 if build < 9000 else 3}.{build}",
                                                            "BuildNumber": str(build), "OSArchitecture": "64-bit", "ProductType": product_type,
                                                            "OperatingSystemSKU": 48, "OSLanguage": 1031 if german else 1033, "LastBootUpTime": boot,
                                                            "InstallDate": "2025-03-02T10:00:00Z"}), ms=30),
        item(f"wmi:{cim}:Win32_Service", value=rows(*services(STD_SERVICES)), ms=210),
        item(f"wmi:{cim}:Win32_OptionalFeature", value=rows({"Name": "SMB1Protocol", "InstallState": 2}, {"Name": "SMB1Protocol-Client", "InstallState": 2},
                                                           {"Name": "SMB1Protocol-Server", "InstallState": 2}, {"Name": "TelnetClient", "InstallState": 2}), ms=480),
        item(f"wmi:{cim}:Win32_QuickFixEngineering", value=rows({"HotFixID": "KB0000001", "InstalledOn": "2026-10-14T00:00:00Z"},
                                                               {"HotFixID": "KB0000002", "InstalledOn": "2026-09-10T00:00:00Z"}), ms=820),
        item(f"wmi:{cim}:Win32_UserAccount", value=rows(*users(host, german)), ms=56),
        item(f"wmi:{cim}:Win32_NetworkAdapterConfiguration", value=rows({"SettingID": "{00000000-0000-4000-8000-0000000000b1}", "TcpipNetbiosOptions": 2,
                                                                        "DefaultIPGateway": ["192.0.2.1"], "DHCPEnabled": False})),
        item(f"wmi:{cim}:Win32_NetworkAdapter", value=rows({"Name": "Ethernet-Adapter (Beispiel)", "NetConnectionStatus": 2, "AdapterTypeID": 0,
                                                           "GUID": "{00000000-0000-4000-8000-0000000000b1}", "NetEnabled": True})),
        item("wmi:root\\wmi:MSNdis_PhysicalMediumType", value=rows({"InstanceName": "Ethernet-Adapter (Beispiel)", "NdisPhysicalMediumType": 0, "Active": True})),
        item(f"wmi:{cim}:Win32_Share", value=rows(
            {"Name": "ADMIN$", "Path": "C:\\Windows", "Type": 2147483648, "GetSecurityDescriptor": {"ReturnValue": 0, "Aces": [{"Sid": "S-1-5-32-544", "AccessMask": 2032127, "AceType": 0}]}},
            {"Name": "C$", "Path": "C:\\", "Type": 2147483648, "GetSecurityDescriptor": {"ReturnValue": 0, "Aces": [{"Sid": "S-1-5-32-544", "AccessMask": 2032127, "AceType": 0}]}},
            {"Name": "IPC$", "Path": "", "Type": 2147483651, "GetSecurityDescriptor": {"ReturnValue": 0, "Aces": []}},
            {"Name": "Austausch", "Path": "D:\\Austausch", "Type": 0, "GetSecurityDescriptor": {"ReturnValue": 0, "Aces": [{"Sid": "S-1-1-0", "AccessMask": 1179817, "AceType": 0}]}})),
        item("wmi:root\\Microsoft\\Windows\\Defender:MSFT_MpComputerStatus", value=rows({
            "AMRunningMode": "Normal", "AMServiceEnabled": True, "AntivirusEnabled": True, "RealTimeProtectionEnabled": True, "BehaviorMonitorEnabled": True,
            "IoavProtectionEnabled": True, "IsTamperProtected": True, "TamperProtectionSource": "UI", "AntivirusSignatureLastUpdated": "2026-10-15T04:12:00Z",
            "AntivirusSignatureAge": 0, "AMProductVersion": "4.18.0.0"}), ms=130),
        item("wmi:root\\Microsoft\\Windows\\Defender:MSFT_MpPreference", value=rows({
            "AttackSurfaceReductionRules_Ids": None, "AttackSurfaceReductionRules_Actions": None, "MAPSReporting": 2, "SubmitSamplesConsent": 1,
            "PUAProtection": 1, "EnableNetworkProtection": 0, "EnableControlledFolderAccess": 0, "DisableRealtimeMonitoring": False,
            "DisableBehaviorMonitoring": False,
            "ExclusionPath": ["D:\\Beispiel-Leitsystem\\Daten"] if admin else ["N/A: Must be an administrator to view exclusions"],
            "ExclusionExtension": None, "ExclusionProcess": None, "ExclusionIpAddress": None}), ms=90),
        item("wmi:root\\SecurityCenter2:AntivirusProduct", value=rows({"displayName": "Windows Defender", "productState": 397568, "timestamp": "2026-10-15T04:12:00Z"}), ms=23),
        item("wmi:root\\Microsoft\\Windows\\DeviceGuard:Win32_DeviceGuard", value=rows({
            "VirtualizationBasedSecurityStatus": 2, "SecurityServicesConfigured": [2], "SecurityServicesRunning": [2],
            "CodeIntegrityPolicyEnforcementStatus": 0, "UsermodeCodeIntegrityPolicyEnforcementStatus": 0}), ms=23),
    ]
    if admin:
        i += [
            item("wmi:root\\cimv2\\Security\\MicrosoftVolumeEncryption:Win32_EncryptableVolume", value=rows({
                "DriveLetter": "C:", "VolumeType": 0, "ProtectionStatus": 1, "ConversionStatus": 1, "EncryptionMethod": 7,
                "GetConversionStatus": {"ReturnValue": 0, "ConversionStatus": 1, "EncryptionPercentage": 100},
                "GetEncryptionMethod": {"ReturnValue": 0, "EncryptionMethod": 7},
                "GetKeyProtectors": {"ReturnValue": 0, "VolumeKeyProtectorID": ["{00000000-0000-4000-8000-0000000000c1}", "{00000000-0000-4000-8000-0000000000c2}"], "KeyProtectorTypes": [1, 3]},
                "GetProtectionStatus": {"ReturnValue": 0, "ProtectionStatus": 1}}), ms=88),
            item("wmi:root\\cimv2\\Security\\MicrosoftTpm:Win32_Tpm", value=rows({"IsEnabled_InitialValue": True, "IsActivated_InitialValue": True,
                                                                                 "IsOwned_InitialValue": True, "SpecVersion": "2.0, 0, 1.59"}), ms=40),
        ]
    else:
        i += [
            item("wmi:root\\cimv2\\Security\\MicrosoftVolumeEncryption:Win32_EncryptableVolume", "denied", error_code=-2147024891, error="Access denied", ms=12),
            item("wmi:root\\cimv2\\Security\\MicrosoftTpm:Win32_Tpm", "denied", error_code=-2147024891, error="Access denied", ms=9),
        ]
    i += [
        item("wmi:root\\Microsoft\\Windows\\SMB:MSFT_SmbServerConfiguration.GetConfiguration", value={"out": {
            "EnableSMB1Protocol": False, "EnableSMB2Protocol": True, "RequireSecuritySignature": True, "EnableSecuritySignature": True,
            "EncryptData": False, "RejectUnencryptedAccess": True}}),
        item("wmi:root\\Microsoft\\Windows\\SMB:MSFT_SmbClientConfiguration.GetConfiguration", value={"out": {
            "RequireSecuritySignature": True, "EnableSecuritySignature": True, "EnableInsecureGuestLogons": False}}),
    ]
    # API
    i += [
        item("api:netusermodals", value={"minPwLen": 12, "maxPwAgeDays": 365, "minPwAgeDays": 1, "pwHistLen": 24, "lockoutThreshold": 10,
                                         "lockoutDurationMin": 15, "lockoutWindowMin": 15}, ms=4),
        item("api:netuserenum", value=netusers(german), ms=8),
        item("api:lsa-rights", value=lsa_rights(german), ms=10) if admin else item("api:lsa-rights", "denied", error_code=5, error="Access denied", ms=6),
        item("api:localgroup:S-1-5-32-544", value=[
            {"sid": f"{MACHINE_SID}-500", "name": "Administrator", "kind": "user", "domain": host},
            {"sid": f"{MACHINE_SID}-1001", "name": "operator", "kind": "user", "domain": host}], ms=60),
        item("api:localgroup:S-1-5-32-555", value=[], ms=12),
        item("api:audit-policy", value=audit_policy(), ms=25) if admin else item("api:audit-policy", "error", error_code=1314, error="A required privilege is not held by the client", ms=3),
        item("api:lsass-protection", "notCollected", error="only with --deep", ms=0),
        item("evt:system:wininit-12", value={"found": True, "timeCreatedUtc": "2026-10-12T06:30:41Z", "eventData": ["4"], "bootTimeUtc": boot}, ms=240),
        item("api:firewall", value={"currentProfiles": 2, "profiles": {
            "domain": {"enabled": True, "defaultInbound": "block", "defaultOutbound": "allow"},
            "private": {"enabled": True, "defaultInbound": "block", "defaultOutbound": "allow"},
            "public": {"enabled": True, "defaultInbound": "block", "defaultOutbound": "allow"}},
            "rulesCollected": True, "rules": [
                {"name": "Beispiel-Leitsystem OPC UA", "enabled": True, "direction": "in", "action": "allow", "protocol": 6, "localPorts": "4840",
                 "remoteAddresses": "192.0.2.0/255.255.255.0", "profiles": 7, "application": None, "service": None},
                {"name": "Remotedesktop – Benutzermodus (TCP eingehend)", "enabled": False, "direction": "in", "action": "allow", "protocol": 6,
                 "localPorts": "3389", "remoteAddresses": "*", "profiles": 7, "application": "%SystemRoot%\\system32\\svchost.exe", "service": "termservice"}]}, ms=47),
        item("api:windows-update-history", "notCollected", error="Windows Update not running – not queried", ms=0),
        item("api:winhttp-proxy", value={"accessType": 1, "proxy": None, "bypass": None}, ms=1),
        item("api:wsc-av", value=[{"name": "Windows Defender", "state": "on", "signature": "upToDate"}], ms=15),
        item("raw:registry-pol", value={"base64": base64.b64encode(POL_BYTES).decode(), "sha256": hashlib.sha256(POL_BYTES).hexdigest()}, ms=1),
    ]
    if admin:
        i.append(item("parsed:secedit", value=secedit(german=german), ms=0))
    else:
        i += [item("raw:secedit-inf", "denied", error_code=740, error="The requested operation requires elevation", ms=120),
              item("raw:gpresult-xml", "denied", error="Access denied", ms=900)]
    return i

def snapshot(host, items, admin=True, origin="synthetic", collected="2026-10-15T08:00:00Z", clr="4.0.30319.42000", build=26100, ubr=6899,
             edition="Professional", display="24H2", product_type=1, domain_role=0, domain=None, install="Client", lang="en-US",
             boot="2026-10-12T06:30:00Z", options=None, coverage_entries=None, arch="AMD64"):
    machine = {"hostName": host, "domainRole": domain_role, "productType": product_type, "build": build, "ubr": ubr, "editionId": edition,
               "installationType": install, "displayVersion": display, "architecture": arch,
               "machineGuid": "00000000-0000-4000-8000-00000000" + guid_tail(host), "osLanguage": lang, "lastBootUtc": boot}
    if domain: machine = {"hostName": host, "dnsDomain": domain, **{k: v for k, v in machine.items() if k != "hostName"}}
    if display is None: machine.pop("displayVersion")
    return {"schema": "riegel.snapshot/1",
            "collector": {"name": "Riegel.Collector", "version": "0.1.0", "clr": clr, "is64BitProcess": arch == "AMD64"},
            "collectedAtUtc": collected, "isAdmin": admin, "origin": origin,
            "options": options or {"minimal": False, "deep": False, "pseudonymized": False},
            "machine": machine, "coverage": coverage_entries if coverage_entries is not None else coverage(),
            "items": sorted(items, key=lambda x: x["key"].lower())}

ORDER = ["key", "status", "value", "errorCode", "error", "durationMs"]

def set_item(items, key, **changes):
    for it in items:
        if it["key"].lower() == key.lower():
            it.update(changes)
            if changes.get("status") not in (None, "ok") and "value" not in changes: it.pop("value", None)
            ordered = {k: it[k] for k in ORDER if k in it}
            it.clear()
            it.update(ordered)
            return it
    it = item(key, **{k: v for k, v in changes.items() if k in ("status", "value")})
    items.append(it)
    return it

def remove(items, prefix):
    items[:] = [it for it in items if not it["key"].lower().startswith(prefix.lower())]

def with_value(items, key, name, value):
    for it in items:
        if it["key"].lower() == key.lower():
            it["value"]["values"][name] = value
            return
    raise KeyError(key)

def rows_of(items, key):
    for it in items:
        if it["key"].lower() == key.lower(): return it["value"]["rows"]
    raise KeyError(key)

FIX = {}
# 1/2 Windows 11 24H2, Arbeitsgruppe, als Administrator und mit eingeschränktem Token
h = "W11-WG-01"
FIX["win11-24h2-workgroup-admin"] = snapshot(h, base_items(h))
nonadmin_cov = coverage(denied=["HKLM\\SOFTWARE\\Microsoft\\Windows Defender\\Exclusions"])
FIX["win11-24h2-workgroup-nonadmin"] = snapshot(h, base_items(h, admin=False), admin=False, collected="2026-10-15T08:10:00Z", coverage_entries=nonadmin_cov)

# 3 Server 2022, Domänenmitglied (mit Domänen-GPO in parsed:gpresult)
h = "SRV22-MEMBER"
items = base_items(h, build=20348, ubr=4294, edition="ServerStandard", display="21H2", caption="Microsoft Windows Server 2022 Standard",
                   product_type=3, domain_role=3, domain="corp.example")
remove(items, "wmi:root\\SecurityCenter2")
items.append(item("wmi:root\\SecurityCenter2:AntivirusProduct", "namespaceMissing", error_code=-2147217394, error="Invalid namespace", ms=3))
set_item(items, "api:wsc-av", status="notCollected", error="WSC API not available on Server")
items.append(item("parsed:gpresult", value={"gpos": [{"name": "Local Group Policy", "guid": "LocalGPO"},
                                                     {"name": "Linie-3-Basis", "guid": "{00000000-0000-4000-8000-0000000000d1}"}],
                                            "registry": [{"key": "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient", "value": "EnableMulticast", "gpo": "Linie-3-Basis"},
                                                         {"key": "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services", "value": "UserAuthentication", "gpo": "Linie-3-Basis"}],
                                            "security": [{"name": "MinimumPasswordLength", "gpo": "Linie-3-Basis"}]}, ms=0))
FIX["server2022-domain-member"] = snapshot(h, items, build=20348, ubr=4294, edition="ServerStandard", display="21H2", product_type=3, domain_role=3,
                                           domain="corp.example", install="Server", collected="2026-10-14T10:00:00Z")

# 4 Windows 10 LTSC 2019 Bedienstation (HMI): automatische Anmeldung, RDP an ohne NLA, PowerShell 2.0 vorhanden, SMBv1-Client-Treiber
h = "HMI-LTSC19"
items = base_items(h, build=17763, ubr=7792, edition="EnterpriseS", display=None, caption="Microsoft Windows 10 Enterprise LTSC 2019")
set_item(items, "reg:HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon",
         value=reg({"AutoAdminLogon": sz("1"), "DefaultUserName": sz("operator"), "DefaultPassword": {"t": "sz", "redacted": True, "empty": False}, "Shell": sz("explorer.exe")}))
set_item(items, f"reg:HKLM\\{CCS}\\Control\\Terminal Server", value=reg({"fDenyTSConnections": dword(0)}, ["WinStations"]))
set_item(items, f"reg:HKLM\\{CCS}\\Control\\Terminal Server\\WinStations\\RDP-Tcp", value=reg({"UserAuthentication": dword(0), "SecurityLayer": dword(1), "PortNumber": dword(3389)}))
remove(items, "reg:HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services")
set_item(items, "reg:HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows NT", value=reg(subkeys=["DNSClient"]))
set_item(items, f"reg:HKLM\\{CCS}\\Services\\mrxsmb10", status="ok", value=reg({"Start": dword(4)}))
set_item(items, "wmi:root\\cimv2:Win32_OptionalFeature", value=rows({"Name": "SMB1Protocol", "InstallState": 2}, {"Name": "MicrosoftWindowsPowerShellV2Root", "InstallState": 1},
                                                                 {"Name": "MicrosoftWindowsPowerShellV2", "InstallState": 1}))
set_item(items, "wmi:root\\Microsoft\\Windows\\DeviceGuard:Win32_DeviceGuard", value=rows({"VirtualizationBasedSecurityStatus": 0, "SecurityServicesConfigured": [],
                                                                                         "SecurityServicesRunning": [], "CodeIntegrityPolicyEnforcementStatus": 0,
                                                                                         "UsermodeCodeIntegrityPolicyEnforcementStatus": 0}))
set_item(items, "evt:system:wininit-12", value={"found": False, "timeCreatedUtc": None, "eventData": [], "bootTimeUtc": "2026-10-01T05:00:00Z"})
with_value(items, f"reg:HKLM\\{CCS}\\Control\\Lsa", "RunAsPPL", dword(0))
for r in rows_of(items, "wmi:root\\cimv2:Win32_Service"):
    if r["Name"] == "TermService": r.update({"State": "Running", "StartMode": "Auto"})
FIX["win10-ltsc2019-hmi"] = snapshot(h, items, build=17763, ubr=7792, edition="EnterpriseS", display=None, collected="2026-10-16T07:30:00Z",
                                     boot="2026-10-01T05:00:00Z")

# 5 Server 2016 Historian
h = "HIST-SRV16"
items = base_items(h, build=14393, ubr=8519, edition="ServerStandard", display=None, caption="Microsoft Windows Server 2016 Standard", product_type=3)
remove(items, "wmi:root\\SecurityCenter2")
items.append(item("wmi:root\\SecurityCenter2:AntivirusProduct", "namespaceMissing", error_code=-2147217394, error="Invalid namespace", ms=3))
set_item(items, "api:wsc-av", status="notCollected", error="WSC API not available on Server")
set_item(items, "wmi:root\\Microsoft\\Windows\\DeviceGuard:Win32_DeviceGuard", value=rows({"VirtualizationBasedSecurityStatus": 0, "SecurityServicesConfigured": [],
                                                                                         "SecurityServicesRunning": [], "CodeIntegrityPolicyEnforcementStatus": 0,
                                                                                         "UsermodeCodeIntegrityPolicyEnforcementStatus": 0}))
set_item(items, "evt:system:wininit-12", value={"found": False, "timeCreatedUtc": None, "eventData": [], "bootTimeUtc": "2026-07-20T02:00:00Z"})
FIX["server2016-historian"] = snapshot(h, items, build=14393, ubr=8519, edition="ServerStandard", display=None, product_type=3, install="Server",
                                       collected="2026-08-03T06:15:00Z", boot="2026-07-20T02:00:00Z")

# 6 Windows 7 SP1 (CLR 2, ohne Defender-, DeviceGuard- und SMB-Namespaces)
h = "PG-W7"
items = base_items(h, build=7601, ubr=0, edition="Professional", display=None, caption="Microsoft Windows 7 Professional")
for prefix in ["wmi:root\\Microsoft\\Windows\\Defender", "wmi:root\\Microsoft\\Windows\\DeviceGuard", "wmi:root\\Microsoft\\Windows\\SMB",
               "wmi:root\\cimv2\\Security\\MicrosoftTpm"]:
    remove(items, prefix)
for key in ["wmi:root\\Microsoft\\Windows\\Defender:MSFT_MpComputerStatus", "wmi:root\\Microsoft\\Windows\\Defender:MSFT_MpPreference",
            "wmi:root\\Microsoft\\Windows\\DeviceGuard:Win32_DeviceGuard", "wmi:root\\Microsoft\\Windows\\SMB:MSFT_SmbServerConfiguration.GetConfiguration",
            "wmi:root\\Microsoft\\Windows\\SMB:MSFT_SmbClientConfiguration.GetConfiguration"]:
    items.append(item(key, "namespaceMissing", error_code=-2147217394, error="Invalid namespace", ms=2))
items.append(item("wmi:root\\cimv2\\Security\\MicrosoftTpm:Win32_Tpm", "ok", value=rows(), ms=5))
set_item(items, "wmi:root\\SecurityCenter2:AntivirusProduct", value=rows({"displayName": "Beispiel-Virenschutz (fiktiv)", "productState": 266240, "timestamp": "2026-10-09T08:00:00Z"}))
set_item(items, "api:wsc-av", status="notCollected", error="WSC API requires Windows 8 or later")
set_item(items, "evt:system:wininit-12", value={"found": False, "timeCreatedUtc": None, "eventData": [], "bootTimeUtc": "2026-10-08T06:00:00Z"})
set_item(items, f"reg:HKLM\\{CCS}\\Services\\mrxsmb10", status="ok", value=reg({"Start": dword(2)}))
set_item(items, f"reg:HKLM\\{CCS}\\Services\\LanmanServer\\Parameters", value=reg({"EnableSecuritySignature": dword(0), "RequireSecuritySignature": dword(0)}))
set_item(items, "wmi:root\\cimv2:Win32_OptionalFeature", value=rows({"Name": "TelnetClient", "InstallState": 2}))
set_item(items, f"reg:HKLM\\{CCS}\\Control\\SecureBoot\\State", status="keyMissing")
remove(items, f"reg:HKLM\\{CCS}\\Control\\DeviceGuard")
remove(items, f"reg:HKLM\\{CCS}\\Control\\CI")
items.append(item(f"reg:HKLM\\{CCS}\\Control\\DeviceGuard", "keyMissing"))
items.append(item(f"reg:HKLM\\{CCS}\\Control\\CI", value=reg()))
remove(items, "reg:HKLM\\SOFTWARE\\Microsoft\\Windows Defender")
items.append(item("reg:HKLM\\SOFTWARE\\Microsoft\\Windows Defender", "keyMissing"))
remove(items, "reg:HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\LAPS")
items.append(item("reg:HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\LAPS", "keyMissing"))
set_item(items, "reg:HKLM\\SOFTWARE\\Microsoft\\.NETFramework\\v4.0.30319", status="keyMissing")
set_item(items, "reg:HKLM\\SOFTWARE\\WOW6432Node\\Microsoft\\.NETFramework\\v4.0.30319", status="keyMissing")
with_value(items, f"reg:HKLM\\{CCS}\\Control\\Lsa", "RunAsPPL", dword(0))
for r in rows_of(items, "wmi:root\\cimv2:Win32_Service"):
    if r["Name"] == "WinDefend": r.update({"DisplayName": "Windows Defender", "State": "Stopped", "StartMode": "Disabled"})
FIX["win7-sp1"] = snapshot(h, items, clr="2.0.50727.8000", build=7601, ubr=0, edition="Professional", display=None, collected="2026-10-10T09:00:00Z",
                           boot="2026-10-08T06:00:00Z")

# 7 Server 2012 R2 (kein Defender, keine DeviceGuard, kein SecurityCenter2)
h = "SRV12R2"
items = base_items(h, build=9600, ubr=21620, edition="ServerStandard", display=None, caption="Microsoft Windows Server 2012 R2 Standard", product_type=3)
for prefix in ["wmi:root\\Microsoft\\Windows\\Defender", "wmi:root\\Microsoft\\Windows\\DeviceGuard", "wmi:root\\SecurityCenter2"]:
    remove(items, prefix)
for key in ["wmi:root\\Microsoft\\Windows\\Defender:MSFT_MpComputerStatus", "wmi:root\\Microsoft\\Windows\\Defender:MSFT_MpPreference",
            "wmi:root\\Microsoft\\Windows\\DeviceGuard:Win32_DeviceGuard", "wmi:root\\SecurityCenter2:AntivirusProduct"]:
    items.append(item(key, "namespaceMissing", error_code=-2147217394, error="Invalid namespace", ms=2))
set_item(items, "api:wsc-av", status="notCollected", error="WSC API not available on Server")
set_item(items, "evt:system:wininit-12", value={"found": False, "timeCreatedUtc": None, "eventData": [], "bootTimeUtc": "2026-10-05T03:00:00Z"})
with_value(items, f"reg:HKLM\\{CCS}\\Control\\Lsa", "RunAsPPL", dword(0))
FIX["server2012r2"] = snapshot(h, items, build=9600, ubr=21620, edition="ServerStandard", display=None, product_type=3, install="Server",
                               collected="2026-10-13T07:00:00Z", boot="2026-10-05T03:00:00Z")

# 8 Deutschsprachiges System: Namen „Gast“, „Administratoren“; nur SIDs entscheiden
h = "PC-DE-01"
items = base_items(h, build=19045, ubr=6456, edition="Professional", display="22H2", caption="Microsoft Windows 10 Pro", german=True)
set_item(items, "api:localgroup:S-1-5-32-544", value=[
    {"sid": f"{MACHINE_SID}-500", "name": "Administrator", "kind": "user", "domain": h},
    {"sid": f"{MACHINE_SID}-1001", "name": "operator", "kind": "user", "domain": h}])
FIX["win10-german-locale"] = snapshot(h, items, build=19045, ubr=6456, edition="Professional", display="22H2", lang="de-DE", collected="2026-10-15T09:00:00Z")

# 9 Domänencontroller
h = "DC-SRV22"
items = base_items(h, build=20348, ubr=4294, edition="ServerStandard", display="21H2", caption="Microsoft Windows Server 2022 Standard",
                   product_type=2, domain_role=5, domain="corp.example")
remove(items, "wmi:root\\SecurityCenter2")
items.append(item("wmi:root\\SecurityCenter2:AntivirusProduct", "namespaceMissing", error_code=-2147217394, error="Invalid namespace", ms=3))
set_item(items, "api:wsc-av", status="notCollected", error="WSC API not available on Server")
set_item(items, "api:localgroup:S-1-5-32-544", value=[
    {"sid": f"{DOMAIN_SID}-500", "name": "Administrator", "kind": "user", "domain": "CORP"},
    {"sid": f"{DOMAIN_SID}-512", "name": "Domain Admins", "kind": "group", "domain": "CORP"},
    {"sid": f"{DOMAIN_SID}-519", "name": "Enterprise Admins", "kind": "group", "domain": "CORP"}])
set_item(items, "wmi:root\\cimv2:Win32_UserAccount", value=rows())
set_item(items, "api:netuserenum", value=[{"name": "Administrator", "sid": f"{DOMAIN_SID}-500", "rid": 500, "disabled": False, "passwordNotRequired": False,
                                           "passwordNeverExpires": False, "lockedOut": False, "lastLogonDate": "2026-10-13", "passwordAgeDays": 30}])
FIX["server2022-dc"] = snapshot(h, items, build=20348, ubr=4294, edition="ServerStandard", display="21H2", product_type=2, domain_role=5,
                                domain="corp.example", install="Server", collected="2026-10-14T11:00:00Z")

# 10 Defender passiv, Drittanbieter-Virenschutz aktiv
h = "ENG-W11-PASSIVE"
items = base_items(h)
set_item(items, "wmi:root\\Microsoft\\Windows\\Defender:MSFT_MpComputerStatus", value=rows({
    "AMRunningMode": "Passive Mode", "AMServiceEnabled": True, "AntivirusEnabled": False, "RealTimeProtectionEnabled": False,
    "BehaviorMonitorEnabled": False, "IoavProtectionEnabled": False, "IsTamperProtected": False, "TamperProtectionSource": "N/A",
    "AntivirusSignatureLastUpdated": "2026-10-10T04:00:00Z", "AntivirusSignatureAge": 5, "AMProductVersion": "4.18.0.0"}))
set_item(items, "wmi:root\\SecurityCenter2:AntivirusProduct", value=rows(
    {"displayName": "Beispiel-Virenschutz (fiktiv)", "productState": 266240, "timestamp": "2026-10-15T06:00:00Z"},
    {"displayName": "Windows Defender", "productState": 393472, "timestamp": "2026-10-10T04:00:00Z"}))
set_item(items, "api:wsc-av", value=[{"name": "Beispiel-Virenschutz (fiktiv)", "state": "on", "signature": "upToDate"},
                                     {"name": "Windows Defender", "state": "off", "signature": "outOfDate"}])
FIX["win11-defender-passive-thirdparty-av"] = snapshot(h, items, collected="2026-10-15T13:05:00Z")

for name, data in FIX.items():
    path = os.path.join(out, f"{name}.snapshot.json")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(data, indent=2, ensure_ascii=False) + "\n")
    print(path, len(data["items"]))

# ---------------------------------------------------------------- Bordmittel-Pakete (spec 7.5)
def utf16(path, text):
    with open(path, "wb") as f:
        f.write(b"\xff\xfe" + text.replace("\n", "\r\n").encode("utf-16-le"))

def ascii_crlf(path, text):
    with open(path, "wb") as f:
        f.write(text.replace("\n", "\r\n").encode("ascii"))

def secedit_inf(audit_section, privilege_extra=""):
    return f"""[Unicode]
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
{audit_section}[Registry Values]
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
{privilege_extra}[Version]
signature="$CHICAGO$"
Revision=1
"""

def reg_file(keys):
    lines = ["Windows Registry Editor Version 5.00", ""]
    for path, values in keys:
        lines.append(f"[{path}]")
        for name, v in values:
            lines.append(f"{name}={v}")
        lines.append("")
    return "\n".join(lines) + "\n"

def services_start(entries, xp=False):
    out_lines = [""]
    if xp: out_lines += ["! REG.EXE VERSION 3.0", ""]
    for name, start in entries:
        out_lines.append(f"HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\{name}")
        out_lines.append(f"    Start\tREG_DWORD\t0x{start:x}" if xp else f"    Start    REG_DWORD    0x{start:x}")
        out_lines.append("")
    if not xp: out_lines.append(f"End of search: {len(entries)} match(es) found.")
    return "\n".join(out_lines) + "\n"

def winlogon(host, auto, user, password_present, xp=False):
    key = "HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon"
    sep = "\t" if xp else "    "
    parts = []
    for name, value in [("AutoAdminLogon", auto), ("DefaultUserName", user), ("DefaultDomainName", host)]:
        parts += ["", key, f"    {name}{sep}REG_SZ{sep}{value}", ""]
    parts.append("DefaultPassword=" + ("present" if password_present else "absent"))
    return "\n".join(parts) + "\n"

def bundle(folder, host, ver_text, xp):
    path = os.path.join(out, folder)
    os.makedirs(os.path.join(path, "reg"), exist_ok=True)
    audit = "" if xp else "AuditSystemEvents = 0\nAuditLogonEvents = 3\nAuditObjectAccess = 0\nAuditPrivilegeUse = 0\nAuditPolicyChange = 1\nAuditAccountManage = 1\nAuditProcessTracking = 0\nAuditDSAccess = 0\nAuditAccountLogon = 1\n"
    if xp: audit = "AuditSystemEvents = 0\nAuditLogonEvents = 1\nAuditObjectAccess = 0\nAuditPrivilegeUse = 0\nAuditPolicyChange = 0\nAuditAccountManage = 0\nAuditProcessTracking = 0\nAuditDSAccess = 0\nAuditAccountLogon = 0\n"
    utf16(os.path.join(path, "secedit.inf"), secedit_inf(audit))
    utf16(os.path.join(path, "secedit.log"), "\nCompleted successfully.\n")
    steps = ["secedit"]
    if not xp:
        rows_csv = ["Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value"]
        for name, setting, value in [("Logon", "Success and Failure", 3), ("Logoff", "Success", 1), ("Credential Validation", "Success", 1),
                                     ("User Account Management", "Success", 1), ("Security Group Management", "Success", 1),
                                     ("Process Creation", "No Auditing", 0), ("Audit Policy Change", "Success", 1)]:
            rows_csv.append(f"{host},System,{name},{AUDIT[name]},{setting},,{value}")
        rows_csv.append(",,Option:CrashOnAuditFail,,Disabled,,0")
        ascii_crlf(os.path.join(path, "auditpol.csv"), "\n".join(rows_csv) + "\n")
        steps.append("auditpol")
    regs = [
        ("01-policies", [("HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies", []), ("HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft", []),
                         ("HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient", [('"EnableMulticast"', "dword:00000000")])]),
        ("02-currentversion-policies", [("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies", []),
                                        ("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer",
                                         [('"NoDriveTypeAutoRun"', "dword:00000091")]),
                                        ("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System",
                                         [('"EnableLUA"', "dword:00000001" if not xp else "dword:00000000"), ('"ConsentPromptBehaviorAdmin"', "dword:00000005"),
                                          ('"legalnoticetext"', 'hex(7):42,00,65,00,69,00,73,00,70,00,69,00,65,00,6c,00,00,00,00,00')])]),
        ("03-lsa", [("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Lsa",
                     [('"LmCompatibilityLevel"', "dword:00000003" if not xp else "dword:00000000"), ('"NoLMHash"', "dword:00000000"),
                      ('"RestrictAnonymous"', "dword:00000000"), ('"Security Packages"', 'hex(7):6b,00,65,00,72,00,62,00,65,00,72,00,6f,00,73,00,00,00,00,00')]),
                    ("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Lsa\\MSV1_0", [('"NTLMMinClientSec"', "dword:00000000")])]),
        ("04-securityproviders", [("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\SecurityProviders", [('"SecurityProviders"', '"credssp.dll"')]),
                                  ("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\WDigest", [('"Debuglevel"', "dword:00000000")])]),
        ("05-lanmanserver", [("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters",
                              [('"RequireSecuritySignature"', "dword:00000000"), ('"EnableSecuritySignature"', "dword:00000000"),
                               ('"NullSessionShares"', "hex(7):00,00")])]),
        ("06-lanmanworkstation", [("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\LanmanWorkstation\\Parameters",
                                   [('"EnablePlainTextPassword"', "dword:00000000"), ('"RequireSecuritySignature"', "dword:00000000")])]),
        ("07-netbt", [("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\NetBT\\Parameters", [('"NodeType"', "dword:00000008")])]),
        ("08-firewallpolicy", [("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\StandardProfile",
                                [('"EnableFirewall"', "dword:00000001")]),
                               ("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\DomainProfile",
                                [('"EnableFirewall"', "dword:00000000")])]),
        ("09-terminalserver", [("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Terminal Server", [('"fDenyTSConnections"', "dword:00000000")]),
                               ("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Terminal Server\\WinStations\\RDP-Tcp",
                                [('"UserAuthentication"', "dword:00000000"), ('"SecurityLayer"', "dword:00000001"), ('"PortNumber"', "dword:00000d3d")])]),
        ("10-currentversion", [("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion",
                                [('"CurrentBuild"', '"2600"' if xp else '"7601"'), ('"CSDVersion"', '"Service Pack 3"' if xp else '"Service Pack 1"'),
                                 ('"EditionID"', '"Professional"'), ('"InstallationType"', '"Client"'),
                                 ('"ProductName"', '"Microsoft Windows XP"' if xp else '"Windows 7 Professional"'),
                                 ('"InstallDate"', "dword:4d0f1a20")])]),
        ("11-usbstor", [("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\USBSTOR", [('"Start"', "dword:00000003"), ('"Type"', "dword:00000001")])]),
        ("12-mrxsmb10", [("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\mrxsmb10", [('"Start"', "dword:00000002")])]),
        ("13-productoptions", [("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\ProductOptions", [('"ProductType"', '"WinNT"')])]),
        ("14-tcpip-parameters", [("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters",
                                  [('"IPEnableRouter"', "dword:00000000"), ('"Hostname"', f'"{host}"'), ('"SearchList"', '""')])]),
    ]
    for name, keys in regs:
        utf16(os.path.join(path, "reg", f"{name}.reg"), reg_file(keys))
        steps.append(f"reg-{name.split('-')[0]}")
    services_list = [("Spooler", 2), ("RemoteRegistry", 2 if xp else 3), ("TermService", 3), ("W32Time", 2), ("wuauserv", 2), ("lanmanserver", 2),
                     ("mrxsmb10", 2), ("USBSTOR", 3)]
    ascii_crlf(os.path.join(path, "services-start.txt"), services_start(services_list, xp))
    ascii_crlf(os.path.join(path, "winlogon.txt"), winlogon(host, "1" if xp else "0", "operator", xp, xp))
    ascii_crlf(os.path.join(path, "hostname.txt"), host + "\n")
    ascii_crlf(os.path.join(path, "ver.txt"), "\n" + ver_text + "\n")
    steps += ["services", "winlogon", "hostname", "ver"]
    ascii_crlf(os.path.join(path, "exitcodes.txt"), "".join(f"{s}=0\n" for s in steps))
    print(path)

bundle("PG-ALT-01_20261010.riegel-builtin", "PG-ALT-01", "Microsoft Windows [Version 6.1.7601]", xp=False)
bundle("XP-HMI-01_20261010.riegel-builtin", "XP-HMI-01", "Microsoft Windows XP [Version 5.1.2600]", xp=True)
