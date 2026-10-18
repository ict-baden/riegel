// SPDX-License-Identifier: MIT
using System.Text;
using System.Text.Json;
using Riegel.Core.Model.Json;

namespace Riegel.Commands.Output;

/// <summary>
/// JSON-Ausgabe der Befehle (<c>--format json</c>): invariant, UTF-8, eingerückt mit 2 Leerzeichen und LF wie alle
/// Riegel-Dateien. Eigene Hüllen (<c>riegel.cli.*/1</c>) entstehen mit <see cref="Utf8JsonWriter"/>; Vertragstypen darin
/// (Lauf, Vergleich, Regel …) über <see cref="RiegelJson"/>, damit sie genau so aussehen wie in den Dateien.
/// </summary>
internal static class JsonOutput
{
    private static JsonWriterOptions Options => new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = RiegelJson.Options.Encoder,
        MaxDepth = 128,
    };

    /// <summary>Schreibt ein JSON-Dokument nach <paramref name="output"/> (mit LF am Ende).</summary>
    public static void Write(TextWriter output, Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Options)) body(writer);
        output.Write(Encoding.UTF8.GetString(buffer.ToArray()));
        output.Write('\n');
    }

    /// <summary>Ein Vertragsobjekt als Eigenschaft (null wird zu JSON null).</summary>
    public static void Contract<T>(Utf8JsonWriter writer, string name, T? value)
    {
        writer.WritePropertyName(name);
        if (value is null) writer.WriteNullValue();
        else JsonSerializer.Serialize(writer, value, RiegelJson.TypeInfo<T>());
    }

    /// <summary>Ein Vertragsobjekt als Element eines Arrays.</summary>
    public static void ContractValue<T>(Utf8JsonWriter writer, T value) =>
        JsonSerializer.Serialize(writer, value, RiegelJson.TypeInfo<T>());

    /// <summary>Liste von Texten.</summary>
    public static void Strings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values) writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    /// <summary>Text oder null.</summary>
    public static void StringOrNull(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value);
    }

    /// <summary>Statuswort in JSON (klein, invariant).</summary>
    public static string Status(int exitCode) => exitCode switch
    {
        CommandApp.Ok => "ok",
        CommandApp.Warning => "warning",
        CommandApp.Critical => "critical",
        _ => "unknown",
    };
}
