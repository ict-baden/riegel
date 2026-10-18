// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Diagnostics;
using Riegel.Core.Model;
using Riegel.Core.Parsing;

namespace Riegel.Core.Snapshots;

/// <summary>
/// Was beim Import mit jedem Export geschieht (spec 7.3): Geheimnisse erneut schwärzen und die <c>parsed:*</c>-Einträge
/// aus den Rohdaten berechnen. In Exporten mit einer anderen Herkunft als <c>synthetic</c> gelten mitgelieferte
/// <c>parsed:*</c>-Einträge als feindlich: sie werden verworfen und neu berechnet. Synthetische Fixtures dürfen sie
/// direkt enthalten; fehlende werden auch dort aus Rohdaten ergänzt.
/// </summary>
internal static class DerivedItems
{
    /// <summary>Die abgeleiteten Einträge in fester Reihenfolge.</summary>
    internal static readonly string[] Keys =
        [ItemKeys.ParsedSecedit, ItemKeys.ParsedRegistryPol, ItemKeys.ParsedGpresult, ItemKeys.ParsedFirewallRulesRegistry];

    internal static Snapshot Process(Snapshot snapshot, ReadLimits limits)
    {
        bool synthetic = snapshot.Origin == SnapshotOrigin.Synthetic;
        var items = new List<SnapshotItem>(snapshot.Items.Count + Keys.Length);
        int rejected = 0;
        foreach (var item in snapshot.Items)
        {
            if (item.Key.StartsWith("parsed:", StringComparison.OrdinalIgnoreCase) && !synthetic)
            {
                rejected++;
                continue;
            }
            items.Add(Redact(item, limits));
        }
        if (rejected > 0)
            Log.Warning(RglCodesParsing.ItemError(RglCodesParsing.ParsedItemRecomputed, rejected.ToString(CultureInfo.InvariantCulture)));
        var cleaned = snapshot with { Items = items };
        var present = new HashSet<string>(items.Select(i => i.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var key in Keys)
        {
            if (present.Contains(key)) continue;
            var derived = Derive(key, cleaned, limits, synthetic);
            if (derived is not null) items.Add(derived);
        }
        return cleaned with { Items = items };
    }

    // ---------------------------------------------------------------- Schwärzen

    private static SnapshotItem Redact(SnapshotItem item, ReadLimits limits)
    {
        if (item.Value is not { } value || item.Status is not (ItemStatus.Ok or ItemStatus.ValueMissing)) return item;
        try
        {
            if (item.Key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase))
                return Secrets.RedactRegistryItem(item.Key[4..], value) is { } redacted ? item with { Value = redacted } : item;
            if (ItemKeys.Same(item.Key, ItemKeys.RawSeceditInf) && Text(value) is { } inf)
                return SeceditParser.RedactText(inf) is { } clean ? item with { Value = TextValue(clean) } : item;
            if (ItemKeys.Same(item.Key, ItemKeys.RawGpresultXml) && Text(value) is { } xml)
                return GpresultParser.Redact(xml, item.Key, limits.MaxTextFileBytes) is { } clean ? item with { Value = TextValue(clean) } : item;
            if (ItemKeys.Same(item.Key, ItemKeys.RawRegistryPol)) return RedactRegistryPol(item, value, limits);
        }
        catch (RiegelException e)
        {
            // Unlesbare Rohdaten können Geheimnisse enthalten, die sich nicht finden lassen: Inhalt verwerfen
            return item with { Status = ItemStatus.Error, Value = null, ErrorCode = null, Error = e.Code + ": " + e.Message };
        }
        return item;
    }

    private static SnapshotItem RedactRegistryPol(SnapshotItem item, JsonElement value, ReadLimits limits)
    {
        var bytes = PolBytes(value, limits, checkHash: false);
        var entries = RegistryPolParser.Parse(bytes, item.Key, limits.MaxItems);
        var clean = RegistryPolParser.Redact(bytes, entries);
        if (clean is null) return item;
        var sha = value.TryGetProperty("sha256", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : Hashes.Sha256Hex(bytes);
        return item with
        {
            Value = JsonValues.Build(w =>
            {
                w.WriteStartObject();
                w.WriteString("base64", Convert.ToBase64String(clean));
                w.WriteString("sha256", sha);
                w.WriteBoolean("redacted", true);
                w.WriteEndObject();
            }),
        };
    }

    // ---------------------------------------------------------------- Ableiten

    private static SnapshotItem? Derive(string key, Snapshot snapshot, ReadLimits limits, bool synthetic)
    {
        if (key == ItemKeys.ParsedFirewallRulesRegistry) return FirewallRules(snapshot, limits, synthetic);
        var rawKey = key switch
        {
            ItemKeys.ParsedSecedit => ItemKeys.RawSeceditInf,
            ItemKeys.ParsedRegistryPol => ItemKeys.RawRegistryPol,
            _ => ItemKeys.RawGpresultXml,
        };
        var raw = snapshot.Items.FirstOrDefault(i => ItemKeys.Same(i.Key, rawKey));
        if (raw is null)
        {
            // Synthetische Fixtures ohne Rohdaten bleiben, wie sie sind
            if (synthetic) return null;
            return new SnapshotItem(key, ItemStatus.NotCollected, null, null, RglCodesParsing.ItemError(RglCodesParsing.RawSourceUnavailable, rawKey), 0);
        }
        if (raw.Status != ItemStatus.Ok || raw.Value is not { } value)
            return new SnapshotItem(key, raw.Status == ItemStatus.Ok ? ItemStatus.Error : raw.Status, null, raw.ErrorCode,
                raw.Error ?? RglCodesParsing.ItemError(RglCodesParsing.RawSourceUnavailable, rawKey), 0);
        try
        {
            var parsed = key switch
            {
                ItemKeys.ParsedSecedit => SeceditParser.ToParsedItem(SeceditParser.Parse(
                    Text(value) ?? throw RglCodesParsing.Error(RglCodesParsing.InfInvalid, rawKey, 0), rawKey, limits.MaxStringChars)),
                ItemKeys.ParsedRegistryPol => RegistryPol(value, rawKey, limits),
                _ => GpresultParser.ToParsedItem(GpresultParser.Parse(
                    Text(value) ?? throw RglCodesParsing.Error(RglCodesParsing.GpresultInvalid, rawKey, "text"), rawKey, limits.MaxTextFileBytes)),
            };
            return new SnapshotItem(key, ItemStatus.Ok, parsed, null, null, 0, raw.Truncated);
        }
        catch (RiegelException e)
        {
            return new SnapshotItem(key, ItemStatus.Error, null, null, e.Code + ": " + e.Message, 0);
        }
    }

    private static JsonElement RegistryPol(JsonElement value, string rawKey, ReadLimits limits) =>
        RegistryPolParser.ToParsedItem(RegistryPolParser.Parse(PolBytes(value, limits, checkHash: true), rawKey, limits.MaxItems), limits.MaxStringChars);

    /// <summary>Bytes aus <c>{ base64, sha256 }</c>; Größe und (wenn nicht geschwärzt) Prüfsumme geprüft.</summary>
    private static byte[] PolBytes(JsonElement value, ReadLimits limits, bool checkHash)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("base64", out var b64) || b64.ValueKind != JsonValueKind.String)
            throw RglCodesParsing.Error(RglCodesParsing.RegistryPolInvalid, ItemKeys.RawRegistryPol, 0);
        var text = b64.GetString()!;
        if ((long)text.Length / 4 * 3 > limits.MaxRegistryPolBytes)
            throw RglCodesParsing.Error(RglCodesParsing.SnapshotTooLarge, ItemKeys.RawRegistryPol, limits.MaxRegistryPolBytes);
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            throw RglCodesParsing.Error(RglCodesParsing.RegistryPolInvalid, ItemKeys.RawRegistryPol, 0);
        }
        bool redacted = value.TryGetProperty("redacted", out var r) && r.ValueKind == JsonValueKind.True;
        if (checkHash && !redacted && value.TryGetProperty("sha256", out var sha) && sha.ValueKind == JsonValueKind.String
            && !string.Equals(sha.GetString(), Hashes.Sha256Hex(bytes), StringComparison.OrdinalIgnoreCase))
            throw RglCodesParsing.Error(RglCodesParsing.RawHashMismatch, ItemKeys.RawRegistryPol);
        return bytes;
    }

    /// <summary>
    /// Firewall-Regeln beider Registry-Speicher. Nur wenn beide Belege tragen (vorhanden oder nachweislich nicht
    /// vorhanden), entsteht ein Eintrag Ok; sonst übernimmt er den Status des fehlenden Speichers.
    /// </summary>
    private static SnapshotItem? FirewallRules(Snapshot snapshot, ReadLimits limits, bool synthetic)
    {
        var evidence = new RegistryEvidence(snapshot);
        var stores = new[] { ("gpo", FirewallRuleParser.GpoStoreKey), ("local", FirewallRuleParser.LocalStoreKey) };
        var rules = new List<FirewallRule>();
        bool truncated = false, anyPresent = false;
        foreach (var (store, keyPath) in stores)
        {
            var state = evidence.Classify(keyPath, out var item);
            if (state == KeyEvidence.Unavailable)
            {
                // Synthetische Exporte ohne Firewall-Daten bekommen keinen Eintrag
                if (synthetic && item is null) return null;
                var status = item?.Status is { } s && s is not (ItemStatus.Ok or ItemStatus.ValueMissing or ItemStatus.KeyMissing) ? s : ItemStatus.NotCollected;
                return new SnapshotItem(ItemKeys.ParsedFirewallRulesRegistry, status, null, item?.ErrorCode,
                    RglCodesParsing.ItemError(RglCodesParsing.RawSourceUnavailable, ItemKeys.Registry(keyPath)), 0);
            }
            if (state == KeyEvidence.NotSet || item?.Value is not { } value) continue;
            anyPresent = true;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object)
                continue;
            if (value.TryGetProperty("valueFilter", out _)) truncated = true;
            foreach (var property in values.EnumerateObject())
            {
                var rv = property.Value;
                bool isText = rv.ValueKind == JsonValueKind.Object && rv.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.String
                              && t.GetString() == "sz" && rv.TryGetProperty("v", out var v) && v.ValueKind == JsonValueKind.String;
                bool cut = rv.ValueKind == JsonValueKind.Object && rv.TryGetProperty("truncated", out var tr) && tr.ValueKind == JsonValueKind.True;
                if (cut) truncated = true;
                rules.Add(isText && !cut
                    ? FirewallRuleParser.Parse(store, property.Name, rv.GetProperty("v").GetString()!)
                    : new FirewallRule(store, property.Name, null, null, false, null, null, [], [], [], null, null, true));
            }
        }
        if (synthetic && !anyPresent && rules.Count == 0 && !snapshot.Items.Any(i => i.Key.StartsWith("reg:", StringComparison.OrdinalIgnoreCase)))
            return null;
        return new SnapshotItem(ItemKeys.ParsedFirewallRulesRegistry, ItemStatus.Ok, FirewallRuleParser.ToParsedItem(rules), null, null, 0, truncated);
    }

    // ---------------------------------------------------------------- Helfer

    private static string? Text(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;

    private static JsonElement TextValue(string text) => JsonValues.Build(w =>
    {
        w.WriteStartObject();
        w.WriteString("text", text);
        w.WriteEndObject();
    });
}
