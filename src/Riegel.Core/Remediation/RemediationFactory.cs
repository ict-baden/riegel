// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using Riegel.Core.Model;
using Riegel.Core.Resources;
using Riegel.Core.Services;

namespace Riegel.Core.Remediation;

/// <summary>
/// Factory der Abhilfe-Dateien (gehört ab dem Gerüst dem Paket data-outputs; Signatur fest). Der Platzhalter legt
/// die fünf Dateien von O10 mit dem Pflichthinweis an, aber ohne Einstellungen. Riegel führt nie etwas davon aus.
/// </summary>
public static class RemediationFactory
{
    public static IRemediationWriter Create(RiegelServices services) => new StubRemediationWriter();
}

/// <summary>Platzhalter (Gerüst).</summary>
internal sealed class StubRemediationWriter : IRemediationWriter
{
    public RemediationOutput Write(Run r, Snapshot s, ICatalogue c, RegisterSet reg, string outDir, string lang)
    {
        var folder = Path.Combine(outDir, ReportFileNames.RemediationFolder(r.HostName, r.Id).Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(folder);
        var culture = CultureInfo.GetCultureInfo(Localization.UiLanguage.Normalize(lang) is { Length: > 0 } code ? code : "en");
        var header = CommonStrings.ResourceManager.GetString(nameof(CommonStrings.Remediation_Header), culture) ?? "";
        var files = new List<string>();
        foreach (var name in ReportFileNames.RemediationFiles)
        {
            var path = Path.Combine(folder, name);
            if (name.EndsWith(".reg", StringComparison.Ordinal))
                // .reg-Dateien schreibt Windows als UTF-16 LE mit BOM (Version 5.00)
                File.WriteAllText(path, "Windows Registry Editor Version 5.00\r\n\r\n; " + header + "\r\n", Encoding.Unicode);
            else if (name.EndsWith(".csv", StringComparison.Ordinal))
                File.WriteAllText(path, "# " + header + "\r\n", new UTF8Encoding(false));
            else
                File.WriteAllText(path, "; " + header + "\r\n", new UTF8Encoding(false));
            files.Add(path);
        }
        return new RemediationOutput(folder, files, [], [], []);
    }
}
