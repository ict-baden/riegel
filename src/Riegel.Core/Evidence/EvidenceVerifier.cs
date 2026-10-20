// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.About;
using Riegel.Core.Localization;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Resources;
using Riegel.Core.Services;

namespace Riegel.Core.Evidence;

/// <summary>
/// <c>verify</c> und <c>compare-snapshot</c> (spec 5 O9, 2.14, 7.7). Liest das Paket nur im Speicher, prüft
/// SHA256SUMS.txt und die Prüfsummen im Manifest, wertet jeden gepackten Export mit dem gepackten Katalog, der
/// gepackten Anlage und den gepackten Registern neu aus (mit der Auswertungsuhr des gepackten Laufs) und vergleicht
/// mit den gepackten Ergebnissen. Ergebnisse importierter Baselines ohne Inhalt sind „nicht nachprüfbar“; die Zählungen
/// und der Härtungsgrad werden trotzdem nachgerechnet (mit den gepackten Ergebnissen dieser Baselines).
/// Der gepackte Katalog wird mit dem eingebauten verglichen (<paramref name="builtIn"/>): Hätte er dieselbe Version und
/// einen anderen Inhalt, ließen sich Katalog und Ergebnisse gemeinsam fälschen, denn die erneute Auswertung nimmt den
/// gepackten Katalog.
/// </summary>
internal sealed class EvidenceVerifier(RiegelServices services, Func<ICatalogue> builtIn)
{
    /// <summary>Höchstzahl der Befunde; danach nur noch ein Sammelbefund.</summary>
    public const int MaxFindings = 1000;

    /// <summary>Codes, die „abweichend“ bedeuten (Exitcode 2). Alle anderen Befunde sind Hinweise.</summary>
    private static readonly HashSet<string> MismatchCodes =
    [
        RglCodesDataOutputs.FileHashMismatch, RglCodesDataOutputs.FileMissing, RglCodesDataOutputs.FileNotListed,
        RglCodesDataOutputs.CatalogueMismatch, RglCodesDataOutputs.ResultMismatch, RglCodesDataOutputs.SummaryMismatch,
        RglCodesDataOutputs.RunNotVerifiable, RglCodesDataOutputs.PackedFileUnreadable, RglCodesDataOutputs.RunManifestMismatch,
        RglCodesDataOutputs.CatalogueNotBuiltIn,
    ];

    /// <summary>
    /// Flags, die vom Verlauf abhängen (vorheriger Lauf, Katalog des vorherigen Laufs) und deshalb aus einem einzelnen
    /// Export nicht reproduzierbar sind.
    /// </summary>
    internal const ResultFlags HistoryFlags = ResultFlags.Regression | ResultFlags.NewRule;

    internal const RunFlags HistoryRunFlags = RunFlags.MachineIdentityChanged;

    private sealed class Findings
    {
        public List<VerifyFinding> List { get; } = [];
        private int _dropped;

        public void Add(string code, string? file, string? host, string? rule, string message)
        {
            if (List.Count < MaxFindings) List.Add(new VerifyFinding(code, file, host, rule, message));
            else _dropped++;
        }

        public void Close()
        {
            if (_dropped > 0)
                List.Add(new VerifyFinding(RglCodesDataOutputs.ResultMismatch, null, null, null,
                    RglCodesDataOutputs.Message(RglCodesDataOutputs.ResultMismatch, "…", "+" + _dropped.ToString(CultureInfo.InvariantCulture))));
        }

        public bool AnyMismatch => List.Any(f => MismatchCodes.Contains(f.Code)) || _dropped > 0;
    }

    // ================================================================ verify

    public VerifyResult Verify(string zipPath, ReadLimits l)
    {
        var engine = AppInfo.Version;
        EvidenceArchive archive;
        try { archive = EvidenceArchive.Read(zipPath, l); }
        catch (RiegelException e) { return Error(engine, null, e.Code, Path.GetFileName(zipPath), e.Message); }

        var findings = new Findings();
        if (!CheckIntegrity(archive, findings, out var manifest, out var error)) return Error(engine, null, error!.Code, error.File, error.Message);
        var packedEngine = manifest!.Tool.Version;

        // Katalog, Baselines, Anlage, Register aus dem Paket
        ICatalogue? core = null;
        bool builtInCatalogue = false;
        var catalogueBytes = archive.Get(manifest.CatalogueFile.Path);
        if (catalogueBytes is not null)
        {
            try
            {
                using var stream = new MemoryStream(catalogueBytes, writable: false);
                core = services.Catalogue.Load(stream, l);
                if (!string.Equals(core.Sha256, manifest.Catalogue.Sha256, StringComparison.OrdinalIgnoreCase)
                    || core.Version != manifest.Catalogue.Version)
                    findings.Add(RglCodesDataOutputs.CatalogueMismatch, manifest.CatalogueFile.Path, null, null,
                        RglCodesDataOutputs.Message(RglCodesDataOutputs.CatalogueMismatch, Hashes.Short(core.Sha256), Hashes.Short(manifest.Catalogue.Sha256)));
                builtInCatalogue = CompareWithBuiltIn(core, manifest.CatalogueFile.Path, packedEngine, findings);
            }
            catch (RiegelException e)
            {
                findings.Add(RglCodesDataOutputs.PackedFileUnreadable, manifest.CatalogueFile.Path, null, null,
                    RglCodesDataOutputs.Message(RglCodesDataOutputs.PackedFileUnreadable, manifest.CatalogueFile.Path, e.Code));
            }
        }

        var withContent = new List<ImportedBaseline>();
        var withoutContent = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in manifest.Baselines)
        {
            if (!b.ContentIncluded)
            {
                withoutContent.Add(b.Baseline.Id);
                findings.Add(RglCodesDataOutputs.BaselineNotVerifiable, b.File?.Path, null, null,
                    RglCodesDataOutputs.Message(RglCodesDataOutputs.BaselineNotVerifiable, b.Baseline.Name));
                continue;
            }
            var path = EvidenceLayout.BaselineRules(b.Baseline.Id);
            var bytes = archive.Get(path);
            try
            {
                if (bytes is null) throw RiegelException.Common(RglCodesCommon.NotFound, path);
                var file = RiegelJson.Deserialize<RuleFile>(bytes);
                withContent.Add(new ImportedBaseline(b.Baseline, [.. file.Rules.Select(r => r with { RuleSetId = b.Baseline.Id })], true));
            }
            catch (RiegelException e)
            {
                withoutContent.Add(b.Baseline.Id);
                findings.Add(RglCodesDataOutputs.PackedFileUnreadable, path, null, null,
                    RglCodesDataOutputs.Message(RglCodesDataOutputs.PackedFileUnreadable, path, e.Code));
            }
        }
        Plant? plant = null;
        var plantBytes = archive.Get(EvidenceLayout.PlantFile);
        if (plantBytes is null)
            findings.Add(RglCodesDataOutputs.FileMissing, EvidenceLayout.PlantFile, null, null,
                RglCodesDataOutputs.Message(RglCodesDataOutputs.FileMissing, EvidenceLayout.PlantFile));
        else
        {
            try { plant = RiegelJson.Deserialize<Plant>(plantBytes); }
            catch (RiegelException e)
            {
                findings.Add(RglCodesDataOutputs.PackedFileUnreadable, EvidenceLayout.PlantFile, null, null,
                    RglCodesDataOutputs.Message(RglCodesDataOutputs.PackedFileUnreadable, EvidenceLayout.PlantFile, e.Code));
            }
        }
        var registers = archive.Entries.Where(e => e.Key.StartsWith("registers/", StringComparison.Ordinal))
            .GroupBy(e => Hashes.Sha256Hex(e.Value)).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

        // Läufe neu auswerten und vergleichen
        int snapshots = 0, compared = 0;
        var notVerifiable = new List<string>();
        foreach (var entry in manifest.Runs.OrderBy(r => r.HostName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.RunId, StringComparer.Ordinal))
        {
            var outcome = VerifyRun(archive, entry, core, withContent, withoutContent, plant, registers, l, findings, notVerifiable);
            if (outcome.Evaluated) snapshots++;
            compared += outcome.Compared;
        }
        findings.Close();
        var status = findings.AnyMismatch ? VerifyStatus.Mismatch : VerifyStatus.Reproduced;
        return new VerifyResult(status, engine, packedEngine, findings.List, snapshots, compared, notVerifiable)
        {
            Catalogue = core is null ? null : new CatalogueRef(core.Id, core.Version, core.ReleasedOn, core.Sha256),
            CatalogueBuiltIn = builtInCatalogue,
        };
    }

    /// <summary>
    /// Vergleicht den gepackten Katalog mit dem eingebauten. Gleiche ID und Version, anderer SHA-256: die Regeln wurden
    /// verändert (RGL-0572, abweichend). Andere ID oder Version: keine Referenz in dieser Riegel-Version (RGL-0573,
    /// Hinweis, Exitcode 1). true nur bei einem bytegleichen (kanonischen) Katalog.
    /// </summary>
    private bool CompareWithBuiltIn(ICatalogue packed, string file, string packedEngine, Findings findings)
    {
        var reference = builtIn();
        if (string.Equals(packed.Id, reference.Id, StringComparison.Ordinal) && packed.Version == reference.Version)
        {
            if (Same(packed.Sha256, reference.Sha256)) return true;
            findings.Add(RglCodesDataOutputs.CatalogueNotBuiltIn, file, null, null,
                RglCodesDataOutputs.Message(RglCodesDataOutputs.CatalogueNotBuiltIn, packed.Id + " " + packed.Version, Hashes.Short(packed.Sha256),
                    Hashes.Short(reference.Sha256)));
            return false;
        }
        findings.Add(RglCodesDataOutputs.CatalogueNotReference, file, null, null,
            RglCodesDataOutputs.Message(RglCodesDataOutputs.CatalogueNotReference, packed.Id + " " + packed.Version,
                reference.Id + " " + reference.Version, packedEngine));
        return false;
    }

    private readonly record struct RunOutcome(bool Evaluated, int Compared);

    private RunOutcome VerifyRun(EvidenceArchive archive, EvidenceRun entry, ICatalogue? core, IReadOnlyList<ImportedBaseline> withContent,
        IReadOnlySet<string> withoutContent, Plant? plant, IReadOnlyDictionary<string, byte[]> registerFiles, ReadLimits l,
        Findings findings, List<string> notVerifiable)
    {
        var host = entry.HostName;
        void NotVerifiable(string? file, string reason) =>
            findings.Add(RglCodesDataOutputs.RunNotVerifiable, file, host, null, RglCodesDataOutputs.Message(RglCodesDataOutputs.RunNotVerifiable, entry.RunId, reason));

        // Gepackter Lauf
        var resultsBytes = archive.Get(entry.Results.Path);
        if (resultsBytes is null) return default; // schon als fehlende Datei gemeldet
        Run packed;
        try { packed = RiegelJson.Deserialize<Run>(resultsBytes); }
        catch (RiegelException e)
        {
            findings.Add(RglCodesDataOutputs.PackedFileUnreadable, entry.Results.Path, host, null,
                RglCodesDataOutputs.Message(RglCodesDataOutputs.PackedFileUnreadable, entry.Results.Path, e.Code));
            return default;
        }
        if (packed.Id != entry.RunId || !string.Equals(packed.HostName, entry.HostName, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(RglCodesDataOutputs.RunManifestMismatch, entry.Results.Path, host, null,
                RglCodesDataOutputs.Message(RglCodesDataOutputs.RunManifestMismatch, entry.RunId, packed.Id));
            return default;
        }
        if (!string.Equals(packed.Snapshot.Sha256, entry.Snapshot.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(RglCodesDataOutputs.RunManifestMismatch, entry.Snapshot.Path, host, null,
                RglCodesDataOutputs.Message(RglCodesDataOutputs.RunManifestMismatch, Hashes.Short(entry.Snapshot.Sha256), Hashes.Short(packed.Snapshot.Sha256)));
            return default;
        }

        // Katalog des Laufs: der gepackte, verbunden mit den Baselines dieses Laufs, deren Inhalt im Paket liegt
        ICatalogue? catalogue = null;
        if (core is not null)
        {
            var runBaselines = withContent.Where(b => packed.Baselines.Any(x => x.Id == b.Baseline.Id)).ToList();
            ICatalogue candidate;
            try { candidate = runBaselines.Count > 0 ? services.Catalogue.Combine(core, runBaselines) : core; }
            catch (RiegelException e)
            {
                NotVerifiable(entry.Results.Path, e.Code);
                return default;
            }
            if (Same(packed.Catalogue.Sha256, core.Sha256) || Same(packed.Catalogue.Sha256, candidate.Sha256)) catalogue = candidate;
            // Mit einer Baseline ohne Inhalt lässt sich die Prüfsumme des verbundenen Katalogs nicht nachrechnen: dann
            // genügen Katalog-ID und Version; die Ergebnisse der eigenen Regeln werden trotzdem verglichen.
            else if (packed.Baselines.Any(b => withoutContent.Contains(b.Id)) && packed.Catalogue.Id == core.Id
                     && packed.Catalogue.Version == core.Version) catalogue = candidate;
        }
        if (catalogue is null)
        {
            NotVerifiable(entry.Results.Path, packed.Catalogue.Version + " " + Hashes.Short(packed.Catalogue.Sha256));
            return default;
        }
        if (plant is null) return default; // schon gemeldet

        // Register in der beim Lauf verwendeten Fassung
        RegisterSet registers;
        try { registers = Registers(packed.Registers, registerFiles, plant, l); }
        catch (RiegelException e)
        {
            NotVerifiable(null, e.Message);
            return default;
        }

        // Export mit demselben Leser wie beim Import
        var snapshotBytes = archive.Get(entry.Snapshot.Path);
        if (snapshotBytes is null) return default;
        Snapshot snapshot;
        try { snapshot = PackedSnapshots.Read(services.Snapshots, snapshotBytes, entry.Snapshot.Path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase), l); }
        catch (RiegelException e)
        {
            findings.Add(RglCodesDataOutputs.PackedFileUnreadable, entry.Snapshot.Path, host, null,
                RglCodesDataOutputs.Message(RglCodesDataOutputs.PackedFileUnreadable, entry.Snapshot.Path, e.Code));
            return default;
        }

        // Neu auswerten mit der Uhr des gepackten Laufs; Detailtexte hängen von der Sprache ab, deshalb notfalls weitere Sprachen
        var clockServices = RiegelServices.Create(services.Platform, new FixedTimeProvider(packed.EvaluatedAtUtc));
        var unverifiable = packed.Results.Where(r => withoutContent.Contains(r.RuleSetId)).ToList();
        Run Evaluate(string language)
        {
            var run = clockServices.Runs.EvaluateOnly(snapshot, new RunOptions(packed.EvaluationDate, packed.ImportedAtUtc,
                language, Store: false, Limits: l, Catalogue: catalogue, Registers: registers, Plant: plant with { Folder = null, ReadOnly = true }));
            if (unverifiable.Count == 0) return run;
            // Baselines ohne Inhalt: Zählungen und Wert über die neu ausgewerteten Ergebnisse und die (nicht nachprüfbaren)
            // gepackten Ergebnisse dieser Baselines – so fällt eine geänderte Zusammenfassung trotzdem auf. Der Härtungsgrad
            // zählt nur den eigenen Katalog und ist damit vollständig nachgerechnet.
            List<RuleResult> merged = [.. run.Results.Where(r => !withoutContent.Contains(r.RuleSetId)), .. unverifiable];
            var (summary, categories) = clockServices.Scoring.Score(merged, catalogue, null, (run.Flags & RunFlags.DomainController) != 0);
            return run with { Summary = summary, Categories = categories };
        }
        Run fresh;
        List<Difference> differences;
        try
        {
            fresh = Evaluate(UiLanguage.Fallback);
            differences = Compare(packed, fresh, withoutContent);
            if (differences.Count > 0 && differences.All(d => d.DetailsOnly))
            {
                foreach (var language in new[] { plant.Settings.ReportLanguage }.Concat(UiLanguage.Supported).OfType<string>().Distinct())
                {
                    var retry = Evaluate(language);
                    var again = Compare(packed, retry, withoutContent);
                    if (again.Count == 0) { fresh = retry; differences = again; break; }
                }
            }
        }
        catch (RiegelException e)
        {
            NotVerifiable(entry.Snapshot.Path, e.Code + " " + e.Message);
            return new RunOutcome(true, 0);
        }

        foreach (var d in differences)
            findings.Add(d.Summary ? RglCodesDataOutputs.SummaryMismatch : RglCodesDataOutputs.ResultMismatch, entry.Results.Path, host, d.RuleId,
                RglCodesDataOutputs.Message(d.Summary ? RglCodesDataOutputs.SummaryMismatch : RglCodesDataOutputs.ResultMismatch, d.RuleId ?? entry.RunId, d.Text));
        int compared = 0;
        foreach (var r in packed.Results)
        {
            if (withoutContent.Contains(r.RuleSetId)) notVerifiable.Add(host + ": " + r.RuleId);
            else compared++;
        }
        return new RunOutcome(true, compared);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static RegisterSet Registers(RegisterHashes wanted, IReadOnlyDictionary<string, byte[]> files, Plant plant, ReadLimits l)
    {
        T Load<T>(string sha, string kind, Func<T> empty)
        {
            if (sha.Length == 0) return empty();
            if (!files.TryGetValue(sha.ToLowerInvariant(), out var bytes))
                throw RglCodesDataOutputs.Error(RglCodesDataOutputs.FileMissing, "registers/" + kind + "_" + Hashes.Short(sha) + ".json");
            if (bytes.LongLength > l.MaxRegisterBytes) throw RiegelException.Common(RglCodesCommon.LimitExceeded, kind, l.MaxRegisterBytes);
            return RiegelJson.Deserialize<T>(bytes);
        }
        var exceptions = Load(wanted.Exceptions, "exceptions", () => new ExceptionFile(SchemaIds.Exceptions, plant.Id, []));
        var tailoring = Load(wanted.Tailoring, "tailoring", () => new TailoringFile(SchemaIds.Tailoring, plant.Id, []));
        var measures = Load(wanted.Measures, "measures", () => new MeasureFile(SchemaIds.Measures, plant.Id, []));
        if (exceptions.Exceptions.Count + tailoring.Entries.Count + measures.Measures.Count > l.MaxRegisterEntries)
            throw RiegelException.Common(RglCodesCommon.LimitExceeded, "registers", l.MaxRegisterEntries);
        return new RegisterSet(exceptions, tailoring, measures, wanted);
    }

    // ---------------------------------------------------------------- Vergleich gepackt ↔ neu ausgewertet

    internal sealed record Difference(string? RuleId, string Text, bool DetailsOnly, bool Summary);

    /// <summary>
    /// Unterschiede der Ergebnisse (ohne Auswertungszeit, Lauf-ID, Pfad des Exports, Werkzeug und Verlaufsangaben) und
    /// der verlaufsunabhängigen Zählungen.
    /// </summary>
    internal static List<Difference> Compare(Run packed, Run fresh, IReadOnlySet<string> notVerifiableRuleSets)
    {
        var result = new List<Difference>();
        var actual = new Dictionary<(string, string), RuleResult>();
        foreach (var r in fresh.Results) actual.TryAdd((r.RuleSetId, r.RuleId), r);
        var seen = new HashSet<(string, string)>();
        foreach (var expected in packed.Results)
        {
            var key = (expected.RuleSetId, expected.RuleId);
            if (notVerifiableRuleSets.Contains(expected.RuleSetId) || !seen.Add(key)) continue;
            if (!actual.TryGetValue(key, out var a))
            {
                result.Add(new Difference(expected.RuleId, "– (" + OutputText.Camel(expected.Status) + " → ∅)", false, false));
                continue;
            }
            var fields = Fields(expected, a, out bool detailsOnly);
            if (fields.Count > 0) result.Add(new Difference(expected.RuleId, string.Join("; ", fields), detailsOnly, false));
        }
        foreach (var r in fresh.Results)
            if (!notVerifiableRuleSets.Contains(r.RuleSetId) && !seen.Contains((r.RuleSetId, r.RuleId)))
                result.Add(new Difference(r.RuleId, "(∅ → " + OutputText.Camel(r.Status) + ")", false, false));

        // Zählungen immer: bei Baselines ohne Inhalt hat der Aufrufer die gepackten Ergebnisse dieser Baselines in die
        // Zählungen von fresh übernommen (VerifyRun)
        var s = packed.Summary;
        var f = fresh.Summary;
        var summary = new List<string>();
        void Check(string name, int? x, int? y) { if (x != y) summary.Add($"{name}: {N(x)} → {N(y)}"); }
        Check("pass", s.Pass, f.Pass); Check("fail", s.Fail, f.Fail); Check("exception", s.Exception, f.Exception);
        Check("outOfScope", s.OutOfScope, f.OutOfScope); Check("notApplicable", s.NotApplicable, f.NotApplicable);
        Check("notCheckable", s.NotCheckable, f.NotCheckable); Check("info", s.Info, f.Info);
        Check("openHigh", s.OpenHigh, f.OpenHigh); Check("openMedium", s.OpenMedium, f.OpenMedium); Check("openLow", s.OpenLow, f.OpenLow);
        Check("exceptionsExpiring", s.ExceptionsExpiring, f.ExceptionsExpiring); Check("exceptionsExpired", s.ExceptionsExpired, f.ExceptionsExpired);
        Check("exceptionsPending", s.ExceptionsPending, f.ExceptionsPending); Check("measuresOverdue", s.MeasuresOverdue, f.MeasuresOverdue);
        Check("score", s.Score, f.Score); Check("documented", s.Documented, f.Documented); Check("checkedShare", s.CheckedShare, f.CheckedShare);
        var pf = packed.Flags & ~HistoryRunFlags;
        var ff = fresh.Flags & ~HistoryRunFlags;
        if (pf != ff) summary.Add("flags: " + string.Join(",", FlagsArrayConverter<RunFlags>.Names(pf)) + " → " + string.Join(",", FlagsArrayConverter<RunFlags>.Names(ff)));
        if (summary.Count > 0) result.Add(new Difference(null, string.Join("; ", summary), false, true));
        return result;
    }

    private static List<string> Fields(RuleResult e, RuleResult a, out bool detailsOnly)
    {
        var list = new List<string>();
        void Check(string name, string? x, string? y) { if (!string.Equals(x, y, StringComparison.Ordinal)) list.Add($"{name}: {x ?? "∅"} → {y ?? "∅"}"); }
        Check("status", OutputText.Camel(e.Status), OutputText.Camel(a.Status));
        Check("severity", OutputText.Camel(e.Severity), OutputText.Camel(a.Severity));
        Check("expected", e.Expected, a.Expected);
        Check("actual", e.Actual, a.Actual);
        Check("valueSource", OutputText.Camel(e.Source), OutputText.Camel(a.Source));
        Check("sourceDetail", e.SourceDetail, a.SourceDetail);
        Check("evidenceKeys", string.Join("|", e.EvidenceKeys), string.Join("|", a.EvidenceKeys));
        Check("exceptionId", e.ExceptionId, a.ExceptionId);
        Check("tailoringId", e.TailoringId, a.TailoringId);
        Check("measureId", e.MeasureId, a.MeasureId);
        Check("reasonCode", e.ReasonCode, a.ReasonCode);
        var ef = e.Flags & ~HistoryFlags;
        var af = a.Flags & ~HistoryFlags;
        if (ef != af)
            list.Add("flags: " + string.Join(",", FlagsArrayConverter<ResultFlags>.Names(ef)) + " → " + string.Join(",", FlagsArrayConverter<ResultFlags>.Names(af)));
        int before = list.Count;
        Check("details", e.Details is null ? null : string.Join("|", e.Details), a.Details is null ? null : string.Join("|", a.Details));
        detailsOnly = before == 0 && list.Count == 1;
        return list;
    }

    private static string N(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "∅";

    // ---------------------------------------------------------------- Unversehrtheit (SHA256SUMS.txt und Manifest)

    private sealed record Failure(string Code, string? File, string Message);

    /// <summary>
    /// Prüft SHA256SUMS.txt und manifest.json. false = Paket nicht auswertbar (Exitcode 3, <paramref name="error"/>);
    /// Abweichungen einzelner Dateien sind Befunde (Exitcode 2).
    /// </summary>
    private static bool CheckIntegrity(EvidenceArchive archive, Findings findings, out EvidenceManifest? manifest, out Failure? error)
    {
        manifest = null;
        error = null;
        var sumsBytes = archive.Get(EvidenceLayout.Checksums);
        var sums = sumsBytes is null ? null : EvidenceLayout.ParseChecksums(sumsBytes);
        if (sums is null)
        {
            error = new Failure(RglCodesDataOutputs.ChecksumFileInvalid, EvidenceLayout.Checksums,
                RglCodesDataOutputs.Message(RglCodesDataOutputs.ChecksumFileInvalid, EvidenceLayout.Checksums));
            return false;
        }
        var actual = archive.Entries.ToDictionary(e => e.Key, e => Hashes.Sha256Hex(e.Value), StringComparer.Ordinal);
        foreach (var (path, sha) in sums)
        {
            if (!actual.TryGetValue(path, out var real))
                findings.Add(RglCodesDataOutputs.FileMissing, path, null, null, RglCodesDataOutputs.Message(RglCodesDataOutputs.FileMissing, path));
            else if (real != sha)
                findings.Add(RglCodesDataOutputs.FileHashMismatch, path, null, null,
                    RglCodesDataOutputs.Message(RglCodesDataOutputs.FileHashMismatch, path, Hashes.Short(sha), Hashes.Short(real)));
        }
        foreach (var path in actual.Keys)
            if (path != EvidenceLayout.Checksums && !sums.ContainsKey(path))
                findings.Add(RglCodesDataOutputs.FileNotListed, path, null, null, RglCodesDataOutputs.Message(RglCodesDataOutputs.FileNotListed, path));

        var manifestBytes = archive.Get(EvidenceLayout.Manifest);
        try
        {
            if (manifestBytes is null) throw RiegelException.Common(RglCodesCommon.NotFound, EvidenceLayout.Manifest);
            manifest = RiegelJson.Deserialize<EvidenceManifest>(manifestBytes);
            if (manifest.Schema != SchemaIds.Evidence) throw RiegelException.Common(RglCodesCommon.UnsupportedSchema, manifest.Schema);
        }
        catch (RiegelException e)
        {
            error = new Failure(RglCodesDataOutputs.ManifestInvalid, EvidenceLayout.Manifest,
                RglCodesDataOutputs.Message(RglCodesDataOutputs.ManifestInvalid, e.Code + " " + e.Message));
            manifest = null;
            return false;
        }

        // Jede Datei außer manifest.json und SHA256SUMS.txt steht im Manifest – und umgekehrt
        var listed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in manifest.Files)
        {
            if (!EvidenceArchive.IsSafeName(f.Path, out _) || !listed.TryAdd(f.Path, f.Sha256))
            {
                error = new Failure(RglCodesDataOutputs.ManifestInvalid, EvidenceLayout.Manifest,
                    RglCodesDataOutputs.Message(RglCodesDataOutputs.ManifestInvalid, EvidenceArchive.Printable(f.Path)));
                return false;
            }
            CheckRef(f, actual, findings);
        }
        foreach (var path in actual.Keys)
            if (path is not EvidenceLayout.Manifest and not EvidenceLayout.Checksums && !listed.ContainsKey(path))
                findings.Add(RglCodesDataOutputs.FileNotListed, path, null, null, RglCodesDataOutputs.Message(RglCodesDataOutputs.FileNotListed, path));
        CheckRef(manifest.CatalogueFile, actual, findings);
        foreach (var run in manifest.Runs)
        {
            CheckRef(run.Results, actual, findings);
            CheckRef(run.Snapshot, actual, findings);
        }
        foreach (var b in manifest.Baselines)
            if (b.File is { } file) CheckRef(file, actual, findings);
        return true;
    }

    private static void CheckRef(FileRef f, IReadOnlyDictionary<string, string> actual, Findings findings)
    {
        if (!actual.TryGetValue(f.Path, out var real))
        {
            if (!findings.List.Any(x => x.Code == RglCodesDataOutputs.FileMissing && x.File == f.Path))
                findings.Add(RglCodesDataOutputs.FileMissing, f.Path, null, null, RglCodesDataOutputs.Message(RglCodesDataOutputs.FileMissing, f.Path));
        }
        else if (!string.Equals(real, f.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            if (!findings.List.Any(x => x.Code == RglCodesDataOutputs.FileHashMismatch && x.File == f.Path))
                findings.Add(RglCodesDataOutputs.FileHashMismatch, f.Path, null, null,
                    RglCodesDataOutputs.Message(RglCodesDataOutputs.FileHashMismatch, f.Path, Hashes.Short(f.Sha256), Hashes.Short(real)));
        }
    }

    private static VerifyResult Error(string engine, string? packed, string code, string? file, string message) =>
        new(VerifyStatus.Error, engine, packed, [new VerifyFinding(code, file, null, null, message)], 0, 0, []);

    // ================================================================ compare-snapshot

    public SampleResult CompareSample(string freshSnapshotPath, string zipPath, string? host, ReadLimits l)
    {
        try
        {
            var archive = EvidenceArchive.Read(zipPath, l);
            var findings = new Findings();
            if (!CheckIntegrity(archive, findings, out var manifest, out var error))
                return new SampleResult(SampleStatus.Error, null, error!.Code, error.Message);
            if (findings.AnyMismatch)
            {
                // Ein verändertes Paket taugt nicht als Vergleichsgrundlage
                var first = findings.List.First(f => MismatchCodes.Contains(f.Code));
                return new SampleResult(SampleStatus.Error, null, first.Code, first.Message);
            }
            var fresh = services.Snapshots.Read(freshSnapshotPath, l);
            var wanted = string.IsNullOrWhiteSpace(host) ? fresh.Machine.HostName : host.Trim();
            var run = manifest!.Runs.Where(r => string.Equals(r.HostName, wanted, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.RunId, StringComparer.Ordinal).LastOrDefault();
            if (run is null)
                return new SampleResult(SampleStatus.Error, null, RglCodesDataOutputs.HostNotInPack,
                    RglCodesDataOutputs.Message(RglCodesDataOutputs.HostNotInPack, wanted));
            var bytes = archive.Get(run.Snapshot.Path)
                ?? throw RglCodesDataOutputs.Error(RglCodesDataOutputs.FileMissing, run.Snapshot.Path);
            var packed = PackedSnapshots.Read(services.Snapshots, bytes, run.Snapshot.Path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase), l);
            var comparison = services.Comparison.CompareSnapshots(packed, fresh);
            return comparison.Consistent
                ? new SampleResult(SampleStatus.Consistent, comparison, null, DataOutputStrings.Sample_Consistent)
                : new SampleResult(SampleStatus.Different, comparison, null,
                    string.Format(CultureInfo.CurrentCulture, DataOutputStrings.Sample_Different, comparison.Differences.Count));
        }
        catch (RiegelException e)
        {
            return new SampleResult(SampleStatus.Error, null, e.Code, e.Message);
        }
    }
}
