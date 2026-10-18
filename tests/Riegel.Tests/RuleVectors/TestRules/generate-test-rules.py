# Erzeugt die Testregeln der Engine (Paket engine) unter tests/Riegel.Tests/RuleVectors/TestRules/rules/.
# Ein kleiner, erfundener Katalog im Vertragsformat (riegel.catalogue/1, riegel.rules/1 …): je Prüfart eine oder
# mehrere Regeln mit Testvektoren für pass, fail, fehlend, nicht erfasst, verweigert, abgeschnitten, falscher Typ und
# n/a. Er ersetzt nicht die echten Regeln der Pakete rules-a/b/c, sondern prüft die Engine unabhängig davon.
# Alle Werte sind erfunden: Registry-Schlüssel unter SOFTWARE\Policies\Riegel-Test, SIDs S-1-5-21-1000-2000-3000-<rid>,
# Produkte „Testsystem …“, Quellen unter https://example.org (RFC 2606), GUIDs 00000000-0000-4000-8000-… .
# Ausnahmen: Pfade und Wertnamen, die die Engine selbst kennt (Winlogon, Lsa\RunAsPPL, Firewall-Profile, ASR-Richtlinie),
# stehen wie im Erfassungsplan bzw. in den Belegen von docs/evaluation.md.
# Aufruf (aus dem Repo-Stamm): python tests/Riegel.Tests/RuleVectors/TestRules/generate-test-rules.py .
import base64, json, os, sys

repo = sys.argv[1]
out = os.path.join(repo, "tests", "Riegel.Tests", "RuleVectors", "TestRules", "rules")
SEEN = "2026-10-18"
SRC = "https://example.org/riegel-test"
SID = "S-1-5-21-1000-2000-3000"
CCS = "SYSTEM\\CurrentControlSet"
TEST_KEY = "HKLM\\SOFTWARE\\Policies\\Riegel-Test\\Values"
POLICY_KEY = "HKLM\\SOFTWARE\\Policies\\Riegel-Test\\Policy"
LOCAL_KEY = "HKLM\\SOFTWARE\\Policies\\Riegel-Test\\Local"

# ---------------------------------------------------------------- Kodierung (spec 7.3)
def dword(v): return {"t": "dword", "v": v}
def qword(v): return {"t": "qword", "v": str(v)}
def sz(v): return {"t": "sz", "v": v}
def msz(*v): return {"t": "multi_sz", "v": list(v)}
def binary(b): return {"t": "binary", "v": base64.b64encode(b).decode()}
def redacted(empty=False): return {"t": "sz", "redacted": True, "empty": empty}
def reg(values=None, subkeys=None): return {"values": values or {}, "subkeys": subkeys or []}
def ok(value): return {"status": "ok", "value": value}
def st(status): return {"status": status}
def trunc(value): return {"status": "ok", "value": value, "truncated": True}
def rows(*r): return {"rows": list(r)}
def out_(o): return {"out": o}
def R(path): return "reg:" + path

def machine(build=26100, ubr=6899, edition="Professional", inst="Client", pt=1, dr=0, admin=True):
    m = {"build": build}
    if ubr is not None: m["ubr"] = ubr
    if edition is not None: m["edition"] = edition
    if inst is not None: m["installationType"] = inst
    m.update({"productType": pt, "domainRole": dr, "isAdmin": admin})
    return m

CLIENT = machine()
SERVER = machine(build=20348, ubr=4294, edition="ServerStandard", inst="Server", pt=3, dr=3)
STANDALONE_SERVER = machine(build=20348, ubr=4294, edition="ServerStandard", inst="Server", pt=3, dr=2)
DC = machine(build=20348, ubr=4294, edition="ServerStandard", inst="Server", pt=2, dr=5)

def vec(name, expect, items, m=None, coverage=None, facts=None, flags=None):
    v = {"name": name, "machine": m or CLIENT}
    if coverage is not None: v["coverage"] = coverage
    if facts is not None: v["facts"] = facts
    v["items"] = items
    v["expect"] = expect
    if flags: v["expectFlags"] = flags
    return v

def applies(os_=("client", "server"), min_build=None, max_build=None, domain="any", exclude_dc=False, when=(), capability=None, absent=None):
    a = {"os": list(os_)}
    if min_build is not None: a["minBuild"] = min_build
    if max_build is not None: a["maxBuild"] = max_build
    a.update({"domain": domain, "excludeDc": exclude_dc, "when": list(when)})
    if capability: a["capability"] = capability; a["ifCapabilityAbsent"] = absent or "notApplicable"
    return a

def rule(id, title, check, tests, category="system", kind="check", app=None, severity="medium"):
    return {"id": id, "since": "2026.10", "state": "active", "category": category, "severity": severity, "kind": kind,
            "title": {"de": "Testregel: " + title[1], "en": "Test rule: " + title[0]},
            "rationale": {"de": "Prüft die Engine (erfundene Testregel).", "en": "Exercises the engine (invented test rule)."},
            "appliesTo": app or applies(), "needsAdmin": False, "check": check, "references": [], "textSources": [], "verified": [],
            "tests": tests}

def registry(key, value, vt, op, expected=None, when_missing="fail", **extra):
    c = {"type": "registry", "key": key, "value": value, "valueType": vt, "op": op}
    if expected is not None: c["expected"] = expected
    c["whenMissing"] = when_missing
    c.update(extra)
    return c

FILES = {}
def add(prefix, *rules): FILES[prefix] = list(rules)

# Wiederkehrende Einträge
def val(v): return {R(TEST_KEY): ok(reg({"Enabled": v}))}
NA_SERVER = ("n/a: Server", "notApplicable")

# ---------------------------------------------------------------- registry, registryEffective
def standard(key, value, good, bad, wrong, base_key=TEST_KEY):
    """Die acht Fälle für eine Registry-Regel mit whenMissing fail."""
    k = R(base_key)
    return [
        vec("pass", "pass", {k: ok(reg({value: good}))}),
        vec("fail", "fail", {k: ok(reg({value: bad}))}),
        vec("missing (key)", "fail", {k: st("keyMissing")}),
        vec("missing (value)", "fail", {k: ok(reg({}))}),
        vec("not collected", "notCheckable", {k: st("notCollected")}),
        vec("denied", "notCheckable", {k: st("denied")}),
        vec("truncated", "notCheckable", {k: trunc(reg({value: good}))}),
        vec("wrong type", "fail", {k: ok(reg({value: wrong}))}),
        vec("n/a: Server", "notApplicable", {k: ok(reg({value: good}))}, m=SERVER),
    ]

add("TREG",
    rule("TREG-01", ("dword eq", "dword eq"), registry(TEST_KEY, "Enabled", "dword", "eq", 1), standard(TEST_KEY, "Enabled", dword(1), dword(0), sz("1")),
         app=applies(os_=("client",))),
    rule("TREG-02", ("osDefault", "Standardwert"), registry(TEST_KEY, "Enabled", "dword", "eq", 1, "osDefault",
         osDefaults=[{"minBuild": 26100, "editions": ["Professional", "Enterprise"], "value": 1, "source": SRC + "/default", "seen": SEEN}]), [
        vec("default on 26100 Pro", "pass", {R(TEST_KEY): st("keyMissing")}, flags=["fromDefault"]),
        vec("default unknown on 22631", "notCheckable", {R(TEST_KEY): st("keyMissing")}, m=machine(build=22631)),
        vec("default unknown for Education", "notCheckable", {R(TEST_KEY): ok(reg({}))}, m=machine(edition="Education")),
        vec("set to 0", "fail", {R(TEST_KEY): ok(reg({"Enabled": dword(0)}))}),
        vec("not collected", "notCheckable", {R(TEST_KEY): st("notCollected")}),
        vec("missing outside coverage", "notCheckable", {R(TEST_KEY): st("keyMissing")}, coverage=[]),
    ]),
    rule("TREG-03", ("whenMissing pass", "whenMissing pass"), registry(TEST_KEY, "Enabled", "dword", "eq", 1, "pass",
         missingPass={"source": SRC + "/missing", "seen": SEEN, "note": "Not set means enabled (invented for the test)."}), [
        vec("missing counts as pass", "pass", {R(TEST_KEY): st("keyMissing")}, flags=["fromDefault"]),
        vec("set to 1", "pass", {R(TEST_KEY): ok(reg({"Enabled": dword(1)}))}),
        vec("set to 0", "fail", {R(TEST_KEY): ok(reg({"Enabled": dword(0)}))}),
        vec("not collected", "notCheckable", {R(TEST_KEY): st("notCollected")}),
    ]),
    rule("TREG-04", ("whenMissing notCheckable", "whenMissing notCheckable"), registry(TEST_KEY, "Enabled", "dword", "eq", 1, "notCheckable"), [
        vec("missing", "notCheckable", {R(TEST_KEY): st("keyMissing")}),
        vec("pass", "pass", {R(TEST_KEY): ok(reg({"Enabled": dword(1)}))}),
        vec("fail", "fail", {R(TEST_KEY): ok(reg({"Enabled": dword(2)}))}),
        vec("not collected", "notCheckable", {R(TEST_KEY): st("notCollected")}),
    ]),
    rule("TREG-05", ("dword with acceptTypes sz", "dword mit acceptTypes sz"), registry(TEST_KEY, "Level", "dword", "ge", 3, acceptTypes=["sz"]), [
        vec("dword 3", "pass", {R(TEST_KEY): ok(reg({"Level": dword(3)}))}),
        vec("sz 5", "pass", {R(TEST_KEY): ok(reg({"Level": sz("5")}))}),
        vec("sz 2", "fail", {R(TEST_KEY): ok(reg({"Level": sz("2")}))}),
        vec("sz unparsable", "fail", {R(TEST_KEY): ok(reg({"Level": sz("drei")}))}),
        vec("qword (not accepted)", "fail", {R(TEST_KEY): ok(reg({"Level": qword(3)}))}),
        vec("missing", "fail", {R(TEST_KEY): st("keyMissing")}),
        vec("not collected", "notCheckable", {R(TEST_KEY): st("notCollected")}),
    ]),
    rule("TREG-06", ("multi_sz containsAll", "multi_sz containsAll"), registry(TEST_KEY, "Names", "multi_sz", "containsAll", ["alpha", "beta"]), [
        vec("all contained", "pass", {R(TEST_KEY): ok(reg({"Names": msz("Beta", "alpha", "gamma")}))}),
        vec("one missing", "fail", {R(TEST_KEY): ok(reg({"Names": msz("alpha")}))}),
        vec("wrong type sz", "fail", {R(TEST_KEY): ok(reg({"Names": sz("alpha,beta")}))}),
        vec("missing", "fail", {R(TEST_KEY): ok(reg({}))}),
        vec("not collected", "notCheckable", {R(TEST_KEY): st("notCollected")}),
    ]),
    rule("TREG-07", ("multi_sz eq as list", "multi_sz eq als Liste"), registry(TEST_KEY, "Names", "multi_sz", "eq", ["alpha", "beta"], multiSz="list"), [
        vec("same order", "pass", {R(TEST_KEY): ok(reg({"Names": msz("alpha", "BETA")}))}),
        vec("other order", "fail", {R(TEST_KEY): ok(reg({"Names": msz("beta", "alpha")}))}),
        vec("missing", "fail", {R(TEST_KEY): st("keyMissing")}),
        vec("not collected", "notCheckable", {R(TEST_KEY): st("notCollected")}),
    ]),
    rule("TREG-08", ("multi_sz eq as set", "multi_sz eq als Menge"), registry(TEST_KEY, "Names", "multi_sz", "eq", ["alpha", "beta"]), [
        vec("other order", "pass", {R(TEST_KEY): ok(reg({"Names": msz("beta", "alpha", "alpha")}))}),
        vec("extra element", "fail", {R(TEST_KEY): ok(reg({"Names": msz("beta", "alpha", "gamma")}))}),
        vec("missing", "fail", {R(TEST_KEY): st("keyMissing")}),
        vec("not collected", "notCheckable", {R(TEST_KEY): st("notCollected")}),
    ]),
    rule("TREG-09", ("binary eq", "binary eq"), registry(TEST_KEY, "Blob", "binary", "eq", "hex:0a0b0c"), [
        vec("same bytes", "pass", {R(TEST_KEY): ok(reg({"Blob": binary(b"\x0a\x0b\x0c")}))}),
        vec("other bytes", "fail", {R(TEST_KEY): ok(reg({"Blob": binary(b"\x0a\x0b")}))}),
        vec("truncated value", "notCheckable", {R(TEST_KEY): ok(reg({"Blob": {**binary(b"\x0a"), "truncated": True, "len": 9000}}))}),
        vec("wrong type", "fail", {R(TEST_KEY): ok(reg({"Blob": dword(10)}))}),
        vec("missing", "fail", {R(TEST_KEY): st("keyMissing")}),
        vec("not collected", "notCheckable", {R(TEST_KEY): st("notCollected")}),
    ]),
    rule("TREG-10", ("notExists", "notExists"), registry(TEST_KEY, "Forbidden", "sz", "notExists"), [
        vec("absent", "pass", {R(TEST_KEY): ok(reg({}))}),
        vec("present", "fail", {R(TEST_KEY): ok(reg({"Forbidden": sz("x")}))}),
        vec("present with any type", "fail", {R(TEST_KEY): ok(reg({"Forbidden": dword(0)}))}),
        vec("not collected", "notCheckable", {R(TEST_KEY): st("notCollected")}),
        vec("key denied", "notCheckable", {R(TEST_KEY): st("denied")}),
    ]),
    rule("TREG-11", ("redacted value", "geschwärzter Wert"), registry(TEST_KEY, "SecretToken", "sz", "eq", "x"), [
        vec("redacted cannot be compared", "notCheckable", {R(TEST_KEY): ok(reg({"SecretToken": redacted()}))}),
        vec("missing", "fail", {R(TEST_KEY): st("keyMissing")}),
        vec("pass", "pass", {R(TEST_KEY): ok(reg({"SecretToken": sz("X")}))}),
        vec("not collected", "notCheckable", {R(TEST_KEY): st("notCollected")}),
    ]),
    rule("TREG-12", ("automatic logon (LOGON-10 trap)", "automatische Anmeldung (Falle LOGON-10)"),
         registry("HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon", "AutoAdminLogon", "sz", "ne", "1", "notCheckable"), [
        vec("autologon without DefaultPassword", "fail", {R("HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon"): ok(reg({"AutoAdminLogon": sz("1")}))}),
        vec("autologon with DefaultPassword", "fail", {R("HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon"): ok(reg({"AutoAdminLogon": sz("1"), "DefaultPassword": redacted()}))}),
        vec("no autologon", "pass", {R("HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon"): ok(reg({"AutoAdminLogon": sz("0")}))}),
        vec("missing", "notCheckable", {R("HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon"): ok(reg({}))}),
        vec("not collected", "notCheckable", {R("HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon"): st("notCollected")}),
    ], category="logon"),
    rule("TREG-13", ("registryEffective policy before local", "registryEffective Richtlinie vor lokal"),
         {"type": "registryEffective", "sources": [{"key": POLICY_KEY, "value": "Mode", "origin": "policy"}, {"key": LOCAL_KEY, "value": "Mode", "origin": "local"}],
          "valueType": "dword", "op": "eq", "expected": 1, "whenMissing": "fail"}, [
        vec("policy 1", "pass", {R(POLICY_KEY): ok(reg({"Mode": dword(1)})), R(LOCAL_KEY): ok(reg({"Mode": dword(0)}))}),
        vec("policy 0 beats local 1", "fail", {R(POLICY_KEY): ok(reg({"Mode": dword(0)})), R(LOCAL_KEY): ok(reg({"Mode": dword(1)}))}),
        vec("policy missing, local 1", "pass", {R(LOCAL_KEY): ok(reg({"Mode": dword(1)}))}),
        vec("policy missing, local 0", "fail", {R(LOCAL_KEY): ok(reg({"Mode": dword(0)}))}),
        vec("both missing", "fail", {}),
        vec("policy denied, local 1", "notCheckable", {R(POLICY_KEY): st("denied"), R(LOCAL_KEY): ok(reg({"Mode": dword(1)}))}),
        vec("policy not collected", "notCheckable", {R(POLICY_KEY): st("notCollected")}),
        vec("policy truncated", "notCheckable", {R(POLICY_KEY): trunc(reg({"Mode": dword(1)}))}),
        vec("wrong type", "fail", {R(POLICY_KEY): ok(reg({"Mode": sz("1")}))}),
        vec("n/a on a domain controller", "notApplicable", {R(POLICY_KEY): ok(reg({"Mode": dword(1)}))}, m=DC),
    ], app=applies(exclude_dc=True)),
    rule("TREG-14", ("qword le and bitsSet", "qword le und bitsSet"),
         {"type": "allOf", "checks": [registry(TEST_KEY, "Size", "qword", "le", "4294967296"), registry(TEST_KEY, "Flags", "dword", "bitsSet", 5)]}, [
        vec("both hold", "pass", {R(TEST_KEY): ok(reg({"Size": qword(4294967296), "Flags": dword(7)}))}),
        vec("size too big", "fail", {R(TEST_KEY): ok(reg({"Size": qword(4294967297), "Flags": dword(7)}))}),
        vec("bit missing", "fail", {R(TEST_KEY): ok(reg({"Size": qword(1), "Flags": dword(6)}))}),
        vec("one missing", "fail", {R(TEST_KEY): ok(reg({"Flags": dword(7)}))}),
        vec("not collected", "notCheckable", {R(TEST_KEY): st("notCollected")}),
    ]),
)

# ---------------------------------------------------------------- secedit, accountPolicy, auditSubcategory, userRight
def secedit(system_access=None, registry_values=None, rights=None):
    s = {"System Access": system_access or {}, "Registry Values": registry_values or {}, "Privilege Rights": rights or {}}
    return {"sections": s}
P = "parsed:secedit"
LBPU = "MACHINE\\System\\CurrentControlSet\\Control\\Lsa\\LimitBlankPasswordUse"
MODALS = "api:netusermodals"
RIGHTS = "api:lsa-rights"
LOGON = "{0CCE9215-69AE-11D9-BED3-505054503030}"
USERS = "api:netuserenum"
def users(*accounts): return list(accounts)
def account(name, rid, disabled=False, never=False, not_required=False, last="2026-10-14", age=30):
    return {"name": name, "sid": f"{SID}-{rid}", "rid": rid, "disabled": disabled, "passwordNotRequired": not_required,
            "passwordNeverExpires": never, "lockedOut": False, "lastLogonDate": last, "passwordAgeDays": age}
STD_USERS = users(account("Administrator", 500, disabled=True, never=True), account("Guest", 501, disabled=True, never=True),
                  account("operator", 1001))

add("TSEC",
    rule("TSEC-01", ("secedit registry value", "secedit Registry-Wert"),
         {"type": "secedit", "section": "Registry Values", "key": LBPU, "op": "eq", "expected": 1, "valueKind": "registryValue", "whenMissing": "fail"}, [
        vec("pass", "pass", {P: ok(secedit(registry_values={LBPU: {"type": 4, "value": "1"}}))}, m=SERVER),
        vec("fail", "fail", {P: ok(secedit(registry_values={LBPU: {"type": 4, "value": "0"}}))}, m=SERVER),
        vec("wrong type", "fail", {P: ok(secedit(registry_values={LBPU: {"type": 1, "value": "1"}}))}, m=SERVER),
        vec("missing", "fail", {P: ok(secedit())}, m=SERVER),
        vec("not collected", "notCheckable", {P: st("notCollected")}, m=SERVER),
        vec("denied", "notCheckable", {P: st("denied")}, m=SERVER),
        vec("truncated", "notCheckable", {P: trunc(secedit(registry_values={LBPU: {"type": 4, "value": "1"}}))}, m=SERVER),
        vec("n/a: server only", "notApplicable", {P: ok(secedit(registry_values={LBPU: {"type": 4, "value": "1"}}))}, m=CLIENT),
    ], app=applies(os_=("server",))),
    rule("TSEC-02", ("secedit number", "secedit Zahl"),
         {"type": "secedit", "section": "System Access", "key": "PasswordComplexity", "op": "eq", "expected": 1, "valueKind": "number", "whenMissing": "notCheckable"}, [
        vec("pass", "pass", {P: ok(secedit({"PasswordComplexity": "1"}))}),
        vec("fail", "fail", {P: ok(secedit({"PasswordComplexity": "0"}))}),
        vec("missing", "notCheckable", {P: ok(secedit())}),
        vec("not collected", "notCheckable", {P: st("notCollected")}),
        vec("unreadable value", "notCheckable", {P: ok(secedit({"PasswordComplexity": "ja"}))}),
    ], category="accounts"),
    rule("TSEC-03", ("accountPolicy minPwLen", "accountPolicy minPwLen"), {"type": "accountPolicy", "field": "minPwLen", "op": "ge", "expected": 14}, [
        vec("api pass", "pass", {MODALS: ok({"minPwLen": 14, "maxPwAgeDays": 365})}),
        vec("api fail", "fail", {MODALS: ok({"minPwLen": 8, "maxPwAgeDays": 365})}),
        vec("api denied, secedit pass", "pass", {MODALS: st("denied"), P: ok(secedit({"MinimumPasswordLength": "15"}))}),
        vec("api without field, secedit fail", "fail", {MODALS: ok({"maxPwAgeDays": 365}), P: ok(secedit({"MinimumPasswordLength": "0"}))}),
        vec("both not collected", "notCheckable", {MODALS: st("notCollected"), P: st("notCollected")}),
        vec("missing in secedit", "notCheckable", {MODALS: st("keyMissing"), P: ok(secedit())}),
        vec("truncated", "notCheckable", {MODALS: trunc({"minPwLen": 14}), P: st("notCollected")}),
        vec("wrong type", "notCheckable", {MODALS: ok({"minPwLen": True}), P: st("notCollected")}),
        vec("denied", "notCheckable", {MODALS: st("denied"), P: st("denied")}),
        vec("n/a: domain member", "notApplicable", {MODALS: ok({"minPwLen": 14})}, m=SERVER),
    ], category="accounts", app=applies(domain="standalone")),
    rule("TSEC-04", ("lockout threshold 0 never locks", "Sperrschwelle 0 sperrt nie"), {"type": "accountPolicy", "field": "lockoutThreshold", "op": "le", "expected": 5}, [
        vec("5", "pass", {MODALS: ok({"lockoutThreshold": 5})}),
        vec("10", "fail", {MODALS: ok({"lockoutThreshold": 10})}),
        vec("0 never locks", "fail", {MODALS: ok({"lockoutThreshold": 0})}),
        vec("secedit 0", "fail", {MODALS: st("notCollected"), P: ok(secedit({"LockoutBadCount": "0"}))}),
        vec("not collected", "notCheckable", {MODALS: st("notCollected"), P: st("notCollected")}),
    ], category="accounts"),
    rule("TSEC-05", ("maximum password age", "maximales Kennwortalter"), {"type": "accountPolicy", "field": "maxPwAgeDays", "op": "le", "expected": 365}, [
        vec("365", "pass", {MODALS: ok({"maxPwAgeDays": 365})}),
        vec("unlimited (api null)", "fail", {MODALS: ok({"maxPwAgeDays": None})}),
        vec("unlimited (secedit -1)", "fail", {MODALS: st("denied"), P: ok(secedit({"MaximumPasswordAge": "-1"}))}),
        vec("never expires (secedit 0)", "fail", {MODALS: st("denied"), P: ok(secedit({"MaximumPasswordAge": "0"}))}),
        vec("not collected", "notCheckable", {MODALS: st("notCollected"), P: st("notCollected")}),
    ], category="accounts"),
    rule("TSEC-06", ("audit subcategory Logon", "Überwachung Anmelden"), {"type": "auditSubcategory", "guid": LOGON, "expected": "successAndFailure"}, [
        vec("3", "pass", {"api:audit-policy": ok({LOGON: 3})}),
        vec("success only", "fail", {"api:audit-policy": ok({LOGON: 1})}),
        vec("guid not collected", "notCheckable", {"api:audit-policy": ok({"{0CCE9216-69AE-11D9-BED3-505054503030}": 3})}),
        vec("missing item", "notCheckable", {"api:audit-policy": st("keyMissing")}),
        vec("error", "notCheckable", {"api:audit-policy": st("error")}),
        vec("denied", "notCheckable", {"api:audit-policy": st("denied")}),
        vec("truncated", "notCheckable", {"api:audit-policy": trunc({LOGON: 3})}),
        vec("wrong type", "notCheckable", {"api:audit-policy": ok({LOGON: "Success and Failure"})}),
        vec("n/a: client only", "notApplicable", {"api:audit-policy": ok({LOGON: 3})}, m=SERVER),
    ], category="audit", app=applies(os_=("client",))),
    rule("TSEC-07", ("audit includesFailure", "Überwachung enthält Fehler"), {"type": "auditSubcategory", "guid": LOGON, "expected": "includesFailure"}, [
        vec("3", "pass", {"api:audit-policy": ok({LOGON: 3})}),
        vec("2", "pass", {"api:audit-policy": ok({LOGON: 2})}),
        vec("1", "fail", {"api:audit-policy": ok({LOGON: 1})}),
        vec("not collected", "notCheckable", {"api:audit-policy": st("notCollected")}),
        vec("missing", "notCheckable", {"api:audit-policy": st("keyMissing")}),
    ], category="audit"),
    rule("TSEC-08", ("deny network logon includes Guest", "Gast im Recht „Netzwerkanmeldung verweigern“"),
         {"type": "userRight", "right": "SeDenyNetworkLogonRight", "mode": "mustInclude", "sids": ["*-501"]}, [
        vec("api with local guest", "pass", {RIGHTS: ok({"SeDenyNetworkLogonRight": [f"{SID}-501"]}), USERS: ok(STD_USERS)}, m=SERVER),
        vec("api without guest", "fail", {RIGHTS: ok({"SeDenyNetworkLogonRight": []}), USERS: ok(STD_USERS)}, m=SERVER),
        vec("domain guest only", "fail", {RIGHTS: ok({"SeDenyNetworkLogonRight": ["S-1-5-21-4000-5000-6000-501"]}), USERS: ok(STD_USERS)}, m=SERVER),
        vec("machine SID unknown", "notCheckable", {RIGHTS: ok({"SeDenyNetworkLogonRight": [f"{SID}-501"]}), USERS: st("denied"), "wmi:root\\cimv2:Win32_UserAccount": st("denied")}, m=SERVER),
        vec("secedit with German name only", "notCheckable", {RIGHTS: st("denied"), P: ok(secedit(rights={"SeDenyNetworkLogonRight": ["Gast"]})), USERS: ok(STD_USERS)}, m=SERVER),
        vec("secedit with SID", "pass", {RIGHTS: st("denied"), P: ok(secedit(rights={"SeDenyNetworkLogonRight": [f"*{SID}-501"]})), USERS: ok(STD_USERS)}, m=SERVER),
        vec("not collected", "notCheckable", {RIGHTS: st("notCollected"), P: st("notCollected")}, m=SERVER),
        vec("missing", "notCheckable", {RIGHTS: st("keyMissing"), P: st("keyMissing")}, m=SERVER),
        vec("n/a: workgroup", "notApplicable", {RIGHTS: ok({"SeDenyNetworkLogonRight": []})}, m=STANDALONE_SERVER),
    ], category="userRights", app=applies(domain="joined")),
    rule("TSEC-09", ("debug only by Administrators", "Debuggen nur Administratoren"),
         {"type": "userRight", "right": "SeDebugPrivilege", "mode": "onlyFrom", "sids": ["S-1-5-32-544"]}, [
        vec("only admins", "pass", {RIGHTS: ok({"SeDebugPrivilege": ["S-1-5-32-544"]})}),
        vec("also a user", "fail", {RIGHTS: ok({"SeDebugPrivilege": ["S-1-5-32-544", f"{SID}-1001"]})}),
        vec("secedit name Administratoren", "notCheckable", {RIGHTS: st("denied"), P: ok(secedit(rights={"SeDebugPrivilege": ["Administratoren"]}))}),
        vec("secedit SID and foreign SID", "fail", {RIGHTS: st("denied"), P: ok(secedit(rights={"SeDebugPrivilege": ["*S-1-5-32-544", "*S-1-5-32-545", "Benutzer"]}))}),
        vec("secedit without the right", "pass", {RIGHTS: st("denied"), P: ok(secedit())}),
        vec("not collected", "notCheckable", {RIGHTS: st("notCollected"), P: st("notCollected")}),
        vec("missing", "notCheckable", {RIGHTS: st("keyMissing"), P: st("keyMissing")}),
        vec("denied", "notCheckable", {RIGHTS: st("denied"), P: st("denied")}),
        vec("truncated", "notCheckable", {RIGHTS: trunc({"SeDebugPrivilege": ["S-1-5-32-544"]}), P: st("notCollected")}),
        vec("wrong type", "notCheckable", {RIGHTS: ok({"SeDebugPrivilege": "S-1-5-32-544"}), P: ok(secedit(rights={"SeDebugPrivilege": {"x": 1}}))}),
        vec("n/a: domain controller", "notApplicable", {RIGHTS: ok({"SeDebugPrivilege": ["S-1-5-32-544"]})}, m=DC),
    ], category="userRights", app=applies(exclude_dc=True)),
    rule("TSEC-10", ("act as part of the OS: nobody", "Als Teil des Betriebssystems: niemand"),
         {"type": "userRight", "right": "SeTcbPrivilege", "mode": "empty", "sids": []}, [
        vec("empty", "pass", {RIGHTS: ok({"SeTcbPrivilege": []})}),
        vec("assigned", "fail", {RIGHTS: ok({"SeTcbPrivilege": ["S-1-5-32-544"]})}),
        vec("unresolved name only", "notCheckable", {RIGHTS: st("denied"), P: ok(secedit(rights={"SeTcbPrivilege": ["dienstkonto"]}))}),
        vec("not collected", "notCheckable", {RIGHTS: st("notCollected"), P: st("notCollected")}),
        vec("missing", "notCheckable", {RIGHTS: st("keyMissing"), P: st("keyMissing")}),
    ], category="userRights"),
    rule("TSEC-11", ("remote logon not for Everyone", "Remoteanmeldung nicht für Jeder"),
         {"type": "userRight", "right": "SeRemoteInteractiveLogonRight", "mode": "mustNotInclude", "sids": ["S-1-1-0"]}, [
        vec("admins only", "pass", {RIGHTS: ok({"SeRemoteInteractiveLogonRight": ["S-1-5-32-544"]})}),
        vec("everyone", "fail", {RIGHTS: ok({"SeRemoteInteractiveLogonRight": ["S-1-1-0"]})}),
        vec("unresolved name", "notCheckable", {RIGHTS: st("denied"), P: ok(secedit(rights={"SeRemoteInteractiveLogonRight": ["Jeder"]}))}),
        vec("not collected", "notCheckable", {RIGHTS: st("notCollected"), P: st("notCollected")}),
        vec("missing", "notCheckable", {RIGHTS: st("keyMissing"), P: st("keyMissing")}),
    ], category="userRights"),
)

# ---------------------------------------------------------------- service, feature, wmiProperty
SVC = "wmi:root\\cimv2:Win32_Service"
FEAT = "wmi:root\\cimv2:Win32_OptionalFeature"
SMB = "wmi:root\\Microsoft\\Windows\\SMB:MSFT_SmbServerConfiguration.GetConfiguration"
VOL = "wmi:root\\cimv2\\Security\\MicrosoftVolumeEncryption:Win32_EncryptableVolume"
QFE = "wmi:root\\cimv2:Win32_QuickFixEngineering"
def service(name, state, start, display=None): return {"Name": name, "DisplayName": display or name, "State": state, "StartMode": start}
SERVICES_COVERAGE = [{"root": "HKLM\\SYSTEM\\CurrentControlSet\\Services", "recursive": True, "maxDepth": 1, "complete": True, "deniedSubkeys": []}]

add("TSYS",
    rule("TSYS-01", ("service disabled", "Dienst deaktiviert"), {"type": "service", "name": "RemoteRegistry", "startMode": ["Disabled"], "ifAbsent": "pass"}, [
        vec("disabled", "pass", {SVC: ok(rows(service("RemoteRegistry", "Stopped", "Disabled")))}),
        vec("case-insensitive name", "pass", {SVC: ok(rows(service("remoteregistry", "Stopped", "Disabled")))}),
        vec("auto", "fail", {SVC: ok(rows(service("RemoteRegistry", "Running", "Auto")))}),
        vec("absent", "pass", {SVC: ok(rows(service("Spooler", "Running", "Auto")))}),
        vec("not collected, no registry", "notCheckable", {SVC: st("notCollected")}),
        vec("denied", "notCheckable", {SVC: st("denied")}),
        vec("truncated", "notCheckable", {SVC: trunc(rows(service("RemoteRegistry", "Stopped", "Disabled")))}),
        vec("missing item", "notCheckable", {SVC: st("keyMissing")}),
        vec("wrong type", "notCheckable", {SVC: ok({"value": 1})}),
        vec("builtin: registry Start 4", "pass", {SVC: st("notCollected"), R("HKLM\\SYSTEM\\CurrentControlSet\\Services\\RemoteRegistry"): ok(reg({"Start": dword(4)}))}, coverage=SERVICES_COVERAGE),
        vec("builtin: registry Start 2", "fail", {SVC: st("notCollected"), R("HKLM\\SYSTEM\\CurrentControlSet\\Services\\RemoteRegistry"): ok(reg({"Start": dword(2)}))}, coverage=SERVICES_COVERAGE),
        vec("builtin: service key absent", "pass", {SVC: st("notCollected")}, coverage=SERVICES_COVERAGE),
        vec("n/a: client only", "notApplicable", {SVC: ok(rows())}, m=SERVER),
    ], category="services", app=applies(os_=("client",))),
    rule("TSYS-02", ("service stopped", "Dienst gestoppt"), {"type": "service", "name": "Spooler", "state": ["Stopped"], "ifAbsent": "notApplicable"}, [
        vec("stopped", "pass", {SVC: ok(rows(service("Spooler", "Stopped", "Disabled")))}),
        vec("running", "fail", {SVC: ok(rows(service("Spooler", "Running", "Auto")))}),
        vec("absent", "notApplicable", {SVC: ok(rows())}),
        vec("builtin: no running state", "notCheckable", {SVC: st("notCollected"), R("HKLM\\SYSTEM\\CurrentControlSet\\Services\\Spooler"): ok(reg({"Start": dword(4)}))}, coverage=SERVICES_COVERAGE),
        vec("not collected", "notCheckable", {SVC: st("notCollected")}),
        vec("missing", "notCheckable", {SVC: st("keyMissing")}),
    ], category="services"),
    rule("TSYS-03", ("feature disabled or absent", "Feature deaktiviert oder nicht vorhanden"), {"type": "feature", "name": "SMB1Protocol", "installState": [2, 3], "ifAbsent": "pass"}, [
        vec("disabled", "pass", {FEAT: ok(rows({"Name": "SMB1Protocol", "InstallState": 2}))}),
        vec("enabled", "fail", {FEAT: ok(rows({"Name": "SMB1Protocol", "InstallState": 1}))}),
        vec("absent row", "pass", {FEAT: ok(rows({"Name": "TelnetClient", "InstallState": 2}))}),
        vec("not collected", "notCheckable", {FEAT: st("notCollected")}),
        vec("missing", "notCheckable", {FEAT: st("keyMissing")}),
        vec("denied", "notCheckable", {FEAT: st("denied")}),
        vec("truncated", "notCheckable", {FEAT: trunc(rows({"Name": "SMB1Protocol", "InstallState": 2}))}),
        vec("wrong type", "notCheckable", {FEAT: ok(rows({"Name": "SMB1Protocol", "InstallState": "Disabled"}))}),
        vec("n/a: build", "notApplicable", {FEAT: ok(rows())}, m=machine(build=7601, ubr=None)),
    ], category="network", app=applies(min_build=9200)),
    rule("TSYS-04", ("WMI method value", "WMI-Methodenwert"),
         {"type": "wmiProperty", "item": SMB, "property": "RequireSecuritySignature", "rows": "first", "op": "eq", "expected": True, "ifNoRows": "notCheckable"}, [
        vec("required", "pass", {SMB: ok(out_({"RequireSecuritySignature": True}))}, m=SERVER),
        vec("not required", "fail", {SMB: ok(out_({"RequireSecuritySignature": False}))}, m=SERVER),
        vec("namespace missing", "notCheckable", {SMB: st("namespaceMissing")}, m=SERVER),
        vec("not collected", "notCheckable", {SMB: st("notCollected")}, m=SERVER),
        vec("missing", "notCheckable", {SMB: st("keyMissing")}, m=SERVER),
        vec("denied", "notCheckable", {SMB: st("denied")}, m=SERVER),
        vec("truncated", "notCheckable", {SMB: trunc(out_({"RequireSecuritySignature": True}))}, m=SERVER),
        vec("wrong type", "notCheckable", {SMB: ok(out_({"RequireSecuritySignature": "yes"}))}, m=SERVER),
        vec("property absent", "notCheckable", {SMB: ok(out_({}))}, m=SERVER),
        vec("n/a: server only", "notApplicable", {SMB: ok(out_({"RequireSecuritySignature": True}))}),
    ], category="network", app=applies(os_=("server",))),
    rule("TSYS-05", ("WMI rows with filter", "WMI-Zeilen mit Filter"),
         {"type": "wmiProperty", "item": VOL, "property": "ProtectionStatus", "rowFilter": {"property": "DriveLetter", "op": "eq", "value": "C:"},
          "rows": "all", "op": "eq", "expected": 1, "ifNoRows": "fail"}, [
        vec("C: protected", "pass", {VOL: ok(rows({"DriveLetter": "C:", "ProtectionStatus": 1}, {"DriveLetter": "D:", "ProtectionStatus": 0}))}),
        vec("C: unprotected", "fail", {VOL: ok(rows({"DriveLetter": "C:", "ProtectionStatus": 0}))}),
        vec("no C:", "fail", {VOL: ok(rows({"DriveLetter": "D:", "ProtectionStatus": 1}))}),
        vec("denied (non-admin)", "notCheckable", {VOL: st("denied")}),
        vec("not collected", "notCheckable", {VOL: st("notCollected")}),
        vec("missing", "notCheckable", {VOL: st("keyMissing")}),
    ], category="encryption"),
    rule("TSYS-06", ("WMI date ageDaysLe", "WMI-Datum ageDaysLe"),
         {"type": "wmiProperty", "item": QFE, "property": "InstalledOn", "rows": "any", "op": "ageDaysLe", "expected": 35, "ifNoRows": "notCheckable"}, [
        vec("recent hotfix", "pass", {QFE: ok(rows({"HotFixID": "KB0000001", "InstalledOn": "2026-09-01T00:00:00Z"}, {"HotFixID": "KB0000002", "InstalledOn": "2026-10-14T00:00:00Z"}))}),
        vec("old hotfixes", "fail", {QFE: ok(rows({"HotFixID": "KB0000001", "InstalledOn": "2026-08-01T00:00:00Z"}))}),
        vec("no rows", "notCheckable", {QFE: ok(rows())}),
        vec("not collected", "notCheckable", {QFE: st("notCollected")}),
        vec("missing", "notCheckable", {QFE: st("keyMissing")}),
        vec("unparsable date", "notCheckable", {QFE: ok(rows({"HotFixID": "KB0000001", "InstalledOn": "gestern"}))}),
    ], category="lifecycle"),
)

# ---------------------------------------------------------------- defenderSetting, asrRule, avStatus (Sperre nach spec 2.5)
MPS = "wmi:root\\Microsoft\\Windows\\Defender:MSFT_MpComputerStatus"
MPP = "wmi:root\\Microsoft\\Windows\\Defender:MSFT_MpPreference"
SC2 = "wmi:root\\SecurityCenter2:AntivirusProduct"
WSC = "api:wsc-av"
ASR_GUID = "00000000-0000-4000-8000-0000000000a1"
ASR_POLICY = "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Windows Defender Exploit Guard\\ASR\\Rules"
def status(mode="Normal", av=True, rtp=True, updated="2026-10-17T04:00:00Z", **extra):
    r = {"AMServiceEnabled": True, "AntivirusEnabled": av, "RealTimeProtectionEnabled": rtp, "AntivirusSignatureLastUpdated": updated}
    if mode is not None: r["AMRunningMode"] = mode
    r.update(extra)
    return ok(rows(r))
def wsc(*products): return ok([{"name": n, "state": s, "signature": g} for n, s, g in products])
DEFENDER_ONLY = wsc(("Windows Defender", "on", "upToDate"))
THIRD_PARTY = wsc(("Beispiel-Virenschutz (fiktiv)", "on", "upToDate"), ("Windows Defender", "off", "outOfDate"))
NO_AV = wsc(("Windows Defender", "off", "outOfDate"))
def pref(ids=None, actions=None): return ok(rows({"AttackSurfaceReductionRules_Ids": ids, "AttackSurfaceReductionRules_Actions": actions}))

def gating(item_ok, extra=None):
    """Die Zeilen der Sperrtabelle (spec 2.5) für eine Defender-Einstellung."""
    e = extra or {}
    return [
        vec("active", "pass", {MPS: status(), WSC: DEFENDER_ONLY, **e}),
        vec("active, setting off", "fail", {MPS: status(rtp=False), WSC: DEFENDER_ONLY, **e}),
        vec("passive + third-party AV", "notApplicable", {MPS: status("Passive", av=False, rtp=False), WSC: THIRD_PARTY, **e}),
        vec("EDR block mode + third-party AV", "notApplicable", {MPS: status("EDR Block Mode", av=False, rtp=False), WSC: THIRD_PARTY, **e}),
        vec("passive, no other AV", "fail", {MPS: status("Passive", av=False, rtp=False), WSC: NO_AV, **e}),
        vec("off, no other AV", "fail", {MPS: status(None, av=False, rtp=False), WSC: NO_AV, **e}, flags=["derived"]),
        vec("off + third-party AV", "notApplicable", {MPS: status(None, av=False, rtp=False), WSC: THIRD_PARTY, **e}, flags=["derived"]),
        vec("absent + third-party AV", "notApplicable", {MPS: st("namespaceMissing"), WSC: THIRD_PARTY, **e}),
        vec("absent, no AV", "fail", {MPS: st("namespaceMissing"), WSC: ok([]), **e}),
        vec("unknown (denied)", "notCheckable", {MPS: st("denied"), WSC: DEFENDER_ONLY, **e}),
        vec("unknown (not collected)", "notCheckable", {MPS: st("notCollected"), WSC: DEFENDER_ONLY, **e}),
        vec("unknown (missing)", "notCheckable", {MPS: st("keyMissing"), WSC: DEFENDER_ONLY, **e}),
        vec("unknown (truncated)", "notCheckable", {MPS: trunc(rows({"AMRunningMode": "Normal"})), WSC: DEFENDER_ONLY, **e}),
        vec("passive, third-party unknown", "notCheckable", {MPS: status("Passive", av=False, rtp=False), WSC: st("notCollected"), SC2: st("namespaceMissing"), **e}),
        vec("derived active", "pass", {MPS: status(None), WSC: DEFENDER_ONLY, **e}, flags=["derived"]),
        vec("third-party only heuristic", "notApplicable", {MPS: status("Passive", av=False, rtp=False), WSC: st("notCollected"),
                                                            SC2: ok(rows({"displayName": "Beispiel-Virenschutz (fiktiv)", "productState": 266240}))}, flags=["heuristic"]),
        vec("wrong type", "notCheckable", {MPS: status(rtp="yes"), WSC: DEFENDER_ONLY, **e}) if item_ok else None,
        vec("vector fact override passive", "notApplicable", {MPS: status(), WSC: DEFENDER_ONLY, **e},
            facts={"defender.mode": "passive", "av.thirdPartyActive": True}),
    ]

add("TDEF",
    rule("TDEF-01", ("real-time protection", "Echtzeitschutz"), {"type": "defenderSetting", "source": "status", "property": "RealTimeProtectionEnabled", "op": "eq", "expected": True},
         [v for v in gating(True) if v], category="defender"),
    rule("TDEF-02", ("signature age (LIFE-04 style)", "Alter der Signaturen (wie LIFE-04)"),
         {"type": "defenderSetting", "source": "status", "property": "AntivirusSignatureLastUpdated", "op": "ageDaysLe", "expected": 7}, [
        vec("1 day old", "pass", {MPS: status(updated="2026-10-17T04:00:00Z"), WSC: DEFENDER_ONLY}),
        vec("30 days old", "fail", {MPS: status(updated="2026-09-18T04:00:00Z"), WSC: DEFENDER_ONLY}),
        vec("not collected", "notCheckable", {MPS: st("notCollected"), WSC: DEFENDER_ONLY}),
        vec("missing", "notCheckable", {MPS: st("keyMissing"), WSC: DEFENDER_ONLY}),
        vec("unparsable", "notCheckable", {MPS: status(updated="gestern"), WSC: DEFENDER_ONLY}),
        vec("passive + third-party AV", "notApplicable", {MPS: status("Passive", av=False, rtp=False), WSC: THIRD_PARTY}),
    ], category="lifecycle"),
    rule("TDEF-03", ("ASR rule block", "ASR-Regel blockieren"), {"type": "asrRule", "guid": ASR_GUID}, [
        vec("block, active, RTP on", "pass", {MPS: status(), WSC: DEFENDER_ONLY, MPP: pref([ASR_GUID.upper()], [1])}),
        vec("audit only", "fail", {MPS: status(), WSC: DEFENDER_ONLY, MPP: pref([ASR_GUID], [2])}),
        vec("warn only", "fail", {MPS: status(), WSC: DEFENDER_ONLY, MPP: pref([ASR_GUID], [6])}),
        vec("off", "fail", {MPS: status(), WSC: DEFENDER_ONLY, MPP: pref([ASR_GUID], [0])}),
        vec("not configured (no rules)", "fail", {MPS: status(), WSC: DEFENDER_ONLY, MPP: pref()}),
        vec("not configured (other rule)", "fail", {MPS: status(), WSC: DEFENDER_ONLY, MPP: pref(["00000000-0000-4000-8000-0000000000a2"], [1])}),
        vec("block but RTP off", "fail", {MPS: status(rtp=False), WSC: DEFENDER_ONLY, MPP: pref([ASR_GUID], [1])}),
        vec("passive + third-party AV", "notApplicable", {MPS: status("Passive", av=False, rtp=False), WSC: THIRD_PARTY, MPP: pref([ASR_GUID], [1])}),
        vec("passive, no AV", "fail", {MPS: status("Passive", av=False, rtp=False), WSC: NO_AV, MPP: pref([ASR_GUID], [1])}),
        vec("absent, no AV: n/a for a single ASR rule", "notApplicable", {MPS: st("namespaceMissing"), WSC: ok([])}),
        vec("preference denied, policy block", "notCheckable", {MPS: status(), WSC: DEFENDER_ONLY, MPP: st("denied"), R(ASR_POLICY): ok(reg({ASR_GUID: sz("1")}))}, flags=["configuredOnly"]),
        vec("preference denied, policy audit", "fail", {MPS: status(), WSC: DEFENDER_ONLY, MPP: st("denied"), R(ASR_POLICY): ok(reg({ASR_GUID: sz("2")}))}),
        vec("preference denied, nothing in policy", "notCheckable", {MPS: status(), WSC: DEFENDER_ONLY, MPP: st("denied")}),
        vec("preference not collected", "notCheckable", {MPS: status(), WSC: DEFENDER_ONLY, MPP: st("notCollected")}),
        vec("preference missing", "notCheckable", {MPS: status(), WSC: DEFENDER_ONLY, MPP: st("keyMissing")}),
        vec("preference truncated", "notCheckable", {MPS: status(), WSC: DEFENDER_ONLY, MPP: trunc(rows({"AttackSurfaceReductionRules_Ids": [ASR_GUID], "AttackSurfaceReductionRules_Actions": [1]}))}),
        vec("undocumented mode", "notCheckable", {MPS: status(), WSC: DEFENDER_ONLY, MPP: pref([ASR_GUID], [3])}),
        vec("wrong type", "notCheckable", {MPS: status(), WSC: DEFENDER_ONLY, MPP: pref(ASR_GUID, [1])}),
        vec("n/a: capability absent", "notApplicable", {MPS: status(), WSC: DEFENDER_ONLY, MPP: pref([ASR_GUID], [1])}, m=machine(build=15063, ubr=None)),
    ], category="defender", app=applies(capability="testAsr", absent="notApplicable")),
    rule("TDEF-04", ("ASR rule block or warn", "ASR-Regel blockieren oder warnen"), {"type": "asrRule", "guid": ASR_GUID, "accept": "blockOrWarn"}, [
        vec("warn", "pass", {MPS: status(), WSC: DEFENDER_ONLY, MPP: pref([ASR_GUID], [6])}),
        vec("audit", "fail", {MPS: status(), WSC: DEFENDER_ONLY, MPP: pref([ASR_GUID], [2])}),
        vec("not collected", "notCheckable", {MPS: status(), WSC: DEFENDER_ONLY, MPP: st("notCollected")}),
        vec("missing", "notCheckable", {MPS: status(), WSC: DEFENDER_ONLY, MPP: st("keyMissing")}),
    ], category="defender"),
    rule("TDEF-05", ("ASR configured at all (DEF-09 style)", "ASR überhaupt konfiguriert (wie DEF-09)"), {"type": "asrRule", "guid": ASR_GUID, "accept": "blockOrAudit"}, [
        vec("audit", "pass", {MPS: status(), WSC: DEFENDER_ONLY, MPP: pref([ASR_GUID], [2])}),
        vec("absent, no AV: protection gap", "fail", {MPS: st("namespaceMissing"), WSC: ok([])}),
        vec("absent + third-party AV", "notApplicable", {MPS: st("namespaceMissing"), WSC: THIRD_PARTY}),
        vec("capability absent: fail", "fail", {MPS: st("namespaceMissing"), WSC: ok([])}, m=machine(build=15063, ubr=None)),
        vec("not collected", "notCheckable", {MPS: st("notCollected"), WSC: DEFENDER_ONLY}),
        vec("missing", "notCheckable", {MPS: st("keyMissing"), WSC: DEFENDER_ONLY}),
    ], category="defender", app=applies(capability="testAsr", absent="fail")),
    rule("TDEF-06", ("an antivirus is active and current (DEF-18 style)", "ein Virenschutz ist aktiv und aktuell (wie DEF-18)"), {"type": "avStatus"}, [
        vec("Defender current", "pass", {MPS: status(updated="2026-10-17T04:00:00Z"), WSC: DEFENDER_ONLY}),
        vec("Defender old signatures", "fail", {MPS: status(updated="2026-09-01T04:00:00Z"), WSC: DEFENDER_ONLY}),
        vec("Defender RTP off", "fail", {MPS: status(rtp=False), WSC: DEFENDER_ONLY}),
        vec("third-party current (WSC)", "pass", {MPS: status("Passive", av=False, rtp=False), WSC: THIRD_PARTY}),
        vec("third-party out of date (WSC)", "fail", {MPS: status("Passive", av=False, rtp=False), WSC: wsc(("Beispiel-Virenschutz (fiktiv)", "on", "outOfDate"))}),
        vec("WSC: nothing on", "fail", {MPS: status("Passive", av=False, rtp=False), WSC: NO_AV}),
        vec("heuristic only (Windows 7)", "notCheckable", {MPS: st("namespaceMissing"), WSC: st("notCollected"),
                                                          SC2: ok(rows({"displayName": "Beispiel-Virenschutz (fiktiv)", "productState": 266240}))}, flags=["heuristic"]),
        vec("no AV at all", "fail", {MPS: st("namespaceMissing"), WSC: st("notCollected"), SC2: ok(rows())}),
        vec("server without security center", "notCheckable", {MPS: st("namespaceMissing"), WSC: st("notCollected"), SC2: st("namespaceMissing")}, m=SERVER),
        vec("Defender denied, WSC current", "pass", {MPS: st("denied"), WSC: DEFENDER_ONLY}),
        vec("everything not collected", "notCheckable", {MPS: st("notCollected"), WSC: st("notCollected"), SC2: st("notCollected")}),
        vec("missing", "notCheckable", {MPS: st("keyMissing"), WSC: st("keyMissing"), SC2: st("keyMissing")}),
        vec("truncated", "notCheckable", {MPS: trunc(rows({"AMRunningMode": "Normal"})), WSC: trunc([]), SC2: trunc(rows())}),
        vec("wrong type", "notCheckable", {MPS: status(updated=12345, AntivirusSignatureAge="neu"), WSC: DEFENDER_ONLY}),
        vec("everything denied", "notCheckable", {MPS: st("denied"), WSC: st("denied"), SC2: st("denied")}),
        vec("n/a: domain controller", "notApplicable", {MPS: status(), WSC: DEFENDER_ONLY}, m=DC),
    ], category="defender", severity="high", app=applies(exclude_dc=True)),
)

# ---------------------------------------------------------------- firewallProfile, firewallExposure
FW = "api:firewall"
FWR = "parsed:firewall-rules-registry"
FW_LOCAL = "HKLM\\SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy"
FW_GPO = "HKLM\\SOFTWARE\\Policies\\Microsoft\\WindowsFirewall"
def firewall(domain=(True, "block"), private=(True, "block"), public=(True, "block"), current=2, rules=None, collected=True):
    def p(x): return {"enabled": x[0], "defaultInbound": x[1], "defaultOutbound": "allow"}
    return ok({"currentProfiles": current, "profiles": {"domain": p(domain), "private": p(private), "public": p(public)},
               "rulesCollected": collected, "rules": rules or []})
def com_rule(name, port, remote="*", enabled=True, profiles=7, protocol=6, action="allow", direction="in"):
    return {"name": name, "enabled": enabled, "direction": direction, "action": action, "protocol": protocol, "localPorts": port,
            "remoteAddresses": remote, "profiles": profiles, "application": None, "service": None}
def reg_rule(name, port, store="local", ra=None, profiles=None, active=True, protocol="6"):
    r = {"store": store, "name": name, "action": "Allow", "active": active, "dir": "In", "protocol": protocol, "lports": [port]}
    if ra is not None: r["ra"] = ra
    if profiles is not None: r["profiles"] = profiles
    return r

add("TNET",
    rule("TNET-01", ("public profile on", "öffentliches Profil an"), {"type": "firewallProfile", "profile": "public", "property": "enabled", "expected": True}, [
        vec("on", "pass", {FW: firewall()}),
        vec("off", "fail", {FW: firewall(public=(False, "block"))}),
        vec("not collected", "notCheckable", {FW: st("notCollected")}),
        vec("missing", "notCheckable", {FW: st("keyMissing")}),
        vec("denied", "notCheckable", {FW: st("denied")}),
        vec("truncated", "notCheckable", {FW: {**firewall(), "truncated": True}}),
        vec("wrong type", "notCheckable", {FW: ok({"currentProfiles": 2, "profiles": {"public": {"enabled": "ja"}}})}),
        vec("builtin: no runtime, only registry", "notCheckable", {FW: st("notCollected"), R(FW_LOCAL + "\\PublicProfile"): ok(reg({"EnableFirewall": dword(1)}))}),
        vec("n/a: domain controller", "notApplicable", {FW: firewall()}, m=DC),
    ], category="firewall", severity="high", app=applies(exclude_dc=True)),
    rule("TNET-02", ("domain profile blocks inbound", "Domänenprofil blockiert eingehend"), {"type": "firewallProfile", "profile": "domain", "property": "defaultInboundBlock", "expected": True}, [
        vec("block", "pass", {FW: firewall()}),
        vec("allow", "fail", {FW: firewall(domain=(True, "allow"))}),
        vec("not collected", "notCheckable", {FW: st("notCollected")}),
        vec("missing", "notCheckable", {FW: st("keyMissing")}),
    ], category="firewall"),
    rule("TNET-03", ("public profile logs dropped packets", "öffentliches Profil protokolliert verworfene Pakete"),
         {"type": "firewallProfile", "profile": "public", "property": "logDropped", "expected": True}, [
        vec("policy 1", "pass", {R(FW_GPO + "\\PublicProfile\\Logging"): ok(reg({"LogDroppedPackets": dword(1)})), R(FW_LOCAL + "\\PublicProfile\\Logging"): ok(reg({"LogDroppedPackets": dword(0)}))}),
        vec("policy 0 beats local 1", "fail", {R(FW_GPO + "\\PublicProfile\\Logging"): ok(reg({"LogDroppedPackets": dword(0)})), R(FW_LOCAL + "\\PublicProfile\\Logging"): ok(reg({"LogDroppedPackets": dword(1)}))}),
        vec("local 1", "pass", {R(FW_LOCAL + "\\PublicProfile\\Logging"): ok(reg({"LogDroppedPackets": dword(1)}))}),
        vec("not configured", "fail", {}),
        vec("policy denied", "notCheckable", {R(FW_GPO + "\\PublicProfile\\Logging"): st("denied"), R(FW_LOCAL + "\\PublicProfile\\Logging"): ok(reg({"LogDroppedPackets": dword(1)}))}),
        vec("local not collected", "notCheckable", {R(FW_LOCAL + "\\PublicProfile\\Logging"): st("notCollected")}),
        vec("truncated", "notCheckable", {R(FW_LOCAL + "\\PublicProfile\\Logging"): trunc(reg({"LogDroppedPackets": dword(1)}))}),
        vec("wrong type", "fail", {R(FW_LOCAL + "\\PublicProfile\\Logging"): ok(reg({"LogDroppedPackets": sz("1")}))}),
    ], category="firewall", severity="low"),
    rule("TNET-04", ("public profile ignores local rules", "öffentliches Profil ignoriert lokale Regeln"),
         {"type": "firewallProfile", "profile": "public", "property": "allowLocalRules", "expected": False}, [
        vec("policy 0", "pass", {R(FW_GPO + "\\PublicProfile"): ok(reg({"AllowLocalPolicyMerge": dword(0)}))}),
        vec("policy 1", "fail", {R(FW_GPO + "\\PublicProfile"): ok(reg({"AllowLocalPolicyMerge": dword(1)}))}),
        vec("not configured", "fail", {}),
        vec("not collected", "notCheckable", {R(FW_GPO + "\\PublicProfile"): st("notCollected")}),
    ], category="firewall"),
    rule("TNET-05", ("RDP/SMB not open to any address (FW-11 style)", "RDP/SMB nicht für beliebige Adressen offen (wie FW-11)"),
         {"type": "firewallExposure", "ports": [{"protocol": "tcp", "port": 3389}, {"protocol": "tcp", "port": 445}], "profiles": "all"}, [
        vec("only restricted rules", "pass", {FW: firewall(rules=[com_rule("OPC UA", "4840"), com_rule("RDP admin net", "3389", remote="192.0.2.0/255.255.255.0")]), FWR: ok([])}),
        vec("RDP from any address", "fail", {FW: firewall(rules=[com_rule("Remote Desktop", "3389")]), FWR: ok([])}),
        vec("disabled RDP rule", "pass", {FW: firewall(rules=[com_rule("Remote Desktop", "3389", enabled=False)]), FWR: ok([])}),
        vec("port range covers 445", "fail", {FW: firewall(rules=[com_rule("Range", "400-500")]), FWR: ok([])}),
        vec("keyword ports do not match", "pass", {FW: firewall(rules=[com_rule("RPC", "RPC"), com_rule("EPMap", "RPC-EPMap")]), FWR: ok([])}),
        vec("udp rule does not open tcp", "pass", {FW: firewall(rules=[com_rule("UDP", "445", protocol=17)]), FWR: ok([])}),
        vec("profile off", "fail", {FW: firewall(public=(False, "block")), FWR: ok([])}),
        vec("inbound allowed by default", "fail", {FW: firewall(private=(True, "allow")), FWR: ok([])}),
        vec("GPO store rule", "fail", {FW: firewall(), FWR: ok([reg_rule("GPO SMB", "445", store="gpo")])}),
        vec("local store rule, merge on", "fail", {FW: firewall(), FWR: ok([reg_rule("Local SMB", "445", profiles=["Public"])])}),
        vec("local store rule, merge off", "pass", {FW: firewall(), FWR: ok([reg_rule("Local SMB", "445", profiles=["Public"])]),
                                                  R(FW_GPO + "\\PublicProfile"): ok(reg({"AllowLocalPolicyMerge": dword(0)}))}),
        vec("local store rule with remote address", "pass", {FW: firewall(), FWR: ok([reg_rule("Local SMB", "445", ra=["192.0.2.0/24"])])}),
        vec("registry rules missing", "notCheckable", {FW: firewall(rules=[com_rule("OPC UA", "4840")]), FWR: st("notCollected")}),
        vec("registry rules missing but COM exposes", "fail", {FW: firewall(rules=[com_rule("Remote Desktop", "3389")]), FWR: st("notCollected")}),
        vec("rules not collected (--minimal)", "notCheckable", {FW: firewall(collected=False), FWR: ok([])}),
        vec("firewall not collected", "notCheckable", {FW: st("notCollected"), FWR: ok([])}),
        vec("firewall missing", "notCheckable", {FW: st("keyMissing"), FWR: ok([])}),
        vec("firewall denied", "notCheckable", {FW: st("denied"), FWR: ok([])}),
        vec("firewall truncated", "notCheckable", {FW: {**firewall(), "truncated": True}, FWR: ok([])}),
        vec("unknown protocol shape", "notCheckable", {FW: firewall(rules=[com_rule("Odd", "3389", protocol="SCTP")]), FWR: ok([])}),
        vec("wrong type", "notCheckable", {FW: ok({"currentProfiles": 2, "profiles": "alle", "rulesCollected": True, "rules": []}), FWR: ok([])}),
        vec("n/a: domain controller", "notApplicable", {FW: firewall(), FWR: ok([])}, m=DC),
    ], category="firewall", app=applies(exclude_dc=True)),
    rule("TNET-06", ("exposure on active profiles only", "Freigabe nur in aktiven Profilen"),
         {"type": "firewallExposure", "ports": [{"protocol": "tcp", "port": 5985}], "profiles": "active"}, [
        vec("rule only for the inactive domain profile", "pass", {FW: firewall(current=2, rules=[com_rule("WinRM", "5985", profiles=1)]), FWR: ok([])}),
        vec("rule for the active private profile", "fail", {FW: firewall(current=2, rules=[com_rule("WinRM", "5985", profiles=2)]), FWR: ok([])}),
        vec("inactive profile off does not count", "pass", {FW: firewall(current=2, domain=(False, "block")), FWR: ok([])}),
        vec("active profile unknown", "notCheckable", {FW: firewall(current=0), FWR: ok([])}),
        vec("not collected", "notCheckable", {FW: st("notCollected"), FWR: ok([])}),
        vec("missing", "notCheckable", {FW: st("keyMissing"), FWR: ok([])}),
    ], category="firewall"),
)

# ---------------------------------------------------------------- lifecycle, updateAge (Tabellen in diesem Testkatalog)
WU = "api:windows-update-history"
add("TTIM",
    rule("TTIM-01", ("supported operating system", "unterstütztes Betriebssystem"), {"type": "lifecycle"}, [
        vec("supported", "pass", {}, m=machine(build=99001, ubr=300)),
        vec("support ended, no ESU", "fail", {}, m=machine(build=99001, ubr=300, edition="Enterprise")),
        vec("older than every entry", "fail", {}, m=machine(build=98000, ubr=1)),
        vec("unknown build", "notCheckable", {}, m=machine(build=99500, ubr=1)),
        vec("edition unknown", "notCheckable", {}, m=machine(build=99001, ubr=300, edition=None)),
        vec("server entry", "pass", {}, m=machine(build=99001, ubr=300, edition="ServerStandard", inst="Server", pt=3, dr=2)),
    ], category="lifecycle", severity="high"),
    rule("TTIM-02", ("days since last cumulative update", "Tage seit dem letzten kumulativen Update"), {"type": "updateAge", "maxDays": 35}, [
        vec("current", "pass", {}, m=machine(build=99001, ubr=300)),
        vec("68 days old", "fail", {}, m=machine(build=99001, ubr=100)),
        vec("newer than the table", "notCheckable", {QFE: ok(rows({"HotFixID": "KB0000009", "InstalledOn": "2026-10-16T00:00:00Z"}))}, m=machine(build=99001, ubr=400)),
        vec("older than the oldest entry", "fail", {}, m=machine(build=99001, ubr=50)),
        vec("between entries, at most 40 days: undecided, QFE recent", "pass", {QFE: ok(rows({"HotFixID": "KB0000009", "InstalledOn": "2026-10-10T00:00:00Z"}))}, m=machine(build=99001, ubr=250)),
        vec("between entries, QFE not collected", "notCheckable", {QFE: st("notCollected")}, m=machine(build=99001, ubr=250)),
        vec("between 100 and 200: at least 40 days", "fail", {}, m=machine(build=99001, ubr=150)),
        vec("build not in table, QFE recent", "pass", {QFE: ok(rows({"HotFixID": "KB0000009", "InstalledOn": "2026-10-10T00:00:00Z"}))}, m=machine(build=99500, ubr=1)),
        vec("build not in table, QFE old", "fail", {QFE: ok(rows({"HotFixID": "KB0000009", "InstalledOn": "2026-08-01T00:00:00Z"}))}, m=machine(build=99500, ubr=1)),
        vec("only update history, old", "fail", {QFE: st("denied"), WU: ok({"serviceWasRunning": True, "entries": [
            {"dateUtc": "2026-07-01T10:00:00Z", "resultCode": 2, "operation": 1, "title": "Beispiel-Update"}]})}, m=machine(build=99500, ubr=1)),
        vec("only update history, recent: not provable", "notCheckable", {QFE: st("denied"), WU: ok({"serviceWasRunning": True, "entries": [
            {"dateUtc": "2026-10-17T10:00:00Z", "resultCode": 2, "operation": 1, "title": "Beispiel-Signaturupdate"}]})}, m=machine(build=99500, ubr=1)),
        vec("nothing", "notCheckable", {QFE: st("notCollected"), WU: st("notCollected")}, m=machine(build=99500, ubr=1)),
        vec("missing", "notCheckable", {QFE: st("keyMissing"), WU: st("keyMissing")}, m=machine(build=99500, ubr=1)),
        vec("ubr unknown (builtin)", "notCheckable", {QFE: st("notCollected")}, m=machine(build=99001, ubr=None)),
        vec("truncated", "notCheckable", {QFE: trunc(rows({"HotFixID": "KB0000009", "InstalledOn": "2026-10-10T00:00:00Z"})), WU: st("notCollected")}, m=machine(build=99500, ubr=1)),
        vec("wrong type", "notCheckable", {QFE: ok(rows({"HotFixID": "KB0000009", "InstalledOn": 20261010})), WU: st("notCollected")}, m=machine(build=99500, ubr=1)),
        vec("denied", "notCheckable", {QFE: st("denied"), WU: st("denied")}, m=machine(build=99500, ubr=1)),
        vec("n/a: domain controller", "notApplicable", {}, m=machine(build=99001, ubr=300, edition="ServerStandard", inst="Server", pt=2, dr=5)),
    ], category="lifecycle", severity="high", app=applies(exclude_dc=True)),
)

# ---------------------------------------------------------------- lsaProtection, deviceGuard
LSA = "HKLM\\SYSTEM\\CurrentControlSet\\Control\\Lsa"
DG = "wmi:root\\Microsoft\\Windows\\DeviceGuard:Win32_DeviceGuard"
EVT = "evt:system:wininit-12"
DEEP = "api:lsass-protection"
def event(found=True, created="2026-10-12T06:30:41Z", boot="2026-10-12T06:30:00Z"):
    return ok({"found": found, "timeCreatedUtc": created if found else None, "eventData": ["4"] if found else [], "bootTimeUtc": boot})
def dg(vbs=2, configured=(2,), running=(2,)):
    return ok(rows({"VirtualizationBasedSecurityStatus": vbs, "SecurityServicesConfigured": list(configured), "SecurityServicesRunning": list(running)}))

add("TISO",
    rule("TISO-01", ("LSA protection (CRED-06 style)", "LSA-Schutz (wie CRED-06)"), {"type": "lsaProtection"}, [
        vec("runtime PPL (--deep)", "pass", {DEEP: ok({"protectionByte": 65, "type": "light", "signer": "Lsa"}), R(LSA): ok(reg({"RunAsPPL": dword(0)}))}),
        vec("runtime not protected", "fail", {DEEP: ok({"protectionByte": 0, "type": "none", "signer": "None"}), R(LSA): ok(reg({"RunAsPPL": dword(2)}))}),
        vec("event 12 after boot", "pass", {DEEP: st("notCollected"), EVT: event(), R(LSA): ok(reg({"RunAsPPL": dword(2)}))}),
        vec("event 12 before boot", "notCheckable", {DEEP: st("notCollected"), EVT: event(created="2026-10-01T06:30:41Z"), R(LSA): ok(reg({}))}),
        vec("configured only", "notCheckable", {DEEP: st("notCollected"), EVT: event(found=False), R(LSA): ok(reg({"RunAsPPL": dword(2)}))}, flags=["configuredOnly"]),
        vec("configured 0, no event", "notCheckable", {DEEP: st("notCollected"), EVT: event(found=False), R(LSA): ok(reg({"RunAsPPL": dword(0)}))}),
        vec("not set, no event", "notCheckable", {DEEP: st("notCollected"), EVT: event(found=False), R(LSA): ok(reg({}))}),
        vec("all not collected", "notCheckable", {DEEP: st("notCollected"), EVT: st("notCollected"), R(LSA): st("notCollected")}),
        vec("missing", "notCheckable", {DEEP: st("keyMissing"), EVT: st("keyMissing"), R(LSA): st("keyMissing")}),
        vec("denied", "notCheckable", {DEEP: st("denied"), EVT: st("denied"), R(LSA): st("denied")}),
        vec("truncated", "notCheckable", {DEEP: trunc({"type": "light"}), EVT: trunc({"found": True}), R(LSA): trunc(reg({"RunAsPPL": dword(1)}))}),
        vec("wrong type", "notCheckable", {DEEP: st("notCollected"), EVT: event(found=False), R(LSA): ok(reg({"RunAsPPL": sz("2")}))}),
        vec("n/a: before Windows 8.1", "notApplicable", {R(LSA): ok(reg({"RunAsPPL": dword(1)}))}, m=machine(build=7601, ubr=None)),
    ], category="credentials", severity="high", app=applies(min_build=9600)),
    rule("TISO-02", ("Credential Guard running", "Credential Guard läuft"), {"type": "deviceGuard", "service": "credentialGuard"}, [
        vec("running", "pass", {DG: dg(configured=(1, 2), running=(1, 2))}),
        vec("configured, not running", "fail", {DG: dg(configured=(1, 2), running=(2,))}),
        vec("not configured", "fail", {DG: dg(configured=(0,), running=(0,))}),
        vec("namespace missing", "notCheckable", {DG: st("namespaceMissing")}),
        vec("not collected", "notCheckable", {DG: st("notCollected")}),
        vec("missing", "notCheckable", {DG: st("keyMissing")}),
        vec("denied", "notCheckable", {DG: st("denied")}),
        vec("truncated", "notCheckable", {DG: trunc(rows({"SecurityServicesRunning": [1]}))}),
        vec("wrong type", "notCheckable", {DG: ok(rows({"VirtualizationBasedSecurityStatus": 2, "SecurityServicesRunning": "1"}))}),
        vec("n/a: capability absent", "notApplicable", {DG: st("namespaceMissing")}, m=machine(build=9600, ubr=None, edition="ServerStandard", inst="Server", pt=3, dr=2)),
    ], category="credentials", app=applies(capability="testFeature", absent="notApplicable")),
    rule("TISO-03", ("VBS (requireRunning false)", "VBS (requireRunning false)"), {"type": "deviceGuard", "service": "vbs", "requireRunning": False}, [
        vec("running", "pass", {DG: dg(vbs=2)}),
        vec("enabled, not running", "notCheckable", {DG: dg(vbs=1)}, flags=["configuredOnly"]),
        vec("off", "fail", {DG: dg(vbs=0)}),
        vec("not collected", "notCheckable", {DG: st("notCollected")}),
        vec("missing", "notCheckable", {DG: st("keyMissing")}),
    ], category="credentials"),
    rule("TISO-04", ("memory integrity running (protection rule)", "Speicherintegrität läuft (Schutzregel)"), {"type": "deviceGuard", "service": "hvci"}, [
        vec("running", "pass", {DG: dg(running=(2, 7))}),
        vec("not running", "fail", {DG: dg(running=(0,))}),
        vec("capability absent: fail", "fail", {DG: st("namespaceMissing")}, m=machine(build=9600, ubr=None)),
        vec("not collected", "notCheckable", {DG: st("notCollected")}),
        vec("missing", "notCheckable", {DG: st("keyMissing")}),
    ], category="credentials", app=applies(capability="testFeature", absent="fail")),
)

# ---------------------------------------------------------------- localAccount, localAccounts, localGroup
UA = "wmi:root\\cimv2:Win32_UserAccount"
ADM = "api:localgroup:S-1-5-32-544"
def ua(name, rid, disabled): return {"Name": name, "SID": f"{SID}-{rid}", "Disabled": disabled, "Lockout": False, "PasswordRequired": True, "PasswordExpires": False}
add("TACC",
    rule("TACC-01", ("Guest disabled", "Gast deaktiviert"), {"type": "localAccount", "rid": 501, "expect": "disabled"}, [
        vec("disabled", "pass", {UA: ok(rows(ua("Guest", 501, True)))}),
        vec("enabled", "fail", {UA: ok(rows(ua("Guest", 501, False)))}),
        vec("German name, by RID", "pass", {UA: ok(rows(ua("Gast", 501, True)))}),
        vec("WMI not collected, API", "pass", {UA: st("notCollected"), USERS: ok(STD_USERS)}),
        vec("not collected", "notCheckable", {UA: st("notCollected"), USERS: st("notCollected")}),
        vec("missing", "notCheckable", {UA: st("keyMissing"), USERS: st("keyMissing")}),
        vec("denied", "notCheckable", {UA: st("denied"), USERS: st("denied")}),
        vec("truncated", "notCheckable", {UA: trunc(rows(ua("Guest", 501, True))), USERS: trunc([])}),
        vec("wrong type", "notCheckable", {UA: ok(rows({"Name": "Guest", "SID": f"{SID}-501", "Disabled": "ja"})), USERS: st("notCollected")}),
        vec("account absent (domain controller)", "notCheckable", {UA: ok(rows()), USERS: ok([])}),
    ], category="accounts"),
    rule("TACC-05", ("built-in Administrator disabled (clients)", "integrierter Administrator deaktiviert (Clients)"), {"type": "localAccount", "rid": 500, "expect": "disabled"}, [
        vec("disabled", "pass", {UA: ok(rows(ua("Administrator", 500, True)))}),
        vec("enabled", "fail", {UA: ok(rows(ua("Administrator", 500, False)))}),
        vec("not collected", "notCheckable", {UA: st("notCollected"), USERS: st("notCollected")}),
        vec("missing", "notCheckable", {UA: st("keyMissing"), USERS: st("keyMissing")}),
        vec("n/a: server", "notApplicable", {UA: ok(rows(ua("Administrator", 500, True)))}, m=SERVER),
    ], category="accounts", app=applies(os_=("client",))),
    rule("TACC-02", ("no password that never expires", "kein Kennwort, das nie abläuft"),
         {"type": "localAccounts", "condition": "passwordNeverExpires", "excludeRids": [500, 501, 503, 504], "enabledOnly": True}, [
        vec("only built-in accounts never expire", "pass", {USERS: ok(STD_USERS)}),
        vec("operator never expires", "fail", {USERS: ok(users(account("operator", 1001, never=True)))}),
        vec("disabled account ignored", "pass", {USERS: ok(users(account("alt", 1002, disabled=True, never=True)))}),
        vec("not collected", "notCheckable", {USERS: st("notCollected")}),
        vec("missing", "notCheckable", {USERS: st("keyMissing")}),
        vec("denied", "notCheckable", {USERS: st("denied")}),
        vec("truncated", "notCheckable", {USERS: trunc(STD_USERS)}),
        vec("wrong type", "notCheckable", {USERS: ok({"name": "operator"})}),
        vec("n/a: client only", "notApplicable", {USERS: ok(STD_USERS)}, m=SERVER),
    ], category="accounts", app=applies(os_=("client",))),
    rule("TACC-03", ("no unused enabled accounts", "keine unbenutzten aktiven Konten"), {"type": "localAccounts", "condition": "unusedDays", "excludeRids": [500, 501, 503, 504]}, [
        vec("recently used", "pass", {USERS: ok(STD_USERS)}),
        vec("unused since June", "fail", {USERS: ok(users(account("service", 1003, last="2026-06-01")))}),
        vec("never logged on, old", "fail", {USERS: ok(users(account("service", 1003, last=None, age=200)))}),
        vec("never logged on, new", "pass", {USERS: ok(users(account("service", 1003, last=None, age=3)))}),
        vec("not collected", "notCheckable", {USERS: st("notCollected")}),
        vec("missing", "notCheckable", {USERS: st("keyMissing")}),
    ], category="accounts"),
    rule("TACC-04", ("administrators group limited", "Administratorgruppe begrenzt"), {"type": "localGroup", "sid": "S-1-5-32-544", "maxMembers": 2, "allowedSids": ["*-500", "*-1001"]}, [
        vec("allowed members", "pass", {ADM: ok([{"sid": f"{SID}-500", "name": "Administrator", "kind": "user", "domain": "PC"}]), USERS: ok(STD_USERS)}),
        vec("foreign member", "fail", {ADM: ok([{"sid": f"{SID}-1002", "name": "zweiter", "kind": "user", "domain": "PC"}]), USERS: ok(STD_USERS)}),
        vec("too many", "fail", {ADM: ok([{"sid": f"{SID}-500"}, {"sid": f"{SID}-1001"}, {"sid": f"{SID}-500"}]), USERS: ok(STD_USERS)}),
        vec("domain account with the same RID", "fail", {ADM: ok([{"sid": "S-1-5-21-4000-5000-6000-500", "name": "Administrator", "kind": "user", "domain": "CORP"}]), USERS: ok(STD_USERS)}),
        vec("deleted account without SID", "notCheckable", {ADM: ok([{"sid": None, "name": "?", "kind": "deletedAccount"}]), USERS: ok(STD_USERS)}),
        vec("not collected", "notCheckable", {ADM: st("notCollected")}),
        vec("missing", "notCheckable", {ADM: st("keyMissing")}),
        vec("denied", "notCheckable", {ADM: st("denied")}),
        vec("truncated", "notCheckable", {ADM: trunc([{"sid": f"{SID}-500"}]), USERS: ok(STD_USERS)}),
        vec("wrong type", "notCheckable", {ADM: ok({"sid": f"{SID}-500"}), USERS: ok(STD_USERS)}),
        vec("n/a: domain controller", "notApplicable", {ADM: ok([])}, m=DC),
    ], category="accounts", app=applies(exclude_dc=True)),
)

# ---------------------------------------------------------------- inventory (Bestand)
SHARE = "wmi:root\\cimv2:Win32_Share"
def inv(id, title, renderer, items, tests, category="system"):
    return rule(id, title, {"type": "inventory", "renderer": renderer, "items": items}, tests, category=category, kind="inventory")
add("TINV",
    inv("TINV-01", ("local administrators", "lokale Administratoren"), "localAdmins", [ADM], [
        vec("listed", "info", {ADM: ok([{"sid": f"{SID}-500", "name": "Administrator", "kind": "user", "domain": "PC"}])}),
        vec("not collected", "notCheckable", {ADM: st("notCollected")}),
        vec("denied", "notCheckable", {ADM: st("denied")}),
    ], category="accounts"),
    inv("TINV-02", ("remote-access tools", "Fernwartungswerkzeuge"), "remoteAccessTools", [SVC, R("HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall")], [
        vec("tool service found", "info", {SVC: ok(rows(service("BspFernwartung", "Running", "Auto", "Beispiel-Fernwartung Dienst")))}),
        vec("nothing found", "info", {SVC: ok(rows(service("Spooler", "Running", "Auto")))}),
        vec("everything not collected", "notCheckable", {SVC: st("notCollected"), R("HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall"): st("notCollected")}),
    ], category="appControl"),
    inv("TINV-03", ("shares", "Freigaben"), "shares", [SHARE], [
        vec("open share", "info", {SHARE: ok(rows({"Name": "Austausch", "Path": "D:\\Austausch", "Type": 0, "GetSecurityDescriptor": {"ReturnValue": 0, "Aces": [{"Sid": "S-1-1-0", "AccessMask": 1179817, "AceType": 0}]}}))}),
        vec("not collected", "notCheckable", {SHARE: st("notCollected")}),
    ]),
    inv("TINV-04", ("administrative shares", "Verwaltungsfreigaben"), "adminShares", [SHARE], [
        vec("listed", "info", {SHARE: ok(rows({"Name": "C$", "Path": "C:\\", "Type": 2147483648}))}),
        vec("denied", "notCheckable", {SHARE: st("denied")}),
    ]),
    inv("TINV-05", ("RDP state", "RDP-Zustand"), "rdpState", [R("HKLM\\SYSTEM\\CurrentControlSet\\Control\\Terminal Server")], [
        vec("on", "info", {R("HKLM\\SYSTEM\\CurrentControlSet\\Control\\Terminal Server"): ok(reg({"fDenyTSConnections": dword(0)}))}),
        vec("not collected", "notCheckable", {R("HKLM\\SYSTEM\\CurrentControlSet\\Control\\Terminal Server"): st("notCollected")}),
    ], category="rdp"),
    inv("TINV-06", ("Defender exclusions", "Defender-Ausnahmen"), "defenderExclusions", [MPP], [
        vec("listed", "info", {MPP: ok(rows({"ExclusionPath": ["D:\\Daten"], "ExclusionExtension": None, "ExclusionProcess": None, "ExclusionIpAddress": None}))}),
        vec("non-admin notice, registry denied", "notCheckable", {MPP: ok(rows({"ExclusionPath": ["N/A: Must be an administrator to view exclusions"]}))},
            coverage=[{"root": "HKLM\\SOFTWARE\\Microsoft\\Windows Defender", "recursive": True, "maxDepth": 8, "complete": True,
                       "deniedSubkeys": ["HKLM\\SOFTWARE\\Microsoft\\Windows Defender\\Exclusions"]},
                      {"root": "HKLM\\SOFTWARE\\Policies", "recursive": True, "maxDepth": 8, "complete": True, "deniedSubkeys": []}]),
        vec("not collected", "notCheckable", {MPP: st("notCollected")}, coverage=[]),
    ], category="defender"),
    inv("TINV-07", ("network facts", "Netzanbindung"), "networkFacts", ["wmi:root\\cimv2:Win32_NetworkAdapterConfiguration"], [
        vec("listed", "info", {"wmi:root\\cimv2:Win32_NetworkAdapterConfiguration": ok(rows({"SettingID": "{x}", "DefaultIPGateway": ["192.0.2.1"]}))}),
        vec("not collected", "notCheckable", {"wmi:root\\cimv2:Win32_NetworkAdapterConfiguration": st("notCollected")}),
    ], category="network"),
    inv("TINV-08", ("application control state", "Zustand der Anwendungssteuerung"), "appControlState", [DG], [
        vec("listed", "info", {DG: ok(rows({"CodeIntegrityPolicyEnforcementStatus": 2, "UsermodeCodeIntegrityPolicyEnforcementStatus": 0}))}),
        vec("namespace missing", "notCheckable", {DG: st("namespaceMissing")}),
    ], category="appControl"),
    inv("TINV-09", ("wireless adapters", "WLAN-Adapter"), "wireless", ["wmi:root\\wmi:MSNdis_PhysicalMediumType"], [
        vec("wireless", "info", {"wmi:root\\wmi:MSNdis_PhysicalMediumType": ok(rows({"InstanceName": "WLAN (Beispiel)", "NdisPhysicalMediumType": 9, "Active": True}))}),
        vec("denied", "notCheckable", {"wmi:root\\wmi:MSNdis_PhysicalMediumType": st("denied")}),
    ], category="network"),
    rule("TINV-10", ("inventory rule with a group check", "Bestandsregel mit Gruppenprüfung"), {"type": "localGroup", "sid": "S-1-5-32-555"}, [
        vec("listed", "info", {"api:localgroup:S-1-5-32-555": ok([])}),
        vec("not collected", "notCheckable", {"api:localgroup:S-1-5-32-555": st("notCollected")}),
    ], kind="inventory", category="rdp"),
)

# ---------------------------------------------------------------- Verknüpfungen und Anwendbarkeit
LANMAN = "HKLM\\SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters"
SIGNING_DEFAULT = registry(LANMAN, "RequireSecuritySignature", "dword", "eq", 1, "osDefault",
                           osDefaults=[{"minBuild": 26100, "editions": ["Professional", "Enterprise", "Education"], "value": 1, "source": SRC + "/smb-default", "seen": SEEN}])
SIGNING_RUNTIME = {"type": "wmiProperty", "item": SMB, "property": "RequireSecuritySignature", "rows": "first", "op": "eq", "expected": True, "ifNoRows": "notCheckable"}
def pa(): return registry(TEST_KEY, "A", "dword", "eq", 1)
def pb(): return registry(TEST_KEY, "B", "dword", "eq", 1)
def pc(): return {"type": "service", "name": "Gibtsnicht", "startMode": ["Disabled"], "ifAbsent": "notApplicable"}
def ab(a=None, b=None):
    values = {}
    if a is not None: values["A"] = dword(a)
    if b is not None: values["B"] = dword(b)
    return {R(TEST_KEY): ok(reg(values)), SVC: ok(rows())}

add("TCMP",
    rule("TCMP-01", ("runtime before configuration (firstOf)", "Laufzeit vor Konfiguration (firstOf)"), {"type": "firstOf", "checks": [SIGNING_RUNTIME, SIGNING_DEFAULT]}, [
        vec("runtime not required beats cited default", "fail", {SMB: ok(out_({"RequireSecuritySignature": False})), R(LANMAN): ok(reg({}))}),
        vec("runtime required", "pass", {SMB: ok(out_({"RequireSecuritySignature": True})), R(LANMAN): ok(reg({"RequireSecuritySignature": dword(0)}))}),
        vec("runtime not collected, registry 1", "pass", {SMB: st("notCollected"), R(LANMAN): ok(reg({"RequireSecuritySignature": dword(1)}))}),
        vec("runtime not collected, default", "pass", {SMB: st("notCollected"), R(LANMAN): ok(reg({}))}, flags=["fromDefault"]),
        vec("runtime not collected, registry 0", "fail", {SMB: st("notCollected"), R(LANMAN): ok(reg({"RequireSecuritySignature": dword(0)}))}),
        vec("nothing decisive", "notCheckable", {SMB: st("denied"), R(LANMAN): st("notCollected")}),
        vec("missing", "notCheckable", {SMB: st("keyMissing"), R(LANMAN): ok(reg({}))}, m=machine(build=22631)),
    ], category="network"),
    rule("TCMP-02", ("allOf", "allOf"), {"type": "allOf", "checks": [pa(), pb()]}, [
        vec("both pass", "pass", ab(1, 1)),
        vec("one fails", "fail", ab(1, 0)),
        vec("one missing (whenMissing fail)", "fail", ab(1)),
        vec("one not collected", "notCheckable", {R(TEST_KEY): st("notCollected")}),
    ]),
    rule("TCMP-03", ("allOf all n/a", "allOf alle n/a"), {"type": "allOf", "checks": [pc(), pc()]}, [
        vec("all n/a", "notApplicable", ab()),
        vec("not collected", "notCheckable", {SVC: st("notCollected")}),
        vec("missing", "notCheckable", {SVC: st("keyMissing")}),
    ]),
    rule("TCMP-04", ("anyOf", "anyOf"), {"type": "anyOf", "checks": [pa(), pb(), pc()]}, [
        vec("one passes", "pass", ab(0, 1)),
        vec("all fail (n/a ignored)", "fail", ab(0, 0)),
        vec("not collected beside n/a", "notCheckable", {R(TEST_KEY): st("notCollected"), SVC: ok(rows())}),
        vec("missing", "fail", ab()),
    ]),
    rule("TCMP-05", ("noneOf", "noneOf"), {"type": "noneOf", "checks": [pa(), pb()]}, [
        vec("none passes", "pass", ab(0, 0)),
        vec("one passes", "fail", ab(1, 0)),
        vec("not collected", "notCheckable", {R(TEST_KEY): st("notCollected")}),
        vec("missing", "pass", ab()),
    ]),
    rule("TCMP-06", ("not", "not"), {"type": "not", "check": pa()}, [
        vec("inner fails", "pass", ab(0)),
        vec("inner passes", "fail", ab(1)),
        vec("not collected stays", "notCheckable", {R(TEST_KEY): st("notCollected")}),
        vec("missing", "pass", ab()),
    ]),
    rule("TCMP-07", ("domain-joined only", "nur Domänenmitglieder"), pa(), [
        vec("member", "pass", ab(1), m=SERVER),
        vec("workgroup", "notApplicable", ab(1), m=CLIENT),
        vec("domain controller excluded", "notApplicable", ab(1), m=DC),
        vec("not collected", "notCheckable", {R(TEST_KEY): st("notCollected")}, m=SERVER),
        vec("missing", "fail", ab(), m=SERVER),
    ], app=applies(domain="joined", exclude_dc=True)),
    rule("TCMP-08", ("only where Office is installed", "nur mit Office"), pa(), [
        vec("office", "pass", {**ab(1), R("HKLM\\SOFTWARE\\Microsoft\\Office\\ClickToRun\\Configuration"): ok(reg({"Platform": sz("x64")}))}),
        vec("no office", "notApplicable", {**ab(1), R("HKLM\\SOFTWARE\\Microsoft\\Office\\ClickToRun\\Configuration"): st("keyMissing")}),
        vec("office unknown", "notCheckable", {**ab(1), R("HKLM\\SOFTWARE\\Microsoft\\Office\\ClickToRun\\Configuration"): st("denied")}),
        vec("fact override", "notApplicable", ab(1), facts={"office.installed": False}),
        vec("fact unknown by override", "notCheckable", ab(1), facts={"office.installed": None}),
        vec("not collected", "notCheckable", {R(TEST_KEY): st("notCollected"), R("HKLM\\SOFTWARE\\Microsoft\\Office\\ClickToRun\\Configuration"): ok(reg({}))}),
        vec("missing", "fail", {R(TEST_KEY): st("keyMissing"), R("HKLM\\SOFTWARE\\Microsoft\\Office\\ClickToRun\\Configuration"): ok(reg({}))}),
    ], app=applies(when=[{"fact": "office.installed", "op": "eq", "value": True}])),
    rule("TCMP-09", ("protection absent counts as gap", "fehlender Schutz ist eine Lücke"), pa(), [
        vec("capability present", "pass", ab(1)),
        vec("capability absent", "fail", ab(1), m=machine(build=19045, ubr=1)),
        vec("capability unknown (no build)", "notCheckable", ab(1), facts={"capability.testFeature": None}),
        vec("not collected", "notCheckable", {R(TEST_KEY): st("notCollected")}),
        vec("missing", "fail", ab()),
    ], app=applies(capability="testFeature", absent="fail")),
    rule("TCMP-10", ("build range", "Build-Bereich"), pa(), [
        vec("inside", "pass", ab(1)),
        vec("too old", "notApplicable", ab(1), m=machine(build=17763, ubr=1)),
        vec("too new", "notApplicable", ab(1), m=machine(build=26200, ubr=1)),
        vec("not collected", "notCheckable", {R(TEST_KEY): st("notCollected")}),
        vec("missing", "fail", ab()),
    ], app=applies(min_build=19041, max_build=26100)),
)

# ---------------------------------------------------------------- Tabellen und Daten (erfunden)
TABLES = {
    "catalogue.json": {"schema": "riegel.catalogue/1", "id": "riegel", "version": "2026.10", "releasedOn": "2026-10-18"},
    "lifecycle/windows.json": {"schema": "riegel.lifecycle/1", "products": [
        {"id": "test-client-a-pro", "name": "Testsystem A Pro", "os": "client", "build": 99001, "editions": ["Professional"], "channel": "gac",
         "endOfSupport": "2027-01-01", "source": SRC + "/lifecycle", "seen": SEEN},
        {"id": "test-client-a-ent", "name": "Testsystem A Enterprise", "os": "client", "build": 99001, "editions": ["Enterprise"], "channel": "gac",
         "endOfSupport": "2025-01-01", "esuEnd": "2027-06-30", "source": SRC + "/lifecycle", "seen": SEEN},
        {"id": "test-server-a", "name": "Testsystem A Server", "os": "server", "build": 99001, "endOfSupport": "2030-01-01", "source": SRC + "/lifecycle", "seen": SEEN},
        {"id": "test-client-old", "name": "Testsystem Alt", "os": "client", "build": 99000, "endOfSupport": "2020-01-01", "source": SRC + "/lifecycle", "seen": SEEN},
    ]},
    "lifecycle/builds.json": {"schema": "riegel.builds/1", "builds": [
        {"build": 99001, "name": "Testsystem A", "releases": [
            {"ubr": 100, "date": "2026-08-11", "kb": "KB0000100", "kind": "B"},
            {"ubr": 200, "date": "2026-09-08", "kb": "KB0000200", "kind": "B"},
            {"ubr": 300, "date": "2026-10-13", "kb": "KB0000300", "kind": "B"}], "source": SRC + "/builds", "seen": SEEN},
    ]},
    "capabilities/test.json": {"schema": "riegel.capabilities/1", "capabilities": [
        {"capability": "testFeature", "exists": [{"os": "client", "minBuild": 22000}, {"os": "server", "minBuild": 20348}], "source": SRC + "/capability", "seen": SEEN},
        {"capability": "testAsr", "exists": [{"os": "client", "minBuild": 16299}, {"os": "server", "minBuild": 17763}], "source": SRC + "/capability", "seen": SEEN},
    ]},
    "data/office-detection.json": {"schema": "riegel.data/1", "kind": "officeDetection", "officeDetection": {
        "clickToRunKeys": [{"key": "HKLM\\SOFTWARE\\Microsoft\\Office\\ClickToRun\\Configuration", "source": SRC + "/office", "seen": SEEN}],
        "installRootKeys": [], "uninstallDisplayNamePrefixes": [{"pattern": "Beispiel-Office", "source": SRC + "/office", "seen": SEEN}]}},
    "data/remote-access-tools.json": {"schema": "riegel.data/1", "kind": "remoteAccessTools", "remoteAccessTools": [
        {"product": "Beispiel-Fernwartung", "serviceNames": ["BspFernwartung"], "displayNameContains": ["Beispiel-Fernwartung"], "source": SRC + "/tools", "seen": SEEN}]},
}

def write(rel, data):
    path = os.path.join(out, *rel.split("/"))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(data, indent=2, ensure_ascii=False) + "\n")

for rel, data in TABLES.items(): write(rel, data)
total = 0
for prefix, rules in FILES.items():
    write(prefix + ".json", {"schema": "riegel.rules/1", "prefix": prefix, "rules": rules})
    total += len(rules)
print(total, "rules,", sum(len(r["tests"]) for rs in FILES.values() for r in rs), "vectors")
