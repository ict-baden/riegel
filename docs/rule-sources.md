[Riegel](../README.md) › Rule sources

# Rule sources

**Contents:** [Principles](#principles) · [Sources](#sources) · [Further Microsoft sources (facts only)](#further-microsoft-sources-facts-only) ·
[Lifecycle and build tables](#lifecycle-and-build-tables) · [STIG status](#stig-status) ·
[Deviations from the planned catalogue](#deviations-from-the-planned-catalogue) · [Not used](#not-used) ·
[Open and unverified points](#open-and-unverified-points) ·
[Re-verification after an upstream release](#re-verification-after-an-upstream-release)

## Principles

- **Own texts.** Titles, rationales, plant notes and remedies are written by ICT-Baden in English and German. No text
  is copied from the sources below; rules that adapted a CC BY 4.0 text would list it under `textSources` (none do in
  catalogue 2026.10).
- **Value provenance.** The expected value of every `check` rule and every cited OS default is backed by at least one of
  ACSC, the CC BY 4.0 Microsoft Windows Server documentation, a Microsoft Learn fact page or a DISA STIG, recorded in the
  rule's `verified` list with the URL or document and the date seen. Microsoft Security Baseline references only
  corroborate and never carry a value.
- **Local copies stay local.** The downloaded copies used for verification (ACSC page text, STIG XCCDF files, baseline
  workbooks, the Grundschutz++ catalogue) are read only and never committed.
- **Unverified means not shipped.** Where no shippable source backs an expected value, the rule is an inventory rule
  (see [Deviations](#deviations-from-the-planned-catalogue)) or the fact is marked unverified below and not used as an
  expected value.

## Sources

All sources were seen on 2026-10-18.

| Code | Source and version used | Licence evidence | Use in the catalogue | Next expected update |
|---|---|---|---|---|
| ACSC | Australian Signals Directorate, *Hardening Microsoft Windows 11 workstations*, page last reviewed 2026-01-23 (settings from Windows 11 25H2), https://www.cyber.gov.au/publication/hardening-microsoft-windows-11-workstations | CC BY 4.0 – https://www.cyber.gov.au/copyright | Expected values; section names as references. Attribution: © Commonwealth of Australia 2026 | Roughly yearly, with a Windows 11 feature update (the last change log is from January 2026) |
| MSDOCS-CCBY | Microsoft Windows Server documentation, repository `MicrosoftDocs/windowsserverdocs` (branch main, last pushed 2026-10-14), published on learn.microsoft.com/windows-server | CC BY 4.0 – https://github.com/MicrosoftDocs/windowsserverdocs/blob/main/LICENSE | SMBv1, SMB signing, insecure guest logons, LSA protection, Windows LAPS, Kerberos encryption types, well-known SIDs, local Administrator guidance, service guidance (Remote Registry, Print Spooler), TLS/Schannel registry settings, command-line process auditing | Continuous |
| MSLEARN-FACT | Microsoft Learn pages (security policy settings reference, Policy CSP pages, WMI class references, Windows release information, lifecycle pages), each cited by URL in the rule or table | Microsoft Learn terms of use – https://learn.microsoft.com/en-us/legal/termsofuse (facts and URLs only, no text) | Registry locations, value meanings and defaults; build and lifecycle dates | Continuous; release information monthly (Patch Tuesday) |
| DISA | DISA STIGs: Microsoft Windows 11 STIG V2R11, Microsoft Windows Server 2022 STIG V2R11, Microsoft Windows Server 2025 STIG V1R4 (all benchmark date 30 Sep 2026), Microsoft Defender Antivirus STIG V2R10, Microsoft Windows Defender Firewall with Advanced Security STIG V2R2 (2023-11-09, the newest release found) | Public information, may be copied, credit requested – https://www.cyber.mil/privacy-security | STIG IDs and V-IDs as references; registry paths, types and values as verification | Quarterly (next quarterly release January 2027) |
| ATTACK | MITRE ATT&CK v20.0, Enterprise and ICS mitigation lists | Royalty-free licence, notice required – https://attack.mitre.org/resources/legal-and-branding/terms-of-use/ | Mitigation IDs per rule. © 2026 The MITRE Corporation; reproduced with permission | Usually twice a year (unverified) |
| GSPP | BSI Grundschutz++ (Stand-der-Technik-Bibliothek), Anwenderkatalog last modified 2026-09-24 | CC BY-SA 4.0 – https://github.com/BSI-Bund/Stand-der-Technik-Bibliothek | Control identifiers only (no titles, no requirement text) | Rolling publication |
| ENDOFLIFE | endoflife.date API v1, products `windows` (last modified 2026-09-30) and `windows-server` (2026-09-24) | MIT – https://github.com/endoflife-date/endoflife.date | Cross-check of the lifecycle table (`crossCheck`); dates are taken from Microsoft | Daily |
| MSB | Microsoft Security Baselines from the Security Compliance Toolkit: *Windows 11 v26H2 Security Baseline v2* (release notes 2026-10-08) and *Windows Server 2025 Security Baseline 2602* | Distribution not permitted (SCT licence terms) – https://www.microsoft.com/en-us/download/details.aspx?id=55319 | Document and setting name as corroboration; a reference is only added where the setting is configured in that baseline | With each Windows feature update and server baseline update |

## Further Microsoft sources (facts only)

The rules of the categories audit, PowerShell, Remote Desktop, removable media and TLS (and some of the Defender and
firewall rules) also cite these Microsoft pages, all seen 2026-10-18. They are used for **facts only** (registry
locations, value meanings, GUIDs, defaults); no text is copied. Pages from repositories under CC BY 4.0 are named with
the repository.

- [MS-GPAC] Group Policy: Advanced Audit Policy Configuration, 2.2.1.2 Subcategory and SubcategoryGUID and Appendix A
  (page 2024-10-30): https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpac/77878370-0712-47cd-997d-b07053429f6d
  and https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpac/5e1c7528-62d1-4613-a4a2-e1b028ecc521 – the
  58 audit subcategory GUIDs.
- Advanced security auditing reference, one page per subcategory (updated 2026-04-27):
  https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/auditing/ – the
  recommendation table where ACSC names no subcategory value.
- Force audit subcategory settings and the `SCENoApplyLegacyAuditPolicy` path:
  https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/audit-force-audit-policy-subcategory-settings-to-override,
  https://learn.microsoft.com/en-us/troubleshoot/windows-server/windows-security/security-auditing-settings-not-applied-when-deploy-domain-based-policy
- windowsserverdocs (CC BY 4.0): command line process auditing
  (https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/manage/component-updates/command-line-process-auditing),
  TLS registry settings (https://learn.microsoft.com/en-us/windows-server/security/tls/tls-registry-settings), Manage TLS
  (https://learn.microsoft.com/en-us/windows-server/security/tls/manage-tls), AD FS SSL/TLS
  (https://learn.microsoft.com/en-us/windows-server/identity/ad-fs/operations/manage-ssl-protocols-in-ad-fs).
- Schannel protocol table per build: https://learn.microsoft.com/en-us/windows/win32/secauthn/protocols-in-tls-ssl--schannel-ssp-
  (capabilities `schannelSsl2`, `schannelTls11`, `schannelTls12` and the TLS defaults; the table ends at Windows 11 24H2
  and Windows Server 2025).
- PowerShell-Docs (CC BY 4.0): about_Logging, about_Group_Policy_Settings, about_Execution_Policies (5.1); Installing the
  Windows PowerShell 2.0 Engine:
  https://learn.microsoft.com/en-us/previous-versions/powershell/scripting/windows-powershell/install/installing-the-windows-powershell-2.0-engine;
  the optional feature name from
  https://learn.microsoft.com/en-us/powershell/dsc/reference/psdscresources/resources/windowsoptionalfeatureset/disable?view=dsc-2.0
- KB5065506 (removal of the PowerShell 2.0 engine from Windows 11 24H2 and Windows Server 2025):
  https://support.microsoft.com/help/5065506
- SupportArticles (CC BY 4.0): local RDP values
  (https://learn.microsoft.com/en-us/troubleshoot/azure/virtual-machines/windows/cannot-connect-rdp-azure-vm,
  https://learn.microsoft.com/en-us/troubleshoot/azure/virtual-machines/windows/troubleshoot-rdp-intermittent-connectivity),
  USBSTOR (https://learn.microsoft.com/en-us/troubleshoot/windows-server/group-policy/adm-template-disable-drivers,
  https://learn.microsoft.com/en-us/troubleshoot/windows-server/group-policy/removable-devices-group-policy-troubleshooting-guidance).
- Eventlog key: https://learn.microsoft.com/en-us/windows/win32/eventlog/eventlog-key; Enabling and Disabling AutoRun:
  https://learn.microsoft.com/en-us/windows/win32/shell/autoplay-reg; ADMX list element:
  https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2008-r2-and-2008/cc725892(v=ws.10)
- Attack surface reduction rules reference (GUIDs and minimum builds, ms.date 2026-09-09):
  https://learn.microsoft.com/en-us/defender-endpoint/attack-surface-reduction-rules-reference
- BitLocker CSP and its ADMX mapping (ENC-07 `RDVDenyWriteAccess` under `SYSTEM\CurrentControlSet\Policies\Microsoft\FVE`):
  https://learn.microsoft.com/en-us/windows/client-management/mdm/bitlocker-csp
- Remote access tools in `rules/data/remote-access-tools.json`: each entry cites the vendor's page (product names are
  trademarks of their owners and are used only to identify software).

## Lifecycle and build tables

- `rules/lifecycle/windows.json`: end of mainstream support, end of support and, where Microsoft lists it, the end of
  Extended Security Updates, per product, edition family and servicing channel. Every entry cites its Microsoft Lifecycle
  page (https://learn.microsoft.com/en-us/lifecycle/products/…) and is cross-checked against endoflife.date. Lifecycle
  pages give end dates as UTC times (for example `10/14/2026 6:59:59 AM`, the end of 13 October in Redmond); the table
  stores the last day with updates (2026-10-13), as the Windows release information pages do. ESU end dates for Windows 10
  and Windows Server 2012/2012 R2 come from https://learn.microsoft.com/en-us/lifecycle/faq/extended-security-updates.
  Build numbers come from the Windows release information pages, and for older releases from
  https://learn.microsoft.com/en-us/windows/win32/msi/operating-system-property-values.
- `rules/lifecycle/builds.json`: build → UBR → release date, KB and update type for every update since October 2024 of
  Windows 10 1607/1809/21H2/22H2, Windows 11 21H2 to 26H2 and 26H1, and Windows Server 2016/2019/2022/2025, including
  hotpatch releases. Sources: https://learn.microsoft.com/en-us/windows/release-health/release-information,
  https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information and
  https://learn.microsoft.com/en-us/windows/release-health/windows-server-release-info (pages dated 2026-10-13).
  Windows 11 24H2 and Windows Server 2025 share build 26100 but use different UBR sequences since January 2026; their two
  entries carry `os: client` and `os: server`, and the engine picks the entry by the machine's OS kind.
  Windows 7, 8.1 and Server 2008 R2/2012/2012 R2 publish no UBR table; LIFE-02 falls back to hotfix dates there.
- `rules/capabilities/system.json`: Credential Guard, VBS, memory integrity and LSA protection availability per build, each
  with its Microsoft Learn or Windows Server documentation page.
- Next expected update: builds monthly after Patch Tuesday; lifecycle dates when Microsoft announces changes. A catalogue
  more than six months old gets a banner in the reports (spec 2.8).

## STIG status

The old download path `https://dl.dod.cyber.mil/wp-content/uploads/stigs/zip/…` was tried once per file on 2026-10-18 and
returned HTTP 200 for the current files (an earlier attempt the same day with an older file name had returned 404):

| File | SHA-256 |
|---|---|
| `U_MS_Windows_11_V2R11_STIG.zip` (Windows 11 STIG V2R11) | `5a1337e899f113a2482debd3ef240a867fdd205423cbf134e8961de249260d54` |
| `U_MS_Windows_Server_2022_V2R11_STIG.zip` (Windows Server 2022 STIG V2R11) | `808e83970c4f27590f31435856be9810e02d8f72d3c0a67c0a195cae46dbf48e` |
| `U_MS_Windows_Server_2025_V1R4_STIG.zip` (Windows Server 2025 STIG V1R4) | `0b83a3d42789577e5850d1bc60474af2e4c28b28b155f21f7b6a486ee0c1d84e` |

| `U_MS_Defender_Antivirus_V2R10_STIG.zip` (Microsoft Defender Antivirus STIG V2R10) | `041d01252d6d9e2446e509b11d894fe3454953f919353b93c94f8bd341a06283` |
| `U_MS_Windows_Defender_Firewall_V2R2_STIG.zip` (Windows Defender Firewall STIG V2R2) | `4fd9e65948717c06e00605b11e12958eedde409ac50040958e7c13e7146d25b3` |

STIG IDs in the rules are taken from the XCCDF files in these archives. Releases V2R3 to V3R1 of the Firewall STIG
returned HTTP 404 on dl.dod.cyber.mil (checked 2026-10-18). The audit, PowerShell, RDP, removable media and TLS rules
carry no STIG references: no current XCCDF was retrieved for them (spec open point 10).

## Deviations from the planned catalogue

The catalogue keeps the 153 rule IDs and the 20 prefix files of spec 2.1. Where a rules package found no shippable
source for an expected value, it turned the planned check into an inventory rule (spec 2.1 "verification decides the
kind"); the reason is in each rule's text and in the package's report:

| Rule | Planned | Shipped | Reason |
|---|---|---|---|
| NET-29 IP forwarding | check | inventory | no shippable source sets an expected value for `IPEnableRouter` |
| SVC-04 Remote Registry | check | inventory | ACSC and DISA set no requirement; Microsoft's CC BY server guidance says "Do not disable" |
| RDP-08 idle session limit | check | inventory | no shippable source recommends a value |
| TLS-06/07/08/11 RC4, DES/3DES, NULL, MD5 | check | inventory | Microsoft's CC BY TLS documentation says ciphers and hashes are controlled through the cipher suite order and that the SCHANNEL registry keys for them "isn't supported"; Riegel 0.1 cannot read the effective cipher suite list |

The planned inventory rules ACCT-15, APP-06, RDP-09, NETX-01, DEF-11, SYS-07 and SYS-08 are inventory as specified.
ACCT-16 and ACCT-17 stay checks, backed by the current DISA STIGs.

Decisions made in the integration step (the vectors and docs say the same):

- **CRED-06** without `--deep`: "no Wininit event 12 since the last boot and no configuration" is *not checkable*, not a
  fail – the System log may have wrapped since the start, so the missing event proves nothing.
- **ASR rules** with Defender active but `MSFT_MpPreference` not readable: a matching value in the Group Policy store is
  *not checkable* with the flag ConfiguredOnly ("konfiguriert, Wirkung nicht nachgewiesen"), as in the engine's
  documentation – runtime before configuration (spec 2.4.6).
- **ACCT-17**: an enabled account that has never logged on counts as unused, as the DISA check counts "Never".
- **FW-01/04/07** (Domain profile) and **FW-10** apply to domain members only, following the DISA Firewall STIG.
- **TLS-09/10** on 32-bit Windows: the `WOW6432Node` part does not apply there (no 32-bit registry view), so the rule is
  not checkable instead of a false fail.

## Not used

CIS Benchmarks, BSI SiSyPHuS and the IT-Grundschutz-Kompendium are not used: the catalogue contains none of their texts,
values or identifiers. Customers who hold the rights to such lists can import them as their own baselines.

## Open and unverified points

- **EditionID values.** The lifecycle table matches editions by the `EditionID` registry value (for example `EnterpriseS`,
  `IoTEnterpriseS`, `Enterprise`, `Professional`). Microsoft does not publish these values as a list; they are unverified.
  An unknown EditionID falls back to the Home/Pro entry, which never ends later. Server editions are not distinguished;
  `ServerStandard` and `ServerDatacenter` (used for SMB signing defaults) are named on
  https://learn.microsoft.com/en-us/windows-server/get-started/upgrade-conversion-options.
- **Extended Security Updates** for Windows 10 2016 LTSB, Windows 10 LTSC 2021 and Windows Server 2016 are listed by
  endoflife.date but were not found on a Microsoft page; the table sets no `esuEnd` for them.
- **Windows 11 26H2 Home/Pro**: the lifecycle page shows an end one day earlier than the release information page and
  endoflife.date; the table uses 2028-10-10.
- **Windows Server 2008 SP2**: only build 6002 is listed; a later build number is unverified.
- **Embedded and IoT variants of Windows 7 and 8.1** (Windows Embedded Standard 7, Embedded 8.1 Industry) are not separated
  because their EditionIDs are unverified; their support ended in 2020 and 2023.
- **Wininit event 12 and LSASS protection encoding**: `eventData` holds the protection level as text (`["4"]`) and
  `api:lsass-protection` reports `type` none/light/full and the signer number – the encoding the collector writes on the
  CI runners ([File formats › Item encodings](file-formats.md#item-encodings)).
- **Windows 11 25H2 (build 26200)** is not in Microsoft's Schannel protocol table; TLS-02 to TLS-05 are not checkable
  there unless the values are set explicitly.
- **PS-01** on Windows Server: that the optional feature has the same name `MicrosoftWindowsPowerShellV2` is unverified.
- **AUD-17** (PNP Activity) on Windows 10 and Server 2016 and later is inferred from [MS-GPAC] note 4.
- **MED-06** numbers its list values from 1 (ADMX list element with `valuePrefix=""`); Microsoft does not state the
  starting number.

## Re-verification after an upstream release

1. Download the new release (ACSC page text, STIG ZIPs, baseline workbooks, Grundschutz++ JSON, ATT&CK mitigation pages)
   into a local folder outside the repository.
2. Run `dotnet tools/RuleTool/bin/Release/net10.0/RuleTool.dll verify-sources <folder>`; it lists, per rule, every ACSC
   section, MSB setting, STIG ID, GS++ and ATT&CK identifier that no longer appears in the sources.
3. Update the rules, then run `RuleTool rules-doc` to regenerate [the rule catalogue page](rules.md) and `RuleTool coverage`
   to see evidence keys that the collection plan does not cover.
4. Record the new versions and dates on this page.
