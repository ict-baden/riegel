// SPDX-License-Identifier: MIT
using Riegel.Commands.Output;
using Riegel.Commands.Parsing;
using Riegel.Commands.Resources;
using Riegel.Core.Model;

namespace Riegel.Commands.Commands;

/// <summary>
/// Übrige Befehle (spec 7.7): <c>remediate</c> (O10, nur Dateien), <c>rules</c>, <c>collector export</c> (O15, Windows)
/// und <c>plant init</c>.
/// </summary>
internal static class MiscCommands
{
    private static readonly OptionSpec[] RemediateOptions =
    [
        new("--plant", OptionKind.Value),
        new("--machine", OptionKind.Value),
        new("--run", OptionKind.Value),
        new("--out", OptionKind.Value),
        new("--language", OptionKind.Value),
    ];

    /// <summary>
    /// <c>remediate --plant &lt;dir&gt; --machine &lt;host&gt; [--run &lt;id&gt;] --out &lt;dir&gt; [--language &lt;code&gt;]</c>:
    /// Vorschlag als Dateien unter <c>&lt;out&gt;/remediation/&lt;host&gt;/&lt;runId&gt;/</c>; ohne <c>--out</c> im Anlagenordner.
    /// Riegel führt nichts davon aus.
    /// </summary>
    public static int Remediate(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("remediate", args, RemediateOptions);
        var plant = ctx.OpenPlant(a.Required("--plant", "<folder>"), forWrite: false, "remediate");
        var host = a.Required("--machine", "<host>");
        PlantHosts.Require(plant, [host], "--machine");
        var run = a.Value("--run") is { } id ? CompareCommands.RunOf(ctx, plant, host, id) : CompareCommands.Latest(ctx, plant, host);
        // Die Rücknahme braucht genau den Export dieses Laufs: nur aus dem Anlagenordner und mit passendem SHA-256
        var snapshot = new Pipeline(ctx).SnapshotOf(plant, run);
        string outDir;
        if (a.Value("--out") is { } given) outDir = Path.GetFullPath(given);
        else if (ctx.ReadOnly || plant.ReadOnly) throw RglCodesCli.Error(RglCodesCli.ReadOnlyNeedsOut);
        else outDir = plant.Folder!;
        var language = ctx.ReportLanguage(plant, a.Value("--language"));
        var catalogue = PlantBaselines.Catalogue(ctx, plant);
        var registers = ctx.Services.Registers.Load(plant);
        var result = ctx.Services.Remediation.Write(run, snapshot, catalogue, registers, outDir, language);
        ctx.Line(CliStrings.Cli_RemediationWritten, result.Directory,
            Plural.Text("Cli_CountRulesInFiles", result.IncludedRuleIds.Count, CommandContext.N(result.IncludedRuleIds.Count)),
            Plural.Text("Cli_CountLeftOut", result.Excluded.Count, CommandContext.N(result.Excluded.Count)),
            Plural.Text("Cli_CountManualSteps", result.ManualRuleIds.Count, CommandContext.N(result.ManualRuleIds.Count)));
        foreach (var file in result.Files) ctx.Written(file);
        ctx.Line(CliStrings.Cli_RemediationNote);
        return CommandApp.Ok;
    }

    private static readonly OptionSpec[] RulesOptions = [new("--id", OptionKind.Value), new("--format", OptionKind.Value)];

    /// <summary><c>rules [--id &lt;ID&gt;] [--format text|json|md]</c>: der eingebettete Katalog.</summary>
    public static int Rules(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("rules", args, RulesOptions);
        var format = a.Choice("--format", "text", "json", "md") ?? "text";
        var catalogue = ctx.Services.Catalogue.LoadEmbedded();
        IReadOnlyList<Rule> rules = catalogue.Rules;
        if (a.Value("--id") is { } id)
        {
            var rule = catalogue.Find(id) ?? catalogue.Rules.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase))
                       ?? throw RglCodesCli.Error(RglCodesCli.UnknownRule, id);
            rules = [rule];
        }
        var lang = ctx.Language;
        switch (format)
        {
            case "json":
                JsonOutput.Write(ctx.Out, w =>
                {
                    w.WriteStartObject();
                    w.WriteString("schema", "riegel.cli.rules/1");
                    w.WriteString("catalogue", catalogue.Id);
                    w.WriteString("version", catalogue.Version);
                    w.WriteString("sha256", catalogue.Sha256);
                    w.WriteStartArray("rules");
                    foreach (var rule in rules) JsonOutput.ContractValue(w, rule);
                    w.WriteEndArray();
                    w.WriteEndObject();
                });
                break;
            case "md":
                ctx.Line(CliStrings.Rules_MdHeader);
                ctx.Line("|---|---|---|---|");
                foreach (var rule in rules)
                    ctx.Line($"| {rule.Id} | {CommandContext.SeverityName(rule.Severity)} | {CommandContext.CategoryName(rule.Category)} | {Md(rule.Title.Get(lang))} |");
                break;
            default:
                if (rules.Count == 1 && a.Has("--id")) Detail(ctx, rules[0], lang);
                else
                {
                    ctx.Line(CliStrings.Cli_RulesHeader, catalogue.Id, catalogue.Version, CommandContext.N(rules.Count));
                    foreach (var rule in rules)
                        ctx.Line(CliStrings.Cli_RuleLine, rule.Id, CommandContext.SeverityName(rule.Severity), CommandContext.CategoryName(rule.Category),
                            rule.Title.Get(lang));
                }
                break;
        }
        return CommandApp.Ok;
    }

    private static void Detail(CommandContext ctx, Rule rule, string lang)
    {
        ctx.Line($"{rule.Id} – {rule.Title.Get(lang)}");
        ctx.Line(CliStrings.Cli_RuleMeta, CommandContext.SeverityName(rule.Severity), CommandContext.CategoryName(rule.Category), rule.Check.Type,
            rule.Since, rule.State == RuleState.Retired ? CliStrings.Rule_Retired : CliStrings.Rule_Active);
        ctx.Line();
        ctx.Line(rule.Rationale.Get(lang));
        if (rule.OtNote is { } ot) { ctx.Line(); ctx.Line(CliStrings.Cli_RuleOtNote, ot.Get(lang)); }
        if (rule.Remedy is { } remedy) { ctx.Line(); ctx.Line(CliStrings.Cli_RuleRemedy, remedy.Get(lang)); }
        if (rule.NeedsAdmin) ctx.Line(CliStrings.Cli_RuleNeedsAdmin);
        if (rule.References.Count > 0)
        {
            ctx.Line();
            ctx.Line(CliStrings.Cli_RuleReferences);
            foreach (var r in rule.References)
                ctx.Line("  " + string.Join(" · ", new[] { r.Src, r.Doc, r.Section, r.Setting, r.Id, r.Url }.Where(x => !string.IsNullOrEmpty(x))));
        }
    }

    /// <summary>„|“ in Markdown-Tabellen maskieren.</summary>
    private static string Md(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");

    /// <summary><c>collector export &lt;dir&gt;</c> (Windows): Sammler-Paket mit Übergabezettel (O15).</summary>
    public static int Collector(CommandContext ctx, IReadOnlyList<string> args) =>
        RegisterCommands.Dispatch(ctx, "collector", args, new Dictionary<string, Func<CommandContext, IReadOnlyList<string>, int>>
        {
            ["export"] = (c, rest) =>
            {
                var a = ArgumentParser.Parse("collector export", rest, [], maxPositional: 1);
                if (a.Positional.Count == 0) throw RglCodesCli.Error(RglCodesCli.MissingOption, "collector export", "<folder>");
                if (!c.Services.Platform.CanCollect)
                    throw RglCodesCli.Error(RglCodesCli.WindowsOnly, "collector export", CommandContext.ProgramName + " check --snapshot <file>");
                var target = c.Services.Platform.ExportCollectorBundle(Path.GetFullPath(a.Positional[0]));
                c.Line(CliStrings.Cli_CollectorExported, target);
                return CommandApp.Ok;
            },
        });

    /// <summary><c>plant init &lt;dir&gt; --name &lt;name&gt;</c>: Anlagenordner anlegen.</summary>
    public static int Plant(CommandContext ctx, IReadOnlyList<string> args) =>
        RegisterCommands.Dispatch(ctx, "plant", args, new Dictionary<string, Func<CommandContext, IReadOnlyList<string>, int>>
        {
            ["init"] = (c, rest) =>
            {
                var a = ArgumentParser.Parse("plant init", rest, [new("--name", OptionKind.Value)], maxPositional: 1);
                if (a.Positional.Count == 0) throw RglCodesCli.Error(RglCodesCli.MissingOption, "plant init", "<folder>");
                var name = ApproveCommand.NotBlank(a, "--name", "<name>");
                if (c.ReadOnly) throw RglCodesCli.Error(RglCodesCli.ReadOnlyRefused, "plant init");
                var plant = c.Services.Plants.Create(Path.GetFullPath(a.Positional[0]), name);
                c.Line(CliStrings.Cli_PlantInitialized, plant.Name, plant.Id, plant.Folder);
                return CommandApp.Ok;
            },
        });
}
