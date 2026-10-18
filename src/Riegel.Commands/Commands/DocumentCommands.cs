// SPDX-License-Identifier: MIT
using Riegel.Commands.Parsing;
using Riegel.Commands.Resources;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Model.Reports;

namespace Riegel.Commands.Commands;

/// <summary>
/// Dokumente aus einem Anlagenordner (spec 5, 7.7): <c>report</c> (O2–O5, O7, O9), <c>summary</c> (O6),
/// <c>target-config</c> (O13) und <c>collection-record</c> (O14). Ohne <c>--out</c> landen sie unter
/// <c>reports/&lt;Zeitstempel&gt;/</c> im Anlagenordner, im Prüfermodus nur unter <c>--out</c>.
/// </summary>
internal static class DocumentCommands
{
    private static readonly string[] FormatNames = ["pdf", "xlsx", "csv", "json"];

    private static readonly OptionSpec[] ReportOptions =
    [
        new("--plant", OptionKind.Value),
        new("--machine", OptionKind.List, SplitCommas: true),
        new("--format", OptionKind.Value),
        new("--per-machine", OptionKind.Flag),
        new("--no-appendix", OptionKind.Flag),
        new("--preset", OptionKind.Value),
        new("--evidence", OptionKind.Flag),
        new("--include-baseline-content", OptionKind.Flag),
        new("--pdfa", OptionKind.Flag),
        new("--language", OptionKind.Value),
        new("--out", OptionKind.Value),
    ];

    /// <summary><c>report --plant &lt;dir&gt; [--machine &lt;host&gt;…] [--format pdf,xlsx,csv,json] …</c></summary>
    public static int Report(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("report", args, ReportOptions);
        var plant = ctx.OpenPlant(a.Required("--plant", "<folder>"), forWrite: false, "report");
        var formats = Formats(a.Value("--format"));
        if (a.Has("--include-baseline-content") && !a.Has("--evidence"))
            throw RglCodesCli.Error(RglCodesCli.MissingOption, "report --include-baseline-content", "--evidence");
        if (a.Has("--pdfa") && !formats.Contains(ReportFormat.Pdf))
            throw RglCodesCli.Error(RglCodesCli.MissingOption, "report --pdfa", "--format pdf");
        var preset = a.Choice("--preset", "audit", "as-delivered") switch
        {
            "audit" => ReportPreset.Audit,
            "as-delivered" => ReportPreset.AsDelivered,
            _ => plant.Settings.ReportPreset,
        };
        var hosts = a.Values("--machine") is { Count: > 0 } list ? list : null;
        var language = ctx.ReportLanguage(plant, a.Value("--language"));
        var outDir = ctx.ReportFolder(plant, a.Value("--out"));
        var date = ctx.Services.Today;
        bool perMachine = a.Has("--per-machine"), appendix = !a.Has("--no-appendix");

        var data = ctx.Services.ReportData.Build(new ReportRequest(plant, hosts, date, language, preset, perMachine, appendix,
            formats, a.Has("--evidence"), a.Has("--include-baseline-content"), a.Has("--pdfa"), outDir));
        var documents = new List<string>();
        if (formats.Contains(ReportFormat.Pdf))
        {
            documents.Add(ctx.Services.Pdf.WriteReport(data, new PdfOptions(outDir, a.Has("--pdfa"), perMachine, appendix)));
            if (perMachine)
                documents.AddRange(data.Machines
                    .Select(m => Path.Combine(outDir, ReportFileNames.MachineReport(plant.Name, date, m.HostName)))
                    .Where(File.Exists));
        }
        if (formats.Contains(ReportFormat.Xlsx)) documents.Add(ctx.Services.Tabular.WriteWorkbook(data, outDir));
        if (formats.Contains(ReportFormat.Csv)) documents.Add(ctx.Services.Tabular.WriteCsv(data, outDir));
        if (formats.Contains(ReportFormat.Json))
        {
            // O2: die verwendeten Läufe, Byte für Byte wie im Anlagenordner (RiegelJson ist deterministisch)
            foreach (var machine in data.Machines)
            {
                var run = ctx.Services.Plants.LoadRun(plant, machine.RunId);
                var path = Path.Combine(outDir, ReportFileNames.RunFile(run.HostName, run.Id).Replace('/', Path.DirectorySeparatorChar));
                RiegelJson.WriteFile(path, run);
                documents.Add(path);
            }
        }
        foreach (var document in documents) ctx.Written(document);

        if (a.Has("--evidence"))
        {
            if (a.Has("--include-baseline-content")) ctx.Note(CliStrings.Cli_BaselineContentWarning);
            var zip = ctx.Services.Evidence.Write(new EvidenceRequest(plant, hosts, date, documents, a.Has("--include-baseline-content"),
                plant.Settings.OperatorRole, language, outDir));
            ctx.Written(zip);
        }
        return CommandApp.Ok;
    }

    private static readonly OptionSpec[] SummaryOptions =
    [
        new("--plant", OptionKind.List),
        new("--out", OptionKind.Value),
        new("--language", OptionKind.Value),
    ];

    /// <summary>
    /// <c>summary --plant &lt;dir&gt; [--plant &lt;dir&gt;…] [--out &lt;dir&gt;] [--language &lt;code&gt;]</c>: eine Seite über
    /// mehrere Anlagen (O6). Ohne <c>--out</c> in den aktuellen Ordner – das Blatt gehört zu keiner einzelnen Anlage.
    /// </summary>
    public static int Summary(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("summary", args, SummaryOptions);
        var plants = a.RequiredList("--plant", "<folder>").Select(d => ctx.OpenPlant(d, forWrite: false, "summary")).ToList();
        var language = ctx.ReportLanguage(plants[0], a.Value("--language"));
        var outDir = Path.GetFullPath(a.Value("--out") ?? Directory.GetCurrentDirectory());
        var data = ctx.Services.ReportData.BuildSummary(plants, ctx.Services.Today, language);
        ctx.Written(ctx.Services.Pdf.WriteSummary(data, new PdfOptions(outDir)));
        return CommandApp.Ok;
    }

    private static readonly OptionSpec[] TargetConfigOptions =
    [
        new("--plant", OptionKind.Value),
        new("--group", OptionKind.Value),
        new("--out", OptionKind.Value),
        new("--language", OptionKind.Value),
    ];

    /// <summary><c>target-config --plant &lt;dir&gt; [--group &lt;id&gt;] [--out &lt;dir&gt;]</c>: Soll-Konfiguration als PDF und Excel (O13).</summary>
    public static int TargetConfig(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("target-config", args, TargetConfigOptions);
        var plant = ctx.OpenPlant(a.Required("--plant", "<folder>"), forWrite: false, "target-config");
        var group = a.Value("--group");
        if (group is not null && !plant.Groups.Any(g => string.Equals(g.Id, group, StringComparison.OrdinalIgnoreCase)))
            throw RglCodesCli.Error(RglCodesCli.InvalidValue, "--group", group,
                plant.Groups.Count == 0 ? "–" : string.Join(", ", plant.Groups.Select(g => g.Id)));
        var language = ctx.ReportLanguage(plant, a.Value("--language"));
        var outDir = ctx.ReportFolder(plant, a.Value("--out"));
        var data = ctx.Services.ReportData.BuildTargetConfig(plant, group, language);
        ctx.Written(ctx.Services.Pdf.WriteTargetConfig(data, new PdfOptions(outDir)));
        ctx.Written(ctx.Services.Tabular.WriteTargetConfigWorkbook(data, outDir));
        return CommandApp.Ok;
    }

    private static readonly OptionSpec[] RecordOptions =
    [
        new("--plant", OptionKind.Value),
        new("--import", OptionKind.Value),
        new("--latest", OptionKind.Flag),
        new("--out", OptionKind.Value),
        new("--language", OptionKind.Value),
    ];

    /// <summary><c>collection-record --plant &lt;dir&gt; [--import &lt;id&gt;|--latest] [--out &lt;dir&gt;]</c>: Erhebungsprotokoll (O14).</summary>
    public static int CollectionRecord(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("collection-record", args, RecordOptions);
        a.Exclusive("--import", "--latest");
        var plant = ctx.OpenPlant(a.Required("--plant", "<folder>"), forWrite: false, "collection-record");
        var importId = a.Value("--import") is { } id ? CheckImport(plant, id) : LatestImport(plant);
        var language = ctx.ReportLanguage(plant, a.Value("--language"));
        var outDir = ctx.ReportFolder(plant, a.Value("--out"));
        var data = ctx.Services.ReportData.BuildCollectionRecord(plant, importId, language);
        ctx.Written(ctx.Services.Pdf.WriteCollectionRecord(data, new PdfOptions(outDir)));
        return CommandApp.Ok;
    }

    /// <summary>Formatliste „pdf,xlsx“; ohne Angabe PDF und Excel (spec 5).</summary>
    internal static IReadOnlyList<ReportFormat> Formats(string? value)
    {
        if (value is null) return [ReportFormat.Pdf, ReportFormat.Xlsx];
        var result = new List<ReportFormat>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var format = part.ToLowerInvariant() switch
            {
                "pdf" => ReportFormat.Pdf,
                "xlsx" => ReportFormat.Xlsx,
                "csv" => ReportFormat.Csv,
                "json" => ReportFormat.Json,
                _ => throw RglCodesCli.Error(RglCodesCli.InvalidValue, "--format", part, string.Join(", ", FormatNames)),
            };
            if (!result.Contains(format)) result.Add(format);
        }
        return result.Count > 0 ? result : throw RglCodesCli.Error(RglCodesCli.InvalidValue, "--format", value, string.Join(", ", FormatNames));
    }

    /// <summary>Neuester Importnachweis (imports/&lt;ts&gt;-&lt;6hex&gt;.import.json; der Name beginnt mit dem Zeitstempel).</summary>
    internal static string LatestImport(Plant plant)
    {
        var folder = Path.Combine(plant.Folder ?? "", "imports");
        var latest = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.import.json").Select(Path.GetFileName).Order(StringComparer.Ordinal).LastOrDefault()
            : null;
        return latest is null
            ? throw RglCodesCli.Error(RglCodesCli.NoImports, "imports/")
            : latest[..^".import.json".Length];
    }

    private static string CheckImport(Plant plant, string id)
    {
        // Nur Zeichen einer Import-ID, damit kein Pfad aus der Angabe entsteht
        if (id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            throw RglCodesCli.Error(RglCodesCli.InvalidValue, "--import", id, "<yyyyMMddTHHmmssZ>-<6 hex>");
        var file = Path.Combine(plant.Folder ?? "", "imports", id + ".import.json");
        return File.Exists(file) ? id : throw RglCodesCli.Error(RglCodesCli.NoImports, id);
    }
}
