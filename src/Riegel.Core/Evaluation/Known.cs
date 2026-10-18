// SPDX-License-Identifier: MIT
using Riegel.Core.Model;

namespace Riegel.Core.Evaluation;

/// <summary>
/// Einträge und Registry-Pfade, die die Engine selbst kennt (Prüfarten mit eingebauter Belegreihenfolge, Fakten).
/// Alle stehen im Erfassungsplan contracts/collection-plan.json bzw. unter seinen rekursiven Wurzeln.
/// </summary>
internal static class Known
{
    private const string Cim = @"root\cimv2";
    private const string DefenderNs = @"root\Microsoft\Windows\Defender";

    public static readonly string ComputerSystem = ItemKeys.Wmi(Cim, "Win32_ComputerSystem");
    public static readonly string OperatingSystem = ItemKeys.Wmi(Cim, "Win32_OperatingSystem");
    public static readonly string Service = ItemKeys.Wmi(Cim, "Win32_Service");
    public static readonly string OptionalFeature = ItemKeys.Wmi(Cim, "Win32_OptionalFeature");
    public static readonly string QuickFix = ItemKeys.Wmi(Cim, "Win32_QuickFixEngineering");
    public static readonly string UserAccount = ItemKeys.Wmi(Cim, "Win32_UserAccount");
    public static readonly string AdapterConfiguration = ItemKeys.Wmi(Cim, "Win32_NetworkAdapterConfiguration");
    public static readonly string Adapter = ItemKeys.Wmi(Cim, "Win32_NetworkAdapter");
    public static readonly string Share = ItemKeys.Wmi(Cim, "Win32_Share");
    public static readonly string PhysicalMedium = ItemKeys.Wmi(@"root\wmi", "MSNdis_PhysicalMediumType");
    public static readonly string MpStatus = ItemKeys.Wmi(DefenderNs, "MSFT_MpComputerStatus");
    public static readonly string MpPreference = ItemKeys.Wmi(DefenderNs, "MSFT_MpPreference");
    public static readonly string SecurityCenterAv = ItemKeys.Wmi(@"root\SecurityCenter2", "AntivirusProduct");
    public static readonly string DeviceGuard = ItemKeys.Wmi(@"root\Microsoft\Windows\DeviceGuard", "Win32_DeviceGuard");

    // Registry (Pfade ohne „reg:“)
    public const string CurrentVersion = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    public const string Winlogon = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
    public const string ProductOptions = @"HKLM\SYSTEM\CurrentControlSet\Control\ProductOptions";
    public const string Lsa = @"HKLM\SYSTEM\CurrentControlSet\Control\Lsa";
    public const string TerminalServer = @"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server";
    public const string RdpTcp = @"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp";
    public const string TerminalServicesPolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";
    public const string TcpipParameters = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";
    public const string ServicesRoot = @"HKLM\SYSTEM\CurrentControlSet\Services";
    public const string LanmanServer = @"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters";
    public const string FirewallPolicyLocal = @"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
    public const string FirewallPolicyGpo = @"HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall";
    public const string AsrPolicyRules = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\ASR\Rules";
    public const string MdmDefender = @"HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Defender";
    public const string DefenderExclusionsLocal = @"HKLM\SOFTWARE\Microsoft\Windows Defender\Exclusions";
    public const string DefenderExclusionsPolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Exclusions";
    public const string CodeIntegrityConfig = @"HKLM\SYSTEM\CurrentControlSet\Control\CI\Config";
    public const string CodeIntegrityPolicy = @"HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy";
    public const string AppLockerPolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\SrpV2";
    public const string UninstallNative = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    public const string UninstallWow = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Profilschlüssel der Firewall: Richtlinie (GPO-Speicher) und lokaler Speicher (StandardProfile = privat).</summary>
    /// <remarks>
    /// Belegt durch DISA STIG „Windows Defender Firewall with Advanced Security“ (V-241995, V-242000, gesehen 2026-10-18
    /// über https://www.stigviewer.com): Richtlinie <c>SOFTWARE\Policies\Microsoft\WindowsFirewall\&lt;Domain|Private|Public&gt;Profile</c>,
    /// danach lokal <c>…\FirewallPolicy\&lt;Domain|Standard|Public&gt;Profile</c>.
    /// </remarks>
    public static (string Policy, string Local) FirewallProfileKeys(FirewallProfileName profile) => profile switch
    {
        FirewallProfileName.Domain => (FirewallPolicyGpo + @"\DomainProfile", FirewallPolicyLocal + @"\DomainProfile"),
        FirewallProfileName.Private => (FirewallPolicyGpo + @"\PrivateProfile", FirewallPolicyLocal + @"\StandardProfile"),
        _ => (FirewallPolicyGpo + @"\PublicProfile", FirewallPolicyLocal + @"\PublicProfile"),
    };

    /// <summary>Bit der Firewall-Profile in api:firewall (currentProfiles, Regel-Profile): 1 Domäne, 2 privat, 4 öffentlich.</summary>
    public static int ProfileBit(FirewallProfileName profile) => profile switch
    {
        FirewallProfileName.Domain => 1,
        FirewallProfileName.Private => 2,
        _ => 4,
    };

    /// <summary>Name des Profils in api:firewall.profiles.</summary>
    public static string ProfileName(FirewallProfileName profile) => profile switch
    {
        FirewallProfileName.Domain => "domain",
        FirewallProfileName.Private => "private",
        _ => "public",
    };
}
