// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Plants;
using Riegel.Tests.TestSupport;
using CliRunner = Riegel.Tests.Cli.Cli;
using UiCulture = Riegel.Tests.Cli.UiCulture;

namespace Riegel.Tests.Registers;

/// <summary>
/// Prüfung vom 2026-10-20: <c>exceptions approve --ids EX-0003</c> gab die Ausnahme der mitgelieferten Vorlage frei,
/// obwohl ihr der akzeptierte Wert fehlt (RGL-0430), und <c>list</c> zeigte sie danach als „valid“. Jetzt endet die
/// Freigabe mit RGL-0448 ohne Änderung, und die Liste zeigt ungültige Einträge als „invalid“.
/// </summary>
public sealed class ApproveInvalidTests
{
    private static readonly string Template = Path.Combine(RepoPaths.Templates, "exceptions", "beispiel-anlagenbau.exceptions-template.json");

    [Fact]
    public void Ungueltige_Ausnahme_wird_nicht_freigegeben_und_die_Liste_nennt_sie_ungueltig()
    {
        using var _ = new UiCulture("en");
        using var plant = new TempPlant();
        Assert.Equal(0, CliRunner.Run("exceptions", "import-template", Template, "--plant", plant.Folder).Exit);
        var before = File.ReadAllBytes(plant.PathOf("exceptions.json"));

        var approve = CliRunner.Run("exceptions", "approve", "--plant", plant.Folder, "--ids", "EX-0003", "--role", "ISB", "--record", "CHG-1",
            "--review-by", "2027-10-01");
        Assert.Equal(3, approve.Exit);
        Assert.Contains(RglCodesWorkflow.InvalidNotApproved + ": Not approved: EX-0003 is invalid (" + RglCodesWorkflow.AcceptedValueRequired, approve.Err,
            StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(plant.PathOf("exceptions.json")));

        // Zusammen mit einer gültigen: nichts wird freigegeben
        Assert.Equal(3, CliRunner.Run("exceptions", "approve", "--plant", plant.Folder, "--ids", "EX-0002", "EX-0003", "--role", "ISB", "--record", "CHG-1",
            "--review-by", "2027-10-01").Exit);
        Assert.Equal(before, File.ReadAllBytes(plant.PathOf("exceptions.json")));

        // Eine gültige allein schon
        var ok = CliRunner.Run("exceptions", "approve", "--plant", plant.Folder, "--ids", "EX-0002", "--role", "ISB", "--record", "CHG-1",
            "--review-by", "2027-10-01");
        Assert.True(ok.Exit == 0, ok.ToString());

        var list = CliRunner.Run("exceptions", "list", "--plant", plant.Folder, "--format", "json");
        Assert.Equal(0, list.Exit);
        var states = list.Json().GetProperty("entries").EnumerateArray()
            .ToDictionary(e => e.GetProperty("entry").GetProperty("id").GetString()!, e => e.GetProperty("state").GetString());
        Assert.Equal("invalid", states["EX-0003"]);
        Assert.Equal("valid", states["EX-0002"]);
        Assert.Contains("EX-0003  CRED-09  invalid", CliRunner.Run("exceptions", "list", "--plant", plant.Folder).Out.Replace("·", " "), StringComparison.Ordinal);
    }

    [Fact]
    public void Ungueltige_Ausnahme_mit_Freigabe_aus_einer_Datei_zeigt_die_Liste_als_ungueltig()
    {
        using var plant = new TempPlant();
        Assert.Equal(0, CliRunner.Run("exceptions", "import-template", Template, "--plant", plant.Folder).Exit);
        // Freigabe von Hand eingetragen (wie mit Riegel 0.1.0 möglich)
        var file = RiegelJson.ReadFile<ExceptionFile>(plant.PathOf("exceptions.json"), 1 << 20);
        var approved = file with
        {
            Exceptions = [.. file.Exceptions.Select(e => e.Id == "EX-0003" ? e with
            {
                Approval = new Approval("ISB", null, new DateOnly(2026, 10, 1), new DateOnly(2027, 10, 1), "CHG-1",
                    ContentHashes.Of(e)),
            } : e)],
        };
        RiegelJson.WriteFile(plant.PathOf("exceptions.json"), approved);
        var list = CliRunner.Run("exceptions", "list", "--plant", plant.Folder, "--format", "json");
        var entry = list.Json().GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("entry").GetProperty("id").GetString() == "EX-0003");
        Assert.Equal("invalid", entry.GetProperty("state").GetString());
    }
}
