// SPDX-License-Identifier: MIT
using Riegel.Commands;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Cli;

/// <summary>
/// Regressionstests (Review docs-ui-1, docs-ui-9): <c>import &lt;Ordner&gt;</c> nimmt die Exporte eines Ordners von
/// <c>collect --out</c> auf (vorher RGL-0260 „kein Bordmittel-Paket“), und <c>tailoring import-template hmi</c> erreicht
/// die eingebauten Rollenvorlagen, die es in keinem Paket als Datei gibt.
/// </summary>
public sealed class ImportFolderAndTemplateTests
{
    [Fact]
    public void Import_nimmt_die_Exporte_eines_Ordners_auf()
    {
        using var temp = new TempFolder();
        using var plant = new TempPlant();
        var exports = temp.Sub("Exporte");
        SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").CollectedAt(TestRuns.Collected).WriteTo(exports);
        SnapshotBuilder.Windows10Ltsc2019("W11-TEST").CollectedAt(TestRuns.Collected).WriteTo(exports);
        File.WriteAllText(Path.Combine(exports, "liesmich.txt"), "kein Export\n");

        var result = Cli.Run("import", exports, "--plant", plant.Folder, "--format", "json");
        Assert.True(result.Exit is 0 or 1 or 2, result.ToString());
        Assert.Single(Directory.GetFiles(plant.PathOf("runs/HMI-TEST"), "*.results.json"));
        Assert.Single(Directory.GetFiles(plant.PathOf("runs/W11-TEST"), "*.results.json"));
        var record = RiegelJson.ReadFile<ImportRecord>(Directory.GetFiles(plant.PathOf("imports"), "*.import.json").Single(), 1 << 20);
        Assert.Equal(2, record.Files.Count);
    }

    [Fact]
    public void Ein_Ordner_ohne_Exporte_ist_RGL0470()
    {
        using var temp = new TempFolder();
        using var plant = new TempPlant();
        var result = Cli.Run("import", temp.Sub("leer"), "--plant", plant.Folder);
        Assert.Equal(3, result.Exit);
        Assert.Contains(RglCodesCli.NoExportsInFolder, result.Err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(plant.PathOf("imports")));
    }

    [Fact]
    public void Bordmittel_Pakete_bleiben_Pakete()
    {
        Assert.True(Riegel.Commands.Commands.ImportCommand.IsBuiltinBundle(Path.Combine(RepoPaths.SyntheticFixtures, "PG-ALT-01_20261010.riegel-builtin")));
        using var temp = new TempFolder();
        var renamed = temp.Sub("umbenannt");
        File.WriteAllText(Path.Combine(renamed, "hostname.txt"), "PG-ALT-01\r\n");
        Assert.True(Riegel.Commands.Commands.ImportCommand.IsBuiltinBundle(renamed));
        Assert.False(Riegel.Commands.Commands.ImportCommand.IsBuiltinBundle(temp.Sub("Exporte")));
    }

    [Fact]
    public void Tailoring_import_template_nimmt_eine_eingebaute_Rollenvorlage_beim_Namen()
    {
        using var plant = new TempPlant();
        var result = Cli.Run("tailoring", "import-template", "hmi", "--plant", plant.Folder);
        Assert.True(result.Exit == 0, result.ToString());
        var tailoring = RiegelJson.ReadFile<TailoringFile>(plant.PathOf("tailoring.json"), 1 << 20);
        Assert.NotEmpty(tailoring.Entries);
        Assert.All(tailoring.Entries, e => Assert.Null(e.Approval?.ApprovalRecord));

        var unknown = Cli.Run("tailoring", "import-template", "gibtsnicht", "--plant", plant.Folder);
        Assert.Equal(3, unknown.Exit);
        Assert.Contains(RglCodesCli.UnknownTemplate, unknown.Err, StringComparison.Ordinal);
        Assert.Contains("jump-host", unknown.Err, StringComparison.Ordinal);

        var missingFile = Cli.Run("tailoring", "import-template", Path.Combine(plant.Folder, "fehlt.tailoring-template.json"), "--plant", plant.Folder);
        Assert.Equal(3, missingFile.Exit);
        Assert.Contains(RglCodesCommon.NotFound, missingFile.Err, StringComparison.Ordinal);

        var exceptions = Cli.Run("exceptions", "import-template", "beispiel-anlagenbau", "--plant", plant.Folder);
        Assert.True(exceptions.Exit == 0, exceptions.ToString());
    }
}
