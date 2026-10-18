// SPDX-License-Identifier: MIT
using System.Text;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Plants;
using Riegel.Core.Registers;
using Riegel.Tests.Plants;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Plants.WorkflowKit;

namespace Riegel.Tests.Registers;

/// <summary>
/// Laden und Speichern der Register (spec 2.11, 7.6): feste Reihenfolge, nach id sortiert, UTF-8 mit LF, Verlauf bei
/// jedem Speichern, Sperre plus optimistische Nebenläufigkeit, Prüfermodus; Freigabe und Vorlagen.
/// </summary>
public sealed class RegisterStoreTests
{
    [Fact]
    public void Speichern_sortiert_nach_id_mit_LF_ohne_BOM_und_schreibt_den_Verlauf()
    {
        using var plant = new TempPlant();
        var services = Services();
        var loaded = services.Registers.Load(plant.Plant);
        Assert.Equal(RegisterHashes.Empty, loaded.Hashes);
        var changed = loaded with
        {
            Exceptions = loaded.Exceptions with { Exceptions = [Exception("EX-0002", "NET-08", All), Exception("EX-0001", "PS-02", Machines("HMI-TEST"))] },
        };
        services.Registers.Save(plant.Plant, changed, loaded.Hashes);

        var file = plant.PathOf("exceptions.json");
        var bytes = File.ReadAllBytes(file);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "kein BOM");
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("EX-0001", StringComparison.Ordinal) < text.IndexOf("EX-0002", StringComparison.Ordinal));
        Assert.StartsWith("{\n  \"schema\": \"riegel.exceptions/1\",\n  \"plantId\": \"testanlage\",\n  \"exceptions\": [\n    {\n      \"id\": \"EX-0001\",\n      \"ruleId\": \"PS-02\",\n      \"scope\": {", text, StringComparison.Ordinal);
        SchemaValidator.AssertValid(text, "RegisterStoreTests.cs");

        // Verlauf: history/exceptions/<ts>_<sha8>.json mit genau diesem Inhalt; leere Register nicht angelegt
        var sha = Hashes.Sha256File(file);
        var history = Path.Combine(plant.Folder, "history", "exceptions", $"20261018T090000Z_{sha[..8]}.json");
        Assert.True(File.Exists(history), history);
        Assert.Equal(bytes, File.ReadAllBytes(history));
        Assert.False(File.Exists(plant.PathOf("tailoring.json")));
        Assert.False(Directory.Exists(Path.Combine(plant.Folder, "history", "tailoring")));

        var reloaded = services.Registers.Load(plant.Plant);
        Assert.Equal(sha, reloaded.Hashes.Exceptions);
        Assert.Equal(["EX-0001", "EX-0002"], reloaded.Exceptions.Exceptions.Select(e => e.Id));
    }

    [Fact]
    public void Jedes_Speichern_schreibt_eine_neue_Verlaufskopie()
    {
        using var plant = new TempPlant();
        var clock = new FixedClock();
        var services = Services(clock);
        var r = services.Registers.Load(plant.Plant);
        for (int i = 1; i <= 3; i++)
        {
            var next = r with { Measures = r.Measures with { Measures = [.. r.Measures.Measures, Measure($"M-000{i}", "NET-08", All, Today.AddDays(i))] } };
            services.Registers.Save(plant.Plant, next, r.Hashes);
            r = services.Registers.Load(plant.Plant);
            clock.Advance(TimeSpan.FromMinutes(1));
        }
        var copies = Directory.GetFiles(Path.Combine(plant.Folder, "history", "measures")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(3, copies.Count);
        Assert.All(copies, c => Assert.Matches(@"^\d{8}T\d{6}Z_[0-9a-f]{8}\.json$", c!));
        Assert.EndsWith(r.Hashes.Measures[..8] + ".json", copies[^1], StringComparison.Ordinal);
        // Unverändert speichern legt keine Kopie an
        services.Registers.Save(plant.Plant, r, r.Hashes);
        Assert.Equal(3, Directory.GetFiles(Path.Combine(plant.Folder, "history", "measures")).Length);
    }

    [Fact]
    public void Fremde_Aenderung_dazwischen_ergibt_RGL_0403_und_aendert_nichts()
    {
        using var plant = new TempPlant();
        var services = Services();
        var start = services.Registers.Load(plant.Plant);
        services.Registers.Save(plant.Plant, start with { Exceptions = start.Exceptions with { Exceptions = [Exception("EX-0001", "NET-08", All)] } }, start.Hashes);
        var mine = services.Registers.Load(plant.Plant);
        // jemand anders ändert die Datei
        var theirs = mine with { Exceptions = mine.Exceptions with { Exceptions = [Exception("EX-0001", "NET-08", All, accepted: "5")] } };
        services.Registers.Save(plant.Plant, theirs, mine.Hashes);
        var before = File.ReadAllBytes(plant.PathOf("exceptions.json"));

        var stale = mine with
        {
            Exceptions = mine.Exceptions with { Exceptions = [Exception("EX-0001", "NET-08", All, accepted: "9")] },
            Measures = mine.Measures with { Measures = [Measure("M-0001", "NET-08", All, Today)] },
        };
        var e = Assert.Throws<RiegelException>(() => services.Registers.Save(plant.Plant, stale, mine.Hashes));
        Assert.Equal(RglCodesWorkflow.ChangedByOthers, e.Code);
        Assert.Equal(before, File.ReadAllBytes(plant.PathOf("exceptions.json")));
        Assert.False(File.Exists(plant.PathOf("measures.json")), "keine Teiländerung");

        // ein unverändertes Register stört nicht, auch wenn es jemand anders geändert hat
        var current = services.Registers.Load(plant.Plant);
        services.Registers.Save(plant.Plant, mine with { Measures = mine.Measures with { Measures = [Measure("M-0001", "NET-08", All, Today)] } }, mine.Hashes);
        Assert.Equal(current.Hashes.Exceptions, services.Registers.Load(plant.Plant).Hashes.Exceptions);
    }

    [Fact]
    public void Zwei_Instanzen_schreiben_gleichzeitig_genau_eine_gewinnt()
    {
        using var plant = new TempPlant();
        var a = Services();
        var b = Services();
        var start = a.Registers.Load(plant.Plant);
        for (int round = 0; round < 5; round++)
        {
            var baseline = a.Registers.Load(plant.Plant);
            var outcomes = new string?[2];
            using var go = new ManualResetEventSlim();
            var threads = new[] { a, b }.Select((s, i) => new Thread(() =>
            {
                go.Wait();
                var mine = baseline with { Measures = baseline.Measures with { Measures = [.. baseline.Measures.Measures, Measure($"M-{round}{i}00", "NET-08", All, Today)] } };
                try { s.Registers.Save(plant.Plant, mine, baseline.Hashes); outcomes[i] = "ok"; }
                catch (RiegelException e) { outcomes[i] = e.Code; }
            })).ToList();
            threads.ForEach(t => t.Start());
            go.Set();
            threads.ForEach(t => t.Join());
            Assert.Equal(1, outcomes.Count(o => o == "ok"));
            Assert.Equal(1, outcomes.Count(o => o == RglCodesWorkflow.ChangedByOthers));
        }
        Assert.Equal(5, a.Registers.Load(plant.Plant).Measures.Measures.Count);
        Assert.NotEqual(start.Hashes, a.Registers.Load(plant.Plant).Hashes);
    }

    [Fact]
    public void Gehaltene_Sperre_ergibt_RGL_0404()
    {
        using var plant = new TempPlant();
        var service = new RegisterService(Services()) { LockTimeout = TimeSpan.FromMilliseconds(300) };
        var r = service.Load(plant.Plant);
        using (new FileStream(plant.PathOf(".riegel.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var e = Assert.Throws<RiegelException>(() =>
                service.Save(plant.Plant, r with { Exceptions = r.Exceptions with { Exceptions = [Exception("EX-0001", "NET-08", All)] } }, r.Hashes));
            Assert.Equal(RglCodesWorkflow.LockTimeout, e.Code);
        }
        Assert.False(File.Exists(plant.PathOf("exceptions.json")));
        service.Save(plant.Plant, r with { Exceptions = r.Exceptions with { Exceptions = [Exception("EX-0001", "NET-08", All)] } }, r.Hashes);
        Assert.True(File.Exists(plant.PathOf("exceptions.json")));
        if (OperatingSystem.IsWindows()) Assert.False(File.Exists(plant.PathOf(".riegel.lock")), "unter Windows beim Schließen gelöscht");
    }

    [Fact]
    public void Pruefermodus_lehnt_Speichern_ab()
    {
        using var plant = new TempPlant();
        var services = Services();
        var readOnly = services.Plants.Open(plant.Folder, readOnly: true);
        var r = services.Registers.Load(readOnly);
        var e = Assert.Throws<RiegelException>(() =>
            services.Registers.Save(readOnly, r with { Exceptions = r.Exceptions with { Exceptions = [Exception("EX-0001", "NET-08", All)] } }, r.Hashes));
        Assert.Equal(RglCodesWorkflow.ReadOnlyPlant, e.Code);
        Assert.False(File.Exists(plant.PathOf("exceptions.json")));
    }

    [Fact]
    public void Falsches_Schema_und_zu_viele_Eintraege_werden_abgelehnt()
    {
        using var plant = new TempPlant();
        File.WriteAllText(plant.PathOf("tailoring.json"), ToJson(new TailoringFile("riegel.tailoring/9", "testanlage", [])));
        var e = Assert.Throws<RiegelException>(() => Services().Registers.Load(plant.Plant));
        Assert.Equal(RglCodesCommon.UnsupportedSchema, e.Code);
        File.WriteAllText(plant.PathOf("tailoring.json"), "{ kaputt");
        Assert.Equal(RglCodesCommon.InvalidJson, Assert.Throws<RiegelException>(() => Services().Registers.Load(plant.Plant)).Code);
    }

    // ---------------------------------------------------------------- Freigabe

    [Fact]
    public void Freigabe_setzt_Felder_und_Inhalts_Hash()
    {
        var services = Services();
        var proposed = Exception("EX-0001", "NET-08", All) with { Approval = Approval.None with { ReviewBy = Today.AddMonths(6) } };
        var r = Set([proposed, Exception("EX-0002", "PS-02", All)]);
        var approved = services.Registers.Approve(r, RegisterKind.Exceptions, ["ex-0001", "EX-0002"],
            new Approval(" Qualitätssicherung ", "Name des Kunden", null, null, "Freigabeblatt FB-12 vom 2026-10-20", null));
        var first = approved.Exceptions.Exceptions[0].Approval;
        Assert.Equal("Qualitätssicherung", first.ApprovedByRole);
        Assert.Equal(Today, first.ApprovedOn);
        Assert.Equal(Today.AddMonths(6), first.ReviewBy);
        Assert.Equal("Freigabeblatt FB-12 vom 2026-10-20", first.ApprovalRecord);
        Assert.Equal(ContentHashes.Of(proposed), first.ApprovedContentSha256);
        // ohne vorgeschlagenen Termin: 12 Monate
        Assert.Equal(Today.AddMonths(12), approved.Exceptions.Exceptions[1].Approval.ReviewBy);
        Assert.DoesNotContain(services.Registers.Validate(approved, new TestCatalogue([Check("NET-08"), Check("PS-02")]), WorkflowKit.Plant(), Today),
            i => i.Code != RglCodesWorkflow.Overlap);

        Assert.Same(r.Exceptions.Exceptions[0].Approval, services.Registers.Approve(r, RegisterKind.Exceptions, [], first).Exceptions.Exceptions[0].Approval);
        Assert.Equal(RglCodesWorkflow.EntryNotFound,
            Assert.Throws<RiegelException>(() => services.Registers.Approve(r, RegisterKind.Exceptions, ["EX-0099"], first)).Code);
        Assert.Equal(RglCodesWorkflow.ApprovalIncomplete,
            Assert.Throws<RiegelException>(() => services.Registers.Approve(r, RegisterKind.Exceptions, ["EX-0001"], first with { ApprovalRecord = " " })).Code);
        Assert.Equal(RglCodesWorkflow.MeasuresNeedNoApproval,
            Assert.Throws<RiegelException>(() => services.Registers.Approve(r, RegisterKind.Measures, ["M-0001"], first)).Code);

        var tailoring = services.Registers.Approve(Set(tailoring: [Tailoring("TL-0001", "NET-08", All)]), RegisterKind.Tailoring, ["TL-0001"], first);
        Assert.Equal(ContentHashes.Of(Tailoring("TL-0001", "NET-08", All)), tailoring.Tailoring.Entries[0].Approval.ApprovedContentSha256);
    }

    // ---------------------------------------------------------------- Vorlagen

    [Fact]
    public void Vorlage_ergibt_beantragte_Eintraege_und_behaelt_die_Rolle()
    {
        using var temp = new TempFolder();
        var services = Services();
        var template = Path.Combine(temp.Path, "hersteller.exceptions-template.json");
        var fromBuilder = Exception("EX-0001", "NET-08", Groups("g-hmi")) with { CreatedByRole = "Hersteller", CreatedOn = new DateOnly(2025, 1, 1) };
        RiegelJson.WriteFile(template, new ExceptionFile(SchemaIds.ExceptionsTemplate, "vorlage",
            [fromBuilder, fromBuilder with { Id = "EX-0007", RuleId = "PS-02" }]));
        SchemaValidator.AssertValid(File.ReadAllText(template), "RegisterStoreTests.cs");
        // eingeschmuggelte Freigabefelder werden nie übernommen
        var smuggled = Path.Combine(temp.Path, "fremd.exceptions-template.json");
        RiegelJson.WriteFile(smuggled, new ExceptionFile(SchemaIds.ExceptionsTemplate, "vorlage", [fromBuilder.Approve()]));
        Assert.Equal(Approval.None, services.Registers.ImportTemplate(Set(), smuggled, ReadLimits.Default).Exceptions.Exceptions.Single().Approval);

        var existing = Set([Exception("EX-0001", "NET-01", All).Approve()]);
        var merged = services.Registers.ImportTemplate(existing, template, ReadLimits.Default);
        Assert.Equal(["EX-0001", "EX-0002", "EX-0007"], merged.Exceptions.Exceptions.Select(e => e.Id));
        var imported = merged.Exceptions.Exceptions.Skip(1).ToList();
        Assert.All(imported, e => Assert.Equal(Approval.None, e.Approval));
        Assert.All(imported, e => Assert.Equal("Hersteller", e.CreatedByRole));
        Assert.All(imported, e => Assert.Equal(Today, e.CreatedOn));
        Assert.NotNull(merged.Exceptions.Exceptions[0].Approval.ApprovedContentSha256);

        var tlTemplate = Path.Combine(temp.Path, "hmi.tailoring-template.json");
        RiegelJson.WriteFile(tlTemplate, new TailoringFile(SchemaIds.TailoringTemplate, "vorlage", [Tailoring("TL-0001", "DEF-03", Groups("g-hmi"))]));
        var tl = services.Registers.ImportTemplate(Set(), tlTemplate, ReadLimits.Default);
        Assert.Equal(Approval.None, tl.Tailoring.Entries.Single().Approval);

        var register = Path.Combine(temp.Path, "exceptions.json");
        RiegelJson.WriteFile(register, existing.Exceptions);
        Assert.Equal(RglCodesCommon.UnsupportedSchema,
            Assert.Throws<RiegelException>(() => services.Registers.ImportTemplate(existing, register, ReadLimits.Default)).Code);
        Assert.Equal(RglCodesCommon.LimitExceeded,
            Assert.Throws<RiegelException>(() => services.Registers.ImportTemplate(existing, template, ReadLimits.Default with { MaxRegisterEntries = 2 })).Code);
        Assert.Equal(RglCodesCommon.LimitExceeded,
            Assert.Throws<RiegelException>(() => services.Registers.ImportTemplate(existing, template, ReadLimits.Default with { MaxRegisterBytes = 10 })).Code);
    }
}
