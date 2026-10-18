// SPDX-License-Identifier: MIT
using System.Text.Json;
using Json.Schema;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Windows.Collector;

/// <summary>
/// Snapshot-Schema für die Sammler-Tests mit eigenem Schema-Register: JsonSchema.Net trägt Schemas sonst ins globale
/// Register ein, und zwei Testklassen, die es zum ersten Mal gleichzeitig laden, scheitern mit „Overwriting registered
/// schemas is not permitted“. Einmal gebaut (Lazy, threadsicher), nie global registriert.
/// </summary>
internal static class SnapshotSchema
{
    private static readonly Lazy<JsonSchema> Schema = new(() =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(RepoPaths.Schemas, "snapshot.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() }), LazyThreadSafetyMode.ExecutionAndPublication);

    public static void AssertValid(string json, string label)
    {
        using var document = JsonDocument.Parse(json);
        var result = Schema.Value.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (result.IsValid) return;
        var errors = new List<string>();
        foreach (var detail in result.Details ?? [])
            if (detail.Errors is not null)
                foreach (var (keyword, message) in detail.Errors) errors.Add($"{detail.InstanceLocation} [{keyword}] {message}");
        Assert.Fail($"{label} ist nicht schema-gültig:\n  " + string.Join("\n  ", errors.Take(25)));
    }
}
