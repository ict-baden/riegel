// SPDX-License-Identifier: MIT
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Riegel.Core.Resources;

namespace Riegel.Core.Diagnostics;

/// <summary>
/// Diagnosepaket für den Support (Richtlinie 10, spec 7.11; Muster aus Zutritt): Version, Systemangaben und das
/// Protokoll der letzten Tage in einer ZIP-Datei. Standardmäßig ohne Namen und Adressen (<see cref="Anonymizer"/>).
/// Nie enthalten: Exporte (Snapshots), Register, Läufe, Berichte oder Einstellungswerte – sie beschreiben die
/// Sicherheitskonfiguration des Kunden.
/// </summary>
public static class DiagnosticsPackage
{
    /// <summary>Vorschlag für den Dateinamen: Riegel-Diagnose-JJJJ-MM-TT.zip.</summary>
    public static string SuggestedFileName(DateTime now) =>
        $"Riegel-Diagnose-{now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.zip";

    /// <summary>Paket schreiben (ersetzt eine vorhandene Datei).</summary>
    /// <param name="info">Angaben der App (Version, Build, Sammler-Version …) in dieser Reihenfolge; keine Inhalte von Anlagen.</param>
    /// <param name="anonymize">Rechner-, Konto-, Domänen- und Servernamen, Pfade, SIDs und Adressen ersetzen.</param>
    /// <param name="knownHosts">Weitere Hostnamen, die ersetzt werden sollen (z. B. die Rechner der geöffneten Anlage).</param>
    public static void Write(string zipPath, IReadOnlyList<KeyValuePair<string, string>> info, bool anonymize,
        IEnumerable<string>? knownHosts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentNullException.ThrowIfNull(info);
        var anonymizer = anonymize ? new Anonymizer(knownHosts ?? []) : null;
        string Clean(string text) => anonymizer?.Apply(text) ?? text;

        var temp = zipPath + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var text = new StringBuilder();
                text.AppendLine(CommonStrings.Diag_Title);
                text.AppendLine(string.Format(CultureInfo.CurrentCulture, CommonStrings.Diag_Created,
                    DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)));
                text.AppendLine(anonymize ? CommonStrings.Diag_AnonymizedYes : CommonStrings.Diag_AnonymizedNo);
                text.AppendLine();
                foreach (var (key, value) in info) text.AppendLine(CultureInfo.InvariantCulture, $"{key}: {Clean(value)}");
                text.AppendLine(string.Format(CultureInfo.CurrentCulture, CommonStrings.Diag_OperatingSystem,
                    RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture));
                text.AppendLine(string.Format(CultureInfo.CurrentCulture, CommonStrings.Diag_Runtime, RuntimeInformation.FrameworkDescription));
                text.AppendLine(string.Format(CultureInfo.CurrentCulture, CommonStrings.Diag_Language,
                    CultureInfo.CurrentUICulture.Name, TimeZoneInfo.Local.Id));
                var logs = Log.Files();
                text.AppendLine(string.Format(CultureInfo.CurrentCulture, CommonStrings.Diag_Logs, logs.Count, Log.KeepDays));
                Add(zip, "info.txt", text.ToString());

                foreach (var file in logs)
                {
                    string content;
                    try
                    {
                        using var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                        content = reader.ReadToEnd();
                    }
                    catch (IOException e)
                    {
                        content = $"({e.GetType().Name})";
                    }
                    Add(zip, "logs/" + Path.GetFileName(file), Clean(content));
                }
            }
            File.Move(temp, zipPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}

/// <summary>
/// Ersetzt in Texten, was auf Personen, Rechner oder das Netz des Kunden schließen lässt (Zutritt, erweitert für Riegel):
/// Rechner-, Benutzer- und Domänenname dieses Rechners, bekannte Hostnamen (z. B. der Anlage), UNC-Server und -Pfade
/// (<c>\\server\…</c>, <c>\\?\UNC\…</c>, <c>//server/…</c>), lokale Pfade, Konto-SIDs (Domäne/Rechner S-1-5-21-…,
/// Entra ID S-1-12-1-…), „DOMÄNE\name“, Text in „…“, IP- und E-Mail-Adressen. Gleiche Namen werden gleich nummeriert,
/// damit das Protokoll lesbar bleibt. Bekannte SIDs wie S-1-5-32-544 bleiben stehen.
/// </summary>
public sealed partial class Anonymizer
{
    private readonly Dictionary<string, string> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly (string Value, string Replacement)[] _local;

    public Anonymizer() : this([]) { }

    public Anonymizer(IEnumerable<string> knownHosts)
        : this(Environment.MachineName, Environment.UserName, Environment.UserDomainName, knownHosts) { }

    internal Anonymizer(string machine, string user, string domain, IEnumerable<string>? knownHosts = null)
    {
        var local = new List<(string, string)>();
        foreach (var (value, replacement) in new[] { (machine, "<HOST>"), (user, "<USER>"), (domain, "<DOMAIN>") }
                     .Where(p => p.Item1.Length >= 2))
            local.Add((value, replacement));
        foreach (var host in (knownHosts ?? []).Where(h => h.Length >= 2).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (local.Any(l => l.Item1.Equals(host, StringComparison.OrdinalIgnoreCase))) continue;
            local.Add((host, Host(host)));
        }
        // Längste zuerst, damit „HMI-L3-01“ nicht als „HMI“ + Rest ersetzt wird
        _local = [.. local.OrderByDescending(p => p.Item1.Length)];
    }

    public string Apply(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = Quoted().Replace(text, "„…“");
        text = Email().Replace(text, "<E-MAIL>");
        text = Unc().Replace(text, m => $@"\\{Server(m.Groups["server"].Value)}{(m.Groups["rest"].Length > 0 ? @"\…" : "")}");
        text = UncSlash().Replace(text, m => $"//{Server(m.Groups["server"].Value)}{(m.Groups["rest"].Length > 0 ? "/…" : "")}");
        text = DrivePath().Replace(text, m => m.Groups["drive"].Value + @":\…");
        text = AccountSid().Replace(text, "S-1-5-21-…");
        text = EntraSid().Replace(text, "S-1-12-1-…");
        text = Account().Replace(text, "<ACCOUNT>");
        text = Ipv4().Replace(text, "<IP>");
        text = Ipv6().Replace(text, "<IP>");
        foreach (var (value, replacement) in _local)
            text = Regex.Replace(text, $@"(?<![\w-]){Regex.Escape(value)}(?![\w-])", replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return text;
    }

    private string Server(string name)
    {
        if (!_servers.TryGetValue(name, out var replacement))
            _servers[name] = replacement = "SERVER-" + (_servers.Count + 1).ToString(CultureInfo.InvariantCulture);
        return replacement;
    }

    private string Host(string name)
    {
        if (!_hosts.TryGetValue(name, out var replacement))
            _hosts[name] = replacement = "HOST-" + (_hosts.Count + 1).ToString(CultureInfo.InvariantCulture);
        return replacement;
    }

    [GeneratedRegex(@"„[^“\r\n]*“")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+")]
    private static partial Regex Email();

    // \\server[\rest…] und \\?\UNC\server\…
    [GeneratedRegex(@"\\\\(\?\\UNC\\)?(?<server>[^\\\s„“""'|<>:*?]+)(?<rest>\\[^\s„“""'|<>]*)?")]
    private static partial Regex Unc();

    // //server/share… (URLs mit „://“ bleiben stehen)
    [GeneratedRegex(@"(?<![:\w/])//(?<server>[^/\s„“""'|<>:*?]+)(?<rest>/[^\s„“""'|<>]*)?")]
    private static partial Regex UncSlash();

    [GeneratedRegex(@"(?<![\w\\])(?<drive>[A-Za-z]):\\[^\s„“""'|<>]*")]
    private static partial Regex DrivePath();

    [GeneratedRegex(@"S-1-5-21(-\d+){3,4}", RegexOptions.IgnoreCase)]
    private static partial Regex AccountSid();

    [GeneratedRegex(@"S-1-12-1(-\d+){4}", RegexOptions.IgnoreCase)]
    private static partial Regex EntraSid();

    // DOMÄNE\name (nach den Pfaden, die dann schon ersetzt sind)
    [GeneratedRegex(@"(?<![\w\\.-])[\p{L}\d][\p{L}\d.-]{0,30}\\[\p{L}\d._$-]+")]
    private static partial Regex Account();

    [GeneratedRegex(@"\b(25[0-5]|2[0-4]\d|1?\d?\d)(\.(25[0-5]|2[0-4]\d|1?\d?\d)){3}\b")]
    private static partial Regex Ipv4();

    // Mindestens vier Gruppen oder „::“ – Uhrzeiten wie 11:15:22 sind keine Adressen
    [GeneratedRegex(@"\b([0-9A-Fa-f]{1,4}:){4,7}[0-9A-Fa-f]{1,4}\b|\b([0-9A-Fa-f]{1,4}:)+:([0-9A-Fa-f]{1,4}:?)*[0-9A-Fa-f]{1,4}\b")]
    private static partial Regex Ipv6();
}
