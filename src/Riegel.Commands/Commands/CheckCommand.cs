// SPDX-License-Identifier: MIT
using Riegel.Commands.Alerting;
using Riegel.Commands.Parsing;
using Riegel.Commands.Resources;
using Riegel.Core.Model;

namespace Riegel.Commands.Commands;

/// <summary>
/// <c>check [--plant &lt;dir&gt;] [--snapshot &lt;file&gt;…|--this-pc] [--brief] [--alert change|state] [--critical &lt;list&gt;]
/// [--warning &lt;list&gt;] [--no-store] [--format text|json]</c> (spec 7.7). Ohne <c>--plant</c> nur auswerten und
/// ausgeben; mit <c>--plant</c> Register anwenden, Lauf speichern, mit vorigem Lauf und freigegebenem Stand vergleichen.
/// Unter Windows ohne <c>--snapshot</c>: dieser PC. Exitcodes nach Nagios (0/1/2/3) aus den Tokens der Alarmart.
/// </summary>
internal static class CheckCommand
{
    private static readonly OptionSpec[] Options =
    [
        new("--plant", OptionKind.Value),
        new("--snapshot", OptionKind.List),
        new("--this-pc", OptionKind.Flag),
        new("--brief", OptionKind.Flag),
        new("--alert", OptionKind.Value),
        new("--critical", OptionKind.Value),
        new("--warning", OptionKind.Value),
        new("--no-store", OptionKind.Flag),
        new("--format", OptionKind.Value),
    ];

    public static int Run(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("check", args, Options);
        a.Exclusive("--snapshot", "--this-pc");
        a.Exclusive("--brief", "--format");
        var format = a.Choice("--format", "text", "json") ?? "text";
        var plantDir = a.Value("--plant");
        var policy = Policy(a, plantDir is not null);

        var snapshots = a.Values("--snapshot").Select(Path.GetFullPath).ToList();
        bool thisPc = snapshots.Count == 0;
        if (thisPc && !ctx.Services.Platform.CanCollect)
            throw RglCodesCli.Error(RglCodesCli.WindowsOnly, "check --this-pc", CommandContext.ProgramName + " check --snapshot <file>");

        var pipeline = new Pipeline(ctx);
        var assessments = new List<RunAssessment>();
        if (plantDir is null)
        {
            if (thisPc)
            {
                var snapshot = ctx.Services.Platform.CollectThisMachine(new CollectOptions(), ctx.Cancel);
                assessments.Add(pipeline.WithoutPlant(snapshot, CliStrings.Cli_SourceThisPc, policy));
            }
            foreach (var path in snapshots)
            {
                ctx.Cancel.ThrowIfCancellationRequested();
                pipeline.CheckSidecar(path);
                assessments.Add(pipeline.WithoutPlant(pipeline.Read(path), path, policy));
            }
        }
        else
        {
            bool store = !a.Has("--no-store") && !ctx.ReadOnly;
            var plant = OpenOrCreate(ctx, plantDir, store);
            if (thisPc)
            {
                var snapshot = ctx.Services.Platform.CollectThisMachine(new CollectOptions(), ctx.Cancel);
                using var temp = new PrivateTemp();
                var file = SnapshotFile.Write(snapshot, temp.Path);
                assessments.Add(pipeline.WithPlant(plant, file, snapshot, store, policy, CliStrings.Cli_SourceThisPc));
            }
            foreach (var path in snapshots)
            {
                ctx.Cancel.ThrowIfCancellationRequested();
                pipeline.CheckSidecar(path);
                var snapshot = pipeline.Read(path);
                if (!store && Directory.Exists(path))
                {
                    // Bordmittel-Paket ohne Speichern: als Export-JSON in einen eigenen Temp-Ordner, dann auswerten
                    using var temp = new PrivateTemp();
                    var file = SnapshotFile.Write(snapshot, temp.Path);
                    assessments.Add(pipeline.WithPlant(plant, file, snapshot, store: false, policy, path));
                }
                else
                {
                    assessments.Add(pipeline.WithPlant(plant, path, snapshot, store, policy, path));
                }
            }
        }

        if (a.Has("--brief")) CheckOutput.Brief(ctx, assessments);
        else if (format == "json") CheckOutput.Json(ctx, assessments, policy);
        else CheckOutput.Text(ctx, assessments, policy);
        return assessments.Count == 0 ? CommandApp.Ok : assessments.Max(x => x.Outcome.ExitCode);
    }

    /// <summary>Alarmart und Listen: <c>change</c> mit Anlage, <c>state</c> ohne; <c>--critical</c>/<c>--warning</c> ersetzen.</summary>
    internal static AlertPolicy Policy(ParsedArguments a, bool withPlant)
    {
        var mode = a.Choice("--alert", "change", "state") switch
        {
            "change" => AlertMode.Change,
            "state" => AlertMode.State,
            _ => withPlant ? AlertMode.Change : AlertMode.State,
        };
        var critical = a.Value("--critical") is { } c ? AlertTokens.ParseList("--critical", c) : null;
        var warning = a.Value("--warning") is { } w ? AlertTokens.ParseList("--warning", w) : null;
        return AlertPolicy.For(mode, critical, warning);
    }

    /// <summary>
    /// <c>check --plant</c> legt den Anlagenordner an, wenn er fehlt (spec 7.6) – nur wenn gespeichert wird; im
    /// Prüfermodus und mit <c>--no-store</c> muss er da sein.
    /// </summary>
    private static Plant OpenOrCreate(CommandContext ctx, string dir, bool store)
    {
        var folder = Path.GetFullPath(dir);
        if (!File.Exists(Path.Combine(folder, CommandContext.PlantFileName)))
        {
            if (!store) throw RglCodesCli.Error(RglCodesCli.PlantNotFound, folder);
            var name = new DirectoryInfo(folder).Name;
            ctx.Services.Plants.Create(folder, string.IsNullOrWhiteSpace(name) ? "plant" : name);
            ctx.Note(CliStrings.Cli_PlantCreated, folder);
        }
        return ctx.Services.Plants.Open(folder, ctx.ReadOnly);
    }
}
