// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Riegel.Core.Evaluation;
using Riegel.Core.Model;

namespace Riegel.Core.Catalogue;

/// <summary>
/// Geschlossenes Vokabular (spec 2.3) auf der Ebene des JSON-Texts: jede Prüfart muss bekannt sein und jede Prüfung
/// (wie jede Regel und Tabelle) darf nur die Felder ihres Vertragstyps haben. Die erlaubten Feldnamen kommen aus den
/// Konstruktorparametern der Vertragstypen (camelCase, wie RiegelJson schreibt) – so bleibt die Prüfung bei
/// Vertragsänderungen von selbst aktuell. Fehler: RGL-0201 (Prüfart), RGL-0202 (Feld).
/// </summary>
internal static class ClosedVocabulary
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, Type>?> Fields = new();

    /// <summary>Prüfart → Typ, aus den JsonDerivedType-Attributen von <see cref="CheckSpec"/>.</summary>
    public static IReadOnlyDictionary<string, Type> CheckTypes { get; } = typeof(CheckSpec)
        .GetCustomAttributes<JsonDerivedTypeAttribute>()
        .ToDictionary(a => (string)a.TypeDiscriminator!, a => a.DerivedType, StringComparer.Ordinal);

    /// <summary>Erlaubte Felder einer Prüfart (ohne „type“), z. B. für Tests und docs/evaluation.md.</summary>
    public static IReadOnlyCollection<string> FieldsOf(string checkType) =>
        CheckTypes.TryGetValue(checkType, out var t) && FieldMap(t) is { } map ? [.. map.Keys.Order(StringComparer.Ordinal)] : [];

    /// <summary>Prüft ein JSON-Dokument gegen den Vertragstyp <paramref name="root"/>; jedes Problem kommt in <paramref name="issues"/>.</summary>
    public static void Check(JsonElement element, Type root, string where, List<CatalogueIssue> issues) =>
        Walk(element, root, where, null, issues, 0);

    private static void Walk(JsonElement e, Type type, string path, string? ruleId, List<CatalogueIssue> issues, int depth)
    {
        if (depth > 64 || issues.Count > 200) return;
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return;
        if (type == typeof(JsonElement) || type == typeof(string) || type.IsPrimitive || type.IsEnum || type == typeof(decimal)
            || type == typeof(DateOnly) || type == typeof(DateTimeOffset) || type == typeof(LocalizedText))
            return;
        if (ElementType(type) is { } item)
        {
            if (e.ValueKind != JsonValueKind.Array) return; // Form prüft RiegelJson
            int i = 0;
            foreach (var x in e.EnumerateArray()) Walk(x, item, $"{path}[{i++}]", ruleId, issues, depth + 1);
            return;
        }
        if (DictionaryValueType(type) is { } valueType)
        {
            if (e.ValueKind != JsonValueKind.Object) return;
            foreach (var p in e.EnumerateObject()) Walk(p.Value, valueType, $"{path}.{p.Name}", ruleId, issues, depth + 1);
            return;
        }
        if (e.ValueKind != JsonValueKind.Object) return;
        if (type == typeof(CheckSpec))
        {
            if (!e.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String || !CheckTypes.TryGetValue(t.GetString()!, out var derived))
            {
                var name = e.TryGetProperty("type", out var tt) ? tt.ToString() : "(none)";
                issues.Add(new(RglCodesEngine.UnknownCheckType, Where(ruleId, path), $"type \"{name}\""));
                return;
            }
            type = derived;
        }
        if (type == typeof(Rule) && e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) ruleId = id.GetString();
        if (FieldMap(type) is not { } fields) return;
        bool isCheck = typeof(CheckSpec).IsAssignableFrom(type);
        foreach (var p in e.EnumerateObject())
        {
            if (isCheck && p.Name == "type") continue;
            if (!fields.TryGetValue(p.Name, out var fieldType))
            {
                issues.Add(new(RglCodesEngine.UnknownField, Where(ruleId, path), $"field \"{p.Name}\"" + (isCheck ? $" in check \"{CheckTypeName(type)}\"" : "")));
                continue;
            }
            Walk(p.Value, fieldType, $"{path}.{p.Name}", ruleId, issues, depth + 1);
        }
    }

    private static string Where(string? ruleId, string path) => ruleId is null ? path : $"{ruleId} ({path})";

    private static string CheckTypeName(Type t) => CheckTypes.FirstOrDefault(p => p.Value == t).Key ?? t.Name;

    /// <summary>Felder eines Vertragstyps (camelCase der Parameter des größten öffentlichen Konstruktors).</summary>
    private static IReadOnlyDictionary<string, Type>? FieldMap(Type type) => Fields.GetOrAdd(type, t =>
    {
        if (t.Namespace?.StartsWith("Riegel.Core.Model", StringComparison.Ordinal) != true) return null;
        var ctor = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance).OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        if (ctor is null) return null;
        return ctor.GetParameters().Where(p => p.Name is not null && !(t.IsSubclassOf(typeof(CheckSpec)) && p.Name == "Type"))
            .ToDictionary(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name!), p => p.ParameterType, StringComparer.Ordinal);
    });

    private static Type? ElementType(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        if (!type.IsGenericType) return null;
        var def = type.GetGenericTypeDefinition();
        if (def == typeof(IReadOnlyList<>) || def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyCollection<>))
            return type.GetGenericArguments()[0];
        return null;
    }

    private static Type? DictionaryValueType(Type type)
    {
        if (!type.IsGenericType) return null;
        var def = type.GetGenericTypeDefinition();
        return def == typeof(IReadOnlyDictionary<,>) || def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>) || def == typeof(SortedDictionary<,>)
            ? type.GetGenericArguments()[1]
            : null;
    }
}

/// <summary>Ein Problem im Katalog: RGL-Code, Stelle (Regel-ID oder JSON-Pfad) und eine kurze technische Angabe.</summary>
internal sealed record CatalogueIssue(string Code, string Where, string What)
{
    public override string ToString() => $"{Where}: {What}";
}
