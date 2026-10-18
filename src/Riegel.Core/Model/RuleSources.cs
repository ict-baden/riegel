// SPDX-License-Identifier: MIT
using Riegel.Core.Model.Reports;

namespace Riegel.Core.Model;

/// <summary>
/// Quellen der Regeln mit Lizenz und Namensnennung (spec 2.2; dieselben Angaben wie in THIRD-PARTY-NOTICES.txt,
/// gesehen 2026-10-18). Für den Methodenteil der Berichte, die Info-Seite und docs/rule-sources.md.
/// </summary>
public static class RuleSources
{
    /// <summary>src-Werte, die einen Sollwert belegen dürfen (spec 2.2 „Value provenance“).</summary>
    public static IReadOnlyList<string> ValueBacking { get; } = ["ACSC", "MSDOCS-CCBY", "MSLEARN-FACT", "DISA"];

    /// <summary>Alle Quellen, die Regeln nennen dürfen (Verweise), mit Lizenz und Namensnennung.</summary>
    public static IReadOnlyList<SourceAttribution> All { get; } =
    [
        new("ACSC", "Australian Signals Directorate's Australian Cyber Security Centre: Hardening Microsoft Windows 11 workstations",
            "CC BY 4.0", "© Commonwealth of Australia 2026. Adapted texts are marked as changed.", "https://www.cyber.gov.au/copyright"),
        new("MSDOCS-CCBY", "Microsoft Windows Server documentation (MicrosoftDocs/windowsserverdocs)",
            "CC BY 4.0", "© Microsoft Corporation, licensed under CC BY 4.0.", "https://github.com/MicrosoftDocs/windowsserverdocs/blob/main/LICENSE"),
        new("MSLEARN-FACT", "Microsoft Learn (facts and URLs only, no text copied)",
            "Microsoft Learn terms of use", "Facts such as GUIDs, version facts and defaults, each with its source URL.",
            "https://learn.microsoft.com/en-us/legal/termsofuse"),
        new("DISA", "Defense Information Systems Agency (DISA) Security Technical Implementation Guides",
            "Public information (credit requested)", "STIG references courtesy of DISA (https://www.cyber.mil/stigs).",
            "https://www.cyber.mil/privacy-security"),
        new("ATTACK", "MITRE ATT&CK®",
            "Royalty-free licence of The MITRE Corporation",
            "© 2026 The MITRE Corporation. This work is reproduced and distributed with the permission of The MITRE Corporation.",
            "https://attack.mitre.org/resources/legal-and-branding/terms-of-use/"),
        new("GSPP", "BSI Grundschutz++ (Stand-der-Technik-Bibliothek), identifiers only",
            "CC BY-SA 4.0", "Control identifiers from BSI Grundschutz++, licensed under CC BY-SA 4.0; no requirement text is used.",
            "https://github.com/BSI-Bund/Stand-der-Technik-Bibliothek"),
        new("ENDOFLIFE", "endoflife.date (cross-check of the lifecycle table)",
            "MIT", "Copyright 2020 endoflife.date contributors.", "https://github.com/endoflife-date/endoflife.date"),
        new("MSB", "Microsoft Security Baselines (reference to document and setting name only, no values or files)",
            "Not distributed", "Corroboration only; customers import the baselines they downloaded themselves.",
            "https://www.microsoft.com/en-us/download/details.aspx?id=55319"),
    ];
}

/// <summary>
/// Einordnung (spec 2.16): Kennungen, für die Riegel Nachweise unterstützt. Ausdrücklich keine Konformitätsaussage.
/// </summary>
public static class Frameworks
{
    public static IReadOnlyList<string> SupportedEvidence { get; } =
    [
        "ISO/IEC 27001:2022 A.8.9",
        "NIS2 Art. 21(2)(e),(f)",
        "BSIG § 30 Abs. 1 Satz 3",
        "ENISA Technical Implementation Guidance 6.3",
        "BSI Grundschutz++ KONF.2.1, KONF.2.5, UMS.5.1, UMS.5.2, KONF.1.3",
        "IEC 62443-2-1 CM 1.3, COMP 1.1, COMP 3.3",
        "IEC 62443-3-3 / 62443-4-2 SR/CR 7.6 RE(1), 7.7, 7.8",
        "EU-GMP Annex 11 (draft, 2025) 14.2, 15.10–15.18",
    ];
}
