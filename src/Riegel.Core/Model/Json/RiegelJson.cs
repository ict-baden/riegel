// SPDX-License-Identifier: MIT
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Riegel.Core.Model.Json;

/// <summary>
/// Einheitliches Lesen und Schreiben aller Riegel-Dateien. Ausgabe: UTF-8 ohne BOM, eingerückt mit 2 Leerzeichen,
/// LF, abschließender Zeilenumbruch, Umlaute lesbar (nicht als \u-Folge) – damit Register in git und im DMS
/// sauber vergleichbar sind und Prüfsummen auf jedem System gleich ausfallen.
/// </summary>
public static class RiegelJson
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Die Optionen hinter <see cref="Context"/> (schreibgeschützt).</summary>
    public static JsonSerializerOptions Options { get; }

    /// <summary>Kontext mit den Riegel-Optionen; für eigene Typinformationen (<c>Context.Snapshot</c> …).</summary>
    public static RiegelJsonContext Context { get; }

    static RiegelJson()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            IndentSize = 2,
            NewLine = "\n",
            AllowOutOfOrderMetadataProperties = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            NumberHandling = JsonNumberHandling.Strict,
            MaxDepth = 64,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new UtcDateTimeOffsetConverter());
        Context = new RiegelJsonContext(options);
        options.MakeReadOnly();
        Options = options;
    }

    /// <summary>Typinformation für <typeparamref name="T"/>; Fehler, wenn der Typ nicht im Kontext steht.</summary>
    public static JsonTypeInfo<T> TypeInfo<T>() =>
        Context.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
        ?? throw new InvalidOperationException($"{typeof(T).Name} fehlt in RiegelJsonContext.");

    /// <summary>JSON-Text mit LF am Ende.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, TypeInfo<T>()) + "\n";

    /// <summary>UTF-8 ohne BOM, mit LF am Ende – Grundlage aller Prüfsummen über Riegel-Dateien.</summary>
    public static byte[] SerializeToUtf8Bytes<T>(T value) => Utf8.GetBytes(Serialize(value));

    /// <summary>Kompakte Form (eine Zeile, ohne Einrückung) für Inhalts-Hashes einzelner Einträge.</summary>
    public static byte[] SerializeCompact<T>(T value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, Encoder = Options.Encoder }))
            JsonSerializer.Serialize(writer, value, TypeInfo<T>());
        return stream.ToArray();
    }

    /// <summary>Liest <typeparamref name="T"/>; ungültiges JSON, falsche Form oder null → <see cref="RiegelException"/> (RGL-0004).</summary>
    public static T Deserialize<T>(ReadOnlySpan<byte> utf8)
    {
        // Ein BOM am Anfang (von Editoren unter Windows) ist erlaubt
        if (utf8.StartsWith(Utf8BomBytes)) utf8 = utf8[3..];
        try
        {
            return JsonSerializer.Deserialize(utf8, TypeInfo<T>())
                   ?? throw RiegelException.Common(RglCodesCommon.InvalidJson, typeof(T).Name);
        }
        catch (JsonException e)
        {
            throw new RiegelException(RglCodesCommon.InvalidJson, RglCodesCommon.Message(RglCodesCommon.InvalidJson, e.Path ?? "$"), e);
        }
        catch (NotSupportedException e)
        {
            throw new RiegelException(RglCodesCommon.InvalidJson, RglCodesCommon.Message(RglCodesCommon.InvalidJson, typeof(T).Name), e);
        }
    }

    /// <summary>Liest <typeparamref name="T"/> aus Text.</summary>
    public static T Deserialize<T>(string json) => Deserialize<T>(Encoding.UTF8.GetBytes(json));

    /// <summary>Liest eine Datei, höchstens <paramref name="maxBytes"/> groß (RGL-0006 sonst).</summary>
    public static T ReadFile<T>(string path, long maxBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw RiegelException.Common(RglCodesCommon.NotFound, path);
        if (info.Length > maxBytes) throw RiegelException.Common(RglCodesCommon.LimitExceeded, path, maxBytes);
        return Deserialize<T>(File.ReadAllBytes(path));
    }

    /// <summary>
    /// Schreibt atomar: erst eine temporäre Datei daneben, dann Ersetzen. Gibt den SHA-256 des Inhalts zurück.
    /// </summary>
    public static string WriteFile<T>(string path, T value)
    {
        var bytes = SerializeToUtf8Bytes(value);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return Hashes.Sha256Hex(bytes);
    }

    private static ReadOnlySpan<byte> Utf8BomBytes => [0xEF, 0xBB, 0xBF];
}
