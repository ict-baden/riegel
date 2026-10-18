// SPDX-License-Identifier: MIT
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;

namespace Riegel.Tools.SnapshotAnonymizer;

/// <summary>
/// Entwicklerwerkzeug (Paket collector, nicht ausgeliefert): anonymisiert einen aufgezeichneten Export zu einer
/// Fixture mit einer Erlaubnisliste eingebauter Namen (spec 8.1) und wendet dieselbe Zuordnung auf die Rohdateien
/// desselben Laufs an.
/// <code>
/// SnapshotAnonymizer &lt;export.snapshot.json&gt; &lt;fixture.snapshot.json&gt; [--host RUNNER-2022] [--raw &lt;datei&gt; &lt;ziel&gt;]…
/// </code>
/// Exitcodes: 0 geschrieben, 3 Fehler (nichts geschrieben).
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("SnapshotAnonymizer <export.snapshot.json> <fixture.snapshot.json> [--host NAME] [--raw <file> <target>]...");
            return 3;
        }
        string host = "RUNNER-01";
        var raw = new List<(string From, string To)>();
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--host" && i + 1 < args.Length) host = args[++i];
            else if (args[i] == "--raw" && i + 2 < args.Length) raw.Add((args[++i], args[++i]));
            else
            {
                Console.Error.WriteLine($"{RglCodesCommon.InvalidArgument}: {args[i]}");
                return 3;
            }
        }
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(args[0], Encoding.UTF8))!.AsObject();
            var anonymizer = new Anonymizer(node, host);
            var result = anonymizer.Snapshot(node);
            string json = Write(result);
            // Gegenprobe: muss ein gültiger Export des Vertrags bleiben
            RiegelJson.Deserialize<Snapshot>(Encoding.UTF8.GetBytes(json));
            File.WriteAllText(args[1], json, new UTF8Encoding(false));
            int written = 0;
            foreach (var (from, to) in raw)
            {
                // Fehlt eine Rohdatei (z. B. gpresult ohne Rechte), wird sie übersprungen und genannt
                if (!File.Exists(from)) { Console.Error.WriteLine($"{RglCodesCommon.NotFound}: {from} (skipped)"); continue; }
                RawFile(anonymizer, from, to);
                written++;
            }
            Console.WriteLine($"{args[1]} ({written} raw files)");
            return 0;
        }
        catch (Exception e) when (e is IOException or JsonException or RiegelException or InvalidOperationException or FormatException)
        {
            Console.Error.WriteLine($"{RglCodesCommon.Unexpected}: {e.Message}");
            return 3;
        }
    }

    /// <summary>JSON wie RiegelJson: 2 Leerzeichen, LF, Umlaute lesbar, abschließender Zeilenumbruch.</summary>
    internal static string Write(JsonNode node)
    {
        var options = new JsonSerializerOptions { WriteIndented = true, IndentSize = 2, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return node.ToJsonString(options) + "\n";
    }

    /// <summary>Rohdatei mit derselben Kodierung zurückschreiben (UTF-16 mit BOM bei secedit/gpresult, sonst wie gelesen).</summary>
    private static void RawFile(Anonymizer anonymizer, string from, string to)
    {
        byte[] bytes = File.ReadAllBytes(from);
        Encoding encoding = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE ? new UnicodeEncoding(false, true)
            : bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? new UTF8Encoding(true)
            : new UTF8Encoding(false);
        string text;
        using (var reader = new StreamReader(new MemoryStream(bytes), encoding, detectEncodingFromByteOrderMarks: true)) text = reader.ReadToEnd();
        anonymizer.LearnGpresult(text);
        using var writer = new StreamWriter(to, false, encoding);
        writer.Write(anonymizer.Text(text));
    }
}
