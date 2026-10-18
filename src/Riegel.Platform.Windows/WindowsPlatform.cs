// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using Riegel.Collector.Collection;
using Riegel.Core.About;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;
using Riegel.Core.Settings;
using Riegel.Platform.Windows.Resources;
using CollectionRun = Riegel.Collector.Collection.Collector;

namespace Riegel.Platform.Windows;

/// <summary>
/// <see cref="IPlatform"/> für Windows (spec 7.2, 7.6): Erfassung über den verlinkten Code des Sammlers (Herkunft
/// app), Übergabeordner und Rechteerhöhung, Öffnen mit der Shell, Sammler-Paket kopieren.
/// </summary>
internal sealed class WindowsPlatform : IPlatform
{
    /// <summary>Dateiarten, die Riegel selbst erzeugt und die die Shell öffnen darf (nie .reg, .exe, .cmd, .ps1 …).</summary>
    internal static readonly string[] OpenableExtensions = [".pdf", ".xlsx", ".csv", ".json", ".txt", ".zip", ".log", ".md", ".gz"];

    public bool CanCollect => true;

    public bool IsElevated => MachineFacts.IsAdmin();

    public Snapshot CollectThisMachine(CollectOptions o, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(o);
        ct.ThrowIfCancellationRequested();
        var settings = new CollectSettings
        {
            Minimal = o.Minimal,
            Deep = o.Deep,
            Pseudonymize = o.Pseudonymize,
            Origin = "app",
            CollectorName = AppInfo.Name,
            CollectorVersion = AppInfo.Version,
            ExeSha256 = AppInfo.ExeSha256,
            CollectedAtUtc = TruncateToSeconds(DateTime.UtcNow),
            Cancel = () => ct.IsCancellationRequested,
        };
        SnapshotData data;
        try
        {
            data = CollectionRun.Collect(LoadPlan(), settings);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new RiegelException(RglCodesCollector.CollectionFailed, Message(RglCodesCollector.CollectionFailed, e.GetType().Name), e);
        }
        ct.ThrowIfCancellationRequested();
        return ToSnapshot(data);
    }

    /// <summary>Der Export als Vertragstyp: derselbe JSON-Text wie im net35-Sammler, gelesen mit RiegelJson.</summary>
    internal static Snapshot ToSnapshot(SnapshotData data) =>
        RiegelJson.Deserialize<Snapshot>(Encoding.UTF8.GetBytes(JsonWriter.Write(data.ToJson())));

    /// <summary>Plan aus der eingebetteten Ressource (contracts/collection-plan.json, wie im Sammler).</summary>
    internal static Plan LoadPlan()
    {
        using var stream = typeof(WindowsPlatform).Assembly.GetManifestResourceStream(Plan.ResourceName)
            ?? throw new InvalidOperationException("Erfassungsplan nicht eingebettet (" + Plan.ResourceName + ")");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Plan.Parse(reader.ReadToEnd());
    }

    private static DateTime TruncateToSeconds(DateTime utc) => new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    // ------------------------------------------------------------------ Übergabe mit Rechteerhöhung (spec 7.6)

    public string CreateHandoffDirectory(string token) => Handoff.CreateDirectory(AppPaths.HandoffFolder, token);

    public HandoffResult RunElevatedCollect(string token, CancellationToken ct)
    {
        Handoff.ValidateToken(token);
        string exe = RiegelExe();
        int error = Shell.RunElevatedAndWait(exe, "collect --handoff " + token, ct, out int exitCode, out bool cancelled);
        if (error == Shell.ERROR_CANCELLED)
            return new HandoffResult(false, null, CommandExit.Unknown, RglCodesCollector.ElevationCancelled, Message(RglCodesCollector.ElevationCancelled), false);
        if (error != 0)
            return new HandoffResult(false, null, CommandExit.Unknown, RglCodesCollector.ElevationFailed, Message(RglCodesCollector.ElevationFailed, error), false);
        if (cancelled)
            return new HandoffResult(false, null, CommandExit.Unknown, RglCodesCollector.ElevationCancelled, Message(RglCodesCollector.ElevationCancelled), false);
        return Handoff.Evaluate(AppPaths.HandoffFolder, token, exitCode);
    }

    /// <summary>Riegel.exe neben dem laufenden Programm, sonst das laufende Programm selbst (z. B. Riegel.Cli.exe).</summary>
    private static string RiegelExe()
    {
        string current = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Riegel.exe");
        if (string.Equals(Path.GetFileName(current), "Riegel.exe", StringComparison.OrdinalIgnoreCase)) return current;
        string gui = Path.Combine(Path.GetDirectoryName(current) ?? AppContext.BaseDirectory, "Riegel.exe");
        return File.Exists(gui) ? gui : current;
    }

    // ------------------------------------------------------------------ Shell

    public void OpenWithShell(string path)
    {
        if (!CanOpen(path)) throw new RiegelException(RglCodesCollector.ShellOpenRefused, Message(RglCodesCollector.ShellOpenRefused, path));
        int error = Shell.Open(IsWebLink(path) ? path : Path.GetFullPath(path));
        if (error != 0) throw new RiegelException(RglCodesCollector.ShellOpenFailed, Message(RglCodesCollector.ShellOpenFailed, path, error));
    }

    /// <summary>Nur http/https-Links, vorhandene Ordner und vorhandene Dateien der Arten, die Riegel erzeugt.</summary>
    internal static bool CanOpen(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (IsWebLink(path)) return true;
        if (path.Contains("://", StringComparison.Ordinal) || path.StartsWith("\\\\?\\", StringComparison.Ordinal)) return false;
        try
        {
            string full = Path.GetFullPath(path);
            if (Directory.Exists(full)) return true;
            if (!File.Exists(full)) return false;
            string extension = Path.GetExtension(full);
            return OpenableExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsWebLink(string path) =>
        Uri.TryCreate(path, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    // ------------------------------------------------------------------ Sammler-Paket (O15)

    public string ExportCollectorBundle(string targetDir) =>
        CollectorBundle.Export(Path.Combine(AppContext.BaseDirectory, "collector"), targetDir, DateTime.UtcNow);

    internal static string Message(string code, params object[] args)
    {
        string? text = PlatformStrings.ResourceManager.GetString("Error_" + code.Replace("-", "", StringComparison.Ordinal), CultureInfo.CurrentUICulture);
        return text is null ? code : string.Format(CultureInfo.CurrentCulture, text, args);
    }
}

/// <summary>Exitcodes der Befehlszeile, soweit die Anbindung sie meldet (Nagios-Art, spec 7.7).</summary>
internal static class CommandExit
{
    public const int Unknown = 3;
}
