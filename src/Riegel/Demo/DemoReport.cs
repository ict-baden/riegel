// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using Riegel.Commands;
using Riegel.Core.Diagnostics;
using Riegel.Core.Localization;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Services;
using Riegel.Session;

namespace Riegel.Demo;

/// <summary>
/// Verstecktes <c>--demo-report &lt;Ordner&gt;</c> (spec 7.7, 7.9): baut die Demo-Anlage über die Dienste auf und schreibt
/// jedes Dokument aus spec 5 in den Ordner – für die Beispiele der Doku und als Rundlauf in der CI. Was ein Dienst in
/// diesem Stand noch nicht kann (RGL-0003), wird übersprungen und genannt; jeder andere Fehler bricht mit Exitcode 3 ab.
/// Am Ende steht <c>demo-report.txt</c> mit jedem Dokument und seinem Ergebnis.
/// </summary>
internal static class DemoReport
{
    public sealed record Entry(string Output, string? Path, string? Skipped);

    public static int Run(string folder, IPlatform platform, TextWriter output, TextWriter error)
    {
        folder = System.IO.Path.GetFullPath(folder);
        Directory.CreateDirectory(folder);
        var work = DemoPlant.NewFolder();
        try
        {
            var entries = Write(folder, work, platform);
            File.WriteAllText(System.IO.Path.Combine(folder, "demo-report.txt"), Manifest(entries, folder), new UTF8Encoding(false));
            foreach (var e in entries)
            {
                if (e.Skipped is null) output.WriteLine($"{e.Output}: {System.IO.Path.GetRelativePath(folder, e.Path!)}");
                else error.WriteLine($"{e.Output}: {e.Skipped}");
            }
            output.WriteLine(string.Format(CultureInfo.CurrentCulture, Riegel.UI.Loc.T("App_DemoReportWritten"), folder));
            return CommandApp.Ok;
        }
        catch (RiegelException e)
        {
            error.WriteLine(e.ToString());
            return CommandApp.Unknown;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error.WriteLine(RglCodesApp.Message(RglCodesApp.DocumentFailed, e.Message));
            return CommandApp.Unknown;
        }
        finally
        {
            DemoPlant.Delete(work);
        }
    }

    /// <summary>Alle Dokumente der Demo-Anlage nach <paramref name="folder"/> (Anlage im Arbeitsordner <paramref name="work"/>).</summary>
    public static IReadOnlyList<Entry> Write(string folder, string work, IPlatform platform)
    {
        var plant = DemoPlant.Build(work, platform);
        var services = DemoPlant.Services(platform, DemoData.Now);
        plant = services.Plants.Open(plant.Folder!, readOnly: false);
        var language = UiLanguage.Normalize(CultureInfo.CurrentUICulture.Name) is { Length: > 0 } code ? code : UiLanguage.Fallback;
        var today = services.Today;
        var entries = new List<Entry>();

        void Do(string output, Func<string> write)
        {
            try { entries.Add(new Entry(output, write(), null)); }
            catch (RiegelException e) when (e.Code == RglCodesCommon.NotImplemented)
            {
                // Noch nicht in diesem Stand (Platzhalter eines anderen Pakets) – ehrlich nennen, nicht vortäuschen
                entries.Add(new Entry(output, null, e.ToString()));
                Log.Warning($"Demo-Bericht: {output} übersprungen ({e.Code})");
            }
        }

        var formats = new[] { ReportFormat.Pdf, ReportFormat.Xlsx, ReportFormat.Csv, ReportFormat.Json };
        var request = new ReportRequest(plant, null, today, language, ReportPreset.Audit, true, true, formats, true, false, false, folder);
        var report = services.ReportData.Build(request);
        var documents = new List<string>();
        Do("O4/O5", () => Remember(documents, services.Pdf.WriteReport(report, new PdfOptions(folder, PerMachine: true))));
        Do("O7", () => Remember(documents, services.Tabular.WriteWorkbook(report, folder)));
        Do("O3", () => Remember(documents, services.Tabular.WriteCsv(report, folder)));
        var delivered = System.IO.Path.Combine(folder, "as-delivered");
        Do("O4 asDelivered", () =>
        {
            var data = services.ReportData.Build(request with { Preset = ReportPreset.AsDelivered, Hosts = ["HMI-L3-01"], OutDir = delivered });
            return services.Pdf.WriteReport(data, new PdfOptions(delivered));
        });
        Do("O6", () => services.Pdf.WriteSummary(services.ReportData.BuildSummary([plant], today, language), new PdfOptions(folder)));
        var target = services.ReportData.BuildTargetConfig(plant, null, language);
        Do("O13", () => services.Pdf.WriteTargetConfig(target, new PdfOptions(folder)));
        Do("O13 xlsx", () => services.Tabular.WriteTargetConfigWorkbook(target, folder));
        Do("O12", () => services.Pdf.WriteApprovalSheet(services.ReportData.BuildApprovalSheet(plant, null, language), new PdfOptions(folder)));
        Do("O14", () => services.Pdf.WriteCollectionRecord(services.ReportData.BuildCollectionRecord(plant, "latest", language), new PdfOptions(folder)));
        Do("O9", () => services.Evidence.Write(new EvidenceRequest(plant, null, today, documents, false, plant.Settings.OperatorRole, language, folder)));

        var catalogue = services.Catalogue.LoadEmbedded();
        var registers = services.Registers.Load(plant);
        foreach (var machine in plant.Machines)
        {
            var latest = services.Plants.ListRuns(plant, machine.HostName).LastOrDefault();
            if (latest is null) continue;
            var run = services.Plants.LoadRun(plant, latest.Id);
            var snapshot = services.Snapshots.Read(System.IO.Path.Combine(plant.Folder!, run.Snapshot.Path), ReadLimits.Default);
            Do($"O10 {machine.HostName}", () => services.Remediation.Write(run, snapshot, catalogue, registers, folder, language).Directory);
        }
        try { entries.Add(new Entry("O15", services.Platform.ExportCollectorBundle(System.IO.Path.Combine(folder, "collector")), null)); }
        catch (RiegelException e) when (e.Code is RglCodesCommon.NotImplemented or "RGL-0130")
        {
            // RGL-0130: kein Sammler-Paket neben der App (z. B. die Einzeldatei Riegel.exe) – nennen, nicht vortäuschen
            entries.Add(new Entry("O15", null, e.ToString()));
            Log.Warning($"Demo-Bericht: O15 übersprungen ({e.Code})");
        }

        // O1, O2, O8: Exporte, Läufe, Register und Importnachweise liegen im Anlagenordner – als Kopie dazu
        var copy = System.IO.Path.Combine(folder, "plant");
        CopyFolder(plant.Folder!, copy);
        entries.Add(new Entry("O1/O2/O8", copy, null));

        // O11: Konsolenausgabe von check (Befehl aus dem Paket cli)
        var hmi = services.Plants.ListRuns(plant, DemoData.ApprovedHost).LastOrDefault();
        if (hmi is not null)
        {
            var run = services.Plants.LoadRun(plant, hmi.Id);
            var snapshotPath = System.IO.Path.Combine(plant.Folder!, run.Snapshot.Path);
            // O11 ist die Icinga-Zeile (check --brief); darunter dieselbe Prüfung als Text
            using var brief = new StringWriter(CultureInfo.InvariantCulture);
            int briefExit = CommandApp.Run(["check", "--snapshot", snapshotPath, "--brief"], platform, brief, brief);
            using var text = new StringWriter(CultureInfo.InvariantCulture);
            int exit = CommandApp.Run(["check", "--snapshot", snapshotPath], platform, text, text);
            var path = System.IO.Path.Combine(folder, "check.txt");
            File.WriteAllText(path, brief + $"exit code {briefExit}\n\n" + text + $"exit code {exit}\n", new UTF8Encoding(false));
            entries.Add(new Entry("O11", path, null));
        }
        return entries;
    }

    private static string Remember(List<string> documents, string path)
    {
        documents.Add(path);
        return path;
    }

    private static void CopyFolder(string source, string target)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(System.IO.Path.Combine(target, System.IO.Path.GetRelativePath(source, dir)));
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (System.IO.Path.GetFileName(file) is ".riegel.lock") continue;
            File.Copy(file, System.IO.Path.Combine(target, System.IO.Path.GetRelativePath(source, file)), overwrite: true);
        }
    }

    private static string Manifest(IReadOnlyList<Entry> entries, string folder)
    {
        var sb = new StringBuilder();
        sb.Append("Riegel demo report – plant \"").Append(DemoData.PlantName).Append("\" (fictitious), as of ")
            .Append(DemoData.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append('\n');
        foreach (var e in entries)
            sb.Append(e.Output).Append('\t').Append(e.Skipped is null ? System.IO.Path.GetRelativePath(folder, e.Path!).Replace('\\', '/') : "skipped: " + e.Skipped).Append('\n');
        return sb.ToString();
    }
}
