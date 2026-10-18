// SPDX-License-Identifier: MIT
namespace Riegel.Core.Model;

/// <summary>
/// <c>manifest.json</c> im Nachweispaket (O9, <c>riegel.evidence/1</c>): was im Paket liegt und womit es entstand.
/// </summary>
/// <param name="Catalogue">Gepackter Katalog (Pfad im Paket über <paramref name="CatalogueFile"/>).</param>
/// <param name="Baselines">Importierte Baselines; <see cref="EvidenceBaseline.ContentIncluded"/> false = nur Metadaten.</param>
/// <param name="Files">Alle Dateien im Paket außer manifest.json und SHA256SUMS.txt, mit Prüfsumme.</param>
public sealed record EvidenceManifest(string Schema, string PlantId, string PlantName, DateTimeOffset CreatedAtUtc,
    DateOnly EvaluationDate, ToolInfo Tool, CatalogueRef Catalogue, FileRef CatalogueFile,
    IReadOnlyList<EvidenceBaseline> Baselines, RegisterHashes Registers, IReadOnlyList<EvidenceRun> Runs,
    string? OperatorRole, IReadOnlyList<FileRef> Files)
{
    public IReadOnlyList<EvidenceBaseline> Baselines { get; init; } = Baselines ?? [];
    public IReadOnlyList<EvidenceRun> Runs { get; init; } = Runs ?? [];
    public IReadOnlyList<FileRef> Files { get; init; } = Files ?? [];
}

/// <summary>Eine Baseline im Paket (Inhalt nur mit --include-baseline-content, spec 2.15).</summary>
public sealed record EvidenceBaseline(BaselineRef Baseline, bool ContentIncluded, FileRef? File);

/// <summary>Ein Lauf im Paket mit seinem Export.</summary>
public sealed record EvidenceRun(string RunId, string HostName, FileRef Results, FileRef Snapshot);
