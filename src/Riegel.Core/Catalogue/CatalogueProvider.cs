// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Text.Json;
using Riegel.Core.Evaluation;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;

namespace Riegel.Core.Catalogue;

/// <summary>
/// Lädt und prüft Kataloge (spec 2.3, 7.2). Reihenfolge je Datei: geschlossenes Vokabular auf dem JSON-Text
/// (RGL-0201/0202), dann Lesen mit RiegelJson, dann die inhaltliche Prüfung aller Regeln und Tabellen. Ein
/// ungültiger Katalog wird nie benutzt.
/// </summary>
internal sealed class CatalogueProvider : ICatalogueProvider
{
    private static readonly Assembly Core = typeof(CatalogueProvider).Assembly;
    private readonly Lazy<ICatalogue> _embedded = new(LoadEmbeddedCore);

    public ICatalogue LoadEmbedded() => _embedded.Value;

    /// <summary>Alle Dateien unter rules/ in Riegel.Core: Kopf, Präfixdateien, lifecycle/, capabilities/, data/.</summary>
    internal static ICatalogue LoadEmbeddedCore()
    {
        var names = Core.GetManifestResourceNames().Where(n => n.StartsWith("rules/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
        var files = names.ToDictionary(n => n, n => ReadResource(n), StringComparer.Ordinal);
        return FromFiles(files);
    }

    /// <summary>Ein Katalog aus Dateien (logischer Name „rules/&lt;Pfad&gt;“ → Inhalt), z. B. für Tests mit eigenen Regeln.</summary>
    internal static ICatalogue FromFiles(IReadOnlyDictionary<string, byte[]> files)
    {
        if (!files.TryGetValue("rules/catalogue.json", out var headerBytes))
            throw RglCodesEngine.Exception(RglCodesEngine.InvalidTable, "rules/catalogue.json missing");
        var header = Read<CatalogueDocument>(headerBytes, "rules/catalogue.json", 64);
        if (header.Schema != SchemaIds.Catalogue) throw RiegelException.Common(RglCodesCommon.UnsupportedSchema, header.Schema);
        var rules = new List<Rule>();
        var capabilities = new List<CapabilityEntry>();
        var tools = new List<RemoteAccessTool>();
        OfficeDetection? office = null;
        LifecycleTable lifecycle = LifecycleTable.Empty;
        BuildTable builds = BuildTable.Empty;
        var issues = new List<CatalogueIssue>();
        foreach (var (name, bytes) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var relative = name.StartsWith("rules/", StringComparison.Ordinal) ? name["rules/".Length..] : name;
            if (relative == "catalogue.json") continue;
            if (!relative.Contains('/') && relative.EndsWith(".json", StringComparison.Ordinal))
            {
                var file = Read<RuleFile>(bytes, name, 64);
                if (file.Schema != SchemaIds.Rules) issues.Add(new(RglCodesEngine.InvalidTable, name, $"schema \"{file.Schema}\""));
                if (file.Prefix != relative[..^5]) issues.Add(new(RglCodesEngine.InvalidRule, name, $"prefix \"{file.Prefix}\" does not match the file name"));
                foreach (var rule in file.Rules)
                    if (rule is not null && !rule.Id.StartsWith(file.Prefix + "-", StringComparison.Ordinal))
                        issues.Add(new(RglCodesEngine.InvalidRule, rule.Id, $"not in prefix file {relative}"));
                rules.AddRange(file.Rules);
            }
            else if (relative == "lifecycle/windows.json")
            {
                lifecycle = Read<LifecycleTable>(bytes, name, 64);
                if (lifecycle.Schema != SchemaIds.Lifecycle) issues.Add(new(RglCodesEngine.InvalidTable, name, $"schema \"{lifecycle.Schema}\""));
            }
            else if (relative == "lifecycle/builds.json")
            {
                builds = Read<BuildTable>(bytes, name, 64);
                if (builds.Schema != SchemaIds.Builds) issues.Add(new(RglCodesEngine.InvalidTable, name, $"schema \"{builds.Schema}\""));
            }
            else if (relative.StartsWith("capabilities/", StringComparison.Ordinal) && !relative["capabilities/".Length..].Contains('/'))
            {
                var file = Read<CapabilityFile>(bytes, name, 64);
                if (file.Schema != SchemaIds.Capabilities) issues.Add(new(RglCodesEngine.InvalidTable, name, $"schema \"{file.Schema}\""));
                capabilities.AddRange(file.Capabilities);
            }
            else if (relative.StartsWith("data/", StringComparison.Ordinal) && !relative["data/".Length..].Contains('/'))
            {
                var file = Read<DataFile>(bytes, name, 64);
                if (file.Schema != SchemaIds.Data) issues.Add(new(RglCodesEngine.InvalidTable, name, $"schema \"{file.Schema}\""));
                switch (file.Kind)
                {
                    case DataKind.RemoteAccessTools when file.RemoteAccessTools is { } list && file.OfficeDetection is null:
                        foreach (var tool in list)
                            if (tool is null || string.IsNullOrWhiteSpace(tool.Product) || (tool.ServiceNames.Count == 0 && tool.DisplayNameContains.Count == 0)
                                || tool.ServiceNames.Concat(tool.DisplayNameContains).Any(string.IsNullOrWhiteSpace))
                                issues.Add(new(RglCodesEngine.InvalidTable, name, $"remote access tool \"{tool?.Product}\" needs a product and non-empty patterns"));
                        tools.AddRange(list);
                        break;
                    case DataKind.OfficeDetection when file.OfficeDetection is { } detection && file.RemoteAccessTools is null:
                        if (office is not null) issues.Add(new(RglCodesEngine.InvalidTable, name, "office detection defined twice"));
                        office = detection;
                        break;
                    default:
                        issues.Add(new(RglCodesEngine.InvalidTable, name, $"kind {file.Kind} does not match the content"));
                        break;
                }
            }
            else issues.Add(new(RglCodesEngine.InvalidTable, name, "unknown file in the catalogue"));
        }
        CatalogueValidator.ThrowIfAny(issues);
        var document = header with
        {
            Rules = rules,
            Lifecycle = lifecycle,
            Builds = builds,
            Capabilities = new CapabilityTable(capabilities),
            Data = new CatalogueData(tools, office),
        };
        return Validated(document);
    }

    public ICatalogue Load(Stream catalogueJson, ReadLimits l)
    {
        ArgumentNullException.ThrowIfNull(catalogueJson);
        l ??= ReadLimits.Default;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = catalogueJson.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > l.MaxCatalogueBytes) throw RiegelException.Common(RglCodesCommon.LimitExceeded, "catalogue", l.MaxCatalogueBytes);
            buffer.Write(chunk, 0, read);
        }
        var bytes = buffer.ToArray();
        using (var doc = Parse(bytes, "catalogue", Math.Clamp(l.MaxJsonDepth, 1, 64)))
        {
            var schema = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("schema", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString() : null;
            if (schema != SchemaIds.Catalogue) throw RiegelException.Common(RglCodesCommon.UnsupportedSchema, schema ?? "(none)");
        }
        var document = Read<CatalogueDocument>(bytes, "catalogue", Math.Clamp(l.MaxJsonDepth, 1, 64));
        return Validated(document);
    }

    public ICatalogue Combine(ICatalogue core, IEnumerable<ImportedBaseline> b)
    {
        ArgumentNullException.ThrowIfNull(core);
        var issues = new List<CatalogueIssue>();
        var baselineIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rules = new List<Rule>(core.Rules);
        foreach (var baseline in b ?? [])
        {
            var id = baseline?.Baseline?.Id;
            if (string.IsNullOrWhiteSpace(id)) { issues.Add(new(RglCodesEngine.BaselineConflict, "baseline", "id missing")); continue; }
            if (id == "riegel" || !baselineIds.Add(id)) issues.Add(new(RglCodesEngine.BaselineConflict, id, "baseline id duplicate or reserved"));
            foreach (var rule in baseline!.Rules)
            {
                if (rule is null) continue;
                if (rule.RuleSetId != id) issues.Add(new(RglCodesEngine.BaselineConflict, rule.Id, $"ruleSetId \"{rule.RuleSetId}\" is not the baseline id \"{id}\""));
                rules.Add(rule);
            }
        }
        CatalogueValidator.ThrowIfAny(issues);
        CatalogueValidator.ThrowIfAny(CatalogueValidator.ValidateRules(rules, core.Capabilities));
        return LoadedCatalogue.From(new CatalogueDocument(SchemaIds.Catalogue, core.Id, core.Version, core.ReleasedOn, rules,
            core.Lifecycle, core.Builds, core.Capabilities, core.Data));
    }

    /// <summary>Ein Katalog aus einem Dokument, mit derselben Prüfung wie beim Laden (für Tests und Werkzeuge).</summary>
    internal static ICatalogue FromDocument(CatalogueDocument document) => Validated(document);

    /// <summary>Tabellen und Regeln prüfen, dann der geladene Katalog mit kanonischem SHA-256.</summary>
    private static LoadedCatalogue Validated(CatalogueDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.Id) || string.IsNullOrWhiteSpace(document.Version))
            throw RglCodesEngine.Exception(RglCodesEngine.CatalogueInvalid, "id and version are required");
        var capabilities = document.Capabilities ?? CapabilityTable.Empty;
        var issues = CatalogueValidator.ValidateTables(document.Lifecycle ?? LifecycleTable.Empty, document.Builds ?? BuildTable.Empty, capabilities);
        issues.AddRange(CatalogueValidator.ValidateRules(document.Rules ?? [], capabilities));
        CatalogueValidator.ThrowIfAny(issues);
        return LoadedCatalogue.From(document);
    }

    /// <summary>Geschlossenes Vokabular prüfen, dann mit RiegelJson lesen.</summary>
    private static T Read<T>(byte[] bytes, string name, int maxDepth)
    {
        using (var doc = Parse(bytes, name, maxDepth))
        {
            var issues = new List<CatalogueIssue>();
            ClosedVocabulary.Check(doc.RootElement, typeof(T), name, issues);
            CatalogueValidator.ThrowIfAny(issues);
        }
        return RiegelJson.Deserialize<T>(bytes);
    }

    private static JsonDocument Parse(byte[] bytes, string name, int maxDepth)
    {
        ReadOnlyMemory<byte> data = bytes;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) data = data[3..];
        try
        {
            return JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = maxDepth, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
        }
        catch (JsonException e)
        {
            throw new RiegelException(RglCodesCommon.InvalidJson, RglCodesCommon.Message(RglCodesCommon.InvalidJson, name), e);
        }
    }

    private static byte[] ReadResource(string name)
    {
        using var stream = Core.GetManifestResourceStream(name) ?? throw RiegelException.Common(RglCodesCommon.NotFound, name);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}

/// <summary>Ein geladener, geprüfter Katalog; SHA-256 über die kanonische Form (Regeln nach RuleSetId und ID sortiert).</summary>
internal sealed class LoadedCatalogue : ICatalogue
{
    private readonly Dictionary<string, Rule> _byId;

    private LoadedCatalogue(CatalogueDocument document, string sha256)
    {
        Id = document.Id;
        Version = document.Version;
        ReleasedOn = document.ReleasedOn;
        Sha256 = sha256;
        Rules = document.Rules ?? [];
        Lifecycle = document.Lifecycle ?? LifecycleTable.Empty;
        Builds = document.Builds ?? BuildTable.Empty;
        Capabilities = document.Capabilities ?? CapabilityTable.Empty;
        Data = document.Data ?? CatalogueData.Empty;
        _byId = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in Rules) _byId.TryAdd(rule.Id, rule);
    }

    /// <summary>
    /// Kanonische Form: Schema riegel.catalogue/1, Regeln nach RuleSetId und ID (ordinal), Tabellen wie geladen
    /// (Dateien in Namensreihenfolge), geschrieben mit RiegelJson. Dieselben Inhalte ergeben denselben SHA-256 – auch
    /// über <see cref="ICatalogueProvider.Load"/> aus einem Nachweispaket.
    /// </summary>
    public static LoadedCatalogue From(CatalogueDocument document)
    {
        var sorted = (document.Rules ?? []).OrderBy(r => r.RuleSetId, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        var canonical = document with
        {
            Schema = SchemaIds.Catalogue,
            Rules = sorted,
            Lifecycle = document.Lifecycle ?? LifecycleTable.Empty,
            Builds = document.Builds ?? BuildTable.Empty,
            Capabilities = document.Capabilities ?? CapabilityTable.Empty,
            Data = document.Data ?? CatalogueData.Empty,
        };
        return new LoadedCatalogue(canonical, Hashes.Sha256Hex(RiegelJson.SerializeToUtf8Bytes(canonical)));
    }

    public string Id { get; }
    public string Version { get; }
    public DateOnly ReleasedOn { get; }
    public string Sha256 { get; }
    public IReadOnlyList<Rule> Rules { get; }
    public LifecycleTable Lifecycle { get; }
    public BuildTable Builds { get; }
    public CapabilityTable Capabilities { get; }
    public CatalogueData Data { get; }

    public Rule? Find(string id) => id is null ? null : _byId.TryGetValue(id, out var rule) ? rule : null;
}
