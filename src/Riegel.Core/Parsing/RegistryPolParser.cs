// SPDX-License-Identifier: MIT
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Core.Parsing;

/// <summary>Was ein Eintrag einer Registry.pol bewirkt.</summary>
internal enum PolEntryKind
{
    /// <summary>Wert setzen.</summary>
    Value,

    /// <summary>„**Del.&lt;Wert&gt;“: einen Wert löschen (Ziel in <see cref="PolEntry.Targets"/>).</summary>
    DeleteValue,

    /// <summary>„**DeleteValues“: durch „;“ getrennte Liste von Werten löschen.</summary>
    DeleteValues,

    /// <summary>„**DelVals“: alle Werte des Schlüssels löschen.</summary>
    DeleteAllValues,

    /// <summary>„**DeleteKeys“: durch „;“ getrennte Liste von Schlüsseln löschen.</summary>
    DeleteKeys,

    /// <summary>„**SecureKey“: Rechte des Schlüssels setzen.</summary>
    SecureKey,

    /// <summary>Nur den Schlüssel anlegen (Wert, Typ, Größe oder Daten fehlen oder sind null).</summary>
    CreateKey,

    /// <summary>Anderer Sondername mit „**“ (z. B. „**soft.“), auf der Microsoft-Seite nicht beschrieben – nicht ausgewertet.</summary>
    OtherSpecial,
}

/// <summary>Ein Eintrag <c>[key;value;type;size;data]</c>.</summary>
/// <param name="Key">Pfad ohne Hive (laut Microsoft „Do not include HKEY_LOCAL_MACHINE“).</param>
/// <param name="ValueName">Wertname wie in der Datei (auch Sondernamen).</param>
/// <param name="Targets">Bei DeleteValue/DeleteValues/DeleteKeys die betroffenen Namen.</param>
internal sealed record PolEntry(string Key, string ValueName, uint Type, byte[] Data, PolEntryKind Kind, IReadOnlyList<string> Targets)
{
    /// <summary>Wert in der Snapshot-Kodierung (nur für <see cref="PolEntryKind.Value"/>).</summary>
    internal RegValue Decode() => RegValue.FromRaw(Type, Data);

    /// <summary><c>HKLM\…</c> (Registry.pol des Computers).</summary>
    internal string MachineKey => @"HKLM\" + Key.Trim('\\');
}

/// <summary>
/// Leser für Registry.pol nach der Beschreibung von Microsoft: „Registry Policy File Format“,
/// https://learn.microsoft.com/en-us/previous-versions/windows/desktop/policy/registry-policy-file-format
/// (gesehen 2026-10-18, Seite vom 2018-05-31). Kopf: zwei DWORD, REGFILE_SIGNATURE 0x67655250 („PReg“) und
/// REGISTRY_FILE_VERSION 1. Rumpf: Einträge <c>[key;value;type;size;data]</c>; Sondernamen **DeleteValues,
/// **Del.&lt;Wert&gt;, **DelVals, **DeleteKeys, **SecureKey; fehlen Wert, Typ, Größe oder Daten (oder sind 0), wird nur
/// der Schlüssel angelegt.
/// Die Seite nennt die Zeichenkodierung nicht. Wie auf dem Testrechner gemessen (research-technik §3.2: 150 Bytes,
/// ein Eintrag) sind Klammern, Trennzeichen, Schlüssel und Wertname UTF-16 LE, Schlüssel und Wert mit NUL beendet,
/// Typ und Größe je ein 32-Bit-Wert (little endian), danach <c>size</c> Datenbytes.
/// </summary>
internal static class RegistryPolParser
{
    internal const uint Signature = 0x67655250;
    internal const uint Version = 1;

    /// <summary>Schutz vor feindlichen Dateien: Schlüssel und Wertnamen höchstens so lang (Zeichen).</summary>
    private const int MaxNameChars = 32767;

    /// <summary>Liest eine Registry.pol. Jede Abweichung vom Format → RGL-0270.</summary>
    internal static List<PolEntry> Parse(ReadOnlySpan<byte> bytes, string source, int maxEntries)
    {
        if (bytes.Length < 8) throw Invalid(source, 0);
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Signature) throw Invalid(source, 0);
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != Version) throw Invalid(source, 4);
        var entries = new List<PolEntry>();
        int pos = 8;
        while (pos < bytes.Length)
        {
            if (entries.Count >= maxEntries) throw RglCodesParsing.Error(RglCodesParsing.TooManyItems, source, maxEntries);
            int entryStart = pos;
            ExpectChar(bytes, ref pos, '[', source);
            var key = ReadString(bytes, ref pos, source);
            ExpectChar(bytes, ref pos, ';', source);
            var value = ReadString(bytes, ref pos, source);
            ExpectChar(bytes, ref pos, ';', source);
            uint type = ReadUInt32(bytes, ref pos, source);
            ExpectChar(bytes, ref pos, ';', source);
            uint size = ReadUInt32(bytes, ref pos, source);
            ExpectChar(bytes, ref pos, ';', source);
            if (size > bytes.Length - pos) throw Invalid(source, entryStart);
            var data = bytes.Slice(pos, (int)size).ToArray();
            pos += (int)size;
            ExpectChar(bytes, ref pos, ']', source);
            entries.Add(Classify(key, value, type, data));
        }
        return entries;
    }

    private static PolEntry Classify(string key, string value, uint type, byte[] data)
    {
        if (value.StartsWith("**", StringComparison.Ordinal))
        {
            if (value.StartsWith("**del.", StringComparison.OrdinalIgnoreCase))
                return new(key, value, type, data, PolEntryKind.DeleteValue, [value[6..]]);
            if (value.Equals("**DeleteValues", StringComparison.OrdinalIgnoreCase))
                return new(key, value, type, data, PolEntryKind.DeleteValues, SplitList(data));
            if (value.Equals("**DelVals", StringComparison.OrdinalIgnoreCase) || value.Equals("**DelVals.", StringComparison.OrdinalIgnoreCase))
                return new(key, value, type, data, PolEntryKind.DeleteAllValues, []);
            if (value.StartsWith("**DeleteKeys", StringComparison.OrdinalIgnoreCase))
                return new(key, value, type, data, PolEntryKind.DeleteKeys, SplitList(data));
            if (value.StartsWith("**SecureKey", StringComparison.OrdinalIgnoreCase))
                return new(key, value, type, data, PolEntryKind.SecureKey, []);
            return new(key, value, type, data, PolEntryKind.OtherSpecial, []);
        }
        // „If value, type, size, or data are missing or zero, only the registry key is created.“ (wörtlich genommen:
        // schon eines davon leer oder 0 genügt)
        if (value.Length == 0 || type == 0 || data.Length == 0) return new(key, value, type, data, PolEntryKind.CreateKey, []);
        return new(key, value, type, data, PolEntryKind.Value, []);
    }

    /// <summary>Die Liste der Sondernamen steht als Text (REG_SZ) in den Daten, getrennt durch „;“.</summary>
    private static List<string> SplitList(byte[] data)
    {
        string text;
        try
        {
            text = data.Length % 2 == 0 ? TextFiles.StrictUtf16.GetString(data) : "";
        }
        catch (DecoderFallbackException)
        {
            text = "";
        }
        return [.. text.TrimEnd('\0').Split(';').Select(s => s.Trim().TrimEnd('\0')).Where(s => s.Length > 0)];
    }

    private static void ExpectChar(ReadOnlySpan<byte> bytes, ref int pos, char c, string source)
    {
        if (pos + 2 > bytes.Length || bytes[pos] != (byte)c || bytes[pos + 1] != 0) throw Invalid(source, pos);
        pos += 2;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, ref int pos, string source)
    {
        if (pos + 4 > bytes.Length) throw Invalid(source, pos);
        var v = BinaryPrimitives.ReadUInt32LittleEndian(bytes[pos..]);
        pos += 4;
        return v;
    }

    /// <summary>UTF-16-LE-Zeichenkette bis NUL (das NUL wird verbraucht).</summary>
    private static string ReadString(ReadOnlySpan<byte> bytes, ref int pos, string source)
    {
        int start = pos;
        while (true)
        {
            if (pos + 2 > bytes.Length) throw Invalid(source, start);
            if (bytes[pos] == 0 && bytes[pos + 1] == 0) break;
            pos += 2;
            if ((pos - start) / 2 > MaxNameChars) throw Invalid(source, start);
        }
        string text;
        try
        {
            text = TextFiles.StrictUtf16.GetString(bytes[start..pos]);
        }
        catch (DecoderFallbackException)
        {
            throw Invalid(source, start);
        }
        pos += 2;
        return text;
    }

    private static RiegelException Invalid(string source, int offset) =>
        RglCodesParsing.Error(RglCodesParsing.RegistryPolInvalid, source, offset);

    /// <summary>
    /// Wert von <c>parsed:registry-pol</c>: <c>[ { key, value, t, v } ]</c> für die gesetzten Werte. Lösch-, Anlege- und
    /// Rechte-Einträge sind keine Werte und stehen nicht darin (der Baseline-Import wertet **Del. aus). Geheime
    /// Wertnamen werden geschwärzt.
    /// </summary>
    internal static JsonElement ToParsedItem(IReadOnlyList<PolEntry> entries, int maxStringChars) => JsonValues.Build(w =>
    {
        w.WriteStartArray();
        foreach (var e in entries)
        {
            if (e.Kind != PolEntryKind.Value) continue;
            RegValue value;
            try
            {
                value = e.Decode();
            }
            catch (FormatException)
            {
                value = RegValue.Bytes("unknown", e.Data);
            }
            if (Secrets.ShouldRedact(e.ValueName, value.Type)) value = RegValue.RedactedValue(value.Type, value.IsEmpty);
            w.WriteStartObject();
            w.WriteString("key", e.MachineKey);
            w.WriteString("value", e.ValueName);
            // t und v wie in einem reg:-Eintrag
            using var inner = new MemoryStream();
            using (var iw = new Utf8JsonWriter(inner)) value.WriteTo(iw, maxStringChars);
            using var doc = JsonDocument.Parse(inner.ToArray());
            foreach (var p in doc.RootElement.EnumerateObject()) p.WriteTo(w);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    });

    /// <summary>
    /// Registry.pol mit geschwärzten Daten bei geheimen Wertnamen (Größe 0). Gibt null zurück, wenn nichts zu tun war.
    /// </summary>
    internal static byte[]? Redact(ReadOnlySpan<byte> original, IReadOnlyList<PolEntry> entries)
    {
        if (!entries.Any(e => IsSecret(e) && e.Data.Length > 0)) return null;
        using var stream = new MemoryStream();
        stream.Write(original[..8]);
        Span<byte> buffer = stackalloc byte[4];
        foreach (var e in entries)
        {
            bool secret = IsSecret(e);
            var data = secret ? [] : e.Data;
            WriteChar(stream, '[');
            WriteString(stream, e.Key);
            WriteChar(stream, ';');
            WriteString(stream, e.ValueName);
            WriteChar(stream, ';');
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, e.Type);
            stream.Write(buffer);
            WriteChar(stream, ';');
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)data.Length);
            stream.Write(buffer);
            WriteChar(stream, ';');
            stream.Write(data);
            WriteChar(stream, ']');
        }
        return stream.ToArray();
    }

    private static bool IsSecret(PolEntry e) => e.Kind == PolEntryKind.Value && Secrets.ShouldRedact(e.ValueName, RegTypes.Name(e.Type));

    private static void WriteChar(Stream s, char c)
    {
        s.WriteByte((byte)c);
        s.WriteByte(0);
    }

    private static void WriteString(Stream s, string text)
    {
        s.Write(Encoding.Unicode.GetBytes(text));
        s.WriteByte(0);
        s.WriteByte(0);
    }
}
