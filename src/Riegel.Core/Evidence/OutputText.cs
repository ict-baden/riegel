// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Localization;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Model.Reports;
using Riegel.Core.Resources;

namespace Riegel.Core.Evidence;

/// <summary>
/// Texte der Datenausgaben in einer festen Sprache (Excel, README der Abhilfe und des Nachweispakets). Holt eigene
/// Texte aus DataOutputStrings und gemeinsame Namen (Status, Schwere, Kategorien, Flags) aus CommonStrings – immer
/// in der Berichtssprache, nie in der Sprache des Threads.
/// </summary>
internal sealed class OutputText
{
    private OutputText(string language)
    {
        var code = UiLanguage.Normalize(language);
        Language = code.Length > 0 ? code : UiLanguage.Fallback;
        Culture = CultureInfo.GetCultureInfo(Language);
    }

    /// <summary>Unterstützter Sprachcode (en, de, es, fr, zh-Hans).</summary>
    public string Language { get; }

    public CultureInfo Culture { get; }

    public static OutputText For(string? language) => new(language ?? UiLanguage.Fallback);

    /// <summary>Eigener Text (DataOutputStrings), mit Platzhaltern in der Berichtskultur.</summary>
    public string Get(string key, params object?[] args)
    {
        var text = DataOutputStrings.ResourceManager.GetString(key, Culture) ?? key;
        return args.Length == 0 ? text : string.Format(Culture, text, args);
    }

    /// <summary>Gemeinsamer Text (CommonStrings).</summary>
    public string Common(string key, params object?[] args)
    {
        var text = CommonStrings.ResourceManager.GetString(key, Culture) ?? key;
        return args.Length == 0 ? text : string.Format(Culture, text, args);
    }

    public string Status(ResultStatus s) => Common("Status_" + s);

    public string Severity(Severity s) => Common("Severity_" + s);

    public string Severity(Severity? s) => s is { } v ? Severity(v) : "";

    public string Category(string code) => CommonStrings.ResourceManager.GetString("Category_" + code, Culture) ?? code;

    public string Reason(ReasonCategory r) => Common("Reason_" + r);

    /// <summary>Flags als lesbare Namen, mit Komma getrennt.</summary>
    public string Flags(ResultFlags f) =>
        string.Join(", ", FlagsArrayConverter<ResultFlags>.Names(f).Select(n => Common("Flag_" + Pascal(n))));

    public string Flags(RunFlags f) =>
        string.Join(", ", FlagsArrayConverter<RunFlags>.Names(f).Select(n => Common("RunFlag_" + Pascal(n))));

    public string Flags(RegisterFlags f) =>
        string.Join(", ", FlagsArrayConverter<RegisterFlags>.Names(f).Select(n => Common("RegisterFlag_" + Pascal(n))));

    public string NotSet => Common("Value_NotSet");

    /// <summary>Kanonischer Wert für die Anzeige: null → „(nicht gesetzt)“.</summary>
    public string Display(string? canonical) => canonical ?? NotSet;

    /// <summary>Datum ISO 8601 (yyyy-MM-dd), invariant.</summary>
    public static string Date(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    /// <summary>Zeitpunkt ISO 8601 UTC, invariant.</summary>
    public static string Timestamp(DateTimeOffset? t) => t is { } v ? UtcDateTimeOffsetConverter.Format(v) : "";

    /// <summary>camelCase-Name eines Enum-Werts (wie in JSON).</summary>
    public static string Camel<T>(T value) where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static string Pascal(string camel) => camel.Length == 0 ? camel : char.ToUpperInvariant(camel[0]) + camel[1..];
}

/// <summary>Atomares Schreiben der Ausgabedateien: erst eine Temp-Datei daneben, dann Ersetzen.</summary>
internal static class OutputFiles
{
    public static void WriteAtomic(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
