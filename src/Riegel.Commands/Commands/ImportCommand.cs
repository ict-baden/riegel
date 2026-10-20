// SPDX-License-Identifier: MIT
using Riegel.Commands.Alerting;
using Riegel.Commands.Output;
using Riegel.Commands.Parsing;
using Riegel.Commands.Resources;
using Riegel.Core.Model;

namespace Riegel.Commands.Commands;

/// <summary>
/// <c>import &lt;file|folder&gt;… --plant &lt;dir&gt; [--collected-by-role &lt;r&gt;] [--witness-role &lt;r&gt;] [--record]</c>
/// (spec 7.7): Exporte oder Bordmittel-Pakete aufnehmen, Begleitdateien prüfen, Importnachweis schreiben, auswerten und
/// speichern; <c>--record</c> schreibt zusätzlich das Erhebungsprotokoll (O14). Exitcodes wie check (Alarmart
/// „change“, mit <c>--alert</c>/<c>--critical</c>/<c>--warning</c> änderbar). Exporte, die schon mit Lauf in der Anlage
/// liegen, werden übersprungen (wiederkehrender Import eines Ordners); <c>--reevaluate</c> wertet sie erneut aus.
/// </summary>
internal static class ImportCommand
{
    private static readonly OptionSpec[] Options =
    [
        new("--plant", OptionKind.Value),
        new("--collected-by-role", OptionKind.Value),
        new("--witness-role", OptionKind.Value),
        new("--record", OptionKind.Flag),
        new("--reevaluate", OptionKind.Flag),
        new("--alert", OptionKind.Value),
        new("--critical", OptionKind.Value),
        new("--warning", OptionKind.Value),
        new("--brief", OptionKind.Flag),
        new("--format", OptionKind.Value),
    ];

    public static int Run(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("import", args, Options, maxPositional: int.MaxValue);
        a.Exclusive("--brief", "--format");
        var format = a.Choice("--format", "text", "json") ?? "text";
        var plantDir = a.Required("--plant", "<folder>");
        if (a.Positional.Count == 0) throw RglCodesCli.Error(RglCodesCli.MissingOption, "import", "<file|folder>");
        var policy = CheckCommand.Policy(a, withPlant: true);
        var plant = ctx.OpenPlant(plantDir, forWrite: true, "import");

        // Erst alles prüfen, dann importieren: eine verfälschte Datei verhindert den ganzen Import (nichts halb aufgenommen)
        var pipeline = new Pipeline(ctx);
        var paths = Expand(a.Positional);
        foreach (var path in paths)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) throw RiegelException.Common(RglCodesCommon.NotFound, path);
            pipeline.CheckSidecar(path);
        }

        var record = ctx.Services.Plants.Import(plant, paths,
            new ImportOptions(a.Value("--collected-by-role"), a.Value("--witness-role"), plant.Settings.OperatorRole, Evaluate: true,
                Limits: ReadLimits.Default, SkipKnown: !a.Has("--reevaluate")));
        var skipped = Skipped(paths, record);
        var assessments = new List<RunAssessment>();
        foreach (var file in record.Files)
        {
            ctx.Cancel.ThrowIfCancellationRequested();
            var run = file.RunId is null ? null : pipeline.TryLoadRun(plant, file.RunId);
            if (run is null) throw RglCodesCli.Error(RglCodesCli.NoRuns, file.HostName);
            assessments.Add(pipeline.Assess(plant, run, null, policy, stored: true, file.FileName));
        }

        string? recordPdf = null;
        if (a.Has("--record") && record.Files.Count > 0)
        {
            var language = ctx.ReportLanguage(plant, null);
            var data = ctx.Services.ReportData.BuildCollectionRecord(plant, record.Id, language);
            recordPdf = ctx.Services.Pdf.WriteCollectionRecord(data, new PdfOptions(ctx.ReportFolder(plant, null)));
        }

        if (a.Has("--brief")) CheckOutput.Brief(ctx, assessments);
        else if (format == "json") CheckOutput.Json(ctx, assessments, policy, record);
        else
        {
            if (record.Files.Count > 0)
                ctx.Line(Plural.Text("Cli_ImportDone", record.Files.Count, record.Id, CommandContext.N(record.Files.Count), plant.Folder));
            foreach (var name in skipped) ctx.Line(CliStrings.Cli_ImportSkipped, name);
            foreach (var file in record.Files.Where(f => !f.SidecarOk))
                ctx.Line(CliStrings.Cli_ImportNoSidecar, file.FileName);
            ctx.Line();
            CheckOutput.Text(ctx, assessments, policy);
            if (recordPdf is not null) ctx.Written(recordPdf);
        }
        return assessments.Count == 0 ? CommandApp.Ok : assessments.Max(x => x.Outcome.ExitCode);
    }

    /// <summary>Namen der übersprungenen Exporte: angegebene Dateien, die nicht im Importnachweis stehen (Anzahl je Name).</summary>
    internal static List<string> Skipped(IEnumerable<string> paths, ImportRecord record)
    {
        var imported = record.Files.GroupBy(f => f.FileName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var skipped = new List<string>();
        foreach (var name in paths.Select(p => Path.GetFileName(Path.TrimEndingDirectorySeparator(p))))
        {
            if (imported.TryGetValue(name, out var n) && n > 0) imported[name] = n - 1;
            else skipped.Add(name);
        }
        return skipped;
    }

    /// <summary>
    /// Argumente zu Pfaden (Review docs-ui-1): ein Ordner, dessen Name auf .riegel-builtin endet oder der hostname.txt
    /// bzw. secedit.inf enthält, ist ein Bordmittel-Paket (wie in der Oberfläche); jeder andere Ordner steht für die
    /// Exporte darin (*.snapshot.json, *.snapshot.json.gz, nicht rekursiv, nach Namen sortiert) – so funktioniert
    /// <c>import D:\Exporte --plant …</c> mit dem Ordner von <c>collect --out</c>, und cmd braucht keine Platzhalter.
    /// </summary>
    internal static List<string> Expand(IEnumerable<string> arguments)
    {
        var paths = new List<string>();
        foreach (var argument in arguments)
        {
            var path = Path.GetFullPath(argument);
            if (!Directory.Exists(path) || IsBuiltinBundle(path))
            {
                paths.Add(path);
                continue;
            }
            var exports = Directory.EnumerateFiles(path)
                .Where(f => f.EndsWith(SnapshotFile.Extension, StringComparison.OrdinalIgnoreCase) || f.EndsWith(SnapshotFile.GzipExtension, StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .ToList();
            if (exports.Count == 0) throw RglCodesCli.Error(RglCodesCli.NoExportsInFolder, path);
            paths.AddRange(exports);
        }
        return paths;
    }

    /// <summary>Bordmittel-Paket statt Ordner mit Exporten.</summary>
    internal static bool IsBuiltinBundle(string folder) =>
        Path.TrimEndingDirectorySeparator(folder).EndsWith(".riegel-builtin", StringComparison.OrdinalIgnoreCase)
        || File.Exists(Path.Combine(folder, "hostname.txt")) || File.Exists(Path.Combine(folder, "secedit.inf"));
}
