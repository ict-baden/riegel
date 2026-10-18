// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.Model;
using Riegel.Core.Parsing;
using Riegel.Core.Resources;
using Riegel.Tests.Infrastructure;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Parsing;

/// <summary>
/// Die Parser gegen die aufgezeichneten Rohdaten der Runner (secedit-Export, auditpol-Sicherung, gpresult-XML,
/// Registry.pol, .reg-Exporte unter Fixtures/recorded/raw). Die Dateien bringt das Paket collector; bis dahin sind die
/// Tests übersprungen.
/// </summary>
public sealed class RecordedRawTests
{
    private static readonly ReadLimits Limits = ReadLimits.Default;

    [RecordedRawFact]
    public void Secedit_Exporte_lassen_sich_lesen()
    {
        var withSystemAccess = 0;
        foreach (var file in RecordedRaw.Files("*.inf"))
        {
            var bytes = File.ReadAllBytes(file);
            var text = TextFiles.DetectBom(bytes) == TextFiles.Bom.Utf16Le ? TextFiles.DecodeUtf16WithBom(bytes, file) : TextFiles.DecodeConsoleText(bytes, file);
            var sections = SeceditParser.Parse(text, file, Limits.MaxStringChars);
            // /mergedpolicy auf einem Rechner ohne Domäne enthält kaum mehr als [Profile Description] (research-technik §3.2)
            if (sections.Any(s => s.Name == SeceditParser.SystemAccess)) withSystemAccess++;
            SeceditParser.ToParsedItem(sections);
        }
        if (RecordedRaw.Files("*.inf").Count > 0) Assert.True(withSystemAccess > 0, "Kein secedit-Export mit [System Access].");
    }

    [RecordedRawFact]
    public void Auditpol_Sicherungen_lassen_sich_lesen()
    {
        foreach (var file in RecordedRaw.Files("*.csv").Where(f => Path.GetFileName(f).Contains("audit", StringComparison.OrdinalIgnoreCase)))
        {
            var rows = AuditCsvParser.Parse(TextFiles.DecodeConsoleText(File.ReadAllBytes(file), file), file, Limits.MaxItems, Limits.MaxStringChars);
            Assert.NotEmpty(rows);
            Assert.All(rows, r => Assert.InRange(r.Setting, 0, 3));
        }
    }

    [RecordedRawFact]
    public void Gpresult_XML_laesst_sich_lesen()
    {
        foreach (var file in RecordedRaw.Files("*.xml").Where(f => Path.GetFileName(f).Contains("gpresult", StringComparison.OrdinalIgnoreCase)))
        {
            var bytes = File.ReadAllBytes(file);
            var text = TextFiles.DetectBom(bytes) == TextFiles.Bom.Utf16Le ? TextFiles.DecodeUtf16WithBom(bytes, file) : TextFiles.DecodeConsoleText(bytes, file);
            var data = GpresultParser.Parse(text, file, Limits.MaxTextFileBytes);
            // Jeder Rechner hat die lokale Gruppenrichtlinie
            Assert.Contains(data.Gpos, g => g.Guid == GpresultData.LocalGpoId);
        }
    }

    [RecordedRawFact]
    public void Registry_pol_und_reg_Exporte_lassen_sich_lesen()
    {
        foreach (var file in RecordedRaw.Files("*.pol"))
            RegistryPolParser.Parse(File.ReadAllBytes(file), file, Limits.MaxItems);
        foreach (var file in RecordedRaw.Files("*.reg"))
            Assert.NotEmpty(RegFileParser.Parse(File.ReadAllBytes(file), file, Limits.MaxItems, Limits.MaxStringChars).Keys);
    }
}

/// <summary>Fehlercodes RGL-0250–0299: im Bereich, eindeutig, mit Text in allen Sprachen.</summary>
public sealed class ParsingCodesTests
{
    [Fact]
    public void Codes_liegen_im_Bereich_und_haben_Texte()
    {
        Assert.Equal(RglCodesParsing.All.Count, RglCodesParsing.All.Distinct().Count());
        foreach (var code in RglCodesParsing.All)
        {
            var n = int.Parse(code[4..], CultureInfo.InvariantCulture);
            Assert.InRange(n, RglCodesParsing.First, RglCodesParsing.Last);
            foreach (var language in Core.Localization.UiLanguage.Supported)
                Assert.False(string.IsNullOrWhiteSpace(ParsingStrings.ResourceManager.GetString("Error_" + code.Replace("-", "", StringComparison.Ordinal),
                    CultureInfo.GetCultureInfo(language))), code + " " + language);
        }
    }

    [Fact]
    public void Jeder_Text_der_Sprachdatei_gehoert_zu_einem_Code_oder_einer_Vorlage()
    {
        var english = LocalizationTests.Load(Path.Combine(RepoPaths.Src, "Riegel.Core", "Resources", "ParsingStrings.resx"));
        foreach (var key in english.Keys)
            Assert.True(key == "Baseline_Rationale" || RglCodesParsing.All.Contains("RGL-" + key["Error_RGL".Length..]), key);
    }

    [Fact]
    public void Texte_kommen_uebersetzt()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            Assert.StartsWith("x.reg: Zeile 3", RglCodesParsing.Message(RglCodesParsing.RegFileInvalid, "x.reg", 3), StringComparison.Ordinal);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            Assert.StartsWith("x.reg: line 3", RglCodesParsing.Message(RglCodesParsing.RegFileInvalid, "x.reg", 3), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
