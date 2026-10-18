// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

namespace Riegel.Core.Runs;

/// <summary>
/// Factory der Auswertung mit Speichern (Paket workflow; Signatur fest): Export → Fakten → Auswertung → Register →
/// Vergleichsbasis und Flags → Wertung → Lauf (<see cref="RunService"/>).
/// </summary>
public static class RunsFactory
{
    public static IRunService Create(RiegelServices services) => new RunService(services);
}
