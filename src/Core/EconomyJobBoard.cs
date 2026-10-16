namespace FrontierAirportBuilder.Core;

/// <summary>Maintains configured system-funded transport work. Scheduling is supplied by EconomyRealTime; stock provisioning stays auditable.</summary>
public static class EconomyJobBoard
{
    public static EconomyTransition Maintain(EconomyWorld source, DateTimeOffset occurredUtc, bool incrementRevision = true, bool scheduledGeneration = false)
    {
        var world = source.EnsureValid();
        if (occurredUtc == default) throw new ArgumentOutOfRangeException(nameof(occurredUtc));
        var requirements = world.Requirements.ToList();
        var jobs = world.Jobs.ToList();
        var inventories = world.Inventories.ToList();
        var inventoryLedger = world.InventoryLedger.ToList();
        var changed = false;

        foreach (var definition in world.RecurringDemand.Where(x => x.IsEnabled && (world.RealTime is null || scheduledGeneration)).OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var destination = world.Locations.Single(x => x.Id == definition.DestinationLocationId);
            if (destination.Status != EconomicLocationStatus.ActiveCustomer && !(destination.Kind==EconomicLocationKind.Gateway&&destination.Status==EconomicLocationStatus.MsfsVerified)) continue;

            var outstanding = requirements.Count(x => x.NeedId == definition.NeedId && (world.RealTime is null ? x.State is TransportRequirementState.Open or TransportRequirementState.Reserved : x.State == TransportRequirementState.Open));
            var missing = Math.Max(0, (world.RealTime is null ? definition.DesiredAvailableOffers : definition.DesiredAvailableOffers * world.RealTime.OfferSlotsPerDemand) - outstanding);
            for (var index = 0; index < missing; index++)
            {
                var cycle = requirements.Count(x => x.NeedId == definition.NeedId) + 1;
                var suffix = cycle == 1 ? definition.IdStem : $"{definition.IdStem}-{cycle:D6}";
                var requirementId = "requirement-" + suffix;
                var jobId = "job-" + suffix;
                var shipmentId = "shipment-" + suffix;
                if (requirements.Any(x => x.Id == requirementId) || jobs.Any(x => x.Id == jobId || x.ShipmentId == shipmentId))
                    throw new InvalidDataException("Deterministic recurring-demand identifier collides with existing economy data.");

                var promised = jobs.Where(x => x.State == EconomyJobState.Offered)
                    .Select(x => requirements.Single(r => r.Id == x.RequirementId))
                    .Where(x => x.OriginLocationId == definition.OriginLocationId && x.ResourceId == definition.ResourceId)
                    .Sum(x => x.Quantity);
                var inventoryIndex = inventories.FindIndex(x => x.LocationId == definition.OriginLocationId && x.ResourceId == definition.ResourceId);
                var available = inventoryIndex < 0 ? 0 : inventories[inventoryIndex].AvailableQuantity;
                var shortfall = Math.Max(0, promised + definition.PreferredShipmentQuantity - available);
                if (shortfall > 0)
                {
                    if (inventoryIndex < 0) { inventories.Add(new(definition.OriginLocationId, definition.ResourceId, shortfall)); inventoryIndex = inventories.Count - 1; }
                    else inventories[inventoryIndex] = inventories[inventoryIndex] with { AvailableQuantity = available + shortfall };
                    inventoryLedger.Add(new(EconomyIds.Derive("inventory-gateway-", shipmentId), InventoryTransactionKind.SystemGatewaySupply,
                        definition.OriginLocationId, definition.ResourceId, shortfall, null, occurredUtc,
                        "Prototype gateway supply from economic activity outside the playable simulation."));
                }

                requirements.Add(new(requirementId, definition.NeedId, definition.OriginLocationId, definition.DestinationLocationId,
                    definition.ResourceId, definition.PreferredShipmentQuantity, definition.PaymentAmount, definition.CurrencyCode,
                    definition.FundingBusinessId, TransportRequirementState.Open));
                jobs.Add(new(jobId, requirementId, definition.EmployerBusinessId, definition.PlayerId, shipmentId, EconomyJobState.Offered) { OfferedUtc = world.RealTime is null ? null : occurredUtc, ExpiresUtc = world.RealTime is null ? null : occurredUtc.AddHours(world.RealTime.OfferLifetimeHours) });
                changed = true;
            }
        }

        var recurring = changed ? world with { Requirements = requirements,
            Jobs = jobs, Inventories = inventories, InventoryLedger = inventoryLedger }
            : world;
        var operations = OperationJobBoard.Maintain(recurring, occurredUtc, false);
        if (!changed && !operations.Changed) return new(world, false);
        var next = operations.World with { Revision = incrementRevision ? world.Revision + 1 : world.Revision };
        return new(next.EnsureValid(), true);
    }
}
