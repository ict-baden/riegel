// SPDX-License-Identifier: MIT
using System.Text;
using Riegel.Core.Baselines;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Parsing;
using Riegel.Core.Services;
using Riegel.Tests.Parsing;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Parsing.ParsingSamples;

namespace Riegel.Tests.Baselines;

/// <summary>Kunden-Baselines (spec 2.15): JSON, CSV-Vorlage und GPO-Sicherung; stabile IDs, Kollisionen, Grenzen.</summary>
public sealed class BaselineImporterTests
{
    private static IBaselineImporter Importer() => RiegelServices.Create(new NullPlatform(), new FixedClock()).Baselines;

    private static void AssertRulesValid(ImportedBaseline imported) =>
        LocalSchema.AssertValid(RiegelJson.Serialize(new RuleFile(SchemaIds.Rules, "IMP", imported.Rules)), "importierte Regeln");

    private static BaselineRule Reg(string? id, string key, string value, uint expected, Severity? severity = null) =>
        new(id, "Vorgabe " + value, severity, null,
            new RegistryCheck(key, value, RegValueType.Dword, null, CheckOperator.Eq, JsonValues.Number(expected), WhenMissing.Fail, null, null, null),
            null, null);

    private static string WriteJson(TempFolder temp, string name, BaselineFile file)
    {
        var path = Path.Combine(temp.Path, name);
        RiegelJson.WriteFile(path, file);
        return path;
    }

    // ---------------------------------------------------------------- riegel.baseline/1

    [Fact]
    public void JSON_Baseline_mit_Autor_IDs_und_Hash_IDs()
    {
        using var temp = new TempFolder();
        var path = WriteJson(temp, "kunde.json", new BaselineFile(SchemaIds.Baseline, "anlagenbau-x", "Kundenvorgaben", null, null, null, null,
        [
            Reg("R1", @"HKLM\SOFTWARE\Policies\Beispiel", "A", 1, Severity.High),
            Reg(null, @"HKLM\SOFTWARE\Policies\Beispiel", "B", 0),
            new BaselineRule(null, "Druckdienst aus", null, "services", new ServiceCheck("Spooler", ["Disabled"], null, AbsentOutcome.Pass), "Kein Drucken in der Zelle.", "Pflichtenheft 4.2"),
        ]));
        LocalSchema.AssertValid(File.ReadAllText(path), "Baseline");
        var imported = Importer().Import(path, "", ReadLimits.Default);
        Assert.Equal(["IMP-anlagenbau-x-R1", "IMP-anlagenbau-x-" + RuleIdentity.Hash8(@"registry|hklm\software\policies\beispiel|b"), imported.Rules[2].Id],
            imported.Rules.Select(r => r.Id));
        Assert.Matches("^IMP-anlagenbau-x-[0-9a-f]{8}$", imported.Rules[2].Id);
        Assert.All(imported.Rules, r => Assert.Equal("anlagenbau-x", r.RuleSetId));
        Assert.Equal([Severity.High, Severity.Medium, Severity.Medium], imported.Rules.Select(r => r.Severity));
        Assert.Equal(["system", "system", "services"], imported.Rules.Select(r => r.Category));
        Assert.Contains("Pflichtenheft 4.2", imported.Rules[2].Rationale.Get("en"), StringComparison.Ordinal);
        Assert.Contains("Kundenvorgaben", imported.Rules[0].Rationale.Get("de"), StringComparison.Ordinal);
        var b = imported.Baseline;
        Assert.Equal(("anlagenbau-x", "Kundenvorgaben", BaselineSourceKind.RiegelJson, Hashes.Sha256File(path), "kunde.json", FixedClock.Default, 3),
            (b.Id, b.Name, b.Kind, b.SourceSha256, b.SourceFileName, b.ImportedAtUtc, b.RuleCount));
        Assert.False(imported.ContentIncluded);
        AssertRulesValid(imported);
    }

    [Fact]
    public void IDs_bleiben_beim_erneuten_Import_gleich_auch_wenn_sich_die_Erwartung_aendert()
    {
        using var temp = new TempFolder();
        BaselineFile File(uint expected, string value = "B") => new(SchemaIds.Baseline, "golden", "Golden", null, null, null, null,
            [Reg(null, @"HKLM\SOFTWARE\Policies\Beispiel", value, expected), Reg("KEEP", @"HKLM\SOFTWARE\Policies\Beispiel", "C", expected)]);
        var first = Importer().Import(WriteJson(temp, "v1.json", File(1)), "Golden", ReadLimits.Default).Rules.Select(r => r.Id).ToList();
        var again = Importer().Import(WriteJson(temp, "v1b.json", File(1)), "Golden", ReadLimits.Default).Rules.Select(r => r.Id).ToList();
        var changed = Importer().Import(WriteJson(temp, "v2.json", File(7)), "Golden", ReadLimits.Default).Rules.Select(r => r.Id).ToList();
        var otherKey = Importer().Import(WriteJson(temp, "v3.json", File(1, "D")), "Golden", ReadLimits.Default).Rules.Select(r => r.Id).ToList();
        Assert.Equal(first, again);
        Assert.Equal(first, changed);
        Assert.NotEqual(first[0], otherKey[0]);
        Assert.Equal(first[1], otherKey[1]);
        // Groß/klein und ein „\“ am Ende ändern die Identität nicht
        Assert.Equal(RuleIdentity.Of(new RegistryCheck(@"HKLM\SOFTWARE\Policies\Beispiel\", "b", RegValueType.Dword, null, CheckOperator.Eq, JsonValues.Number(1), WhenMissing.Fail, null, null, null)),
            RuleIdentity.Of(new RegistryCheck(@"hklm\software\policies\BEISPIEL", "B", RegValueType.Dword, null, CheckOperator.Ne, JsonValues.Number(9), WhenMissing.NotCheckable, null, null, null)));
    }

    [Fact]
    public void Kollisionen_werden_erkannt()
    {
        using var temp = new TempFolder();
        var authors = WriteJson(temp, "a.json", new BaselineFile(SchemaIds.Baseline, "k", "K", null, null, null, null,
            [Reg("X1", @"HKLM\SOFTWARE\Policies\A", "B", 1), Reg("x1", @"HKLM\SOFTWARE\Policies\A", "C", 1)]));
        Assert.Equal(RglCodesParsing.BaselineIdCollision, Assert.Throws<RiegelException>(() => Importer().Import(authors, "", ReadLimits.Default)).Code);
        // Gleicher Schlüssel, andere Erwartung: gleiche Identität → Kollision
        var conflict = WriteJson(temp, "b.json", new BaselineFile(SchemaIds.Baseline, "k", "K", null, null, null, null,
            [Reg(null, @"HKLM\SOFTWARE\Policies\A", "B", 1), Reg(null, @"HKLM\SOFTWARE\Policies\A", "B", 2)]));
        Assert.Equal(RglCodesParsing.BaselineIdCollision, Assert.Throws<RiegelException>(() => Importer().Import(conflict, "", ReadLimits.Default)).Code);
        // Exakt dieselbe Vorgabe zweimal: einmal übernommen
        var same = WriteJson(temp, "c.json", new BaselineFile(SchemaIds.Baseline, "k", "K", null, null, null, null,
            [Reg(null, @"HKLM\SOFTWARE\Policies\A", "B", 1), Reg(null, @"HKLM\SOFTWARE\Policies\A", "B", 1)]));
        Assert.Single(Importer().Import(same, "", ReadLimits.Default).Rules);
    }

    [Fact]
    public void Ungueltige_JSON_Baselines()
    {
        using var temp = new TempFolder();
        string Bad(string id, BaselineRule rule) => WriteJson(temp, Guid.NewGuid().ToString("N") + ".json",
            new BaselineFile(SchemaIds.Baseline, id, "K", null, null, null, null, [rule]));
        Assert.Equal(RglCodesParsing.BaselineIdInvalid, Assert.Throws<RiegelException>(() => Importer().Import(Bad("Gross Und Leer", Reg(null, @"HKLM\SOFTWARE\A", "B", 1)), "", ReadLimits.Default)).Code);
        Assert.Equal(RglCodesParsing.BaselineIdInvalid, Assert.Throws<RiegelException>(() => Importer().Import(Bad("k", Reg("mit leer", @"HKLM\SOFTWARE\A", "B", 1)), "", ReadLimits.Default)).Code);
        Assert.Equal(RglCodesParsing.BaselineInvalid, Assert.Throws<RiegelException>(() => Importer().Import(Bad("k", Reg(null, @"HKCU\Software\A", "B", 1)), "", ReadLimits.Default)).Code);
        Assert.Equal(RglCodesParsing.BaselineInvalid, Assert.Throws<RiegelException>(() => Importer().Import(Bad("k", Reg(null, @"HKLM\SOFTWARE\A", "B", 1) with { Category = "erfunden" }), "", ReadLimits.Default)).Code);
        Assert.Equal(RglCodesParsing.BaselineInvalid, Assert.Throws<RiegelException>(() => Importer().Import(Bad("k",
            new BaselineRule(null, "Recht", null, null, new UserRightCheck("SeDebugPrivilege", UserRightMode.OnlyFrom, ["Administratoren"]), null, null)), "", ReadLimits.Default)).Code);
        CheckSpec deep = new LifecycleCheck();
        for (int i = 0; i < 20; i++) deep = new NotCheck(deep);
        Assert.Equal(RglCodesParsing.BaselineInvalid, Assert.Throws<RiegelException>(() => Importer().Import(Bad("k", new BaselineRule(null, "Tief", null, null, deep, null, null)), "", ReadLimits.Default)).Code);
        // Unbekannte Felder und Prüfarten sind im geschlossenen Vokabular Fehler
        var unknown = Path.Combine(temp.Path, "unbekannt.json");
        File.WriteAllText(unknown, """{"schema":"riegel.baseline/1","id":"k","name":"K","rules":[{"title":"T","check":{"type":"script","code":"x"}}]}""");
        Assert.Equal(RglCodesCommon.InvalidJson, Assert.Throws<RiegelException>(() => Importer().Import(unknown, "", ReadLimits.Default)).Code);
        var schema = Path.Combine(temp.Path, "schema.json");
        File.WriteAllText(schema, """{"schema":"riegel.baseline/2","id":"k","name":"K","rules":[]}""");
        Assert.Equal(RglCodesCommon.UnsupportedSchema, Assert.Throws<RiegelException>(() => Importer().Import(schema, "", ReadLimits.Default)).Code);
    }

    [Fact]
    public void Grenzen_und_Formate()
    {
        using var temp = new TempFolder();
        var many = WriteJson(temp, "viel.json", new BaselineFile(SchemaIds.Baseline, "k", "K", null, null, null, null,
            [.. Enumerable.Range(0, 20).Select(i => Reg(null, @"HKLM\SOFTWARE\Policies\A", "V" + i, 1))]));
        Assert.Equal(RglCodesParsing.BaselineTooLarge, Assert.Throws<RiegelException>(() => Importer().Import(many, "", ReadLimits.Default with { MaxBaselineCsvRows = 10 })).Code);
        Assert.Equal(RglCodesParsing.BaselineTooLarge, Assert.Throws<RiegelException>(() => Importer().Import(many, "", ReadLimits.Default with { MaxBaselineCsvBytes = 100 })).Code);
        var txt = Path.Combine(temp.Path, "x.txt");
        File.WriteAllText(txt, "x");
        Assert.Equal(RglCodesParsing.BaselineUnknownFormat, Assert.Throws<RiegelException>(() => Importer().Import(txt, "", ReadLimits.Default)).Code);
        Assert.Equal(RglCodesCommon.NotFound, Assert.Throws<RiegelException>(() => Importer().Import(Path.Combine(temp.Path, "fehlt.json"), "", ReadLimits.Default)).Code);
        Assert.Equal(RglCodesParsing.BaselineEmpty, Assert.Throws<RiegelException>(() => Importer().Import(temp.Sub("leer"), "", ReadLimits.Default)).Code);
    }

    [Fact]
    public void Das_Beispiel_aus_der_Doku_ist_gueltig()
    {
        var doc = File.ReadAllText(Path.Combine(RepoPaths.Root, "docs", "baselines.md"));
        int start = doc.IndexOf("```json\n", StringComparison.Ordinal) + 8;
        var json = doc[start..doc.IndexOf("```", start, StringComparison.Ordinal)];
        LocalSchema.AssertValid(json, "docs/baselines.md");
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "beispiel.json");
        File.WriteAllText(path, json);
        var imported = Importer().Import(path, "", ReadLimits.Default);
        Assert.Equal("IMP-example-builder-hmi-HMI-01", imported.Rules[0].Id);
        Assert.Equal(2, imported.Rules.Count);
        AssertRulesValid(imported);
    }

    // ---------------------------------------------------------------- CSV-Vorlage

    [Fact]
    public void Die_Vorlage_aus_der_Doku_laesst_sich_importieren()
    {
        var path = Path.Combine(RepoPaths.Root, "docs", "assets", "baseline-template.csv");
        var imported = Importer().Import(path, "Vorlage", ReadLimits.Default);
        Assert.Equal(2, imported.Rules.Count);
        Assert.Equal(BaselineSourceKind.Csv, imported.Baseline.Kind);
        Assert.Equal("vorlage", imported.Baseline.Id);
        AssertRulesValid(imported);
        var header = File.ReadAllLines(path)[0];
        Assert.Equal(string.Join(",", BaselineCsvReader.Columns), header);
    }

    private const string Csv = """
        id,title,severity,type,key,value,op,expected,valueType
        R-1,Fernkonfiguration aus,high,registry,HKLM\SOFTWARE\Beispiel\Hmi,Remote,eq,0,dword
        ,Mehrere erlaubt,,registry,HKEY_LOCAL_MACHINE\SOFTWARE\Beispiel\Hmi,Modus,in,1;0x2,dword
        ,Text,low,registry,HKLM\SOFTWARE\Beispiel\Hmi,Name,eq,"Linie 3, Halle 2",sz
        ,Liste,,registry,HKLM\SOFTWARE\Beispiel\Hmi,Server,eq,a;b,multi_sz
        ,Nicht vorhanden,,registry,HKLM\SOFTWARE\Beispiel\Hmi,Debug,notExists,,dword
        ,Groß,,registry,HKLM\SOFTWARE\Beispiel\Hmi,Zaehler,ge,18446744073709551615,qword
        ,Druckdienst aus,,service,Spooler,,in,Disabled,
        ,Leitsystem läuft,,service,BeispielLeitsystem,state,eq,Running,
        ,Anmeldung überwachen,,auditSubcategory,0cce9215-69ae-11d9-bed3-505054503030,,,successAndFailure,
        ,Debuggen nur Admins,,userRight,SeDebugPrivilege,,onlyFrom,*S-1-5-32-544,
        ,Niemand als Teil des Betriebssystems,,userRight,SeTcbPrivilege,,empty,,
        """;

    [Fact]
    public void CSV_mit_allen_vier_Typen()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "Vorgaben Kunde Ä.csv");
        File.WriteAllText(path, Csv, new UTF8Encoding(true));
        var imported = Importer().Import(path, "", ReadLimits.Default);
        Assert.Equal("vorgaben-kunde-ae", imported.Baseline.Id);
        Assert.Equal(11, imported.Rules.Count);
        Assert.Equal("IMP-vorgaben-kunde-ae-R-1", imported.Rules[0].Id);
        var checks = imported.Rules.Select(r => r.Check).ToList();
        var modus = (RegistryCheck)checks[1];
        Assert.Equal((@"HKLM\SOFTWARE\Beispiel\Hmi", CheckOperator.In, "[1,2]"), (modus.Key, modus.Op, CanonicalValue.Render(modus.Expected)));
        Assert.Equal("Linie 3, Halle 2", ((RegistryCheck)checks[2]).Expected!.Value.GetString());
        Assert.Equal("[\"a\",\"b\"]", CanonicalValue.Render(((RegistryCheck)checks[3]).Expected));
        Assert.Null(((RegistryCheck)checks[4]).Expected);
        Assert.Equal("18446744073709551615", ((RegistryCheck)checks[5]).Expected!.Value.GetString());
        var spooler = (ServiceCheck)checks[6];
        Assert.Equal((AbsentOutcome.Pass, "Disabled"), (spooler.IfAbsent, spooler.StartMode!.Single()));
        var leitsystem = (ServiceCheck)checks[7];
        Assert.Equal((AbsentOutcome.Fail, "Running"), (leitsystem.IfAbsent, leitsystem.State!.Single()));
        Assert.Equal("{0CCE9215-69AE-11D9-BED3-505054503030}", ((AuditSubcategoryCheck)checks[8]).Guid);
        Assert.Equal(["S-1-5-32-544"], ((UserRightCheck)checks[9]).Sids);
        Assert.Equal(UserRightMode.Empty, ((UserRightCheck)checks[10]).Mode);
        Assert.Equal(["system", "system", "system", "system", "system", "system", "services", "services", "audit", "userRights", "userRights"],
            imported.Rules.Select(r => r.Category));
        Assert.Equal([false, false, false, false, false, false, false, false, true, true, true], imported.Rules.Select(r => r.NeedsAdmin));
        AssertRulesValid(imported);
    }

    [Fact]
    public void CSV_mit_Semikolon_wie_aus_deutschem_Excel()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "excel.csv");
        File.WriteAllText(path, "id;title;severity;type;key;value;op;expected;valueType\r\n;Erlaubt;;registry;HKLM\\SOFTWARE\\A;B;in;\"1;2\";dword\r\n", new UTF8Encoding(false));
        var check = (RegistryCheck)Importer().Import(path, "Excel", ReadLimits.Default).Rules.Single().Check;
        Assert.Equal("[1,2]", CanonicalValue.Render(check.Expected));
    }

    [Theory]
    [InlineData("id,title\n", 1, "severity")]
    [InlineData("id,title,severity,type,key,value,op,expected,valueType,extra\n", 1, "extra")]
    [InlineData("id,title,severity,type,key,value,op,expected,valueType\n,,,registry,HKLM\\A,B,eq,1,dword\n", 2, "title")]
    [InlineData("id,title,severity,type,key,value,op,expected,valueType\nX,T,kritisch,registry,HKLM\\A,B,eq,1,dword\n", 2, "severity")]
    [InlineData("id,title,severity,type,key,value,op,expected,valueType\nX,T,,feature,SMB1,,,1,\n", 2, "type")]
    [InlineData("id,title,severity,type,key,value,op,expected,valueType\nX,T,,registry,HKCU\\A,B,eq,1,dword\n", 2, "key")]
    [InlineData("id,title,severity,type,key,value,op,expected,valueType\nX,T,,registry,HKLM\\A,B,eq,eins,dword\n", 2, "expected")]
    [InlineData("id,title,severity,type,key,value,op,expected,valueType\nX,T,,registry,HKLM\\A,B,eq,4294967296,dword\n", 2, "expected")]
    [InlineData("id,title,severity,type,key,value,op,expected,valueType\nX,T,,registry,HKLM\\A,B,gt,a,sz\n", 2, "op")]
    [InlineData("id,title,severity,type,key,value,op,expected,valueType\nX,T,,registry,HKLM\\A,B,eq,1,binary\n", 2, "valueType")]
    [InlineData("id,title,severity,type,key,value,op,expected,valueType\nX,T,,registry,HKLM\\A,B,exists,1,dword\n", 2, "expected")]
    [InlineData("id,title,severity,type,key,value,op,expected,valueType\nX,T,,service,Spooler,,in,Aus,\n", 2, "expected")]
    [InlineData("id,title,severity,type,key,value,op,expected,valueType\nX,T,,auditSubcategory,keine-guid,,,success,\n", 2, "key")]
    [InlineData("id,title,severity,type,key,value,op,expected,valueType\nX,T,,userRight,SeDebugPrivilege,,onlyFrom,Administratoren,\n", 2, "expected")]
    [InlineData("id,title,severity,type,key,value,op,expected,valueType\nX,T,,userRight,SeDebugPrivilege,,empty,S-1-5-32-544,\n", 2, "expected")]
    [InlineData("id,title,severity,type,key,value,op,expected,valueType\nmit leer,T,,registry,HKLM\\A,B,eq,1,dword\n", 2, "id")]
    [InlineData("id,title,severity,type,key,value,op,expected,valueType\nX,\"offen,,registry\n", 0, "\"")]
    public void CSV_Fehler_nennen_Zeile_und_Spalte(string csv, int line, string column)
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "fehler.csv");
        File.WriteAllText(path, csv, new UTF8Encoding(false));
        var e = Assert.Throws<RiegelException>(() => Importer().Import(path, "", ReadLimits.Default));
        Assert.True(e.Code == RglCodesParsing.BaselineCsvInvalid, e.ToString());
        Assert.Equal(RglCodesParsing.Message(RglCodesParsing.BaselineCsvInvalid, "fehler.csv", line, column), e.Message);
    }

    [Fact]
    public void CSV_Grenzen_und_Kodierung()
    {
        using var temp = new TempFolder();
        var rows = Path.Combine(temp.Path, "viel.csv");
        File.WriteAllText(rows, "id,title,severity,type,key,value,op,expected,valueType\n"
            + string.Concat(Enumerable.Range(0, 20).Select(i => $",T{i},,registry,HKLM\\SOFTWARE\\A,V{i},eq,1,dword\n")));
        Assert.Equal(RglCodesParsing.BaselineTooLarge, Assert.Throws<RiegelException>(() => Importer().Import(rows, "", ReadLimits.Default with { MaxBaselineCsvRows = 10 })).Code);
        Assert.Equal(RglCodesParsing.BaselineTooLarge, Assert.Throws<RiegelException>(() => Importer().Import(rows, "", ReadLimits.Default with { MaxBaselineCsvBytes = 64 })).Code);
        var ansi = Path.Combine(temp.Path, "ansi.csv");
        File.WriteAllBytes(ansi, [.. Encoding.ASCII.GetBytes("id,title,severity,type,key,value,op,expected,valueType\n,"), 0xC4, .. Encoding.ASCII.GetBytes(",,registry,HKLM\\A,B,eq,1,dword\n")]);
        Assert.Equal(RglCodesParsing.InvalidEncoding, Assert.Throws<RiegelException>(() => Importer().Import(ansi, "", ReadLimits.Default)).Code);
        var utf16 = Path.Combine(temp.Path, "utf16.csv");
        File.WriteAllBytes(utf16, Utf16Bom("id,title\n"));
        Assert.Equal(RglCodesParsing.InvalidEncoding, Assert.Throws<RiegelException>(() => Importer().Import(utf16, "", ReadLimits.Default)).Code);
        var control = Path.Combine(temp.Path, "steuer.csv");
        File.WriteAllText(control, "id,title,severity,type,key,value,op,expected,valueType\n,T\u0007,,registry,HKLM\\A,B,eq,1,dword\n");
        Assert.Equal(RglCodesParsing.BaselineCsvInvalid, Assert.Throws<RiegelException>(() => Importer().Import(control, "", ReadLimits.Default)).Code);
    }

    // ---------------------------------------------------------------- GPO-Sicherung

    private const string GptTmpl = """
        [Unicode]
        Unicode=yes
        [System Access]
        MinimumPasswordLength = 14
        PasswordComplexity = 1
        EnableGuestAccount = 0
        NewGuestName = "Besuch"
        [Event Audit]
        AuditLogonEvents = 3
        [Registry Values]
        MACHINE\System\CurrentControlSet\Control\Lsa\NoLMHash=4,1
        MACHINE\Software\Beispiel\Banner=7,Zeile 1,Zeile 2
        MACHINE\Software\Beispiel\Titel=1,"Hinweis"
        [Privilege Rights]
        SeDebugPrivilege = *S-1-5-32-544
        SeTcbPrivilege =
        SeDenyNetworkLogonRight = Gast
        [Service General Setting]
        "Spooler",4,""
        [Version]
        signature="$CHICAGO$"
        Revision=1
        """;

    private const string AuditCsv = """
        Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value
        ,System,Audit Logon,{0cce9215-69ae-11d9-bed3-505054503030},Success and Failure,,3
        ,System,Audit Process Creation,{0cce922b-69ae-11d9-bed3-505054503030},Success,,1
        """;

    /// <summary>Eine erfundene GPO-Sicherung im Aufbau {GUID}\DomainSysvol\GPO\Machine (Schreibweise bewusst gemischt).</summary>
    private static string GpoBackup(string root, string guid, byte[]? pol = null, string? inf = GptTmpl, string? audit = AuditCsv, bool lower = false)
    {
        var machine = Path.Combine(root, guid, lower ? "domainsysvol" : "DomainSysvol", lower ? "gpo" : "GPO", lower ? "machine" : "Machine");
        Directory.CreateDirectory(machine);
        File.WriteAllText(Path.Combine(root, guid, "bkupInfo.xml"), "<BackupInst/>");
        File.WriteAllBytes(Path.Combine(machine, lower ? "Registry.pol" : "registry.pol"), pol ?? Pol(
            (@"Software\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast", 4, Dword(0)),
            (@"Software\Policies\Beispiel", "**del.Alt", 1, Sz(" ")),
            (@"Software\Policies\Beispiel", "**DeleteValues", 1, Sz("X;Y")),
            (@"Software\Policies\Beispiel", "**DelVals.", 1, Sz(" ")),
            (@"Software\Policies\Beispiel", "Liste", 7, MultiSz("a", "b")),
            (@"Software\Policies\Beispiel", "", 0, [])));
        var nt = Path.Combine(machine, "microsoft", "windows nt");
        if (inf is not null)
        {
            Directory.CreateDirectory(Path.Combine(nt, "SecEdit"));
            File.WriteAllBytes(Path.Combine(nt, "SecEdit", "GptTmpl.inf"), Utf16Bom(inf));
        }
        if (audit is not null)
        {
            Directory.CreateDirectory(Path.Combine(nt, "Audit"));
            File.WriteAllBytes(Path.Combine(nt, "Audit", "audit.csv"), Crlf(audit));
        }
        return Path.Combine(root, guid);
    }

    [Fact]
    public void GPO_Sicherung_wird_ins_geschlossene_Vokabular_uebersetzt()
    {
        using var temp = new TempFolder();
        var folder = GpoBackup(temp.Path, "{00000000-0000-4000-8000-00000000b001}");
        var imported = Importer().Import(folder, "Golden GPO Linie 3", ReadLimits.Default);
        Assert.Equal(("golden-gpo-linie-3", BaselineSourceKind.GpoBackup, "{00000000-0000-4000-8000-00000000b001}"),
            (imported.Baseline.Id, imported.Baseline.Kind, imported.Baseline.SourceFileName));
        Assert.True(Hashes.IsSha256(imported.Baseline.SourceSha256));
        var byTitle = imported.Rules.ToDictionary(r => r.Title.Get("en"), r => r.Check);

        var dns = (RegistryCheck)byTitle[@"HKLM\Software\Policies\Microsoft\Windows NT\DNSClient\EnableMulticast"];
        Assert.Equal((RegValueType.Dword, CheckOperator.Eq, "0", WhenMissing.Fail), (dns.ValueType, dns.Op, CanonicalValue.Render(dns.Expected), dns.WhenMissing));
        // **del. und **DeleteValues: Soll „nicht vorhanden“
        Assert.Equal(CheckOperator.NotExists, ((RegistryCheck)byTitle[@"HKLM\Software\Policies\Beispiel\Alt"]).Op);
        Assert.Equal(CheckOperator.NotExists, ((RegistryCheck)byTitle[@"HKLM\Software\Policies\Beispiel\X"]).Op);
        Assert.Equal(CheckOperator.NotExists, ((RegistryCheck)byTitle[@"HKLM\Software\Policies\Beispiel\Y"]).Op);
        var liste = (RegistryCheck)byTitle[@"HKLM\Software\Policies\Beispiel\Liste"];
        Assert.Equal((MultiSzMode.List, "[\"a\",\"b\"]"), (liste.MultiSz!.Value, CanonicalValue.Render(liste.Expected)));

        var minLen = (AccountPolicyCheck)byTitle["[System Access] MinimumPasswordLength"];
        Assert.Equal((AccountPolicyField.MinPwLen, "14"), (minLen.Field, CanonicalValue.Render(minLen.Expected)));
        Assert.Equal(AccountPolicyField.PasswordComplexity, ((AccountPolicyCheck)byTitle["[System Access] PasswordComplexity"]).Field);
        var guest = (SeceditCheck)byTitle["[System Access] EnableGuestAccount"];
        Assert.Equal((SeceditValueKind.Number, "0"), (guest.ValueKind, CanonicalValue.Render(guest.Expected)));
        Assert.Equal("\"Besuch\"", CanonicalValue.Render(((SeceditCheck)byTitle["[System Access] NewGuestName"]).Expected));
        Assert.Equal("Event Audit", ((SeceditCheck)byTitle["[Event Audit] AuditLogonEvents"]).Section);

        var nolm = (RegistryCheck)byTitle[@"HKLM\System\CurrentControlSet\Control\Lsa\NoLMHash"];
        Assert.Equal((RegValueType.Dword, "1"), (nolm.ValueType, CanonicalValue.Render(nolm.Expected)));
        Assert.Equal("[\"Zeile 1\",\"Zeile 2\"]", CanonicalValue.Render(((RegistryCheck)byTitle[@"HKLM\Software\Beispiel\Banner"]).Expected));
        Assert.Equal("Hinweis", ((RegistryCheck)byTitle[@"HKLM\Software\Beispiel\Titel"]).Expected!.Value.GetString());

        // Benutzerrechte: genau diese Liste (mustInclude und onlyFrom); Namen statt SIDs werden übergangen
        var debug = (AllOfCheck)byTitle["SeDebugPrivilege"];
        Assert.Equal([UserRightMode.MustInclude, UserRightMode.OnlyFrom], debug.Checks.Cast<UserRightCheck>().Select(c => c.Mode));
        Assert.All(debug.Checks.Cast<UserRightCheck>(), c => Assert.Equal(["S-1-5-32-544"], c.Sids));
        Assert.Equal(UserRightMode.Empty, ((UserRightCheck)byTitle["SeTcbPrivilege"]).Mode);
        Assert.DoesNotContain("SeDenyNetworkLogonRight", byTitle.Keys);

        var logon = (AuditSubcategoryCheck)byTitle["Audit Logon {0CCE9215-69AE-11D9-BED3-505054503030}"];
        Assert.Equal(AuditExpectation.SuccessAndFailure, logon.Expected);
        Assert.Equal(AuditExpectation.Success, ((AuditSubcategoryCheck)byTitle["Audit Process Creation {0CCE922B-69AE-11D9-BED3-505054503030}"]).Expected);

        // Dienste, **DelVals und reine Schlüssel sind nicht im Vokabular des GPO-Imports
        Assert.DoesNotContain(imported.Rules, r => r.Check is ServiceCheck);
        // registry.pol 5 (EnableMulticast, Alt, X, Y, Liste), GptTmpl.inf 10, audit.csv 2
        Assert.Equal(17, imported.Rules.Count);
        AssertRulesValid(imported);
    }

    [Fact]
    public void GPO_Sicherung_gibt_beim_erneuten_Import_dieselben_IDs()
    {
        using var temp = new TempFolder();
        var a = Importer().Import(GpoBackup(temp.Sub("a"), "{00000000-0000-4000-8000-00000000b001}"), "Golden", ReadLimits.Default);
        var changed = GptTmpl.Replace("MinimumPasswordLength = 14", "MinimumPasswordLength = 16", StringComparison.Ordinal);
        var b = Importer().Import(GpoBackup(temp.Sub("b"), "{00000000-0000-4000-8000-00000000b001}", inf: changed), "Golden", ReadLimits.Default);
        Assert.Equal(a.Rules.Select(r => r.Id), b.Rules.Select(r => r.Id));
        Assert.NotEqual(a.Baseline.SourceSha256, b.Baseline.SourceSha256);
    }

    [Fact]
    public void Mehrere_GPOs_flache_Ordner_und_Schreibweisen()
    {
        using var temp = new TempFolder();
        // Zwei GPOs mit derselben Einstellung (gleicher Wert): einmal übernommen
        var set = temp.Sub("GPOs");
        GpoBackup(set, "{00000000-0000-4000-8000-00000000b001}", audit: null);
        GpoBackup(set, "{00000000-0000-4000-8000-00000000b002}", inf: null, audit: null, lower: true);
        var both = Importer().Import(set, "Satz", ReadLimits.Default);
        Assert.Single(both.Rules, r => r.Title.Get("en").EndsWith(@"DNSClient\EnableMulticast", StringComparison.Ordinal));
        // Widersprüchliche Werte in zwei GPOs: Kollision
        var conflict = temp.Sub("Widerspruch");
        GpoBackup(conflict, "{00000000-0000-4000-8000-00000000b001}", audit: null);
        GpoBackup(conflict, "{00000000-0000-4000-8000-00000000b002}", pol: Pol((@"Software\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast", 4, Dword(1))), inf: null, audit: null);
        Assert.Equal(RglCodesParsing.BaselineIdCollision, Assert.Throws<RiegelException>(() => Importer().Import(conflict, "Widerspruch", ReadLimits.Default)).Code);
        // Ordner mit den Dateien direkt darin
        var flat = temp.Sub("flach");
        File.WriteAllBytes(Path.Combine(flat, "registry.pol"), Pol((@"Software\Policies\A", "B", 4, Dword(1))));
        File.WriteAllBytes(Path.Combine(flat, "GptTmpl.inf"), Utf16Bom("[System Access]\nLockoutBadCount = 10\n"));
        Assert.Equal(2, Importer().Import(flat, "", ReadLimits.Default).Rules.Count);
    }

    [Fact]
    public void Kaputte_GPO_Dateien_ergeben_RGL_Codes()
    {
        using var temp = new TempFolder();
        var badPol = temp.Sub("pol");
        File.WriteAllBytes(Path.Combine(badPol, "registry.pol"), [0x50, 0x52, 0x65, 0x67, 1, 0, 0, 0, 0x5B]);
        Assert.Equal(RglCodesParsing.RegistryPolInvalid, Assert.Throws<RiegelException>(() => Importer().Import(badPol, "", ReadLimits.Default)).Code);
        var badInf = temp.Sub("inf");
        File.WriteAllBytes(Path.Combine(badInf, "GptTmpl.inf"), [0xFF, 0xFE, 0x00, 0xD8]);
        Assert.Equal(RglCodesParsing.InvalidEncoding, Assert.Throws<RiegelException>(() => Importer().Import(badInf, "", ReadLimits.Default)).Code);
        var bigPol = temp.Sub("gross");
        File.WriteAllBytes(Path.Combine(bigPol, "registry.pol"), new byte[5000]);
        Assert.Equal(RglCodesParsing.BundleFileTooLarge, Assert.Throws<RiegelException>(() => Importer().Import(bigPol, "", ReadLimits.Default with { MaxRegistryPolBytes = 4096 })).Code);
    }
}
