// SPDX-License-Identifier: MIT
using System.Reflection;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Infrastructure;

/// <summary>
/// Plattformneutraler Teil des Gates „docs/error-codes.md aktuell“: Jeder Code von Core und Befehlen steht mit Name
/// und englischem Text als Zeile auf der Seite, und die Seite nennt keinen Code dieser Bereiche, den es nicht gibt. So
/// fällt ein neuer oder geänderter RGL-Code schon im Linux-Job auf. Die ganze Seite (mit Sammler, Windows-Anbindung,
/// RuleTool und App) erzeugt und vergleicht ErrorCodesDocTests unter Windows.
/// </summary>
public sealed class ErrorCodesCoreDocTests
{
    private static readonly Assembly Core = typeof(Riegel.Core.Model.RglCodesCommon).Assembly;
    private static readonly Assembly Commands = typeof(Riegel.Commands.RglCodesCli).Assembly;

    private static IEnumerable<(Type Codes, Assembly Assembly, string Strings)> Areas() =>
    [
        (typeof(Riegel.Core.Model.RglCodesCommon), Core, "Riegel.Core.Resources.CommonStrings"),
        (typeof(Riegel.Core.Evaluation.RglCodesEngine), Core, "Riegel.Core.Resources.EngineStrings"),
        (typeof(Riegel.Core.Parsing.RglCodesParsing), Core, "Riegel.Core.Resources.ParsingStrings"),
        (typeof(Riegel.Core.Plants.RglCodesWorkflow), Core, "Riegel.Core.Resources.WorkflowStrings"),
        (typeof(Riegel.Commands.RglCodesCli), Commands, "Riegel.Commands.Resources.CliStrings"),
        (typeof(Riegel.Core.Reports.Pdf.RglCodesPdf), Core, "Riegel.Core.Resources.PdfStrings"),
        (typeof(Riegel.Core.Evidence.RglCodesDataOutputs), Core, "Riegel.Core.Resources.DataOutputStrings"),
    ];

    [Fact]
    public void Codes_von_Core_und_Befehlen_stehen_aktuell_in_der_Fehlercode_Seite()
    {
        var page = File.ReadAllText(Path.Combine(RepoPaths.Root, "docs", "error-codes.md")).Replace("\r\n", "\n");
        var lines = new HashSet<string>(page.Split('\n'), StringComparer.Ordinal);
        var problems = new List<string>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (type, assembly, strings) in Areas())
            foreach (var (code, name) in ErrorCodeTable.Codes(type))
            {
                known.Add(code);
                var row = ErrorCodeTable.Row(code, name, ErrorCodeTable.Text(assembly, strings, code)).TrimEnd('\n');
                if (!lines.Contains(row)) problems.Add($"{code} ({type.Name}.{name}) fehlt oder ist veraltet; erwartet:\n    {row}");
            }
        // Umgekehrt: keine Zeile für einen Code dieser Klassen, der entfernt wurde (die Bereiche liegen fest, spec 7.13)
        foreach (var line in lines.Where(l => l.StartsWith("| `RGL-", StringComparison.Ordinal)))
        {
            var code = line[3..11];
            int n = int.Parse(code[4..], System.Globalization.CultureInfo.InvariantCulture);
            bool ours = n is (>= 1 and <= 99) or (>= 200 and <= 299) or (>= 400 and <= 599);
            if (ours && !known.Contains(code)) problems.Add($"{code} steht in docs/error-codes.md, gibt es in Core/Befehlen aber nicht");
        }
        Assert.True(problems.Count == 0,
            "docs/error-codes.md passt nicht zu den RglCodes-Klassen (unter Windows mit RIEGEL_UPDATE_DOCS=1 dotnet test neu erzeugen):\n"
            + string.Join("\n", problems));
    }
}
