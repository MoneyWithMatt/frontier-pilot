namespace FrontierAirportBuilder.Core;

/// <summary>Temporary authored opening stock for physical Frontier-economy gateways.
/// Values are prototype fixtures and do not model imports, prices, or replenishment.</summary>
public sealed record PersistentEconomyPrototypeOptions(
    decimal GatewayGeneralSuppliesKilograms = 300m,
    decimal GatewayRationsKilograms = 200m)
{
    public PersistentEconomyPrototypeOptions EnsureValid()
    {
        if (GatewayGeneralSuppliesKilograms < 0 || GatewayRationsKilograms < 0)
            throw new InvalidDataException("Persistent gateway opening stock cannot be negative.");
        return this;
    }
}

public sealed record PersistentInventoryTransferRequest(string Id, string CommandId,
    string OriginLocationId, string DestinationLocationId, string ResourceId,
    decimal Quantity, DateTimeOffset OccurredUtc, string Details);

/// <summary>Transactions for the bounded persistent-world prototype. Both gateway seeding and
/// transfers use the existing authoritative inventory audit ledger.</summary>
public static class PersistentEconomyKernel
{
    public static EconomyTransition EnsurePrototypeGatewayStock(EconomyWorld source,
        PersistentEconomyPrototypeOptions? configured, DateTimeOffset occurredUtc)
    {
        var world = source.EnsureValid();
        var options = (configured ?? new()).EnsureValid();
        if (occurredUtc == default) throw new ArgumentOutOfRangeException(nameof(occurredUtc));
        if (!world.Locations.Any(x => x.Id == EmployedCargoPrototype.ErosId && x.Kind == EconomicLocationKind.Gateway))
            throw new InvalidDataException("The persistent prototype gateway is missing.");

        var resources = world.Resources.ToList();
        EnsureResource(resources, OperationArchetypeCatalogue.RationsResourceId, "Rations", "operation-supplies", "kg");
        EnsureResource(resources, OperationArchetypeCatalogue.RawMineralsResourceId, "Raw Minerals", "unprocessed-output", "kg");
        var inventories = world.Inventories.ToList();
        var ledger = world.InventoryLedger.ToList();
        var changed = false;

        Seed(EmployedCargoPrototype.ResourceId, options.GatewayGeneralSuppliesKilograms);
        Seed(OperationArchetypeCatalogue.RationsResourceId, options.GatewayRationsKilograms);
        EnsurePosition(OperationArchetypeCatalogue.RawMineralsResourceId);
        if (!changed) return new(world, false);
        return new((world with { Revision = world.Revision + 1, Resources = resources,
            Inventories = inventories, InventoryLedger = ledger }).EnsureValid(), true);

        void EnsurePosition(string resourceId)
        {
            if (inventories.Any(x => x.LocationId == EmployedCargoPrototype.ErosId && x.ResourceId == resourceId)) return;
            inventories.Add(new(EmployedCargoPrototype.ErosId, resourceId, 0)); changed = true;
        }
        void Seed(string resourceId, decimal targetQuantity)
        {
            EnsurePosition(resourceId);
            var transactionId = $"persistent-gateway-opening-{resourceId}";
            if (ledger.Any(x => x.Id == transactionId)) return;
            var index = inventories.FindIndex(x => x.LocationId == EmployedCargoPrototype.ErosId && x.ResourceId == resourceId);
            var quantity = Math.Max(0, targetQuantity - inventories[index].AvailableQuantity);
            if (quantity > 0)
            {
                inventories[index] = inventories[index] with { AvailableQuantity = inventories[index].AvailableQuantity + quantity };
                ledger.Add(new(transactionId, InventoryTransactionKind.PersistentGatewayOpeningStock,
                    EmployedCargoPrototype.ErosId, resourceId, quantity, null, occurredUtc,
                    "Temporary V0 opening stock for the persistent Eros gateway.", CommandId: "persistent-gateway-opening-v0"));
            }
            if (quantity > 0) changed = true;
        }
    }

    public static EconomyTransition Transfer(EconomyWorld source, PersistentInventoryTransferRequest request)
    {
        var world = source.EnsureValid(); ArgumentNullException.ThrowIfNull(request);
        EconomyValidation.Id(request.Id, nameof(request.Id)); EconomyValidation.Id(request.CommandId, nameof(request.CommandId));
        EconomyValidation.Id(request.OriginLocationId, nameof(request.OriginLocationId)); EconomyValidation.Id(request.DestinationLocationId, nameof(request.DestinationLocationId));
        EconomyValidation.Id(request.ResourceId, nameof(request.ResourceId)); EconomyValidation.Positive(request.Quantity, nameof(request.Quantity));
        EconomyValidation.Text(request.Details, nameof(request.Details), 512);
        if (request.OccurredUtc == default) throw new InvalidDataException("Persistent transfer timestamp is missing.");
        if (request.OriginLocationId == request.DestinationLocationId) throw new InvalidOperationException("Persistent transfer origin and destination must differ.");
        var debitId = request.Id + "-out"; var creditId = request.Id + "-in";
        var existingDebit = world.InventoryLedger.Any(x => x.Id == debitId);
        var existingCredit = world.InventoryLedger.Any(x => x.Id == creditId);
        if (existingDebit && existingCredit) return new(world, false);
        if (existingDebit || existingCredit) throw new InvalidDataException("Persistent transfer audit is incomplete.");

        var origin = RequirePersistentLocation(world, request.OriginLocationId);
        var destination = RequirePersistentLocation(world, request.DestinationLocationId);
        if (!world.Resources.Any(x => x.Id == request.ResourceId)) throw new InvalidOperationException("Transfer resource is unknown.");
        var inventories = world.Inventories.ToList();
        var originIndex = inventories.FindIndex(x => x.LocationId == origin.Id && x.ResourceId == request.ResourceId);
        if (originIndex < 0 || inventories[originIndex].AvailableQuantity < request.Quantity)
            throw new InvalidOperationException("The origin does not contain enough available inventory for this transfer.");
        var destinationIndex = inventories.FindIndex(x => x.LocationId == destination.Id && x.ResourceId == request.ResourceId);
        if (destinationIndex < 0) { inventories.Add(new(destination.Id, request.ResourceId, 0)); destinationIndex = inventories.Count - 1; }
        var capacity = Capacity(world, destination.Id, request.ResourceId);
        if (capacity is not null && inventories[destinationIndex].AvailableQuantity + request.Quantity > capacity)
            throw new InvalidOperationException("The destination does not have enough configured storage capacity for this transfer.");

        inventories[originIndex] = inventories[originIndex] with { AvailableQuantity = inventories[originIndex].AvailableQuantity - request.Quantity };
        inventories[destinationIndex] = inventories[destinationIndex] with { AvailableQuantity = inventories[destinationIndex].AvailableQuantity + request.Quantity };
        var ledger = world.InventoryLedger.Append(new(debitId, InventoryTransactionKind.PersistentTransferOut,
                origin.Id, request.ResourceId, -request.Quantity, null, request.OccurredUtc, request.Details, CommandId: request.CommandId))
            .Append(new(creditId, InventoryTransactionKind.PersistentTransferIn,
                destination.Id, request.ResourceId, request.Quantity, null, request.OccurredUtc, request.Details, CommandId: request.CommandId)).ToArray();
        return new((world with { Revision = world.Revision + 1, Inventories = inventories, InventoryLedger = ledger }).EnsureValid(), true);
    }

    public static bool IsPersistentLocation(EconomyWorld world, string locationId) =>
        world.Locations.Any(x => x.Id == locationId && (x.Kind == EconomicLocationKind.Gateway ||
            x.Kind == EconomicLocationKind.OperationSite && x.Owner == EconomicOwnerKind.Player));

    private static EconomicLocation RequirePersistentLocation(EconomyWorld world, string id) =>
        world.Locations.SingleOrDefault(x => x.Id == id && IsPersistentLocation(world, id)) ??
        throw new InvalidOperationException("Inventory transfers require a persistent gateway or player-owned operation location.");

    private static decimal? Capacity(EconomyWorld world, string locationId, string resourceId)
    {
        var site = world.Sites.SingleOrDefault(x => x.LocationId == locationId); if (site is null) return null;
        var operation = world.Operations.Single(x => x.SiteId == site.Id);
        return OperationArchetypeCatalogue.Get(operation.ArchetypeId).Storage.SingleOrDefault(x => x.ResourceId == resourceId)?.Capacity;
    }

    private static void EnsureResource(List<ResourceType> resources, string id, string name, string category, string unit)
    {
        var existing = resources.SingleOrDefault(x => x.Id == id);
        if (existing is null) resources.Add(new(id, name, category, unit));
        else if (existing.Name != name || existing.Category != category || existing.UnitName != unit)
            throw new InvalidDataException($"Resource definition '{id}' conflicts with persistent-economy data.");
    }
}

internal static class PersistentEconomyValidation
{
    public static void EnsureValid(EconomyWorld world)
    {
        var persistentPositions = world.InventoryLedger
            .Where(x => x.Kind is InventoryTransactionKind.PersistentGatewayOpeningStock or
                InventoryTransactionKind.PersistentTransferOut or InventoryTransactionKind.PersistentTransferIn)
            .Select(x => (x.LocationId, x.ResourceId)).Distinct().ToArray();
        foreach (var position in persistentPositions)
        {
            if (!PersistentEconomyKernel.IsPersistentLocation(world, position.LocationId))
                throw new InvalidDataException("Persistent inventory audit refers to a non-persistent economic location.");
            var balance = world.Inventories.SingleOrDefault(x => x.LocationId == position.LocationId && x.ResourceId == position.ResourceId)
                ?? throw new InvalidDataException("Persistent inventory audit has no cached balance.");
            var audited = world.InventoryLedger.Where(x => x.LocationId == position.LocationId && x.ResourceId == position.ResourceId).Sum(x => x.QuantityDelta);
            if (audited != balance.AvailableQuantity)
                throw new InvalidDataException("Persistent inventory balance disagrees with the authoritative inventory audit ledger.");
        }
    }
}
