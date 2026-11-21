// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Riegel.Tests.TestSupport;

/// <summary>
/// Bausteine der Seite docs/error-codes.md (spec 7.13): die Codes einer Klasse RglCodes* mit dem Namen ihrer Konstante,
/// ihr englischer Text aus der Sprachdatei des Pakets und die Tabellenzeile. Die ganze Seite erzeugt und vergleicht
/// <c>ErrorCodesDocTests</c> (nur unter Windows, weil Sammler, Windows-Anbindung und App dazugehören); die Bereiche von
/// Core und Befehlen prüft <c>ErrorCodesCoreDocTests</c> auch unter Linux.
/// </summary>
public static class ErrorCodeTable
{
    /// <summary>Englischer Text eines Codes (Schlüssel <c>Error_RGLnnnn</c>) aus einer Sprachdatei.</summary>
    public static string? Text(Assembly assembly, string baseName, string code) =>
        new ResourceManager(baseName, assembly).GetString("Error_" + code.Replace("-", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);

    /// <summary>Alle Codes eines Bereichs mit dem Namen ihrer Konstante (Reihenfolge der Liste All).</summary>
    public static List<(string Code, string Name)> Codes(Type type)
    {
        var names = type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .GroupBy(f => (string)f.GetRawConstantValue()!).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        var member = (MemberInfo?)type.GetProperty("All", BindingFlags.Public | BindingFlags.Static) ?? type.GetField("All", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(type.Name + ": All fehlt");
        var all = (IEnumerable<string>)(member is PropertyInfo p ? p.GetValue(null)! : ((FieldInfo)member).GetValue(null)!);
        return [.. all.Select(c => (c, names.TryGetValue(c, out var n) ? n : "?"))];
    }

    /// <summary>Tabellenzeile der Seite (mit Zeilenende).</summary>
    public static string Row(string code, string name, string? text)
    {
        // Spitze Klammern als Entität: GitHub hält <Ordner> sonst für ein HTML-Element und blendet es aus
        var cell = (text ?? "").Replace("\r\n", " ").Replace('\n', ' ').Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Trim();
        return $"| `{code}` | {name} | {cell} |\n";
    }
}
