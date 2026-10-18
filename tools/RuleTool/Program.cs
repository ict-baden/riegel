// SPDX-License-Identifier: MIT
namespace Riegel.Tools.RuleTool;

/// <summary>
/// Entwicklerwerkzeug für den Katalog (Paket rules-a): <c>rules-doc</c> (docs/rules.md erzeugen), <c>verify-sources</c>
/// (Verweise gegen lokale Quellkopien prüfen), <c>coverage</c> (Belegschlüssel gegen den Erfassungsplan). Gerüst:
/// Befehle melden sich als noch nicht umgesetzt (Exitcode 3). Nicht ausgeliefert; Ausgabe bewusst nur Englisch.
/// </summary>
internal static class Program
{
    private static readonly string[] Commands = ["rules-doc", "verify-sources", "coverage"];

    public static int Main(string[] args)
    {
        if (args.Length == 0 || !Commands.Contains(args[0], StringComparer.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Usage: RuleTool rules-doc | verify-sources <folders> | coverage");
            return 3;
        }
        Console.Error.WriteLine($"{Core.Model.RglCodesCommon.NotImplemented}: RuleTool {args[0]}");
        return 3;
    }
}
