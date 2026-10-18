// SPDX-License-Identifier: MIT
using System.Security.Principal;
using Riegel.Core.Model;
using Riegel.Core.Services;

namespace Riegel.Platform.Windows;

/// <summary>
/// Factory der Windows-Anbindung (gehört ab dem Gerüst dem Paket collector; Signatur fest). Der Platzhalter kennt
/// nur die Rechte des Prozesses; Erfassung, Übergabe, Shell und Sammler-Paket melden RGL-0003.
/// </summary>
public static class PlatformFactory
{
    public static IPlatform Create() => new StubWindowsPlatform();
}

/// <summary>Platzhalter (Gerüst).</summary>
internal sealed class StubWindowsPlatform : IPlatform
{
    public bool CanCollect => true;

    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public Snapshot CollectThisMachine(CollectOptions o, CancellationToken ct) => throw NotYet("collect");

    public string CreateHandoffDirectory(string token) => throw NotYet("handoff");

    public HandoffResult RunElevatedCollect(string token, CancellationToken ct) => throw NotYet("handoff");

    public void OpenWithShell(string path) => throw NotYet("shell");

    public string ExportCollectorBundle(string targetDir) => throw NotYet("collector export");

    private static RiegelException NotYet(string what) => RiegelException.Common(RglCodesCommon.NotImplemented, what);
}
