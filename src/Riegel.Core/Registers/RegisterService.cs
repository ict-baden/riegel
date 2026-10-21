// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Model.Reports;
using Riegel.Core.Plants;
using Riegel.Core.Services;

namespace Riegel.Core.Registers;

/// <summary>
/// Die drei Register der Anlage (spec 2.11–2.13): Laden und Speichern (feste Reihenfolge, nach id sortiert,
/// eingerückt, UTF-8 mit LF; Sperre, Vergleich der Prüfsumme, Verlaufskopie), Prüfen, Anwenden auf Rohergebnisse,
/// Freigeben (mit Inhalts-Hash) und Vorlagen übernehmen (als beantragte Einträge).
/// </summary>
internal sealed class RegisterService(RiegelServices services) : IRegisterService
{
    /// <summary>Wartezeit auf die Sperre (spec 7.6: 10 s; Tests kürzer).</summary>
    public TimeSpan LockTimeout { get; init; } = PlantLock.DefaultTimeout;

    // ---------------------------------------------------------------- Laden und Speichern

    public RegisterSet Load(Plant p)
    {
        var folder = PlantStore.Folder(p);
        var limits = ReadLimits.Default;
        var (exceptions, exSha) = Read(Path.Combine(folder, PlantFiles.ExceptionsFile), SchemaIds.Exceptions,
            () => new ExceptionFile(SchemaIds.Exceptions, p.Id, []), f => f.Schema, f => f.Exceptions.Count, limits);
        var (tailoring, tlSha) = Read(Path.Combine(folder, PlantFiles.TailoringFile), SchemaIds.Tailoring,
            () => new TailoringFile(SchemaIds.Tailoring, p.Id, []), f => f.Schema, f => f.Entries.Count, limits);
        var (measures, mSha) = Read(Path.Combine(folder, PlantFiles.MeasuresFile), SchemaIds.Measures,
            () => new MeasureFile(SchemaIds.Measures, p.Id, []), f => f.Schema, f => f.Measures.Count, limits);
        return new RegisterSet(exceptions, tailoring, measures, new RegisterHashes(exSha, tlSha, mSha));
    }

    /// <summary>
    /// Speichert die Register, die sich gegenüber dem Laden geändert haben. Unter der Sperre: geänderte Datei muss noch
    /// den SHA-256 aus <paramref name="expected"/> haben (sonst RGL-0403), dann Ersetzen und Verlaufskopie
    /// <c>history/&lt;register&gt;/&lt;ts&gt;_&lt;sha8&gt;.json</c>. Ein Register, das der Aufrufer nicht verändert hat,
    /// bleibt unberührt – auch wenn es inzwischen jemand anders geändert hat.
    /// </summary>
    public void Save(Plant p, RegisterSet r, RegisterHashes expected)
    {
        var folder = PlantStore.Writable(p);
        var limits = ReadLimits.Default;
        var exceptions = r.Exceptions with { Schema = SchemaIds.Exceptions, PlantId = p.Id, Exceptions = [.. r.Exceptions.Exceptions.OrderBy(e => e.Id, StringComparer.Ordinal)] };
        var tailoring = r.Tailoring with { Schema = SchemaIds.Tailoring, PlantId = p.Id, Entries = [.. r.Tailoring.Entries.OrderBy(e => e.Id, StringComparer.Ordinal)] };
        var measures = r.Measures with { Schema = SchemaIds.Measures, PlantId = p.Id, Measures = [.. r.Measures.Measures.OrderBy(e => e.Id, StringComparer.Ordinal)] };
        foreach (var count in new[] { exceptions.Exceptions.Count, tailoring.Entries.Count, measures.Measures.Count })
            if (count > limits.MaxRegisterEntries) throw RiegelException.Common(RglCodesCommon.LimitExceeded, "register", limits.MaxRegisterEntries);

        var writes = new[]
        {
            (File: PlantFiles.ExceptionsFile, Kind: PlantFiles.HistoryKind.Exceptions, Bytes: RiegelJson.SerializeToUtf8Bytes(exceptions),
                Expected: expected.Exceptions, Empty: exceptions.Exceptions.Count == 0),
            (File: PlantFiles.TailoringFile, Kind: PlantFiles.HistoryKind.Tailoring, Bytes: RiegelJson.SerializeToUtf8Bytes(tailoring),
                Expected: expected.Tailoring, Empty: tailoring.Entries.Count == 0),
            (File: PlantFiles.MeasuresFile, Kind: PlantFiles.HistoryKind.Measures, Bytes: RiegelJson.SerializeToUtf8Bytes(measures),
                Expected: expected.Measures, Empty: measures.Measures.Count == 0),
        };
        using var _ = PlantLock.Acquire(folder, LockTimeout);
        var now = services.Clock.GetUtcNow();
        var planned = new List<(string Path, string Kind, byte[] Bytes)>();
        foreach (var w in writes)
        {
            var path = Path.Combine(folder, w.File);
            var expectedSha = w.Expected ?? "";
            if (string.Equals(Hashes.Sha256Hex(w.Bytes), expectedSha, StringComparison.OrdinalIgnoreCase)) continue; // unverändert
            if (expectedSha.Length == 0 && w.Empty && !File.Exists(path)) continue; // leeres Register nicht anlegen
            if (!string.Equals(PlantFiles.ShaOrEmpty(path), expectedSha, StringComparison.OrdinalIgnoreCase))
                throw RglCodesWorkflow.Error(RglCodesWorkflow.ChangedByOthers, w.File);
            planned.Add((path, w.Kind, w.Bytes));
        }
        // erst alle prüfen, dann schreiben: eine Abweichung lässt alle Register unverändert
        foreach (var (path, kind, bytes) in planned)
        {
            PlantFiles.WriteAtomic(path, bytes);
            PlantFiles.WriteHistory(folder, kind, now, bytes);
        }
    }

    private static (T Value, string Sha) Read<T>(string path, string schema, Func<T> empty, Func<T, string> schemaOf, Func<T, int> count,
        ReadLimits limits)
    {
        if (!File.Exists(path)) return (empty(), "");
        var bytes = ReadBytes(path, limits.MaxRegisterBytes);
        var value = Deserialize<T>(bytes, path);
        if (schemaOf(value) != schema) throw RiegelException.Common(RglCodesCommon.UnsupportedSchema, schemaOf(value));
        if (count(value) > limits.MaxRegisterEntries) throw RiegelException.Common(RglCodesCommon.LimitExceeded, path, limits.MaxRegisterEntries);
        return (value, Hashes.Sha256Hex(bytes));
    }

    private static byte[] ReadBytes(string path, long max)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw RiegelException.Common(RglCodesCommon.NotFound, path);
        if (info.Length > max) throw RiegelException.Common(RglCodesCommon.LimitExceeded, path, max);
        return File.ReadAllBytes(path);
    }

    // ---------------------------------------------------------------- Prüfen und Anwenden

    public IReadOnlyList<ValidationIssue> Validate(RegisterSet r, ICatalogue c, Plant p, DateOnly today) =>
        RegisterValidator.Validate(r, c, p, today);

    public IReadOnlyList<RuleResult> Apply(IReadOnlyList<RuleResult> raw, RegisterSet r, Plant p, string host, DateOnly today) =>
        RegisterApplier.Apply(raw, r, p, host, today);

    // ---------------------------------------------------------------- Freigeben

    /// <summary>
    /// Trägt die Freigabe ein und bindet sie an den Inhalt (<see cref="ContentHashes"/>). Pflicht: Rolle und
    /// Nachweis (RGL-0408). approvedOn ohne Angabe = heute; reviewBy ohne Angabe = vorgeschlagener Termin des
    /// Eintrags, sonst 12 Monate nach der Freigabe (die längste Frist ohne Warnung).
    /// </summary>
    public RegisterSet Approve(RegisterSet r, RegisterKind kind, IReadOnlyList<string> ids, Approval a)
    {
        if (kind == RegisterKind.Measures) throw RglCodesWorkflow.Error(RglCodesWorkflow.MeasuresNeedNoApproval);
        if (string.IsNullOrWhiteSpace(a.ApprovedByRole) || string.IsNullOrWhiteSpace(a.ApprovalRecord))
            throw RglCodesWorkflow.Error(RglCodesWorkflow.ApprovalIncomplete);
        var wanted = new HashSet<string>(ids.Select(i => i.Trim()), StringComparer.OrdinalIgnoreCase);
        var known = kind == RegisterKind.Exceptions ? r.Exceptions.Exceptions.Select(e => e.Id) : r.Tailoring.Entries.Select(e => e.Id);
        var unknown = wanted.Except(known, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).FirstOrDefault();
        if (unknown is not null) throw RglCodesWorkflow.Error(RglCodesWorkflow.EntryNotFound, unknown, kind.ToString().ToLowerInvariant());
        var on = a.ApprovedOn ?? services.Today;
        Approval For(Approval previous, string contentSha)
        {
            var reviewBy = a.ReviewBy ?? (previous.ReviewBy is { } proposed && proposed > on ? proposed : on.AddMonths(RegisterRules.WarnReviewMonths));
            return new Approval(a.ApprovedByRole.Trim(), string.IsNullOrWhiteSpace(a.ApprovedByName) ? null : a.ApprovedByName.Trim(), on, reviewBy,
                a.ApprovalRecord.Trim(), contentSha);
        }
        return kind == RegisterKind.Exceptions
            ? r with
            {
                Exceptions = r.Exceptions with
                {
                    Exceptions = [.. r.Exceptions.Exceptions.Select(e => wanted.Contains(e.Id) ? e with { Approval = For(e.Approval, ContentHashes.Of(e)) } : e)],
                },
            }
            : r with
            {
                Tailoring = r.Tailoring with
                {
                    Entries = [.. r.Tailoring.Entries.Select(e => wanted.Contains(e.Id) ? e with { Approval = For(e.Approval, ContentHashes.Of(e)) } : e)],
                },
            };
    }

    // ---------------------------------------------------------------- Vorlagen

    /// <summary>
    /// Übernimmt eine Vorlage (<c>*.exceptions-template.json</c>, <c>*.tailoring-template.json</c>): jeder Eintrag wird
    /// beantragt (Freigabefelder leer), createdOn = heute, createdByRole bleibt (z. B. „Hersteller“). IDs, die es im
    /// Register schon gibt, bekommen die nächste freie Nummer. Vorlagen werden nie automatisch angewendet.
    /// Pflichtfelder der Einträge (ohne id, approval, createdOn, die der Import setzt) müssen in der Vorlage stehen,
    /// sonst RGL-0004 mit Datei, Eintrag und Feld – vorher wurde ein Eintrag ohne „scope“ übernommen und machte die
    /// Anlage unlesbar.
    /// </summary>
    public RegisterSet ImportTemplate(RegisterSet r, string templatePath, ReadLimits l)
    {
        var bytes = ReadBytes(templatePath, l.MaxRegisterBytes);
        string? schema;
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = l.MaxJsonDepth });
            var root = document.RootElement;
            schema = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("schema", out var s) &&
                     s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            if (schema == SchemaIds.ExceptionsTemplate) RequireFields(root, "exceptions", ExceptionTemplateFields, templatePath);
            else if (schema == SchemaIds.TailoringTemplate) RequireFields(root, "entries", TailoringTemplateFields, templatePath);
        }
        catch (JsonException e)
        {
            throw new RiegelException(RglCodesCommon.InvalidJson, RglCodesCommon.Message(RglCodesCommon.InvalidJson, templatePath), e);
        }
        var today = services.Today;
        if (schema == SchemaIds.ExceptionsTemplate)
        {
            var template = Deserialize<ExceptionFile>(bytes, templatePath);
            CheckCount(r.Exceptions.Exceptions.Count + template.Exceptions.Count, l, templatePath);
            var ids = new Ids("EX-", r.Exceptions.Exceptions.Select(e => e.Id));
            var added = template.Exceptions.Select(e => e with
            {
                Id = ids.Take(e.Id), Approval = Approval.None, CreatedOn = today, ModifiedOn = null,
            }).ToList();
            return r with { Exceptions = r.Exceptions with { Exceptions = [.. r.Exceptions.Exceptions, .. added] } };
        }
        if (schema == SchemaIds.TailoringTemplate)
        {
            var template = Deserialize<TailoringFile>(bytes, templatePath);
            CheckCount(r.Tailoring.Entries.Count + template.Entries.Count, l, templatePath);
            var ids = new Ids("TL-", r.Tailoring.Entries.Select(e => e.Id));
            var added = template.Entries.Select(e => e with
            {
                Id = ids.Take(e.Id), Approval = Approval.None, CreatedOn = today, ModifiedOn = null,
            }).ToList();
            return r with { Tailoring = r.Tailoring with { Entries = [.. r.Tailoring.Entries, .. added] } };
        }
        throw RiegelException.Common(RglCodesCommon.UnsupportedSchema, schema ?? "?");
    }

    /// <summary>Pflichtfelder der Vorlagen-Einträge (schemas/*-template.schema.json ohne id, approval, createdOn).</summary>
    private static readonly string[] ExceptionTemplateFields = ["ruleId", "scope", "category", "justification"];

    private static readonly string[] TailoringTemplateFields = ["ruleId", "scope", "reason"];

    private static readonly string[] ScopeFields = ["machines", "groups", "allMachines"];

    /// <summary>
    /// Jeder Eintrag der Liste <paramref name="list"/> hat die Felder <paramref name="fields"/> (nicht null), der
    /// Geltungsbereich seine drei Felder; sonst RGL-0004 „Datei: liste[i].feld“.
    /// </summary>
    private static void RequireFields(JsonElement root, string list, string[] fields, string path)
    {
        RiegelException Missing(string where) =>
            RiegelException.Common(RglCodesCommon.InvalidJson, Path.GetFileName(path) + ": " + where);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(list, out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw Missing(list);
        int i = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            var at = $"{list}[{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}]";
            if (entry.ValueKind != JsonValueKind.Object) throw Missing(at);
            foreach (var field in fields)
                if (!entry.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) throw Missing(at + "." + field);
            var scope = entry.GetProperty("scope");
            if (scope.ValueKind != JsonValueKind.Object) throw Missing(at + ".scope");
            foreach (var field in ScopeFields)
                if (!scope.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) throw Missing(at + ".scope." + field);
            i++;
        }
    }

    /// <summary>RiegelJson.Deserialize, bei RGL-0004 mit dem Dateinamen vor der Stelle im JSON.</summary>
    private static T Deserialize<T>(byte[] bytes, string path)
    {
        try { return RiegelJson.Deserialize<T>(bytes); }
        catch (RiegelException e) when (e.Code == RglCodesCommon.InvalidJson)
        {
            throw new RiegelException(e.Code, RglCodesCommon.Message(RglCodesCommon.InvalidJson, Path.GetFileName(path) + ": " +
                (e.InnerException is JsonException j && j.Path is { } p ? p : typeof(T).Name)), e);
        }
    }

    private static void CheckCount(int count, ReadLimits l, string path)
    {
        if (count > l.MaxRegisterEntries) throw RiegelException.Common(RglCodesCommon.LimitExceeded, path, l.MaxRegisterEntries);
    }

    /// <summary>Vergibt IDs: die gewünschte, wenn sie frei und gültig ist, sonst die nächste freie Nummer.</summary>
    private sealed class Ids(string prefix, IEnumerable<string> existing)
    {
        private readonly HashSet<string> _used = new(existing, StringComparer.OrdinalIgnoreCase);

        public string Take(string? wanted)
        {
            if (!string.IsNullOrWhiteSpace(wanted) && wanted.StartsWith(prefix, StringComparison.Ordinal) && wanted.Length >= prefix.Length + 4 &&
                wanted[prefix.Length..].All(char.IsAsciiDigit) && _used.Add(wanted))
                return wanted;
            int next = 1 + _used.Select(Number).DefaultIfEmpty(0).Max();
            var id = prefix + next.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            _used.Add(id);
            return id;
        }

        private int Number(string id) =>
            id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(id.AsSpan(prefix.Length), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
    }
}
