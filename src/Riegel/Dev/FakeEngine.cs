// SPDX-License-Identifier: MIT
using System.Text;
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;
using static Riegel.Dev.FakeView;

namespace Riegel.Dev;

/// <summary>
/// Nur Entwicklung (RIEGEL_FAKE_SERVICES=1, Demo-Anlage): nachgebildeter Katalog, Fakten und Auswertung aus
/// <see cref="FakeRules"/>. Ersetzt die Platzhalter von engine, solange es keine echte Engine gibt – damit die Seiten
/// der App und ihre Screenshots geprüft werden können. Nie für Anlagen der Benutzer.
/// </summary>
internal sealed class FakeEngine(ICatalogueProvider real)
{
    private ICatalogue? _catalogue;

    /// <summary>Katalog mit den nachgebildeten Regeln (Kopf wie der eingebettete Katalog).</summary>
    public ICatalogue Catalogue => _catalogue ??= Build();

    private ICatalogue Build()
    {
        var embedded = real.LoadEmbedded();
        var rules = FakeRules.All.Select(ToRule).ToList();
        var document = new CatalogueDocument(SchemaIds.Catalogue, embedded.Id, embedded.Version, embedded.ReleasedOn, rules,
            embedded.Lifecycle, embedded.Builds, embedded.Capabilities, embedded.Data);
        using var stream = new MemoryStream(RiegelJson.SerializeToUtf8Bytes(document));
        return real.Load(stream, ReadLimits.Default);
    }

    private static LocalizedText Localized((string En, string De) t) => LocalizedText.Of(("de", t.De), ("en", t.En));

    private static Rule ToRule(FakeRule f) => new(f.Id, "2026.10", RuleState.Active, f.Category, f.Severity, f.Kind, Localized(f.Title),
        Localized(f.Rationale), f.OtNote is { } ot ? Localized(ot) : null, f.Remedy is { } remedy ? Localized(remedy) : null, f.Suggested,
        new Applicability(f.Os, null, null, f.Domain, false, [], null, AbsentCapability.NotApplicable), f.NeedsAdmin, f.Check, null,
        f.Attack.Length == 0 ? [] : [new RuleReference("ATTACK", null, null, null, null, f.Attack, null, null)], [], [], []);

    // ---------------------------------------------------------------- Fakten (spec 2.5, vereinfacht)

    public IReadOnlyDictionary<string, JsonElement> Facts(Snapshot s)
    {
        var facts = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        var view = new FakeView(s, new EvaluationContext(default, default, null, 35, 90, false, "en"), facts);
        var m = s.Machine;
        facts["os.kind"] = m.ProductType is { } pt ? JsonValues.Text(pt == 1 ? "client" : "server") : JsonValues.Null;
        facts["os.build"] = m.Build is { } b ? JsonValues.Number(b) : JsonValues.Null;
        facts["os.ubr"] = m.Ubr is { } u ? JsonValues.Number(u) : JsonValues.Null;
        facts["os.edition"] = m.EditionId is { } e ? JsonValues.Text(e) : JsonValues.Null;
        facts["os.arch"] = m.Architecture is { } a ? JsonValues.Text(a) : JsonValues.Null;
        facts["domain.joined"] = m.DomainRole is { } role ? JsonValues.Bool(role is 1 or 3 or 4 or 5) : JsonValues.Null;
        facts["domain.isDc"] = m.DomainRole is { } dc ? JsonValues.Bool(dc is 4 or 5) : JsonValues.Null;

        var cs = view.Rows(@"wmi:root\cimv2:Win32_ComputerSystem");
        facts["machine.mobile"] = cs is [var row, ..] && Num(row, "PCSystemType") is { } t ? JsonValues.Bool(t == 2) : JsonValues.Null;

        string mode;
        var status = view.Rows(@"wmi:root\Microsoft\Windows\Defender:MSFT_MpComputerStatus");
        if (status is [var st, ..])
        {
            mode = Text(st, "AMRunningMode") switch
            {
                "Normal" => "active",
                "Passive Mode" or "Passive" => "passive",
                "EDR Block Mode" => "edrBlock",
                _ => Bool(st, "AntivirusEnabled") == true && Bool(st, "RealTimeProtectionEnabled") == true ? "active" : "off",
            };
            facts["defender.rtp"] = Bool(st, "RealTimeProtectionEnabled") is { } rtp ? JsonValues.Bool(rtp) : JsonValues.Null;
        }
        else
        {
            mode = view.StatusOf(@"wmi:root\Microsoft\Windows\Defender:MSFT_MpComputerStatus") == ItemStatus.NamespaceMissing ? "absent" : "unknown";
            facts["defender.rtp"] = JsonValues.Null;
        }
        facts["defender.mode"] = JsonValues.Text(mode);

        bool? third = null;
        if (view.TryValue(ItemKeys.WscAntivirus, out var wsc) && wsc.ValueKind == JsonValueKind.Array)
            third = wsc.EnumerateArray().Any(p => Text(p, "state") == "on" && !(Text(p, "name") ?? "").Contains("Defender", StringComparison.OrdinalIgnoreCase));
        else if (view.Rows(@"wmi:root\SecurityCenter2:AntivirusProduct") is { } products)
            third = products.Any(p => !(Text(p, "displayName") ?? "").Contains("Defender", StringComparison.OrdinalIgnoreCase));
        facts["av.thirdPartyActive"] = third is { } th ? JsonValues.Bool(th) : JsonValues.Null;

        var ts = view.Reg(@"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections");
        facts["rdp.enabled"] = ts.Evidence == Evidence.Present ? JsonValues.Bool(Number(ts.Value) == 0) : JsonValues.Null;
        facts["office.installed"] = view.Item(@"reg:HKLM\SOFTWARE\Microsoft\Office\ClickToRun\Configuration") is { Status: ItemStatus.KeyMissing }
            ? JsonValues.False : JsonValues.Null;

        var adapters = view.Rows(@"wmi:root\cimv2:Win32_NetworkAdapterConfiguration");
        facts["net.gatewayConfigured"] = adapters is null ? JsonValues.Null
            : JsonValues.Bool(adapters.Any(r => r.TryGetProperty("DefaultIPGateway", out var g) && g.ValueKind == JsonValueKind.Array && g.GetArrayLength() > 0));
        var nics = view.Rows(@"wmi:root\cimv2:Win32_NetworkAdapter");
        facts["net.activeAdapters"] = nics is null ? JsonValues.Null : JsonValues.Number(nics.Count(r => Bool(r, "NetEnabled") == true));
        var medium = view.Rows(@"wmi:root\wmi:MSNdis_PhysicalMediumType");
        facts["net.wireless"] = medium is null ? JsonValues.Null : JsonValues.Bool(medium.Any(r => Num(r, "NdisPhysicalMediumType") == 9));
        var router = view.Reg(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "IPEnableRouter");
        facts["net.ipForwarding"] = router.Evidence == Evidence.Present ? JsonValues.Bool(Number(router.Value) == 1) : JsonValues.Null;
        facts["net.proxyConfigured"] = view.TryValue(ItemKeys.WinHttpProxy, out var proxy) && Num(proxy, "accessType") is { } at
            ? JsonValues.Bool(at != 1) : JsonValues.Null;
        return facts;
    }

    // ---------------------------------------------------------------- Auswertung

    public IReadOnlyList<RuleResult> Evaluate(Snapshot s, EvaluationContext ctx)
    {
        var facts = Facts(s);
        var view = new FakeView(s, ctx, facts);
        var results = new List<RuleResult>();
        foreach (var rule in FakeRules.All)
        {
            FakeOutcome outcome;
            var kind = view.Fact("os.kind");
            if (kind is null) outcome = new(ResultStatus.NotCheckable, Details: [view.T("Applicability unknown", "Anwendbarkeit nicht bestimmbar")]);
            else if (!rule.Os.Contains(kind)) outcome = new(ResultStatus.NotApplicable, Details: [view.T("Only for " + string.Join("/", rule.Os), "Nur für " + string.Join("/", rule.Os))]);
            else if (rule.NeedsAdmin && !s.IsAdmin) outcome = new(ResultStatus.NotCheckable, Details: [view.T("Collected without administrator rights", "Ohne Administratorrechte erfasst")]);
            else outcome = rule.Evaluate(view);
            results.Add(new RuleResult("riegel", rule.Id, outcome.Status, rule.Severity, outcome.Expected, outcome.Actual, outcome.Source,
                outcome.SourceDetail, outcome.Evidence ?? [], null, null, null, outcome.Flags, null, outcome.Details));
        }
        return results;
    }

    /// <summary>Kurzbeschreibung für das Protokoll (nie in der Oberfläche).</summary>
    public static string Describe() => new StringBuilder("Nachgebildete Dienste aktiv: ").Append(FakeRules.All.Count).Append(" Regeln").ToString();
}
