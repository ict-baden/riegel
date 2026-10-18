// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Commands.Resources;
using Riegel.Core.Localization;
using Riegel.Core.Model;
using Riegel.Core.Resources;
using Riegel.Core.Services;

namespace Riegel.Commands;

/// <summary>
/// Was ein Befehl zum Arbeiten bekommt: Dienste, Ausgaben, Prüfermodus und Abbruch. Texte kommen aus CliStrings in
/// der Sprache der Oberfläche (<see cref="CultureInfo.CurrentUICulture"/>), Zahlen und Daten im Bildschirmtext nach
/// <see cref="CultureInfo.CurrentCulture"/>; Dateien und JSON bleiben invariant.
/// </summary>
internal sealed class CommandContext(CommandServices services, TextWriter output, TextWriter error, bool readOnly, CancellationToken cancel)
{
    /// <summary>Name der Anlagendatei im Anlagenordner (spec 7.6).</summary>
    public const string PlantFileName = "riegel-plant.json";

    public CommandServices Services { get; } = services;

    public TextWriter Out { get; } = output;

    public TextWriter Err { get; } = error;

    /// <summary>--nur-lesen / --read-only: nichts in den Anlagenordner schreiben (spec 7.6, Richtlinie 09).</summary>
    public bool ReadOnly { get; } = readOnly;

    public CancellationToken Cancel { get; } = cancel;

    /// <summary>Sprache der Ausgaben (en, de, es, fr, zh-Hans).</summary>
    public string Language { get; } = UiLanguage.Normalize(CultureInfo.CurrentUICulture.Name) is { Length: > 0 } code ? code : UiLanguage.Fallback;

    /// <summary>Riegel.Cli.exe unter Windows, riegel sonst (für Hilfe und Beispiele).</summary>
    public static string ProgramName => OperatingSystem.IsWindows() ? "Riegel.Cli.exe" : "riegel";

    public void Line() => Out.WriteLine();

    /// <summary>Eine Zeile (oder mehrere: Zeilenumbrüche aus den Sprachdateien werden zu denen der Ausgabe).</summary>
    public void Line(string text) => Out.WriteLine(text.ReplaceLineEndings(Out.NewLine));

    /// <summary>Text aus CliStrings mit Platzhaltern, nach der Bildschirmkultur formatiert.</summary>
    public void Line(string format, params object?[] args) => Line(F(format, args));

    /// <summary>Hinweis auf stderr (z. B. fehlende .sha256), ändert den Exitcode nicht.</summary>
    public void Note(string format, params object?[] args) => Err.WriteLine(F(format, args).ReplaceLineEndings(Err.NewLine));

    public static string F(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>Zahl im Bildschirmtext.</summary>
    public static string N(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Datum im Bildschirmtext (kurzes Datumsformat der Kultur).</summary>
    public static string D(DateOnly value) => value.ToString("d", CultureInfo.CurrentCulture);

    /// <summary>Zeitpunkt im Bildschirmtext, in UTC und als solcher gekennzeichnet.</summary>
    public static string T(DateTimeOffset value) =>
        value.UtcDateTime.ToString("g", CultureInfo.CurrentCulture) + " UTC";

    /// <summary>Statusname in der Oberflächensprache (CommonStrings).</summary>
    public static string StatusName(ResultStatus status) =>
        CommonStrings.ResourceManager.GetString("Status_" + status, CultureInfo.CurrentUICulture) ?? status.ToString();

    /// <summary>Schwere in der Oberflächensprache.</summary>
    public static string SeverityName(Severity severity) =>
        CommonStrings.ResourceManager.GetString("Severity_" + severity, CultureInfo.CurrentUICulture) ?? severity.ToString();

    /// <summary>Kategorie in der Oberflächensprache.</summary>
    public static string CategoryName(string category) =>
        CommonStrings.ResourceManager.GetString("Category_" + category, CultureInfo.CurrentUICulture) ?? category;

    /// <summary>Kennzeichen eines Ergebnisses in Worten, durch Kommas getrennt.</summary>
    public static string FlagNames(ResultFlags flags) =>
        string.Join(", ", Enum.GetValues<ResultFlags>().Where(f => f != ResultFlags.None && (flags & f) == f)
            .Select(f => CommonStrings.ResourceManager.GetString("Flag_" + f, CultureInfo.CurrentUICulture) ?? f.ToString()));

    /// <summary>Kennzeichen eines Laufs in Worten.</summary>
    public static string RunFlagNames(RunFlags flags) =>
        string.Join(", ", Enum.GetValues<RunFlags>().Where(f => f != RunFlags.None && (flags & f) == f)
            .Select(f => CommonStrings.ResourceManager.GetString("RunFlag_" + f, CultureInfo.CurrentUICulture) ?? f.ToString()));

    /// <summary>Begründungsart in der Oberflächensprache.</summary>
    public static string ReasonName(ReasonCategory reason) =>
        CommonStrings.ResourceManager.GetString("Reason_" + reason, CultureInfo.CurrentUICulture) ?? reason.ToString();

    // ---- Anlagenordner ----

    /// <summary>
    /// Öffnet eine vorhandene Anlage. <paramref name="forWrite"/>: der Befehl schreibt in den Anlagenordner – im
    /// Prüfermodus abgelehnt (RGL-0457). Fehlt riegel-plant.json: RGL-0468.
    /// </summary>
    public Plant OpenPlant(string dir, bool forWrite, string command)
    {
        if (forWrite && ReadOnly) throw RglCodesCli.Error(RglCodesCli.ReadOnlyRefused, command);
        var folder = Path.GetFullPath(dir);
        if (!File.Exists(Path.Combine(folder, PlantFileName))) throw RglCodesCli.Error(RglCodesCli.PlantNotFound, folder);
        return Services.Plants.Open(folder, ReadOnly);
    }

    /// <summary>
    /// Zielordner für erzeugte Dokumente: <c>--out</c>, sonst <c>reports/&lt;yyyy-MM-ddTHHmmssZ&gt;/</c> im
    /// Anlagenordner (spec 7.6). Im Prüfermodus ist <c>--out</c> Pflicht (RGL-0458).
    /// </summary>
    public string ReportFolder(Plant plant, string? outDir)
    {
        if (outDir is not null) return Path.GetFullPath(outDir);
        if (ReadOnly || plant.ReadOnly) throw RglCodesCli.Error(RglCodesCli.ReadOnlyNeedsOut);
        var folder = plant.Folder ?? throw RglCodesCli.Error(RglCodesCli.ReadOnlyNeedsOut);
        var stamp = Services.Clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        return Path.Combine(folder, "reports", stamp);
    }

    /// <summary>Berichtssprache: --language, sonst die der Anlage, sonst die der Oberfläche.</summary>
    public string ReportLanguage(Plant? plant, string? option)
    {
        if (option is not null)
        {
            var code = UiLanguage.Normalize(option);
            if (code.Length == 0) throw RglCodesCli.Error(RglCodesCli.InvalidValue, "--language", option, string.Join(", ", UiLanguage.Supported));
            return code;
        }
        if (UiLanguage.Normalize(plant?.Settings.ReportLanguage) is { Length: > 0 } fromPlant) return fromPlant;
        return Language;
    }

    /// <summary>Hinweis „Datei geschrieben: …“.</summary>
    public void Written(string path) => Line(CliStrings.Cli_Written, path);
}
