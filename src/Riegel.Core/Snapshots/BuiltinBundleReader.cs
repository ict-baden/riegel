// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Parsing;

namespace Riegel.Core.Snapshots;

/// <summary>
/// Liest ein Bordmittel-Paket (spec 7.5, docs/file-formats.md „Built-in-commands bundle“) und macht daraus einen Export
/// mit Herkunft <c>builtin</c>:
/// <list type="bullet">
/// <item><c>reg\*.reg</c> → <c>reg:</c>-Einträge; Abdeckung genau für die exportierten Schlüssel (je Datei die obersten
/// Schlüssel, rekursiv ohne Tiefengrenze – <c>reg export</c> schreibt den ganzen Teilbaum).</item>
/// <item><c>services-start.txt</c> → <c>reg:…\Services\&lt;Name&gt;</c> mit nur dem Wert Start (valueFilter); Abdeckung
/// von <c>Services</c> eine Ebene tief: <c>reg query /s /v Start</c> nennt jeden Schlüssel mit einem Start-Wert.</item>
/// <item><c>winlogon.txt</c> → Winlogon mit AutoAdminLogon, DefaultUserName, DefaultDomainName und – nur als
/// „vorhanden“, geschwärzt – DefaultPassword.</item>
/// <item><c>secedit.inf</c> → <c>raw:secedit-inf</c> (daraus <c>parsed:secedit</c>), <c>auditpol.csv</c> →
/// <c>api:audit-policy</c>, <c>hostname.txt</c>/<c>ver.txt</c> → Rechnerkopf, <c>exitcodes.txt</c> → Schritte mit
/// Fehler werden nicht verwendet.</item>
/// </list>
/// Alles andere aus dem Erfassungsplan ist <c>notCollected</c>. Jede Datei hat eine eigene Größengrenze
/// (<see cref="ReadLimits.MaxTextFileBytes"/>); kaputte Kodierung oder Syntax ist ein RGL-Fehler, nie ein Absturz.
/// </summary>
internal static class BuiltinBundleReader
{
    /// <summary>reg export liest den ganzen Teilbaum; dieser Wert steht für „ohne Tiefengrenze“.</summary>
    internal const int UnlimitedDepth = 512;

    internal const string ServicesKey = @"HKLM\SYSTEM\CurrentControlSet\Services";
    internal const string WinlogonKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
    internal const string CurrentVersionKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    internal const string ProductOptionsKey = @"HKLM\SYSTEM\CurrentControlSet\Control\ProductOptions";
    internal const string FolderSuffix = ".riegel-builtin";

    /// <summary>Name und Version im Kopf (Aufbau des Pakets laut spec 7.5, Fassung 1).</summary>
    internal static readonly CollectorInfo Collector = new("collect-builtin", "1", null, "", false);

    private const int MaxRegFiles = 64;
    private static readonly string[] WinlogonValues = ["AutoAdminLogon", "DefaultUserName", "DefaultDomainName"];

    /// <summary>Wertefilter von CurrentVersion wie im Erfassungsplan (nur Versionswerte; PlanItemsTests vergleicht).</summary>
    internal static readonly string[] CurrentVersionValues =
        ["CurrentBuild", "CurrentBuildNumber", "CurrentMajorVersionNumber", "CurrentMinorVersionNumber", "CurrentVersion", "UBR", "EditionID", "InstallationType", "DisplayVersion", "ReleaseId", "ProductName", "CSDVersion"];

    internal static Snapshot Read(string folder, ReadLimits l)
    {
        ArgumentNullException.ThrowIfNull(l);
        var dir = new DirectoryInfo(folder);
        if (!dir.Exists) throw RiegelException.Common(RglCodesCommon.NotFound, folder);
        if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw RglCodesParsing.Error(RglCodesParsing.BundleInvalid, dir.Name);
        var bundle = new Bundle(dir, l);
        var exitCodes = bundle.ExitCodes();

        // ---- Registry: .reg-Dateien
        var keys = new Dictionary<string, RegKeyData>(StringComparer.OrdinalIgnoreCase);
        var coverage = new List<CoverageEntry>();
        var regDir = new DirectoryInfo(Path.Combine(dir.FullName, "reg"));
        if (regDir.Exists && !regDir.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            var files = regDir.EnumerateFiles("*.reg").Where(f => !f.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count > MaxRegFiles) throw RglCodesParsing.Error(RglCodesParsing.BundleInvalid, "reg");
            int values = 0;
            foreach (var file in files)
            {
                if (StepFailed(exitCodes, RegStep(file.Name))) continue;
                var parsed = RegFileParser.Parse(TextFiles.ReadCapped(file.FullName, l.MaxTextFileBytes), "reg/" + file.Name,
                    l.MaxItems - values, l.MaxStringChars);
                values += parsed.Keys.Sum(k => k.Values.Count);
                var paths = parsed.Keys.Select(k => k.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var key in parsed.Keys)
                {
                    Merge(keys, key);
                    if (!HasAncestorIn(key.Path, paths)) AddCoverage(coverage, new CoverageEntry(key.Path, true, UnlimitedDepth, true, [], null));
                }
            }
        }

        // ---- Dienste: reg query … /s /v Start
        if (bundle.Has("services-start.txt") && !StepFailed(exitCodes, "services") && !StepFailed(exitCodes, "services-start"))
        {
            var text = TextFiles.DecodeConsoleText(bundle.Bytes("services-start.txt"), "services-start.txt");
            var root = keys.TryGetValue(ServicesKey, out var existingRoot) ? existingRoot : null;
            if (root is null)
            {
                root = new RegKeyData(ServicesKey) { Subkeys = null, ValueFilter = [] };
                keys[ServicesKey] = root;
            }
            foreach (var queried in RegQueryParser.Parse(text, "services-start.txt", l.MaxItems))
            {
                var start = queried.Values.FirstOrDefault(v => v.Name.Equals("Start", StringComparison.OrdinalIgnoreCase));
                if (start is null || !queried.Path.StartsWith(ServicesKey + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                var value = RegQueryParser.ToRegValue(start, "services-start.txt");
                if (value is null) continue;
                if (keys.TryGetValue(queried.Path, out var existing))
                {
                    // Aus einer .reg-Datei vollständig bekannt: die hat Vorrang
                    if (existing.ValueFilter is not null && !existing.Values.Any(v => v.Key.Equals("Start", StringComparison.OrdinalIgnoreCase)))
                    {
                        existing.ValueFilter.Add("Start");
                        existing.Set("Start", value);
                    }
                    continue;
                }
                var service = new RegKeyData(queried.Path) { Subkeys = null, ValueFilter = ["Start"] };
                service.Set("Start", value);
                keys[queried.Path] = service;
            }
            AddCoverage(coverage, new CoverageEntry(ServicesKey, true, 1, true, [], null));
        }

        // ---- Winlogon (Review evidence-3): ein 32-Bit-cmd auf 64-Bit-Windows liest WOW6432Node, wo z. B. AutoAdminLogon
        // fehlt – Winlogon aus einem solchen Lauf (wow64=1) wird nicht verwendet. Ein fehlender Wert gilt nur als „nicht
        // gesetzt“, wenn seine Abfrage errorlevel 1 („Wert fehlt“) meldet und der Lauf nachweislich in der 64-Bit-Ansicht
        // lief (wow64=0); sonst ist er nicht erfasst (Wertfilter ohne ihn).
        if (bundle.Has("winlogon.txt") && !StepFailed(exitCodes, "winlogon") && !keys.ContainsKey(WinlogonKey)
            && !(exitCodes.TryGetValue("wow64", out var wow64) && wow64 != 0))
        {
            var text = TextFiles.DecodeConsoleText(bundle.Bytes("winlogon.txt"), "winlogon.txt");
            var winlogon = new RegKeyData(WinlogonKey) { Subkeys = null, ValueFilter = [] };
            var lines = TextFiles.Lines(text);
            var queryText = string.Join("\n", lines.Where(line => !line.StartsWith("DefaultPassword=", StringComparison.OrdinalIgnoreCase)));
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var queried in RegQueryParser.Parse(queryText, "winlogon.txt", 64))
            {
                if (!queried.Path.Equals(WinlogonKey, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var v in queried.Values)
                {
                    if (!WinlogonValues.Contains(v.Name, StringComparer.OrdinalIgnoreCase)) continue;
                    if (RegQueryParser.ToRegValue(v, "winlogon.txt") is { } value)
                    {
                        winlogon.Set(v.Name, value);
                        found.Add(v.Name);
                    }
                }
            }
            bool nativeView = exitCodes.TryGetValue("wow64", out var view) && view == 0;
            foreach (var name in WinlogonValues)
                if (found.Contains(name) || (nativeView && exitCodes.TryGetValue("winlogon-" + name.ToLowerInvariant(), out var rc) && rc == 1))
                    winlogon.ValueFilter!.Add(name);
            var marker = lines.Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith("DefaultPassword=", StringComparison.OrdinalIgnoreCase));
            if (marker is not null)
            {
                var state = marker["DefaultPassword=".Length..].Trim();
                if (state.Equals("present", StringComparison.OrdinalIgnoreCase))
                {
                    winlogon.ValueFilter!.Add("DefaultPassword");
                    winlogon.Set("DefaultPassword", RegValue.RedactedValue("unknown", null));
                }
                else if (state.Equals("absent", StringComparison.OrdinalIgnoreCase))
                {
                    winlogon.ValueFilter!.Add("DefaultPassword");
                }
            }
            keys[WinlogonKey] = winlogon;
            AddCoverage(coverage, new CoverageEntry(WinlogonKey, false, 0, true, [], null));
        }

        // ---- CurrentVersion: reg query ohne /s (collect-builtin.cmd). Ein reg export wäre rekursiv und enthielte
        // Winlogon\DefaultPassword im Klartext; deshalb nur die Werte genau dieses Schlüssels, Unterschlüssel unbekannt.
        if (bundle.Has("currentversion.txt") && !StepFailed(exitCodes, "currentversion") && !keys.ContainsKey(CurrentVersionKey))
        {
            var text = TextFiles.DecodeConsoleText(bundle.Bytes("currentversion.txt"), "currentversion.txt");
            var currentVersionKey = new RegKeyData(CurrentVersionKey) { Subkeys = null, ValueFilter = [.. CurrentVersionValues] };
            foreach (var queried in RegQueryParser.Parse(text, "currentversion.txt", l.MaxItems))
            {
                if (!queried.Path.Equals(CurrentVersionKey, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var v in queried.Values)
                    if (CurrentVersionValues.Contains(v.Name, StringComparer.OrdinalIgnoreCase) && RegQueryParser.ToRegValue(v, "currentversion.txt") is { } value)
                        currentVersionKey.Set(v.Name, value);
            }
            keys[CurrentVersionKey] = currentVersionKey;
            AddCoverage(coverage, new CoverageEntry(CurrentVersionKey, false, 0, true, [], null));
        }

        AddImplicitAncestors(keys, coverage);
        LinkSubkeys(keys);

        var items = new Dictionary<string, SnapshotItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys.Values)
            items[ItemKeys.Registry(key.Path)] = new SnapshotItem(ItemKeys.Registry(key.Path), ItemStatus.Ok, key.ToItemValue(l.MaxStringChars), null, null, 0);

        // ---- Sicherheitsvorlage
        bool admin = false;
        if (StepFailed(exitCodes, "secedit"))
        {
            items[ItemKeys.RawSeceditInf] = Failed(ItemKeys.RawSeceditInf, "secedit", exitCodes["secedit"]);
        }
        else if (bundle.Has("secedit.inf"))
        {
            var text = TextFiles.DecodeUtf16WithBom(bundle.Bytes("secedit.inf"), "secedit.inf");
            SeceditParser.Parse(text, "secedit.inf", l.MaxStringChars);
            items[ItemKeys.RawSeceditInf] = new SnapshotItem(ItemKeys.RawSeceditInf, ItemStatus.Ok, TextValue(text), null, null, 0);
            // secedit /export braucht erhöhte Rechte (research-technik §3: ohne Admin Exitcode 740)
            admin = true;
        }

        // ---- Überwachungsrichtlinie
        if (StepFailed(exitCodes, "auditpol"))
        {
            items[ItemKeys.AuditPolicy] = Failed(ItemKeys.AuditPolicy, "auditpol", exitCodes["auditpol"]);
        }
        else if (bundle.Has("auditpol.csv"))
        {
            var text = TextFiles.DecodeConsoleText(bundle.Bytes("auditpol.csv"), "auditpol.csv");
            var rows = AuditCsvParser.Parse(text, "auditpol.csv", l.MaxItems, l.MaxStringChars);
            items[ItemKeys.AuditPolicy] = new SnapshotItem(ItemKeys.AuditPolicy, ItemStatus.Ok, AuditCsvParser.ToItem(rows), null, null, 0);
            // auditpol /backup ohne Admin: Fehler 0x522 (research-technik §3)
            admin = true;
        }

        // ---- Rechnerkopf
        var host = bundle.Has("hostname.txt")
            ? TextFiles.Lines(TextFiles.DecodeConsoleText(bundle.Bytes("hostname.txt"), "hostname.txt")).Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0)
            : null;
        host ??= HostFromFolder(dir.Name);
        if (string.IsNullOrWhiteSpace(host) || host.Length > 255 || TextFiles.HasControlCharacters(host))
            throw RglCodesParsing.Error(RglCodesParsing.BundleInvalid, "hostname.txt");
        var version = bundle.Has("ver.txt") ? ParseVer(TextFiles.DecodeConsoleText(bundle.Bytes("ver.txt"), "ver.txt")) : null;
        keys.TryGetValue(CurrentVersionKey, out var currentVersion);
        keys.TryGetValue(ProductOptionsKey, out var productOptions);
        int? build = IntValue(currentVersion, "CurrentBuild") ?? version?.Build;
        int? ubr = IntValue(currentVersion, "UBR") ?? version?.Ubr;
        var installationType = StringValue(currentVersion, "InstallationType");
        var machine = new MachineInfo(host, null, null, ProductType(StringValue(productOptions, "ProductType"), installationType), build, ubr,
            StringValue(currentVersion, "EditionID"), installationType, StringValue(currentVersion, "DisplayVersion"), null, null, null, null);

        // ---- Alles andere: nicht erfasst
        foreach (var key in PlanItems.NonRegistry)
        {
            if (items.ContainsKey(key)) continue;
            var part = key switch
            {
                ItemKeys.AuditPolicy => "auditpol.csv",
                ItemKeys.RawSeceditInf => "secedit.inf",
                _ => key,
            };
            items[key] = new SnapshotItem(key, ItemStatus.NotCollected, null, null, RglCodesParsing.ItemError(RglCodesParsing.BundlePartMissing, part), 0);
        }
        var evidence = new RegistryEvidence(new Snapshot(SchemaIds.Snapshot, Collector, default, admin, SnapshotOrigin.Builtin,
            new CollectionOptions(false, false, false), machine, coverage, [.. items.Values]));
        foreach (var path in PlanItems.Registry)
        {
            var itemKey = ItemKeys.Registry(path);
            if (items.ContainsKey(itemKey) || evidence.Covered(path)) continue;
            items[itemKey] = new SnapshotItem(itemKey, ItemStatus.NotCollected, null, null,
                RglCodesParsing.ItemError(RglCodesParsing.BundlePartMissing, path), 0);
        }

        var snapshot = new Snapshot(SchemaIds.Snapshot, Collector, CollectedAt(dir), admin, SnapshotOrigin.Builtin,
            new CollectionOptions(false, false, false), machine, [.. coverage.OrderBy(c => c.Root, StringComparer.OrdinalIgnoreCase)],
            [.. items.Values.OrderBy(i => i.Key, StringComparer.OrdinalIgnoreCase)]);
        if (snapshot.Items.Count > l.MaxItems) throw RglCodesParsing.Error(RglCodesParsing.TooManyItems, dir.Name, l.MaxItems);
        return DerivedItems.Process(snapshot, l);
    }

    /// <summary>
    /// Prüfsumme eines Pakets für <see cref="Services.ISnapshotReader.VerifySidecar"/>: SHA-256 über die Liste
    /// „&lt;sha256&gt;␠␠&lt;relativer Pfad&gt;\n“ aller Dateien, sortiert nach Pfad (wie SHA256SUMS).
    /// </summary>
    internal static string BundleSha256(string folder)
    {
        var root = new DirectoryInfo(folder);
        var lines = new StringBuilder();
        foreach (var file in root.EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, AttributesToSkip = FileAttributes.ReparsePoint })
                     .Select(f => (Path: Path.GetRelativePath(root.FullName, f.FullName).Replace('\\', '/'), f.FullName))
                     .OrderBy(f => f.Path, StringComparer.Ordinal))
            lines.Append(Hashes.Sha256File(file.FullName)).Append("  ").Append(file.Path).Append('\n');
        return Hashes.Sha256Hex(lines.ToString());
    }

    // ---------------------------------------------------------------- Registry

    private static void Merge(Dictionary<string, RegKeyData> keys, RegKeyData key)
    {
        if (!keys.TryGetValue(key.Path, out var existing))
        {
            var copy = new RegKeyData(key.Path);
            foreach (var (name, value) in key.Values) copy.Set(name, value);
            keys[key.Path] = copy;
            return;
        }
        foreach (var (name, value) in key.Values) existing.Set(name, value);
        // Aus einer anderen Quelle nur teilweise bekannt (Filter, Unterschlüssel unbekannt) – jetzt vollständig
        existing.ValueFilter = null;
        existing.Subkeys ??= new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasAncestorIn(string path, HashSet<string> paths)
    {
        for (int cut = path.LastIndexOf('\\'); cut > 0; cut = path.LastIndexOf('\\', cut - 1))
            if (paths.Contains(path[..cut])) return true;
        return false;
    }

    private static void AddCoverage(List<CoverageEntry> coverage, CoverageEntry entry)
    {
        if (coverage.Any(c => string.Equals(c.Root, entry.Root, StringComparison.OrdinalIgnoreCase) && c.Recursive == entry.Recursive && c.MaxDepth >= entry.MaxDepth)) return;
        coverage.Add(entry);
    }

    /// <summary>
    /// Übergeordnete Schlüssel, die in einem abgedeckten Bereich liegen, aber selbst nicht im Paket stehen (eine Datei nennt
    /// einen Unterschlüssel ohne seinen Elternschlüssel, oder ein Dienst steht nur über einen exportierten Unterschlüssel
    /// darin): es gibt sie, sonst gäbe es ihre Unterschlüssel nicht. Sie entstehen als Einträge ohne erfasste Werte
    /// (valueFilter leer), damit die Abdeckung sie nicht als „nicht vorhanden“ ausgibt. Ihre Unterschlüssel sind nur
    /// bekannt, wenn sie in einem vollständig exportierten Teilbaum liegen.
    /// </summary>
    private static void AddImplicitAncestors(Dictionary<string, RegKeyData> keys, List<CoverageEntry> coverage)
    {
        var probe = new RegistryEvidence(new Snapshot(SchemaIds.Snapshot, Collector, default, false, SnapshotOrigin.Builtin,
            new CollectionOptions(false, false, false), new MachineInfo("probe", null, null, null, null, null, null, null, null, null, null, null, null),
            coverage, []));
        foreach (var path in keys.Keys.ToList())
        {
            for (int cut = path.LastIndexOf('\\'); cut > 0; cut = path.LastIndexOf('\\', cut - 1))
            {
                var ancestor = path[..cut];
                if (keys.ContainsKey(ancestor) || !probe.Covered(ancestor)) continue;
                bool subtreeKnown = coverage.Any(c => c.Recursive && c.MaxDepth == UnlimitedDepth
                    && (ancestor.Equals(c.Root, StringComparison.OrdinalIgnoreCase) || ancestor.StartsWith(c.Root + "\\", StringComparison.OrdinalIgnoreCase)));
                keys[ancestor] = new RegKeyData(ancestor)
                {
                    ValueFilter = [],
                    Subkeys = subtreeKnown ? new SortedSet<string>(StringComparer.OrdinalIgnoreCase) : null,
                };
            }
        }
    }

    /// <summary>Unterschlüssel aus den bekannten Pfaden (nur bei Schlüsseln, deren Unterschlüssel bekannt sind).</summary>
    private static void LinkSubkeys(Dictionary<string, RegKeyData> keys)
    {
        foreach (var path in keys.Keys)
        {
            int cut = path.LastIndexOf('\\');
            if (cut <= 0 || !keys.TryGetValue(path[..cut], out var parent) || parent.Subkeys is null) continue;
            parent.Subkeys.Add(path[(cut + 1)..]);
        }
    }

    // ---------------------------------------------------------------- Kopf

    private static int? IntValue(RegKeyData? key, string name)
    {
        var value = key?.Values.FirstOrDefault(v => v.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        return value?.Data switch
        {
            uint d when d <= int.MaxValue => (int)d,
            string s when int.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) => n,
            _ => null,
        };
    }

    private static string? StringValue(RegKeyData? key, string name)
    {
        var value = key?.Values.FirstOrDefault(v => v.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        return value?.Data is string s && s.Length > 0 && s.Length <= 256 ? s : null;
    }

    /// <summary>
    /// ProductType des Kopfs (1 = Client) aus <c>ProductOptions\ProductType</c> = „WinNT“ (Microsoft, „ProductType: Core
    /// Services“, https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2003/cc782360(v=ws.10),
    /// gesehen 2026-10-18: WinNT = Windows XP Professional; für neuere Versionen dort nicht belegt – unverified) oder
    /// InstallationType „Client“. ServerNT und LanmanNT nennt die Seite beide „Windows Server 2003“ ohne Unterschied
    /// zwischen Server und Domänencontroller – dann bleibt das Feld null (spec 2.3: Regeln, die davon abhängen, werden
    /// „nicht prüfbar“).
    /// </summary>
    private static int? ProductType(string? productOptions, string? installationType)
    {
        if (string.Equals(productOptions, "WinNT", StringComparison.OrdinalIgnoreCase)) return 1;
        if (productOptions is null && string.Equals(installationType, "Client", StringComparison.OrdinalIgnoreCase)) return 1;
        return null;
    }

    /// <summary>„Microsoft Windows [Version 6.1.7601]“ → Build 7601 (UBR, wenn vier Teile).</summary>
    private static (int Build, int? Ubr)? ParseVer(string text)
    {
        int open = text.IndexOf('['), close = open < 0 ? -1 : text.IndexOf(']', open);
        if (open < 0 || close < 0) return null;
        var inside = text[(open + 1)..close];
        int digit = inside.IndexOfAny("0123456789".ToCharArray());
        if (digit < 0) return null;
        var parts = inside[digit..].Trim().Split('.');
        if (parts.Length < 3 || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var build)) return null;
        int? ubr = parts.Length >= 4 && int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var u) ? u : null;
        return (build, ubr);
    }

    private static string? HostFromFolder(string name)
    {
        if (name.EndsWith(FolderSuffix, StringComparison.OrdinalIgnoreCase)) name = name[..^FolderSuffix.Length];
        int cut = name.LastIndexOf('_');
        return cut > 0 ? name[..cut] : null;
    }

    /// <summary>
    /// Zeitpunkt der Erfassung: das Datum aus dem Ordnernamen (<c>&lt;HOST&gt;_&lt;yyyyMMdd&gt;</c>) um 00:00 UTC – die Uhrzeit
    /// kennt das Paket nicht. Ohne Datum im Namen die jüngste Änderungszeit der Dateien.
    /// </summary>
    internal static DateTimeOffset CollectedAt(DirectoryInfo dir)
    {
        var name = dir.Name.EndsWith(FolderSuffix, StringComparison.OrdinalIgnoreCase) ? dir.Name[..^FolderSuffix.Length] : dir.Name;
        int cut = name.LastIndexOf('_');
        if (cut > 0 && DateTime.TryParseExact(name[(cut + 1)..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
        var newest = dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly).Select(f => f.LastWriteTimeUtc).DefaultIfEmpty(DateTime.UnixEpoch).Max();
        return new DateTimeOffset(DateTime.SpecifyKind(newest, DateTimeKind.Utc)).ToUniversalTime();
    }

    // ---------------------------------------------------------------- Schritte

    private static string RegStep(string fileName)
    {
        int dash = fileName.IndexOf('-');
        return "reg-" + (dash > 0 ? fileName[..dash] : Path.GetFileNameWithoutExtension(fileName));
    }

    private static bool StepFailed(Dictionary<string, int> exitCodes, string step) => exitCodes.TryGetValue(step, out var code) && code != 0;

    private static SnapshotItem Failed(string key, string step, int code) =>
        new(key, ItemStatus.Error, null, code, RglCodesParsing.ItemError(RglCodesParsing.BundleStepFailed, step, code), 0);

    private static JsonElement TextValue(string text) => JsonValues.Build(w =>
    {
        w.WriteStartObject();
        w.WriteString("text", text);
        w.WriteEndObject();
    });

    /// <summary>Dateien des Pakets mit Größengrenze; nur direkt im Ordner, keine Verknüpfungen.</summary>
    private sealed class Bundle(DirectoryInfo dir, ReadLimits limits)
    {
        internal bool Has(string name)
        {
            var file = new FileInfo(Path.Combine(dir.FullName, name));
            return file.Exists && !file.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }

        internal byte[] Bytes(string name) => TextFiles.ReadCapped(Path.Combine(dir.FullName, name), limits.MaxTextFileBytes);

        /// <summary><c>exitcodes.txt</c>: je Zeile „Schritt=Exitcode“; andere Zeilen werden übergangen.</summary>
        internal Dictionary<string, int> ExitCodes()
        {
            var codes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!Has("exitcodes.txt")) return codes;
            foreach (var line in TextFiles.Lines(TextFiles.DecodeConsoleText(Bytes("exitcodes.txt"), "exitcodes.txt")))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (int.TryParse(line[(eq + 1)..].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var code))
                    codes[line[..eq].Trim()] = code;
            }
            return codes;
        }
    }
}
