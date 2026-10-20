// SPDX-License-Identifier: MIT
using System.Text.Json.Nodes;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Plants;
using Riegel.Core.Samples;
using CliRunner = Riegel.Tests.Cli.Cli;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Plants.WorkflowKit;

namespace Riegel.Tests.Registers;

/// <summary>
/// Fehlende Pflichtfelder (Prüfung vom 2026-10-20): Eine Vorlage ohne „scope“ wurde übernommen, danach endeten list,
/// validate, report und check mit RGL-0001 bzw. UNKNOWN, und die Oberfläche konnte die Anlage nicht öffnen. Jetzt
/// lehnt der Import solche Vorlagen ab (RGL-0004 mit Datei, Eintrag und Feld), und eine Anlage, deren Register schon
/// einen solchen Eintrag hat, bleibt lesbar: der Eintrag ist ungültig und wirkt nicht.
/// </summary>
public sealed class MissingFieldTests
{
    private static string Template(string folder, string kind, Action<JsonObject> change)
    {
        var source = kind == "exceptions"
            ? Path.Combine(RepoPaths.Templates, "exceptions", "beispiel-anlagenbau.exceptions-template.json")
            : Path.Combine(RepoPaths.Templates, "tailoring", "hmi.tailoring-template.json");
        var root = JsonNode.Parse(File.ReadAllText(source))!.AsObject();
        var list = root[kind == "exceptions" ? "exceptions" : "entries"]!.AsArray();
        change(list[0]!.AsObject());
        var path = Path.Combine(folder, "kaputt." + kind + "-template.json");
        File.WriteAllText(path, root.ToJsonString());
        return path;
    }

    [Theory]
    [InlineData("exceptions", "scope", "exceptions[0].scope")]
    [InlineData("exceptions", "category", "exceptions[0].category")]
    [InlineData("exceptions", "justification", "exceptions[0].justification")]
    [InlineData("tailoring", "scope", "entries[0].scope")]
    [InlineData("tailoring", "reason", "entries[0].reason")]
    [InlineData("tailoring", "ruleId", "entries[0].ruleId")]
    public void Vorlage_ohne_Pflichtfeld_wird_abgelehnt(string kind, string field, string where)
    {
        using var temp = new TempFolder();
        var path = Template(temp.Path, kind, e => e.Remove(field));
        var e = Assert.Throws<RiegelException>(() => Services().Registers.ImportTemplate(Set(), path, ReadLimits.Default));
        Assert.Equal(RglCodesCommon.InvalidJson, e.Code);
        Assert.Contains("kaputt." + kind + "-template.json: " + where, e.Message, StringComparison.Ordinal);

        // null zählt wie fehlend, ebenso ein Geltungsbereich ohne seine Felder
        var withNull = Template(temp.Path, kind, x => x[field] = null);
        Assert.Equal(RglCodesCommon.InvalidJson,
            Assert.Throws<RiegelException>(() => Services().Registers.ImportTemplate(Set(), withNull, ReadLimits.Default)).Code);
    }

    [Fact]
    public void Vorlage_mit_unvollstaendigem_Geltungsbereich_wird_abgelehnt()
    {
        using var temp = new TempFolder();
        var path = Template(temp.Path, "exceptions", e => e["scope"]!.AsObject().Remove("allMachines"));
        var e = Assert.Throws<RiegelException>(() => Services().Registers.ImportTemplate(Set(), path, ReadLimits.Default));
        Assert.Contains("exceptions[0].scope.allMachines", e.Message, StringComparison.Ordinal);
        // die mitgelieferten Vorlagen bleiben gültig
        foreach (var file in Directory.GetFiles(RepoPaths.Templates, "*.json", SearchOption.AllDirectories))
            Services().Registers.ImportTemplate(Set(), file, ReadLimits.Default);
    }

    [Fact]
    public void CLI_lehnt_Vorlage_ohne_scope_ab_und_die_Anlage_bleibt_unveraendert()
    {
        using var temp = new TempFolder();
        using var plant = new TempPlant();
        var path = Template(temp.Path, "exceptions", e => e.Remove("scope"));
        var run = CliRunner.Run("exceptions", "import-template", path, "--plant", plant.Folder);
        Assert.Equal(3, run.Exit);
        Assert.Contains(RglCodesCommon.InvalidJson, run.Err, StringComparison.Ordinal);
        Assert.Contains("exceptions[0].scope", run.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(plant.PathOf("exceptions.json")));
    }

    [Fact]
    public void Register_mit_fehlenden_Pflichtfeldern_bleibt_lesbar_und_der_Eintrag_ist_ungueltig()
    {
        using var plant = new TempPlant();
        plant.WriteRegisters(SampleData.Registers());
        // Wie nach dem alten Import: ein Eintrag je Register ohne scope, eine Ausnahme ohne approval
        void Strip(string file, string list, params string[] fields)
        {
            var root = JsonNode.Parse(File.ReadAllText(plant.PathOf(file)))!.AsObject();
            var first = root[list]!.AsArray()[0]!.AsObject();
            foreach (var f in fields) first.Remove(f);
            File.WriteAllText(plant.PathOf(file), root.ToJsonString());
        }
        Strip("exceptions.json", "exceptions", "scope", "approval");
        Strip("tailoring.json", "entries", "scope");
        Strip("measures.json", "measures", "scope", "action");

        var services = Services();
        var opened = services.Plants.Open(plant.Folder, readOnly: true);
        var registers = services.Registers.Load(opened);
        var ex = registers.Exceptions.Exceptions[0];
        Assert.Equal(RegisterScope.Empty, ex.Scope);
        Assert.Equal(Approval.None, ex.Approval);
        Assert.Equal(RegisterScope.Empty, registers.Tailoring.Entries[0].Scope);
        Assert.Equal("", registers.Measures.Measures[0].Action);

        var catalogue = services.Catalogue.LoadEmbedded();
        var issues = services.Registers.Validate(registers, catalogue, opened, Today);
        Assert.Contains(issues, i => i.Register == RegisterKind.Exceptions && i.EntryId == ex.Id && i.Code == RglCodesWorkflow.EmptyScope);
        Assert.Contains(issues, i => i.Register == RegisterKind.Tailoring && i.EntryId == registers.Tailoring.Entries[0].Id && i.Code == RglCodesWorkflow.EmptyScope);
        Assert.Contains(issues, i => i.Register == RegisterKind.Measures && i.Code == RglCodesWorkflow.FieldRequired && i.Field == "action");

        // Befehle, die vorher mit RGL-0001 endeten
        foreach (var args in new[]
        {
            new[] { "exceptions", "list", "--plant", plant.Folder },
            ["exceptions", "validate", "--plant", plant.Folder],
            ["tailoring", "list", "--plant", plant.Folder],
            ["measures", "list", "--plant", plant.Folder],
            ["check", "--snapshot", RepoPaths.Synthetic("win11-24h2-workgroup-admin"), "--plant", plant.Folder, "--read-only"],
        })
        {
            var run = CliRunner.Run(args);
            Assert.True(run.Exit is 0 or 1 or 2, string.Join(" ", args) + "\n" + run);
            Assert.DoesNotContain(RglCodesCommon.Unexpected, run.Err, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Register_mit_kaputtem_JSON_nennt_die_Datei()
    {
        using var plant = new TempPlant();
        File.WriteAllText(plant.PathOf("tailoring.json"), "{\"schema\": \"riegel.tailoring/1\", \"plantId\": \"testanlage\", \"entries\": [ {\"id\": 5 } ] }");
        var e = Assert.Throws<RiegelException>(() => Services().Registers.Load(plant.Plant));
        Assert.Equal(RglCodesCommon.InvalidJson, e.Code);
        Assert.Contains("tailoring.json", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Anlage_ohne_settings_oeffnet_mit_den_Standardwerten()
    {
        using var plant = new TempPlant();
        var root = JsonNode.Parse(File.ReadAllText(plant.PathOf(TempPlant.PlantFile)))!.AsObject();
        root.Remove("settings");
        File.WriteAllText(plant.PathOf(TempPlant.PlantFile), root.ToJsonString());
        var opened = Services().Plants.Open(plant.Folder, readOnly: true);
        Assert.Equal(PlantSettings.Default.WarnDays, opened.Settings.WarnDays);
        Assert.Equal(0, CliRunner.Run("exceptions", "list", "--plant", plant.Folder).Exit);
    }
}
