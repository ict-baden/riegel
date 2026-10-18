// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Riegel.Core.Model;

namespace Riegel.Core.Evidence;

/// <summary>
/// Aufbau des Nachweispakets (spec 5 O9, docs/evidence.md). Pfade im Paket immer mit „/“, alle Namensteile aus
/// eigenen, bereinigten Angaben (Hostname, Lauf-ID, Hash) – nie ungeprüft aus Dateien des Anlagenordners.
/// </summary>
internal static partial class EvidenceLayout
{
    public const string Manifest = "manifest.json";
    public const string Checksums = "SHA256SUMS.txt";
    public const string Readme = "README.txt";
    public const string PlantFile = "plant/riegel-plant.json";
    public const string BaselineLicenseNote = "baselines/LICENSE-WARNING.txt";

    public static string Catalogue(string version) => $"catalogue/riegel-catalogue-{FileNames.Token(version)}.json";

    public static string RunFile(string host, string runId) => $"runs/{FileNames.Host(host)}/{runId}.results.json";

    public static string SnapshotFile(string host, string fileName) => $"snapshots/{FileNames.Host(host)}/{fileName}";

    public static string Register(RegisterKind kind, string sha256) => $"registers/{OutputText.Camel(kind)}_{Hashes.Short(sha256)}.json";

    public static string BaselineMetadata(string id) => $"baselines/{FileNames.Token(id)}.metadata.json";

    public static string BaselineContent(string id) => $"baselines/{FileNames.Token(id)}.json";

    public static string BaselineRules(string id) => $"baselines/{FileNames.Token(id)}.rules.json";

    public static string Comparison(string host, ComparisonBasis basis) => $"comparison/{FileNames.Host(host)}.{OutputText.Camel(basis)}.json";

    public static string Import(string id) => $"imports/{id}.import.json";

    /// <summary>Dateiname eines Exports, wie ihn Sammler und Anlagenspeicher anlegen (O1).</summary>
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,120}\.snapshot\.json(\.gz)?$", RegexOptions.CultureInvariant)]
    public static partial Regex SnapshotName();

    /// <summary>Kennung eines Imports (<c>&lt;yyyyMMddTHHmmssZ&gt;-&lt;6 hex&gt;</c>).</summary>
    [GeneratedRegex(@"^\d{8}T\d{6}Z-[0-9a-f]{6}$", RegexOptions.CultureInvariant)]
    public static partial Regex ImportId();

    /// <summary>Name einer Berichtsdatei im Paket (Wurzel): nur <c>[A-Za-z0-9._-]</c>, mit Endung.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,150}\.(pdf|xlsx|csv|json)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    public static partial Regex DocumentName();

    /// <summary>Eine Zeile in SHA256SUMS.txt (sha256sum-Format, Textmodus „␠␠“ oder Binärmodus „␠*“).</summary>
    [GeneratedRegex(@"^([0-9a-f]{64}) [ *](.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChecksumLine();

    /// <summary>SHA256SUMS.txt: eine Zeile je Datei, nach Pfad sortiert, LF, UTF-8 ohne BOM.</summary>
    public static byte[] ChecksumFile(IEnumerable<KeyValuePair<string, string>> pathToSha)
    {
        var sb = new StringBuilder();
        foreach (var (path, sha) in pathToSha.OrderBy(p => p.Key, StringComparer.Ordinal))
            sb.Append(sha).Append("  ").Append(path).Append('\n');
        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    /// <summary>Liest SHA256SUMS.txt; null bei falschem Format (leere Datei, doppelte Pfade, kaputte Zeilen, CRLF).</summary>
    public static SortedDictionary<string, string>? ParseChecksums(byte[] bytes)
    {
        string text;
        try { text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException) { return null; }
        if (text.Length == 0 || !text.EndsWith('\n') || text.Contains('\r')) return null;
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text[..^1].Split('\n'))
        {
            var m = ChecksumLine().Match(line);
            if (!m.Success) return null;
            var path = m.Groups[2].Value;
            if (!EvidenceArchive.IsSafeName(path, out bool directory) || directory) return null;
            if (!result.TryAdd(path, m.Groups[1].Value)) return null;
        }
        return result;
    }

    /// <summary>Kurzform einer Zahl für Meldungen.</summary>
    public static string N(long value) => value.ToString(CultureInfo.InvariantCulture);
}
