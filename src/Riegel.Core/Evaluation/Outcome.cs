// SPDX-License-Identifier: MIT
using System.Collections.Immutable;
using System.Globalization;
using Riegel.Core.Model;
using Riegel.Core.Resources;

namespace Riegel.Core.Evaluation;

/// <summary>
/// Ergebnis einer (Teil-)Prüfung, bevor es zum <see cref="RuleResult"/> wird. <see cref="Deciding"/> sind die
/// Belege, auf denen die Entscheidung beruht (für den Mutationstest: ein Beleg, der nicht verfügbar gemacht wurde,
/// darf nie ein „erfüllt“ tragen).
/// </summary>
internal sealed record Outcome(ResultStatus Status, string? ReasonCode = null)
{
    public ResultFlags Flags { get; init; }
    public string? Expected { get; init; }
    public string? Actual { get; init; }
    public ValueSource Source { get; init; } = ValueSource.Unknown;
    public string? SourceDetail { get; init; }
    public ImmutableList<string> Details { get; init; } = [];
    public ImmutableHashSet<string> Deciding { get; init; } = ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// true: die Prüfart hat ihre entscheidenden Belege selbst benannt (Vorrang mehrerer Quellen). Sonst gelten alle
    /// Belege, die das Blatt gelesen hat, als entscheidend.
    /// </summary>
    public bool ExplicitDeciding { get; init; }

    public bool IsDecisive => Status is ResultStatus.Pass or ResultStatus.Fail;

    public static Outcome Pass() => new(ResultStatus.Pass);

    public static Outcome Fail() => new(ResultStatus.Fail);

    public static Outcome NotCheckable(string reason) => new(ResultStatus.NotCheckable, reason);

    public static Outcome NotApplicable(string reason) => new(ResultStatus.NotApplicable, reason);

    public static Outcome Info() => new(ResultStatus.Info);

    public Outcome With(string? detail) => detail is null ? this : this with { Details = Details.Add(detail) };

    public Outcome WithFlags(ResultFlags flags) => this with { Flags = Flags | flags };

    public Outcome WithDeciding(IEnumerable<string> keys) => this with { Deciding = Deciding.Union(keys) };

    /// <summary>Nur diese Belege (und die schon gesetzten, z. B. aus der Defender-Sperre) tragen die Entscheidung.</summary>
    public Outcome DecidedBy(params IEnumerable<string?> keys) =>
        this with { Deciding = Deciding.Union(keys.OfType<string>()), ExplicitDeciding = true };

    /// <summary>Vertauscht pass und fail (not); alles andere bleibt.</summary>
    public Outcome Negate() => Status switch
    {
        ResultStatus.Pass => this with { Status = ResultStatus.Fail },
        ResultStatus.Fail => this with { Status = ResultStatus.Pass },
        _ => this,
    };
}

/// <summary>Texte der Engine (EngineStrings) in der Sprache der Auswertung.</summary>
internal sealed class EngineText(CultureInfo culture)
{
    public CultureInfo Culture { get; } = culture;

    public static EngineText For(string? language)
    {
        CultureInfo culture;
        try { culture = string.IsNullOrWhiteSpace(language) ? CultureInfo.GetCultureInfo("en") : CultureInfo.GetCultureInfo(language); }
        catch (CultureNotFoundException) { culture = CultureInfo.GetCultureInfo("en"); }
        return new EngineText(culture);
    }

    /// <summary>Text mit Platzhaltern; fehlt der Schlüssel, der Schlüssel selbst (fällt in Tests auf).</summary>
    public string this[string key, params object?[] args]
    {
        get
        {
            var text = EngineStrings.ResourceManager.GetString(key, Culture) ?? key;
            return args.Length == 0 ? text : string.Format(Culture, text, args);
        }
    }

    /// <summary>Status eines Eintrags in Worten (CommonStrings Item_*).</summary>
    public string ItemStatus(ItemStatus? status) =>
        status is { } s ? CommonStrings.ResourceManager.GetString("Item_" + s, Culture) ?? s.ToString() : this["Detail_ItemAbsent"];

    /// <summary>„(nicht gesetzt)“ bzw. der Wert.</summary>
    public string Value(string? canonical) =>
        canonical ?? CommonStrings.ResourceManager.GetString("Value_NotSet", Culture) ?? "(not set)";
}
