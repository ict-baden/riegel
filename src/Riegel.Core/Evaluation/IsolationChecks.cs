// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model;

namespace Riegel.Core.Evaluation;

/// <summary>Prüfarten lsaProtection und deviceGuard (spec 2.3, 2.4 Punkt 6: Laufzeit vor Konfiguration).</summary>
internal sealed partial class EvaluationSession
{
    /// <summary>
    /// LSA-Schutz: api:lsass-protection (nur mit --deep) belegt den Laufzeitzustand; sonst das Wininit-Ereignis 12
    /// („LSASS.exe was started as a protected process“, Microsoft: Configure added LSA protection) seit dem letzten Start;
    /// sonst nur RunAsPPL – das allein ergibt nie pass: konfiguriert → „nicht prüfbar“ mit ConfiguredOnly, nicht
    /// konfiguriert → „nicht prüfbar“ (ein UEFI-gesperrter Schutz oder die Standardaktivierung ab 22H2 wären aus der
    /// Registry nicht zu sehen).
    /// </summary>
    private Outcome LsaProtection()
    {
        var o = new Outcome(ResultStatus.Pass) { Expected = "protected", Source = ValueSource.Runtime };
        var runtime = Store.Item(ItemKeys.LsassProtection);
        if (runtime.IsPresent && runtime.Value.ValueKind == JsonValueKind.Object)
        {
            var type = JsonRead.String(runtime.Value, "type")?.ToLowerInvariant();
            if (type is "light" or "full") return (o with { Actual = type }).DecidedBy(ItemKeys.LsassProtection);
            if (type == "none") return (o with { Status = ResultStatus.Fail, Actual = "none" }).DecidedBy(ItemKeys.LsassProtection);
        }
        var evt = Store.Item(ItemKeys.WininitEvent12);
        if (evt.IsPresent && evt.Value.ValueKind == JsonValueKind.Object && JsonRead.Bool(evt.Value, "found") == true)
        {
            var created = JsonRead.Time(JsonRead.Get(evt.Value, "timeCreatedUtc"));
            var boot = JsonRead.Time(JsonRead.Get(evt.Value, "bootTimeUtc")) ?? Facts.LastBootUtc;
            if (created is { } t && (boot is null || t >= boot))
                return (o with { Actual = "protected" }).DecidedBy(ItemKeys.WininitEvent12).With(T["Detail_LsaEvent", t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)]);
        }
        // Nur noch die Konfiguration
        var config = Store.RegistryValue(Known.Lsa, "RunAsPPL");
        var notProven = new Outcome(ResultStatus.NotCheckable, RglCodesEngine.ConfiguredOnly) { Expected = "protected", Source = ValueSource.Unknown };
        if (config.Class == EvidenceClass.Unavailable) return Unavailable(config.Reason, config.ItemKey, null, "protected");
        if (config.IsPresent && config.Type == "dword" && config.V is { ValueKind: JsonValueKind.Number } n && n.TryGetInt64(out var value))
        {
            var (src, detail) = SourceOf(config.ItemKey, "RunAsPPL", null);
            notProven = notProven with { Actual = value.ToString(CultureInfo.InvariantCulture), Source = src, SourceDetail = detail };
            if (value is 1 or 2) return notProven.WithFlags(ResultFlags.ConfiguredOnly).With(T["Detail_ConfiguredOnly"]);
            return (notProven with { ReasonCode = RglCodesEngine.NoDecisiveEvidence }).With(T["Detail_LsaNotConfigured"]);
        }
        return (notProven with { ReasonCode = RglCodesEngine.NoDecisiveEvidence, Source = config.IsPresent ? ValueSource.Unknown : ValueSource.NotSet })
            .With(T["Detail_LsaNoRuntime"]);
    }

    /// <summary>
    /// VBS, Speicherintegrität (HVCI) und Credential Guard nach Win32_DeviceGuard: SecurityServicesRunning enthält
    /// 1 (Credential Guard) bzw. 2 (Speicherintegrität), VirtualizationBasedSecurityStatus 2 = läuft (Microsoft: Enable
    /// memory integrity). Konfiguriert, aber nicht laufend → fail „konfiguriert, läuft nicht“; mit requireRunning false
    /// stattdessen „nicht prüfbar“ mit ConfiguredOnly – nie pass ohne laufenden Dienst.
    /// </summary>
    private Outcome DeviceGuard(DeviceGuardCheck c)
    {
        var o = new Outcome(ResultStatus.Fail) { Expected = "running", Source = ValueSource.Runtime };
        var item = Store.Item(Known.DeviceGuard);
        if (!item.IsPresent) return Unavailable(item.Reason, item.Key, null, "running");
        if (JsonRead.Rows(item.Value) is not { Count: > 0 } rows) return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.UnexpectedValue };
        var row = rows[0];
        bool? running, configured;
        if (c.Service == DeviceGuardService.Vbs)
        {
            var status = JsonRead.Integer(row, "VirtualizationBasedSecurityStatus");
            running = status switch { 2 => true, 0 or 1 => false, _ => null };
            configured = status switch { 1 or 2 => true, 0 => false, _ => null };
        }
        else
        {
            int id = c.Service == DeviceGuardService.CredentialGuard ? 1 : 2;
            running = Contains(JsonRead.Get(row, "SecurityServicesRunning"), id);
            configured = Contains(JsonRead.Get(row, "SecurityServicesConfigured"), id);
        }
        if (running is null) return o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.UnexpectedValue };
        if (running == true) return o with { Status = ResultStatus.Pass, Actual = "running" };
        o = o with { Actual = configured == true ? "configured" : "off" };
        if (configured != true) return o;
        return c.RequireRunning
            ? o.With(T["Detail_ConfiguredNotRunning"])
            : (o with { Status = ResultStatus.NotCheckable, ReasonCode = RglCodesEngine.ConfiguredOnly }).WithFlags(ResultFlags.ConfiguredOnly).With(T["Detail_ConfiguredNotRunning"]);
    }

    /// <summary>Enthält die WMI-Liste den Wert? null = keine Liste (unbekannt).</summary>
    private static bool? Contains(JsonElement? list, int id)
    {
        if (list is not { ValueKind: JsonValueKind.Array } a) return null;
        foreach (var e in a.EnumerateArray())
            if (JsonRead.Integer(e) == id) return true;
        return false;
    }
}
