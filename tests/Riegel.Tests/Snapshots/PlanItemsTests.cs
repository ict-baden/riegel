// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Model.Json;
using Riegel.Core.Snapshots;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Snapshots;

/// <summary>Die Liste der Planeinträge im Bordmittel-Leser entspricht contracts/collection-plan.json.</summary>
public sealed class PlanItemsTests
{
    [Fact]
    public void Liste_entspricht_dem_Erfassungsplan()
    {
        var plan = RiegelJson.ReadFile<CollectionPlan>(RepoPaths.CollectionPlan, 10 * 1024 * 1024);
        var nonRegistry = plan.Wmi.Select(w => w.Key).Concat(plan.Api.Select(a => a.Key)).Concat(plan.Events.Select(e => e.Key)).Concat(plan.Raw.Select(r => r.Key));
        Assert.Equal(nonRegistry.Order(StringComparer.Ordinal), PlanItems.NonRegistry.Order(StringComparer.Ordinal));
        var registry = plan.Registry.Roots.Concat(plan.Registry.Keys).Select(e => e.Path);
        Assert.Equal(registry.Order(StringComparer.Ordinal), PlanItems.Registry.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Abgeleitete_Eintraege_sind_die_aus_spec_7_3()
    {
        Assert.Equal([ItemKeys.ParsedSecedit, ItemKeys.ParsedRegistryPol, ItemKeys.ParsedGpresult, ItemKeys.ParsedFirewallRulesRegistry], DerivedItems.Keys);
    }
}
