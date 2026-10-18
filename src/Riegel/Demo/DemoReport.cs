// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Samples;
using Riegel.Core.Services;
using Riegel.Platform.Windows;

namespace Riegel.Demo;

/// <summary>
/// Verstecktes <c>--demo-report &lt;Ordner&gt;</c> (spec 7.7, 7.9). Gerüst: alle Dokumente aus spec 5 über die Dienste
/// aus den Beispieldaten (<see cref="SampleData"/>) – mit den Platzhalter-Schreibern also Platzhalter unter den echten
/// Dateinamen. Das Paket app ersetzt die Beispieldaten durch die Demo-Anlage „Musterwerk – Linie 3“.
/// </summary>
internal static class DemoReport
{
    public static void Write(string folder)
    {
        var services = RiegelServices.CreateDefault(PlatformFactory.Create());
        Directory.CreateDirectory(folder);
        var options = new PdfOptions(folder, PerMachine: true);
        var report = SampleData.Report();
        services.Pdf.WriteReport(report, options);
        services.Pdf.WriteSummary(SampleData.Summary(), options);
        services.Pdf.WriteTargetConfig(SampleData.TargetConfig(), options);
        services.Pdf.WriteApprovalSheet(SampleData.ApprovalSheet(), options);
        services.Pdf.WriteCollectionRecord(SampleData.CollectionRecord(), options);
        services.Tabular.WriteWorkbook(report, folder);
        services.Tabular.WriteTargetConfigWorkbook(SampleData.TargetConfig(), folder);
        services.Tabular.WriteCsv(report, folder);
        services.Evidence.Write(new EvidenceRequest(SampleData.Plant(), null, SampleData.Date, [], false, null, "en", folder));
        var run = SampleData.Runs()[0];
        services.Remediation.Write(run, SampleData.Snapshot(run.HostName), services.Catalogue.LoadEmbedded(), SampleData.Registers(), folder, "en");
    }
}
