// SPDX-License-Identifier: MIT
namespace Riegel.Core.Snapshots;

/// <summary>
/// Die Einträge des Erfassungsplans <c>contracts/collection-plan.json</c> (v1), die ein Bordmittel-Paket nicht liefern
/// kann. Der Plan ist nicht in Riegel.Core eingebettet; ein Test (<c>PlanItemsTests</c>) vergleicht diese Liste mit
/// der Datei, damit sie nicht veraltet.
/// </summary>
internal static class PlanItems
{
    /// <summary>WMI-, API-, Ereignis- und Rohdaten-Einträge des Plans.</summary>
    internal static readonly string[] NonRegistry =
    [
        @"wmi:root\cimv2:Win32_ComputerSystem",
        @"wmi:root\cimv2:Win32_OperatingSystem",
        @"wmi:root\cimv2:Win32_Service",
        @"wmi:root\cimv2:Win32_OptionalFeature",
        @"wmi:root\cimv2:Win32_QuickFixEngineering",
        @"wmi:root\cimv2:Win32_UserAccount",
        @"wmi:root\cimv2:Win32_NetworkAdapterConfiguration",
        @"wmi:root\cimv2:Win32_NetworkAdapter",
        @"wmi:root\wmi:MSNdis_PhysicalMediumType",
        @"wmi:root\cimv2:Win32_Share",
        @"wmi:root\Microsoft\Windows\Defender:MSFT_MpComputerStatus",
        @"wmi:root\Microsoft\Windows\Defender:MSFT_MpPreference",
        @"wmi:root\SecurityCenter2:AntivirusProduct",
        @"wmi:root\Microsoft\Windows\DeviceGuard:Win32_DeviceGuard",
        @"wmi:root\cimv2\Security\MicrosoftVolumeEncryption:Win32_EncryptableVolume",
        @"wmi:root\cimv2\Security\MicrosoftTpm:Win32_Tpm",
        @"wmi:root\Microsoft\Windows\SMB:MSFT_SmbServerConfiguration.GetConfiguration",
        @"wmi:root\Microsoft\Windows\SMB:MSFT_SmbClientConfiguration.GetConfiguration",
        "api:netusermodals",
        "api:netuserenum",
        "api:lsa-rights",
        "api:localgroup:S-1-5-32-544",
        "api:localgroup:S-1-5-32-555",
        "api:audit-policy",
        "api:lsass-protection",
        "api:firewall",
        "api:windows-update-history",
        "api:winhttp-proxy",
        "api:wsc-av",
        "evt:system:wininit-12",
        "raw:secedit-inf",
        "raw:registry-pol",
        "raw:gpresult-xml",
    ];

    /// <summary>Registry-Bereiche und einzelne Schlüssel des Plans.</summary>
    internal static readonly string[] Registry =
    [
        @"HKLM\SOFTWARE\Policies",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies",
        @"HKLM\SOFTWARE\Microsoft\PolicyManager\current\device",
        @"HKLM\SYSTEM\CurrentControlSet\Control\Lsa",
        @"HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders",
        @"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server",
        @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard",
        @"HKLM\SYSTEM\CurrentControlSet\Services\NetBT\Parameters",
        @"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy",
        @"HKLM\SOFTWARE\Microsoft\Windows Defender",
        @"HKLM\SOFTWARE\Microsoft\Policies\LAPS",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\LAPS",
        @"HKLM\SYSTEM\CurrentControlSet\Control\CI",
        @"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters",
        @"HKLM\SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters",
        @"HKLM\SYSTEM\CurrentControlSet\Services\mrxsmb10",
        @"HKLM\SYSTEM\CurrentControlSet\Services\USBSTOR",
        @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
        @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters",
        @"HKLM\SYSTEM\CurrentControlSet\Services\EventLog\Security",
        @"HKLM\SYSTEM\CurrentControlSet\Services\EventLog\System",
        @"HKLM\SYSTEM\CurrentControlSet\Services\EventLog\Application",
        @"HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State",
        @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager",
        @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
        @"HKLM\SYSTEM\CurrentControlSet\Control\ProductOptions",
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon",
        @"HKLM\SOFTWARE\Microsoft\.NETFramework\v4.0.30319",
        @"HKLM\SOFTWARE\Microsoft\.NETFramework\v2.0.50727",
        @"HKLM\SOFTWARE\WOW6432Node\Microsoft\.NETFramework\v4.0.30319",
        @"HKLM\SOFTWARE\WOW6432Node\Microsoft\.NETFramework\v2.0.50727",
        @"HKLM\SOFTWARE\Microsoft\Cryptography",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKLM\SOFTWARE\Microsoft\Office\ClickToRun\Configuration",
        @"HKLM\SOFTWARE\Microsoft\Office\16.0\Common\InstallRoot",
        @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Office\16.0\Common\InstallRoot",
        @"HKLM\SOFTWARE\Microsoft\Office\15.0\Common\InstallRoot",
        @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Office\15.0\Common\InstallRoot",
        @"HKLM\SOFTWARE\Microsoft\Office\14.0\Common\InstallRoot",
        @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Office\14.0\Common\InstallRoot",
    ];
}
