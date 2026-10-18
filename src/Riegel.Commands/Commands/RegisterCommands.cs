// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Commands.Output;
using Riegel.Commands.Parsing;
using Riegel.Commands.Resources;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;

namespace Riegel.Commands.Commands;

/// <summary>
/// Pflege der Register (spec 2.11–2.13, 7.7): <c>exceptions validate|list|expiring|approval-sheet|approve|import-template</c>,
/// <c>tailoring list|validate|approve|import-template</c> und <c>measures list|overdue|add|close</c>. Gültigkeit,
/// Anwendung und Speichern (mit Sperre und Verlauf) macht der Registerdienst; hier nur Aufruf und Ausgabe.
/// </summary>
internal static class RegisterCommands
{
    private static readonly OptionSpec Plant = new("--plant", OptionKind.Value);
    private static readonly OptionSpec Format = new("--format", OptionKind.Value);

    public static int Exceptions(CommandContext ctx, IReadOnlyList<string> args) =>
        Dispatch(ctx, "exceptions", args, new Dictionary<string, Func<CommandContext, IReadOnlyList<string>, int>>
        {
            ["validate"] = (c, r) => Validate(c, r, RegisterKind.Exceptions),
            ["list"] = (c, r) => List(c, r, RegisterKind.Exceptions, expiringOnly: false),
            ["expiring"] = (c, r) => List(c, r, RegisterKind.Exceptions, expiringOnly: true),
            ["approval-sheet"] = ApprovalSheet,
            ["approve"] = (c, r) => Approve(c, r, RegisterKind.Exceptions),
            ["import-template"] = (c, r) => ImportTemplate(c, r, RegisterKind.Exceptions),
        });

    public static int Tailoring(CommandContext ctx, IReadOnlyList<string> args) =>
        Dispatch(ctx, "tailoring", args, new Dictionary<string, Func<CommandContext, IReadOnlyList<string>, int>>
        {
            ["list"] = (c, r) => List(c, r, RegisterKind.Tailoring, expiringOnly: false),
            ["validate"] = (c, r) => Validate(c, r, RegisterKind.Tailoring),
            ["approve"] = (c, r) => Approve(c, r, RegisterKind.Tailoring),
            ["import-template"] = (c, r) => ImportTemplate(c, r, RegisterKind.Tailoring),
        });

    public static int Measures(CommandContext ctx, IReadOnlyList<string> args) =>
        Dispatch(ctx, "measures", args, new Dictionary<string, Func<CommandContext, IReadOnlyList<string>, int>>
        {
            ["list"] = (c, r) => MeasureList(c, r, overdueOnly: false),
            ["overdue"] = (c, r) => MeasureList(c, r, overdueOnly: true),
            ["add"] = MeasureAdd,
            ["close"] = MeasureClose,
        });

    /// <summary>Unterbefehl wählen; fehlt er oder ist er unbekannt: RGL-0450 mit der Liste.</summary>
    internal static int Dispatch(CommandContext ctx, string command, IReadOnlyList<string> args,
        IReadOnlyDictionary<string, Func<CommandContext, IReadOnlyList<string>, int>> subcommands)
    {
        if (args.Count == 0)
            throw RglCodesCli.Error(RglCodesCli.MissingOption, command, string.Join("|", subcommands.Keys));
        var sub = subcommands.Keys.FirstOrDefault(k => string.Equals(k, args[0], StringComparison.OrdinalIgnoreCase))
                  ?? throw RglCodesCli.Error(RglCodesCli.UsageError, $"{command} {args[0]}");
        return subcommands[sub](ctx, args.Skip(1).ToList());
    }

    // ================= validate =================

    /// <summary>0 gültig, 1 Warnungen, 2 ungültig, 3 Fehler (spec 7.7).</summary>
    private static int Validate(CommandContext ctx, IReadOnlyList<string> args, RegisterKind kind)
    {
        var name = Name(kind) + " validate";
        var a = ArgumentParser.Parse(name, args, [Plant, Format]);
        var format = a.Choice("--format", "text", "json") ?? "text";
        var plant = ctx.OpenPlant(a.Required("--plant", "<folder>"), forWrite: false, name);
        var registers = ctx.Services.Registers.Load(plant);
        var catalogue = PlantBaselines.Catalogue(ctx, plant);
        var issues = ctx.Services.Registers.Validate(registers, catalogue, plant, ctx.Services.Today).Where(i => i.Register == kind).ToList();
        int exit = issues.Any(i => i.Severity == IssueSeverity.Error) ? CommandApp.Critical
            : issues.Count > 0 ? CommandApp.Warning : CommandApp.Ok;

        if (format == "json")
        {
            JsonOutput.Write(ctx.Out, w =>
            {
                w.WriteStartObject();
                w.WriteString("schema", "riegel.cli.validate/1");
                w.WriteString("register", Name(kind));
                w.WriteString("status", exit switch { CommandApp.Ok => "valid", CommandApp.Warning => "warnings", _ => "invalid" });
                w.WriteNumber("exitCode", exit);
                w.WriteStartArray("issues");
                foreach (var issue in issues) JsonOutput.ContractValue(w, issue);
                w.WriteEndArray();
                w.WriteEndObject();
            });
            return exit;
        }
        int entries = kind == RegisterKind.Exceptions ? registers.Exceptions.Exceptions.Count : registers.Tailoring.Entries.Count;
        foreach (var issue in issues)
            ctx.Line(CliStrings.Cli_IssueLine, issue.Severity == IssueSeverity.Error ? CliStrings.Issue_Error : CliStrings.Issue_Warning,
                issue.EntryId ?? "–", issue.Code, issue.Message);
        ctx.Line(exit switch
        {
            CommandApp.Ok => CliStrings.Cli_ValidateOk,
            CommandApp.Warning => CliStrings.Cli_ValidateWarnings,
            _ => CliStrings.Cli_ValidateInvalid,
        }, Name(kind), CommandContext.N(entries), CommandContext.N(issues.Count(i => i.Severity == IssueSeverity.Error)),
            CommandContext.N(issues.Count(i => i.Severity == IssueSeverity.Warning)));
        return exit;
    }

    // ================= list / expiring =================

    private static readonly OptionSpec[] ListOptions = [Plant, Format, new("--days", OptionKind.Value)];

    private static int List(CommandContext ctx, IReadOnlyList<string> args, RegisterKind kind, bool expiringOnly)
    {
        var name = Name(kind) + (expiringOnly ? " expiring" : " list");
        var a = ArgumentParser.Parse(name, args, expiringOnly ? ListOptions : [Plant, Format]);
        var format = a.Choice("--format", "text", "json") ?? "text";
        var plant = ctx.OpenPlant(a.Required("--plant", "<folder>"), forWrite: false, name);
        int warnDays = a.Int("--days", 0, 3650) ?? plant.Settings.WarnDays;
        var today = ctx.Services.Today;
        var registers = ctx.Services.Registers.Load(plant);
        var rows = kind == RegisterKind.Exceptions
            ? registers.Exceptions.Exceptions.Select(e => new Row(e.Id, e.RuleId, Scope(e.Scope), EntryState(e.Approval, ContentHashes.Of(e), today, warnDays),
                e.Approval.ReviewBy, ReasonText(e.Category), e.Justification, Entry: e, Tailored: null)).ToList()
            : registers.Tailoring.Entries.Select(e => new Row(e.Id, e.RuleId, Scope(e.Scope), EntryState(e.Approval, ContentHashes.Of(e), today, warnDays),
                e.Approval.ReviewBy, null, e.Reason, Entry: null, Tailored: e)).ToList();
        if (expiringOnly) rows = rows.Where(r => r.State is RegisterEntryState.Expiring or RegisterEntryState.Expired).ToList();
        rows = rows.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();

        if (format == "json")
        {
            JsonOutput.Write(ctx.Out, w =>
            {
                w.WriteStartObject();
                w.WriteString("schema", "riegel.cli.register/1");
                w.WriteString("register", Name(kind));
                w.WriteString("today", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                w.WriteStartArray("entries");
                foreach (var row in rows)
                {
                    w.WriteStartObject();
                    w.WriteString("state", StateKey(row.State));
                    if (row.Entry is not null) JsonOutput.Contract(w, "entry", row.Entry);
                    else JsonOutput.Contract(w, "entry", row.Tailored);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            });
            return CommandApp.Ok;
        }
        if (rows.Count == 0)
        {
            ctx.Line(expiringOnly ? CliStrings.Cli_RegisterNoneExpiring : CliStrings.Cli_RegisterEmpty, Name(kind), CommandContext.N(warnDays));
            return CommandApp.Ok;
        }
        foreach (var row in rows)
        {
            ctx.Line(CliStrings.Cli_RegisterLine, row.Id, row.RuleId, StateName(row.State), row.ReviewBy is { } d ? CommandContext.D(d) : "–", row.Scope);
            ctx.Line(CliStrings.Cli_RegisterText, row.Reason is null ? row.Text : $"{row.Reason}: {row.Text}");
        }
        return CommandApp.Ok;
    }

    private sealed record Row(string Id, string RuleId, string Scope, RegisterEntryState State, DateOnly? ReviewBy, string? Reason,
        string Text, ExceptionEntry? Entry, TailoringEntry? Tailored);

    /// <summary>
    /// Zustand eines Eintrags für die Anzeige (spec 2.11): ohne vollständige Freigabe beantragt; Inhalt nach der
    /// Freigabe geändert; Prüftermin vorbei abgelaufen; innerhalb von warnDays läuft ab; sonst gültig. Was darüber
    /// hinaus ungültig ist, meldet <c>validate</c>.
    /// </summary>
    internal static RegisterEntryState EntryState(Approval approval, string contentSha256, DateOnly today, int warnDays)
    {
        if (string.IsNullOrWhiteSpace(approval.ApprovedByRole) || approval.ApprovedOn is null || approval.ReviewBy is null ||
            string.IsNullOrWhiteSpace(approval.ApprovalRecord))
            return RegisterEntryState.Pending;
        if (approval.ApprovedContentSha256 is { Length: > 0 } approved && !string.Equals(approved, contentSha256, StringComparison.OrdinalIgnoreCase))
            return RegisterEntryState.ChangedAfterApproval;
        if (approval.ReviewBy.Value < today) return RegisterEntryState.Expired;
        if (approval.ReviewBy.Value.DayNumber - today.DayNumber <= warnDays) return RegisterEntryState.Expiring;
        return RegisterEntryState.Valid;
    }

    // ================= approval-sheet =================

    private static readonly OptionSpec[] SheetOptions =
    [
        Plant, new("--ids", OptionKind.List, SplitCommas: true), new("--pending", OptionKind.Flag),
        new("--out", OptionKind.Value), new("--language", OptionKind.Value),
    ];

    /// <summary><c>exceptions approval-sheet [--ids …|--pending] --out &lt;dir&gt;</c>: Genehmigungsblatt (O12); ohne Angabe alle beantragten.</summary>
    private static int ApprovalSheet(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("exceptions approval-sheet", args, SheetOptions);
        a.Exclusive("--ids", "--pending");
        var plant = ctx.OpenPlant(a.Required("--plant", "<folder>"), forWrite: false, "exceptions approval-sheet");
        IReadOnlyList<string>? ids = a.Values("--ids") is { Count: > 0 } list ? list : null;
        if (ids is not null)
        {
            var registers = ctx.Services.Registers.Load(plant);
            var known = registers.Exceptions.Exceptions.Select(e => e.Id).Concat(registers.Tailoring.Entries.Select(e => e.Id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = ids.Where(id => !known.Contains(id)).ToList();
            if (unknown.Count > 0) throw RglCodesCli.Error(RglCodesCli.UnknownEntries, string.Join(", ", unknown));
        }
        var language = ctx.ReportLanguage(plant, a.Value("--language"));
        var outDir = ctx.ReportFolder(plant, a.Value("--out"));
        var data = ctx.Services.ReportData.BuildApprovalSheet(plant, ids, language);
        ctx.Written(ctx.Services.Pdf.WriteApprovalSheet(data, new PdfOptions(outDir)));
        return CommandApp.Ok;
    }

    // ================= approve =================

    private static readonly OptionSpec[] ApproveOptions =
    [
        Plant, new("--ids", OptionKind.List, SplitCommas: true), new("--role", OptionKind.Value), new("--record", OptionKind.Value),
        new("--date", OptionKind.Value), new("--name", OptionKind.Value), new("--review-by", OptionKind.Value),
    ];

    /// <summary>
    /// <c>exceptions|tailoring approve --ids … --role … --record … [--date] [--name] [--review-by]</c>: Freigabe eintragen
    /// (spec 2.11). Der Prüftermin ist Pflicht für die Gültigkeit: <c>--review-by</c> oder der schon im Eintrag stehende.
    /// </summary>
    private static int Approve(CommandContext ctx, IReadOnlyList<string> args, RegisterKind kind)
    {
        var name = Name(kind) + " approve";
        var a = ArgumentParser.Parse(name, args, ApproveOptions);
        var ids = a.RequiredList("--ids", "<id>…");
        var role = ApproveCommand.NotBlank(a, "--role", "\"<role>\"");
        var record = ApproveCommand.NotBlank(a, "--record", "\"<reference>\"");
        var date = a.Date("--date") ?? ctx.Services.Today;
        var reviewBy = a.Date("--review-by");
        var plant = ctx.OpenPlant(a.Required("--plant", "<folder>"), forWrite: true, name);
        var registers = ctx.Services.Registers.Load(plant);

        // Jede ID muss es geben, und jede braucht einen Prüftermin – sonst wird nichts geändert
        var approvals = new List<(string Id, DateOnly ReviewBy)>();
        var unknown = new List<string>();
        foreach (var id in ids)
        {
            Approval? current = kind == RegisterKind.Exceptions
                ? registers.Exceptions.Exceptions.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))?.Approval
                : registers.Tailoring.Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))?.Approval;
            if (current is null) { unknown.Add(id); continue; }
            var review = reviewBy ?? current.ReviewBy ?? throw RglCodesCli.Error(RglCodesCli.ReviewDateMissing, id);
            if (review < date) throw RglCodesCli.Error(RglCodesCli.InvalidValue, "--review-by", review.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "≥ " + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            approvals.Add((id, review));
        }
        if (unknown.Count > 0) throw RglCodesCli.Error(RglCodesCli.UnknownEntries, string.Join(", ", unknown));

        var updated = registers;
        foreach (var group in approvals.GroupBy(x => x.ReviewBy))
            updated = ctx.Services.Registers.Approve(updated, kind, [.. group.Select(x => x.Id)],
                new Approval(role, a.Value("--name"), date, group.Key, record, null));
        ctx.Services.Registers.Save(plant, updated, registers.Hashes);
        foreach (var (id, review) in approvals)
            ctx.Line(CliStrings.Cli_EntryApproved, id, role, record, CommandContext.D(date), CommandContext.D(review));
        return CommandApp.Ok;
    }

    // ================= import-template =================

    private static int ImportTemplate(CommandContext ctx, IReadOnlyList<string> args, RegisterKind kind)
    {
        var name = Name(kind) + " import-template";
        var a = ArgumentParser.Parse(name, args, [Plant], maxPositional: 1);
        if (a.Positional.Count == 0) throw RglCodesCli.Error(RglCodesCli.MissingOption, name, "<template.json>");
        var template = Path.GetFullPath(a.Positional[0]);
        if (!File.Exists(template)) throw RiegelException.Common(RglCodesCommon.NotFound, template);
        var plant = ctx.OpenPlant(a.Required("--plant", "<folder>"), forWrite: true, name);
        var registers = ctx.Services.Registers.Load(plant);
        var updated = ctx.Services.Registers.ImportTemplate(registers, template, ReadLimits.Default);
        int added = kind == RegisterKind.Exceptions
            ? updated.Exceptions.Exceptions.Count - registers.Exceptions.Exceptions.Count
            : updated.Tailoring.Entries.Count - registers.Tailoring.Entries.Count;
        ctx.Services.Registers.Save(plant, updated, registers.Hashes);
        ctx.Line(CliStrings.Cli_TemplateImported, CommandContext.N(added), Name(kind));
        return CommandApp.Ok;
    }

    // ================= measures =================

    private static int MeasureList(CommandContext ctx, IReadOnlyList<string> args, bool overdueOnly)
    {
        var name = overdueOnly ? "measures overdue" : "measures list";
        var a = ArgumentParser.Parse(name, args, [Plant, Format]);
        var format = a.Choice("--format", "text", "json") ?? "text";
        var plant = ctx.OpenPlant(a.Required("--plant", "<folder>"), forWrite: false, name);
        var today = ctx.Services.Today;
        var measures = ctx.Services.Registers.Load(plant).Measures.Measures
            .Where(m => !overdueOnly || IsOverdue(m, today))
            .OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
        if (format == "json")
        {
            JsonOutput.Write(ctx.Out, w =>
            {
                w.WriteStartObject();
                w.WriteString("schema", "riegel.cli.register/1");
                w.WriteString("register", "measures");
                w.WriteString("today", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                w.WriteStartArray("entries");
                foreach (var m in measures)
                {
                    w.WriteStartObject();
                    w.WriteBoolean("overdue", IsOverdue(m, today));
                    JsonOutput.Contract(w, "entry", m);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            });
            return CommandApp.Ok;
        }
        if (measures.Count == 0)
        {
            ctx.Line(overdueOnly ? CliStrings.Cli_MeasuresNoneOverdue : CliStrings.Cli_RegisterEmpty, "measures", CommandContext.N(plant.Settings.WarnDays));
            return CommandApp.Ok;
        }
        foreach (var m in measures)
        {
            var state = IsOverdue(m, today) ? CliStrings.MeasureState_Overdue
                : CliStrings.ResourceManager.GetString("MeasureState_" + m.State, CultureInfo.CurrentUICulture) ?? m.State.ToString();
            ctx.Line(CliStrings.Cli_MeasureLine, m.Id, m.RuleId, state, CommandContext.D(m.DueDate), m.OwnerRole, Scope(m.Scope));
            ctx.Line(CliStrings.Cli_RegisterText, m.Ticket is null ? m.Action : $"{m.Action} ({m.Ticket})");
        }
        return CommandApp.Ok;
    }

    private static bool IsOverdue(MeasureEntry m, DateOnly today) => m.State == MeasureState.Planned && m.DueDate < today;

    private static readonly OptionSpec[] AddOptions =
    [
        Plant, new("--rule", OptionKind.Value), new("--action", OptionKind.Value), new("--owner-role", OptionKind.Value),
        new("--due", OptionKind.Value), new("--machine", OptionKind.List, SplitCommas: true), new("--group", OptionKind.List, SplitCommas: true),
        new("--all-machines", OptionKind.Flag), new("--ticket", OptionKind.Value), new("--comment", OptionKind.Value),
        new("--created-by-role", OptionKind.Value),
    ];

    /// <summary>
    /// <c>measures add --plant &lt;dir&gt; --rule &lt;id&gt; --action "…" --owner-role "…" --due &lt;yyyy-MM-dd&gt;
    /// [--machine …|--group …|--all-machines] [--ticket …] [--comment …] [--created-by-role …]</c>. Ohne Geltungsbereich: alle Rechner.
    /// </summary>
    private static int MeasureAdd(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("measures add", args, AddOptions);
        var ruleId = a.Required("--rule", "<rule-id>").Trim();
        var action = ApproveCommand.NotBlank(a, "--action", "\"<action>\"");
        var owner = ApproveCommand.NotBlank(a, "--owner-role", "\"<role>\"");
        var due = a.Date("--due") ?? throw RglCodesCli.Error(RglCodesCli.MissingOption, "measures add", "--due <yyyy-MM-dd>");
        var plant = ctx.OpenPlant(a.Required("--plant", "<folder>"), forWrite: true, "measures add");
        var catalogue = PlantBaselines.Catalogue(ctx, plant);
        var rule = catalogue.Find(ruleId) ?? catalogue.Rules.FirstOrDefault(r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase))
                   ?? throw RglCodesCli.Error(RglCodesCli.UnknownRule, ruleId);
        var machines = a.Values("--machine");
        var groups = a.Values("--group");
        foreach (var g in groups.Where(g => !plant.Groups.Any(x => string.Equals(x.Id, g, StringComparison.OrdinalIgnoreCase))))
            throw RglCodesCli.Error(RglCodesCli.InvalidValue, "--group", g, plant.Groups.Count == 0 ? "–" : string.Join(", ", plant.Groups.Select(x => x.Id)));
        bool all = a.Has("--all-machines") || (machines.Count == 0 && groups.Count == 0);

        var registers = ctx.Services.Registers.Load(plant);
        var id = NextId("M-", registers.Measures.Measures.Select(m => m.Id));
        var today = ctx.Services.Today;
        var entry = new MeasureEntry(id, rule.Id, new RegisterScope([.. machines], [.. groups], all), action, owner, due, a.Value("--ticket"),
            MeasureState.Planned, today, a.Value("--created-by-role"), null, a.Value("--comment"));
        var updated = registers with { Measures = registers.Measures with { Measures = [.. registers.Measures.Measures, entry] } };
        ctx.Services.Registers.Save(plant, updated, registers.Hashes);
        ctx.Line(CliStrings.Cli_MeasureAdded, id, rule.Id, CommandContext.D(due), owner);
        return CommandApp.Ok;
    }

    private static readonly OptionSpec[] CloseOptions = [Plant, new("--state", OptionKind.Value), new("--comment", OptionKind.Value)];

    /// <summary><c>measures close &lt;id&gt; --plant &lt;dir&gt; [--state done|dropped] [--comment …]</c>; ohne --state: erledigt.</summary>
    private static int MeasureClose(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("measures close", args, CloseOptions, maxPositional: 1);
        if (a.Positional.Count == 0) throw RglCodesCli.Error(RglCodesCli.MissingOption, "measures close", "<id>");
        var id = a.Positional[0];
        var state = a.Choice("--state", "done", "dropped") == "dropped" ? MeasureState.Dropped : MeasureState.Done;
        var plant = ctx.OpenPlant(a.Required("--plant", "<folder>"), forWrite: true, "measures close");
        var registers = ctx.Services.Registers.Load(plant);
        var entry = registers.Measures.Measures.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))
                    ?? throw RglCodesCli.Error(RglCodesCli.UnknownEntries, id);
        var closed = entry with { State = state, ModifiedOn = ctx.Services.Today, Comment = a.Value("--comment") ?? entry.Comment };
        var updated = registers with
        {
            Measures = registers.Measures with { Measures = [.. registers.Measures.Measures.Select(m => ReferenceEquals(m, entry) ? closed : m)] },
        };
        ctx.Services.Registers.Save(plant, updated, registers.Hashes);
        ctx.Line(CliStrings.Cli_MeasureClosed, entry.Id,
            CliStrings.ResourceManager.GetString("MeasureState_" + state, CultureInfo.CurrentUICulture) ?? state.ToString());
        return CommandApp.Ok;
    }

    // ================= Hilfen =================

    /// <summary>Nächste freie ID der Form „M-0001“ (höchste vorhandene Nummer plus eins).</summary>
    internal static string NextId(string prefix, IEnumerable<string> existing)
    {
        int max = 0;
        foreach (var id in existing)
            if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(id[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max)
                max = n;
        return prefix + (max + 1).ToString("D4", CultureInfo.InvariantCulture);
    }

    private static string Name(RegisterKind kind) => kind switch
    {
        RegisterKind.Exceptions => "exceptions",
        RegisterKind.Tailoring => "tailoring",
        _ => "measures",
    };

    /// <summary>Geltungsbereich in Worten: Rechner, Gruppen oder ganze Anlage.</summary>
    internal static string Scope(RegisterScope scope)
    {
        if (scope.AllMachines) return CliStrings.Scope_All;
        var parts = new List<string>();
        if (scope.Machines.Count > 0) parts.Add(string.Format(CultureInfo.CurrentCulture, CliStrings.Scope_Machines, string.Join(", ", scope.Machines)));
        if (scope.Groups.Count > 0) parts.Add(string.Format(CultureInfo.CurrentCulture, CliStrings.Scope_Groups, string.Join(", ", scope.Groups)));
        return parts.Count == 0 ? "–" : string.Join("; ", parts);
    }

    private static string ReasonText(ReasonCategory reason) => CommandContext.ReasonName(reason);

    private static string StateKey(RegisterEntryState state) => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(state.ToString());

    private static string StateName(RegisterEntryState state) =>
        CliStrings.ResourceManager.GetString("EntryState_" + state, CultureInfo.CurrentUICulture) ?? state.ToString();
}
