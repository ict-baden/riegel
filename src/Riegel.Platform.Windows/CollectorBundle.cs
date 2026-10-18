// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using Riegel.Collector.Collection;
using Riegel.Core.About;
using Riegel.Core.Model;
using Riegel.Platform.Windows.Resources;

namespace Riegel.Platform.Windows;

/// <summary>
/// „Sammler auf Datenträger kopieren“ (O15): kopiert <c>&lt;App&gt;\collector\*</c> in den Zielordner und schreibt
/// <c>UEBERGABE.txt</c> mit Version, Prüfsummen, Datum, Aufruf und dem, was der Sammler hinterlässt. Vorhandene
/// Dateien werden nie überschrieben.
/// </summary>
internal static class CollectorBundle
{
    public const string HandoverFile = "UEBERGABE.txt";

    public static string Export(string sourceDir, string targetDir, DateTime nowUtc)
    {
        if (!Directory.Exists(sourceDir) || !File.Exists(Path.Combine(sourceDir, "Riegel.Collector.exe")))
            throw new RiegelException(RglCodesCollector.CollectorBundleMissing, WindowsPlatform.Message(RglCodesCollector.CollectorBundleMissing, sourceDir));
        string target = Path.GetFullPath(targetDir);
        var files = Directory.GetFiles(sourceDir).OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var name in files.Select(Path.GetFileName).Append(HandoverFile))
            if (File.Exists(Path.Combine(target, name!)))
                throw new RiegelException(RglCodesCollector.CollectorBundleTargetExists, WindowsPlatform.Message(RglCodesCollector.CollectorBundleTargetExists, Path.Combine(target, name!)));
        Directory.CreateDirectory(target);
        var hashes = new List<(string Name, string Sha256)>();
        foreach (var file in files)
        {
            string destination = Path.Combine(target, Path.GetFileName(file));
            File.Copy(file, destination, overwrite: false);
            hashes.Add((Path.GetFileName(file), Hashes.Sha256File(destination)));
        }
        string text = Handover(CollectorVersion(sourceDir), hashes, nowUtc, CultureInfo.CurrentUICulture);
        // Für den alten Editor auf Windows 7/XP: UTF-8 mit BOM und CRLF
        using (var stream = new FileStream(Path.Combine(target, HandoverFile), FileMode.CreateNew, FileAccess.Write))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(true)))
            writer.Write(text.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        return target;
    }

    /// <summary>Version des kopierten Sammlers (Dateiversion der Exe), sonst die zum Build gehörende.</summary>
    private static string CollectorVersion(string sourceDir)
    {
        var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(sourceDir, "Riegel.Collector.exe"));
        return string.IsNullOrEmpty(info.ProductVersion) ? AppInfo.CollectorVersion : info.ProductVersion;
    }

    /// <summary>Text des Übergabeblatts in der Sprache der Oberfläche.</summary>
    internal static string Handover(string version, IReadOnlyList<(string Name, string Sha256)> files, DateTime nowUtc, CultureInfo culture)
    {
        string T(string key) => PlatformStrings.ResourceManager.GetString(key, culture) ?? key;
        var sb = new StringBuilder();
        sb.AppendLine(T("Handover_Title"));
        sb.AppendLine(new string('=', T("Handover_Title").Length));
        sb.AppendLine();
        sb.AppendLine(string.Format(culture, T("Handover_Version"), version));
        sb.AppendLine(string.Format(culture, T("Handover_Copied"), nowUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"));
        sb.AppendLine();
        sb.AppendLine(T("Handover_Files"));
        foreach (var (name, sha256) in files) sb.AppendLine($"  {sha256}  {name}");
        sb.AppendLine();
        sb.AppendLine(T("Handover_Check"));
        sb.AppendLine();
        sb.AppendLine(T("Handover_HowTo"));
        sb.AppendLine();
        sb.AppendLine(T("Handover_LeavesBehind"));
        sb.AppendLine();
        sb.AppendLine(T("Handover_Record"));
        return sb.ToString();
    }
}
