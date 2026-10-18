// SPDX-License-Identifier: MIT
using System.Text.Json;
using Json.Schema;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Parsing;

/// <summary>
/// Schema-Prüfung für die Tests dieses Pakets mit eigener Registry je Schema. <c>SchemaValidator</c> (TestSupport)
/// registriert die $id global; laden zwei Testklassen dasselbe Schema gleichzeitig zum ersten Mal, wirft JsonSchema.Net
/// „Overwriting registered schemas is not permitted“ (Integrationsanfrage). Mit eigener Registry kann das nicht passieren.
/// </summary>
internal static class LocalSchema
{
    private static readonly Dictionary<string, JsonSchema> Cache = new(StringComparer.Ordinal);
    private static readonly Lock Gate = new();

    private static JsonSchema Load(string name)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(name, out var schema)) return schema;
            schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(RepoPaths.Schemas, name + ".schema.json")),
                new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            Cache[name] = schema;
            return schema;
        }
    }

    /// <summary>Wirft mit den Fehlern, wenn <paramref name="json"/> nicht zum Schema seines Felds „schema“ passt.</summary>
    internal static void AssertValid(string json, string label)
    {
        using var document = JsonDocument.Parse(json);
        var id = document.RootElement.GetProperty("schema").GetString()!;
        var result = Load(SchemaValidator.FileFor(id)).Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (result.IsValid) return;
        var errors = (result.Details ?? []).Where(d => d.Errors is not null)
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation} [{e.Key}] {e.Value}")).Take(25);
        Assert.Fail($"{label} ist nicht schema-gültig:\n  " + string.Join("\n  ", errors));
    }
}
