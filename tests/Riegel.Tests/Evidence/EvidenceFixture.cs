// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Text;
using Riegel.Core.Evidence;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Evidence;

/// <summary>
/// Anlage im Temp-Ordner mit Exporten, Registern und Läufen, ausgewertet mit den Diensten (Platzhalter oder echte
/// Pakete) und einem kleinen Testkatalog, der über <see cref="ICatalogueProvider.Load"/> entsteht – so berechnet verify
/// dieselbe Katalog-Prüfsumme wie beim Schreiben.
/// </summary>
internal sealed class EvidenceFixture : IDisposable
{
    public EvidenceFixture()
    {
        Services = RiegelServices.Create(new NullPlatform(), Clock);
        Catalogue = LoadCatalogue(Services, Rules());
        Plant = new TempPlant();
        Out = new TempFolder();
    }

    public FixedClock Clock { get; } = new();

    public RiegelServices Services { get; }

    /// <summary>Katalog, mit dem die Anlage ausgewertet und das Paket geschrieben wird (Tests setzen einen veränderten).</summary>
    public ICatalogue Catalogue { get; set; }

    public TempPlant Plant { get; }

    public TempFolder Out { get; }

    /// <summary>Das Nachweispaket mit dem Testkatalog an Stelle des eingebetteten.</summary>
    public IEvidencePack Pack => new EvidencePack(Services, () => Catalogue);

    public static IReadOnlyList<Rule> Rules() =>
    [
        RuleBuilder.Check("NET-08", "network").Severity(Severity.Medium)
            .Registry(@"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "RequireSecuritySignature", RegValueType.Dword,
                CheckOperator.Eq, 1, WhenMissing.Fail)
            .Verified("MSDOCS-CCBY", "https://learn.microsoft.com/").Build(),
        RuleBuilder.Check("LOGON-10", "logon").Severity(Severity.High)
            .Registry(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "AutoAdminLogon", RegValueType.Sz,
                CheckOperator.Ne, "1", WhenMissing.Fail)
            .Verified("ACSC", "https://www.cyber.gov.au/").Build(),
        RuleBuilder.Check("NET-03", "network").Severity(Severity.Medium)
            .Registry(@"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", "EnableMulticast", RegValueType.Dword,
                CheckOperator.Eq, 0, WhenMissing.Fail)
            .Verified("ACSC", "https://www.cyber.gov.au/").Build(),
    ];

    public static ICatalogue LoadCatalogue(RiegelServices services, IReadOnlyList<Rule> rules, string version = "2026.10")
    {
        var document = new CatalogueDocument(SchemaIds.Catalogue, "riegel", version, new DateOnly(2026, 10, 18), rules,
            LifecycleTable.Empty, BuildTable.Empty, CapabilityTable.Empty, CatalogueData.Empty);
        using var stream = new MemoryStream(RiegelJson.SerializeToUtf8Bytes(document));
        return services.Catalogue.Load(stream, ReadLimits.Default);
    }

    /// <summary>Register der Beispielanlage auf die Testanlage zugeschnitten (Rechner HMI-TEST, W11-TEST).</summary>
    public RegisterHashes WriteRegisters(string justificationSuffix = "")
    {
        var exceptions = new ExceptionFile(SchemaIds.Exceptions, Plant.Plant.Id,
        [
            new ExceptionEntry("EX-0001", "LOGON-10", new RegisterScope(["HMI-TEST"], [], false), ReasonCategory.VendorRequirement,
                "Automatische Anmeldung laut Herstellerdokument erforderlich." + justificationSuffix, null, "Herstellerdokument HD-4711", "1",
                Approval.None, new DateOnly(2026, 4, 1), "Anlagenverantwortlicher", null, "2026.10", null),
        ]);
        var tailoring = new TailoringFile(SchemaIds.Tailoring, Plant.Plant.Id,
        [
            new TailoringEntry("TL-0001", "NET-03", new RegisterScope([], ["g-hmi"], false),
                "Namensauflösung ist in dieser Rolle nicht Teil der Soll-Konfiguration.", Approval.None, new DateOnly(2026, 4, 1),
                "Anlagenverantwortlicher", null, null),
        ]);
        var measures = new MeasureFile(SchemaIds.Measures, Plant.Plant.Id,
        [
            new MeasureEntry("M-0001", "NET-08", new RegisterScope([], [], true), "SMB-Signierung im Wartungsfenster einschalten",
                "Anlagenverantwortlicher", new DateOnly(2026, 12, 15), null, MeasureState.Planned, new DateOnly(2026, 9, 1),
                "Anlagenverantwortlicher", null, null),
        ]);
        return Plant.WriteRegisters(new RegisterSet(exceptions, tailoring, measures, RegisterHashes.Empty));
    }

    /// <summary>Export in den Anlagenordner legen und mit den Diensten auswerten und speichern.</summary>
    public Run Evaluate(Snapshot snapshot, ICatalogue? catalogue = null)
    {
        var path = Plant.AddSnapshot(snapshot);
        return Services.Runs.EvaluateAndStore(Plant.Plant, path, new RunOptions(ImportedAtUtc: Clock.Now, Catalogue: catalogue ?? Catalogue));
    }

    /// <summary>Zwei Rechner mit Registern, ausgewertet; gibt die Läufe zurück.</summary>
    public IReadOnlyList<Run> Standard()
    {
        WriteRegisters();
        var hmi = Evaluate(SnapshotBuilder.Windows10Ltsc2019("HMI-TEST")
            .RegistryValue(@"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "RequireSecuritySignature", RegValueType.Dword, 0)
            .Build());
        Clock.Advance(TimeSpan.FromMinutes(1));
        var w11 = Evaluate(SnapshotBuilder.Windows11Workgroup("W11-TEST").Build());
        return [hmi, w11];
    }

    /// <summary>Paket schreiben (mit zwei Berichtsdateien in der Wurzel).</summary>
    public string Write(bool includeBaselineContent = false, IReadOnlyList<string>? hosts = null, string language = "de")
    {
        var documents = Out.Sub("documents");
        var pdf = Path.Combine(documents, "Riegel-Report_Testanlage_2026-10-18.pdf");
        File.WriteAllBytes(pdf, Encoding.ASCII.GetBytes("%PDF-1.7\n% Testdokument\n"));
        var csv = Path.Combine(documents, "results.csv");
        File.WriteAllText(csv, "plant,host\r\ntestanlage,HMI-TEST\r\n", new UTF8Encoding(false));
        return Pack.Write(new EvidenceRequest(Plant.Plant, hosts, new DateOnly(2026, 10, 18), [pdf, csv], includeBaselineContent,
            "Anlagenverantwortlicher", language, Out.Sub("evidence")));
    }

    public void Dispose()
    {
        Plant.Dispose();
        Out.Dispose();
    }

    // ---------------------------------------------------------------- Pakete lesen und verändern

    public static SortedDictionary<string, byte[]> ReadAll(string zip)
    {
        var result = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        using var archive = ZipFile.OpenRead(zip);
        foreach (var e in archive.Entries)
        {
            using var s = e.Open();
            using var m = new MemoryStream();
            s.CopyTo(m);
            result[e.FullName] = m.ToArray();
        }
        return result;
    }

    public static void WriteAll(string zip, IReadOnlyDictionary<string, byte[]> entries)
    {
        File.Delete(zip);
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
            using var s = entry.Open();
            s.Write(bytes);
        }
    }

    /// <summary>
    /// Ändert ein Paket. Mit <paramref name="rehash"/> werden Manifest und SHA256SUMS.txt neu berechnet – dann fällt
    /// eine Änderung nur noch bei der erneuten Auswertung auf.
    /// </summary>
    public static void Rewrite(string zip, Action<SortedDictionary<string, byte[]>> change, bool rehash)
    {
        var entries = ReadAll(zip);
        change(entries);
        if (rehash)
        {
            string Sha(string path) => Hashes.Sha256Hex(entries[path]);
            var manifest = RiegelJson.Deserialize<EvidenceManifest>(entries[EvidenceLayout.Manifest]);
            manifest = manifest with
            {
                CatalogueFile = manifest.CatalogueFile with { Sha256 = Sha(manifest.CatalogueFile.Path) },
                Runs = [.. manifest.Runs.Select(r => r with { Results = r.Results with { Sha256 = Sha(r.Results.Path) }, Snapshot = r.Snapshot with { Sha256 = Sha(r.Snapshot.Path) } })],
                Files = [.. entries.Keys.Where(k => k is not EvidenceLayout.Manifest and not EvidenceLayout.Checksums).Select(k => new FileRef(k, Sha(k)))],
            };
            entries[EvidenceLayout.Manifest] = RiegelJson.SerializeToUtf8Bytes(manifest);
            entries.Remove(EvidenceLayout.Checksums);
            entries[EvidenceLayout.Checksums] = EvidenceLayout.ChecksumFile(entries.Select(e => KeyValuePair.Create(e.Key, Hashes.Sha256Hex(e.Value))));
        }
        WriteAll(zip, entries);
    }

    public static EvidenceManifest Manifest(string zip) => RiegelJson.Deserialize<EvidenceManifest>(ReadAll(zip)[EvidenceLayout.Manifest]);
}
