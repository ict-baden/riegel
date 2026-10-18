// SPDX-License-Identifier: MIT
using System.Text;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;
using Riegel.Tests.Parsing;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Snapshots;

/// <summary>
/// Bordmittel-Paket mit <c>currentversion.txt</c> (reg query /v je Versionswert, collect-builtin.cmd des Sammlers 0.1.0) statt
/// eines rekursiven reg export, der Winlogon\DefaultPassword im Klartext enthielte (Anfrage collector/parsing):
/// Build, Edition und Installationstyp kommen aus genau diesem Schlüssel, Unterschlüssel bleiben unbekannt.
/// </summary>
public sealed class BuiltinCurrentVersionTests
{
    private const string Key = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    // Ausgabe von reg query /v je Wert, aneinandergehängt wie in collect-builtin.cmd (Pfadzeile, eingerückter Wert).
    // RegisteredOwner steht nur hier, um zu zeigen, dass der Leser außerhalb des Wertefilters nichts übernimmt.
    private static string Query(string name, string type, string value) =>
        $"\r\nHKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\r\n    {name}    {type}    {value}\r\n\r\n";

    private static readonly string QueryOutput =
        Query("CurrentBuild", "REG_SZ", "7601") + Query("CSDVersion", "REG_SZ", "Service Pack 1") +
        Query("EditionID", "REG_SZ", "Professional") + Query("InstallationType", "REG_SZ", "Client") +
        Query("ProductName", "REG_SZ", "Windows 7 Professional") + Query("RegisteredOwner", "REG_SZ", "Beispiel-Besitzer");

    [Fact]
    public void Currentversion_txt_liefert_Rechnerkopf_und_genau_einen_Schluessel()
    {
        using var temp = new TempFolder();
        var source = Path.Combine(RepoPaths.SyntheticFixtures, "PG-ALT-01_20261010.riegel-builtin");
        var bundle = Path.Combine(temp.Path, "PG-ALT-01_20261010.riegel-builtin");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(bundle, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        File.Delete(Path.Combine(bundle, "reg", "10-currentversion.reg"));
        File.WriteAllText(Path.Combine(bundle, "currentversion.txt"), QueryOutput, Encoding.ASCII);

        var s = RiegelServices.Create(new NullPlatform(), new FixedClock()).Snapshots.FromBuiltinBundle(bundle, ReadLimits.Default);
        Assert.Equal((7601, "Professional", "Client", 1), (s.Machine.Build!.Value, s.Machine.EditionId, s.Machine.InstallationType, s.Machine.ProductType!.Value));
        Assert.Contains(s.Coverage, c => c.Root == Key && !c.Recursive && c.MaxDepth == 0 && c.Complete);
        var item = s.Items.Single(i => ItemKeys.Same(i.Key, ItemKeys.Registry(Key)));
        Assert.Equal(ItemStatus.Ok, item.Status);
        Assert.Equal("7601", item.Value!.Value.GetProperty("values").GetProperty("CurrentBuild").GetProperty("v").GetString());
        Assert.False(item.Value!.Value.GetProperty("values").TryGetProperty("RegisteredOwner", out _));
        Assert.Contains("UBR", item.Value!.Value.GetProperty("valueFilter").EnumerateArray().Select(e => e.GetString()));
        // Winlogon kommt weiter nur aus winlogon.txt (ohne Kennwort), nie aus currentversion.txt
        Assert.DoesNotContain("DefaultPassword\":{\"t\":\"sz\",\"v\"", RiegelJson.Serialize(s), StringComparison.Ordinal);
        LocalSchema.AssertValid(RiegelJson.Serialize(s), "PG-ALT-01 mit currentversion.txt");
    }
}
