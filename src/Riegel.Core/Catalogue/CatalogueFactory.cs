// SPDX-License-Identifier: MIT
using System.Reflection;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;

namespace Riegel.Core.Catalogue;

/// <summary>
/// Factory des Katalogs (gehört ab dem Gerüst dem Paket engine; Signatur fest). Der Platzhalter liest die
/// eingebetteten Dateien nach den Formaten aus docs/file-formats.md, prüft aber noch nichts inhaltlich.
/// </summary>
public static class CatalogueFactory
{
    public static ICatalogueProvider Create(RiegelServices services) => new StubCatalogueProvider();
}

/// <summary>Platzhalter (Gerüst): lädt rules/** und verbindet Baselines, ohne die semantische Prüfung der Engine.</summary>
internal sealed class StubCatalogueProvider : ICatalogueProvider
{
    public ICatalogue LoadEmbedded()
    {
        var assembly = typeof(StubCatalogueProvider).Assembly;
        var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith("rules/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToList();
        var header = Read<CatalogueDocument>(assembly, "rules/catalogue.json");
        var rules = new List<Rule>();
        var capabilities = new List<CapabilityEntry>();
        var tools = new List<RemoteAccessTool>();
        OfficeDetection? office = null;
        LifecycleTable lifecycle = LifecycleTable.Empty;
        BuildTable builds = BuildTable.Empty;
        foreach (var name in names)
        {
            var relative = name["rules/".Length..];
            if (relative == "catalogue.json") continue;
            if (!relative.Contains('/')) rules.AddRange(Read<RuleFile>(assembly, name).Rules);
            else if (relative == "lifecycle/windows.json") lifecycle = Read<LifecycleTable>(assembly, name);
            else if (relative == "lifecycle/builds.json") builds = Read<BuildTable>(assembly, name);
            else if (relative.StartsWith("capabilities/", StringComparison.Ordinal)) capabilities.AddRange(Read<CapabilityFile>(assembly, name).Capabilities);
            else if (relative.StartsWith("data/", StringComparison.Ordinal))
            {
                var data = Read<DataFile>(assembly, name);
                if (data.RemoteAccessTools is { } list) tools.AddRange(list);
                if (data.OfficeDetection is { } detection) office = detection;
            }
        }
        return LoadedCatalogue.From(header with
        {
            Rules = rules,
            Lifecycle = lifecycle,
            Builds = builds,
            Capabilities = new CapabilityTable(capabilities),
            Data = new CatalogueData(tools, office),
        });
    }

    public ICatalogue Load(Stream catalogueJson, ReadLimits l)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = catalogueJson.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > l.MaxCatalogueBytes) throw RiegelException.Common(RglCodesCommon.LimitExceeded, "catalogue", l.MaxCatalogueBytes);
            buffer.Write(chunk, 0, read);
        }
        var document = RiegelJson.Deserialize<CatalogueDocument>(buffer.ToArray());
        if (document.Schema != SchemaIds.Catalogue) throw RiegelException.Common(RglCodesCommon.UnsupportedSchema, document.Schema);
        return LoadedCatalogue.From(document);
    }

    public ICatalogue Combine(ICatalogue core, IEnumerable<ImportedBaseline> b)
    {
        var rules = core.Rules.Concat(b.SelectMany(x => x.Rules)).ToList();
        return LoadedCatalogue.From(new CatalogueDocument(SchemaIds.Catalogue, core.Id, core.Version, core.ReleasedOn, rules,
            core.Lifecycle, core.Builds, core.Capabilities, core.Data));
    }

    private static T Read<T>(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name) ?? throw RiegelException.Common(RglCodesCommon.NotFound, name);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return RiegelJson.Deserialize<T>(memory.ToArray());
    }
}

/// <summary>Ein geladener Katalog; SHA-256 über die kanonische Form (Regeln nach RuleSetId und ID sortiert).</summary>
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

    public static LoadedCatalogue From(CatalogueDocument document)
    {
        var sorted = (document.Rules ?? []).OrderBy(r => r.RuleSetId, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        var canonical = document with { Schema = SchemaIds.Catalogue, Rules = sorted };
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

    public Rule? Find(string id) => _byId.TryGetValue(id, out var rule) ? rule : null;
}
