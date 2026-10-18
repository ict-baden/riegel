// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

namespace Riegel.Core.Registers;

/// <summary>
/// Factory der Register (Paket workflow; Signatur fest): Ausnahmen, Zuschnitt und Maßnahmen mit Prüfung,
/// Freigabebindung, Verlauf und Vorlagen (<see cref="RegisterService"/>).
/// </summary>
public static class RegistersFactory
{
    public static IRegisterService Create(RiegelServices services) => new RegisterService(services);
}
