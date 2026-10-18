// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Evaluation;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;
using Riegel.Tests.RuleVectors;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Evaluation;

/// <summary>
/// Zeitbasis und ClockSuspect (spec 2.8), Lebenszyklus mit ESU, Herkunft der Werte (spec 2.10), kanonische
/// Darstellung (spec 7.3), Sprache der Details, zurückgezogene Regeln, Bestandsregeln, feindliche Exporte und die
/// Determinismus-Anforderung.
/// </summary>
public sealed class EvaluationEdgeTests
{
    private const string Key = @"HKLM\SOFTWARE\Policies\Riegel-Test\Values";

    private static Rule RegistryRule(string id = "TEDG-01", object? expected = null, RegValueType type = RegValueType.Dword, CheckOperator op = CheckOperator.Eq,
        WhenMissing whenMissing = WhenMissing.Fail, IReadOnlyList<OsDefault>? defaults = null) =>
        RuleBuilder.Check(id).Registry(Key, "V", type, op, expected ?? 1, whenMissing, defaults).Build();

    // ---------------------------------------------------------------- Zeitbasis

    [Fact]
    public void Altersangaben_messen_an_CollectedAtUtc()
    {
        // Signaturen 2026-10-10: zur Erfassung am 2026-10-12 2 Tage alt, am Auswertungstag 8 – es zählt die Erfassung
        var s = SnapshotBuilder.Windows11Workgroup().CollectedAt(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero))
            .WmiRows(@"root\Microsoft\Windows\Defender", "MSFT_MpComputerStatus", new { AMRunningMode = "Normal", AntivirusEnabled = true, RealTimeProtectionEnabled = true,
                AntivirusSignatureLastUpdated = "2026-10-10T12:00:00Z" })
            .Build();
        Assert.Equal(ResultStatus.Pass, EngineHarness.Eval(EngineHarness.TestRule("TDEF-02"), s).Status);
    }

    public static TheoryData<string> SuspectClocks => new() { "afterEvaluation", "afterImport", "beforeBoot", "beforeRelease" };

    [Theory]
    [MemberData(nameof(SuspectClocks))]
    public void Unplausible_Uhr_macht_Altersregeln_nicht_pruefbar(string reason)
    {
        var collected = FixedClock.Default.AddHours(-2);
        DateTimeOffset? imported = null;
        var b = SnapshotBuilder.Windows11Workgroup();
        switch (reason)
        {
            case "afterEvaluation": collected = FixedClock.Default.AddDays(3); break;
            case "afterImport": imported = FixedClock.Default.AddDays(-5); break;
            case "beforeBoot": b.Header(m => m with { LastBootUtc = FixedClock.Default.AddHours(-1) }); break;
            case "beforeRelease": b.Header(m => m with { Build = 99001, Ubr = 300 }); collected = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero); break;
        }
        var s = b.CollectedAt(collected)
            .WmiRows(@"root\Microsoft\Windows\Defender", "MSFT_MpComputerStatus", new { AMRunningMode = "Normal", AntivirusEnabled = true, RealTimeProtectionEnabled = true,
                AntivirusSignatureLastUpdated = collected.AddHours(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture) })
            .Api(ItemKeys.WscAntivirus, new[] { new { name = "Windows Defender", state = "on", signature = "upToDate" } })
            .Api(ItemKeys.NetUserEnum, new[] { new { name = "dienst", sid = "S-1-5-21-1000-2000-3000-1003", rid = 1003, disabled = false, passwordNotRequired = false,
                passwordNeverExpires = false, lockedOut = false, lastLogonDate = "2026-06-01", passwordAgeDays = 200 } })
            .Build();
        var context = EngineHarness.Context(imported: imported);
        var catalogue = EngineHarness.TestCatalogue;
        Assert.True(EvaluationClock.IsSuspect(s, catalogue, context));
        foreach (var id in new[] { "TDEF-02", "TDEF-06", "TTIM-02", "TACC-03" })
        {
            var r = new Evaluator(null).Evaluate(s, catalogue, context, null, [catalogue.Find(id)!]).Single();
            Assert.True(r.Status == ResultStatus.NotCheckable, $"{reason}/{id}: {r.Status}");
            Assert.True(r.Flags.HasFlag(ResultFlags.ClockSuspect), $"{reason}/{id}: kein ClockSuspect");
        }
        // Regeln ohne Alter bleiben unberührt
        Assert.Equal(ResultStatus.Pass, new Evaluator(null).Evaluate(s, catalogue, context, null, [catalogue.Find("TDEF-01")!]).Single().Status);
    }

    [Fact]
    public void Plausible_Uhr_ist_nicht_verdaechtig()
    {
        var s = SnapshotBuilder.Windows11Workgroup().CollectedAt(FixedClock.Default.AddHours(-3)).Header(m => m with { LastBootUtc = FixedClock.Default.AddDays(-2) }).Build();
        Assert.False(EvaluationClock.IsSuspect(s, EngineHarness.TestCatalogue, EngineHarness.Context(imported: FixedClock.Default.AddHours(-1))));
    }

    [Fact]
    public void Lebenszyklus_nutzt_EvaluationDate_und_ESU()
    {
        var rule = EngineHarness.TestRule("TTIM-01");
        var enterprise = SnapshotBuilder.Machine("ENT", 99001, 1, 0, "Enterprise").Build();
        Assert.Equal(ResultStatus.Fail, EngineHarness.Eval(rule, enterprise).Status);
        var esu = EngineHarness.Eval(rule, enterprise, EngineHarness.Context(esu: true));
        Assert.Equal(ResultStatus.Pass, esu.Status);
        Assert.Equal("2027-06-30", esu.Expected);
        Assert.Equal(ResultStatus.Fail, EngineHarness.Eval(rule, enterprise, EngineHarness.Context(esu: true, date: new DateOnly(2027, 7, 1))).Status);
        var pro = SnapshotBuilder.Machine("PRO", 99001, 1, 0, "Professional").Build();
        Assert.Equal(ResultStatus.Pass, EngineHarness.Eval(rule, pro, EngineHarness.Context(date: new DateOnly(2027, 1, 1))).Status);
        Assert.Equal(ResultStatus.Fail, EngineHarness.Eval(rule, pro, EngineHarness.Context(date: new DateOnly(2027, 1, 2))).Status);
        Assert.Equal(ResultStatus.Fail, EngineHarness.Eval(rule, pro, EngineHarness.Context(esu: true, date: new DateOnly(2027, 1, 2))).Status); // kein ESU-Ende bekannt
        // Kopf ohne Produkttyp (Bordmittel): nicht prüfbar
        Assert.Equal(ResultStatus.NotCheckable, EngineHarness.Eval(rule, SnapshotBuilder.Machine("X", 99001, null, null, "Professional").NoCoverage().Build()).Status);
    }

    // ---------------------------------------------------------------- Herkunft und Darstellung

    [Fact]
    public void Herkunft_aus_der_Zuordnung_und_aus_der_Art_des_Belegs()
    {
        var s = SnapshotBuilder.Windows11Workgroup().RegistryValue(Key, "V", RegValueType.Dword, 1).Build();
        var map = new AttributionMap(new Dictionary<string, ValueAttribution>
        {
            [AttributionMap.KeyOf(Key, "V")] = new(ValueSource.DomainGpo, "Linie-3-Basis", true),
        }, true);
        var gpo = EngineHarness.Eval(RegistryRule(), s, attribution: _ => map);
        Assert.Equal(ValueSource.DomainGpo, gpo.Source);
        Assert.Equal("Linie-3-Basis", gpo.SourceDetail);
        // Ohne Zuordnung (Platzhalter des Pakets parsing): Unknown
        Assert.Equal(ValueSource.Unknown, EngineHarness.Eval(RegistryRule(), s).Source);
        // Nicht gesetzt, Standardwert, Laufzeit
        var empty = SnapshotBuilder.Windows11Workgroup().RegistryKey(Key).Build();
        Assert.Equal(ValueSource.NotSet, EngineHarness.Eval(RegistryRule(), empty).Source);
        var withDefault = RegistryRule(whenMissing: WhenMissing.OsDefault, defaults: [new OsDefault(26100, null, null, null, null, JsonValues.Number(1), "https://example.org/riegel-test/default", new DateOnly(2026, 10, 18))]);
        var d = EngineHarness.Eval(withDefault, empty);
        Assert.Equal((ResultStatus.Pass, ValueSource.OsDefault), (d.Status, d.Source));
        Assert.True(d.Flags.HasFlag(ResultFlags.FromDefault));
        Assert.Null(d.Actual);
        var runtime = EngineHarness.Eval(EngineHarness.TestRule("TSYS-04"), SnapshotBuilder.Server2022Member()
            .WmiMethod(@"root\Microsoft\Windows\SMB", "MSFT_SmbServerConfiguration", "GetConfiguration", new { RequireSecuritySignature = true }).Build());
        Assert.Equal(ValueSource.Runtime, runtime.Source);
    }

    [Fact]
    public void Soll_und_Ist_in_kanonischer_Form()
    {
        Snapshot With(RegValueType t, object v) => SnapshotBuilder.Windows11Workgroup().RegistryValue(Key, "V", t, v).Build();
        var dword = EngineHarness.Eval(RegistryRule(), With(RegValueType.Dword, 0));
        Assert.Equal(("1", "0"), (dword.Expected, dword.Actual));
        var list = EngineHarness.Eval(RegistryRule(expected: new[] { "a" }, type: RegValueType.MultiSz, op: CheckOperator.ContainsAll), With(RegValueType.MultiSz, new[] { "b", "c" }));
        Assert.Equal(("[\"a\"]", "[\"b\",\"c\"]"), (list.Expected, list.Actual));
        var binary = EngineHarness.Eval(RegistryRule(expected: "hex:00", type: RegValueType.Binary), With(RegValueType.Binary, new byte[] { 1, 2, 3 }));
        Assert.Equal("hex:010203", binary.Actual);
        var redacted = EngineHarness.Eval(RegistryRule(expected: "x", type: RegValueType.Sz), SnapshotBuilder.Windows11Workgroup().RegistryRedacted(Key, "V").Build());
        Assert.Equal(("redacted", ResultStatus.NotCheckable), (redacted.Actual, redacted.Status));
        Assert.Equal([ItemKeys.Registry(Key)], dword.EvidenceKeys);
    }

    [Fact]
    public void Details_kommen_in_der_Sprache_der_Auswertung()
    {
        var s = SnapshotBuilder.Windows11Workgroup().RegistryKey(Key).Build();
        Assert.Contains("Wert nicht gesetzt", EngineHarness.Eval(RegistryRule(), s, EngineHarness.Context(language: "de")).Details!);
        Assert.Contains("Value not set", EngineHarness.Eval(RegistryRule(), s, EngineHarness.Context(language: "en")).Details!);
        Assert.Contains("Value not set", EngineHarness.Eval(RegistryRule(), s, EngineHarness.Context(language: "xx-Unbekannt")).Details!);
    }

    // ---------------------------------------------------------------- Regeln im Katalog

    [Fact]
    public void Jede_Regel_ergibt_genau_ein_Ergebnis_zurueckgezogene_sind_nicht_anwendbar()
    {
        var retired = RuleBuilder.Check("TEDG-02").Registry(Key, "V", RegValueType.Dword, CheckOperator.Eq, 1, WhenMissing.Fail).Retired().Build();
        var catalogue = EngineHarness.Catalogue(RegistryRule(), retired);
        var results = new Evaluator(null).Evaluate(SnapshotBuilder.Windows11Workgroup().Build(), catalogue, EngineHarness.Context());
        Assert.Equal(["TEDG-01", "TEDG-02"], results.Select(r => r.RuleId));
        Assert.Equal((ResultStatus.NotApplicable, RglCodesEngine.Retired), (results[1].Status, results[1].ReasonCode));
    }

    [Fact]
    public void Bestandsregeln_sind_nie_ein_Befund()
    {
        var inventory = RuleBuilder.Inventory("TEDG-03").Registry(Key, "V", RegValueType.Dword, CheckOperator.Eq, 1, WhenMissing.Fail).Build();
        var r = EngineHarness.Eval(inventory, SnapshotBuilder.Windows11Workgroup().RegistryValue(Key, "V", RegValueType.Dword, 0).Build());
        Assert.Equal(ResultStatus.Info, r.Status);
        Assert.Null(r.ReasonCode);
    }

    [Fact]
    public void Feindliche_und_kaputte_Exporte_fuehren_nie_zu_einem_Absturz()
    {
        var garbage = JsonValues.Parse("""{ "values": "kein Objekt", "rows": 5, "out": [1], "sections": [], "profiles": "x" }""");
        var odd = JsonValues.Parse("[null, {\"a\": []}, 3, \"x\"]");
        var keys = EngineHarness.TestCatalogue.Rules.SelectMany(r => CheckEvidence.KeysOf(r.Check)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var valid = new MachineInfo("X", null, 0, 1, 26100, 1, "Professional", "Client", null, null, null, null, null);
        var broken = new MachineInfo("X", null, 9, 7, -1, -1, null, null, null, null, null, null, null);
        var variants = new (MachineInfo Machine, List<SnapshotItem> Items)[]
        {
            (valid, [.. keys.Select(k => new SnapshotItem(k, ItemStatus.Ok, garbage, null, null, 0))]),
            (valid, [.. keys.Select(k => new SnapshotItem(k, ItemStatus.Ok, odd, null, null, 0))]),
            (valid, [.. keys.SelectMany(k => new[] { new SnapshotItem(k, ItemStatus.Ok, garbage, null, null, 0), new SnapshotItem(k, ItemStatus.KeyMissing, null, null, null, 0) })]),
            (broken, [.. keys.Select(k => new SnapshotItem(k, ItemStatus.Ok, garbage, null, null, 0))]),
        };
        foreach (var (machine, items) in variants)
        {
            var hostile = new Snapshot(SchemaIds.Snapshot, new CollectorInfo("x", "0", null, "", false), FixedClock.Default, true, SnapshotOrigin.Synthetic,
                new CollectionOptions(false, false, false), machine,
                [new CoverageEntry("HKLM\\SOFTWARE\\Policies", true, 8, true, [null!, ""], null)], items);
            var results = new Evaluator(null).Evaluate(hostile, EngineHarness.TestCatalogue, EngineHarness.Context());
            Assert.Equal(EngineHarness.TestCatalogue.Rules.Count, results.Count);
            Assert.DoesNotContain(results, r => r.ReasonCode == RglCodesEngine.EvaluationFailed);
            // Bestehen dürfen nur Regeln, die ohne Eintrag entscheiden (Build-Tabelle) – kaputte Rohdaten tragen kein „erfüllt“
            var passes = results.Where(r => r.Status == ResultStatus.Pass).Select(r => r.RuleId).ToList();
            Assert.True(passes.All(id => id.StartsWith("TTIM", StringComparison.Ordinal)), string.Join(", ", passes));
        }
    }

    [Fact]
    public void Der_Dienst_aus_der_Factory_ist_die_Engine()
    {
        var services = RiegelServices.Create(new NullPlatform(), new FixedClock());
        var s = SnapshotBuilder.Windows11Workgroup().RegistryValue(Key, "V", RegValueType.Dword, 1).Build();
        var result = services.Evaluator.Evaluate(s, EngineHarness.Catalogue(RegistryRule()), EngineHarness.Context()).Single();
        Assert.Equal(ResultStatus.Pass, result.Status);
        Assert.Contains("os.kind", services.Facts.Derive(s, EngineHarness.TestCatalogue).Keys);
    }

    // ---------------------------------------------------------------- Determinismus

    [Fact]
    public void Gleiche_Eingaben_geben_dieselbe_Ergebnisliste()
    {
        var catalogue = EngineHarness.TestCatalogue;
        var shuffled = EngineHarness.Catalogue([.. catalogue.Rules.Reverse()]);
        foreach (var file in Directory.EnumerateFiles(RepoPaths.SyntheticFixtures, "*.snapshot.json").Order(StringComparer.Ordinal))
        {
            var snapshot = RiegelJson.ReadFile<Snapshot>(file, 64L * 1024 * 1024);
            var a = RiegelJson.Serialize(RiegelServices.Create(new NullPlatform(), new FixedClock()).Evaluator.Evaluate(snapshot, catalogue, VectorRunner.Context).ToList());
            var b = RiegelJson.Serialize(RiegelServices.Create(new NullPlatform(), new FixedClock()).Evaluator.Evaluate(snapshot, catalogue, VectorRunner.Context).ToList());
            var c = RiegelJson.Serialize(new Evaluator(null).Evaluate(snapshot, shuffled, VectorRunner.Context).ToList());
            var reordered = snapshot with { Items = [.. snapshot.Items.Reverse()], Coverage = [.. snapshot.Coverage.Reverse()] };
            var d = RiegelJson.Serialize(new Evaluator(null).Evaluate(reordered, catalogue, VectorRunner.Context).ToList());
            Assert.Equal(a, b);
            Assert.Equal(a, c);
            Assert.Equal(a, d);
        }
    }

    [Fact]
    public void Ergebnisse_sind_nach_Regelsatz_und_ID_sortiert_und_schemagueltig()
    {
        var snapshot = RiegelJson.ReadFile<Snapshot>(RepoPaths.Synthetic("win11-24h2-workgroup-admin"), 64L * 1024 * 1024);
        var results = new Evaluator(null).Evaluate(snapshot, EngineHarness.TestCatalogue, VectorRunner.Context);
        Assert.Equal(results.Select(r => r.RuleId).Order(StringComparer.Ordinal), results.Select(r => r.RuleId));
        var json = JsonSerializer.Serialize(results.ToList(), RiegelJson.TypeInfo<List<RuleResult>>());
        Assert.DoesNotContain("\"status\":\"exception\"", json, StringComparison.Ordinal);
    }
}
