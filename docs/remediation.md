[Riegel](../README.md) › Remediation

# Remediation proposal (files only)

**Contents:** [What Riegel writes](#what-riegel-writes) · [Which rules are in it](#which-rules-are-in-it) ·
[riegel-remediation.reg](#riegel-remediationreg) · [riegel-rollback.reg](#riegel-rollbackreg) ·
[GptTmpl.inf](#gpttmplinf) · [audit.csv](#auditcsv) · [README.txt](#readmetxt) ·
[Generated files are attack surface](#generated-files-are-attack-surface) · [Error codes](#error-codes) ·
[Sources](#sources)

## What Riegel writes

`Riegel.Cli.exe remediate --plant <dir> --machine <host> [--run <id>] --out <dir> [--language <code>]` (output O10)
writes a proposal for the open findings of one run into `remediation/<host>/<runId>/`:

| File | Content |
|---|---|
| `riegel-remediation.reg` | registry values to set or delete |
| `riegel-rollback.reg` | the values recorded in the export before the change |
| `GptTmpl.inf` | a security template fragment (account policies, security options as registry values, user rights) |
| `audit.csv` | advanced audit policy in the format of a GPO's `audit.csv` |
| `README.txt` | per setting: rule, old → new value, OT note, planned measure; rules left out; manual steps |

**Riegel never applies anything.** It writes no script (no `.ps1` in v1), starts no program, and has no code that
writes to the registry, the security policy or the audit policy. Every file starts with the notice "Proposal – not
executed by Riegel. Agree with the plant vendor before applying." in the report language (as a comment
where the format has comments; see the notes per file). The plant owner agrees the proposal with the plant vendor and
applies it through the customer's own change process.

## Which rules are in it

Only results with status `fail` whose rule has a `remediation` block in the catalogue. Left out, and listed in the
README with the reason and the register entry:

| Reason | When |
|---|---|
| valid exception | status `exception`, or a failing result that names a valid exception (also when the value differs from the accepted value) |
| requested exception | a failing result with `exceptionPending` or `exceptionChangedAfterApproval` |
| tailoring | status `outOfScope`, or a failing result with `tailoringPending` |
| no remediation | the rule has no file-based remediation (or only entries that were not allowed) |

The results are authoritative (the register service has already applied the registers). In addition the writer checks
the register entries that name **this machine or the whole plant**: a valid or requested exception or a tailoring
entry for the rule there also leaves the rule out. Group-scoped entries cannot be resolved without the plant and are
taken from the results. An **expired** exception does not leave a rule out; the README says "Exception … has
expired – renew it or apply the proposal".

## riegel-remediation.reg

Registry Editor format version 5.00: UTF-16 LE with BOM, CRLF, first line `Windows Registry Editor Version 5.00`,
the notice and two information lines as `;` comments, then one `[HKEY_LOCAL_MACHINE\…]` section per key and a blank
line at the end.

| Catalogue type | Written as |
|---|---|
| `dword` | `"Name"=dword:0000000a` (8 hex digits) |
| `qword` | `"Name"=hex(b):` 8 bytes little-endian |
| `sz` | `"Name"="text"` with `\` → `\\` and `"` → `\"` |
| `expand_sz` | `"Name"=hex(2):` UTF-16 LE bytes with the terminating NUL (no variable is expanded) |
| `multi_sz` | `"Name"=hex(7):` UTF-16 LE, each string NUL-terminated, plus a final NUL |
| `binary` | `"Name"=hex:` bytes (base64 in the catalogue) |
| delete | `"Name"=-` |
| default value | `@=` instead of a name |

Long hex data is wrapped like regedit does it: `,\` at the end of the line, two spaces at the start of the next.

## riegel-rollback.reg

Same format. For every value in the remediation file, the old value from the export:

- **present and type-checked** → written back with its old type and data;
- **provably not set** (the key item is `keyMissing`, or the key was read completely and the value is absent) → deleted
  again (`"Name"=-`);
- **unknown or not safe** → left out, with a README line "Rollback: old value of … not taken over (reason,
  RGL-0570) – note it before the change". Reasons: not collected (missing, denied, under a denied subkey, an
  incomplete root, a value filter), truncated or redacted at collection, a type that does not match the value
  (a DWORD that is not a number in 0…4,294,967,295, a QWORD that is not an unsigned decimal), an unsupported type
  (`none`, `unknown`), a string with CR, LF, NUL or any other control character (also U+2028/U+2029), a `multi_sz`
  with an empty entry (it would end the list), a string longer than 16,384 characters (a Riegel limit: such values are
  for manual review).

## GptTmpl.inf

Security template syntax of [MS-GPSB] 2.2: UTF-16 LE with BOM, CRLF, `[Unicode]` `Unicode=yes`, `[Version]`
`signature="$CHICAGO$"` `Revision=1`, then the sections in the order of [MS-GPSB] 2.2 (System Access, Kerberos Policy,
the three log sections, Event Audit, Registry Values, Privilege Rights). The notice is a `;` comment line before
`[Unicode]`; INF files treat text after `;` as a comment.

- `{ section, key, value }` → `Key = Value` (value digits or a quoted string of printable ASCII);
- `[Registry Values]` → `MACHINE\…\Name=<type>,<value>` with type 1, 2, 3, 4 or 7 ([MS-GPSB] 2.2.5);
- `{ section: "Privilege Rights", key: <right>, sids: [...] }` → `SeRight = *S-1-5-32-544,*S-1-…` ([MS-GPSB] 2.2.6).
  Catalogue SIDs of the form `*-<RID>` (local accounts) are resolved with the machine SID taken from the local accounts
  in the export; if that is not possible, or a right is to be assigned to nobody (empty list) or a name instead of a SID
  is given, the right is a manual step in the README.

Sections with ACLs (File Security, Registry Keys, Service General Setting, Group Membership) are not written.

## audit.csv

The advanced audit policy file of a GPO as [MS-GPAC] 2.2 defines it: UTF-8, CRLF, the header line
`Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value` and one
line per subcategory:

```text
,System,Credential Validation,{0CCE923F-69AE-11D9-BED3-505054503030},Success and Failure,,3
```

Setting values per [MS-GPAC] 2.2.1.3.1: 1 success, 2 failure, 3 success and failure, **4 none** ("No Auditing").
The machine name is left empty (it is ignored when the policy is applied, [MS-GPAC] 2.2.4); subcategory names that
are not plain `StringWithSpaces` are quoted, names that cannot be written safely are left out.

The column order matches an `auditpol /backup` file recorded read-only during the research for this product
(2026-10-18, not committed). Note: that local backup wrote `No Auditing,,0` for subcategories without auditing; in a
GPO file [MS-GPAC] defines 0 as "unchanged" and 4 as "none", so Riegel writes 4 when the catalogue asks for `none`.

**First line:** the mandatory notice, as `# …`. [MS-GPAC] has no comment syntax, so the file is not importable as it
stands: remove the first line before you copy it into a GPO (for example into the `audit.csv` of a GPO backup). The
README says so. The deliberate step also keeps the proposal from being imported unread. Other import paths (such as
`auditpol /restore`) were not checked against this format.

## README.txt

UTF-8 without BOM, CRLF, in the report language. For every rule in the proposal: ID, title and severity; each setting
with old → new value; the OT side-effect note of the rule (`otNote`); the planned measure for the rule (ID, action,
owner role, due date, state) where one exists; notes on rollback omissions, not-allowed catalogue entries and expired
exceptions. Then the rules left out with reason and the justification of the register entry, the manual steps from the
catalogue (`remediation.manual`) and how to use the files. Text from the export or the registers is shortened and
control characters are replaced, so the README stays readable.

## Generated files are attack surface

Exports come from the machines being checked and may be hostile. The writer therefore:

- takes key paths and value names **only from the catalogue**, never from the export; a catalogue key must be under
  `HKLM\`, every part printable ASCII without `[`, `]`, `"` or `\`, at most 255 characters, without leading or trailing
  blanks; a value name has no control characters; anything else is left out with `RGL-0569`;
- checks the catalogue's new value against its type (numbers for DWORD/QWORD, strings without control characters,
  lists without empty entries, base64 for binary);
- type-checks every old value from the export before it goes into the rollback (see above);
- escapes `\` and `"` in every quoted string and encodes `expand_sz` and `multi_sz` as UTF-16 LE hex, so no data byte
  can end a line or open a section;
- writes only the five files into its own folder.

The tests use a hostile-snapshot fixture (`tests/Riegel.Tests/Remediation/Fixtures/hostile-registry.snapshot.json`,
generated by `generate-hostile.py` next to it) with quotes, backslashes, CR/LF, NUL, `$()`, 100,000-character strings,
wrong types, redacted, truncated and denied values and value names that try to open new sections. A strict `.reg`
grammar validator in the tests checks both files and decodes every value.

## Error codes

`RGL-0569` catalogue remediation entry not allowed (left out, README note) · `RGL-0570` old value not taken into the
rollback (README note). The other data-output codes are listed in [Evidence pack](evidence.md#error-codes).

## Sources

All seen on 2026-10-18.

- `.reg` syntax: Microsoft KB 310516 "How to add, modify, or delete registry subkeys and values by using a .reg file"
  (syntax, `dword`, `hex`, `hex(2)`, `hex(7)`, deleting values with `=-`). The current support page redirects to a
  landing page; the text was read from the archived copy
  `https://web.archive.org/web/20221223060938/https://support.microsoft.com/en-us/topic/how-to-add-modify-or-delete-registry-subkeys-and-values-by-using-a-reg-file-9c7f37cf-a5e9-e1cd-c4fa-2a26218a1a23`.
- `.reg` details not in the KB (UTF-16 LE with BOM, CRLF, `\\` and `\"` escaping, `@=` for the default value,
  `hex(b)` for QWORD, the `hex(2)`/`hex(7)` NUL terminators, `,\` continuation with two spaces, blank last line):
  checked against `reg export` files written read-only on a Windows 11 machine (build 26200) into a scratch folder,
  not committed.
- `;` comment lines in `.reg` files: Microsoft Learn, "Registry File" (Windows CE / Windows Embedded documentation,
  archived): `https://learn.microsoft.com/en-us/previous-versions/windows/embedded/ms930398(v=msdn.10)`. Riegel puts
  comments only on their own lines after the version line. Riegel never imports `.reg` files, so whether a particular
  Windows version accepts the comment lines was not tested here.
- [MS-GPSB] Group Policy: Security Protocol Extension, 2.2 Message Syntax
  (`https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpsb/fa15485d-ae9f-456e-a08f-81f2e5725a7e`),
  2.2.1 System Access (`…/d9bcb85c-67be-49cc-90ea-d2bd50873417`), 2.2.5 Registry Values
  (`…/3a14ca47-a22f-43c5-b35e-6be791003ca7`), 2.2.6 Privilege Rights (`…/3413b381-a445-4d17-b77e-5bbfadda253b`).
- INF comments: "General Syntax Rules for INF Files",
  `https://learn.microsoft.com/en-us/windows-hardware/drivers/install/general-syntax-rules-for-inf-files`.
- [MS-GPAC] Group Policy: Audit Configuration Extension, 2.2 Message Syntax
  (`https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpac/6494a0f2-8a16-40e2-b87d-328be7d732e0`),
  2.2.1 Subcategory Settings (`…/6932bdb2-e008-40aa-a774-edf6e1a68acd`), 2.2.1.1 Policy Target
  (`…/babbf4f2-5790-41d5-b633-09d489c7ba59`), 2.2.1.2 Subcategory and SubcategoryGUID
  (`…/77878370-0712-47cd-997d-b07053429f6d`), 2.2.1.3.1 settings for system audit subcategories
  (`…/21ad2e80-3011-48ef-be13-cc11ff7bfeb1`), 2.2.4 Machine Name (`…/c76f6418-d4b7-4a8a-bc07-001af61d2ea8`).
