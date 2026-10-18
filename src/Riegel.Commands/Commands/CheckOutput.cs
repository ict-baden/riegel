// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Commands.Alerting;
using Riegel.Commands.Output;
using Riegel.Commands.Resources;
using Riegel.Core.Model;

namespace Riegel.Commands.Commands;

/// <summary>Ausgabe von check und import: Text (übersetzt), JSON (invariant) oder eine Icinga-Zeile je Rechner (O11).</summary>
internal static class CheckOutput
{
    /// <summary>Höchstzahl Regel-IDs je Liste im Bildschirmtext; der Rest steht in JSON und im Bericht.</summary>
    private const int MaxIds = 10;

    /// <summary>Statuswort in der Oberflächensprache.</summary>
    public static string StateName(int exitCode) => exitCode switch
    {
        CommandApp.Ok => CliStrings.State_Ok,
        CommandApp.Warning => CliStrings.State_Warning,
        CommandApp.Critical => CliStrings.State_Critical,
        _ => CliStrings.State_Unknown,
    };

    /// <summary>Eine Zeile je Rechner, der schlechteste zuerst (Icinga zeigt die erste Zeile als Status).</summary>
    public static void Brief(CommandContext ctx, IReadOnlyList<RunAssessment> assessments)
    {
        foreach (var a in assessments.OrderByDescending(x => x.Outcome.ExitCode).ThenBy(x => x.Run.HostName, StringComparer.OrdinalIgnoreCase))
            ctx.Line(BriefLine.Format(a));
    }

    public static void Text(CommandContext ctx, IReadOnlyList<RunAssessment> assessments, AlertPolicy policy)
    {
        foreach (var a in assessments)
        {
            var run = a.Run;
            var s = run.Summary;
            var c = a.Counts;
            ctx.Line(CliStrings.Cli_CheckHost, run.HostName, a.Source);
            ctx.Line(a.Stored ? CliStrings.Cli_CheckRunStored : CliStrings.Cli_CheckRunNotStored, run.Id);
            ctx.Line(CliStrings.Cli_CheckCounts, CommandContext.N(run.Results.Count), CommandContext.N(s.Pass), CommandContext.N(s.Fail),
                CommandContext.N(s.Exception), CommandContext.N(s.OutOfScope), CommandContext.N(s.NotApplicable),
                CommandContext.N(s.NotCheckable), CommandContext.N(s.Info));
            ctx.Line(CliStrings.Cli_CheckOpen, CommandContext.N(c[AlertToken.High]), CommandContext.N(c[AlertToken.Medium]), CommandContext.N(c[AlertToken.Low]));
            ctx.Line(CliStrings.Cli_CheckWorse, CommandContext.N(c[AlertToken.Regression]), Ids(a.Regressions.Select(r => r.RuleId)));
            ctx.Line(CliStrings.Cli_CheckNewHigh, CommandContext.N(c[AlertToken.NewHigh]), Ids(a.NewHigh.Select(r => r.RuleId)));
            ctx.Line(CliStrings.Cli_CheckRegisters, CommandContext.N(c[AlertToken.Expiring]), CommandContext.N(c[AlertToken.Expired]),
                CommandContext.N(c[AlertToken.Pending]), CommandContext.N(c[AlertToken.Overdue]));
            if (s.Score is { } score)
                ctx.Line(CliStrings.Cli_CheckScore, CommandContext.N(score), s.Documented is { } d ? CommandContext.N(d) : "–", CommandContext.N(s.CheckedShare));
            else
                ctx.Line(CliStrings.Cli_CheckScoreNone, CommandContext.N(s.CheckedShare));
            if (run.Flags != RunFlags.None) ctx.Line(CliStrings.Cli_CheckFlags, CommandContext.RunFlagNames(run.Flags));
            if ((run.Flags & RunFlags.Incomplete) != 0) ctx.Line(CliStrings.Cli_CheckIncomplete);
            if (a.Previous is not null) ctx.Line(CliStrings.Cli_CheckVsPrevious, a.Previous.Id, CommandContext.T(a.Previous.CollectedAtUtc));
            else if (a.InPlant) ctx.Line(CliStrings.Cli_CheckNoPrevious);
            if (a.VsApproved is not null) ctx.Line(CliStrings.Cli_CheckVsApproved, a.VsApproved.FromRunId ?? "–");
            var fired = a.Outcome.FiredCritical.Concat(a.Outcome.FiredWarning).Select(AlertTokens.Name).ToList();
            if (fired.Count > 0)
                ctx.Line(CliStrings.Cli_CheckResult, StateName(a.Outcome.ExitCode), a.Outcome.ExitCode, string.Join(", ", fired));
            else
                ctx.Line(CliStrings.Cli_CheckResultOk, StateName(a.Outcome.ExitCode), a.Outcome.ExitCode);
            ctx.Line();
        }
        ctx.Line(CliStrings.Cli_CheckPolicy, policy.ModeName, Tokens(policy.Critical), Tokens(policy.Warning));
        if (assessments.Count > 1)
        {
            int exit = assessments.Max(x => x.Outcome.ExitCode);
            ctx.Line(Plural.Text("Cli_CheckOverall", assessments.Count, StateName(exit), exit, CommandContext.N(assessments.Count)));
        }
    }

    public static void Json(CommandContext ctx, IReadOnlyList<RunAssessment> assessments, AlertPolicy policy, ImportRecord? import = null)
    {
        int exit = assessments.Count == 0 ? CommandApp.Ok : assessments.Max(x => x.Outcome.ExitCode);
        JsonOutput.Write(ctx.Out, w =>
        {
            w.WriteStartObject();
            w.WriteString("schema", import is null ? "riegel.cli.check/1" : "riegel.cli.import/1");
            w.WriteString("status", JsonOutput.Status(exit));
            w.WriteNumber("exitCode", exit);
            w.WriteStartObject("alert");
            w.WriteString("mode", policy.ModeName);
            JsonOutput.Strings(w, "critical", policy.Critical.Select(AlertTokens.Name));
            JsonOutput.Strings(w, "warning", policy.Warning.Select(AlertTokens.Name));
            w.WriteEndObject();
            if (import is not null) JsonOutput.Contract(w, "import", import);
            w.WriteStartArray("machines");
            foreach (var a in assessments) Machine(w, a);
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    private static void Machine(Utf8JsonWriter w, RunAssessment a)
    {
        w.WriteStartObject();
        w.WriteString("host", a.Run.HostName);
        w.WriteString("source", a.Source);
        w.WriteString("runId", a.Run.Id);
        w.WriteBoolean("stored", a.Stored);
        w.WriteString("status", JsonOutput.Status(a.Outcome.ExitCode));
        w.WriteNumber("exitCode", a.Outcome.ExitCode);
        JsonOutput.Strings(w, "firedCritical", a.Outcome.FiredCritical.Select(AlertTokens.Name));
        JsonOutput.Strings(w, "firedWarning", a.Outcome.FiredWarning.Select(AlertTokens.Name));
        w.WriteStartObject("tokens");
        foreach (var (token, count) in a.Counts.Items) w.WriteNumber(AlertTokens.Name(token), count);
        w.WriteEndObject();
        w.WriteNumber("open", a.Open);
        w.WriteStartArray("regressions");
        foreach (var r in a.Regressions)
        {
            w.WriteStartObject();
            w.WriteString("ruleSetId", r.RuleSetId);
            w.WriteString("ruleId", r.RuleId);
            w.WriteString("severity", r.Severity.ToString().ToLowerInvariant());
            JsonOutput.StringOrNull(w, "basis", r.Basis is { } b ? JsonNamingPolicy.CamelCase.ConvertName(b.ToString()) : null);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        JsonOutput.Strings(w, "newHigh", a.NewHigh.Select(r => r.RuleId));
        JsonOutput.StringOrNull(w, "previousRunId", a.Previous?.Id);
        JsonOutput.StringOrNull(w, "approvedRunId", a.VsApproved?.FromRunId);
        JsonOutput.Contract(w, "run", a.Run);
        w.WriteStartObject("comparisons");
        JsonOutput.Contract(w, "previous", a.VsPrevious);
        JsonOutput.Contract(w, "approved", a.VsApproved);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    /// <summary>Regel-IDs für den Bildschirm: höchstens zehn, dann „… und n weitere“.</summary>
    internal static string Ids(IEnumerable<string> ids)
    {
        var list = ids.ToList();
        if (list.Count == 0) return "–";
        var shown = string.Join(", ", list.Take(MaxIds));
        return list.Count <= MaxIds ? shown : shown + " " + string.Format(CultureInfo.CurrentCulture, CliStrings.Cli_AndMore, list.Count - MaxIds);
    }

    private static string Tokens(IReadOnlyList<AlertToken> tokens) => tokens.Count == 0 ? "none" : string.Join(",", tokens.Select(AlertTokens.Name));
}
