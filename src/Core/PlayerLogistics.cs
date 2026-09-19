namespace FrontierAirportBuilder.Core;

public enum OperationLogisticsDirection { Import, Export }
public enum OperationLogisticsContractState { Published, Accepted, RoadInTransit, Completed, Cancelled, Forfeited }
public enum FinancialReservationState { Active, Settled, Released }

public sealed record OperationLogisticsContract(string Id, string CommandId, string OperationId, string OwnerPlayerId,
    string JobId, string RequirementId, string ShipmentId, OperationLogisticsDirection Direction,
    string OriginLocationId, string DestinationLocationId, string ResourceId, decimal Quantity,
    decimal UnitPrice, decimal CargoValue, decimal PilotPayout, int? RoadFallbackDelayMinutes,
    decimal RoadCost, int RoadDurationMinutes, decimal ReservedFunds, OperationLogisticsContractState State,
    DateTimeOffset CreatedUtc, DateTimeOffset? FallbackAtUtc = null, string? AcceptedPilotId = null,
    DateTimeOffset? AcceptedUtc = null, DateTimeOffset? RoadStartedUtc = null, DateTimeOffset? RoadEtaUtc = null,
    DateTimeOffset? CompletedUtc = null, DateTimeOffset? CancelledUtc = null);

public sealed record FinancialReservation(string Id, string ContractId, string OwnerEntityId, decimal Amount,
    string CurrencyCode, FinancialReservationState State, DateTimeOffset CreatedUtc, DateTimeOffset? ClosedUtc = null);

public sealed record GatewayResourcePrice(string GatewayLocationId, string ResourceId,
    decimal? SellPricePerUnit, decimal? BuyPricePerUnit, string CurrencyCode);

public static class GatewayPriceCatalogue
{
    public static IReadOnlyList<GatewayResourcePrice> All { get; } =
    [
        new(EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RationsResourceId, .5m, null, EmployedCargoPrototype.Currency),
        new(EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RawMineralsResourceId, null, .8m, EmployedCargoPrototype.Currency)
    ];
    public static GatewayResourcePrice Quote(string gatewayId, string resourceId) => All.SingleOrDefault(x =>
        x.GatewayLocationId == gatewayId && x.ResourceId == resourceId) ??
        throw new InvalidOperationException("The gateway has no prototype price for that resource.");
}

public sealed record RoadLogisticsQuote(decimal Cost, int DurationMinutes);
public static class RoadLogisticsPrototype
{
    public static RoadLogisticsQuote Quote(double straightLineDistanceKm)
    {
        if (!double.IsFinite(straightLineDistanceKm) || straightLineDistanceKm < 0) throw new ArgumentOutOfRangeException(nameof(straightLineDistanceKm));
        // Deliberately replaceable V0 approximation: road distance 1.35x straight-line, 55 km/h, 0.18 TPC/km.
        var roadKm = Math.Max(10d, straightLineDistanceKm * 1.35d);
        return new(Math.Round((decimal)roadKm * .18m, 2), Math.Max(15, (int)Math.Ceiling(roadKm / 55d * 60d)));
    }
}

public sealed record PublishOperationLogisticsRequest(string CommandId, string OperationId,
    OperationLogisticsDirection Direction, string GatewayLocationId, string ResourceId, decimal Quantity,
    decimal PilotPayout, int? RoadFallbackDelayMinutes, decimal RoadCost, int RoadDurationMinutes,
    DateTimeOffset OccurredUtc);

public static class PlayerLogisticsKernel
{
    public const string GatewayMarketBusinessId = "player-logistics-gateway-market";
    public const string RoadCarrierBusinessId = "player-logistics-road-carrier";

    public static decimal ReservedFunds(EconomyWorld world, string ownerId, string currency) => world.FinancialReservations
        .Where(x => x.OwnerEntityId == ownerId && x.CurrencyCode == currency && x.State == FinancialReservationState.Active).Sum(x => x.Amount);
    public static decimal AvailableFunds(EconomyWorld world, string ownerId, string currency) =>
        EconomyLedger.Balance(world, ownerId, currency) - ReservedFunds(world, ownerId, currency);

    public static EconomyTransition Publish(EconomyWorld source, PublishOperationLogisticsRequest request)
    {
        var world = source.EnsureValid();
        EconomyValidation.Id(request.CommandId, nameof(request.CommandId)); EconomyValidation.Id(request.OperationId, nameof(request.OperationId));
        EconomyValidation.Positive(request.Quantity, nameof(request.Quantity)); EconomyValidation.NonNegative(request.PilotPayout, nameof(request.PilotPayout));
        EconomyValidation.NonNegative(request.RoadCost, nameof(request.RoadCost));
        if (request.OccurredUtc == default || request.RoadDurationMinutes < 1 || request.RoadFallbackDelayMinutes is < 0)
            throw new InvalidDataException("Logistics timing is invalid.");
        var existing = world.LogisticsContracts.SingleOrDefault(x => x.CommandId == request.CommandId);
        if (existing is not null) return new(world, false);
        var operation = world.Operations.SingleOrDefault(x => x.Id == request.OperationId) ?? throw new InvalidOperationException("Operation does not exist.");
        var site = world.Sites.Single(x => x.Id == operation.SiteId);
        if (operation.OwnerId != site.OwnerId) throw new InvalidDataException("Operation ownership is inconsistent.");
        if (!world.Locations.Any(x => x.Id == request.GatewayLocationId && x.Kind == EconomicLocationKind.Gateway))
            throw new InvalidOperationException("Selected source or destination is not a gateway.");
        var price = GatewayPriceCatalogue.Quote(request.GatewayLocationId, request.ResourceId);
        var unitPrice = request.Direction == OperationLogisticsDirection.Import
            ? price.SellPricePerUnit ?? throw new InvalidOperationException("The gateway does not sell this resource.")
            : price.BuyPricePerUnit ?? throw new InvalidOperationException("The gateway does not buy this resource.");
        var origin = request.Direction == OperationLogisticsDirection.Import ? request.GatewayLocationId : site.LocationId;
        var destination = request.Direction == OperationLogisticsDirection.Import ? site.LocationId : request.GatewayLocationId;
        var inventoryIndex = world.Inventories.ToList().FindIndex(x => x.LocationId == origin && x.ResourceId == request.ResourceId);
        if (inventoryIndex < 0 || world.Inventories[inventoryIndex].AvailableQuantity < request.Quantity)
            throw new InvalidOperationException("The origin does not contain enough unreserved inventory.");
        var destinationCapacity = Capacity(world, destination, request.ResourceId);
        var destinationQuantity = world.Inventories.SingleOrDefault(x => x.LocationId == destination && x.ResourceId == request.ResourceId)?.AvailableQuantity ?? 0;
        var incoming = world.LogisticsContracts.Where(x => x.DestinationLocationId == destination && x.ResourceId == request.ResourceId &&
            x.State is OperationLogisticsContractState.Published or OperationLogisticsContractState.Accepted or OperationLogisticsContractState.RoadInTransit).Sum(x => x.Quantity);
        if (destinationCapacity is not null && destinationQuantity + incoming + request.Quantity > destinationCapacity)
            throw new InvalidOperationException("The destination does not have enough storage capacity.");

        var cargoValue = Math.Round(request.Quantity * unitPrice, 2);
        var serviceReserve = request.RoadFallbackDelayMinutes is null ? request.PilotPayout : Math.Max(request.PilotPayout, request.RoadCost);
        var requiredFunds = serviceReserve + (request.Direction == OperationLogisticsDirection.Import ? cargoValue : 0);
        if (AvailableFunds(world, operation.OwnerId, price.CurrencyCode) < requiredFunds)
            throw new InvalidOperationException("Available ledger-derived funds cannot cover this contract and its fallback.");
        var stem = EconomyIds.Derive("player-logistics-", request.CommandId);
        var contractId = stem; var needId = EconomyIds.Derive("need-", stem); var requirementId = EconomyIds.Derive("requirement-", stem);
        var jobId = EconomyIds.Derive("job-", stem); var shipmentId = EconomyIds.Derive("shipment-", stem);
        var inventories = world.Inventories.ToArray(); inventories[inventoryIndex] = inventories[inventoryIndex] with
        { AvailableQuantity = inventories[inventoryIndex].AvailableQuantity - request.Quantity };
        var directionText = request.Direction == OperationLogisticsDirection.Import ? "Import" : "Export";
        var contract = new OperationLogisticsContract(contractId, request.CommandId, operation.Id, operation.OwnerId,
            jobId, requirementId, shipmentId, request.Direction, origin, destination, request.ResourceId, request.Quantity,
            unitPrice, cargoValue, request.PilotPayout, request.RoadFallbackDelayMinutes, request.RoadCost,
            request.RoadDurationMinutes, requiredFunds, OperationLogisticsContractState.Published, request.OccurredUtc,
            request.RoadFallbackDelayMinutes is null ? null : request.OccurredUtc.AddMinutes(request.RoadFallbackDelayMinutes.Value));
        var businesses = world.Businesses.ToList();
        EnsureBusiness(businesses, operation.OwnerId, "Player operation owner", EconomicOwnerKind.Player);
        EnsureBusiness(businesses, GatewayMarketBusinessId, "Gateway market", EconomicOwnerKind.System);
        EnsureBusiness(businesses, RoadCarrierBusinessId, "Prototype road carrier", EconomicOwnerKind.System);
        var audit = new InventoryTransaction(EconomyIds.Derive("inventory-contract-reserve-", contractId), InventoryTransactionKind.PlayerContractReserved,
            origin, request.ResourceId, -request.Quantity, jobId, request.OccurredUtc, $"{directionText} contract cargo reserved at publication.", operation.Id, request.CommandId);
        var need = new CustomerNeed(needId, destination, request.ResourceId, request.Quantity);
        var requirement = new TransportRequirement(requirementId, needId, origin, destination, request.ResourceId,
            request.Quantity, request.PilotPayout, price.CurrencyCode, operation.OwnerId, TransportRequirementState.Open)
        { Purpose = $"PLAYER OPERATION {directionText.ToUpperInvariant()} · {site.Name}" };
        var job = new EconomyJob(jobId, requirementId, operation.OwnerId, EmployedCargoPrototype.PlayerId, shipmentId, EconomyJobState.Offered);
        var reservation = new FinancialReservation(EconomyIds.Derive("financial-reservation-", contractId), contractId,
            operation.OwnerId, requiredFunds, price.CurrencyCode, FinancialReservationState.Active, request.OccurredUtc);
        var next = world with { Revision = world.Revision + 1, Businesses = businesses, Inventories = inventories,
            InventoryLedger = [..world.InventoryLedger, audit], Needs = [..world.Needs, need], Requirements = [..world.Requirements, requirement],
            Jobs = [..world.Jobs, job], LogisticsContracts = [..world.LogisticsContracts, contract], FinancialReservations = [..world.FinancialReservations, reservation] };
        return new(next.EnsureValid(), true);
    }

    public static EconomyTransition EditPayout(EconomyWorld source, string contractId, decimal payout, DateTimeOffset occurredUtc)
    {
        var current = source.EnsureValid().LogisticsContracts.SingleOrDefault(x => x.Id == contractId) ?? throw new InvalidOperationException("Contract does not exist.");
        return EditPublishedTerms(source, contractId, payout, current.RoadFallbackDelayMinutes, occurredUtc);
    }

    public static EconomyTransition EditPublishedTerms(EconomyWorld source, string contractId, decimal payout, int? fallbackMinutes, DateTimeOffset occurredUtc)
    {
        var world = source.EnsureValid(); EconomyValidation.NonNegative(payout, nameof(payout));
        if (fallbackMinutes is < 0) throw new InvalidDataException("Road fallback delay cannot be negative.");
        var contract = world.LogisticsContracts.SingleOrDefault(x => x.Id == contractId) ?? throw new InvalidOperationException("Contract does not exist.");
        if (contract.State != OperationLogisticsContractState.Published) throw new InvalidOperationException("Only an unaccepted published job can be edited.");
        if (contract.PilotPayout == payout && contract.RoadFallbackDelayMinutes == fallbackMinutes) return new(world, false);
        var reservation = world.FinancialReservations.Single(x => x.ContractId == contract.Id);
        var serviceReserve = fallbackMinutes is null ? payout : Math.Max(payout, contract.RoadCost);
        var required = serviceReserve + (contract.Direction == OperationLogisticsDirection.Import ? contract.CargoValue : 0);
        if (AvailableFunds(world, contract.OwnerPlayerId, reservation.CurrencyCode) + reservation.Amount < required)
            throw new InvalidOperationException("Available funds cannot cover the increased pilot payout.");
        return new((world with { Revision = world.Revision + 1,
            LogisticsContracts = world.LogisticsContracts.Select(x => x.Id == contract.Id ? x with { PilotPayout = payout, RoadFallbackDelayMinutes = fallbackMinutes,
                FallbackAtUtc = fallbackMinutes is null ? null : x.CreatedUtc.AddMinutes(fallbackMinutes.Value), ReservedFunds = required } : x).ToArray(),
            Requirements = world.Requirements.Select(x => x.Id == contract.RequirementId ? x with { PaymentAmount = payout } : x).ToArray(),
            FinancialReservations = world.FinancialReservations.Select(x => x.Id == reservation.Id ? x with { Amount = required } : x).ToArray() }).EnsureValid(), true);
    }

    public static EconomyTransition Cancel(EconomyWorld source, string contractId, DateTimeOffset occurredUtc)
    {
        var world = source.EnsureValid(); var contract = world.LogisticsContracts.SingleOrDefault(x => x.Id == contractId) ?? throw new InvalidOperationException("Contract does not exist.");
        if (contract.State == OperationLogisticsContractState.Cancelled) return new(world, false);
        if (contract.State != OperationLogisticsContractState.Published) throw new InvalidOperationException("An accepted or active contract cannot be owner-cancelled.");
        var inventories = world.Inventories.ToList(); var i = inventories.FindIndex(x => x.LocationId == contract.OriginLocationId && x.ResourceId == contract.ResourceId);
        if (i < 0) inventories.Add(new(contract.OriginLocationId, contract.ResourceId, contract.Quantity));
        else inventories[i] = inventories[i] with { AvailableQuantity = inventories[i].AvailableQuantity + contract.Quantity };
        const string reason = "Player operation owner cancelled the unaccepted logistics contract.";
        var audit = new InventoryTransaction(EconomyIds.Derive("inventory-contract-release-", contract.Id), InventoryTransactionKind.PlayerContractReleased,
            contract.OriginLocationId, contract.ResourceId, contract.Quantity, contract.JobId, occurredUtc, reason, contract.OperationId, contract.CommandId);
        var next = world with { Revision = world.Revision + 1, Inventories = inventories, InventoryLedger = [..world.InventoryLedger, audit],
            LogisticsContracts = world.LogisticsContracts.Select(x => x.Id == contract.Id ? x with { State = OperationLogisticsContractState.Cancelled, CancelledUtc = occurredUtc } : x).ToArray(),
            FinancialReservations = world.FinancialReservations.Select(x => x.ContractId == contract.Id ? x with { State = FinancialReservationState.Released, ClosedUtc = occurredUtc } : x).ToArray(),
            Jobs = world.Jobs.Select(x => x.Id == contract.JobId ? x with { State = EconomyJobState.Cancelled, CancelledUtc = occurredUtc, CancellationReason = reason } : x).ToArray(),
            Requirements = world.Requirements.Select(x => x.Id == contract.RequirementId ? x with { State = TransportRequirementState.Cancelled, CancelledUtc = occurredUtc, CancellationReason = reason } : x).ToArray() };
        return new(next.EnsureValid(), true);
    }

    internal static EconomyWorld AcceptReserved(EconomyWorld world, OperationLogisticsContract contract, DateTimeOffset acceptedUtc)
    {
        var shipment = new Shipment(contract.ShipmentId, contract.JobId, contract.ResourceId, contract.Quantity,
            contract.OriginLocationId, contract.DestinationLocationId, ShipmentState.Reserved, contract.OriginLocationId);
        return world with {
            Requirements = world.Requirements.Select(x => x.Id == contract.RequirementId ? x with { State = TransportRequirementState.Reserved } : x).ToArray(),
            Jobs = world.Jobs.Select(x => x.Id == contract.JobId ? x with { State = EconomyJobState.Accepted, AcceptedUtc = acceptedUtc } : x).ToArray(),
            Shipments = [..world.Shipments, shipment],
            LogisticsContracts = world.LogisticsContracts.Select(x => x.Id == contract.Id ? x with { State = OperationLogisticsContractState.Accepted, AcceptedPilotId = EmployedCargoPrototype.PlayerId, AcceptedUtc = acceptedUtc } : x).ToArray() };
    }

    internal static EconomyWorld SettleAir(EconomyWorld world, string contractId, string evidenceId, DateTimeOffset occurredUtc)
    {
        var c = world.LogisticsContracts.Single(x => x.Id == contractId);
        var ledger = world.FinancialLedger.ToList();
        AddSettlement(ledger, c, evidenceId, occurredUtc, air: true);
        var result = world with { FinancialLedger = ledger,
            LogisticsContracts = world.LogisticsContracts.Select(x => x.Id == c.Id ? x with { State = OperationLogisticsContractState.Completed, CompletedUtc = occurredUtc } : x).ToArray(),
            FinancialReservations = world.FinancialReservations.Select(x => x.ContractId == c.Id ? x with { State = FinancialReservationState.Settled, Amount = 0, ClosedUtc = occurredUtc } : x).ToArray() };
        return Rebalance(result);
    }

    internal static EconomyWorld Forfeit(EconomyWorld world, string contractId, DateTimeOffset occurredUtc)
    {
        var contract = world.LogisticsContracts.Single(x => x.Id == contractId);
        return world with {
            LogisticsContracts = world.LogisticsContracts.Select(x => x.Id == contract.Id ? x with { State = OperationLogisticsContractState.Forfeited, CancelledUtc = occurredUtc } : x).ToArray(),
            FinancialReservations = world.FinancialReservations.Select(x => x.ContractId == contract.Id ? x with { Amount = 0, State = FinancialReservationState.Released, ClosedUtc = occurredUtc } : x).ToArray()
        };
    }

    public static EconomyTransition ProcessRoad(EconomyWorld source, DateTimeOffset now)
    {
        var world = source.EnsureValid(); var changed = false;
        foreach (var seed in world.LogisticsContracts.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray())
        {
            var c = world.LogisticsContracts.Single(x => x.Id == seed.Id);
            if (c.State == OperationLogisticsContractState.Published && c.FallbackAtUtc is { } due && due <= now)
            {
                var roadStartedUtc = due;
                var ledger = world.FinancialLedger.ToList();
                ledger.Add(new(EconomyIds.Derive("road-cost-", c.Id), FinancialTransactionKind.RoadTransportCost, c.JobId,
                    EconomyIds.Derive("road-start-", c.Id), c.OwnerPlayerId, RoadCarrierBusinessId, c.RoadCost,
                    EmployedCargoPrototype.Currency, roadStartedUtc, c.Id));
                var remaining = c.Direction == OperationLogisticsDirection.Import ? c.CargoValue : 0;
                world = world with { FinancialLedger = ledger,
                    Shipments = [..world.Shipments, new(c.ShipmentId, c.JobId, c.ResourceId, c.Quantity, c.OriginLocationId, c.DestinationLocationId, ShipmentState.InTransit, null) { LoadedQuantity = c.Quantity }],
                    Jobs = world.Jobs.Select(x => x.Id == c.JobId ? x with { State = EconomyJobState.RoadInTransit } : x).ToArray(),
                    Requirements = world.Requirements.Select(x => x.Id == c.RequirementId ? x with { State = TransportRequirementState.Reserved } : x).ToArray(),
                    LogisticsContracts = world.LogisticsContracts.Select(x => x.Id == c.Id ? x with { State = OperationLogisticsContractState.RoadInTransit, RoadStartedUtc = roadStartedUtc, RoadEtaUtc = roadStartedUtc.AddMinutes(c.RoadDurationMinutes) } : x).ToArray(),
                    FinancialReservations = world.FinancialReservations.Select(x => x.ContractId == c.Id ? x with { Amount = remaining, State = remaining > 0 ? FinancialReservationState.Active : FinancialReservationState.Settled, ClosedUtc = remaining > 0 ? null : roadStartedUtc } : x).ToArray() };
                world = Rebalance(world); changed = true; c = world.LogisticsContracts.Single(x => x.Id == seed.Id);
            }
            if (c.State == OperationLogisticsContractState.RoadInTransit && c.RoadEtaUtc <= now)
            {
                world = CompleteRoad(world, c, now); changed = true;
            }
        }
        if (!changed) return new(source, false);
        return new((world with { Revision = source.Revision + 1 }).EnsureValid(), true);
    }

    private static EconomyWorld CompleteRoad(EconomyWorld world, OperationLogisticsContract c, DateTimeOffset now)
    {
        var inventories = world.Inventories.ToList(); var i = inventories.FindIndex(x => x.LocationId == c.DestinationLocationId && x.ResourceId == c.ResourceId);
        if (i < 0) inventories.Add(new(c.DestinationLocationId, c.ResourceId, c.Quantity)); else inventories[i] = inventories[i] with { AvailableQuantity = inventories[i].AvailableQuantity + c.Quantity };
        var evidence = EconomyIds.Derive("road-arrival-", c.Id); var ledger = world.FinancialLedger.ToList(); AddSettlement(ledger, c, evidence, now, air: false);
        var audit = new InventoryTransaction(EconomyIds.Derive("inventory-road-delivery-", c.Id), InventoryTransactionKind.PlayerContractDelivered,
            c.DestinationLocationId, c.ResourceId, c.Quantity, c.JobId, now, "Road shipment arrived and was settled exactly once.", c.OperationId, c.CommandId);
        world = world with { Inventories = inventories, InventoryLedger = [..world.InventoryLedger, audit], FinancialLedger = ledger,
            Jobs = world.Jobs.Select(x => x.Id == c.JobId ? x with { State = EconomyJobState.Completed, CompletedUtc = now } : x).ToArray(),
            Requirements = world.Requirements.Select(x => x.Id == c.RequirementId ? x with { State = TransportRequirementState.Fulfilled } : x).ToArray(),
            Shipments = world.Shipments.Select(x => x.Id == c.ShipmentId ? x with { State = ShipmentState.Delivered, CurrentLocationId = c.DestinationLocationId, LoadedQuantity = 0, DeliveredQuantity = c.Quantity, DeliveryEvidenceId = evidence, DeliveredUtc = now } : x).ToArray(),
            LogisticsContracts = world.LogisticsContracts.Select(x => x.Id == c.Id ? x with { State = OperationLogisticsContractState.Completed, CompletedUtc = now } : x).ToArray(),
            FinancialReservations = world.FinancialReservations.Select(x => x.ContractId == c.Id ? x with { Amount = 0, State = FinancialReservationState.Settled, ClosedUtc = now } : x).ToArray() };
        return Rebalance(world);
    }

    private static void AddSettlement(List<FinancialTransaction> ledger, OperationLogisticsContract c, string evidence, DateTimeOffset at, bool air)
    {
        if (air) ledger.Add(new(EconomyIds.Derive("contract-pilot-payment-", c.Id), FinancialTransactionKind.JobPayment, c.JobId, evidence,
            c.OwnerPlayerId, c.AcceptedPilotId ?? EmployedCargoPrototype.PlayerId, c.PilotPayout, EmployedCargoPrototype.Currency, at, c.Id));
        if (c.Direction == OperationLogisticsDirection.Import)
            ledger.Add(new(EconomyIds.Derive("contract-purchase-", c.Id), FinancialTransactionKind.LogisticsCargoPurchase, c.JobId, evidence,
                c.OwnerPlayerId, GatewayMarketBusinessId, c.CargoValue, EmployedCargoPrototype.Currency, at, c.Id));
        else
            ledger.Add(new(EconomyIds.Derive("contract-sale-", c.Id), FinancialTransactionKind.LogisticsSaleRevenue, c.JobId, evidence,
                GatewayMarketBusinessId, c.OwnerPlayerId, c.CargoValue, EmployedCargoPrototype.Currency, at, c.Id));
    }

    private static EconomyWorld Rebalance(EconomyWorld world)
    {
        var players = world.Players.Select(x => x with { CachedBalance = EconomyLedger.Balance(world, x.Id, x.CurrencyCode) }).ToArray();
        return world with { Players = players };
    }
    private static decimal? Capacity(EconomyWorld world, string locationId, string resourceId)
    {
        var site = world.Sites.SingleOrDefault(x => x.LocationId == locationId); if (site is null) return null;
        var operation = world.Operations.Single(x => x.SiteId == site.Id);
        return OperationArchetypeCatalogue.Get(operation.ArchetypeId).Storage.SingleOrDefault(x => x.ResourceId == resourceId)?.Capacity;
    }
    private static void EnsureBusiness(List<EconomicBusiness> businesses, string id, string name, EconomicOwnerKind owner)
    { if (!businesses.Any(x => x.Id == id)) businesses.Add(new(id, name, owner)); }
}

internal static class PlayerLogisticsValidation
{
    public static void EnsureValid(EconomyWorld world)
    {
        foreach (var c in world.LogisticsContracts)
        {
            EconomyValidation.Id(c.Id, "contract ID"); EconomyValidation.Id(c.CommandId, "contract command");
            EconomyValidation.Positive(c.Quantity, "contract quantity"); EconomyValidation.NonNegative(c.PilotPayout, "pilot payout");
            if (!world.Operations.Any(x => x.Id == c.OperationId) || !world.Jobs.Any(x => x.Id == c.JobId) ||
                !world.Requirements.Any(x => x.Id == c.RequirementId) || !world.Resources.Any(x => x.Id == c.ResourceId))
                throw new InvalidDataException("Player logistics contract has a broken reference.");
            var reservation = world.FinancialReservations.SingleOrDefault(x => x.ContractId == c.Id) ?? throw new InvalidDataException("Contract escrow is missing.");
            if (reservation.Amount < 0) throw new InvalidDataException("Contract escrow cannot be negative.");
            var job = world.Jobs.Single(x => x.Id == c.JobId); var requirement = world.Requirements.Single(x => x.Id == c.RequirementId);
            var shipment = world.Shipments.SingleOrDefault(x => x.Id == c.ShipmentId);
            var valid = c.State switch
            {
                OperationLogisticsContractState.Published => job.State == EconomyJobState.Offered && requirement.State == TransportRequirementState.Open && shipment is null && reservation.State == FinancialReservationState.Active,
                OperationLogisticsContractState.Accepted => job.State == EconomyJobState.Accepted && requirement.State == TransportRequirementState.Reserved && shipment is not null && reservation.State == FinancialReservationState.Active,
                OperationLogisticsContractState.RoadInTransit => job.State == EconomyJobState.RoadInTransit && requirement.State == TransportRequirementState.Reserved && shipment?.State == ShipmentState.InTransit,
                OperationLogisticsContractState.Completed => job.State == EconomyJobState.Completed && requirement.State == TransportRequirementState.Fulfilled && shipment?.State == ShipmentState.Delivered && reservation.State == FinancialReservationState.Settled,
                OperationLogisticsContractState.Cancelled => job.State == EconomyJobState.Cancelled && requirement.State == TransportRequirementState.Cancelled && shipment is null && reservation.State == FinancialReservationState.Released,
                OperationLogisticsContractState.Forfeited => job.State == EconomyJobState.Forfeited && requirement.State == TransportRequirementState.Cancelled && shipment?.State == ShipmentState.Forfeited && reservation.State == FinancialReservationState.Released,
                _ => false
            };
            if (!valid) throw new InvalidDataException("Player logistics lifecycle records disagree.");
            if (world.InventoryLedger.Count(x => x.Kind == InventoryTransactionKind.PlayerContractReserved && x.JobId == c.JobId) != 1)
                throw new InvalidDataException("Player logistics cargo reservation audit is missing or duplicated.");
        }
        foreach (var group in world.FinancialReservations.Where(x => x.State == FinancialReservationState.Active).GroupBy(x => (x.OwnerEntityId, x.CurrencyCode)))
            if (group.Sum(x => x.Amount) > EconomyLedger.Balance(world, group.Key.OwnerEntityId, group.Key.CurrencyCode))
                throw new InvalidDataException("Active financial reservations exceed ledger-derived funds.");
    }
}
