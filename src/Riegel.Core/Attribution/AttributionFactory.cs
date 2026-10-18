// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

namespace Riegel.Core.Attribution;

/// <summary>
/// Factory der Herkunftszuordnung (Paket parsing; Signatur fest): <see cref="ValueSourceAttributor"/> nach spec 2.10.
/// </summary>
public static class AttributionFactory
{
    public static IValueSourceAttributor Create(RiegelServices services) => new ValueSourceAttributor();
}
