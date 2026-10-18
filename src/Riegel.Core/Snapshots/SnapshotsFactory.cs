// SPDX-License-Identifier: MIT
using System.IO.Compression;
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Services;

namespace Riegel.Core.Snapshots;

/// <summary>
/// Factory des Export-Lesers (gehört ab dem Gerüst dem Paket parsing; Signatur fest). Der Platzhalter liest
/// .snapshot.json und .json.gz nach dem Vertrag und prüft Schema und Größe; Bordmittel-Pakete liest erst das Paket parsing.
/// </summary>
public static class SnapshotsFactory
{
    public static ISnapshotReader Create(RiegelServices services) => new StubSnapshotReader();
}

/// <summary>Platzhalter (Gerüst): JSON-Leser ohne Schwärzen, ohne Neuberechnung von parsed:*.</summary>
internal sealed class StubSnapshotReader : ISnapshotReader
{
    public Snapshot Read(string path, ReadLimits l)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw RiegelException.Common(RglCodesCommon.NotFound, path);
        byte[] bytes;
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            using var file = info.OpenRead();
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            bytes = ReadCapped(gzip, l.MaxSnapshotBytes, path);
        }
        else
        {
            if (info.Length > l.MaxSnapshotBytes) throw RiegelException.Common(RglCodesCommon.LimitExceeded, path, l.MaxSnapshotBytes);
            bytes = File.ReadAllBytes(path);
        }
        var snapshot = RiegelJson.Deserialize<Snapshot>(bytes);
        if (snapshot.Schema != SchemaIds.Snapshot) throw RiegelException.Common(RglCodesCommon.UnsupportedSchema, snapshot.Schema);
        if (snapshot.Items.Count > l.MaxItems) throw RiegelException.Common(RglCodesCommon.LimitExceeded, path, l.MaxItems);
        return snapshot;
    }

    public HashCheck VerifySidecar(string path) => SnapshotFile.Verify(path);

    public Snapshot FromBuiltinBundle(string folder, ReadLimits l) => throw RiegelException.Common(RglCodesCommon.NotImplemented, "builtin bundle");

    private static byte[] ReadCapped(Stream stream, long max, string path)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > max) throw RiegelException.Common(RglCodesCommon.LimitExceeded, path, max);
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
