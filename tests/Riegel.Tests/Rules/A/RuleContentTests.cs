// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.Json.Nodes;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Tests.Infrastructure;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Rules.A;

/// <summary>
/// Inhalt der 56 Regeln des Pakets rules-a über die gemeinsamen Katalogtests hinaus: verbindliche Entscheidungen der
/// Spezifikation (spec 2.1, 2.4 Punkt 7, Aufgabe rules-a), Belege, Texte, Vorlagen und Doku.
/// </summary>
public sealed class RuleContentTests
{
    private static readonly DateOnly Seen = new(2026, 10, 18);

    [Fact]
    public void Paket_hat_genau_die_56_Regeln_aus_spec_2_1()
    {
        var ids = RuleData.Rules.Select(r => r.Id).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(56, ids.Count);
        Assert.Equal(RuleData.ExpectedIds.Order(StringComparer.Ordinal), ids);
        Assert.All(RuleData.Rules, r => Assert.Equal(RuleState.Active, r.State));
        Assert.All(RuleData.Rules, r => Assert.Equal("2026.10", r.Since));
    }

    [Fact]
    public void Domaenen_Anwendbarkeit_folgt_der_Entscheidung()
    {
        // spec 2.1: URA-02/03, ACCT-14, CRED-10 und CRED-21 nur auf Domänenrechnern; CRED-18 überall, mit OT-Hinweis
        foreach (var id in new[] { "URA-02", "URA-03", "ACCT-14", "CRED-10", "CRED-21" })
            Assert.True(RuleData.Get(id).AppliesTo.Domain == DomainScope.Joined, $"{id} muss domain: joined haben");
        var cred18 = RuleData.Get("CRED-18");
        Assert.Equal(DomainScope.Any, cred18.AppliesTo.Domain);
        Assert.NotNull(cred18.OtNote);
        Assert.Contains("workgroup", cred18.OtNote!.Get("en"), StringComparison.OrdinalIgnoreCase);
        foreach (var r in RuleData.Rules.Where(r => r.Id is not ("URA-02" or "URA-03" or "ACCT-14" or "CRED-10" or "CRED-21")))
            Assert.True(r.AppliesTo.Domain == DomainScope.Any, $"{r.Id}: Domäne {r.AppliesTo.Domain} ohne Entscheidung");
    }

    [Fact]
    public void Regelfallen_sind_wie_vorgegeben_umgesetzt()
    {
        // LOGON-10: AutoAdminLogon = 1 ist immer fail, ohne Rücksicht auf DefaultPassword
        var logon10 = Assert.IsType<RegistryCheck>(RuleData.Get("LOGON-10").Check);
        Assert.Equal("AutoAdminLogon", logon10.Value);
        Assert.Equal(CheckOperator.Ne, logon10.Op);
        Assert.Equal("1", logon10.Expected!.Value.GetString());
        Assert.Contains(RuleData.Get("LOGON-10").Tests, t => t.Expect == ResultStatus.Fail &&
            t.Items.Values.All(i => i.Value?.GetRawText().Contains("\"DefaultPassword\"", StringComparison.Ordinal) != true));
        // CRED-06 über lsaProtection, CRED-07/08/09 über laufende Device-Guard-Dienste
        Assert.IsType<LsaProtectionCheck>(RuleData.Get("CRED-06").Check);
        Assert.Contains(RuleData.Get("CRED-06").Tests, t => t.ExpectFlags?.Contains("configuredOnly") == true && t.Expect == ResultStatus.NotCheckable);
        foreach (var (id, service) in new[] { ("CRED-07", DeviceGuardService.CredentialGuard), ("CRED-08", DeviceGuardService.Vbs), ("CRED-09", DeviceGuardService.Hvci) })
        {
            var dg = Assert.IsType<DeviceGuardCheck>(RuleData.Get(id).Check);
            Assert.Equal(service, dg.Service);
            Assert.True(dg.RequireRunning);
            Assert.Equal(AbsentCapability.Fail, RuleData.Get(id).AppliesTo.IfCapabilityAbsent);
        }
        // NET-01/02: Laufzeit bzw. Feature zuerst, dann Registry, nie ein Build-Standardwert
        foreach (var id in new[] { "NET-01", "NET-02" })
        {
            var first = Assert.IsType<FirstOfCheck>(RuleData.Get(id).Check);
            Assert.True(first.Checks[0] is WmiPropertyCheck or FeatureCheck, $"{id}: erste Quelle muss Laufzeit oder Feature sein");
            Assert.DoesNotContain(CatalogueInvariantTests.Flatten(first), c => c is RegistryCheck { WhenMissing: WhenMissing.OsDefault } or RegistryCheck { OsDefaults: not null });
        }
        Assert.IsType<WmiPropertyCheck>(((FirstOfCheck)RuleData.Get("NET-01").Check).Checks[0]);
        // LIFE-01 Lebenszyklus, LIFE-02 Update-Alter, LIFE-04 Defender-Signaturalter
        Assert.IsType<LifecycleCheck>(RuleData.Get("LIFE-01").Check);
        Assert.IsType<UpdateAgeCheck>(RuleData.Get("LIFE-02").Check);
        var life04 = Assert.IsType<DefenderSettingCheck>(RuleData.Get("LIFE-04").Check);
        Assert.Equal(("AntivirusSignatureLastUpdated", CheckOperator.AgeDaysLe, DefenderSource.Status), (life04.Property, life04.Op, life04.Source));
        // Bestand: NETX-01 und ACCT-15; NET-29 mangels auslieferbarem Sollwert ebenfalls Bestand
        foreach (var id in new[] { "NETX-01", "ACCT-15", "NET-29" })
        {
            Assert.Equal(RuleKind.Inventory, RuleData.Get(id).Kind);
            Assert.IsType<InventoryCheck>(RuleData.Get(id).Check);
        }
        // ACCT-16/17 sind durch DISA belegt und daher Prüfregeln
        foreach (var id in new[] { "ACCT-16", "ACCT-17" })
        {
            Assert.Equal(RuleKind.Check, RuleData.Get(id).Kind);
            Assert.Contains(RuleData.Get(id).Verified, v => v.Src == "DISA");
        }
    }

    [Fact]
    public void Laufzeit_kommt_vor_Konfiguration_und_anyOf_mischt_keine_Laufzeit()
    {
        var runtime = new[] { CheckTypes.WmiProperty, CheckTypes.Feature, CheckTypes.DeviceGuard, CheckTypes.LsaProtection, CheckTypes.DefenderSetting };
        foreach (var rule in RuleData.Rules)
            foreach (var check in CatalogueInvariantTests.Flatten(rule.Check))
            {
                if (check is AnyOfCheck any)
                    Assert.False(any.Checks.Any(c => c.Type == CheckTypes.WmiProperty) && any.Checks.Any(c => c.Type == CheckTypes.Registry),
                        $"{rule.Id}: anyOf verbindet Laufzeit und Konfiguration");
                if (check is FirstOfCheck first)
                {
                    var firstRegistry = first.Checks.ToList().FindIndex(c => c.Type is CheckTypes.Registry or CheckTypes.RegistryEffective or CheckTypes.AnyOf);
                    var lastRuntime = first.Checks.ToList().FindLastIndex(c => runtime.Contains(c.Type));
                    Assert.True(lastRuntime < 0 || firstRegistry < 0 || lastRuntime < firstRegistry, $"{rule.Id}: Laufzeitquelle steht hinter einer Konfigurationsquelle");
                }
            }
    }

    [Fact]
    public void Jede_Regel_hat_Texte_Verweise_und_Belege_mit_Datum()
    {
        var problems = new List<string>();
        foreach (var r in RuleData.Rules)
        {
            foreach (var (name, text) in new[] { ("otNote", r.OtNote), ("remedy", r.Remedy) })
                if (text is not null && (!text.Has("en") || !text.Has("de"))) problems.Add($"{r.Id}: {name} ohne en/de");
            if (r.Remedy is null) problems.Add($"{r.Id}: remedy fehlt");
            if (r.Title.Get("en") == r.Title.Get("de")) problems.Add($"{r.Id}: Titel nicht übersetzt");
            if (r.Rationale.Get("en") == r.Rationale.Get("de")) problems.Add($"{r.Id}: Begründung nicht übersetzt");
            if (r.References.Count == 0) problems.Add($"{r.Id}: keine Verweise");
            if (r.TextSources.Count > 0) problems.Add($"{r.Id}: übernommene Texte – textSources prüfen (eigene Texte erwartet)");
            if (r.Kind == RuleKind.Check && r.Remediation is null) problems.Add($"{r.Id}: remediation fehlt");
            foreach (var v in r.Verified)
            {
                if (v.Date != Seen) problems.Add($"{r.Id}: Beleg {v.Src} mit Datum {v.Date}");
                if (v.Url is null || !v.Url.StartsWith("https://", StringComparison.Ordinal)) problems.Add($"{r.Id}: Beleg {v.Src} ohne https-URL");
            }
            foreach (var reference in r.References)
            {
                if (reference.Src == "DISA" && (reference.Id is null || reference.Version is null)) problems.Add($"{r.Id}: DISA-Verweis ohne ID oder Version");
                if (reference.Src == "ACSC" && reference.Section is null) problems.Add($"{r.Id}: ACSC-Verweis ohne Abschnitt");
                if (reference.Src == "MSB" && (reference.Doc is null || reference.Setting is null)) problems.Add($"{r.Id}: MSB-Verweis ohne Dokument oder Einstellung");
            }
            foreach (var check in CatalogueInvariantTests.Flatten(r.Check))
                if (check is RegistryCheck { MissingPass: { } mp } && (!mp.Source.StartsWith("https://", StringComparison.Ordinal) || mp.Seen != Seen))
                    problems.Add($"{r.Id}: missingPass ohne https-Quelle oder Datum");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Erlaubnisliste_whenMissing_pass_passt_genau_zu_den_Regeln()
    {
        var allowed = File.ReadAllLines(Path.Combine(RepoPaths.TestsProject, "Rules", "A", "when-missing-pass.txt"))
            .Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0).Order(StringComparer.Ordinal).ToList();
        var used = RuleData.Rules.Where(r => CatalogueInvariantTests.Flatten(r.Check).Any(c => c is RegistryCheck { WhenMissing: WhenMissing.Pass } or RegistryEffectiveCheck { WhenMissing: WhenMissing.Pass }))
            .Select(r => r.Id).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(used, allowed);
    }

    [Fact]
    public void Ausserhalb_des_Plans_gelesene_Schluessel_haben_Abdeckung_im_Vektor()
    {
        // Vektoren für Schlüssel, die der Erfassungsplan noch nicht enthält (Integrationsanfrage), bringen ihre
        // eigene Abdeckung mit, damit KeyMissing auch heute NotSet bedeutet.
        foreach (var id in new[] { "NET-05", "LIFE-05" })
            Assert.Contains(RuleData.Get(id).Tests, t => t.Coverage is { Count: > 0 } && t.Items.Values.Any(i => i.Status is ItemStatus.KeyMissing or ItemStatus.ValueMissing));
    }

    [Fact]
    public void Zuschnitt_Vorlagen_sind_Vorschlaege_mit_gueltigen_Regeln()
    {
        var folder = Path.Combine(RepoPaths.Templates, "tailoring");
        var files = Directory.EnumerateFiles(folder, "*.tailoring-template.json").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["engineering-station.tailoring-template.json", "historian-server.tailoring-template.json", "hmi.tailoring-template.json", "jump-host.tailoring-template.json"], files);
        var catalogue = CatalogueInvariantTests.Catalogue202610.Values.SelectMany(v => v).ToHashSet(StringComparer.Ordinal);
        foreach (var name in files)
        {
            var text = File.ReadAllText(Path.Combine(folder, name!));
            var doc = RiegelJson.Deserialize<TailoringFile>(text);
            Assert.Equal(SchemaIds.TailoringTemplate, doc.Schema);
            Assert.NotEmpty(doc.Entries);
            foreach (var e in doc.Entries)
            {
                Assert.True(catalogue.Contains(e.RuleId), $"{name}: {e.RuleId} ist nicht im Katalog 2026.10");
                Assert.True(e.Reason.Length >= 20, $"{name}: {e.Id} Begründung zu kurz");
                Assert.Equal("Vorschlag – vor Übernahme prüfen", e.Comment);
                Assert.Null(e.Approval.ApprovedByRole);
                Assert.Null(e.Approval.ApprovedOn);
            }
            Assert.Equal(doc.Entries.Count, doc.Entries.Select(e => e.Id).Distinct().Count());
        }
    }

    [Fact]
    public void Ausnahme_Vorlage_des_Beispiel_Herstellers_ist_vollstaendig_und_ohne_Freigabe()
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Templates, "exceptions", "beispiel-anlagenbau.exceptions-template.json"));
        var doc = RiegelJson.Deserialize<ExceptionFile>(text);
        Assert.Equal(SchemaIds.ExceptionsTemplate, doc.Schema);
        Assert.NotEmpty(doc.Exceptions);
        var catalogue = CatalogueInvariantTests.Catalogue202610.Values.SelectMany(v => v).ToHashSet(StringComparer.Ordinal);
        foreach (var e in doc.Exceptions)
        {
            Assert.Equal("Hersteller", e.CreatedByRole);
            Assert.Equal("Herstellerdokument HD-4711", e.Reference);
            Assert.Equal(ReasonCategory.VendorRequirement, e.Category);
            Assert.True(e.Justification.Length >= 20, $"{e.Id}: Begründung zu kurz");
            Assert.True(catalogue.Contains(e.RuleId), $"{e.Id}: {e.RuleId} ist nicht im Katalog 2026.10");
            Assert.Null(e.Approval.ApprovedOn);
            Assert.Null(e.Approval.ApprovalRecord);
        }
        Assert.Contains("Beispiel-Anlagenbau", text, StringComparison.Ordinal);
        Assert.Equal(doc.Exceptions.Count, doc.Exceptions.Select(e => e.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("rules.md", "Rule catalogue")]
    [InlineData("rule-sources.md", "Rule sources")]
    [InlineData("frameworks.md", "Frameworks")]
    public void Doku_hat_Brotkrumen_und_Inhaltszeile(string file, string title)
    {
        var lines = File.ReadAllLines(Path.Combine(RepoPaths.Root, "docs", file));
        Assert.Equal($"[Riegel](../README.md) › {title}", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("**Contents:**", StringComparison.Ordinal));
    }

    [Fact]
    public void Einordnung_nennt_jede_Rahmenwerk_Kennung_und_den_Haftungsausschluss()
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Root, "docs", "frameworks.md"));
        foreach (var entry in Frameworks.SupportedEvidence)
            Assert.Contains(entry, text, StringComparison.Ordinal);
        Assert.Contains("not a compliance statement", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Entwurf", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Quellenseite_nennt_jede_Quelle_mit_Lizenz_und_STIG_Stand()
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Root, "docs", "rule-sources.md"));
        foreach (var source in RuleSources.All)
        {
            Assert.Contains(source.Src, text, StringComparison.Ordinal);
            if (source.Url is { } url) Assert.Contains(url, text, StringComparison.Ordinal);
        }
        foreach (var stig in new[] { "V2R11", "V1R4" }) Assert.Contains(stig, text, StringComparison.Ordinal);
        Assert.Contains("Next expected update", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Kein_Vektor_nutzt_Felder_ausserhalb_der_Kodierung()
    {
        // Registry-Einträge in Vektoren: { values: { name: { t, v | redacted, empty } }, subkeys: [] }
        var problems = new List<string>();
        foreach (var r in RuleData.Rules)
            foreach (var t in r.Tests)
                foreach (var (key, item) in t.Items)
                {
                    if (!key.StartsWith("reg:", StringComparison.Ordinal) || item.Value is not { } value) continue;
                    var node = JsonNode.Parse(value.GetRawText())!.AsObject();
                    if (node.Any(p => p.Key is not ("values" or "subkeys"))) problems.Add($"{r.Id}/{t.Name}: {key} hat fremde Felder");
                    foreach (var (name, v) in node["values"]!.AsObject())
                    {
                        var obj = v!.AsObject();
                        var type = (string?)obj["t"];
                        if (type is not ("dword" or "qword" or "sz" or "expand_sz" or "multi_sz" or "binary")) problems.Add($"{r.Id}/{t.Name}: {name} Typ {type}");
                        if (type == "dword" && obj["v"] is { } dv && dv.GetValueKind() != JsonValueKind.Number) problems.Add($"{r.Id}/{t.Name}: {name} DWORD nicht als Zahl");
                    }
                }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
