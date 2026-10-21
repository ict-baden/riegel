[Riegel](../README.md) › Frameworks

# Frameworks

**Contents:** [What this page is](#what-this-page-is) · [Per rule](#per-rule) · [Per report](#per-report) ·
[Disclaimers](#disclaimers) · [Sources](#sources)

## What this page is

Riegel collects technical evidence about the hardening of Windows machines and turns it into documents that a plant
owner can sign. Many frameworks ask for exactly this kind of evidence: a documented baseline, the actual state measured
against it, justified and approved deviations, and regular review. This page says where Riegel's output can serve as
evidence and where it cannot.

**Riegel supports evidence; it is not a compliance statement.** No report, score or rule result says that a machine,
a plant or an organisation complies with a law, a standard or a certification scheme. Whether the evidence is
sufficient is decided by the organisation and its auditors.

## Per rule

Every rule in the [rule catalogue](rules.md) carries references, shown in the *References* column:

| Reference | What it is | Notes |
|---|---|---|
| GS++ | Identifiers of BSI Grundschutz++ controls (Anwenderkatalog of 2026-09-24) | Identifiers only, no titles or requirement text (the catalogue is CC BY-SA 4.0) |
| ATT&CK | MITRE ATT&CK mitigation IDs, Enterprise (M1xxx) and ICS (M0xxx), ATT&CK v20.0 | Each ID was checked on attack.mitre.org on 2026-10-18 |
| ACSC | Section of the ACSC publication *Hardening Microsoft Windows 11 workstations* | The section name, as used for the expected value |
| MSB | Document and setting name in a Microsoft Security Baseline | Corroboration only, never a value |
| DISA | STIG ID (with V-ID) from the current DISA STIG XCCDF files | Windows 11 V2R11, Windows Server 2022 V2R11, Windows Server 2025 V1R4 |

STIG severity (CAT I–III) is a reference, not Riegel's severity. Riegel uses its own scale (high, medium, low).

## Per report

The plant report's "Einordnung" block states for which frameworks Riegel's output supports evidence. Each line below
is the exact identifier used in the reports, followed by what Riegel contributes.

- **ISO/IEC 27001:2022 A.8.9** – configuration management. Riegel provides the target configuration (*Soll-Konfiguration*)
  per machine group, the measured state of each machine, the deviations with their approval, and the comparison with the
  last run and the approved state.
- **NIS2 Art. 21(2)(e),(f)** – security in the maintenance of systems and the assessment of the effectiveness of the
  measures. Riegel provides measured results, regressions and the measure plan (*Maßnahmenplan*). It does not cover the
  other measures of Art. 21 (incident handling, business continuity, supply chain and others).
- **BSIG § 30 Abs. 1 Satz 3** – the German duty to document compliance with the risk-management measures. The signed plant
  report, the registers with their history and the evidence pack are documentation for the configuration part of these
  measures only.
- **ENISA Technical Implementation Guidance 6.3** – configuration management. ENISA's examples of evidence include a
  documented baseline, approved exceptions with the alternative measures in place, configuration snapshots and documented
  review results. Riegel produces each of these artefacts: the target configuration, the exception register and approval
  sheet, the snapshots with SHA-256 and the signed report.
- **BSI Grundschutz++ KONF.2.1, KONF.2.5, UMS.5.1, UMS.5.2, KONF.1.3** – basic configuration, comparison of the actual
  configuration with the reference, exceptions, and documentation of the tools used. Riegel names the tool version,
  the catalogue version and the collector hash in every report.
- **IEC 62443-2-1 CM 1.3, COMP 1.1, COMP 3.3** – configuration settings, component hardening and security patch status in
  the asset owner's security programme. Riegel measures settings, hardening and patch state per machine; compensating
  measures for legacy systems are recorded as exceptions.
- **IEC 62443-3-3 / 62443-4-2 SR/CR 7.6 RE(1), 7.7, 7.8** – configuration settings with machine-readable reporting, least
  functionality and component inventory. The JSON snapshot and results export are machine-readable reports of current
  settings; inventory rules list installed remote-access tools, local administrators, shares and network facts.
- **EU-GMP Annex 11 (draft, 2025) 14.2, 15.10–15.18** – *Entwurf* (draft, not adopted): periodic review with configuration
  auditing, supported and patched platforms, control of removable devices and anti-virus. Riegel's comparison and drift
  report is input to the periodic review; Riegel makes no GMP decision and is not a GMP system.

IEC 62443 identifiers are taken only from public table-of-contents previews; Riegel ships no per-rule mapping to IEC 62443
requirements in this version, because the standard text is not freely available.

## Disclaimers

- Riegel supports evidence; it is not a compliance statement, a certification or an audit opinion.
- A green Härtungsgrad never means "secure" or "compliant". It means that the checked settings match Riegel's catalogue
  on the checked share of rules; the share is shown next to every score, and incomplete checks carry a banner.
- The framework references do not mean that a framework owner reviewed or endorsed Riegel. Riegel contains no text from
  CIS Benchmarks, BSI SiSyPHuS or the IT-Grundschutz-Kompendium and makes no claim about them.
- BSI Grundschutz++ identifiers are used under CC BY-SA 4.0; ATT&CK® is a registered trademark of The MITRE Corporation,
  and ATT&CK content is used under its terms of use (see [rule sources](rule-sources.md)).
- Riegel gives no legal advice. Whether an organisation falls under NIS2, the BSIG or another regulation, and what it must
  document, is for the organisation to assess.

## Sources

Seen on 2026-10-18:

- Implementing Regulation (EU) 2024/2690 – https://eur-lex.europa.eu/legal-content/EN/TXT/HTML/?uri=CELEX:32024R2690
- ENISA NIS2 Technical Implementation Guidance v1.0, section 6.3 –
  https://www.enisa.europa.eu/sites/default/files/2025-06/ENISA_Technical_implementation_guidance_on_cybersecurity_risk_management_measures_version_1.0.pdf
- BSIG § 30 – https://www.gesetze-im-internet.de/bsig_2025/__30.html
- BSI Grundschutz++ (CC BY-SA 4.0) – https://github.com/BSI-Bund/Stand-der-Technik-Bibliothek
- Draft EU-GMP Annex 11 consultation –
  https://health.ec.europa.eu/consultations/stakeholders-consultation-eudralex-volume-4-good-manufacturing-practice-guidelines-chapter-4-annex_en
- MITRE ATT&CK mitigations – https://attack.mitre.org/mitigations/enterprise/ and https://attack.mitre.org/mitigations/ics/
