// SPDX-License-Identifier: MIT
using Riegel.Core.Model;

namespace Riegel.Core.Facts;

/// <summary>
/// Hat dieses Betriebssystem eine Fähigkeit (rules/capabilities, spec 2.5)? Eine Fähigkeit existiert, wenn ein
/// Eintrag von <c>exists</c> passt (alle seine Angaben gelten). Fehlt ein Kopf-Feld, das ein Eintrag braucht, und
/// passt kein anderer sicher, ist das Ergebnis unbekannt (null).
/// </summary>
internal static class CapabilityMatcher
{
    public static bool? Exists(CapabilityEntry entry, string? kind, int? build, string? edition, string? installationType)
    {
        bool unknown = false;
        foreach (var condition in entry.Exists)
        {
            var match = Matches(condition, kind, build, edition, installationType);
            if (match == true) return true;
            if (match is null) unknown = true;
        }
        return unknown ? null : false;
    }

    private static bool? Matches(CapabilityCondition c, string? kind, int? build, string? edition, string? installationType)
    {
        bool unknown = false;
        if (c.Os is not null)
        {
            if (kind is null) unknown = true;
            else if (!string.Equals(c.Os, kind, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (c.MinBuild is not null || c.MaxBuild is not null)
        {
            if (build is null) unknown = true;
            else if ((c.MinBuild is { } min && build < min) || (c.MaxBuild is { } max && build > max)) return false;
        }
        if (c.Editions is { Count: > 0 } editions)
        {
            if (edition is null) unknown = true;
            else if (!editions.Contains(edition, StringComparer.OrdinalIgnoreCase)) return false;
        }
        if (c.InstallationTypes is { Count: > 0 } types)
        {
            if (installationType is null) unknown = true;
            else if (!types.Contains(installationType, StringComparer.OrdinalIgnoreCase)) return false;
        }
        return unknown ? null : true;
    }
}
