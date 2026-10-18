// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;

namespace Riegel.Core.Baselines;

/// <summary>
/// Factory des Baseline-Imports (gehört ab dem Gerüst dem Paket parsing; Signatur fest). Der Platzhalter liest nur
/// riegel.baseline/1 (JSON) und bildet einfache IDs; GPO-Sicherungen und CSV kommen mit dem Paket parsing.
/// </summary>
public static class BaselinesFactory
{
    public static IBaselineImporter Create(RiegelServices services) => new StubBaselineImporter(services.Clock);
}

/// <summary>Platzhalter (Gerüst).</summary>
internal sealed class StubBaselineImporter(TimeProvider clock) : IBaselineImporter
{
    public ImportedBaseline Import(string path, string name, ReadLimits l)
    {
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw RiegelException.Common(RglCodesCommon.NotImplemented, Path.GetExtension(path));
        var file = RiegelJson.ReadFile<BaselineFile>(path, l.MaxBaselineCsvBytes);
        if (file.Schema != SchemaIds.Baseline) throw RiegelException.Common(RglCodesCommon.UnsupportedSchema, file.Schema);
        var sha = Hashes.Sha256File(path);
        var rules = file.Rules.Select((r, i) => new Rule(
                $"IMP-{file.Id}-{r.Id ?? (i + 1).ToString("D4", System.Globalization.CultureInfo.InvariantCulture)}",
                "0", RuleState.Active, r.Category ?? "system", r.Severity ?? Severity.Medium, RuleKind.Check,
                LocalizedText.Of(("en", r.Title)), LocalizedText.Of(("en", r.Rationale ?? r.Title)), null, null, null,
                new Applicability(["client", "server"], null, null, DomainScope.Any, false, [], null, AbsentCapability.NotApplicable),
                false, r.Check, null, [], [], [], [], file.Id))
            .ToList();
        var reference = new BaselineRef(file.Id, name, BaselineSourceKind.RiegelJson, sha, Path.GetFileName(path), clock.GetUtcNow(), rules.Count);
        return new ImportedBaseline(reference, rules);
    }
}
