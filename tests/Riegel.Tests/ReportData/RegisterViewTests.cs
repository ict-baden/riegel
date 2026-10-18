// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.Localization;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Plants;
using Riegel.Core.Resources;
using Riegel.Tests.Plants;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Plants.WorkflowKit;

namespace Riegel.Tests.ReportBuilding;

/// <summary>Registersichten (Kennzeichen overlap und valueMismatch) und die Fehlercodes des Pakets.</summary>
public sealed class RegisterViewTests
{
    [Fact]
    public void Ueberschneidung_und_abweichender_Wert_in_den_Registersichten()
    {
        using var rig = new ReportRig();
        rig.Plant.WriteRegisters(Set(
        [
            Exception("EX-0001", "NET-08", Groups("g-hmi"), accepted: "0").Approve(),
            Exception("EX-0002", "NET-08", Machines("HMI-01"), accepted: "1").Approve(),
        ], plantId: "musteranlage"));
        rig.Add(ReportRig.Hmi(1, new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero)).Script("NET-08", "fail|0"));
        rig.Add(ReportRig.Hmi(2, new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero)).Script("NET-08", "fail|0"));
        var data = rig.Build();
        var group = data.Exceptions.Single(e => e.Entry.Id == "EX-0001");
        var machine = data.Exceptions.Single(e => e.Entry.Id == "EX-0002");
        Assert.Equal(RegisterFlags.Overlap, group.Flags);
        Assert.Equal(RegisterFlags.Overlap | RegisterFlags.ValueMismatch, machine.Flags);
        Assert.Equal(["HMI-01"], machine.AffectedHosts);
        // HMI-01: die Rechner-Ausnahme gewinnt, ihr Wert passt nicht; HMI-02: die Gruppen-Ausnahme gilt
        Assert.Equal(ResultStatus.Fail, data.Machines.Single(m => m.HostName == "HMI-01").Results.Single(r => r.Result.RuleId == "NET-08").Result.Status);
        Assert.Equal(ResultStatus.Exception, data.Machines.Single(m => m.HostName == "HMI-02").Results.Single(r => r.Result.RuleId == "NET-08").Result.Status);
        Assert.Equal(2, data.SignOff.ExceptionValidCount);
    }

    [Fact]
    public void Fehlercodes_sind_eindeutig_im_Bereich_und_haben_Texte_in_fuenf_Sprachen()
    {
        Assert.Equal(RglCodesWorkflow.All.Count, RglCodesWorkflow.All.Distinct().Count());
        foreach (var code in RglCodesWorkflow.All)
        {
            Assert.Matches(@"^RGL-04[0-4]\d$", code);
            var number = int.Parse(code[4..], CultureInfo.InvariantCulture);
            Assert.InRange(number, RglCodesWorkflow.First, RglCodesWorkflow.Last);
            foreach (var language in UiLanguage.Supported)
            {
                var text = WorkflowStrings.ResourceManager.GetString("Error_" + code.Replace("-", "", StringComparison.Ordinal), CultureInfo.GetCultureInfo(language));
                Assert.False(string.IsNullOrWhiteSpace(text), $"{code} ({language}) ohne Text");
            }
        }
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("Im Register tailoring gibt es keinen Eintrag TL-0009.", RglCodesWorkflow.Message(RglCodesWorkflow.EntryNotFound, "TL-0009", "tailoring"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Freigabeblatt_Massnahmen_und_Versicherer_Zeile_gibt_es_in_jeder_Sprache()
    {
        foreach (var language in UiLanguage.Supported)
        {
            Assert.False(string.IsNullOrWhiteSpace(WorkflowText.Get("InsurerFacts_NotFromMachine", language)));
            Assert.Contains("MFA", WorkflowText.Get("InsurerFacts_NotFromMachine", language), StringComparison.Ordinal);
            Assert.Contains("{3}", WorkflowText.Get("ApprovalSheet_Measure", language), StringComparison.Ordinal);
        }
        Assert.Equal("Nicht aus dem Rechner ablesbar: MFA, Datensicherung, Netzsegmentierung", WorkflowText.Get("InsurerFacts_NotFromMachine", "de"));
    }
}
