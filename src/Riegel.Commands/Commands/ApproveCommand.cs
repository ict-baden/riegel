// SPDX-License-Identifier: MIT
using Riegel.Commands.Parsing;
using Riegel.Commands.Resources;
using Riegel.Core.Model;

namespace Riegel.Commands.Commands;

/// <summary>
/// <c>approve --plant &lt;dir&gt; --machine &lt;host&gt;… [--run &lt;id&gt;] --role "&lt;role&gt;" --record "&lt;ref&gt;" [--name "&lt;n&gt;"]
/// [--date &lt;d&gt;]</c> (spec 2.14, 7.7): einen Lauf je Rechner als freigegebenen Stand festhalten
/// (<c>approved/&lt;host&gt;.json</c>; den Verlauf schreibt der Anlagenspeicher). Ohne <c>--run</c> der neueste Lauf.
/// </summary>
internal static class ApproveCommand
{
    private static readonly OptionSpec[] Options =
    [
        new("--plant", OptionKind.Value),
        new("--machine", OptionKind.List, SplitCommas: true),
        new("--run", OptionKind.Value),
        new("--role", OptionKind.Value),
        new("--record", OptionKind.Value),
        new("--name", OptionKind.Value),
        new("--date", OptionKind.Value),
    ];

    public static int Run(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("approve", args, Options);
        var plantDir = a.Required("--plant", "<folder>");
        var hosts = a.RequiredList("--machine", "<host>");
        var role = NotBlank(a, "--role", "\"<role>\"");
        var record = NotBlank(a, "--record", "\"<reference>\"");
        var date = a.Date("--date") ?? ctx.Services.Today;
        var runId = a.Value("--run");
        if (runId is not null && hosts.Count > 1) throw RglCodesCli.Error(RglCodesCli.ConflictingOptions, "--run", "--machine <host> <host>…");
        var plant = ctx.OpenPlant(plantDir, forWrite: true, "approve");

        // Erst alle Läufe finden, dann schreiben: ein fehlender Rechner verhindert die ganze Freigabe
        var runs = hosts.Select(h => runId is null ? CompareCommands.Latest(ctx, plant, h) : CompareCommands.RunOf(ctx, plant, h, runId)).ToList();
        foreach (var run in runs)
        {
            var resultsFile = Path.Combine(plant.Folder!, ReportFileNames.RunFile(run.HostName, run.Id).Replace('/', Path.DirectorySeparatorChar));
            var resultsSha = File.Exists(resultsFile) ? Hashes.Sha256File(resultsFile) : Hashes.Sha256Hex(Core.Model.Json.RiegelJson.SerializeToUtf8Bytes(run));
            var approval = new Approval(role, a.Value("--name"), date, null, record, null);
            var state = new ApprovedState(SchemaIds.Approved, run.HostName, run.Id, run.Snapshot.Sha256, resultsSha, run.Catalogue.Version,
                run.Registers, approval);
            ctx.Services.Plants.SaveApproved(plant, state);
            ctx.Line(CliStrings.Cli_Approved, run.HostName, run.Id, role, record, CommandContext.D(date));
        }
        return CommandApp.Ok;
    }

    /// <summary>Pflichttext, der nicht nur aus Leerzeichen besteht.</summary>
    internal static string NotBlank(ParsedArguments a, string name, string placeholder)
    {
        var value = a.Required(name, placeholder);
        return string.IsNullOrWhiteSpace(value) ? throw RglCodesCli.Error(RglCodesCli.InvalidValue, name, value, placeholder) : value.Trim();
    }
}
