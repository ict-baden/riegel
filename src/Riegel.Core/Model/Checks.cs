// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.Json.Serialization;
using Riegel.Core.Model.Json;

namespace Riegel.Core.Model;

// ---- Geschlossenes Prüf-Vokabular (spec 2.3, Tabelle „Check types“) ----
// Jeder Typ hat genau die Felder der Tabelle. Die Engine führt nie Zeichenketten aus Regeln, Importen oder Exporten aus.

/// <summary>
/// Basis aller Prüfungen; in JSON unterschieden über <c>"type"</c> (darf an beliebiger Stelle im Objekt stehen).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(RegistryCheck), CheckTypes.Registry)]
[JsonDerivedType(typeof(RegistryEffectiveCheck), CheckTypes.RegistryEffective)]
[JsonDerivedType(typeof(SeceditCheck), CheckTypes.Secedit)]
[JsonDerivedType(typeof(AccountPolicyCheck), CheckTypes.AccountPolicy)]
[JsonDerivedType(typeof(AuditSubcategoryCheck), CheckTypes.AuditSubcategory)]
[JsonDerivedType(typeof(UserRightCheck), CheckTypes.UserRight)]
[JsonDerivedType(typeof(ServiceCheck), CheckTypes.Service)]
[JsonDerivedType(typeof(FeatureCheck), CheckTypes.Feature)]
[JsonDerivedType(typeof(WmiPropertyCheck), CheckTypes.WmiProperty)]
[JsonDerivedType(typeof(DefenderSettingCheck), CheckTypes.DefenderSetting)]
[JsonDerivedType(typeof(AsrRuleCheck), CheckTypes.AsrRule)]
[JsonDerivedType(typeof(FirewallProfileCheck), CheckTypes.FirewallProfile)]
[JsonDerivedType(typeof(FirewallExposureCheck), CheckTypes.FirewallExposure)]
[JsonDerivedType(typeof(LifecycleCheck), CheckTypes.Lifecycle)]
[JsonDerivedType(typeof(UpdateAgeCheck), CheckTypes.UpdateAge)]
[JsonDerivedType(typeof(LsaProtectionCheck), CheckTypes.LsaProtection)]
[JsonDerivedType(typeof(DeviceGuardCheck), CheckTypes.DeviceGuard)]
[JsonDerivedType(typeof(AvStatusCheck), CheckTypes.AvStatus)]
[JsonDerivedType(typeof(LocalAccountCheck), CheckTypes.LocalAccount)]
[JsonDerivedType(typeof(LocalAccountsCheck), CheckTypes.LocalAccounts)]
[JsonDerivedType(typeof(LocalGroupCheck), CheckTypes.LocalGroup)]
[JsonDerivedType(typeof(InventoryCheck), CheckTypes.Inventory)]
[JsonDerivedType(typeof(FirstOfCheck), CheckTypes.FirstOf)]
[JsonDerivedType(typeof(AllOfCheck), CheckTypes.AllOf)]
[JsonDerivedType(typeof(AnyOfCheck), CheckTypes.AnyOf)]
[JsonDerivedType(typeof(NoneOfCheck), CheckTypes.NoneOf)]
[JsonDerivedType(typeof(NotCheck), CheckTypes.Not)]
public abstract record CheckSpec([property: JsonIgnore] string Type);

/// <summary>Die Werte von <c>"type"</c>.</summary>
public static class CheckTypes
{
    public const string Registry = "registry";
    public const string RegistryEffective = "registryEffective";
    public const string Secedit = "secedit";
    public const string AccountPolicy = "accountPolicy";
    public const string AuditSubcategory = "auditSubcategory";
    public const string UserRight = "userRight";
    public const string Service = "service";
    public const string Feature = "feature";
    public const string WmiProperty = "wmiProperty";
    public const string DefenderSetting = "defenderSetting";
    public const string AsrRule = "asrRule";
    public const string FirewallProfile = "firewallProfile";
    public const string FirewallExposure = "firewallExposure";
    public const string Lifecycle = "lifecycle";
    public const string UpdateAge = "updateAge";
    public const string LsaProtection = "lsaProtection";
    public const string DeviceGuard = "deviceGuard";
    public const string AvStatus = "avStatus";
    public const string LocalAccount = "localAccount";
    public const string LocalAccounts = "localAccounts";
    public const string LocalGroup = "localGroup";
    public const string Inventory = "inventory";
    public const string FirstOf = "firstOf";
    public const string AllOf = "allOf";
    public const string AnyOf = "anyOf";
    public const string NoneOf = "noneOf";
    public const string Not = "not";

    /// <summary>Alle 27 Typen in der Reihenfolge der Tabelle.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Registry, RegistryEffective, Secedit, AccountPolicy, AuditSubcategory, UserRight, Service, Feature, WmiProperty,
        DefenderSetting, AsrRule, FirewallProfile, FirewallExposure, Lifecycle, UpdateAge, LsaProtection, DeviceGuard,
        AvStatus, LocalAccount, LocalAccounts, LocalGroup, Inventory, FirstOf, AllOf, AnyOf, NoneOf, Not,
    ];
}

/// <summary>Registry-Wert vergleichen (Beleg: <c>reg:</c>-Eintrag). Streng typisiert (spec 2.4 Punkt 3).</summary>
/// <param name="Key"><c>HKLM\…</c> ohne „reg:“.</param>
/// <param name="Value">Name des Werts, "" = Standardwert.</param>
/// <param name="AcceptTypes">Weitere zulässige Typen, z. B. sz für eine dword-Regel (invariant dezimal gelesen).</param>
/// <param name="Expected">Sollwert; fehlt bei exists/notExists.</param>
/// <param name="WhenMissing">Was gilt, wenn der Wert nachweislich nicht gesetzt ist (nur NotSet, spec 2.4 Punkt 2).</param>
/// <param name="OsDefaults">Belegte Standardwerte je Build/Edition (nur mit whenMissing osDefault).</param>
/// <param name="MissingPass">Beleg für whenMissing pass (zusätzlich Eintrag in der Erlaubnisliste des Regelpakets).</param>
/// <param name="MultiSz">Vergleich von multi_sz als Menge (Standard) oder Liste.</param>
public sealed record RegistryCheck(string Key, string Value, RegValueType ValueType, IReadOnlyList<RegValueType>? AcceptTypes,
    CheckOperator Op, JsonElement? Expected, WhenMissing WhenMissing, IReadOnlyList<OsDefault>? OsDefaults,
    MissingPass? MissingPass, MultiSzMode? MultiSz) : CheckSpec(CheckTypes.Registry);

/// <summary>Wirksamer Wert aus mehreren Quellen: die erste vorhandene gewinnt; Herkunft wird berichtet.</summary>
/// <param name="Sources">Geordnet, z. B. Richtlinie vor MDM vor lokal. Eine nicht verfügbare Quelle vor der ersten
/// vorhandenen ergibt notCheckable.</param>
/// <param name="Selection">Wie die wirksame Quelle gewählt wird: <c>value</c> (Standard, null) – die erste Quelle, in der
/// der Wert steht; <c>root</c> – der erste Schlüssel mit irgendeinem Wert ist die aktive Richtlinie, ein dort fehlender
/// Wert gilt als nicht gesetzt (Windows LAPS: Einstellungen werden nie über Wurzeln hinweg geteilt).</param>
public sealed record RegistryEffectiveCheck(IReadOnlyList<EffectiveSource> Sources, RegValueType ValueType,
    IReadOnlyList<RegValueType>? AcceptTypes, CheckOperator Op, JsonElement? Expected, WhenMissing WhenMissing,
    IReadOnlyList<OsDefault>? OsDefaults, MissingPass? MissingPass, MultiSzMode? MultiSz, EffectiveSelection? Selection = null)
    : CheckSpec(CheckTypes.RegistryEffective)
{
    public IReadOnlyList<EffectiveSource> Sources { get; init; } = Sources ?? [];
}

/// <summary>Auswahl der wirksamen Quelle von <see cref="RegistryEffectiveCheck"/>.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<EffectiveSelection>))]
public enum EffectiveSelection { Value, Root }

/// <summary>Eine Quelle von <see cref="RegistryEffectiveCheck"/>.</summary>
public sealed record EffectiveSource(string Key, string Value, ValueOrigin Origin);

[JsonConverter(typeof(CamelCaseEnumConverter<ValueOrigin>))]
public enum ValueOrigin { Policy, Mdm, Local, Other }

/// <summary>Wert aus dem Sicherheitstemplate (<c>parsed:secedit</c>). <c>[Registry Values]</c>-Einträge sind „typ,wert“.</summary>
public sealed record SeceditCheck(string Section, string Key, CheckOperator Op, JsonElement? Expected,
    SeceditValueKind ValueKind, WhenMissing WhenMissing) : CheckSpec(CheckTypes.Secedit);

[JsonConverter(typeof(CamelCaseEnumConverter<SeceditValueKind>))]
public enum SeceditValueKind { Number, String, RegistryValue }

/// <summary>
/// Kontorichtlinie: zuerst <c>api:netusermodals</c>, dann <c>parsed:secedit</c> [System Access] (feste Zuordnung in
/// docs/file-formats.md). Komplexität und umkehrbare Verschlüsselung nur aus secedit. lockoutThreshold 0 = sperrt nie.
/// </summary>
public sealed record AccountPolicyCheck(AccountPolicyField Field, CheckOperator Op, JsonElement? Expected) : CheckSpec(CheckTypes.AccountPolicy);

[JsonConverter(typeof(CamelCaseEnumConverter<AccountPolicyField>))]
public enum AccountPolicyField
{
    MinPwLen, MaxPwAgeDays, MinPwAgeDays, PwHistLen, LockoutThreshold, LockoutDurationMin, LockoutWindowMin,
    PasswordComplexity, ClearTextPassword,
}

/// <summary>Erweiterte Überwachungsrichtlinie nach GUID (<c>api:audit-policy</c>, Zahlen 0–3, nie lokalisierter Text).</summary>
public sealed record AuditSubcategoryCheck(string Guid, AuditExpectation Expected) : CheckSpec(CheckTypes.AuditSubcategory);

[JsonConverter(typeof(CamelCaseEnumConverter<AuditExpectation>))]
public enum AuditExpectation { Success, Failure, SuccessAndFailure, IncludesSuccess, IncludesFailure, NoAuditing }

/// <summary>
/// Benutzerrecht (<c>api:lsa-rights</c>, dann <c>parsed:secedit</c> [Privilege Rights]). Ein nicht aufgelöster Name
/// (ohne „*“) ergibt bei mustNotInclude/onlyFrom/empty notCheckable.
/// </summary>
/// <param name="Sids">Bekannte SIDs, oder <c>*-&lt;RID&gt;</c> für lokale RIDs (z. B. *-501 = Gast).</param>
public sealed record UserRightCheck(string Right, UserRightMode Mode, IReadOnlyList<string> Sids) : CheckSpec(CheckTypes.UserRight)
{
    public IReadOnlyList<string> Sids { get; init; } = Sids ?? [];
}

[JsonConverter(typeof(CamelCaseEnumConverter<UserRightMode>))]
public enum UserRightMode { MustInclude, MustNotInclude, OnlyFrom, Empty }

/// <summary>Dienst (Win32_Service, Name ohne Groß/klein; sonst Start-Wert in der Registry, nur Starttyp).</summary>
/// <param name="StartMode">Erlaubte Starttypen: Boot, System, Auto, Manual, Disabled.</param>
/// <param name="State">Erlaubte Zustände (Running, Stopped …); Bordmittel-Pakete kennen keinen → notCheckable.</param>
public sealed record ServiceCheck(string Name, IReadOnlyList<string>? StartMode, IReadOnlyList<string>? State,
    AbsentOutcome IfAbsent) : CheckSpec(CheckTypes.Service);

/// <summary>Optionales Windows-Feature (Win32_OptionalFeature).</summary>
/// <param name="InstallState">Erlaubte Zustände: 1 aktiviert, 2 deaktiviert, 3 nicht vorhanden.</param>
public sealed record FeatureCheck(string Name, IReadOnlyList<int> InstallState, AbsentOutcome IfAbsent) : CheckSpec(CheckTypes.Feature)
{
    public IReadOnlyList<int> InstallState { get; init; } = InstallState ?? [];
}

/// <summary>Ergebnis, wenn Dienst oder Feature fehlen.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<AbsentOutcome>))]
public enum AbsentOutcome { Fail, Pass, NotApplicable }

/// <summary>Eigenschaft eines WMI-Eintrags (Zeilen oder Methoden-<c>out</c>).</summary>
/// <param name="Item">Vollständiger Schlüssel, z. B. <c>wmi:root\Microsoft\Windows\SMB:MSFT_SmbServerConfiguration.GetConfiguration</c>.</param>
/// <param name="Rows">Welche Zeilen zählen: any, all, first.</param>
public sealed record WmiPropertyCheck(string Item, string Property, RowFilter? RowFilter, RowsMode Rows, CheckOperator Op,
    JsonElement? Expected, NoRowsOutcome IfNoRows) : CheckSpec(CheckTypes.WmiProperty);

/// <summary>Zeilenauswahl für <see cref="WmiPropertyCheck"/>.</summary>
public sealed record RowFilter(string Property, CheckOperator Op, JsonElement Value);

[JsonConverter(typeof(CamelCaseEnumConverter<RowsMode>))]
public enum RowsMode { Any, All, First }

[JsonConverter(typeof(CamelCaseEnumConverter<NoRowsOutcome>))]
public enum NoRowsOutcome { NotCheckable, Fail, NotApplicable, Pass }

/// <summary>Defender-Einstellung (MSFT_MpComputerStatus bzw. MSFT_MpPreference) mit eingebauter Sperre nach defender.mode (spec 2.5).</summary>
public sealed record DefenderSettingCheck(DefenderSource Source, string Property, CheckOperator Op, JsonElement? Expected)
    : CheckSpec(CheckTypes.DefenderSetting);

[JsonConverter(typeof(CamelCaseEnumConverter<DefenderSource>))]
public enum DefenderSource { Status, Preference }

/// <summary>ASR-Regel nach GUID. Bestanden nur bei defender.mode = active und Echtzeitschutz an (spec 2.3).</summary>
/// <param name="Accept">Zulässige Modi: block (Standard), blockOrWarn, blockOrAudit.</param>
public sealed record AsrRuleCheck(string Guid, AsrAccept Accept = AsrAccept.Block) : CheckSpec(CheckTypes.AsrRule);

[JsonConverter(typeof(CamelCaseEnumConverter<AsrAccept>))]
public enum AsrAccept { Block, BlockOrWarn, BlockOrAudit }

/// <summary>Firewall-Profil: an/Standard eingehend aus der Laufzeit (COM), Protokoll und Zusammenführung aus der Registry.</summary>
public sealed record FirewallProfileCheck(FirewallProfileName Profile, FirewallProfileProperty Property, bool Expected)
    : CheckSpec(CheckTypes.FirewallProfile);

[JsonConverter(typeof(CamelCaseEnumConverter<FirewallProfileName>))]
public enum FirewallProfileName { Domain, Private, Public }

[JsonConverter(typeof(CamelCaseEnumConverter<FirewallProfileProperty>))]
public enum FirewallProfileProperty { Enabled, DefaultInboundBlock, LogDropped, AllowLocalRules }

/// <summary>
/// Eingehende Freigaben für Ports von beliebigen Adressen im wirksamen Regelsatz (COM + Registry-Speicher). Ohne
/// erfasste Regelliste (<c>--minimal</c>) notCheckable.
/// </summary>
public sealed record FirewallExposureCheck(IReadOnlyList<PortSpec> Ports, ProfileScope Profiles) : CheckSpec(CheckTypes.FirewallExposure)
{
    public IReadOnlyList<PortSpec> Ports { get; init; } = Ports ?? [];
}

public sealed record PortSpec(NetProtocol Protocol, int Port);

[JsonConverter(typeof(CamelCaseEnumConverter<NetProtocol>))]
public enum NetProtocol { Tcp, Udp }

[JsonConverter(typeof(CamelCaseEnumConverter<ProfileScope>))]
public enum ProfileScope { Active, All }

/// <summary>Unterstütztes Betriebssystem (Lebenszyklus-Tabelle, ESU des Anlagenrechners; Zeitbasis EvaluationDate).</summary>
public sealed record LifecycleCheck() : CheckSpec(CheckTypes.Lifecycle);

/// <summary>Tage seit dem letzten kumulativen Update (Build-Tabelle, dann QFE, dann Update-Verlauf; Zeitbasis CollectedAtUtc).</summary>
/// <param name="MaxDays">Ohne Angabe die Anlageneinstellung updateMaxDays (35).</param>
public sealed record UpdateAgeCheck(int? MaxDays) : CheckSpec(CheckTypes.UpdateAge);

/// <summary>LSA-Schutz: Laufzeit (<c>--deep</c>), dann Wininit-Ereignis 12, dann RunAsPPL (allein nur ConfiguredOnly).</summary>
public sealed record LsaProtectionCheck() : CheckSpec(CheckTypes.LsaProtection);

/// <summary>Virtualisierungsbasierte Sicherheit nach laufenden Diensten (Win32_DeviceGuard).</summary>
public sealed record DeviceGuardCheck(DeviceGuardService Service, bool RequireRunning = true) : CheckSpec(CheckTypes.DeviceGuard);

[JsonConverter(typeof(CamelCaseEnumConverter<DeviceGuardService>))]
public enum DeviceGuardService { CredentialGuard, Hvci, Vbs }

/// <summary>Ein Virenschutz ist aktiv und aktuell; die SecurityCenter2-Heuristik allein ergibt nie pass.</summary>
public sealed record AvStatusCheck(int? MaxSignatureAgeDays) : CheckSpec(CheckTypes.AvStatus);

/// <summary>Ein lokales Konto nach RID (500 Administrator, 501 Gast …).</summary>
public sealed record LocalAccountCheck(int Rid, LocalAccountExpectation Expect) : CheckSpec(CheckTypes.LocalAccount);

[JsonConverter(typeof(CamelCaseEnumConverter<LocalAccountExpectation>))]
public enum LocalAccountExpectation { Disabled }

/// <summary>Kein (aktives) lokales Konto erfüllt die Bedingung; Treffer werden aufgelistet.</summary>
public sealed record LocalAccountsCheck(LocalAccountCondition Condition, IReadOnlyList<int>? ExcludeRids, bool EnabledOnly = true)
    : CheckSpec(CheckTypes.LocalAccounts);

[JsonConverter(typeof(CamelCaseEnumConverter<LocalAccountCondition>))]
public enum LocalAccountCondition { PasswordNeverExpires, UnusedDays, PasswordNotRequired }

/// <summary>Mitglieder einer lokalen Gruppe nach SID (<c>api:localgroup:&lt;SID&gt;</c>).</summary>
public sealed record LocalGroupCheck(string Sid, int? MaxMembers, IReadOnlyList<string>? AllowedSids) : CheckSpec(CheckTypes.LocalGroup);

/// <summary>Bestandsliste (Status info) mit einer festen Darstellung.</summary>
public sealed record InventoryCheck(InventoryRenderer Renderer, IReadOnlyList<string> Items) : CheckSpec(CheckTypes.Inventory)
{
    public IReadOnlyList<string> Items { get; init; } = Items ?? [];
}

[JsonConverter(typeof(CamelCaseEnumConverter<InventoryRenderer>))]
public enum InventoryRenderer
{
    LocalAdmins, RemoteAccessTools, RdpState, DefenderExclusions, Shares, AdminShares, NetworkFacts, AppControlState, Wireless,
}

/// <summary>Beleg-Vorrang: der erste Teil mit pass oder fail entscheidet (Laufzeit vor Konfiguration vor Standard).</summary>
public sealed record FirstOfCheck(IReadOnlyList<CheckSpec> Checks) : CheckSpec(CheckTypes.FirstOf)
{
    public IReadOnlyList<CheckSpec> Checks { get; init; } = Checks ?? [];
}

/// <summary>Dreiwertiges UND (spec 2.4 Punkt 5).</summary>
public sealed record AllOfCheck(IReadOnlyList<CheckSpec> Checks) : CheckSpec(CheckTypes.AllOf)
{
    public IReadOnlyList<CheckSpec> Checks { get; init; } = Checks ?? [];
}

/// <summary>Dreiwertiges ODER. Nie, um Laufzeit mit Konfiguration oder Standard zu verbinden (dafür firstOf).</summary>
public sealed record AnyOfCheck(IReadOnlyList<CheckSpec> Checks) : CheckSpec(CheckTypes.AnyOf)
{
    public IReadOnlyList<CheckSpec> Checks { get; init; } = Checks ?? [];
}

/// <summary>Keiner der Teile darf bestehen.</summary>
public sealed record NoneOfCheck(IReadOnlyList<CheckSpec> Checks) : CheckSpec(CheckTypes.NoneOf)
{
    public IReadOnlyList<CheckSpec> Checks { get; init; } = Checks ?? [];
}

/// <summary>Vertauscht pass und fail, alles andere bleibt.</summary>
public sealed record NotCheck(CheckSpec Check) : CheckSpec(CheckTypes.Not);

/// <summary>Vergleichsoperatoren, je Werttyp definiert (spec 2.4 Punkt 4).</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<CheckOperator>))]
public enum CheckOperator
{
    Eq, Ne, Lt, Le, Gt, Ge, In, NotIn, BitsSet, BitsClear, ContainsAll, ContainsNone, Exists, NotExists, AgeDaysLe, AgeDaysGt,
}

/// <summary>Registry-Werttypen wie in der Snapshot-Kodierung.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<RegValueType>))]
public enum RegValueType
{
    Dword,
    Qword,
    Sz,
    [JsonStringEnumMemberName("expand_sz")] ExpandSz,
    [JsonStringEnumMemberName("multi_sz")] MultiSz,
    Binary,
}

/// <summary>Was ein nachweislich nicht gesetzter Wert bedeutet (spec 2.4 Punkt 2).</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<WhenMissing>))]
public enum WhenMissing { NotCheckable, Fail, OsDefault, Pass }

[JsonConverter(typeof(CamelCaseEnumConverter<MultiSzMode>))]
public enum MultiSzMode { Set, List }

/// <summary>
/// Belegter Standardwert; der erste Eintrag, dessen Angaben alle zum Rechner passen, liefert den Wert. Ein so
/// begründetes pass trägt das Flag FromDefault („Standardwert angenommen“).
/// </summary>
/// <param name="Editions">EditionID-Werte (Professional, Enterprise …).</param>
/// <param name="InstallationTypes">Client, Server, Server Core.</param>
/// <param name="Source">URL der Quelle.</param>
public sealed record OsDefault(int? MinBuild, int? MaxBuild, IReadOnlyList<string>? Editions,
    IReadOnlyList<string>? InstallationTypes, bool? DomainJoined, JsonElement Value, string Source, DateOnly Seen);

/// <summary>Beleg, dass ein fehlender Wert sicher ist (whenMissing pass).</summary>
public sealed record MissingPass(string Source, DateOnly Seen, string Note);
