// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

namespace Riegel.Core.Snapshots;

/// <summary>
/// Factory des Export-Lesers (Paket parsing; Signatur fest): <see cref="SnapshotReader"/> für .snapshot.json,
/// .json.gz und Bordmittel-Pakete (spec 7.3, 7.5, 7.12).
/// </summary>
public static class SnapshotsFactory
{
    public static ISnapshotReader Create(RiegelServices services) => new SnapshotReader();
}
