// SPDX-License-Identifier: MIT
using Riegel.Core.Diagnostics;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Infrastructure;

/// <summary>
/// Regressionstest (Review legal-6): das Protokoll unter %LocalAppData%\Riegel\logs enthält nie Pfade, Rechner-,
/// Benutzer- oder Domänennamen (docs/data-and-privacy.md). Vorher schrieb AppSession.Fail die Meldung einer
/// IOException unverändert – .NET setzt dort den vollen Pfad hinein.
/// </summary>
public sealed class LogPrivacyTests
{
    [Theory]
    [InlineData(@"Could not find file 'C:\Users\Musterperson\Documents\Riegel\exceptions.json'.", "Musterperson")]
    [InlineData(@"Access to the path 'D:\Riegel\Beispielkunde Werk\Linie3\exceptions.json' is denied.", "Beispielkunde")]
    [InlineData(@"Could not find a part of the path '\\fileserver\anlagen\Linie3\runs'.", "fileserver")]
    [InlineData("Could not find file '/home/musterperson/riegel/plant/riegel-plant.json'.", "musterperson")]
    [InlineData(@"The process cannot access the file D:\Anlagen\Linie3\measures.json because it is being used", "Linie3")]
    public void Pfade_aus_Ausnahmemeldungen_kommen_nicht_ins_Protokoll(string message, string secret)
    {
        var cleaned = Log.Clean(message);
        Assert.DoesNotContain(secret, cleaned, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rechner_und_Benutzername_werden_ersetzt()
    {
        var cleaned = Log.Clean($"Fehler auf {Environment.MachineName} für {Environment.UserName}");
        if (Environment.MachineName.Length >= 2) Assert.DoesNotContain(Environment.MachineName, cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Fehler auf ", cleaned, StringComparison.Ordinal);
    }

    [Fact]
    public void Codes_und_Typen_bleiben_erhalten()
    {
        Assert.Equal("Fehler in der Oberfläche: System.InvalidOperationException: RGL-0415",
            Log.Clean("Fehler in der Oberfläche: System.InvalidOperationException: RGL-0415"));
        Assert.Equal("Import: 3 Datei(en)", Log.Clean("Import: 3 Datei(en)"));
    }

    [Fact]
    public void Log_Error_schreibt_die_bereinigte_Meldung()
    {
        using var temp = new TempFolder();
        var marker = "Prüfmarke-" + Guid.NewGuid().ToString("N")[..8];
        var previous = Log.Folder;
        try
        {
            Log.Start(temp.Path);
            Log.Error(marker, new IOException(@"Could not find file 'C:\Users\Musterperson\x.json'."));
        }
        finally
        {
            if (previous is null) Log.Stop(); else Log.Start(previous);
        }
        var text = string.Join("\n", Directory.GetFiles(temp.Path).Select(File.ReadAllText));
        var line = text.Split('\n').Single(l => l.Contains(marker, StringComparison.Ordinal));
        Assert.Contains("System.IO.IOException", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Musterperson", line, StringComparison.Ordinal);
    }
}
