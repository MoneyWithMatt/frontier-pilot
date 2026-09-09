namespace FrontierAirportBuilder.Core;

/// <summary>Temporary, configurable values for the first employed-cargo play test. These are not final balance.</summary>
public sealed record EmployedCargoPrototypeOptions(
    decimal ErosSourceInventoryKilograms = 500m,
    decimal UisCargoKilograms = 120m,
    decimal UisPaymentTpc = 240m,
    decimal ArandisCargoKilograms = 100m,
    decimal ArandisPaymentTpc = 200m)
{
    public EmployedCargoPrototypeOptions EnsureValid()
    {
        if (ErosSourceInventoryKilograms <= 0 || UisCargoKilograms <= 0 || ArandisCargoKilograms <= 0 ||
            UisPaymentTpc <= 0 || ArandisPaymentTpc <= 0)
            throw new InvalidDataException("Temporary employed-cargo values are invalid.");
        if (ErosSourceInventoryKilograms < UisCargoKilograms + ArandisCargoKilograms)
            throw new InvalidDataException("Eros prototype inventory cannot fund all configured offers.");
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
        var requirements = eligible.Select(x => new TransportRequirement($"requirement-{x.Suffix}-supplies-v0", $"need-{x.Suffix}-supplies-v0",
            ErosId, x.DestinationId, ResourceId, x.Quantity, x.Payment, Currency, $"customer-funder-{x.Suffix}", TransportRequirementState.Open)).ToArray();
        var jobs = eligible.Select(x => new EconomyJob($"job-{x.Suffix}-supplies-v0", $"requirement-{x.Suffix}-supplies-v0", EmployerId,
            PlayerId, $"shipment-{x.Suffix}-supplies-v0", EconomyJobState.Offered)).ToArray();

        return new EconomyWorld(WorldId, 1, locations,
            [new(EmployerId, "Frontier Regional Cargo (system prototype)", EconomicOwnerKind.System),
             new("customer-funder-uis", "Uis customer funding (prototype)", EconomicOwnerKind.System),
             new("customer-funder-arandis", "Arandis customer funding (prototype)", EconomicOwnerKind.System)],
            [new(ResourceId, "General Supplies", "general-supplies", "kg")],
            [new(ErosId, ResourceId, options.ErosSourceInventoryKilograms), new(UisId, ResourceId, 0), new(ArandisId, ResourceId, 0)],
            needs, requirements, [new(PlayerId, EmployerId, Currency, 0)], jobs, [], []).EnsureValid();
    }
}
