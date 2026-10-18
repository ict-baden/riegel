// SPDX-License-Identifier: MIT
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Riegel.Core.Model;
using Riegel.Core.Resources;
using Riegel.Core.Services;

namespace Riegel.Core.Evidence;

/// <summary>
/// Factory des Nachweispakets (gehört ab dem Gerüst dem Paket data-outputs; Signatur fest). Der Platzhalter schreibt
/// unter dem echten Namen eine ZIP mit README.txt; verify und die Stichprobe melden RGL-0003 (Exitcode 3).
/// </summary>
public static class EvidenceFactory
{
    public static IEvidencePack Create(RiegelServices services) => new StubEvidencePack();
}

/// <summary>Platzhalter (Gerüst).</summary>
internal sealed class StubEvidencePack : IEvidencePack
{
    public string Write(EvidenceRequest r)
    {
        Directory.CreateDirectory(r.OutDir);
        var path = Path.Combine(r.OutDir, ReportFileNames.Evidence(r.Plant.Name, r.EvaluationDate));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("README.txt", CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write((CommonStrings.ResourceManager.GetString(nameof(CommonStrings.Placeholder_Document), CultureInfo.InvariantCulture) ?? "") + "\n");
        return path;
    }

    public VerifyResult Verify(string zipPath, ReadLimits l) =>
        new(VerifyStatus.Error, About.AppInfo.Version, null,
            [new VerifyFinding(RglCodesCommon.NotImplemented, Path.GetFileName(zipPath), null, null, RglCodesCommon.Message(RglCodesCommon.NotImplemented, "verify"))],
            0, 0, []);

    public SampleResult CompareSample(string freshSnapshotPath, string zipPath, string? host, ReadLimits l) =>
        new(SampleStatus.Error, null, RglCodesCommon.NotImplemented, RglCodesCommon.Message(RglCodesCommon.NotImplemented, "compare-snapshot"));
}
