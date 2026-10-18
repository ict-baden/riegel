// SPDX-License-Identifier: MIT
using System.Buffers.Binary;
using System.Text;
using Riegel.Core.Model;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Parsing;

/// <summary>
/// Handgeschriebene Beispiele für die Parser-Tests (eigene, ausgedachte Inhalte – keine Dateien von Microsoft, dem BSI
/// oder echten Rechnern). GPO-Namen, GUIDs und Hostnamen sind erfunden.
/// </summary>
internal static class ParsingSamples
{
    /// <summary>Text als UTF-16 LE mit BOM und CRLF (wie secedit /export und reg export).</summary>
    internal static byte[] Utf16Bom(string text) => [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(text.Replace("\r\n", "\n").Replace("\n", "\r\n"))];

    /// <summary>Text als ASCII/UTF-8 mit CRLF.</summary>
    internal static byte[] Crlf(string text) => Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n").Replace("\n", "\r\n"));

    /// <summary>Registry.pol aus Einträgen (Kopf PReg/1, Einträge [key;value;type;size;data]).</summary>
    internal static byte[] Pol(params (string Key, string Value, uint Type, byte[] Data)[] entries)
    {
        using var stream = new MemoryStream();
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(four, 0x67655250);
        stream.Write(four);
        BinaryPrimitives.WriteUInt32LittleEndian(four, 1);
        stream.Write(four);
        foreach (var (key, value, type, data) in entries)
        {
            Char(stream, '[');
            Str(stream, key);
            Char(stream, ';');
            Str(stream, value);
            Char(stream, ';');
            BinaryPrimitives.WriteUInt32LittleEndian(four, type);
            stream.Write(four);
            Char(stream, ';');
            BinaryPrimitives.WriteUInt32LittleEndian(four, (uint)data.Length);
            stream.Write(four);
            Char(stream, ';');
            stream.Write(data);
            Char(stream, ']');
        }
        return stream.ToArray();
    }

    internal static byte[] Dword(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }

    internal static byte[] Sz(string s) => Encoding.Unicode.GetBytes(s + "\0");

    internal static byte[] MultiSz(params string[] parts) => Encoding.Unicode.GetBytes(string.Join("\0", parts) + "\0\0");

    private static void Char(Stream s, char c)
    {
        s.WriteByte((byte)c);
        s.WriteByte(0);
    }

    private static void Str(Stream s, string text)
    {
        s.Write(Encoding.Unicode.GetBytes(text));
        s.WriteByte(0);
        s.WriteByte(0);
    }

    /// <summary>
    /// gpresult-XML nach dem Aufbau der Ausgabe von <c>gpresult /scope computer /x</c> (Namensräume wie auf dem
    /// Testrechner beobachtet). <paramref name="settings"/> sind fertige Elemente innerhalb von ExtensionData.
    /// </summary>
    internal static string Gpresult(IEnumerable<(string Name, string Id)> gpos, params string[] extensions)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-16\"?>\n");
        sb.Append("<Rsop xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns=\"http://www.microsoft.com/GroupPolicy/Rsop\">\n");
        sb.Append("  <ComputerResults>\n    <Name>TEST-HOST</Name>\n");
        foreach (var (name, id) in gpos)
        {
            sb.Append("    <GPO>\n      <Name>").Append(name).Append("</Name>\n      <Path>\n        <Identifier xmlns=\"http://www.microsoft.com/GroupPolicy/Types\">")
              .Append(id).Append("</Identifier>\n      </Path>\n      <Enabled>true</Enabled>\n      <IsValid>true</IsValid>\n")
              .Append("      <FilterAllowed>true</FilterAllowed>\n      <AccessDenied>false</AccessDenied>\n    </GPO>\n");
        }
        foreach (var e in extensions) sb.Append(e);
        sb.Append("  </ComputerResults>\n</Rsop>\n");
        return sb.ToString();
    }

    /// <summary>Eine Erweiterung (ExtensionData/Extension) mit xsi:type, z. B. „q1:RegistrySettings“.</summary>
    internal static string Extension(string type, string ns, params string[] settings) =>
        "    <ExtensionData>\n      <Extension xmlns:q1=\"" + ns + "\" xsi:type=\"q1:" + type + "\" xmlns=\"http://www.microsoft.com/GroupPolicy/Settings\">\n"
        + string.Concat(settings) + "      </Extension>\n      <Name xmlns=\"http://www.microsoft.com/GroupPolicy/Settings\">" + type + "</Name>\n    </ExtensionData>\n";

    internal const string RegistryNs = "http://www.microsoft.com/GroupPolicy/Settings/Registry";
    internal const string SecurityNs = "http://www.microsoft.com/GroupPolicy/Settings/Security";

    /// <summary>Kopf einer Einstellung: GPO-Kennung und Rang.</summary>
    internal static string GpoRef(string id, int precedence = 1) =>
        "<GPO xmlns=\"http://www.microsoft.com/GroupPolicy/Settings/Base\"><Identifier xmlns=\"http://www.microsoft.com/GroupPolicy/Types\">" + id
        + "</Identifier></GPO><Precedence xmlns=\"http://www.microsoft.com/GroupPolicy/Settings/Base\">"
        + precedence.ToString(System.Globalization.CultureInfo.InvariantCulture) + "</Precedence>";

    internal static string RegistrySetting(string gpoId, string keyPath, string? valueName, string? data = null, int precedence = 1) =>
        "        <q1:RegistrySetting>" + GpoRef(gpoId, precedence) + "<q1:KeyPath>" + keyPath + "</q1:KeyPath>"
        + (valueName is null ? "" : "<q1:Value><q1:Name>" + valueName + "</q1:Name>" + (data is null ? "<q1:Number>1</q1:Number>" : "<q1:String>" + data + "</q1:String>") + "</q1:Value>")
        + "<q1:AdmSetting>false</q1:AdmSetting></q1:RegistrySetting>\n";

    internal static string Policy(string gpoId, string name) =>
        "        <q1:Policy>" + GpoRef(gpoId) + "<q1:Name>" + name + "</q1:Name><q1:State>Enabled</q1:State></q1:Policy>\n";

    internal static string SecurityOption(string gpoId, string keyName, int number) =>
        "        <q1:SecurityOptions>" + GpoRef(gpoId) + "<q1:KeyName>" + keyName + "</q1:KeyName><q1:SettingNumber>"
        + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "</q1:SettingNumber></q1:SecurityOptions>\n";

    internal static string Account(string gpoId, string name, int number) =>
        "        <q1:Account>" + GpoRef(gpoId) + "<q1:Name>" + name + "</q1:Name><q1:SettingNumber>"
        + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "</q1:SettingNumber><q1:Type>Password</q1:Type></q1:Account>\n";

    /// <summary>Ein Export mit Rohdaten (Herkunft collector), aus dem der Leser parsed:* berechnet.</summary>
    internal static SnapshotBuilder Collected(string host = "W11-PARSE") =>
        SnapshotBuilder.Windows11Workgroup(host).Origin(SnapshotOrigin.Collector);
}

/// <summary>
/// Test mit aufgezeichneten Rohdaten der Runner (Fixtures/recorded/raw, kommt mit dem Paket collector); übersprungen,
/// solange der Ordner fehlt. Der Integrationsschritt aktiviert ihn damit von selbst.
/// </summary>
public sealed class RecordedRawFactAttribute : FactAttribute
{
    public RecordedRawFactAttribute()
    {
        if (!Directory.Exists(RecordedRaw.Folder)) Skip = "Fixtures/recorded/raw fehlt noch (Paket collector).";
    }
}

/// <summary>
/// Ort der aufgezeichneten Rohdaten. <c>RIEGEL_RECORDED_RAW</c> zeigt für einen lokalen Lauf auf einen anderen Ordner
/// (z. B. eigene Exporte, die nie ins Repo gehören).
/// </summary>
internal static class RecordedRaw
{
    internal static string Folder =>
        Environment.GetEnvironmentVariable("RIEGEL_RECORDED_RAW") is { Length: > 0 } local ? local : Path.Combine(RepoPaths.RecordedFixtures, "raw");

    /// <summary>Dateien unter raw/ (rekursiv), deren Name zum Muster passt (z. B. „*.inf“).</summary>
    internal static IReadOnlyList<string> Files(string pattern) =>
        Directory.Exists(Folder) ? [.. Directory.EnumerateFiles(Folder, pattern, SearchOption.AllDirectories).Order(StringComparer.Ordinal)] : [];
}
