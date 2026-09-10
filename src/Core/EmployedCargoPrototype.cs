namespace FrontierAirportBuilder.Core;

/// <summary>Temporary, configurable values for the first employed-cargo play test. These are not final balance.</summary>
public sealed record EmployedCargoPrototypeOptions(
    decimal UisCargoKilograms = 120m,
    decimal UisPaymentTpc = 240m,
    decimal ArandisCargoKilograms = 100m,
    decimal ArandisPaymentTpc = 200m,
    int DesiredOffersPerCustomer = 1)
{
    public EmployedCargoPrototypeOptions EnsureValid()
    {
        if (UisCargoKilograms <= 0 || ArandisCargoKilograms <= 0 ||
            UisPaymentTpc <= 0 || ArandisPaymentTpc <= 0)
            throw new InvalidDataException("Temporary employed-cargo values are invalid.");
        if (DesiredOffersPerCustomer is < 1 or > 5) throw new InvalidDataException("Temporary offer count is invalid.");
        return this;
    }
}

public static class EmployedCargoPrototype
{
    public const string WorldId = "namibia-employed-cargo-v0";
    public const string EmployerId = "system-cargo-employer-eros";
    public const string PlayerId = "prototype-employed-player";
    public const string ResourceId = "general-supplies";
    public const string Currency = "TPC"; // Temporary Prototype Credits.
    public const string ErosId = "namibia.windhoek-eros";
    public const string UisId = "namibia.uis-mine";
    public const string ArandisId = "namibia.arandis";

    private sealed record OfferTemplate(string Suffix, string DestinationId, decimal Quantity, decimal Payment);

    public static EconomyWorld Create(NamibiaLocationCatalogueDocument catalogue, EmployedCargoPrototypeOptions? configured = null)
    {
        catalogue = catalogue.EnsureValid(); var options = (configured ?? new()).EnsureValid();
        var eros = catalogue.Get(ErosId);
        if (!eros.IsLockedGateway || eros.VerificationState != LocationVerificationState.MsfsVerified)
            throw new InvalidOperationException("The employed-cargo prototype requires the verified Eros gateway.");

        var templates = new[]
        {
            new OfferTemplate("uis", UisId, options.UisCargoKilograms, options.UisPaymentTpc),
            new OfferTemplate("arandis", ArandisId, options.ArandisCargoKilograms, options.ArandisPaymentTpc)
        };
        var eligible = templates.Where(x => catalogue.Get(x.DestinationId).CanGenerateOrdinaryLiveJobs)
            .Select(x => { _ = NamibiaJobGenerationGuard.RequireEligibleDestination(catalogue, x.DestinationId); return x; }).ToArray();
        var destinationIds = templates.Select(x => x.DestinationId).ToHashSet(StringComparer.Ordinal);
        var locations = catalogue.Locations.Where(x => x.StableId == ErosId || destinationIds.Contains(x.StableId))
            .Select(x => x.ToEconomicLocation()).ToArray();
        var needs = eligible.Select(x => new CustomerNeed($"need-{x.Suffix}-supplies-v0", x.DestinationId, ResourceId, x.Quantity)).ToArray();
        var recurring = eligible.Select(x => new RecurringDemandDefinition($"demand-{x.Suffix}-supplies-v0", $"need-{x.Suffix}-supplies-v0",
            $"{x.Suffix}-supplies-v0", ErosId, x.DestinationId, ResourceId, x.Quantity, x.Payment, Currency,
            options.DesiredOffersPerCustomer, $"customer-funder-{x.Suffix}", EmployerId, PlayerId, true)).ToArray();

        var world = new EconomyWorld(WorldId, 1, locations,
            [new(EmployerId, "Frontier Regional Cargo (system prototype)", EconomicOwnerKind.System),
             new("customer-funder-uis", "Uis customer funding (prototype)", EconomicOwnerKind.System),
             new("customer-funder-arandis", "Arandis customer funding (prototype)", EconomicOwnerKind.System)],
            [new(ResourceId, "General Supplies", "general-supplies", "kg")],
            [new(ErosId, ResourceId, 0), new(UisId, ResourceId, 0), new(ArandisId, ResourceId, 0)],
            needs, [], [new(PlayerId, EmployerId, Currency, 0)], [], [], []) { RecurringDemand = recurring };
        return EconomyJobBoard.Maintain(world, DateTimeOffset.Parse("2026-09-09T00:00:00Z"), incrementRevision: false).World.EnsureValid();
    }

    public static IReadOnlyList<RecurringDemandDefinition> Definitions(EmployedCargoPrototypeOptions? configured = null)
    {
        var options = (configured ?? new()).EnsureValid();
        return
        [
            new("demand-uis-supplies-v0", "need-uis-supplies-v0", "uis-supplies-v0", ErosId, UisId, ResourceId,
                options.UisCargoKilograms, options.UisPaymentTpc, Currency, options.DesiredOffersPerCustomer, "customer-funder-uis", EmployerId, PlayerId, true),
            new("demand-arandis-supplies-v0", "need-arandis-supplies-v0", "arandis-supplies-v0", ErosId, ArandisId, ResourceId,
                options.ArandisCargoKilograms, options.ArandisPaymentTpc, Currency, options.DesiredOffersPerCustomer, "customer-funder-arandis", EmployerId, PlayerId, true)
        ];
    }

    public static EconomyTransition UpgradeForRepeatableJobs(EconomyWorld source, EmployedCargoPrototypeOptions? configured,
        DateTimeOffset occurredUtc)
    {
        var world = source.EnsureValid();
        if (world.Id != WorldId || world.RecurringDemand.Count > 0) return new(world, false);
        var definitions = Definitions(configured).Where(x => world.Needs.Any(n => n.Id == x.NeedId)).ToArray();
        var ledger = world.InventoryLedger.ToList();
        if (!ledger.Any(x => x.Kind == InventoryTransactionKind.LegacyOpeningBalance))
            foreach (var balance in world.Inventories.Where(x => x.AvailableQuantity > 0))
                ledger.Add(new($"inventory-opening-{Sanitize(balance.LocationId)}-{Sanitize(balance.ResourceId)}",
                    InventoryTransactionKind.LegacyOpeningBalance, balance.LocationId, balance.ResourceId, balance.AvailableQuantity,
                    null, occurredUtc, "Opening balance imported from the pre-inventory-ledger prototype document."));
        var upgraded = world with { Revision = world.Revision + 1, RecurringDemand = definitions, InventoryLedger = ledger };
        return new(upgraded.EnsureValid(), true);
    }

    private static string Sanitize(string value) => new(value.Select(x => char.IsAsciiLetterOrDigit(x) ? char.ToLowerInvariant(x) : '-').ToArray());
}
