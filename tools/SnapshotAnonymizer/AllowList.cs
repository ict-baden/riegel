// SPDX-License-Identifier: MIT
namespace Riegel.Tools.SnapshotAnonymizer;

/// <summary>
/// Erlaubnisliste eingebauter Namen (spec 8.1): was hier steht oder einer Regel hier folgt, bleibt in Fixtures stehen;
/// alles andere wird ersetzt. Bewusst eng: lieber ein Windows-Name zu viel ersetzt als ein fremder Name im Repo.
/// </summary>
internal static class AllowList
{
    /// <summary>Eingebaute lokale Konten nach RID mit ihrem englischen Standardnamen (nur dieser Name bleibt).</summary>
    public static readonly IReadOnlyDictionary<long, string> BuiltInAccounts = new Dictionary<long, string>
    {
        [500] = "Administrator",
        [501] = "Guest",
        [503] = "DefaultAccount",
        [504] = "WDAGUtilityAccount",
    };

    /// <summary>Herausgeber von Microsoft-Software (Uninstall-Einträge), genau so geschrieben.</summary>
    public static readonly string[] MicrosoftPublishers = ["Microsoft Corporation", "Microsoft", "Microsoft Corporations"];

    /// <summary>Pfade von Windows und Microsoft-Software (Dienste, Firewall-Programme); ohne Groß/klein, nach dem Ersetzen der Variablen.</summary>
    public static readonly string[] SystemPathPrefixes =
    [
        @"C:\Windows\", @"\SystemRoot\", @"\??\C:\Windows\", @"C:\Program Files\Windows Defender", @"C:\Program Files\Windows Defender Advanced Threat Protection",
        @"C:\ProgramData\Microsoft\Windows Defender", @"C:\Program Files\Microsoft", @"C:\Program Files (x86)\Microsoft", @"C:\Program Files\WindowsApps\Microsoft",
        @"C:\Program Files\Common Files\microsoft shared", @"C:\Program Files (x86)\Common Files\microsoft shared", @"C:\WindowsAzure\",
        @"C:\Program Files\Windows Mail", @"C:\Program Files\Windows Media Player", @"C:\Program Files\Internet Explorer", @"C:\Program Files (x86)\Internet Explorer",
        @"C:\Program Files\PowerShell\", @"C:\Program Files\dotnet\", @"C:\Program Files (x86)\dotnet\",
    ];

    /// <summary>Anmeldekonten von Diensten, die zu Windows gehören.</summary>
    public static readonly string[] ServiceAccounts = ["LocalSystem", @"NT AUTHORITY\", @"NT Service\", @"NT-AUTORITÄT\"];

    /// <summary>Virenschutz von Microsoft (WSC, SecurityCenter2).</summary>
    public static readonly string[] MicrosoftAntivirus = ["Windows Defender", "Microsoft Defender Antivirus"];

    /// <summary>Wörter, an denen ein Microsoft-Name zu erkennen ist (Netzwerkadapter, Hardware, Updates, Firewall-Gruppen).</summary>
    public static readonly string[] MicrosoftWords = ["Microsoft", "Windows", "Hyper-V", "WAN Miniport", "Teredo", "Kerberos", "@"];

    /// <summary>Gruppen- und Konten-SIDs, die überall gleich sind (S-1-5-32-*, S-1-5-18 …) – bleiben unverändert.</summary>
    public static bool IsWellKnownSid(string sid) =>
        !sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) || sid.StartsWith("S-1-5-21-1000-2000-3000-", StringComparison.Ordinal);

    public static bool IsSystemPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        string p = Expand(path.Trim().Trim('"'));
        if (p.Equals("System", StringComparison.OrdinalIgnoreCase)) return true; // Firewall-Regeln des Kernels
        return SystemPathPrefixes.Any(prefix => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Firewall-Gruppe von Windows: eine Ressource einer System-DLL („@FirewallAPI.dll,-25000“), ein
    /// Paket von Microsoft („@{Microsoft.…“, „@{MicrosoftWindows.…“, „@{Windows.…“) oder ein Microsoft-Name. Regeln
    /// ohne Gruppe hat ein Mensch oder ein Installer angelegt: sie gelten als fremd.
    /// </summary>
    public static bool IsWindowsRuleGroup(string? group)
    {
        if (string.IsNullOrWhiteSpace(group)) return false;
        if (group.StartsWith("@{", StringComparison.Ordinal))
            return group.StartsWith("@{Microsoft", StringComparison.OrdinalIgnoreCase) || group.StartsWith("@{Windows", StringComparison.OrdinalIgnoreCase);
        if (group.StartsWith('@')) return true;
        return MicrosoftWords.Where(w => w != "@").Any(w => group.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsMicrosoftName(string? text) =>
        !string.IsNullOrWhiteSpace(text) && MicrosoftWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>Umgebungsvariablen von Windows-Pfaden in die Standardform (nur Text, kein Zugriff auf das System).</summary>
    public static string Expand(string path) => path
        .Replace("%SystemRoot%", @"C:\Windows", StringComparison.OrdinalIgnoreCase)
        .Replace("%windir%", @"C:\Windows", StringComparison.OrdinalIgnoreCase)
        .Replace("%ProgramFiles%", @"C:\Program Files", StringComparison.OrdinalIgnoreCase)
        .Replace("%ProgramFiles(x86)%", @"C:\Program Files (x86)", StringComparison.OrdinalIgnoreCase)
        .Replace("%ProgramData%", @"C:\ProgramData", StringComparison.OrdinalIgnoreCase)
        .Replace("%SystemDrive%", "C:", StringComparison.OrdinalIgnoreCase);
}
