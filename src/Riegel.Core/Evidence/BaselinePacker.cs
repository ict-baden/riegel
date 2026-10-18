// SPDX-License-Identifier: MIT
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Resources;
using Riegel.Core.Services;

namespace Riegel.Core.Evidence;

/// <summary>
/// Importierte Kunden-Baselines im Nachweispaket (spec 2.15): standardmäßig nur Metadaten (Name, Quell-SHA-256,
/// Regel-IDs und Schlüssel, Ergebnisse) in <c>baselines/&lt;id&gt;.metadata.json</c>. Den Inhalt (die gespeicherte
/// <c>baselines/&lt;id&gt;.json</c> und die daraus gebildeten Regeln für verify) nur mit
/// <c>--include-baseline-content</c>, weil SCT-, BSI- und CIS-Bedingungen die Weitergabe verbieten können.
/// </summary>
internal static partial class BaselinePacker
{
    /// <summary>Formatkennung der Metadaten-Datei (docs/evidence.md).</summary>
    public const string MetadataSchema = "riegel.evidence-baseline/1";

    [GeneratedRegex("^[a-z0-9-]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex BaselineId();

    public static IReadOnlyList<EvidenceBaseline> Pack(RiegelServices services, string? folder, IReadOnlyList<Run> runs, bool includeContent,
        ReadLimits limits, Action<string, byte[]> add, List<string> notes, OutputText t)
    {
        var refs = new SortedDictionary<string, BaselineRef>(StringComparer.Ordinal);
        foreach (var b in runs.SelectMany(r => r.Baselines)) refs.TryAdd(b.Id, b);
        var result = new List<EvidenceBaseline>();
        foreach (var (id, baseline) in refs)
        {
            if (!BaselineId().IsMatch(id))
            {
                notes.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_BaselineSkipped), EvidenceArchive.Printable(id)));
                continue;
            }
            ImportedBaseline? imported = null;
            byte[]? stored = null;
            var storedPath = folder is null ? null : Path.Combine(folder, "baselines", id + ".json");
            if (storedPath is not null && File.Exists(storedPath) && (File.GetAttributes(storedPath) & FileAttributes.ReparsePoint) == 0)
            {
                try
                {
                    stored = EvidenceWriter.ReadLimited(storedPath, limits.MaxBaselineCsvBytes, RglCodesDataOutputs.RunNotPackable);
                    imported = services.Baselines.Import(storedPath, baseline.Name, limits);
                }
                catch (RiegelException e)
                {
                    notes.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_BaselineUnreadable), baseline.Name, e.Code));
                    imported = null;
                }
            }

            bool content = includeContent && stored is not null && imported is not null;
            if (includeContent && !content)
                notes.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_BaselineContentMissing), baseline.Name));
            var metadataPath = EvidenceLayout.BaselineMetadata(id);
            var metadata = Metadata(baseline, content, imported, runs);
            add(metadataPath, metadata);
            var file = new FileRef(metadataPath, Hashes.Sha256Hex(metadata));
            if (content)
            {
                var contentPath = EvidenceLayout.BaselineContent(id);
                add(contentPath, stored!);
                var rules = imported!.Rules.Select(r => r with { RuleSetId = id }).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
                add(EvidenceLayout.BaselineRules(id), RiegelJson.SerializeToUtf8Bytes(new RuleFile(SchemaIds.Rules, "IMP-" + id, rules)));
                file = new FileRef(contentPath, Hashes.Sha256Hex(stored!));
            }
            // file: bei Inhalt die gespeicherte Baseline, sonst die Metadaten
            result.Add(new EvidenceBaseline(baseline, content, file));
        }
        return result;
    }

    /// <summary>
    /// Metadaten einer Baseline: <c>{ schema, baseline, contentIncluded, rules: [ { id, type, key } ],
    /// results: [ { host, runId, ruleId, status, expected?, actual? } ] }</c> – ohne Titel und Texte der Vorgaben.
    /// </summary>
    private static byte[] Metadata(BaselineRef baseline, bool content, ImportedBaseline? imported, IReadOnlyList<Run> runs)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("schema", MetadataSchema);
            w.WritePropertyName("baseline");
            JsonSerializer.Serialize(w, baseline, RiegelJson.TypeInfo<BaselineRef>());
            w.WriteBoolean("contentIncluded", content);
            w.WriteStartArray("rules");
            var rules = imported?.Rules.Select(r => (r.Id, r.Check.Type, Key(r.Check)))
                ?? runs.SelectMany(r => r.Results).Where(r => r.RuleSetId == baseline.Id)
                    .Select(r => (r.RuleId, (string)"", string.Join(";", r.EvidenceKeys)));
            foreach (var (id, type, key) in rules.DistinctBy(x => x.Item1).OrderBy(x => x.Item1, StringComparer.Ordinal))
            {
                w.WriteStartObject();
                w.WriteString("id", id);
                if (type.Length > 0) w.WriteString("type", type);
                w.WriteString("key", key);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("results");
            foreach (var run in runs.OrderBy(r => r.HostName, StringComparer.OrdinalIgnoreCase))
                foreach (var r in run.Results.Where(r => r.RuleSetId == baseline.Id).OrderBy(r => r.RuleId, StringComparer.Ordinal))
                {
                    w.WriteStartObject();
                    w.WriteString("host", run.HostName);
                    w.WriteString("runId", run.Id);
                    w.WriteString("ruleId", r.RuleId);
                    w.WriteString("status", OutputText.Camel(r.Status));
                    if (r.Expected is not null) w.WriteString("expected", r.Expected);
                    if (r.Actual is not null) w.WriteString("actual", r.Actual);
                    w.WriteEndObject();
                }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>Was eine Vorgabe prüft (Identität, nicht der Sollwert), z. B. <c>hklm\software\…|wert</c>.</summary>
    internal static string Key(CheckSpec check) => check switch
    {
        RegistryCheck c => AttributionMap.KeyOf(c.Key, c.Value),
        RegistryEffectiveCheck c => string.Join(";", c.Sources.Select(s => AttributionMap.KeyOf(s.Key, s.Value))),
        ServiceCheck c => "service:" + c.Name.ToLowerInvariant(),
        AuditSubcategoryCheck c => "audit:" + c.Guid.ToLowerInvariant(),
        UserRightCheck c => "right:" + c.Right,
        SeceditCheck c => "secedit:" + c.Section + "/" + c.Key,
        AccountPolicyCheck c => "accountPolicy:" + OutputText.Camel(c.Field),
        FeatureCheck c => "feature:" + c.Name,
        WmiPropertyCheck c => c.Item + "/" + c.Property,
        _ => check.Type,
    };
}
