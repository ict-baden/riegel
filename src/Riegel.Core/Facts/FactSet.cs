// SPDX-License-Identifier: MIT
using System.Collections.Immutable;
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Core.Facts;

/// <summary>
/// Abgeleitete Fakten mit ihren Belegen: welche Einträge ein Fakt gelesen hat, ob er hergeleitet (Flag Derived) oder
/// heuristisch (Flag Heuristic) ist. Die öffentliche Schnittstelle <see cref="Services.IFactsDeriver"/> gibt nur die
/// Werte weiter; die Engine braucht auch die Herkunft.
/// </summary>
internal sealed class FactSet
{
    private static readonly ImmutableHashSet<string> NoKeys = ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

    public SortedDictionary<string, JsonElement> Values { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Derived { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Heuristic { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, ImmutableHashSet<string>> Sources { get; } = new(StringComparer.Ordinal);

    /// <summary>Letzter Start (Kopf, sonst Win32_OperatingSystem.LastBootUpTime) – für ClockSuspect.</summary>
    public DateTimeOffset? LastBootUtc { get; set; }

    /// <summary>Ist der Fakt unbekannt? Fehlend, JSON null oder (nur defender.mode) „unknown“.</summary>
    public bool IsUnknown(string fact)
    {
        if (!Values.TryGetValue(fact, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return true;
        return fact == FactNames.DefenderMode && v.ValueKind == JsonValueKind.String && v.GetString() == FactNames.Defender.Unknown;
    }

    public JsonElement? Get(string fact) => IsUnknown(fact) ? null : Values[fact];

    public string? Text(string fact) => Get(fact) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    public bool? Bool(string fact) => Get(fact) switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        _ => null,
    };

    public long? Number(string fact) => Get(fact) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt64(out var n) ? n : null;

    /// <summary>Belege eines Fakts (leer bei Kopf-Feldern oder Überschreibung).</summary>
    public ImmutableHashSet<string> SourcesOf(string fact) => Sources.TryGetValue(fact, out var s) ? s : NoKeys;

    /// <summary>Werte aus Testvektoren überschreiben die abgeleiteten (spec 2.3 „facts“); Herkunftsmarken entfallen.</summary>
    public void Override(IReadOnlyDictionary<string, JsonElement>? overrides)
    {
        if (overrides is null) return;
        foreach (var (fact, value) in overrides)
        {
            Values[fact] = value.Clone();
            Derived.Remove(fact);
            Heuristic.Remove(fact);
            Sources.Remove(fact);
        }
    }

    /// <summary>Nur die Werte, sortiert (für Run.Facts und den öffentlichen Ableiter).</summary>
    public IReadOnlyDictionary<string, JsonElement> ToDictionary() =>
        new SortedDictionary<string, JsonElement>(Values, StringComparer.Ordinal);

    // ---------------------------------------------------------------- Sicht auf den Rechner (aus den Fakten)

    public string? Kind => Text(FactNames.OsKind);
    public int? Build => Number(FactNames.OsBuild) is { } b && b is >= 0 and <= int.MaxValue ? (int)b : null;
    public int? Ubr => Number(FactNames.OsUbr) is { } u && u is >= 0 and <= int.MaxValue ? (int)u : null;
    public string? Edition => Text(FactNames.OsEdition);
    public string? InstallationType => Text(FactNames.OsInstallationType);
    public bool? DomainJoined => Bool(FactNames.DomainJoined);
    public bool? IsDc => Bool(FactNames.DomainIsDc);
}
