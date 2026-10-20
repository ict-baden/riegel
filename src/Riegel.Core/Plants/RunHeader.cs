// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.Json.Serialization;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Plants;

/// <summary>
/// Die Kopfangaben eines Laufs für <see cref="RunInfo"/>: dieselbe Datei wie <see cref="Run"/>, aber Ergebnisse,
/// Fakten und Kategorien werden beim Lesen nur überlesen, nicht als Objekte angelegt (Prüfung vom 2026-10-20: jeder
/// check und jedes Neuladen der Oberfläche las alle Läufe eines Rechners vollständig).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
internal sealed record RunHeader(string Schema, string Id, string HostName, DateTimeOffset EvaluatedAtUtc, DateOnly EvaluationDate,
    DateTimeOffset CollectedAtUtc, RunHeaderSnapshot Snapshot, RunHeaderCatalogue Catalogue, RunHeaderSummary Summary,
    string? ReEvaluationOf, RunFlags Flags)
{
    public RunInfo Info() => new(Id, HostName, EvaluatedAtUtc, EvaluationDate, CollectedAtUtc, Snapshot.Sha256, Catalogue.Version,
        ReEvaluationOf, Flags, Summary.Score, Summary.OpenHigh);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
internal sealed record RunHeaderSnapshot(string Sha256);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
internal sealed record RunHeaderCatalogue(string Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
internal sealed record RunHeaderSummary(int? Score, int OpenHigh);

/// <summary>Lesekontext für <see cref="RunHeader"/> (dieselben Optionen wie <see cref="RiegelJsonContext"/>, aber intern).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    AllowTrailingCommas = false,
    NumberHandling = JsonNumberHandling.Strict,
    MaxDepth = 64,
    Converters = [typeof(UtcDateTimeOffsetConverter)])]
[JsonSerializable(typeof(RunHeader))]
internal sealed partial class RunHeaderJsonContext : JsonSerializerContext
{
    /// <summary>Kopfangaben aus den Bytes einer Lauf-Datei; ungültiges JSON → RGL-0004 mit dem Dateinamen.</summary>
    public static RunInfo Read(byte[] bytes, string fileName)
    {
        ReadOnlySpan<byte> utf8 = bytes;
        if (utf8.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) utf8 = utf8[3..];
        try
        {
            return (JsonSerializer.Deserialize(utf8, Default.RunHeader)
                    ?? throw RiegelException.Common(RglCodesCommon.InvalidJson, fileName)).Info();
        }
        catch (JsonException e)
        {
            throw new RiegelException(RglCodesCommon.InvalidJson, RglCodesCommon.Message(RglCodesCommon.InvalidJson, fileName + ": " + (e.Path ?? "$")), e);
        }
    }
}
