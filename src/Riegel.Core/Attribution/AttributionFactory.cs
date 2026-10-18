// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Services;

namespace Riegel.Core.Attribution;

/// <summary>
/// Factory der Herkunftszuordnung (gehört ab dem Gerüst dem Paket parsing; Signatur fest). Der Platzhalter ordnet
/// nichts zu: alle Werte haben die Herkunft Unknown.
/// </summary>
public static class AttributionFactory
{
    public static IValueSourceAttributor Create(RiegelServices services) => new StubAttributor();
}

/// <summary>Platzhalter (Gerüst).</summary>
internal sealed class StubAttributor : IValueSourceAttributor
{
    public AttributionMap Build(Snapshot s) => AttributionMap.Empty;
}
