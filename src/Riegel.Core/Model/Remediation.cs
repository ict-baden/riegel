// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.Json.Serialization;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Model;

/// <summary>
/// Abhilfe einer Regel (spec 2.3). Riegel schreibt daraus nur Dateien (O10), führt nie etwas aus. Schlüsselpfade und
/// Wertnamen kommen ausschließlich aus dem Katalog.
/// </summary>
/// <param name="SecurityTemplate">Einträge für GptTmpl.inf (secedit, accountPolicy) bzw. [Privilege Rights] (userRight).</param>
/// <param name="Audit">Zeilen für audit.csv (erweiterte Überwachungsrichtlinie).</param>
/// <param name="Manual">Text für alles andere (Dienste, Features, BitLocker, Firmware).</param>
public sealed record RemediationSpec(IReadOnlyList<RegistryAction>? Registry, IReadOnlyList<TemplateAction>? SecurityTemplate,
    IReadOnlyList<AuditAction>? Audit, LocalizedText? Manual);

/// <summary><c>{ key, value, type, data }</c> zum Setzen oder <c>{ key, value, delete: true }</c> zum Löschen.</summary>
public sealed record RegistryAction(string Key, string Value, RegValueType? Type, JsonElement? Data,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Delete = false);

/// <summary>
/// <c>{ section, key, value }</c> oder für Benutzerrechte <c>{ section: "Privilege Rights", key: &lt;Recht&gt;, sids: [...] }</c>.
/// </summary>
public sealed record TemplateAction(string Section, string Key, string? Value, IReadOnlyList<string>? Sids);

/// <summary><c>{ guid, subcategory, setting }</c>; <paramref name="Subcategory"/> ist der englische Name aus dem Katalog.</summary>
public sealed record AuditAction(string Guid, string Subcategory, AuditSetting Setting);

[JsonConverter(typeof(CamelCaseEnumConverter<AuditSetting>))]
public enum AuditSetting { None, Success, Failure, SuccessAndFailure }
