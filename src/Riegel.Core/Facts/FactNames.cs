// SPDX-License-Identifier: MIT
namespace Riegel.Core.Facts;

/// <summary>Werttyp eines Fakts (für die Prüfung von appliesTo.when beim Laden).</summary>
internal enum FactKind { Text, Number, Bool }

/// <summary>
/// Die Fakten aus spec 2.5. Jeder Fakt ist ein Wert oder JSON null (unbekannt). <c>capability.&lt;name&gt;</c> gibt es
/// für jede Fähigkeit der Tabelle in rules/capabilities/.
/// </summary>
internal static class FactNames
{
    public const string OsKind = "os.kind";
    public const string OsBuild = "os.build";
    public const string OsUbr = "os.ubr";
    public const string OsEdition = "os.edition";
    public const string OsInstallationType = "os.installationType";
    public const string OsServerCore = "os.serverCore";
    public const string OsArch = "os.arch";
    public const string DomainJoined = "domain.joined";
    public const string DomainIsDc = "domain.isDc";
    public const string MachineMobile = "machine.mobile";
    public const string DefenderMode = "defender.mode";
    public const string DefenderRtp = "defender.rtp";
    public const string AvThirdPartyActive = "av.thirdPartyActive";
    public const string OfficeInstalled = "office.installed";
    public const string RdpEnabled = "rdp.enabled";
    public const string NetGatewayConfigured = "net.gatewayConfigured";
    public const string NetActiveAdapters = "net.activeAdapters";
    public const string NetWireless = "net.wireless";
    public const string NetIpForwarding = "net.ipForwarding";
    public const string NetProxyConfigured = "net.proxyConfigured";

    /// <summary>Präfix der Fähigkeiten, z. B. <c>capability.asr</c>.</summary>
    public const string CapabilityPrefix = "capability.";

    /// <summary>Werte von defender.mode (spec 2.5).</summary>
    public static class Defender
    {
        public const string Active = "active";
        public const string Passive = "passive";
        public const string EdrBlock = "edrBlock";
        public const string Off = "off";
        public const string Absent = "absent";
        public const string Unknown = "unknown";

        public static IReadOnlyList<string> All { get; } = [Active, Passive, EdrBlock, Off, Absent, Unknown];
    }

    /// <summary>Feste Fakten mit ihrem Werttyp und, wo festgelegt, den erlaubten Werten.</summary>
    public static IReadOnlyDictionary<string, (FactKind Kind, IReadOnlyList<string>? Values)> Fixed { get; } =
        new Dictionary<string, (FactKind, IReadOnlyList<string>?)>(StringComparer.Ordinal)
        {
            [OsKind] = (FactKind.Text, ["client", "server"]),
            [OsBuild] = (FactKind.Number, null),
            [OsUbr] = (FactKind.Number, null),
            [OsEdition] = (FactKind.Text, null),
            [OsInstallationType] = (FactKind.Text, null),
            [OsServerCore] = (FactKind.Bool, null),
            [OsArch] = (FactKind.Text, null),
            [DomainJoined] = (FactKind.Bool, null),
            [DomainIsDc] = (FactKind.Bool, null),
            [MachineMobile] = (FactKind.Bool, null),
            [DefenderMode] = (FactKind.Text, Defender.All),
            [DefenderRtp] = (FactKind.Bool, null),
            [AvThirdPartyActive] = (FactKind.Bool, null),
            [OfficeInstalled] = (FactKind.Bool, null),
            [RdpEnabled] = (FactKind.Bool, null),
            [NetGatewayConfigured] = (FactKind.Bool, null),
            [NetActiveAdapters] = (FactKind.Number, null),
            [NetWireless] = (FactKind.Bool, null),
            [NetIpForwarding] = (FactKind.Bool, null),
            [NetProxyConfigured] = (FactKind.Bool, null),
        };

    /// <summary>Typ eines Fakts, null wenn unbekannt (Fähigkeiten nur, wenn es sie in der Tabelle gibt).</summary>
    public static (FactKind Kind, IReadOnlyList<string>? Values)? Describe(string fact, Func<string, bool> capabilityExists)
    {
        if (Fixed.TryGetValue(fact, out var known)) return known;
        if (fact.StartsWith(CapabilityPrefix, StringComparison.Ordinal) && capabilityExists(fact[CapabilityPrefix.Length..]))
            return (FactKind.Bool, null);
        return null;
    }
}
