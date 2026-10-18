// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;

namespace Riegel.Core.Registers;

/// <summary>
/// Factory der Register (gehört ab dem Gerüst dem Paket workflow; Signatur fest). Der Platzhalter liest und schreibt
/// exceptions.json, tailoring.json und measures.json, wendet aber nichts an (Ergebnisse bleiben roh) und prüft nichts.
/// </summary>
public static class RegistersFactory
{
    public static IRegisterService Create(RiegelServices services) => new StubRegisterService();
}

/// <summary>Platzhalter (Gerüst).</summary>
internal sealed class StubRegisterService : IRegisterService
{
    public const string ExceptionsFile = "exceptions.json";
    public const string TailoringFile = "tailoring.json";
    public const string MeasuresFile = "measures.json";

    public RegisterSet Load(Plant p)
    {
        var folder = p.Folder ?? throw RiegelException.Common(RglCodesCommon.NotFound, p.Id);
        var (exceptions, exSha) = Read(Path.Combine(folder, ExceptionsFile), () => new ExceptionFile(SchemaIds.Exceptions, p.Id, []));
        var (tailoring, tlSha) = Read(Path.Combine(folder, TailoringFile), () => new TailoringFile(SchemaIds.Tailoring, p.Id, []));
        var (measures, mSha) = Read(Path.Combine(folder, MeasuresFile), () => new MeasureFile(SchemaIds.Measures, p.Id, []));
        return new RegisterSet(exceptions, tailoring, measures, new RegisterHashes(exSha, tlSha, mSha));
    }

    public void Save(Plant p, RegisterSet r, RegisterHashes expected)
    {
        if (p.ReadOnly) throw RiegelException.Common(RglCodesCommon.ReadOnlyMode);
        var folder = p.Folder ?? throw RiegelException.Common(RglCodesCommon.NotFound, p.Id);
        RiegelJson.WriteFile(Path.Combine(folder, ExceptionsFile), r.Exceptions with { Exceptions = [.. r.Exceptions.Exceptions.OrderBy(e => e.Id, StringComparer.Ordinal)] });
        RiegelJson.WriteFile(Path.Combine(folder, TailoringFile), r.Tailoring with { Entries = [.. r.Tailoring.Entries.OrderBy(e => e.Id, StringComparer.Ordinal)] });
        RiegelJson.WriteFile(Path.Combine(folder, MeasuresFile), r.Measures with { Measures = [.. r.Measures.Measures.OrderBy(e => e.Id, StringComparer.Ordinal)] });
    }

    public IReadOnlyList<RuleResult> Apply(IReadOnlyList<RuleResult> raw, RegisterSet r, Plant p, string host, DateOnly today) => raw;

    public IReadOnlyList<ValidationIssue> Validate(RegisterSet r, ICatalogue c, Plant p, DateOnly today) => [];

    public RegisterSet Approve(RegisterSet r, RegisterKind kind, IReadOnlyList<string> ids, Approval a)
    {
        var set = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        switch (kind)
        {
            case RegisterKind.Exceptions:
                return r with
                {
                    Exceptions = r.Exceptions with
                    {
                        Exceptions = [.. r.Exceptions.Exceptions.Select(e => set.Contains(e.Id)
                            ? e with { Approval = a with { ApprovedContentSha256 = ContentHashes.Of(e) } } : e)],
                    },
                };
            case RegisterKind.Tailoring:
                return r with
                {
                    Tailoring = r.Tailoring with
                    {
                        Entries = [.. r.Tailoring.Entries.Select(e => set.Contains(e.Id)
                            ? e with { Approval = a with { ApprovedContentSha256 = ContentHashes.Of(e) } } : e)],
                    },
                };
            default:
                // Maßnahmen brauchen keine Freigabe (spec 2.13)
                return r;
        }
    }

    public RegisterSet ImportTemplate(RegisterSet r, string templatePath, ReadLimits l)
    {
        var info = new FileInfo(templatePath);
        if (!info.Exists) throw RiegelException.Common(RglCodesCommon.NotFound, templatePath);
        if (info.Length > l.MaxRegisterBytes) throw RiegelException.Common(RglCodesCommon.LimitExceeded, templatePath, l.MaxRegisterBytes);
        var bytes = File.ReadAllBytes(templatePath);
        var schema = System.Text.Json.JsonDocument.Parse(bytes).RootElement.GetProperty("schema").GetString();
        if (schema == SchemaIds.ExceptionsTemplate)
        {
            var template = RiegelJson.Deserialize<ExceptionFile>(bytes);
            return r with { Exceptions = r.Exceptions with { Exceptions = [.. r.Exceptions.Exceptions, .. template.Exceptions.Select(e => e with { Approval = Approval.None })] } };
        }
        if (schema == SchemaIds.TailoringTemplate)
        {
            var template = RiegelJson.Deserialize<TailoringFile>(bytes);
            return r with { Tailoring = r.Tailoring with { Entries = [.. r.Tailoring.Entries, .. template.Entries.Select(e => e with { Approval = Approval.None })] } };
        }
        throw RiegelException.Common(RglCodesCommon.UnsupportedSchema, schema);
    }

    private static (T Value, string Sha) Read<T>(string path, Func<T> empty)
    {
        if (!File.Exists(path)) return (empty(), "");
        var value = RiegelJson.ReadFile<T>(path, ReadLimits.Default.MaxRegisterBytes);
        return (value, Hashes.Sha256File(path));
    }
}
