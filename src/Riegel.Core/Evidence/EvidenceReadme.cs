// SPDX-License-Identifier: MIT
using System.Text;
using Riegel.Core.About;
using Riegel.Core.Model;
using Riegel.Core.Resources;

namespace Riegel.Core.Evidence;

/// <summary>
/// README.txt des Nachweispakets in der Berichtssprache: Inhalt, Prüfsummen prüfen, <c>verify</c>,
/// <c>compare-snapshot</c>, die Integritätsaussage (Deutsch und Englisch, dazu die Berichtssprache) und der Hinweis
/// zu Baselines. UTF-8 ohne BOM, CRLF (für Prüfer unter Windows).
/// </summary>
internal static class EvidenceReadme
{
    public static byte[] Build(EvidenceWriter.PackContent c, OutputText t)
    {
        var r = c.Request;
        var lines = new List<string>
        {
            t.Get(nameof(DataOutputStrings.EvidenceReadme_Title), r.Plant.Name),
            new('=', 72),
            "",
            t.Get(nameof(DataOutputStrings.EvidenceReadme_Intro), AppInfo.Name, AppInfo.Commit is { } commit ? AppInfo.Version + " (" + commit + ")" : AppInfo.Version,
                OutputText.Date(r.EvaluationDate)),
            t.Get(nameof(DataOutputStrings.EvidenceReadme_Catalogue), c.Catalogue.Id, c.Catalogue.Version, c.Catalogue.Sha256),
            "",
            t.Get(nameof(DataOutputStrings.EvidenceReadme_ContentsHeading)),
            new('-', 72),
            "  manifest.json            " + t.Get(nameof(DataOutputStrings.EvidenceReadme_ContentManifest)),
            "  SHA256SUMS.txt           " + t.Get(nameof(DataOutputStrings.EvidenceReadme_ContentChecksums)),
        };
        foreach (var document in c.Documents) lines.Add("  " + document.PadRight(24) + " " + t.Get(nameof(DataOutputStrings.EvidenceReadme_ContentDocument)));
        // Ordner nur nennen, wenn es sie im Paket gibt
        void Folder(string name, string text)
        {
            if (c.Paths.Any(p => p.StartsWith(name + "/", StringComparison.Ordinal))) lines.Add("  " + (name + "/").PadRight(24) + " " + text);
        }
        Folder("runs", t.Get(nameof(DataOutputStrings.EvidenceReadme_ContentRuns), c.Runs.Count));
        Folder("snapshots", t.Get(nameof(DataOutputStrings.EvidenceReadme_ContentSnapshots)));
        Folder("registers", t.Get(nameof(DataOutputStrings.EvidenceReadme_ContentRegisters)));
        Folder("plant", t.Get(nameof(DataOutputStrings.EvidenceReadme_ContentPlant)));
        Folder("catalogue", t.Get(nameof(DataOutputStrings.EvidenceReadme_ContentCatalogue)));
        Folder("baselines", t.Get(nameof(DataOutputStrings.EvidenceReadme_ContentBaselines)));
        Folder("comparison", t.Get(nameof(DataOutputStrings.EvidenceReadme_ContentComparison)));
        Folder("imports", t.Get(nameof(DataOutputStrings.EvidenceReadme_ContentImports)));
        lines.Add("");
        lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_NoBuiltinBundle)));
        lines.Add("");

        lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_HashHeading)));
        lines.Add(new('-', 72));
        lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_HashLinux)));
        lines.Add("    sha256sum -c SHA256SUMS.txt");
        lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_HashWindows)));
        lines.Add("    Get-FileHash -Algorithm SHA256 <file>");
        lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_HashCompare)));
        lines.Add("");

        lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_VerifyHeading)));
        lines.Add(new('-', 72));
        lines.Add("    Riegel.Cli.exe verify " + ReportFileNames.Evidence(r.Plant.Name, r.EvaluationDate));
        lines.Add("    riegel verify " + ReportFileNames.Evidence(r.Plant.Name, r.EvaluationDate));
        lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_VerifyText)));
        lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_VerifyExit)));
        lines.Add("");

        lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_SampleHeading)));
        lines.Add(new('-', 72));
        lines.Add("    Riegel.Cli.exe compare-snapshot <HOST>_<yyyyMMddTHHmmssZ>.snapshot.json --evidence "
                  + ReportFileNames.Evidence(r.Plant.Name, r.EvaluationDate));
        lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_SampleText)));
        lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_SampleExit)));
        lines.Add("");

        lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_IntegrityHeading)));
        lines.Add(new('-', 72));
        foreach (var language in IntegrityLanguages(t.Language))
        {
            lines.Add(OutputText.For(language).Common("Integrity_Statement"));
            lines.Add("");
        }

        lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_BaselineHeading)));
        lines.Add(new('-', 72));
        if (c.Baselines.Count == 0) lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_BaselineNone)));
        foreach (var b in c.Baselines)
            lines.Add("  " + b.Baseline.Id + " – " + b.Baseline.Name + " (SHA-256 " + b.Baseline.SourceSha256 + "): "
                      + (b.ContentIncluded
                          ? t.Get(nameof(DataOutputStrings.EvidenceReadme_BaselineIncluded))
                          : t.Get(nameof(DataOutputStrings.EvidenceReadme_BaselineMetadataOnly))));
        if (c.Baselines.Any(b => !b.ContentIncluded)) lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_BaselineNotVerifiable)));
        if (c.Baselines.Any(b => b.ContentIncluded)) lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_LicenseWarning)));
        lines.Add("");

        if (c.Notes.Count > 0)
        {
            lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_NotesHeading)));
            lines.Add(new('-', 72));
            lines.AddRange(c.Notes.Select(n => "  - " + n));
            lines.Add("");
        }
        lines.Add(t.Get(nameof(DataOutputStrings.EvidenceReadme_Confidential)));
        return Encode(lines);
    }

    /// <summary>baselines/LICENSE-WARNING.txt: die Lizenzwarnung in der Berichtssprache und auf Englisch.</summary>
    public static byte[] LicenseWarningFile(OutputText t)
    {
        var lines = new List<string> { t.Get(nameof(DataOutputStrings.EvidenceReadme_LicenseWarning)) };
        if (t.Language != "en") lines.Add(OutputText.For("en").Get(nameof(DataOutputStrings.EvidenceReadme_LicenseWarning)));
        return Encode(lines);
    }

    /// <summary>Die Integritätsaussage steht auf Deutsch und Englisch (spec 5), dazu in der Berichtssprache.</summary>
    internal static IEnumerable<string> IntegrityLanguages(string reportLanguage)
    {
        var list = new List<string> { reportLanguage };
        foreach (var l in new[] { "de", "en" }) if (!list.Contains(l)) list.Add(l);
        return list;
    }

    private static byte[] Encode(IEnumerable<string> lines) =>
        new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines) + "\r\n");
}
