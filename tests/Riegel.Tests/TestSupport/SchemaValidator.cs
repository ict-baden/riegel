// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using System.Text.Json;
using Json.Schema;

namespace Riegel.Tests.TestSupport;

/// <summary>
/// Prüft JSON gegen die Schemas unter schemas/ (JSON Schema 2020-12, JsonSchema.Net). Jedes Schema wird einmal gelesen
/// (die $id darf je Prozess nur einmal registriert sein).
/// </summary>
public static class SchemaValidator
{
    private static readonly ConcurrentDictionary<string, JsonSchema> Cache = new(StringComparer.Ordinal);

    /// <summary>Schemadatei zu einer Formatkennung (<c>riegel.snapshot/1</c> → snapshot.schema.json).</summary>
    public static string FileFor(string schemaId) => schemaId switch
    {
        "riegel.snapshot/1" => "snapshot",
        "riegel.run/1" => "run",
        "riegel.exceptions/1" => "exceptions",
        "riegel.tailoring/1" => "tailoring",
        "riegel.measures/1" => "measures",
        "riegel.plant/1" => "plant",
        "riegel.approved/1" => "approved",
        "riegel.import/1" => "import",
        "riegel.baseline/1" => "baseline",
        "riegel.evidence/1" => "evidence",
        "riegel.catalogue/1" => "catalogue",
        "riegel.rules/1" => "rule",
        "riegel.lifecycle/1" => "lifecycle",
        "riegel.builds/1" => "builds",
        "riegel.capabilities/1" => "capabilities",
        "riegel.data/1" => "data",
        "riegel.collection-plan/1" => "collection-plan",
        "riegel.exceptions-template/1" => "exceptions-template",
        "riegel.tailoring-template/1" => "tailoring-template",
        _ => throw new ArgumentOutOfRangeException(nameof(schemaId), schemaId, "unbekanntes Format"),
    };

    /// <summary>Das Schema einer Datei unter schemas/ (ohne Endung), z. B. „rule“.</summary>
    public static JsonSchema Schema(string name) => Cache.GetOrAdd(name, n =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(RepoPaths.Schemas, n + ".schema.json"))));

    /// <summary>Fehler (leer = gültig) für JSON-Text gegen das Schema seines Felds <c>"schema"</c> bzw. gegen <paramref name="schemaName"/>.</summary>
    public static IReadOnlyList<string> Errors(string json, string? schemaName = null)
    {
        using var document = JsonDocument.Parse(json);
        if (schemaName is null)
        {
            var id = document.RootElement.TryGetProperty("schema", out var s) ? s.GetString() : null;
            schemaName = FileFor(id ?? throw new InvalidOperationException("Feld \"schema\" fehlt."));
        }
        var result = Schema(schemaName).Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (result.IsValid) return [];
        var errors = new List<string>();
        foreach (var detail in result.Details ?? [])
        {
            if (detail.Errors is null) continue;
            foreach (var (keyword, message) in detail.Errors)
                errors.Add($"{detail.InstanceLocation} [{keyword}] {message}");
        }
        return errors.Count > 0 ? errors : ["ungültig (ohne Einzelheiten)"];
    }

    /// <summary>Wirft mit allen Fehlern, wenn das JSON nicht gültig ist.</summary>
    public static void AssertValid(string json, string label, string? schemaName = null)
    {
        var errors = Errors(json, schemaName);
        Assert.True(errors.Count == 0, $"{label} ist nicht schema-gültig:\n  " + string.Join("\n  ", errors.Take(25)));
    }
}
