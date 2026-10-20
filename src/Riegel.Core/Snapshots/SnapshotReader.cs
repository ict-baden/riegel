// SPDX-License-Identifier: MIT
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Parsing;
using Riegel.Core.Services;

namespace Riegel.Core.Snapshots;

/// <summary>
/// Liest Exporte (spec 7.3, 7.12): <c>.snapshot.json</c> und <c>.json.gz</c> (am Inhalt erkannt, Entpacken gedeckelt),
/// Bordmittel-Pakete (Ordner, spec 7.5). Prüft Schema und Version und alle Grenzen für feindliche Eingaben (Größe,
/// Einträge, Länge der Zeichenketten, JSON-Tiefe), die Form der Einträge und doppelte Schlüssel; schwärzt Geheimnisse
/// erneut und berechnet <c>parsed:*</c> neu (<see cref="DerivedItems"/>). Felder des Rechnerkopfs dürfen null sein;
/// die Abdeckung kommt unverändert aus dem Kopf.
/// </summary>
internal sealed class SnapshotReader : ISnapshotReader
{
    private static readonly string[] KeyPrefixes = ["reg:", "wmi:", "api:", "evt:", "raw:", "parsed:"];
    private static readonly HashSet<string> RegValueTypes = new(StringComparer.Ordinal) { "dword", "qword", "sz", "expand_sz", "multi_sz", "binary", "none", "unknown" };
    private static readonly HashSet<string> RegValueProperties = new(StringComparer.Ordinal) { "t", "v", "redacted", "empty", "truncated", "len" };
    private static readonly HashSet<string> RegItemProperties = new(StringComparer.Ordinal) { "values", "subkeys", "valueFilter" };

    public Snapshot Read(string path, ReadLimits l)
    {
        ArgumentNullException.ThrowIfNull(l);
        if (Directory.Exists(path)) return FromBuiltinBundle(path, l);
        var bytes = ReadBytes(path, l);
        Prescan(bytes, l, Path.GetFileName(path));
        var snapshot = RiegelJson.Deserialize<Snapshot>(bytes);
        // Fehlende Pflichtfelder sind ein ungültiger Export (RGL-0257), kein Programmfehler (Prüfung vom 2026-10-20)
        if (snapshot.Schema is null) throw Invalid(Path.GetFileName(path), "schema");
        if (!string.Equals(snapshot.Schema, SchemaIds.Snapshot, StringComparison.Ordinal))
        {
            if (snapshot.Schema.StartsWith("riegel.snapshot/", StringComparison.Ordinal))
                throw RglCodesParsing.Error(RglCodesParsing.SnapshotVersion, snapshot.Schema, SchemaIds.Snapshot);
            throw RiegelException.Common(RglCodesCommon.UnsupportedSchema, snapshot.Schema);
        }
        Validate(snapshot, l, Path.GetFileName(path));
        return DerivedItems.Process(snapshot, l);
    }

    public HashCheck VerifySidecar(string path)
    {
        if (Directory.Exists(path)) return new HashCheck(HashCheckStatus.Missing, null, BuiltinBundleReader.BundleSha256(path), null);
        if (!File.Exists(path)) throw RiegelException.Common(RglCodesCommon.NotFound, path);
        var actual = Hashes.Sha256File(path);
        var sidecar = SnapshotFile.SidecarPath(path);
        if (!File.Exists(sidecar)) return new HashCheck(HashCheckStatus.Missing, null, actual, null);
        // Eine Begleitdatei ist eine Zeile; mehr als 4 KB ist keine
        byte[] head;
        using (var stream = new FileStream(sidecar, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length > 4096) return new HashCheck(HashCheckStatus.Malformed, null, actual, sidecar);
            head = new byte[stream.Length];
            stream.ReadExactly(head);
        }
        string text;
        try
        {
            var span = head.AsSpan();
            if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) span = span[3..];
            text = TextFiles.StrictUtf8.GetString(span);
        }
        catch (DecoderFallbackException)
        {
            return new HashCheck(HashCheckStatus.Malformed, null, actual, sidecar);
        }
        if (!SnapshotFile.TryParseSidecar(text, out var expected, out var name)
            || !string.Equals(name, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase))
            return new HashCheck(HashCheckStatus.Malformed, null, actual, sidecar);
        return new HashCheck(expected == actual ? HashCheckStatus.Ok : HashCheckStatus.Mismatch, expected, actual, sidecar);
    }

    public Snapshot FromBuiltinBundle(string folder, ReadLimits l) => BuiltinBundleReader.Read(folder, l);

    // ---------------------------------------------------------------- Bytes

    /// <summary>Datei lesen; gzip (Magic 1F 8B) wird gedeckelt entpackt (Schutz gegen gzip-Bomben).</summary>
    internal static byte[] ReadBytes(string path, ReadLimits l)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw RiegelException.Common(RglCodesCommon.NotFound, path);
        if (info.Length > l.MaxSnapshotBytes) throw RglCodesParsing.Error(RglCodesParsing.SnapshotTooLarge, info.Name, l.MaxSnapshotBytes);
        var raw = TextFiles.ReadCapped(path, l.MaxSnapshotBytes, RglCodesParsing.SnapshotTooLarge);
        if (raw.Length < 2 || raw[0] != 0x1F || raw[1] != 0x8B)
        {
            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) throw RglCodesParsing.Error(RglCodesParsing.GzipInvalid, info.Name);
            return raw;
        }
        // Kopf (10 Bytes) und Abschluss (CRC32 und Länge, 8 Bytes) braucht jede gzip-Datei (RFC 1952)
        if (raw.Length < 18) throw RglCodesParsing.Error(RglCodesParsing.GzipInvalid, info.Name);
        try
        {
            using var gzip = new GZipStream(new MemoryStream(raw), CompressionMode.Decompress);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = gzip.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > l.MaxSnapshotBytes) throw RglCodesParsing.Error(RglCodesParsing.SnapshotTooLarge, info.Name, l.MaxSnapshotBytes);
                buffer.Write(chunk, 0, read);
            }
            if (buffer.Length == 0) throw RglCodesParsing.Error(RglCodesParsing.GzipInvalid, info.Name);
            return buffer.ToArray();
        }
        catch (InvalidDataException)
        {
            throw RglCodesParsing.Error(RglCodesParsing.GzipInvalid, info.Name);
        }
    }

    // ---------------------------------------------------------------- Vorprüfung

    /// <summary>Syntax (z. B. abgeschnittene Datei) und Tiefe prüfen, bevor Typen gebaut werden.</summary>
    internal static void Prescan(ReadOnlySpan<byte> utf8, ReadLimits l, string source)
    {
        if (utf8.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) utf8 = utf8[3..];
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            MaxDepth = l.MaxJsonDepth + 2,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        });
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray && reader.CurrentDepth >= l.MaxJsonDepth)
                    throw RglCodesParsing.Error(RglCodesParsing.JsonTooDeep, source, l.MaxJsonDepth);
            }
            if (reader.BytesConsumed == 0) throw RglCodesParsing.Error(RglCodesParsing.SnapshotNotJson, source, 0);
        }
        catch (JsonException e)
        {
            throw new RiegelException(RglCodesParsing.SnapshotNotJson,
                RglCodesParsing.Message(RglCodesParsing.SnapshotNotJson, source, (e.BytePositionInLine ?? 0).ToString(CultureInfo.InvariantCulture)), e);
        }
    }

    // ---------------------------------------------------------------- Inhalt

    /// <summary>Grenzen und Form prüfen (spec 7.12, schemas/snapshot.schema.json).</summary>
    internal static void Validate(Snapshot s, ReadLimits l, string source)
    {
        RequiredMembers(s, source);
        if (s.Items.Count > l.MaxItems) throw RglCodesParsing.Error(RglCodesParsing.TooManyItems, source, l.MaxItems);
        if (string.IsNullOrWhiteSpace(s.Machine.HostName) || s.Machine.HostName.Length > 255 || TextFiles.HasControlCharacters(s.Machine.HostName))
            throw Invalid(source, "machine.hostName");
        CheckLength(s.Machine.HostName, l.MaxStringChars, source);
        foreach (var c in s.Coverage)
        {
            if (!c.Root.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) || c.MaxDepth < 0) throw Invalid(source, "coverage");
            CheckLength(c.Root, l.MaxStringChars, source);
        }
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in s.Items)
        {
            if (!KeyPrefixes.Any(p => item.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase)) || TextFiles.HasControlCharacters(item.Key))
                throw Invalid(source, "key");
            if (item.Key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase) && !(item.Key[4..].Equals("HKLM", StringComparison.OrdinalIgnoreCase)
                || item.Key[4..].StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase)))
                throw Invalid(source, item.Key);
            CheckLength(item.Key, l.MaxStringChars, source);
            if (!keys.Add(item.Key)) throw Invalid(source, "duplicate " + item.Key);
            if (item.Error is not null) CheckLength(item.Error, l.MaxStringChars, source);
            if (item.DurationMs < 0) throw Invalid(source, item.Key);
            if (item.Status == ItemStatus.Ok && item.Value is null) throw Invalid(source, item.Key);
            if (item.Value is not { } value) continue;
            bool raw = item.Key.StartsWith("raw:", StringComparison.OrdinalIgnoreCase);
            long max = raw ? Math.Max(l.MaxTextFileBytes, (l.MaxRegistryPolBytes + 2) / 3 * 4) : l.MaxStringChars;
            CheckStrings(value, max, source, 0, l.MaxJsonDepth);
            if (item.Key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase) && item.Status is ItemStatus.Ok or ItemStatus.ValueMissing)
                CheckRegistryValue(value, source, item.Key);
        }
    }

    private static void CheckRegistryValue(JsonElement value, string source, string key)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object)
            throw Invalid(source, key);
        foreach (var top in value.EnumerateObject())
        {
            if (!RegItemProperties.Contains(top.Name)) throw Invalid(source, key);
            if (top.Name != "values" && (top.Value.ValueKind != JsonValueKind.Array || top.Value.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.String)))
                throw Invalid(source, key);
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in values.EnumerateObject())
        {
            if (!names.Add(v.Name)) throw Invalid(source, key);
            if (v.Value.ValueKind != JsonValueKind.Object || !v.Value.TryGetProperty("t", out var t) || t.ValueKind != JsonValueKind.String
                || !RegValueTypes.Contains(t.GetString()!))
                throw Invalid(source, key);
            foreach (var p in v.Value.EnumerateObject())
                if (!RegValueProperties.Contains(p.Name)) throw Invalid(source, key);
        }
    }

    private static void CheckStrings(JsonElement e, long max, string source, int depth, int maxDepth)
    {
        if (depth > maxDepth) throw RglCodesParsing.Error(RglCodesParsing.JsonTooDeep, source, maxDepth);
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                string text;
                // ein einzelnes Ersatzzeichen („\udc00“) lässt GetString scheitern: ungültiger Export, kein Programmfehler
                try { text = e.GetString()!; }
                catch (InvalidOperationException) { throw Invalid(source, "text"); }
                CheckLength(text, max, source);
                break;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray()) CheckStrings(x, max, source, depth + 1, maxDepth);
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    string name;
                    try { name = p.Name; }
                    catch (InvalidOperationException) { throw Invalid(source, "text"); }
                    CheckLength(name, max, source);
                    CheckStrings(p.Value, max, source, depth + 1, maxDepth);
                }
                break;
        }
    }

    private static void CheckLength(string text, long max, string source)
    {
        if (text.Length > max) throw RglCodesParsing.Error(RglCodesParsing.StringTooLong, source, max);
        if (HasLoneSurrogate(text)) throw Invalid(source, "text");
    }

    /// <summary>
    /// Pflichtfelder, die im JSON fehlen dürfen, ohne dass der Leser scheitert (fehlend = null): Sammler, Optionen, Rechner,
    /// leere Einträge in Listen. Ohne diese Prüfung endete ein solcher Export mit RGL-0001 statt RGL-0257.
    /// </summary>
    private static void RequiredMembers(Snapshot s, string source)
    {
        if (s.Collector is not { Name: not null, Version: not null, Clr: not null }) throw Invalid(source, "collector");
        if (s.Options is null) throw Invalid(source, "options");
        if (s.Machine is null) throw Invalid(source, "machine");
        foreach (var text in new[] { s.Machine.DnsDomain, s.Machine.EditionId, s.Machine.InstallationType, s.Machine.DisplayVersion,
                     s.Machine.Architecture, s.Machine.MachineGuid, s.Machine.OsLanguage, s.Collector.Name, s.Collector.Version, s.Collector.Clr })
            if (text is not null && HasLoneSurrogate(text)) throw Invalid(source, "text");
        if (s.Coverage.Any(c => c is null || c.Root is null || c.DeniedSubkeys.Any(d => d is null))) throw Invalid(source, "coverage");
        if (s.Items.Any(i => i is null || i.Key is null)) throw Invalid(source, "items");
    }

    /// <summary>Ein Ersatzzeichen ohne Partner (z. B. „\ud800“ im JSON) – kein gültiger Unicode-Text.</summary>
    private static bool HasLoneSurrogate(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { i++; continue; }
            if (char.IsSurrogate(text[i])) return true;
        }
        return false;
    }

    private static RiegelException Invalid(string source, string what) => RglCodesParsing.Error(RglCodesParsing.SnapshotInvalid, source, what);
}
