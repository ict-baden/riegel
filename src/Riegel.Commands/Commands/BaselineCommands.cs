// SPDX-License-Identifier: MIT
using Riegel.Commands.Output;
using Riegel.Commands.Parsing;
using Riegel.Commands.Resources;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;

namespace Riegel.Commands.Commands;

/// <summary>
/// <c>baseline import &lt;path&gt; --plant &lt;dir&gt; --name &lt;name&gt;</c>, <c>baseline list --plant &lt;dir&gt;</c>,
/// <c>baseline remove &lt;id&gt; --plant &lt;dir&gt;</c> (spec 2.15, 7.7). Den Import (GPO-Sicherung, JSON, CSV) macht der
/// Baseline-Dienst; die Ablage unter <c>baselines/&lt;id&gt;.json</c> übernimmt <see cref="PlantBaselines"/>.
/// </summary>
internal static class BaselineCommands
{
    private static readonly OptionSpec Plant = new("--plant", OptionKind.Value);

    public static int Run(CommandContext ctx, IReadOnlyList<string> args) =>
        RegisterCommands.Dispatch(ctx, "baseline", args, new Dictionary<string, Func<CommandContext, IReadOnlyList<string>, int>>
        {
            ["import"] = Import,
            ["list"] = List,
            ["remove"] = Remove,
        });

    private static int Import(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("baseline import", args, [Plant, new("--name", OptionKind.Value)], maxPositional: 1);
        if (a.Positional.Count == 0) throw RglCodesCli.Error(RglCodesCli.MissingOption, "baseline import", "<path>");
        var source = Path.GetFullPath(a.Positional[0]);
        if (!File.Exists(source) && !Directory.Exists(source)) throw RiegelException.Common(RglCodesCommon.NotFound, source);
        var name = ApproveCommand.NotBlank(a, "--name", "<name>");
        var plant = ctx.OpenPlant(a.Required("--plant", "<folder>"), forWrite: true, "baseline import");
        var imported = ctx.Services.Baselines.Import(source, name, ReadLimits.Default);
        var path = PlantBaselines.Save(plant, imported, name);
        ctx.Line(CliStrings.Cli_BaselineImported, imported.Baseline.Id, name, CommandContext.N(imported.Rules.Count), imported.Baseline.SourceSha256);
        ctx.Written(path);
        ctx.Note(CliStrings.Cli_BaselineLicenceNote);
        return CommandApp.Ok;
    }

    private static int List(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("baseline list", args, [Plant, new("--format", OptionKind.Value)]);
        var format = a.Choice("--format", "text", "json") ?? "text";
        var plant = ctx.OpenPlant(a.Required("--plant", "<folder>"), forWrite: false, "baseline list");
        var files = PlantBaselines.List(plant);
        if (format == "json")
        {
            JsonOutput.Write(ctx.Out, w =>
            {
                w.WriteStartObject();
                w.WriteString("schema", "riegel.cli.baselines/1");
                w.WriteStartArray("baselines");
                foreach (var file in files)
                {
                    w.WriteStartObject();
                    w.WriteString("id", file.Id);
                    w.WriteString("name", file.Name);
                    w.WriteNumber("rules", file.Rules.Count);
                    JsonOutput.StringOrNull(w, "sourceFileName", file.SourceFileName);
                    JsonOutput.StringOrNull(w, "sourceSha256", file.SourceSha256);
                    if (file.ImportedAtUtc is { } at) w.WriteString("importedAtUtc", UtcDateTimeOffsetConverter.Format(at));
                    else w.WriteNull("importedAtUtc");
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            });
            return CommandApp.Ok;
        }
        if (files.Count == 0) ctx.Line(CliStrings.Cli_BaselineNone);
        foreach (var file in files)
            ctx.Line(CliStrings.Cli_BaselineLine, file.Id, file.Name, CommandContext.N(file.Rules.Count),
                file.ImportedAtUtc is { } at ? CommandContext.T(at) : "–", file.SourceSha256 ?? "–");
        return CommandApp.Ok;
    }

    private static int Remove(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("baseline remove", args, [Plant], maxPositional: 1);
        if (a.Positional.Count == 0) throw RglCodesCli.Error(RglCodesCli.MissingOption, "baseline remove", "<id>");
        var id = a.Positional[0];
        var plant = ctx.OpenPlant(a.Required("--plant", "<folder>"), forWrite: true, "baseline remove");
        PlantBaselines.Remove(plant, id);
        ctx.Line(CliStrings.Cli_BaselineRemoved, id);
        return CommandApp.Ok;
    }
}

/// <summary>
/// Kunden-Baselines im Anlagenordner: <c>baselines/&lt;id&gt;.json</c> im Format <c>riegel.baseline/1</c> mit Quelle
/// und Importzeit (spec 7.6, docs/file-formats.md). Der Vertrag kennt dafür noch keine Methode im Anlagenspeicher
/// (Integrationsanfrage); bis dahin liest und schreibt die Befehlszeile die Dateien selbst, atomar über RiegelJson.
/// </summary>
internal static class PlantBaselines
{
    public static string Folder(Plant plant) => Path.Combine(plant.Folder ?? throw RglCodesCli.Error(RglCodesCli.PlantNotFound, plant.Id), "baselines");

    /// <summary>Gültige Baseline-ID: [a-z0-9-], 1–63 Zeichen (Teil der Regel-IDs IMP-&lt;id&gt;-…, spec 2.15).</summary>
    public static bool IsValidId(string id) =>
        id.Length is >= 1 and <= 63 && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    /// <summary>Speichert eine importierte Baseline; gibt es die ID schon: RGL-0016 (erst entfernen).</summary>
    public static string Save(Plant plant, ImportedBaseline imported, string name)
    {
        var reference = imported.Baseline;
        if (!IsValidId(reference.Id)) throw RglCodesCli.Error(RglCodesCli.InvalidValue, "id", reference.Id, "[a-z0-9-]");
        var path = Path.Combine(Folder(plant), reference.Id + ".json");
        if (File.Exists(path)) throw RiegelException.Common(RglCodesCommon.AlreadyExists, path);
        var prefix = "IMP-" + reference.Id + "-";
        var rules = imported.Rules.Select(r => new BaselineRule(
                r.Id.StartsWith(prefix, StringComparison.Ordinal) ? r.Id[prefix.Length..] : r.Id,
                r.Title.Get("en"), r.Severity, r.Category, r.Check,
                r.Rationale.Get("en") is { Length: > 0 } rationale ? rationale : null,
                r.References.FirstOrDefault()?.Doc))
            .ToList();
        var file = new BaselineFile(SchemaIds.Baseline, reference.Id, name, null, reference.SourceFileName, reference.SourceSha256,
            reference.ImportedAtUtc, rules);
        RiegelJson.WriteFile(path, file);
        return path;
    }

    /// <summary>Alle gespeicherten Baselines, nach ID sortiert. Kaputte Dateien sind ein Fehler (RGL-0004).</summary>
    public static IReadOnlyList<BaselineFile> List(Plant plant)
    {
        var folder = Folder(plant);
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder, "*.json")
            .Order(StringComparer.Ordinal)
            .Select(f => RiegelJson.ReadFile<BaselineFile>(f, ReadLimits.Default.MaxBaselineCsvBytes))
            .OrderBy(b => b.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Entfernt baselines/&lt;id&gt;.json; unbekannte ID: RGL-0464.</summary>
    public static void Remove(Plant plant, string id)
    {
        if (!IsValidId(id)) throw RglCodesCli.Error(RglCodesCli.BaselineNotFound, id);
        var path = Path.Combine(Folder(plant), id + ".json");
        if (!File.Exists(path)) throw RglCodesCli.Error(RglCodesCli.BaselineNotFound, id);
        File.Delete(path);
    }

    /// <summary>
    /// Eigener Katalog plus die Baselines der Anlage (für Prüfungen der Register und Regel-IDs von Maßnahmen). Die
    /// gespeicherten Dateien sind selbst riegel.baseline/1 und gehen deshalb durch denselben Import.
    /// </summary>
    public static ICatalogue Catalogue(CommandContext ctx, Plant plant)
    {
        var core = ctx.Services.Catalogue.LoadEmbedded();
        var folder = Folder(plant);
        if (!Directory.Exists(folder)) return core;
        var baselines = Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal)
            .Select(f => ctx.Services.Baselines.Import(f, Path.GetFileNameWithoutExtension(f), ReadLimits.Default))
            .ToList();
        return baselines.Count == 0 ? core : ctx.Services.Catalogue.Combine(core, baselines);
    }
}
