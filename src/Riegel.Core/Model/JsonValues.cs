// SPDX-License-Identifier: MIT
using System.Text.Json;

namespace Riegel.Core.Model;

/// <summary>Kleine Helfer, um <see cref="JsonElement"/>-Werte zu bauen (Fakten, Testvektoren, Beispieldaten).</summary>
public static class JsonValues
{
    public static JsonElement Null { get; } = Parse("null");
    public static JsonElement True { get; } = Parse("true");
    public static JsonElement False { get; } = Parse("false");

    public static JsonElement Text(string value) => Build(w => w.WriteStringValue(value));

    public static JsonElement Number(long value) => Build(w => w.WriteNumberValue(value));

    public static JsonElement Number(ulong value) => Build(w => w.WriteNumberValue(value));

    public static JsonElement Bool(bool value) => value ? True : False;

    public static JsonElement Texts(IEnumerable<string> values) => Build(w =>
    {
        w.WriteStartArray();
        foreach (var v in values) w.WriteStringValue(v);
        w.WriteEndArray();
    });

    /// <summary>Wert aus JSON-Text (für Tests und Beispieldaten).</summary>
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>Wert mit einem eigenen Schreiber bauen.</summary>
    public static JsonElement Build(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) write(writer);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
}
