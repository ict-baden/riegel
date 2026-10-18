// SPDX-License-Identifier: MIT
using Riegel.Commands.Parsing;
using Riegel.Commands.Resources;
using Riegel.Core.Model;

namespace Riegel.Commands.Commands;

/// <summary>
/// <c>collect [--out &lt;dir&gt;] [--gzip] [--minimal] [--deep] [--pseudonymize]</c> (spec 7.7, nur Windows): Export dieses
/// PCs mit .sha256 schreiben. Exitcodes wie beim portablen Sammler (spec 7.4): 0 vollständig, 1 geschrieben, aber
/// unvollständig (nicht als Administrator oder Einträge verweigert), 3 nichts geschrieben.
/// Versteckt: <c>collect --handoff &lt;token&gt;</c> – die erhöhte Seite der Übergabe (spec 7.6): legt
/// <c>%LocalAppData%\Riegel\handoff\&lt;token&gt;\</c> selbst an (über IPlatform), schreibt nur Export und Begleitdatei
/// hinein und sonst nichts.
/// </summary>
internal static class CollectCommand
{
    private static readonly OptionSpec[] Options =
    [
        new("--out", OptionKind.Value),
        new("--gzip", OptionKind.Flag),
        new("--minimal", OptionKind.Flag),
        new("--deep", OptionKind.Flag),
        new("--pseudonymize", OptionKind.Flag),
        new("--handoff", OptionKind.Value),
    ];

    /// <summary>Token der Übergabe: 16 bis 64 Zeichen aus [A-Za-z0-9_-] (kein Pfadteil, kein Punkt).</summary>
    internal static bool IsValidToken(string token) =>
        token.Length is >= 16 and <= 64 && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static int Run(CommandContext ctx, IReadOnlyList<string> args)
    {
        var a = ArgumentParser.Parse("collect", args, Options);
        a.Exclusive("--handoff", "--out");
        var token = a.Value("--handoff");
        if (token is not null && !IsValidToken(token)) throw RglCodesCli.Error(RglCodesCli.InvalidHandoffToken);
        var platform = ctx.Services.Platform;
        if (!platform.CanCollect)
            throw RglCodesCli.Error(RglCodesCli.WindowsOnly, "collect", CommandContext.ProgramName + " check --snapshot <file>");

        var options = new CollectOptions(a.Has("--minimal"), a.Has("--deep"), a.Has("--pseudonymize"));
        // Übergabe: den Ordner vor dem Erfassen anlegen – existiert er schon oder ist ein Pfadteil ein Reparse-Punkt,
        // lehnt die Plattform ab, und es wird gar nicht erst erfasst
        var folder = token is not null
            ? platform.CreateHandoffDirectory(token)
            : Path.GetFullPath(a.Value("--out") ?? Directory.GetCurrentDirectory());
        var snapshot = platform.CollectThisMachine(options, ctx.Cancel);
        var path = SnapshotFile.Write(snapshot, folder, a.Has("--gzip"));
        ctx.Line(CliStrings.Cli_CollectWritten, path, Hashes.Sha256File(path));
        bool incomplete = !snapshot.IsAdmin || snapshot.Items.Any(i => i.Status is ItemStatus.Denied);
        if (incomplete) ctx.Line(CliStrings.Cli_CollectIncomplete);
        return incomplete ? CommandApp.Warning : CommandApp.Ok;
    }
}
