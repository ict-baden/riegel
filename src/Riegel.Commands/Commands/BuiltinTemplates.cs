// SPDX-License-Identifier: MIT
using Riegel.Core.Model;

namespace Riegel.Commands.Commands;

/// <summary>
/// Die in Riegel.Core eingebetteten Vorlagen (templates/tailoring/*.tailoring-template.json,
/// templates/exceptions/*.exceptions-template.json) über ihren Namen, z. B. <c>hmi</c> oder <c>jump-host</c>.
/// </summary>
internal static class BuiltinTemplates
{
    private static (string Folder, string Suffix) Layout(RegisterKind kind) => kind == RegisterKind.Exceptions
        ? ("templates/exceptions/", ".exceptions-template.json")
        : ("templates/tailoring/", ".tailoring-template.json");

    private static IEnumerable<string> Resources(RegisterKind kind)
    {
        var (folder, suffix) = Layout(kind);
        return typeof(Snapshot).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(folder, StringComparison.Ordinal) && n.EndsWith(suffix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
    }

    /// <summary>Namen der eingebauten Vorlagen dieser Art.</summary>
    public static IReadOnlyList<string> Names(RegisterKind kind)
    {
        var (folder, suffix) = Layout(kind);
        return [.. Resources(kind).Select(n => n[folder.Length..^suffix.Length])];
    }

    /// <summary>Ressourcenname der Vorlage <paramref name="name"/> (ohne Groß/klein), null wenn es sie nicht gibt.</summary>
    public static string? Find(RegisterKind kind, string name)
    {
        var (folder, suffix) = Layout(kind);
        var wanted = name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? name[..^suffix.Length] : name;
        return Resources(kind).FirstOrDefault(n => string.Equals(n[folder.Length..^suffix.Length], wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Vorlage in <paramref name="folder"/> schreiben (der Register-Dienst liest Vorlagen aus Dateien).</summary>
    public static string Extract(string resource, string folder)
    {
        var path = Path.Combine(folder, resource[(resource.LastIndexOf('/') + 1)..]);
        using var source = typeof(Snapshot).Assembly.GetManifestResourceStream(resource)
                           ?? throw RiegelException.Common(RglCodesCommon.NotFound, resource);
        using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(target);
        return path;
    }
}
