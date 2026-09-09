namespace FrontierAirportBuilder.Core;

/// <summary>Deterministic temporary data for automated and dry prototype work. It is not a balanced game world,
/// an activated Namibia catalogue, or MSFS-verified location data.</summary>
public static class EconomyPrototypeSeed
{
    public const string Currency = "TPC"; // Temporary Prototype Credits.

    public static EconomyWorld Create()
    {
        var locations = new[]
        {
            new EconomicLocation("prototype-gateway", "Prototype Gateway (temporary test data)", EconomicLocationKind.Gateway, EconomicOwnerKind.System, EconomicLocationStatus.PrototypeOnly),
            new EconomicLocation("prototype-mine", "Prototype Mine Customer (temporary test data)", EconomicLocationKind.Mine, EconomicOwnerKind.System, EconomicLocationStatus.PrototypeOnly),
            new EconomicLocation("prototype-community", "Prototype Community Customer (temporary test data)", EconomicLocationKind.Community, EconomicOwnerKind.System, EconomicLocationStatus.PrototypeOnly),
            new EconomicLocation("prototype-lodge", "Prototype Lodge Customer (temporary test data)", EconomicLocationKind.Lodge, EconomicOwnerKind.System, EconomicLocationStatus.PrototypeOnly)
        };
        var requirements = new[]
        {
            Requirement("mine", "prototype-mine", 100), Requirement("community", "prototype-community", 110),
            Requirement("lodge", "prototype-lodge", 120)
        };
        var needs = requirements.Select(x => new CustomerNeed("need-" + x.Id[12..], x.DestinationLocationId, x.ResourceId, 5)).ToArray();
        var jobs = requirements.Select(x => new EconomyJob("job-" + x.Id[12..], x.Id, "prototype-system-cargo", "prototype-player",
            "shipment-" + x.Id[12..], EconomyJobState.Offered)).ToArray();
        return new EconomyWorld("prototype-world-v0", 1, locations,
            [new("prototype-system-cargo", "Prototype System Cargo Employer", EconomicOwnerKind.System),
             new("prototype-customer-funder", "Prototype Customer Funding Account", EconomicOwnerKind.System)],
            [new("prototype-general-supplies", "Prototype General Supplies", "general-supplies", "cargo unit")],
            [new("prototype-gateway", "prototype-general-supplies", 20),
             new("prototype-mine", "prototype-general-supplies", 0),
             new("prototype-community", "prototype-general-supplies", 0),
             new("prototype-lodge", "prototype-general-supplies", 0)],
            needs, requirements, [new("prototype-player", "prototype-system-cargo", Currency, 0)], jobs, [], []).EnsureValid();
    }

    private static TransportRequirement Requirement(string suffix, string destination, decimal payment) =>
        new("requirement-" + suffix, "need-" + suffix, "prototype-gateway", destination, "prototype-general-supplies", 2,
            payment, Currency, "prototype-customer-funder", TransportRequirementState.Open);
}
