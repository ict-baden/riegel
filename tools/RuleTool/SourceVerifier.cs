// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Riegel.Core.Model;

namespace Riegel.Tools.RuleTool;

/// <summary>
/// verify-sources: prüft je Regel, ob ihre Verweise (ACSC-Abschnitt, MSB-Einstellung, STIG-ID, GS++- und
/// ATT&amp;CK-Kennungen) in lokalen Kopien der Quellen vorkommen, die der Entwickler heruntergeladen hat. So wird nach einer
/// neuen Fassung einer Quelle sichtbar, welche Verweise nicht mehr stimmen. Die Kopien werden nur gelesen, nie
/// ausgeführt und nie ins Repo übernommen.
/// </summary>
public static partial class SourceVerifier
{
    /// <summary>Grenzen gegen versehentlich riesige Ordner.</summary>
    public const long MaxFileBytes = 64L * 1024 * 1024;
    public const long MaxTotalBytes = 1024L * 1024 * 1024;

    private static readonly string[] TextExtensions =
        [".txt", ".md", ".csv", ".tsv", ".json", ".xml", ".html", ".htm", ".inf", ".admx", ".adml", ".policyrules", ".yml", ".yaml"];

    /// <summary>Ergebnis eines Verweises.</summary>
    public sealed record ReferenceCheck(string RuleId, string Source, string Token, bool? Found);

    /// <summary>Text aller lesbaren Dateien unter den Ordnern, klein geschrieben und mit einfachen Leerzeichen.</summary>
    public sealed class Corpus
    {
        private readonly List<(string File, string Text)> _texts = [];

        public IReadOnlyList<string> Files => _texts.Select(t => t.File).ToList();
        public List<string> Skipped { get; } = [];

        public static Corpus Load(IEnumerable<string> folders)
        {
            var corpus = new Corpus();
            long total = 0;
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) throw new RuleToolException(RglCodesCatalogue.SourceFolderNotFound, folder);
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    var info = new FileInfo(file);
                    if (info.Length > MaxFileBytes || total + info.Length > MaxTotalBytes) { corpus.Skipped.Add(file); continue; }
                    total += info.Length;
                    try
                    {
                        foreach (var text in Extract(file, File.ReadAllBytes(file), 0))
                            corpus._texts.Add((file, Normalize(text)));
                    }
                    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { corpus.Skipped.Add(file); }
                }
            }
            return corpus;
        }

        /// <summary>Kommt <paramref name="token"/> vor? Bei Kennungen zählt nur ein Treffer an Wortgrenzen.</summary>
        public bool Contains(string token, bool identifier)
        {
            var needle = Normalize(token);
            if (needle.Length == 0) return false;
            foreach (var (_, text) in _texts)
            {
                for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + 1, StringComparison.Ordinal))
                {
                    if (!identifier || IsBoundary(text, i, needle.Length)) return true;
                }
            }
            return false;
        }

        private static bool IsBoundary(string text, int start, int length)
        {
            if (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '.' || text[start - 1] == '-')) return false;
            var end = start + length;
            if (end < text.Length && char.IsLetterOrDigit(text[end])) return false;
            if (end + 1 < text.Length && (text[end] == '.' || text[end] == '-') && char.IsDigit(text[end + 1])) return false;
            return true;
        }
    }

    /// <summary>Prüft alle Regeln des Repositorys gegen den Text der Quellordner.</summary>
    public static IReadOnlyList<ReferenceCheck> Verify(RuleRepository repo, Corpus corpus)
    {
        var results = new List<ReferenceCheck>();
        foreach (var rule in repo.Rules.OrderBy(r => r.Id, RuleIdComparer.Instance))
            foreach (var reference in rule.References)
                foreach (var (token, identifier) in Tokens(reference))
                    results.Add(new ReferenceCheck(rule.Id, reference.Src, token, token.Length == 0 ? null : corpus.Contains(token, identifier)));
        return results;
    }

    /// <summary>Die prüfbaren Teile eines Verweises; Quellen nur mit URL (MSDOCS-CCBY, MSLEARN-FACT, ENDOFLIFE) liefern einen leeren Platzhalter.</summary>
    public static IEnumerable<(string Token, bool Identifier)> Tokens(RuleReference reference)
    {
        switch (reference.Src)
        {
            case "ACSC":
                if (reference.Section is not null) yield return (reference.Section, false);
                break;
            case "MSB":
                if (reference.Setting is not null) yield return (reference.Setting, false);
                break;
            case "DISA":
                if (reference.Id is not null) yield return (reference.Id, true);
                break;
            case "GSPP":
            case "ATTACK":
                foreach (var id in (reference.Ids ?? []).Concat(reference.Id is null ? [] : [reference.Id])) yield return (id, true);
                break;
            default:
                yield return ("", false);
                break;
        }
    }

    /// <summary>Textbausteine einer Datei; ZIP-Container (auch .xlsx/.docx) werden eine Ebene tief gelesen.</summary>
    public static IEnumerable<string> Extract(string name, byte[] bytes, int depth)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext is ".zip" or ".xlsx" or ".docx")
        {
            if (depth > 1) yield break;
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                if (entry.Length > MaxFileBytes || entry.FullName.EndsWith('/')) continue;
                var entryExt = Path.GetExtension(entry.FullName).ToLowerInvariant();
                if (!TextExtensions.Contains(entryExt) && entryExt is not (".zip" or ".xlsx" or ".docx")) continue;
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                foreach (var text in Extract(entry.FullName, buffer.ToArray(), depth + 1)) yield return text;
            }
            yield break;
        }
        if (!TextExtensions.Contains(ext)) yield break;
        var decoded = Decode(bytes);
        yield return ext is ".xml" or ".html" or ".htm" or ".admx" or ".adml" or ".policyrules" ? StripTags(decoded) : decoded;
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        return new UTF8Encoding(false, false).GetString(bytes);
    }

    private static string StripTags(string xml) => WebUtility.HtmlDecode(Tags().Replace(xml, " "));

    /// <summary>Klein geschrieben, typografische Zeichen vereinheitlicht, Leerraum zusammengefasst.</summary>
    public static string Normalize(string text)
    {
        var t = text.ToLowerInvariant().Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"')
            .Replace('–', '-').Replace('—', '-').Replace(' ', ' ');
        return Spaces().Replace(t, " ").Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
