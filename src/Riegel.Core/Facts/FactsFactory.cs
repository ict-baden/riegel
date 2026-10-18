// SPDX-License-Identifier: MIT
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Services;

namespace Riegel.Core.Facts;

/// <summary>
/// Factory der Fakten (gehört ab dem Gerüst dem Paket engine; Signatur fest). Der Platzhalter leitet nur ab, was
/// der Rechnerkopf direkt sagt; alle anderen Fakten aus spec 2.5 sind null (unbekannt).
/// </summary>
public static class FactsFactory
{
    public static IFactsDeriver Create(RiegelServices services) => new StubFactsDeriver();
}

/// <summary>Platzhalter (Gerüst).</summary>
internal sealed class StubFactsDeriver : IFactsDeriver
{
    public IReadOnlyDictionary<string, JsonElement> Derive(Snapshot s, ICatalogue c)
    {
        var m = s.Machine;
        var facts = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["os.kind"] = m.ProductType switch { 1 => Text("client"), 2 or 3 => Text("server"), _ => Null() },
            ["os.build"] = m.Build is { } b ? Number(b) : Null(),
            ["os.ubr"] = m.Ubr is { } u ? Number(u) : Null(),
            ["os.edition"] = m.EditionId is { } e ? Text(e) : Null(),
            ["os.installationType"] = m.InstallationType is { } i ? Text(i) : Null(),
            ["os.arch"] = m.Architecture is { } a ? Text(a) : Null(),
            ["domain.joined"] = m.DomainRole is { } r ? Bool(r is 1 or 3 or 4 or 5) : Null(),
            ["domain.isDc"] = m.DomainRole is { } d ? Bool(d is 4 or 5) : Null(),
        };
        return facts;
    }

    private static JsonElement Text(string value) => JsonValues.Text(value);
    private static JsonElement Number(int value) => JsonValues.Number(value);
    private static JsonElement Bool(bool value) => JsonValues.Bool(value);
    private static JsonElement Null() => JsonValues.Null;
}
