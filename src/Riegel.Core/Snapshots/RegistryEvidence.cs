// SPDX-License-Identifier: MIT
using Riegel.Core.Model;

namespace Riegel.Core.Snapshots;

/// <summary>Beleglage eines Registry-Schlüssels (spec 2.4 Punkt 1, auf Schlüsselebene).</summary>
internal enum KeyEvidence
{
    /// <summary>Eintrag Ok/ValueMissing, nicht abgeschnitten.</summary>
    Present,

    /// <summary>Nachweislich nicht vorhanden: KeyMissing oder kein Eintrag, und der Schlüssel liegt in einem abgedeckten,
    /// vollständigen, nicht verweigerten Bereich.</summary>
    NotSet,

    /// <summary>Alles andere (verweigert, Fehler, nicht erfasst, abgeschnitten, außerhalb der Abdeckung).</summary>
    Unavailable,
}

/// <summary>
/// Schlüssel-Belege aus Einträgen und Abdeckung eines Exports – dieselben Regeln wie in der Auswertung (spec 2.4,
/// docs/file-formats.md „Coverage and what missing means“). Die Leser brauchen sie, um abgeleitete Einträge
/// (Firewall-Regeln aus der Registry) nur aus belastbaren Daten zu bilden.
/// </summary>
internal sealed class RegistryEvidence
{
    private readonly Dictionary<string, SnapshotItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<CoverageEntry> _coverage;

    internal RegistryEvidence(Snapshot snapshot)
    {
        foreach (var item in snapshot.Items)
            if (item.Key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase)) _items.TryAdd(Normalize(item.Key[4..]), item);
        _coverage = snapshot.Coverage;
    }

    /// <summary>Eintrag zu einem Schlüssel (<c>HKLM\…</c>), falls vorhanden.</summary>
    internal SnapshotItem? Item(string keyPath) => _items.TryGetValue(Normalize(keyPath), out var item) ? item : null;

    /// <summary>Beleglage des Schlüssels.</summary>
    internal KeyEvidence Classify(string keyPath, out SnapshotItem? item)
    {
        item = Item(keyPath);
        if (item is not null)
        {
            if (item.Truncated) return KeyEvidence.Unavailable;
            if (item.Status is ItemStatus.Ok or ItemStatus.ValueMissing) return KeyEvidence.Present;
            if (item.Status != ItemStatus.KeyMissing) return KeyEvidence.Unavailable;
        }
        return Covered(keyPath) ? KeyEvidence.NotSet : KeyEvidence.Unavailable;
    }

    /// <summary>Liegt der Schlüssel in einem vollständigen, nicht verweigerten Bereich der Abdeckung?</summary>
    internal bool Covered(string keyPath)
    {
        var path = Normalize(keyPath);
        foreach (var entry in _coverage)
        {
            var root = Normalize(entry.Root);
            int depth;
            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) depth = 0;
            else if (entry.Recursive && path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) depth = Depth(path) - Depth(root);
            else continue;
            if (depth > entry.MaxDepth || !entry.Complete) continue;
            if (entry.DeniedSubkeys.Any(d => IsSameOrBelow(path, Normalize(d)))) continue;
            return true;
        }
        return false;
    }

    private static bool IsSameOrBelow(string path, string ancestor) =>
        string.Equals(path, ancestor, StringComparison.OrdinalIgnoreCase) || path.StartsWith(ancestor + "\\", StringComparison.OrdinalIgnoreCase);

    private static int Depth(string path) => path.Count(c => c == '\\');

    /// <summary>„HKLM\…“ ohne „\“ am Ende; „reg:“ wird vorher abgeschnitten.</summary>
    internal static string Normalize(string keyPath)
    {
        var k = keyPath.StartsWith("reg:", StringComparison.OrdinalIgnoreCase) ? keyPath[4..] : keyPath;
        return k.TrimEnd('\\');
    }
}
