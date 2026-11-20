// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Collector.Collection;
using Riegel.Core.Model;
using Riegel.Platform.Windows.Resources;

namespace Riegel.Platform.Windows;

/// <summary>
/// Übergabeordner der Rechteerhöhung (spec 7.6): die erhöhte Seite legt <c>&lt;handoff&gt;\&lt;token&gt;\</c>
/// selbst an und lehnt einen vorhandenen Ordner und jeden Reparse-Punkt im Pfad ab; die nicht erhöhte Seite liest
/// danach genau einen Export (mit Begleitdatei) daraus.
/// </summary>
internal static class Handoff
{
    /// <summary>Kennzeichen: Buchstaben, Ziffern und „-“, 8 bis 64 Zeichen (kein Pfadteil).</summary>
    public static void ValidateToken(string? token)
    {
        bool ok = token is { Length: >= 8 and <= 64 } && token.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-');
        if (!ok) throw new RiegelException(RglCodesCollector.HandoffInvalidToken, WindowsPlatform.Message(RglCodesCollector.HandoffInvalidToken));
    }

    /// <summary>Legt <paramref name="root"/>\<paramref name="token"/> an und gibt den Pfad zurück.</summary>
    public static string CreateDirectory(string root, string token)
    {
        ValidateToken(token);
        string full = Path.GetFullPath(Path.Combine(root, token));
        RefuseReparsePoints(full);
        if (Directory.Exists(full) || File.Exists(full))
            throw new RiegelException(RglCodesCollector.HandoffExists, WindowsPlatform.Message(RglCodesCollector.HandoffExists, full));
        Directory.CreateDirectory(Path.GetFullPath(root));
        RefuseReparsePoints(full);
        var created = Directory.CreateDirectory(full);
        // Erneut prüfen: zwischen Prüfen und Anlegen darf niemand eine Verknüpfung untergeschoben haben
        if ((created.Attributes & FileAttributes.ReparsePoint) != 0) throw ReparsePoint(full);
        RefuseReparsePoints(full);
        return full;
    }

    /// <summary>Jeder vorhandene Teil des Pfads (vom Laufwerk an) darf kein Reparse-Punkt sein.</summary>
    internal static void RefuseReparsePoints(string fullPath)
    {
        string? current = fullPath;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(current) || File.Exists(current))
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw ReparsePoint(current);
            }
            current = Path.GetDirectoryName(current);
        }
    }

    private static RiegelException ReparsePoint(string path) =>
        new(RglCodesCollector.HandoffReparsePoint, WindowsPlatform.Message(RglCodesCollector.HandoffReparsePoint, path));

    /// <summary>Fehlerdatei des erhöhten Prozesses (geschrieben von collect --handoff): eine Zeile mit dem RGL-Code.</summary>
    internal const string ErrorFile = "error.txt";

    /// <summary>
    /// Ergebnis nach dem Ende des erhöhten Prozesses. Fehlt der Ordner, obwohl der Prozess erfolgreich endete, lief er
    /// mit einem anderen Konto (anderes Profil, „über die Schulter“). Ohne Export nennt die Meldung den Exitcode und,
    /// wenn der erhöhte Prozess ihn hinterlegt hat, den Code der eigentlichen Ursache.
    /// </summary>
    public static HandoffResult Evaluate(string root, string token, int exitCode)
    {
        string dir = Path.Combine(root, token);
        if (!Directory.Exists(dir))
        {
            bool otherProfile = exitCode is 0 or 1;
            return otherProfile
                ? new HandoffResult(false, null, exitCode, RglCodesCollector.HandoffDifferentProfile, WindowsPlatform.Message(RglCodesCollector.HandoffDifferentProfile), true)
                : NoSnapshot(exitCode, null);
        }
        var exports = Directory.GetFiles(dir, "*" + SnapshotFile.Extension).Concat(Directory.GetFiles(dir, "*" + SnapshotFile.GzipExtension)).ToList();
        if (exports.Count != 1 || !File.Exists(SnapshotFile.SidecarPath(exports[0])))
            return NoSnapshot(exitCode, exports.Count == 0 ? ReadCause(dir) : null);
        return new HandoffResult(true, exports[0], exitCode, null, null, false);
    }

    private static HandoffResult NoSnapshot(int exitCode, string? cause)
    {
        string message = WindowsPlatform.Message(RglCodesCollector.HandoffNoSnapshot, exitCode);
        if (cause is not null && PlatformStrings.ResourceManager.GetString("Handoff_Cause", CultureInfo.CurrentUICulture) is { } text)
            message += " " + string.Format(CultureInfo.CurrentCulture, text, cause);
        return new HandoffResult(false, null, exitCode, RglCodesCollector.HandoffNoSnapshot, message, false);
    }

    /// <summary>
    /// Code aus der Fehlerdatei – streng gelesen: höchstens 64 Bytes, genau „RGL-nnnn“. Den Text dazu liefert die eigene
    /// Oberfläche, nie die Datei (der Ordner stammt vom erhöhten Prozess).
    /// </summary>
    internal static string? ReadCause(string dir)
    {
        try
        {
            var file = new FileInfo(Path.Combine(dir, ErrorFile));
            if (!file.Exists || file.Length > 64 || (file.Attributes & FileAttributes.ReparsePoint) != 0) return null;
            var code = File.ReadAllText(file.FullName).Trim();
            return code.Length == 8 && code.StartsWith("RGL-", StringComparison.Ordinal) && code[4..].All(char.IsAsciiDigit) ? code : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
