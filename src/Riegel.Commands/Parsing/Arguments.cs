// SPDX-License-Identifier: MIT
using System.Globalization;

namespace Riegel.Commands.Parsing;

/// <summary>Art einer Option.</summary>
internal enum OptionKind
{
    /// <summary>Schalter ohne Wert (<c>--brief</c>).</summary>
    Flag,

    /// <summary>Genau ein Wert (<c>--plant &lt;Ordner&gt;</c>, auch <c>--plant=&lt;Ordner&gt;</c>); zweimal angegeben ist ein Fehler.</summary>
    Value,

    /// <summary>
    /// Ein oder mehrere Werte, wiederholbar (<c>--machine A B</c> oder <c>--machine A --machine B</c>): nimmt alle
    /// folgenden Argumente bis zum nächsten, das mit „--“ beginnt.
    /// </summary>
    List,
}

/// <summary>Eine Option eines Befehls. <paramref name="SplitCommas"/>: Werte zusätzlich an Kommas trennen (IDs).</summary>
internal sealed record OptionSpec(string Name, OptionKind Kind, bool SplitCommas = false, params string[] Aliases)
{
    public bool Matches(string name) =>
        string.Equals(name, Name, StringComparison.OrdinalIgnoreCase) ||
        Aliases.Any(a => string.Equals(name, a, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Zerlegte Argumente eines Befehls. Optionen sind nicht groß/klein-empfindlich, Werte bleiben, wie sie sind.
/// </summary>
internal sealed class ParsedArguments
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _given = new(StringComparer.OrdinalIgnoreCase);

    public ParsedArguments(string command) => Command = command;

    /// <summary>Befehl (für Fehlermeldungen), z. B. „exceptions approve“.</summary>
    public string Command { get; }

    /// <summary>Argumente ohne Option davor (Dateien, IDs …).</summary>
    public List<string> Positional { get; } = [];

    /// <summary>Wurde die Option (Schalter oder mit Wert) angegeben?</summary>
    public bool Has(string name) => _given.Contains(name);

    /// <summary>Wert einer Option mit genau einem Wert, sonst null.</summary>
    public string? Value(string name) => _values.TryGetValue(name, out var v) && v.Count > 0 ? v[0] : null;

    /// <summary>Alle Werte einer Option (leer, wenn nicht angegeben).</summary>
    public IReadOnlyList<string> Values(string name) => _values.TryGetValue(name, out var v) ? v : [];

    /// <summary>Pflichtwert; fehlt er: RGL-0453.</summary>
    public string Required(string name, string placeholder) =>
        Value(name) ?? throw RglCodesCli.Error(RglCodesCli.MissingOption, Command, $"{name} {placeholder}");

    /// <summary>Pflichtliste; fehlt sie: RGL-0453.</summary>
    public IReadOnlyList<string> RequiredList(string name, string placeholder)
    {
        var values = Values(name);
        return values.Count > 0 ? values : throw RglCodesCli.Error(RglCodesCli.MissingOption, Command, $"{name} {placeholder}");
    }

    /// <summary>Höchstens eine der Optionen; sonst RGL-0454.</summary>
    public void Exclusive(string a, string b)
    {
        if (Has(a) && Has(b)) throw RglCodesCli.Error(RglCodesCli.ConflictingOptions, a, b);
    }

    /// <summary>Datum im Format yyyy-MM-dd (invariant), sonst RGL-0452.</summary>
    public DateOnly? Date(string name)
    {
        var text = Value(name);
        if (text is null) return null;
        return DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw RglCodesCli.Error(RglCodesCli.InvalidValue, name, text, "yyyy-MM-dd");
    }

    /// <summary>Wert aus einer festen Liste (ohne Groß/klein), klein zurück; sonst RGL-0452.</summary>
    public string? Choice(string name, params string[] allowed)
    {
        var text = Value(name);
        if (text is null) return null;
        var hit = allowed.FirstOrDefault(a => string.Equals(a, text.Trim(), StringComparison.OrdinalIgnoreCase));
        return hit ?? throw RglCodesCli.Error(RglCodesCli.InvalidValue, name, text, string.Join(", ", allowed));
    }

    /// <summary>Positives Ganzzahl-Argument; sonst RGL-0452.</summary>
    public int? Int(string name, int min, int max)
    {
        var text = Value(name);
        if (text is null) return null;
        return int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max
            ? n
            : throw RglCodesCli.Error(RglCodesCli.InvalidValue, name, text, $"{min}–{max}");
    }

    internal void AddFlag(string name) => _given.Add(name);

    internal void AddValue(string name, string value)
    {
        _given.Add(name);
        if (!_values.TryGetValue(name, out var list)) _values[name] = list = [];
        list.Add(value);
    }
}

/// <summary>
/// Zerlegt die Argumente eines Befehls nach seinen Optionen – ohne fremde Bibliothek (Paket cli, Punkt 1).
/// Unbekannte Optionen, fehlende Werte und überzählige Argumente sind Aufruffehler (RGL-0450/0451, Exitcode 3).
/// </summary>
internal static class ArgumentParser
{
    /// <summary>Wörter, die überall die Hilfe zeigen.</summary>
    public static readonly string[] HelpWords = ["--help", "--hilfe", "-h", "-?", "/?"];

    public static bool IsHelp(string arg) => HelpWords.Contains(arg.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <param name="command">Name des Befehls für Meldungen.</param>
    /// <param name="args">Argumente nach dem Befehl.</param>
    /// <param name="options">Erlaubte Optionen.</param>
    /// <param name="maxPositional">Höchstzahl freier Argumente (int.MaxValue = beliebig viele).</param>
    public static ParsedArguments Parse(string command, IReadOnlyList<string> args, IReadOnlyList<OptionSpec> options, int maxPositional = 0)
    {
        var parsed = new ParsedArguments(command);
        for (int i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!IsOptionLike(arg))
            {
                if (parsed.Positional.Count >= maxPositional) throw RglCodesCli.Error(RglCodesCli.UsageError, arg);
                parsed.Positional.Add(arg);
                continue;
            }

            // --name=wert
            string name = arg;
            string? inline = null;
            int equals = arg.IndexOf('=', StringComparison.Ordinal);
            if (equals > 2)
            {
                name = arg[..equals];
                inline = arg[(equals + 1)..];
            }
            var spec = options.FirstOrDefault(o => o.Matches(name)) ?? throw RglCodesCli.Error(RglCodesCli.UsageError, arg);
            switch (spec.Kind)
            {
                case OptionKind.Flag:
                    if (inline is not null) throw RglCodesCli.Error(RglCodesCli.UsageError, arg);
                    parsed.AddFlag(spec.Name);
                    break;

                case OptionKind.Value:
                    if (parsed.Has(spec.Name))
                        throw new Core.Model.RiegelException(RglCodesCli.ConflictingOptions,
                            string.Format(CultureInfo.CurrentCulture, Resources.CliStrings.Cli_OptionTwice, spec.Name));
                    if (inline is null)
                    {
                        if (i + 1 >= args.Count || IsOptionLike(args[i + 1])) throw RglCodesCli.Error(RglCodesCli.MissingValue, spec.Name);
                        inline = args[++i];
                    }
                    parsed.AddValue(spec.Name, inline);
                    break;

                case OptionKind.List:
                    int added = 0;
                    if (inline is not null) added += Add(parsed, spec, inline);
                    else
                    {
                        while (i + 1 < args.Count && !IsOptionLike(args[i + 1]))
                            added += Add(parsed, spec, args[++i]);
                    }
                    if (added == 0) throw RglCodesCli.Error(RglCodesCli.MissingValue, spec.Name);
                    break;
            }
        }
        return parsed;
    }

    private static int Add(ParsedArguments parsed, OptionSpec spec, string value)
    {
        if (!spec.SplitCommas)
        {
            if (value.Length == 0) return 0;
            parsed.AddValue(spec.Name, value);
            return 1;
        }
        int n = 0;
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            parsed.AddValue(spec.Name, part);
            n++;
        }
        return n;
    }

    /// <summary>
    /// Beginnt mit „--“ (oder ist ein Hilfewort). Einzelne Bindestriche bleiben Werte, damit z. B. Pfade wie „-alt“
    /// nicht als Option gelten.
    /// </summary>
    private static bool IsOptionLike(string arg) => arg.StartsWith("--", StringComparison.Ordinal) && arg.Length > 2;
}
