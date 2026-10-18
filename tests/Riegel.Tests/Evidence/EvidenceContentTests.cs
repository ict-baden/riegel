// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.RegularExpressions;
using Riegel.Core.Evidence;
using Riegel.Core.Localization;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Resources;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Evidence;

/// <summary>Weitere Inhalte des Pakets (Vergleiche, Importnachweise), Verlaufs-Flags bei verify und die Fehlercodes.</summary>
public sealed partial class EvidenceContentTests
{
    [Fact]
    public void Vergleich_mit_vorigem_Lauf_und_freigegebenem_Stand_liegt_im_Paket()
    {
        using var f = new EvidenceFixture();
        f.WriteRegisters();
        var first = f.Evaluate(SnapshotBuilder.Windows10Ltsc2019("HMI-TEST").CollectedAt(new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero)).Build());
        f.Services.Plants.SaveApproved(f.Plant.Plant, new ApprovedState(SchemaIds.Approved, first.HostName, first.Id, first.Snapshot.Sha256,
            new string('a', 64), first.Catalogue.Version, first.Registers,
            new Approval("Qualitätssicherung", null, new DateOnly(2026, 9, 20), null, "CHG-DEMO-0020", null)));
        f.Clock.Advance(TimeSpan.FromMinutes(5));
        var second = f.Evaluate(SnapshotBuilder.Windows10Ltsc2019("HMI-TEST")
            .RegistryValue(@"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "RequireSecuritySignature", RegValueType.Dword, 1).Build());
        Assert.Equal(first.Id, second.PreviousRunId);

        var zip = f.Write();
        var entries = EvidenceFixture.ReadAll(zip);
        var previous = RiegelJson.Deserialize<RunComparison>(entries["comparison/HMI-TEST.previous.json"]);
        Assert.Equal(first.Id, previous.FromRunId);
        Assert.Equal(second.Id, previous.ToRunId);
        Assert.Equal(ComparisonBasis.Previous, previous.Basis);
        var approved = RiegelJson.Deserialize<RunComparison>(entries["comparison/HMI-TEST.approved.json"]);
        Assert.Equal(ComparisonBasis.Approved, approved.Basis);
        // Nur der neueste Lauf je Rechner ist gepackt
        Assert.Equal([second.Id], EvidenceFixture.Manifest(zip).Runs.Select(r => r.RunId));
        Assert.Equal(VerifyStatus.Reproduced, f.Pack.Verify(zip, ReadLimits.Default).Status);
    }

    [Fact]
    public void Importnachweise_der_gepackten_Laeufe_liegen_im_Paket()
    {
        using var f = new EvidenceFixture();
        using var temp = new TempFolder();
        var export = SnapshotBuilder.Windows11Workgroup("W11-TEST").WriteTo(temp.Path);
        var record = f.Services.Plants.Import(f.Plant.Plant, [export], new ImportOptions("Servicetechniker", "Anlagenverantwortlicher (Zeuge)", null, Evaluate: false));
        f.Services.Runs.EvaluateAndStore(f.Plant.Plant, f.Plant.PathOf($"snapshots/W11-TEST/{Path.GetFileName(export)}"),
            new RunOptions(ImportedAtUtc: f.Clock.Now, Catalogue: f.Catalogue));
        // Ein Import ohne Bezug zu einem gepackten Lauf bleibt draußen
        Directory.CreateDirectory(f.Plant.PathOf("imports"));
        RiegelJson.WriteFile(f.Plant.PathOf("imports/20260101T000000Z-abcdef.import.json"),
            new ImportRecord(SchemaIds.Import, "20260101T000000Z-abcdef", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null, null, null, []));
        var entries = EvidenceFixture.ReadAll(f.Write());
        Assert.Contains($"imports/{record.Id}.import.json", entries.Keys);
        Assert.DoesNotContain("imports/20260101T000000Z-abcdef.import.json", entries.Keys);
    }

    [Fact]
    public void Verlaufsabhaengige_Flags_zaehlen_bei_verify_nicht()
    {
        using var f = new EvidenceFixture();
        var runs = f.Standard();
        var zip = f.Write();
        var path = $"runs/{runs[0].HostName}/{runs[0].Id}.results.json";
        EvidenceFixture.Rewrite(zip, e =>
        {
            var run = RiegelJson.Deserialize<Run>(e[path]);
            e[path] = RiegelJson.SerializeToUtf8Bytes(run with
            {
                Results = [.. run.Results.Select(r => r with { Flags = r.Flags | ResultFlags.Regression | ResultFlags.NewRule })],
                PreviousRunId = "20260901T090000Z-000000",
                Flags = run.Flags | RunFlags.MachineIdentityChanged,
            });
        }, rehash: true);
        var result = f.Pack.Verify(zip, ReadLimits.Default);
        Assert.True(result.Status == VerifyStatus.Reproduced, string.Join("\n", result.Findings.Select(x => $"{x.Code} {x.Message}")));
    }

    [Fact]
    public void Fehlercodes_liegen_im_Bereich_sind_eindeutig_und_haben_Text_in_fuenf_Sprachen()
    {
        Assert.Equal(RglCodesDataOutputs.All.Count, RglCodesDataOutputs.All.Distinct().Count());
        foreach (var code in RglCodesDataOutputs.All)
        {
            Assert.Matches(CodePattern(), code);
            var n = int.Parse(code[4..], CultureInfo.InvariantCulture);
            Assert.InRange(n, RglCodesDataOutputs.First, RglCodesDataOutputs.Last);
            foreach (var lang in UiLanguage.Supported)
            {
                var text = DataOutputStrings.ResourceManager.GetString("Error_" + code.Replace("-", "", StringComparison.Ordinal), CultureInfo.GetCultureInfo(lang));
                Assert.False(string.IsNullOrWhiteSpace(text), $"{lang}: {code}");
            }
        }
        // Jede öffentliche Konstante steht in All
        var constants = typeof(RglCodesDataOutputs).GetFields().Where(x => x.IsLiteral && x.FieldType == typeof(string)).Select(x => (string)x.GetValue(null)!);
        Assert.Equal(constants.Order(), RglCodesDataOutputs.All.Order());
    }

    [GeneratedRegex("^RGL-05[5-9][0-9]$")]
    private static partial Regex CodePattern();
}
