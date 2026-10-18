// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

namespace Riegel.Platform.Windows;

/// <summary>Factory der Windows-Anbindung (Paket collector; Signatur fest).</summary>
public static class PlatformFactory
{
    public static IPlatform Create() => new WindowsPlatform();
}
