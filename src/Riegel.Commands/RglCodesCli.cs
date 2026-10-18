// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Commands.Resources;
using Riegel.Core.Model;

namespace Riegel.Commands;

/// <summary>
/// Fehlercodes der Befehlszeile: RGL-0450 bis RGL-0499 (spec 7.13). Jeder Code endet mit Exitcode 3 (UNKNOWN).
/// Die Texte stehen in CliStrings unter <c>Error_RGL&lt;nnnn&gt;</c> (Richtlinie 13 Punkt 9: Code bleibt, Text übersetzt).
/// </summary>
public static class RglCodesCli
{
    public const int First = 450;
    public const int Last = 499;

    /// <summary>Unbekannter Befehl, unbekannte Option oder überzähliges Argument.</summary>
    public const string UsageError = "RGL-0450";

    /// <summary>Eine Option braucht einen Wert.</summary>
    public const string MissingValue = "RGL-0451";

    /// <summary>Ungültiger Wert für eine Option (Format, Alarmart, Token, Datum …).</summary>
    public const string InvalidValue = "RGL-0452";

    /// <summary>Eine Pflichtangabe fehlt (z. B. --plant).</summary>
    public const string MissingOption = "RGL-0453";

    /// <summary>Zwei Angaben schließen sich aus.</summary>
    public const string ConflictingOptions = "RGL-0454";

    /// <summary>Nur unter Windows möglich (collect, collector export, check --this-pc).</summary>
    public const string WindowsOnly = "RGL-0455";

    /// <summary>Ungültiges Übergabe-Token (collect --handoff).</summary>
    public const string InvalidHandoffToken = "RGL-0456";

    /// <summary>Prüfermodus: der Befehl würde in den Anlagenordner schreiben.</summary>
    public const string ReadOnlyRefused = "RGL-0457";

    /// <summary>Prüfermodus: Ausgaben nur mit --out.</summary>
    public const string ReadOnlyNeedsOut = "RGL-0458";

    /// <summary>Kein Lauf für diesen Rechner in der Anlage.</summary>
    public const string NoRuns = "RGL-0459";

    /// <summary>Kein freigegebener Stand für diesen Rechner.</summary>
    public const string NoApprovedState = "RGL-0460";

    /// <summary>Kein Importnachweis (collection-record).</summary>
    public const string NoImports = "RGL-0461";

    /// <summary>IDs, die es im Register nicht gibt.</summary>
    public const string UnknownEntries = "RGL-0462";

    /// <summary>Regel-ID, die es im Katalog nicht gibt.</summary>
    public const string UnknownRule = "RGL-0463";

    /// <summary>Baseline nicht im Anlagenordner.</summary>
    public const string BaselineNotFound = "RGL-0464";

    /// <summary>Die .sha256-Begleitdatei passt nicht zum Export.</summary>
    public const string SidecarMismatch = "RGL-0465";

    /// <summary>Freigabe ohne Prüftermin (reviewBy).</summary>
    public const string ReviewDateMissing = "RGL-0466";

    /// <summary>--run gehört zu einem anderen Rechner.</summary>
    public const string RunOfOtherMachine = "RGL-0467";

    /// <summary>Kein Anlagenordner (riegel-plant.json fehlt).</summary>
    public const string PlantNotFound = "RGL-0468";

    /// <summary>Mit Strg+C abgebrochen.</summary>
    public const string Cancelled = "RGL-0469";

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        UsageError, MissingValue, InvalidValue, MissingOption, ConflictingOptions, WindowsOnly, InvalidHandoffToken,
        ReadOnlyRefused, ReadOnlyNeedsOut, NoRuns, NoApprovedState, NoImports, UnknownEntries, UnknownRule,
        BaselineNotFound, SidecarMismatch, ReviewDateMissing, RunOfOtherMachine, PlantNotFound, Cancelled,
    ];

    /// <summary>Text zu einem Code dieses Bereichs in der aktuellen Sprache, mit Platzhaltern.</summary>
    public static string Message(string code, params object?[] args)
    {
        var key = "Error_" + code.Replace("-", "", StringComparison.Ordinal);
        var text = CliStrings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? code;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    /// <summary>Fehler mit einem Code dieses Bereichs.</summary>
    internal static RiegelException Error(string code, params object?[] args) => new(code, Message(code, args));
}
