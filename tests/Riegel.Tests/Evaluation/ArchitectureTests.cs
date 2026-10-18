// SPDX-License-Identifier: MIT
using Riegel.Core.Evaluation;
using Riegel.Core.Model;
using Riegel.Core.Services;
using Riegel.Tests.RuleVectors;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Evaluation;

/// <summary>
/// 32-Bit-Windows hat keine Ansicht HKLM\SOFTWARE\WOW6432Node (Anfrage rules-c): Teile, die dort lesen, sind nicht
/// anwendbar statt „fehlt“ – TLS-09 wird auf Windows 7 x86 nicht prüfbar statt fälschlich nicht erfüllt, auf x64
/// bleibt der fehlende Wert ein Befund.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Lazy<ICatalogue> Catalogue = new(() => RiegelServices.Create(new NullPlatform(), new FixedClock()).Catalogue.LoadEmbedded());

    private static RuleResult Tls09(string architecture)
    {
        var snapshot = SnapshotBuilder.Windows7("W7-X86")
            .Header(m => m with { Architecture = architecture })
            .RegistryValue(@"HKLM\SOFTWARE\Microsoft\.NETFramework\v4.0.30319", "SchUseStrongCrypto", RegValueType.Dword, 1)
            .RegistryValue(@"HKLM\SOFTWARE\Microsoft\.NETFramework\v2.0.50727", "SchUseStrongCrypto", RegValueType.Dword, 1)
            .RegistryKeyMissing(@"HKLM\SOFTWARE\WOW6432Node\Microsoft\.NETFramework\v4.0.30319")
            .RegistryKeyMissing(@"HKLM\SOFTWARE\WOW6432Node\Microsoft\.NETFramework\v2.0.50727")
            .Build();
        return new Evaluator(null).Evaluate(snapshot, Catalogue.Value, VectorRunner.Context, null, [Catalogue.Value.Find("TLS-09")!]).Single();
    }

    [Fact]
    public void Wow6432Node_auf_32_Bit_Windows_ist_kein_Befund()
    {
        var result = Tls09("x86");
        Assert.NotEqual(ResultStatus.Fail, result.Status);
        Assert.NotEqual(ResultStatus.Pass, result.Status);
    }

    [Fact]
    public void Wow6432Node_auf_64_Bit_Windows_fehlt_nachweislich() => Assert.Equal(ResultStatus.Fail, Tls09("AMD64").Status);
}
