// SPDX-License-Identifier: MIT
using System.Diagnostics;
using Riegel.Core.Model;
using Riegel.Core.Services;

namespace Riegel.Session;

/// <summary>
/// Öffnet Links und von Riegel erzeugte Dateien oder Ordner mit der Shell (spec 7.12): Links nur http/https, nie
/// Dateien aus fremden Daten. Zuerst über <see cref="IPlatform.OpenWithShell"/>; solange die Windows-Anbindung das noch
/// nicht kann (RGL-0003, Platzhalter des Pakets collector), direkt über den Explorer bzw. Browser.
/// </summary>
internal static class Shell
{
    /// <summary>Nur absolute http(s)-Adressen mit Rechnernamen (aus Zutritt).</summary>
    public static bool IsAllowedLink(string? url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp) return false;
        if (string.IsNullOrEmpty(parsed.Host) || parsed.IsUnc || parsed.IsFile) return false;
        uri = parsed;
        return true;
    }

    /// <summary>Link im Browser öffnen; null bei Erfolg, sonst eine Meldung mit Code.</summary>
    public static string? OpenLink(IPlatform platform, string url)
    {
        if (!IsAllowedLink(url, out var uri)) return RglCodesApp.Message(RglCodesApp.OpenFailed, url);
        return Open(platform, uri!.AbsoluteUri, () => new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    /// <summary>Ordner (oder die Datei markiert im Ordner) im Explorer zeigen.</summary>
    public static string? ShowInFolder(IPlatform platform, string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return RglCodesApp.Message(RglCodesApp.OpenFailed, path);
        bool file = File.Exists(path);
        return Open(platform, file ? Path.GetDirectoryName(path)! : path,
            () => new ProcessStartInfo("explorer.exe", file ? $"/select,\"{path}\"" : $"\"{path}\"") { UseShellExecute = true });
    }

    private static string? Open(IPlatform platform, string target, Func<ProcessStartInfo> fallback)
    {
        try
        {
            platform.OpenWithShell(target);
            return null;
        }
        catch (RiegelException e) when (e.Code is RglCodesCommon.NotImplemented or RglCodesCommon.PlatformNotSupported)
        {
            // Platzhalter der Windows-Anbindung: selbst öffnen
        }
        catch (RiegelException e)
        {
            return e.ToString();
        }
        try
        {
            using var _ = Process.Start(fallback());
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return RglCodesApp.Message(RglCodesApp.OpenFailed, target);
        }
    }
}
