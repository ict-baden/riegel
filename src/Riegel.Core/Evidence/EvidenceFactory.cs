// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Services;

namespace Riegel.Core.Evidence;

/// <summary>
/// Factory des Nachweispakets (Paket data-outputs; Signatur fest): Schreiben (O9), <c>verify</c> und die Stichprobe
/// <c>compare-snapshot</c>.
/// </summary>
public static class EvidenceFactory
{
    public static IEvidencePack Create(RiegelServices services) => new EvidencePack(services, () => services.Catalogue.LoadEmbedded());
}

/// <summary>Nachweispaket mit dem eingebetteten Katalog (Tests geben einen eigenen Katalog vor).</summary>
internal sealed class EvidencePack(RiegelServices services, Func<ICatalogue> catalogue) : IEvidencePack
{
    private readonly EvidenceWriter _writer = new(services, catalogue);
    private readonly EvidenceVerifier _verifier = new(services);

    public string Write(EvidenceRequest r) => _writer.Write(r);

    public VerifyResult Verify(string zipPath, ReadLimits l) => _verifier.Verify(zipPath, l);

    public SampleResult CompareSample(string freshSnapshotPath, string zipPath, string? host, ReadLimits l) =>
        _verifier.CompareSample(freshSnapshotPath, zipPath, host, l);
}
