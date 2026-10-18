// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;

namespace Riegel.Core.ReportBuilding;

/// <summary>
/// Fakten eines Rechners für Bericht und Versicherer-Blatt (spec 5 O7): Supportstand, Virenschutz, Patchstand,
/// lokale Administratoren, RDP, SMBv1, BitLocker, Firewall und Netzanbindung. Entscheidend sind die Ergebnisse der
/// Regeln (LIFE-01, LIFE-02, RDP-01, NET-01/02, ENC-01, FW-01–03); wo es keine gibt, die Rohdaten des Exports.
/// Unbekanntes bleibt null – nichts wird geraten.
/// </summary>
internal static class MachineFacts
{
    public const string DefenderProduct = "Microsoft Defender Antivirus";

    private const string MpStatus = @"wmi:root\Microsoft\Windows\Defender:MSFT_MpComputerStatus";
    private const string SecurityCenter = @"wmi:root\SecurityCenter2:AntivirusProduct";
    private const string Qfe = @"wmi:root\cimv2:Win32_QuickFixEngineering";
    private const string TerminalServer = @"reg:HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server";
    private const string SmbServer = @"wmi:root\Microsoft\Windows\SMB:MSFT_SmbServerConfiguration.GetConfiguration";

    /// <summary>Rechnerkopf aus dem Export; fehlt der Export, aus den Fakten des Laufs.</summary>
    public static MachineInfo Header(Run run, Snapshot? snapshot)
    {
        if (snapshot is not null) return snapshot.Machine;
        string? Text(string fact) => run.Facts.TryGetValue(fact, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int? Number(string fact) => run.Facts.TryGetValue(fact, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
        int? productType = Text("os.kind") switch { "client" => 1, "server" => 3, _ => null };
        return new MachineInfo(run.HostName, null, null, productType, Number("os.build"), Number("os.ubr"), Text("os.edition"),
            Text("os.installationType"), null, Text("os.arch"), null, null, null);
    }

    /// <summary>Supportstand: Eintrag der Lebenszyklus-Tabelle; „unterstützt“ aus LIFE-01, sonst aus der Tabelle.</summary>
    public static LifecycleView? Lifecycle(Run run, MachineInfo machine, ICatalogue catalogue, bool esuCovered, DateOnly date)
    {
        var entry = LifecycleEntry(machine, catalogue);
        bool? supported = Status(run, "LIFE-01") switch
        {
            ResultStatus.Pass => true,
            ResultStatus.Fail or ResultStatus.Exception => false,
            _ => null,
        };
        if (entry is null) return supported is null ? null : new LifecycleView(null, null, null, esuCovered, supported);
        var end = esuCovered && entry.EsuEnd is { } esu ? esu : entry.EndOfSupport;
        supported ??= date <= end;
        return new LifecycleView(entry.Name, entry.EndOfSupport, entry.EsuEnd, esuCovered, supported);
    }

    private static LifecycleEntry? LifecycleEntry(MachineInfo m, ICatalogue catalogue)
    {
        if (m.Build is not { } build) return null;
        var os = m.ProductType switch { 1 => "client", 2 or 3 => "server", _ => null };
        return catalogue.Lifecycle.Products.FirstOrDefault(p =>
            p.Build == build &&
            (os is null || string.Equals(p.Os, os, StringComparison.OrdinalIgnoreCase)) &&
            (p.Editions is not { Count: > 0 } editions || editions.Contains(m.EditionId ?? "", StringComparer.OrdinalIgnoreCase)) &&
            (p.InstallationTypes is not { Count: > 0 } types || types.Contains(m.InstallationType ?? "", StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>Eine Zeile „Fakten für Versicherer-Fragebögen“.</summary>
    public static InsurerFactsRow Insurer(Run run, MachineInfo machine, Snapshot? snapshot, LifecycleView? lifecycle, ICatalogue catalogue)
    {
        var (product, state, age) = Antivirus(run, snapshot);
        DateOnly? supportEnd = lifecycle is null ? null : lifecycle.EsuCovered && lifecycle.EsuEnd is { } esu ? esu : lifecycle.EndOfSupport;
        int? daysSinceUpdate = Status(run, "LIFE-02") is ResultStatus.Pass or ResultStatus.Fail or ResultStatus.Exception &&
            Result(run, "LIFE-02")?.Actual is { } actual && CanonicalValue.TryParseInteger(actual, out var days) && days <= int.MaxValue ? (int)days : null;
        bool? rdpOn = Bool(run, "rdp.enabled") ?? RdpFromRegistry(snapshot);
        bool? nla = Status(run, "RDP-01") switch { ResultStatus.Pass => true, ResultStatus.Fail or ResultStatus.Exception => false, _ => null };
        bool? smb1 = AnyOpen(run, "NET-01", "NET-02") ?? Smb1FromSnapshot(snapshot);
        bool? bitLocker = Status(run, "ENC-01") switch { ResultStatus.Pass => true, ResultStatus.Fail or ResultStatus.Exception => false, _ => null };
        bool? firewall = AnyOpen(run, "FW-01", "FW-02", "FW-03") is { } open ? !open : FirewallFromSnapshot(snapshot);
        return new InsurerFactsRow(run.HostName, lifecycle?.ProductName ?? OsName(machine), machine.Build, supportEnd, lifecycle?.Supported,
            product, state, age, daysSinceUpdate, PatchMonth(machine, snapshot, catalogue), LocalAdmins(snapshot), rdpOn, nla, smb1, bitLocker,
            firewall, Network(run));
    }

    /// <summary>„Netzanbindung“ aus den Fakten net.* (spec 2.5).</summary>
    public static NetworkFacts Network(Run run) =>
        new(Bool(run, "net.gatewayConfigured"), Int(run, "net.activeAdapters"), Bool(run, "net.wireless"), Bool(run, "net.ipForwarding"),
            Bool(run, "net.proxyConfigured"));

    /// <summary>
    /// Virenschutz: Defender aktiv → Produkt, „on“/„off“ nach Echtzeitschutz, Signaturalter; sonst der aktive
    /// Drittanbieter laut WSC-API, sonst SecurityCenter2 (Heuristik, Zustand unbekannt).
    /// </summary>
    private static (string? Product, string? State, int? SignatureAge) Antivirus(Run run, Snapshot? snapshot)
    {
        var mode = Text(run, "defender.mode");
        var status = FirstRow(snapshot, MpStatus);
        if (mode == "active")
        {
            bool? rtp = Bool(run, "defender.rtp") ?? RowBool(status, "RealTimeProtectionEnabled");
            return (DefenderProduct, rtp is null ? null : rtp.Value ? "on" : "off", RowInt(status, "AntivirusSignatureAge"));
        }
        if (Item(snapshot, "api:wsc-av") is { ValueKind: JsonValueKind.Array } wsc)
        {
            foreach (var product in wsc.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Object))
            {
                var name = Prop(product, "name");
                var state = Prop(product, "state");
                if (name is null || name.Contains("Defender", StringComparison.OrdinalIgnoreCase)) continue;
                if (state == "on") return (name, state, null);
            }
        }
        if (mode is "passive" or "edrBlock") return (DefenderProduct, mode, null);
        if (FirstRow(snapshot, SecurityCenter) is { } heuristic && Prop(heuristic, "displayName") is { } display) return (display, null, null);
        return (null, mode == "off" ? "off" : null, null);
    }

    private static string? PatchMonth(MachineInfo m, Snapshot? snapshot, ICatalogue catalogue)
    {
        if (m.Build is { } build && m.Ubr is { } ubr &&
            catalogue.Builds.Find(build, BuildTable.OsKindOf(m.ProductType))?.Releases.FirstOrDefault(r => r.Ubr == ubr) is { } release)
            return release.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        DateTimeOffset? newest = null;
        if (Item(snapshot, Qfe) is { ValueKind: JsonValueKind.Object } qfe && qfe.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            foreach (var row in rows.EnumerateArray())
                if (Prop(row, "InstalledOn") is { } text && Model.Json.UtcDateTimeOffsetConverter.TryParse(text, out var when) && (newest is null || when > newest))
                    newest = when;
        return newest?.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }

    private static int? LocalAdmins(Snapshot? snapshot) =>
        Item(snapshot, ItemKeys.LocalAdministrators) is { ValueKind: JsonValueKind.Array } members ? members.GetArrayLength() : null;

    private static bool? RdpFromRegistry(Snapshot? snapshot)
    {
        if (Item(snapshot, TerminalServer) is { ValueKind: JsonValueKind.Object } key && key.TryGetProperty("values", out var values) &&
            values.ValueKind == JsonValueKind.Object)
            foreach (var p in values.EnumerateObject())
                if (string.Equals(p.Name, "fDenyTSConnections", StringComparison.OrdinalIgnoreCase) &&
                    CanonicalValue.RenderRegistry(p.Value) is { } v && CanonicalValue.TryParseInteger(v, out var deny))
                    return deny == 0;
        return null;
    }

    private static bool? Smb1FromSnapshot(Snapshot? snapshot) =>
        Item(snapshot, SmbServer) is { ValueKind: JsonValueKind.Object } v && v.TryGetProperty("out", out var output) &&
        output.ValueKind == JsonValueKind.Object && output.TryGetProperty("EnableSMB1Protocol", out var smb1) &&
        smb1.ValueKind is JsonValueKind.True or JsonValueKind.False ? smb1.GetBoolean() : null;

    private static bool? FirewallFromSnapshot(Snapshot? snapshot)
    {
        if (Item(snapshot, ItemKeys.Firewall) is not { ValueKind: JsonValueKind.Object } fw || !fw.TryGetProperty("profiles", out var profiles) ||
            profiles.ValueKind != JsonValueKind.Object) return null;
        bool any = false;
        foreach (var p in profiles.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Object || !p.Value.TryGetProperty("enabled", out var enabled) ||
                enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
            if (!enabled.GetBoolean()) return false;
            any = true;
        }
        return any ? true : null;
    }

    private static string? OsName(MachineInfo m) =>
        m.EditionId is null ? null : m.DisplayVersion is null ? m.EditionId : m.EditionId + " " + m.DisplayVersion;

    // ---------------------------------------------------------------- Helfer

    public static RuleResult? Result(Run run, string ruleId) =>
        run.Results.FirstOrDefault(r => r.RuleSetId == "riegel" && string.Equals(r.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));

    private static ResultStatus? Status(Run run, string ruleId) => Result(run, ruleId)?.Status;

    /// <summary>true = mindestens eine Regel offen (fail/exception), false = alle vorhandenen bestanden, null = keine entschieden.</summary>
    private static bool? AnyOpen(Run run, params string[] ruleIds)
    {
        var decided = ruleIds.Select(id => Status(run, id)).Where(s => s is ResultStatus.Pass or ResultStatus.Fail or ResultStatus.Exception).ToList();
        if (decided.Count == 0) return null;
        if (decided.Any(s => s is ResultStatus.Fail or ResultStatus.Exception)) return true;
        return decided.Count == ruleIds.Length ? false : null;
    }

    private static bool? Bool(Run run, string fact) =>
        run.Facts.TryGetValue(fact, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static int? Int(Run run, string fact) =>
        run.Facts.TryGetValue(fact, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    private static string? Text(Run run, string fact) =>
        run.Facts.TryGetValue(fact, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static JsonElement? Item(Snapshot? snapshot, string key)
    {
        var item = snapshot?.Items.FirstOrDefault(i => ItemKeys.Same(i.Key, key));
        return item is { Status: ItemStatus.Ok, Value: { } value } ? value : null;
    }

    private static JsonElement? FirstRow(Snapshot? snapshot, string key) =>
        Item(snapshot, key) is { ValueKind: JsonValueKind.Object } v && v.TryGetProperty("rows", out var rows) &&
        rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() > 0 ? rows[0] : null;

    private static bool? RowBool(JsonElement? row, string property) =>
        row is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty(property, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : null;

    private static int? RowInt(JsonElement? row, string property) =>
        row is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n : null;

    private static string? Prop(JsonElement row, string property)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in row.EnumerateObject())
            if (string.Equals(p.Name, property, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString();
        return null;
    }
}
