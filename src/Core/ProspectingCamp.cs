namespace FrontierAirportBuilder.Core;

public sealed record OperationStorageDefinition(string ResourceId, decimal Capacity, decimal InitialQuantity);
public sealed record OperationProcessDefinition(string InputResourceId, decimal InputPerTick,
    string OutputResourceId, decimal OutputPerTick);
public sealed record OperationLogisticsDefinition(decimal ResupplyTrigger, decimal InboundQuantity,
    decimal InboundPayment, decimal OutboundQuantity, decimal OutboundPayment);
public sealed record OperationArchetypeDefinition(string Id, string DisplayName, ConstructionCapability RequiredCapability,
    decimal FoundingCost, string CurrencyCode, IReadOnlyList<OperationStorageDefinition> Storage,
    OperationProcessDefinition Process, OperationLogisticsDefinition Logistics);

public static class OperationArchetypeCatalogue
{
    public const string ProspectingCampId = "operation-archetype.prospecting-camp-v0";
    public const string RationsResourceId = "rations";
    public const string RawMineralsResourceId = "raw-minerals";
    public static readonly OperationArchetypeDefinition ProspectingCamp = new(
        ProspectingCampId, "Prospecting Camp", ConstructionCapability.BaseLodge, 150m, EmployedCargoPrototype.Currency,
        [new(RationsResourceId, 70m, 70m), new(RawMineralsResourceId, 140m, 0m)],
        new(RationsResourceId, 10m, RawMineralsResourceId, 20m),
        new(30m, 40m, 80m, 40m, 160m));
    public static IReadOnlyList<OperationArchetypeDefinition> All { get; } = [ProspectingCamp];
    static OperationArchetypeCatalogue()
    {
        EconomyValidation.Unique(All.Select(x => x.Id), "operation archetype");
        foreach (var item in All)
        {
            EconomyValidation.Id(item.Id, "operation archetype ID"); EconomyValidation.Text(item.DisplayName, "operation archetype name", 96);
            EconomyValidation.Positive(item.FoundingCost, "operation founding cost"); EconomyValidation.Currency(item.CurrencyCode);
            if (item.Storage is null || item.Storage.Count == 0) throw new InvalidDataException("Operation storage configuration is missing.");
            EconomyValidation.Unique(item.Storage.Select(x => x.ResourceId), "operation storage resource");
            foreach (var storage in item.Storage) { EconomyValidation.Id(storage.ResourceId, "operation storage resource"); EconomyValidation.Positive(storage.Capacity, "operation storage capacity"); EconomyValidation.NonNegative(storage.InitialQuantity, "operation initial stock"); if (storage.InitialQuantity > storage.Capacity) throw new InvalidDataException("Operation initial stock exceeds capacity."); }
            if (!item.Storage.Any(x => x.ResourceId == item.Process.InputResourceId) || !item.Storage.Any(x => x.ResourceId == item.Process.OutputResourceId))
                throw new InvalidDataException("Operation process resources require configured storage.");
            EconomyValidation.Positive(item.Process.InputPerTick, "operation tick input"); EconomyValidation.Positive(item.Process.OutputPerTick, "operation tick output");
            EconomyValidation.NonNegative(item.Logistics.ResupplyTrigger, "operation resupply trigger"); EconomyValidation.Positive(item.Logistics.InboundQuantity, "operation inbound quantity");
            EconomyValidation.Positive(item.Logistics.OutboundQuantity, "operation outbound quantity"); EconomyValidation.NonNegative(item.Logistics.InboundPayment, "operation inbound payment"); EconomyValidation.NonNegative(item.Logistics.OutboundPayment, "operation outbound payment");
        }
    }
    public static OperationArchetypeDefinition Get(string id) => All.SingleOrDefault(x => x.Id == id) ??
        throw new InvalidDataException("Unknown economic-operation archetype.");
}

public sealed record EconomicSite(string Id, string AirportDefinitionId, string AirportIdentifier, string Name,
    string LocationId, string OwnerId, double Latitude, double Longitude, DateTimeOffset FoundedUtc,
    VerifiedSitePresenceEvidence FoundingPresence);
public sealed record EconomicOperation(string Id, string SiteId, string ArchetypeId, string OwnerId,
    int PrototypeDay, string Status, string LastConstraint);
public sealed record OperationTickRecord(string Id, string CommandId, string OperationId, int PrototypeDay,
    decimal InputConsumed, decimal OutputProduced, string Result, DateTimeOffset OccurredUtc);

public sealed record AirportSiteCandidate(string AirportDefinitionId, string Identifier, string Name,
    double Latitude, double Longitude, bool HasRunway, IReadOnlyList<ConstructionCapability> Capabilities)
{
    public AirportSiteCandidate EnsureValid()
    {
        if (!Guid.TryParseExact(AirportDefinitionId, "N", out _)) throw new InvalidDataException("Airport definition ID is invalid.");
        EconomyValidation.Text(Name, "site name", 96);
        if (string.IsNullOrWhiteSpace(Identifier) || Identifier.Length is < 3 or > 8) throw new InvalidDataException("Airport identifier is invalid.");
        if (!double.IsFinite(Latitude) || Latitude is < -90 or > 90 || !double.IsFinite(Longitude) || Longitude is < -180 or > 180)
            throw new InvalidDataException("Airport anchor is invalid.");
        if (Capabilities is null || Capabilities.Distinct().Count() != Capabilities.Count) throw new InvalidDataException("Airport construction capabilities are invalid.");
        return this;
    }
}

/// <summary>Fresh live-avatar proximity proof produced by the guarded tracking/presence service.
/// Aircraft, camera and saved positions are not valid providers.</summary>
public sealed record VerifiedSitePresenceEvidence(string Id, string Provider, string AirportDefinitionId,
    uint AvatarObjectId, double AvatarLatitude, double AvatarLongitude, double DistanceMeters,
    double AllowedRadiusMeters, DateTimeOffset ObservedUtc, DateTimeOffset VerifiedUtc,
    string IntegrationDetails = "guarded explicit avatar proxy")
{
    public VerifiedSitePresenceEvidence EnsureValid()
    {
        EconomyValidation.Id(Id, "site-presence evidence ID"); EconomyValidation.Text(Provider, "site-presence provider", 64);
        if (!Guid.TryParseExact(AirportDefinitionId, "N", out _) || AvatarObjectId == 0 ||
            !double.IsFinite(AvatarLatitude) || AvatarLatitude is < -90 or > 90 ||
            !double.IsFinite(AvatarLongitude) || AvatarLongitude is < -180 or > 180 ||
            !double.IsFinite(DistanceMeters) || DistanceMeters < 0 ||
            !double.IsFinite(AllowedRadiusMeters) || AllowedRadiusMeters <= 0 || DistanceMeters > AllowedRadiusMeters ||
            ObservedUtc == default || VerifiedUtc == default || VerifiedUtc < ObservedUtc || VerifiedUtc - ObservedUtc > TimeSpan.FromSeconds(3))
            throw new InvalidDataException("Site-presence evidence is invalid, stale or outside the allowed radius.");
        EconomyValidation.Text(IntegrationDetails, "site-presence integration details", 512);
        return this;
    }
}

public static class ProspectingCampKernel
{
    public static EconomyTransition Found(EconomyWorld source, AirportSiteCandidate supplied,
        VerifiedSitePresenceEvidence suppliedEvidence, string commandId, DateTimeOffset occurredUtc)
    {
        var world = source.EnsureValid(); var candidate = supplied.EnsureValid(); EconomyValidation.Id(commandId, nameof(commandId));
        if (occurredUtc == default) throw new ArgumentOutOfRangeException(nameof(occurredUtc));
        var existing = world.Sites.SingleOrDefault(x => x.AirportDefinitionId == candidate.AirportDefinitionId);
        if (existing is not null) return new(world, false);
        if (!candidate.HasRunway) throw new InvalidOperationException("The saved player-created airport requires a runway before a prospecting camp can be founded.");
        if (!candidate.Capabilities.Contains(OperationArchetypeCatalogue.ProspectingCamp.RequiredCapability))
            throw new InvalidOperationException("Place and save a construction object with the Base Lodge capability before founding.");
        var evidence = suppliedEvidence.EnsureValid();
        if (evidence.AirportDefinitionId != candidate.AirportDefinitionId)
            throw new InvalidOperationException("Live avatar presence was verified for a different airport.");
        var measuredDistance = WorldTracking.DistanceMeters(evidence.AvatarLatitude, evidence.AvatarLongitude, candidate.Latitude, candidate.Longitude);
        if (Math.Abs(measuredDistance - evidence.DistanceMeters) > 1 || measuredDistance > evidence.AllowedRadiusMeters)
            throw new InvalidOperationException("Site-presence evidence does not agree with the airport anchor.");
        if (occurredUtc < evidence.VerifiedUtc || occurredUtc - evidence.ObservedUtc > TimeSpan.FromSeconds(3))
            throw new InvalidOperationException("Live avatar presence is no longer fresh.");
        var player = world.Players.Single(x => x.Id == EmployedCargoPrototype.PlayerId);
        var archetype = OperationArchetypeCatalogue.ProspectingCamp;
        if (EconomyLedger.Balance(world, player.Id, player.CurrencyCode) < archetype.FoundingCost)
            throw new InvalidOperationException($"Founding requires {archetype.FoundingCost:0} {archetype.CurrencyCode} from the authoritative ledger balance.");

        var stem = candidate.AirportDefinitionId;
        var siteId = EconomyIds.Derive("site-", stem); var locationId = EconomyIds.Derive("operation-location-", stem);
        var operationId = EconomyIds.Derive("operation-", stem);
        var site = new EconomicSite(siteId, stem, candidate.Identifier, candidate.Name, locationId, player.Id,
            candidate.Latitude, candidate.Longitude, occurredUtc, evidence);
        var operation = new EconomicOperation(operationId, siteId, archetype.Id, player.Id, 0, "Operating", "None");
        var resources = world.Resources.ToList();
        AddResource(resources, new(OperationArchetypeCatalogue.RationsResourceId, "Rations", "operation-supplies", "kg"));
        AddResource(resources, new(OperationArchetypeCatalogue.RawMineralsResourceId, "Raw Minerals", "unprocessed-output", "kg"));
        var locations = world.Locations.Append(new EconomicLocation(locationId, candidate.Name + " Prospecting Camp",
            EconomicLocationKind.OperationSite, EconomicOwnerKind.Player, EconomicLocationStatus.ActiveOperation)).ToArray();
        var inventories = world.Inventories.ToList(); var inventoryLedger = world.InventoryLedger.ToList();
        foreach (var storage in archetype.Storage)
        {
            inventories.Add(new(locationId, storage.ResourceId, storage.InitialQuantity));
            if (storage.InitialQuantity > 0) inventoryLedger.Add(new(
                EconomyIds.Derive("inventory-operation-opening-", operationId + "-" + storage.ResourceId),
                InventoryTransactionKind.OperationInitialStock, locationId, storage.ResourceId, storage.InitialQuantity,
                null, occurredUtc, "Temporary Prospecting Camp V0 opening stock.", operationId, commandId));
        }
        // Keep the gateway side of operation cargo explicit even when currently zero.
        EnsureInventory(inventories, EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RationsResourceId);
        EnsureInventory(inventories, EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RawMineralsResourceId);
        var needs = world.Needs.Append(new CustomerNeed(InboundNeed(operationId), locationId,
            archetype.Process.InputResourceId, archetype.Logistics.InboundQuantity))
            .Append(new CustomerNeed(OutboundNeed(operationId), EmployedCargoPrototype.ErosId,
                archetype.Process.OutputResourceId, archetype.Logistics.OutboundQuantity)).ToArray();
        var founding = new FinancialTransaction(EconomyIds.Derive("founding-", siteId), FinancialTransactionKind.SiteFounding,
            null, evidence.Id, player.Id, EmployedCargoPrototype.EmployerId, archetype.FoundingCost,
            archetype.CurrencyCode, occurredUtc, siteId);
        var financial = world.FinancialLedger.Append(founding).ToArray();
        var interim = world with { Locations = locations, Resources = resources, Inventories = inventories,
            Needs = needs, FinancialLedger = financial, InventoryLedger = inventoryLedger,
            Sites = world.Sites.Append(site).ToArray(), Operations = world.Operations.Append(operation).ToArray() };
        var players = interim.Players.Select(x => x.Id == player.Id
            ? x with { CachedBalance = EconomyLedger.Balance(interim, x.Id, x.CurrencyCode) } : x).ToArray();
        interim = interim with { Players = players };
        var maintained = OperationJobBoard.Maintain(interim, occurredUtc, false);
        return new((maintained.World with { Revision = world.Revision + 1 }).EnsureValid(), true);
    }

    public static EconomyTransition AdvanceDay(EconomyWorld source, string operationId, string commandId, DateTimeOffset occurredUtc)
    {
        var world = source.EnsureValid(); EconomyValidation.Id(operationId, nameof(operationId)); EconomyValidation.Id(commandId, nameof(commandId));
        if (occurredUtc == default) throw new ArgumentOutOfRangeException(nameof(occurredUtc));
        if (world.OperationTicks.SingleOrDefault(x => x.CommandId == commandId) is { } prior)
        {
            if (prior.OperationId != operationId) throw new InvalidOperationException("Prototype-day command was already used for a different operation.");
            return new(world, false);
        }
        var operation = world.Operations.SingleOrDefault(x => x.Id == operationId) ?? throw new InvalidOperationException("Economic operation does not exist.");
        var site = world.Sites.Single(x => x.Id == operation.SiteId); var archetype = OperationArchetypeCatalogue.Get(operation.ArchetypeId);
        var input = world.Inventories.Single(x => x.LocationId == site.LocationId && x.ResourceId == archetype.Process.InputResourceId);
        var output = world.Inventories.Single(x => x.LocationId == site.LocationId && x.ResourceId == archetype.Process.OutputResourceId);
        decimal consumed = 0, produced = 0; string result;
        if (input.AvailableQuantity < archetype.Process.InputPerTick)
            result = $"Stopped: requires {archetype.Process.InputPerTick:0.##} kg {ResourceName(world, archetype.Process.InputResourceId)}; no partial ration batch was consumed.";
        else
        {
            consumed = archetype.Process.InputPerTick;
            var capacity = archetype.Storage.Single(x => x.ResourceId == archetype.Process.OutputResourceId).Capacity;
            produced = Math.Min(archetype.Process.OutputPerTick, Math.Max(0, capacity - output.AvailableQuantity));
            result = produced == archetype.Process.OutputPerTick ? "Supplied day completed at the configured prototype output." :
                $"Storage constrained production to {produced:0.##} kg of the configured {archetype.Process.OutputPerTick:0.##} kg batch.";
        }
        var inventories = world.Inventories.Select(x => x == input ? x with { AvailableQuantity = x.AvailableQuantity - consumed } :
            x == output ? x with { AvailableQuantity = x.AvailableQuantity + produced } : x).ToArray();
        var ledger = world.InventoryLedger.ToList();
        if (consumed > 0) ledger.Add(new(EconomyIds.Derive("inventory-consume-", operationId + "-" + commandId),
            InventoryTransactionKind.OperationConsumption, site.LocationId, input.ResourceId, -consumed, null, occurredUtc,
            "Full prototype-day input batch consumed.", operationId, commandId));
        if (produced > 0) ledger.Add(new(EconomyIds.Derive("inventory-produce-", operationId + "-" + commandId),
            InventoryTransactionKind.OperationProduction, site.LocationId, output.ResourceId, produced, null, occurredUtc,
            "Prototype-day output produced within storage capacity.", operationId, commandId));
        var day = operation.PrototypeDay + 1;
        var tick = new OperationTickRecord(EconomyIds.Derive("operation-tick-", operationId + "-" + commandId), commandId,
            operationId, day, consumed, produced, result, occurredUtc);
        var operations = world.Operations.Select(x => x.Id == operationId ? x with
        { PrototypeDay = day, Status = consumed > 0 ? "Operating" : "Supply constrained", LastConstraint = result } : x).ToArray();
        var interim = world with { Inventories = inventories, InventoryLedger = ledger, Operations = operations,
            OperationTicks = world.OperationTicks.Append(tick).ToArray() };
        var maintained = OperationJobBoard.Maintain(interim, occurredUtc, false);
        return new((maintained.World with { Revision = world.Revision + 1 }).EnsureValid(), true);
    }

    public static string InboundNeed(string operationId) => EconomyIds.Derive("need-operation-rations-", operationId);
    public static string OutboundNeed(string operationId) => EconomyIds.Derive("need-operation-minerals-", operationId);
    private static string ResourceName(EconomyWorld world, string id) => world.Resources.Single(x => x.Id == id).Name;
    private static void AddResource(List<ResourceType> values, ResourceType resource)
    { if (!values.Any(x => x.Id == resource.Id)) values.Add(resource); }
    internal static void EnsureInventory(List<InventoryBalance> values, string locationId, string resourceId)
    { if (!values.Any(x => x.LocationId == locationId && x.ResourceId == resourceId)) values.Add(new(locationId, resourceId, 0)); }
}

public static class OperationJobBoard
{
    public static EconomyTransition Maintain(EconomyWorld source, DateTimeOffset occurredUtc, bool incrementRevision = true)
    {
        var world = source.EnsureValid(); if (occurredUtc == default) throw new ArgumentOutOfRangeException(nameof(occurredUtc));
        if (world.Operations.Count == 0) return new(world, false);
        var requirements = world.Requirements.ToList(); var jobs = world.Jobs.ToList(); var inventories = world.Inventories.ToList();
        var inventoryLedger = world.InventoryLedger.ToList(); bool changed = false;
        foreach (var operation in world.Operations.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var site = world.Sites.Single(x => x.Id == operation.SiteId); var a = OperationArchetypeCatalogue.Get(operation.ArchetypeId);
            var rations = inventories.Single(x => x.LocationId == site.LocationId && x.ResourceId == a.Process.InputResourceId);
            var minerals = inventories.Single(x => x.LocationId == site.LocationId && x.ResourceId == a.Process.OutputResourceId);
            if (rations.AvailableQuantity <= a.Logistics.ResupplyTrigger && !Outstanding(ProspectingCampKernel.InboundNeed(operation.Id)))
            {
                var capacity = a.Storage.Single(x => x.ResourceId == a.Process.InputResourceId).Capacity;
                var quantity = Math.Min(a.Logistics.InboundQuantity, Math.Max(0, capacity - rations.AvailableQuantity));
                if (quantity > 0)
                {
                    var ids = Ids(operation.Id, "rations", ProspectingCampKernel.InboundNeed(operation.Id));
                    var gatewayIndex = inventories.FindIndex(x => x.LocationId == EmployedCargoPrototype.ErosId && x.ResourceId == a.Process.InputResourceId);
                    if (gatewayIndex < 0) { inventories.Add(new(EmployedCargoPrototype.ErosId, a.Process.InputResourceId, 0)); gatewayIndex = inventories.Count - 1; }
                    var promised = OfferedAtOrigin(EmployedCargoPrototype.ErosId, a.Process.InputResourceId);
                    var shortfall = Math.Max(0, promised + quantity - inventories[gatewayIndex].AvailableQuantity);
                    if (shortfall > 0)
                    {
                        inventories[gatewayIndex] = inventories[gatewayIndex] with { AvailableQuantity = inventories[gatewayIndex].AvailableQuantity + shortfall };
                        inventoryLedger.Add(new(EconomyIds.Derive("inventory-gateway-", ids.Shipment), InventoryTransactionKind.SystemGatewaySupply,
                            EmployedCargoPrototype.ErosId, a.Process.InputResourceId, shortfall, null, occurredUtc,
                            "System Gateway Supply for temporary frontier-support rations."));
                    }
                    Add(ids, ProspectingCampKernel.InboundNeed(operation.Id), EmployedCargoPrototype.ErosId, site.LocationId,
                        a.Process.InputResourceId, quantity, a.Logistics.InboundPayment,
                        "Temporary frontier-support transport of rations to the prospecting camp.");
                }
            }
            if (minerals.AvailableQuantity >= a.Logistics.OutboundQuantity && !Outstanding(ProspectingCampKernel.OutboundNeed(operation.Id)))
            {
                var ids = Ids(operation.Id, "minerals", ProspectingCampKernel.OutboundNeed(operation.Id));
                Add(ids, ProspectingCampKernel.OutboundNeed(operation.Id), site.LocationId, EmployedCargoPrototype.ErosId,
                    a.Process.OutputResourceId, a.Logistics.OutboundQuantity, a.Logistics.OutboundPayment,
                    "Temporary combined mineral-sale proceeds for transporting raw minerals to Eros.");
            }
        }
        if (!changed) return new(world, false);
        var next = world with { Revision = incrementRevision ? world.Revision + 1 : world.Revision,
            Requirements = requirements, Jobs = jobs, Inventories = inventories, InventoryLedger = inventoryLedger };
        return new(next.EnsureValid(), true);

        bool Outstanding(string needId) => requirements.Any(x => x.NeedId == needId && x.State is TransportRequirementState.Open or TransportRequirementState.Reserved);
        decimal OfferedAtOrigin(string origin, string resource) => jobs.Where(x => x.State == EconomyJobState.Offered)
            .Select(x => requirements.Single(r => r.Id == x.RequirementId)).Where(x => x.OriginLocationId == origin && x.ResourceId == resource).Sum(x => x.Quantity);
        (string Requirement, string Job, string Shipment) Ids(string operationId, string kind, string needId)
        {
            var cycle = requirements.Count(x => x.NeedId == needId) + 1; var suffix = operationId + "-" + kind + $"-{cycle:D6}";
            return (EconomyIds.Derive("requirement-", suffix), EconomyIds.Derive("job-", suffix), EconomyIds.Derive("shipment-", suffix));
        }
        void Add((string Requirement, string Job, string Shipment) ids, string need, string origin, string destination,
            string resource, decimal quantity, decimal payment, string purpose)
        {
            if (requirements.Any(x => x.Id == ids.Requirement) || jobs.Any(x => x.Id == ids.Job || x.ShipmentId == ids.Shipment))
                throw new InvalidDataException("Deterministic operation-job identifier collides with existing economy data.");
            requirements.Add(new(ids.Requirement, need, origin, destination, resource, quantity, payment,
                EmployedCargoPrototype.Currency, EmployedCargoPrototype.EmployerId, TransportRequirementState.Open) { Purpose = purpose });
            jobs.Add(new(ids.Job, ids.Requirement, EmployedCargoPrototype.EmployerId, EmployedCargoPrototype.PlayerId,
                ids.Shipment, EconomyJobState.Offered)); changed = true;
        }
    }
}

internal static class ProspectingCampValidation
{
    public static void EnsureValid(EconomyWorld world)
    {
        foreach (var site in world.Sites)
        {
            EconomyValidation.Id(site.Id, "site ID"); EconomyValidation.Id(site.LocationId, "site location ID"); EconomyValidation.Id(site.OwnerId, "site owner ID");
            EconomyValidation.Text(site.Name, "site name", 96); EconomyValidation.Text(site.AirportIdentifier, "site airport identifier", 8);
            if (!Guid.TryParseExact(site.AirportDefinitionId, "N", out _) || site.FoundedUtc == default ||
                !double.IsFinite(site.Latitude) || site.Latitude is < -90 or > 90 || !double.IsFinite(site.Longitude) || site.Longitude is < -180 or > 180)
                throw new InvalidDataException("Economic site metadata is invalid.");
            var presence = site.FoundingPresence?.EnsureValid() ?? throw new InvalidDataException("Economic site founding presence is missing.");
            if (presence.AirportDefinitionId != site.AirportDefinitionId || presence.VerifiedUtc > site.FoundedUtc)
                throw new InvalidDataException("Economic site founding presence does not match the site.");
            var location = world.Locations.SingleOrDefault(x => x.Id == site.LocationId);
            if (location is null || location.Kind != EconomicLocationKind.OperationSite || location.Owner != EconomicOwnerKind.Player || location.Status != EconomicLocationStatus.ActiveOperation)
                throw new InvalidDataException("Economic site location is inconsistent.");
            if (!world.Players.Any(x => x.Id == site.OwnerId)) throw new InvalidDataException("Economic site owner is unknown.");
            var siteOperation = world.Operations.SingleOrDefault(x => x.SiteId == site.Id)
                ?? throw new InvalidDataException("Economic site has no operation.");
            var archetype = OperationArchetypeCatalogue.Get(siteOperation.ArchetypeId);
            var founding = world.FinancialLedger.Where(x => x.Kind == FinancialTransactionKind.SiteFounding && x.SiteId == site.Id).ToArray();
            if (founding.Length != 1 || founding[0].FromEntityId != site.OwnerId || founding[0].Amount != archetype.FoundingCost)
                throw new InvalidDataException("Economic site and founding debit are inconsistent.");
        }
        foreach (var operation in world.Operations)
        {
            EconomyValidation.Id(operation.Id, "operation ID"); EconomyValidation.Id(operation.SiteId, "operation site ID"); EconomyValidation.Id(operation.OwnerId, "operation owner ID");
            EconomyValidation.Text(operation.Status, "operation status", 96); EconomyValidation.Text(operation.LastConstraint, "operation constraint", 256);
            var site = world.Sites.SingleOrDefault(x => x.Id == operation.SiteId) ?? throw new InvalidDataException("Operation site is unknown.");
            if (site.OwnerId != operation.OwnerId || operation.PrototypeDay < 0) throw new InvalidDataException("Operation ownership or day is invalid.");
            var archetype = OperationArchetypeCatalogue.Get(operation.ArchetypeId);
            foreach (var storage in archetype.Storage)
            {
                var balance = world.Inventories.SingleOrDefault(x => x.LocationId == site.LocationId && x.ResourceId == storage.ResourceId)
                    ?? throw new InvalidDataException("Operation inventory position is missing.");
                if (balance.AvailableQuantity > storage.Capacity) throw new InvalidDataException("Operation inventory exceeds configured storage capacity.");
            }
            if (world.OperationTicks.Count(x => x.OperationId == operation.Id) != operation.PrototypeDay)
                throw new InvalidDataException("Operation day counter and tick ledger disagree.");
        }
        foreach (var tick in world.OperationTicks)
        {
            EconomyValidation.Id(tick.Id, "operation tick ID"); EconomyValidation.Id(tick.CommandId, "operation command ID"); EconomyValidation.Id(tick.OperationId, "operation tick operation ID");
            EconomyValidation.Text(tick.Result, "operation tick result", 512);
            if (!world.Operations.Any(x => x.Id == tick.OperationId) || tick.PrototypeDay < 1 || tick.InputConsumed < 0 || tick.OutputProduced < 0 || tick.OccurredUtc == default)
                throw new InvalidDataException("Operation tick is invalid.");
        }
        if (world.Sites.Count == 0) return;
        foreach (var balance in world.Inventories)
        {
            var audited = world.InventoryLedger.Where(x => x.LocationId == balance.LocationId && x.ResourceId == balance.ResourceId).Sum(x => x.QuantityDelta);
            if (audited != balance.AvailableQuantity) throw new InvalidDataException("Cached inventory balance disagrees with the authoritative inventory audit ledger.");
        }
    }
}
