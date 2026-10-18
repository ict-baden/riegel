// SPDX-License-Identifier: MIT
namespace Riegel.Core.Model;

/// <summary>Kennungen der Dateiformate (Feld <c>"schema"</c>, spec 6). Die Schemas liegen unter <c>schemas/</c>.</summary>
public static class SchemaIds
{
    public const string Snapshot = "riegel.snapshot/1";
    public const string Run = "riegel.run/1";
    public const string Exceptions = "riegel.exceptions/1";
    public const string Tailoring = "riegel.tailoring/1";
    public const string Measures = "riegel.measures/1";
    public const string Plant = "riegel.plant/1";
    public const string Approved = "riegel.approved/1";
    public const string Import = "riegel.import/1";
    public const string Baseline = "riegel.baseline/1";
    public const string Evidence = "riegel.evidence/1";
    public const string Catalogue = "riegel.catalogue/1";
    public const string CollectionPlan = "riegel.collection-plan/1";

    // Vom Gerüst festgelegt (Dateien unter rules/ und templates/, docs/file-formats.md)
    public const string Rules = "riegel.rules/1";
    public const string Lifecycle = "riegel.lifecycle/1";
    public const string Builds = "riegel.builds/1";
    public const string Capabilities = "riegel.capabilities/1";
    public const string Data = "riegel.data/1";
    public const string ExceptionsTemplate = "riegel.exceptions-template/1";
    public const string TailoringTemplate = "riegel.tailoring-template/1";
}

/// <summary>
/// Schlüssel der Snapshot-Einträge (spec 7.3, contracts/collection-plan.json). Regeln, Sammler und Tests benutzen
/// dieselben Texte; Vergleiche immer ohne Groß/klein.
/// </summary>
public static class ItemKeys
{
    public const string NetUserModals = "api:netusermodals";
    public const string NetUserEnum = "api:netuserenum";
    public const string LsaRights = "api:lsa-rights";
    public const string AuditPolicy = "api:audit-policy";
    public const string LsassProtection = "api:lsass-protection";
    public const string WininitEvent12 = "evt:system:wininit-12";
    public const string Firewall = "api:firewall";
    public const string WindowsUpdateHistory = "api:windows-update-history";
    public const string WinHttpProxy = "api:winhttp-proxy";
    public const string WscAntivirus = "api:wsc-av";
    public const string RawSeceditInf = "raw:secedit-inf";
    public const string RawRegistryPol = "raw:registry-pol";
    public const string RawGpresultXml = "raw:gpresult-xml";
    public const string ParsedSecedit = "parsed:secedit";
    public const string ParsedRegistryPol = "parsed:registry-pol";
    public const string ParsedGpresult = "parsed:gpresult";
    public const string ParsedFirewallRulesRegistry = "parsed:firewall-rules-registry";

    /// <summary>Lokale Administratoren (S-1-5-32-544).</summary>
    public const string LocalAdministrators = "api:localgroup:S-1-5-32-544";

    /// <summary>Remotedesktopbenutzer (S-1-5-32-555).</summary>
    public const string LocalRemoteDesktopUsers = "api:localgroup:S-1-5-32-555";

    /// <summary><c>reg:HKLM\&lt;path&gt;</c>; <paramref name="path"/> mit oder ohne „HKLM\“.</summary>
    public static string Registry(string path) =>
        "reg:" + (path.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase) ? path : @"HKLM\" + path).TrimEnd('\\');

    /// <summary><c>wmi:&lt;ns&gt;:&lt;Class&gt;</c></summary>
    public static string Wmi(string ns, string className) => $"wmi:{ns}:{className}";

    /// <summary><c>wmi:&lt;ns&gt;:&lt;Class&gt;.&lt;Method&gt;</c> (statische Methode)</summary>
    public static string WmiMethod(string ns, string className, string method) => $"wmi:{ns}:{className}.{method}";

    /// <summary><c>api:localgroup:&lt;SID&gt;</c></summary>
    public static string LocalGroup(string sid) => "api:localgroup:" + sid;

    /// <summary>Gleicher Schlüssel (ohne Groß/klein)?</summary>
    public static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
