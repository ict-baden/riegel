// SPDX-License-Identifier: MIT
using Riegel.Commands.Output;
using Riegel.Commands.Parsing;
using Riegel.Commands.Resources;
using Riegel.Core.Model;

namespace Riegel.Commands.Commands;

/// <summary>
/// Vergleiche und Nachprüfung (spec 2.14, 7.7): <c>compare</c> (Statusänderungen und Abweichungen eines Rechners),
/// <c>compare-snapshot</c> (Stichprobe des Prüfers, 0 konsistent / 1 abweichend / 3 Fehler) und <c>verify</c>
/// (Nachweispaket nachrechnen, 0 reproduziert / 2 abweichend / 3 Fehler).
/// </summary>
internal static class CompareCommands
{
    /// <summary>Höchstzahl Zeilen je Liste im Bildschirmtext; alles steht in JSON.</summary>
    private const int MaxLines = 50;

    private static readonly OptionSpec[] CompareOptions =
    [
        new("--plant", OptionKind.Value),
        new("--machine", OptionKind.Value),
        new("--against", OptionKind.Value),
        new("--format", OptionKind.Value),
    ];

    /// <summary><c>compare --plant &lt;dir&gt; --machine &lt;host&gt; [--against previous|approved] [--format text|json]</c></summary>
    public static int Compare(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("compare", args, CompareOptions);
        var plant = ctx.OpenPlant(a.Required("--plant", "<folder>"), forWrite: false, "compare");
        var host = a.Required("--machine", "<host>");
        var basis = a.Choice("--against", "previous", "approved") == "approved" ? ComparisonBasis.Approved : ComparisonBasis.Previous;
        var format = a.Choice("--format", "text", "json") ?? "text";
        var pipeline = new Pipeline(ctx);

        var to = Latest(ctx, plant, host);
        // Ziel des Vergleichs: geprüfter Export (RGL-0415/0416/0002); die Basis darf fehlen (dann ohne Rohwerte)
        var toSnapshot = pipeline.SnapshotOf(plant, to);
        Run? from;
        if (basis == ComparisonBasis.Approved)
        {
            var approved = ctx.Services.Plants.LoadApproved(plant, host) ?? throw RglCodesCli.Error(RglCodesCli.NoApprovedState, host);
            from = ctx.Services.Plants.LoadRun(plant, approved.RunId);
        }
        else
        {
            from = pipeline.TryLoadRun(plant, to.PreviousRunId);
        }

        RunComparison? comparison = from is null ? null
            : ctx.Services.Comparison.Compare(from, pipeline.TrySnapshotOf(plant, from), to, toSnapshot, basis);
        if (format == "json")
        {
            JsonOutput.Write(ctx.Out, w =>
            {
                w.WriteStartObject();
                w.WriteString("schema", "riegel.cli.compare/1");
                w.WriteString("host", to.HostName);
                w.WriteString("against", basis == ComparisonBasis.Approved ? "approved" : "previous");
                w.WriteString("toRunId", to.Id);
                JsonOutput.StringOrNull(w, "fromRunId", from?.Id);
                JsonOutput.Contract(w, "comparison", comparison);
                w.WriteEndObject();
            });
            return CommandApp.Ok;
        }

        if (comparison is null || from is null)
        {
            ctx.Line(CliStrings.Cli_CompareNoPrevious, to.HostName, to.Id);
            return CommandApp.Ok;
        }
        ctx.Line(basis == ComparisonBasis.Approved ? CliStrings.Cli_CompareHeaderApproved : CliStrings.Cli_CompareHeaderPrevious,
            to.HostName, from.Id, to.Id);
        if (comparison.MachineIdentityChanged) ctx.Line(CliStrings.Cli_CompareIdentityChanged);
        ctx.Line(CliStrings.Cli_CompareChanges, CommandContext.N(comparison.StatusChanges.Count));
        foreach (var change in comparison.StatusChanges.Take(MaxLines))
            ctx.Line(CliStrings.Cli_CompareChangeLine, change.RuleId, ChangeName(change.Kind),
                change.From is { } f ? CommandContext.StatusName(f) : "–", change.To is { } t ? CommandContext.StatusName(t) : "–");
        if (comparison.StatusChanges.Count > MaxLines) ctx.Line(CliStrings.Cli_AndMore, comparison.StatusChanges.Count - MaxLines);
        ctx.Line(CliStrings.Cli_CompareDrift, CommandContext.N(comparison.Drift.Count));
        Drift(ctx, comparison.Drift);
        return CommandApp.Ok;
    }

    private static readonly OptionSpec[] SampleOptions =
    [
        new("--evidence", OptionKind.Value),
        new("--host", OptionKind.Value),
        new("--format", OptionKind.Value),
    ];

    /// <summary><c>compare-snapshot &lt;fresh-snapshot&gt; --evidence &lt;zip&gt; [--host &lt;h&gt;] [--format text|json]</c></summary>
    public static int CompareSnapshot(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("compare-snapshot", args, SampleOptions, maxPositional: 1);
        if (a.Positional.Count == 0) throw RglCodesCli.Error(RglCodesCli.MissingOption, "compare-snapshot", "<fresh-snapshot>");
        var fresh = Path.GetFullPath(a.Positional[0]);
        var zip = Path.GetFullPath(a.Required("--evidence", "<zip>"));
        var format = a.Choice("--format", "text", "json") ?? "text";
        if (!File.Exists(fresh)) throw RiegelException.Common(RglCodesCommon.NotFound, fresh);
        if (!File.Exists(zip)) throw RiegelException.Common(RglCodesCommon.NotFound, zip);
        new Pipeline(ctx).CheckSidecar(fresh);

        var result = ctx.Services.Evidence.CompareSample(fresh, zip, a.Value("--host"), ReadLimits.Default);
        if (format == "json")
        {
            JsonOutput.Write(ctx.Out, w =>
            {
                w.WriteStartObject();
                w.WriteString("schema", "riegel.cli.compare-snapshot/1");
                w.WriteNumber("exitCode", result.ExitCode);
                JsonOutput.Contract(w, "result", result);
                w.WriteEndObject();
            });
        }
        else
        {
            switch (result.Status)
            {
                case SampleStatus.Consistent:
                    ctx.Line(CliStrings.Cli_SampleConsistent, result.Comparison?.HostName ?? "–");
                    break;
                case SampleStatus.Different:
                    ctx.Line(CliStrings.Cli_SampleDifferent, result.Comparison?.HostName ?? "–", CommandContext.N(result.Comparison?.Differences.Count ?? 0));
                    Drift(ctx, result.Comparison?.Differences ?? []);
                    break;
                default:
                    ctx.Err.WriteLine($"{result.ErrorCode ?? RglCodesCommon.Unexpected}: {result.Message}");
                    break;
            }
            if (result.Comparison is { } c)
                ctx.Line(CliStrings.Cli_SampleHashes, c.PackedSnapshotSha256, CommandContext.T(c.PackedCollectedAtUtc), c.FreshSnapshotSha256,
                    CommandContext.T(c.FreshCollectedAtUtc));
        }
        return result.ExitCode;
    }

    private static readonly OptionSpec[] VerifyOptions = [new("--format", OptionKind.Value)];

    /// <summary><c>verify &lt;evidence.zip&gt; [--format text|json]</c></summary>
    public static int Verify(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("verify", args, VerifyOptions, maxPositional: 1);
        if (a.Positional.Count == 0) throw RglCodesCli.Error(RglCodesCli.MissingOption, "verify", "<evidence.zip>");
        var zip = Path.GetFullPath(a.Positional[0]);
        var format = a.Choice("--format", "text", "json") ?? "text";
        if (!File.Exists(zip)) throw RiegelException.Common(RglCodesCommon.NotFound, zip);

        var result = ctx.Services.Evidence.Verify(zip, ReadLimits.Default);
        if (format == "json")
        {
            JsonOutput.Write(ctx.Out, w =>
            {
                w.WriteStartObject();
                w.WriteString("schema", "riegel.cli.verify/1");
                w.WriteNumber("exitCode", result.ExitCode);
                JsonOutput.Contract(w, "result", result);
                w.WriteEndObject();
            });
            return result.ExitCode;
        }

        var packed = result.PackedEngineVersion ?? "–";
        switch (result.Status)
        {
            case VerifyStatus.Reproduced:
                ctx.Line(CliStrings.Cli_VerifyReproduced, result.EngineVersion, packed);
                break;
            case VerifyStatus.Mismatch:
                ctx.Line(Plural.Text("Cli_VerifyMismatch", result.Findings.Count, result.EngineVersion, packed, CommandContext.N(result.Findings.Count)));
                break;
            default:
                ctx.Line(CliStrings.Cli_VerifyError);
                break;
        }
        ctx.Line(CliStrings.Cli_VerifyCounts, CommandContext.N(result.SnapshotsChecked), CommandContext.N(result.ResultsCompared));
        if (result.Catalogue is { } catalogue)
        {
            // Welches Regelwerk die erneute Auswertung nahm – nur „identisch mit dem eingebauten“ beruht nicht allein auf dem Paket
            if (result.CatalogueBuiltIn) ctx.Line(CliStrings.Cli_VerifyCatalogueBuiltIn, catalogue.Id, catalogue.Version, catalogue.Sha256);
            else
            {
                var builtIn = ctx.Services.Catalogue.LoadEmbedded();
                ctx.Line(CliStrings.Cli_VerifyCatalogueOther, catalogue.Id, catalogue.Version, catalogue.Sha256, builtIn.Id + " " + builtIn.Version);
            }
        }
        foreach (var finding in result.Findings.Take(MaxLines))
        {
            var where = string.Join(" · ", new[] { finding.File, finding.HostName, finding.RuleId }.Where(x => !string.IsNullOrEmpty(x)));
            var line = $"  {finding.Code}: {finding.Message}" + (where.Length > 0 ? $" ({where})" : "");
            if (result.Status == VerifyStatus.Error) ctx.Err.WriteLine(line);
            else ctx.Line(line);
        }
        if (result.Findings.Count > MaxLines) ctx.Line(CliStrings.Cli_AndMore, result.Findings.Count - MaxLines);
        if (result.NotVerifiable.Count > 0)
            ctx.Line(Plural.Text("Cli_VerifyNotVerifiable", result.NotVerifiable.Count, CommandContext.N(result.NotVerifiable.Count), CheckOutput.Ids(result.NotVerifiable)));
        return result.ExitCode;
    }

    /// <summary>Neuester Lauf eines Rechners (neuester Export, davon die neueste Auswertung); keiner: RGL-0459.</summary>
    internal static Run Latest(CommandContext ctx, Plant plant, string host)
    {
        var runs = ctx.Services.Plants.ListRuns(plant, host);
        var latest = runs.OrderBy(r => r.CollectedAtUtc).ThenBy(r => r.EvaluatedAtUtc).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault()
                     ?? throw RglCodesCli.Error(RglCodesCli.NoRuns, host);
        return ctx.Services.Plants.LoadRun(plant, latest.Id);
    }

    /// <summary>Lauf <paramref name="runId"/> laden und prüfen, dass er zu <paramref name="host"/> gehört (RGL-0467).</summary>
    internal static Run RunOf(CommandContext ctx, Plant plant, string host, string runId)
    {
        if (!RunIds.IsValid(runId)) throw RglCodesCli.Error(RglCodesCli.InvalidValue, "--run", runId, "<yyyyMMddTHHmmssZ>-<6 hex>");
        var run = ctx.Services.Plants.LoadRun(plant, runId);
        return string.Equals(run.HostName, host, StringComparison.OrdinalIgnoreCase)
            ? run
            : throw RglCodesCli.Error(RglCodesCli.RunOfOtherMachine, runId, run.HostName, host);
    }

    private static void Drift(CommandContext ctx, IReadOnlyList<ValueDrift> drift)
    {
        foreach (var d in drift.Take(MaxLines))
            ctx.Line(CliStrings.Cli_DriftLine, d.Key + (d.Path is null ? "" : " · " + d.Path), DriftName(d.Kind), d.From ?? "–", d.To ?? "–");
        if (drift.Count > MaxLines) ctx.Line(CliStrings.Cli_AndMore, drift.Count - MaxLines);
    }

    private static string ChangeName(StatusChangeKind kind) =>
        CliStrings.ResourceManager.GetString("Change_" + kind, System.Globalization.CultureInfo.CurrentUICulture) ?? kind.ToString();

    private static string DriftName(DriftKind kind) =>
        CliStrings.ResourceManager.GetString("Drift_" + kind, System.Globalization.CultureInfo.CurrentUICulture) ?? kind.ToString();
}
