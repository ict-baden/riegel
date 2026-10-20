// SPDX-License-Identifier: MIT
using Riegel.Core.Plants;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Plants;

/// <summary>
/// Ordner beim Import (Prüfung vom 2026-10-20): Befehlszeile und Oberfläche behandeln einen Ordner mit Exporten gleich –
/// er steht für die Exporte darin; ein Bordmittel-Paket bleibt ein Paket.
/// </summary>
public sealed class ImportPathsTests
{
    [Fact]
    public void Ordner_mit_Exporten_steht_fuer_die_Exporte_darin()
    {
        using var temp = new TempFolder();
        var share = temp.Sub("freigabe");
        var b = SnapshotBuilder.Windows10Ltsc2019("HMI-B").WriteTo(share);
        var a = SnapshotBuilder.Windows10Ltsc2019("HMI-A").WriteTo(share);
        File.WriteAllText(Path.Combine(share, "notiz.txt"), "x");
        var bundle = temp.Sub("PG-ALT-01_20261010.riegel-builtin");
        var withSecedit = temp.Sub("bordmittel");
        File.WriteAllText(Path.Combine(withSecedit, "secedit.inf"), "[Unicode]");
        var withHostname = temp.Sub("bordmittel2");
        File.WriteAllText(Path.Combine(withHostname, "hostname.txt"), "PG");
        var empty = temp.Sub("leer");

        Assert.True(ImportPaths.IsBuiltinBundle(bundle));
        Assert.True(ImportPaths.IsBuiltinBundle(withSecedit));
        Assert.True(ImportPaths.IsBuiltinBundle(withHostname));
        Assert.False(ImportPaths.IsBuiltinBundle(share));
        Assert.Equal([a, b], ImportPaths.ExportsIn(share).Order(StringComparer.Ordinal));
        Assert.Equal([.. new[] { a, b }.Order(StringComparer.Ordinal), bundle, withSecedit], ImportPaths.Expand([share, bundle, withSecedit, empty]));
    }
}
