// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Facts;

/// <summary>
/// Regressionstest (Review evidence-12) mit der aufgezeichneten Fixture windows-11-arm-nonadmin: mit eingeschränktem
/// Token liefert MSFT_MpComputerStatus Platzhalter (AMRunningMode „Not running“, alles false, Signaturalter 65535),
/// während api:wsc-av Microsoft Defender „on“ meldet. Vorher wurde defender.mode „off“ hergeleitet und DEF-01/02/12,
/// LIFE-04 und ASR-01 meldeten „kein aktiver Virenschutz“, während DEF-18 im selben Bericht „erfüllt“ war.
/// </summary>
public sealed class DefenderPlaceholderTests
{
    private static Run Evaluate(string name)
    {
        var snapshot = RiegelJson.ReadFile<Snapshot>(Path.Combine(RepoPaths.RecordedFixtures, name), 64L * 1024 * 1024);
        return RiegelServices.Create(new NullPlatform(), new FixedClock()).Runs.EvaluateOnly(snapshot, new RunOptions(Language: "en"));
    }

    [Fact]
    public void Platzhalter_ohne_Admin_ergeben_unbekannt_statt_aus()
    {
        var run = Evaluate("windows-11-arm-nonadmin.snapshot.json");
        Assert.Equal("unknown", run.Facts["defender.mode"].GetString());
        foreach (var id in new[] { "DEF-01", "DEF-02", "DEF-12", "LIFE-04", "ASR-01" })
        {
            var result = run.Results.Single(r => r.RuleId == id);
            Assert.True(result.Status is ResultStatus.NotCheckable or ResultStatus.NotApplicable, $"{id}: {result.Status}");
        }
        Assert.Equal(ResultStatus.Pass, run.Results.Single(r => r.RuleId == "DEF-18").Status);
    }

    [Fact]
    public void Mit_Admin_bleibt_Defender_aktiv()
    {
        var run = Evaluate("windows-11-arm-admin.snapshot.json");
        Assert.Equal("active", run.Facts["defender.mode"].GetString());
    }
}
