using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace FrontierAirportBuilder.Core;

public enum EconomicOwnerKind { System, Player }
public enum EconomicLocationKind { Gateway, Mine, Community, Lodge, OperationSite }
public enum EconomicLocationStatus { PrototypeOnly, ResearchCandidate, MsfsVerified, ActiveCustomer, DormantCandidate, ActiveOperation }
public enum TransportRequirementState { Open, Reserved, Fulfilled, Cancelled }
public enum EconomyJobState { Offered, Accepted, RoadInTransit, Completed, Cancelled, Forfeited }
public enum ShipmentState { Available, Reserved, InTransit, Delivered, Consumed, Sold, Cancelled, Loaded, Forfeited }
public enum FinancialTransactionKind { JobPayment, SiteFounding, LogisticsCargoPurchase, LogisticsSaleRevenue, RoadTransportCost }
public enum InventoryTransactionKind { LegacyOpeningBalance, SystemGatewaySupply, ShipmentReserved, DeliveryReceived, OperationInitialStock, OperationConsumption, OperationProduction, ShipmentReservationReleased, PartialDeliveryReceived, PersistentGatewayOpeningStock, PersistentTransferOut, PersistentTransferIn, PlayerContractReserved, PlayerContractReleased, PlayerContractDelivered }
public enum ShipmentCargoTransferKind { LoadedToAircraft, UnloadedAtOrigin, UnloadedAtDestination, UnloadedAndForfeited }

public sealed record EconomicLocation(string Id, string Name, EconomicLocationKind Kind, EconomicOwnerKind Owner,
    EconomicLocationStatus Status);
public sealed record EconomicBusiness(string Id, string Name, EconomicOwnerKind Owner);
public sealed record ResourceType(string Id, string Name, string Category, string UnitName);
public sealed record InventoryBalance(string LocationId, string ResourceId, decimal AvailableQuantity);
public sealed record CustomerNeed(string Id, string DestinationLocationId, string ResourceId, decimal TargetQuantity);
public sealed record TransportRequirement(string Id, string NeedId, string OriginLocationId, string DestinationLocationId,
    string ResourceId, decimal Quantity, decimal PaymentAmount, string CurrencyCode, string FundingBusinessId,
    TransportRequirementState State)
{
    public string? Purpose { get; init; }
    public DateTimeOffset? CancelledUtc { get; init; }
    public string? CancellationReason { get; init; }
}
public sealed record EconomyJob(string Id, string RequirementId, string EmployerBusinessId, string PlayerId,
    string ShipmentId, EconomyJobState State, DateTimeOffset? AcceptedUtc = null, DateTimeOffset? CompletedUtc = null)
{
    public DateTimeOffset? CancelledUtc { get; init; }
    public string? CancellationReason { get; init; }
}
public sealed record Shipment(string Id, string JobId, string ResourceId, decimal Quantity, string OriginLocationId,
    string DestinationLocationId, ShipmentState State, string? CurrentLocationId, string? DeliveryEvidenceId = null,
    DateTimeOffset? DeliveredUtc = null)
{
    public DateTimeOffset? CancelledUtc { get; init; }
    public string? CancellationReason { get; init; }
    public decimal LoadedQuantity { get; init; }
    public decimal DeliveredQuantity { get; init; }
    public decimal ForfeitedQuantity { get; init; }
}
public sealed record PlayerAccount(string Id, string EmployerBusinessId, string CurrencyCode, decimal CachedBalance);
public sealed record FinancialTransaction(string Id, FinancialTransactionKind Kind, string? JobId, string EvidenceId,
    string FromEntityId, string ToEntityId, decimal Amount, string CurrencyCode, DateTimeOffset OccurredUtc,
    string? SiteId = null);
public sealed record InventoryTransaction(string Id, InventoryTransactionKind Kind, string LocationId, string ResourceId,
    decimal QuantityDelta, string? JobId, DateTimeOffset OccurredUtc, string Details,
    string? OperationId = null, string? CommandId = null);
public sealed record ShipmentCargoTransfer(string Id, string CommandId, string JobId, string ShipmentId,
    ShipmentCargoTransferKind Kind, decimal Quantity, string? LocationId, double Latitude, double Longitude,
    DateTimeOffset OccurredUtc, string Details);
public sealed record ShipmentCargoTransferRequest(string Id, string CommandId, string JobId, decimal DesiredLoadedQuantity,
    string? CurrentLocationId, double Latitude, double Longitude, DateTimeOffset OccurredUtc,
    bool ConfirmForfeit = false, VerifiedDeliveryEvidence? CompletionEvidence = null);
public sealed record RecurringDemandDefinition(string Id, string NeedId, string IdStem, string OriginLocationId,
    string DestinationLocationId, string ResourceId, decimal PreferredShipmentQuantity, decimal PaymentAmount,
    string CurrencyCode, int DesiredAvailableOffers, string FundingBusinessId, string EmployerBusinessId,
    string PlayerId, bool IsEnabled);
public sealed record AircraftDispatchProfile(string Id, decimal CargoCapacity);
public sealed record DispatchRecommendation(bool FleetCompatible, string Explanation);

/// <summary>Confirmation supplied by a later flight/location component. Core validates identity and consistency only;
/// it deliberately does not decide how a flight becomes verified.</summary>
public sealed record VerifiedDeliveryEvidence(string Id, string Provider, string JobId, string ShipmentId,
    string OriginLocationId, string DestinationLocationId, DateTimeOffset VerifiedUtc, string Details)
{
    public VerifiedDeliveryEvidence EnsureValid()
    {
        EconomyValidation.Id(Id, nameof(Id)); EconomyValidation.Text(Provider, nameof(Provider), 64);
        EconomyValidation.Id(JobId, nameof(JobId)); EconomyValidation.Id(ShipmentId, nameof(ShipmentId));
        EconomyValidation.Id(OriginLocationId, nameof(OriginLocationId)); EconomyValidation.Id(DestinationLocationId, nameof(DestinationLocationId));
        EconomyValidation.Text(Details, nameof(Details), 512);
        if (VerifiedUtc == default) throw new InvalidDataException("Delivery evidence timestamp is missing.");
        return this;
    }
}

public sealed record EconomyWorld(string Id, int Revision, IReadOnlyList<EconomicLocation> Locations,
    IReadOnlyList<EconomicBusiness> Businesses, IReadOnlyList<ResourceType> Resources,
    IReadOnlyList<InventoryBalance> Inventories, IReadOnlyList<CustomerNeed> Needs,
    IReadOnlyList<TransportRequirement> Requirements, IReadOnlyList<PlayerAccount> Players,
    IReadOnlyList<EconomyJob> Jobs, IReadOnlyList<Shipment> Shipments,
    IReadOnlyList<FinancialTransaction> FinancialLedger)
{
    public IReadOnlyList<InventoryTransaction> InventoryLedger { get; init; } = [];
    public IReadOnlyList<RecurringDemandDefinition> RecurringDemand { get; init; } = [];
    public IReadOnlyList<EconomicSite> Sites { get; init; } = [];
    public IReadOnlyList<EconomicOperation> Operations { get; init; } = [];
    public IReadOnlyList<OperationTickRecord> OperationTicks { get; init; } = [];
    public IReadOnlyList<ShipmentCargoTransfer> CargoTransfers { get; init; } = [];
    public IReadOnlyList<OperationLogisticsContract> LogisticsContracts { get; init; } = [];
    public IReadOnlyList<FinancialReservation> FinancialReservations { get; init; } = [];

    public EconomyWorld EnsureValid()
    {
        EconomyValidation.Id(Id, nameof(Id));
        if (Revision < 1) throw new InvalidDataException("Economy revision is invalid.");
        EconomyValidation.Collection(Locations, nameof(Locations)); EconomyValidation.Collection(Businesses, nameof(Businesses));
        EconomyValidation.Collection(Resources, nameof(Resources)); EconomyValidation.Collection(Inventories, nameof(Inventories));
        EconomyValidation.Collection(Needs, nameof(Needs)); EconomyValidation.Collection(Requirements, nameof(Requirements));
        EconomyValidation.Collection(Players, nameof(Players)); EconomyValidation.Collection(Jobs, nameof(Jobs));
        EconomyValidation.Collection(Shipments, nameof(Shipments)); EconomyValidation.Collection(FinancialLedger, nameof(FinancialLedger));
        EconomyValidation.Collection(InventoryLedger, nameof(InventoryLedger)); EconomyValidation.Collection(RecurringDemand, nameof(RecurringDemand));
        EconomyValidation.Collection(Sites, nameof(Sites)); EconomyValidation.Collection(Operations, nameof(Operations)); EconomyValidation.Collection(OperationTicks, nameof(OperationTicks));
        EconomyValidation.Collection(CargoTransfers, nameof(CargoTransfers));
        EconomyValidation.Collection(LogisticsContracts, nameof(LogisticsContracts)); EconomyValidation.Collection(FinancialReservations, nameof(FinancialReservations));

        EconomyValidation.Unique(Locations.Select(x => x.Id), "location");
        EconomyValidation.Unique(Businesses.Select(x => x.Id), "business");
        EconomyValidation.Unique(Resources.Select(x => x.Id), "resource");
        EconomyValidation.Unique(Needs.Select(x => x.Id), "need");
        EconomyValidation.Unique(Requirements.Select(x => x.Id), "requirement");
        EconomyValidation.Unique(Players.Select(x => x.Id), "player");
        EconomyValidation.Unique(Jobs.Select(x => x.Id), "job");
        EconomyValidation.Unique(Jobs.Select(x => x.RequirementId), "job requirement");
        EconomyValidation.Unique(Shipments.Select(x => x.Id), "shipment");
        EconomyValidation.Unique(FinancialLedger.Select(x => x.Id), "financial transaction");
        EconomyValidation.Unique(InventoryLedger.Select(x => x.Id), "inventory transaction");
        EconomyValidation.Unique(RecurringDemand.Select(x => x.Id), "recurring demand");
        EconomyValidation.Unique(RecurringDemand.Select(x => x.NeedId), "recurring demand need");
        EconomyValidation.Unique(Sites.Select(x => x.Id), "economic site");
        EconomyValidation.Unique(Sites.Select(x => x.AirportDefinitionId), "economic site airport definition");
        EconomyValidation.Unique(Sites.Select(x => x.LocationId), "economic site location");
        EconomyValidation.Unique(Operations.Select(x => x.Id), "economic operation");
        EconomyValidation.Unique(Operations.Select(x => x.SiteId), "economic operation site");
        EconomyValidation.Unique(OperationTicks.Select(x => x.Id), "operation tick");
        EconomyValidation.Unique(OperationTicks.Select(x => x.CommandId), "operation tick command");
        EconomyValidation.Unique(CargoTransfers.Select(x => x.Id), "shipment cargo transfer");
        EconomyValidation.Unique(CargoTransfers.Select(x => x.CommandId), "shipment cargo transfer command");
        EconomyValidation.Unique(LogisticsContracts.Select(x => x.Id), "logistics contract"); EconomyValidation.Unique(LogisticsContracts.Select(x => x.CommandId), "logistics contract command");
        EconomyValidation.Unique(FinancialReservations.Select(x => x.Id), "financial reservation"); EconomyValidation.Unique(FinancialReservations.Select(x => x.ContractId), "financial reservation contract");
        EconomyValidation.Unique(Inventories.Select(x => x.LocationId + "\n" + x.ResourceId), "inventory position");

        var locations = Locations.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var businesses = Businesses.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var resources = Resources.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var needs = Needs.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var requirements = Requirements.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var players = Players.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var jobs = Jobs.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var shipments = Shipments.ToDictionary(x => x.Id, StringComparer.Ordinal);

        foreach (var x in Locations) { EconomyValidation.Id(x.Id, "location ID"); EconomyValidation.Text(x.Name, "location name", 96); }
        foreach (var x in Businesses) { EconomyValidation.Id(x.Id, "business ID"); EconomyValidation.Text(x.Name, "business name", 96); }
        foreach (var x in Resources)
        {
            EconomyValidation.Id(x.Id, "resource ID"); EconomyValidation.Text(x.Name, "resource name", 96);
            EconomyValidation.Text(x.Category, "resource category", 64); EconomyValidation.Text(x.UnitName, "resource unit", 32);
        }
        foreach (var x in Inventories)
        {
            EconomyValidation.Reference(locations, x.LocationId, "inventory location"); EconomyValidation.Reference(resources, x.ResourceId, "inventory resource");
            EconomyValidation.NonNegative(x.AvailableQuantity, "inventory quantity");
        }
        foreach (var x in Needs)
        {
            EconomyValidation.Id(x.Id, "need ID"); EconomyValidation.Reference(locations, x.DestinationLocationId, "need destination");
            EconomyValidation.Reference(resources, x.ResourceId, "need resource"); EconomyValidation.Positive(x.TargetQuantity, "need target");
        }
        foreach (var x in Requirements)
        {
            EconomyValidation.Id(x.Id, "requirement ID"); EconomyValidation.Reference(needs, x.NeedId, "requirement need");
            EconomyValidation.Reference(locations, x.OriginLocationId, "requirement origin"); EconomyValidation.Reference(locations, x.DestinationLocationId, "requirement destination");
            EconomyValidation.Reference(resources, x.ResourceId, "requirement resource"); EconomyValidation.Reference(businesses, x.FundingBusinessId, "requirement funder");
            EconomyValidation.Positive(x.Quantity, "requirement quantity"); EconomyValidation.NonNegative(x.PaymentAmount, "requirement payment"); EconomyValidation.Currency(x.CurrencyCode);
            if (x.Purpose is not null) EconomyValidation.Text(x.Purpose, "requirement purpose", 256);
            if (x.State == TransportRequirementState.Cancelled)
            { if (x.CancelledUtc is null || string.IsNullOrWhiteSpace(x.CancellationReason)) throw new InvalidDataException("Cancelled requirement state is incomplete."); }
            else if (x.CancelledUtc is not null || x.CancellationReason is not null) throw new InvalidDataException("Active requirement contains cancellation data.");
            var need = needs[x.NeedId];
            if (need.DestinationLocationId != x.DestinationLocationId || need.ResourceId != x.ResourceId)
                throw new InvalidDataException("Transport requirement does not match its customer need.");
        }
        foreach (var x in Players)
        {
            EconomyValidation.Id(x.Id, "player ID"); EconomyValidation.Reference(businesses, x.EmployerBusinessId, "player employer");
            EconomyValidation.Currency(x.CurrencyCode);
        }
        foreach (var x in Jobs)
        {
            var contract = LogisticsContracts.SingleOrDefault(c => c.JobId == x.Id);
            EconomyValidation.Id(x.Id, "job ID"); EconomyValidation.Reference(requirements, x.RequirementId, "job requirement");
            EconomyValidation.Reference(businesses, x.EmployerBusinessId, "job employer"); EconomyValidation.Reference(players, x.PlayerId, "job player");
            EconomyValidation.Id(x.ShipmentId, "job shipment ID");
            if (x.State == EconomyJobState.Offered && (x.AcceptedUtc != null || x.CompletedUtc != null || shipments.ContainsKey(x.ShipmentId)))
                throw new InvalidDataException("Offered job has accepted or shipment state.");
            if (x.State == EconomyJobState.Accepted && (x.AcceptedUtc == null || x.CompletedUtc != null || !shipments.ContainsKey(x.ShipmentId)))
                throw new InvalidDataException("Accepted job state is incomplete.");
            if (x.State == EconomyJobState.RoadInTransit && (x.AcceptedUtc != null || x.CompletedUtc != null || !shipments.ContainsKey(x.ShipmentId)))
                throw new InvalidDataException("Road job state is incomplete.");
            if (x.State == EconomyJobState.Completed && (x.CompletedUtc == null || !shipments.ContainsKey(x.ShipmentId) || (x.AcceptedUtc == null && contract?.RoadStartedUtc is null)))
                throw new InvalidDataException("Completed job state is incomplete.");
            if (x.State == EconomyJobState.Cancelled && (x.CompletedUtc != null || x.CancelledUtc is null ||
                string.IsNullOrWhiteSpace(x.CancellationReason) || (contract is null && (x.AcceptedUtc == null || !shipments.ContainsKey(x.ShipmentId)))))
                throw new InvalidDataException("Cancelled job state is incomplete.");
            if (x.State == EconomyJobState.Forfeited && (x.AcceptedUtc is null || x.CompletedUtc is not null || x.CancelledUtc is null ||
                string.IsNullOrWhiteSpace(x.CancellationReason) || !shipments.ContainsKey(x.ShipmentId)))
                throw new InvalidDataException("Forfeited job state is incomplete.");
            if (x.State is not (EconomyJobState.Cancelled or EconomyJobState.Forfeited) && (x.CancelledUtc is not null || x.CancellationReason is not null))
                throw new InvalidDataException("Active job contains cancellation data.");
            var requirement = requirements[x.RequirementId];
            if (x.State == EconomyJobState.Offered && requirement.State != TransportRequirementState.Open ||
                x.State == EconomyJobState.Accepted && requirement.State != TransportRequirementState.Reserved ||
                x.State == EconomyJobState.RoadInTransit && requirement.State != TransportRequirementState.Reserved ||
                x.State == EconomyJobState.Completed && requirement.State != TransportRequirementState.Fulfilled ||
                x.State is (EconomyJobState.Cancelled or EconomyJobState.Forfeited) && requirement.State != TransportRequirementState.Cancelled)
                throw new InvalidDataException("Job and transport requirement states disagree.");
            if (x.State == EconomyJobState.Accepted && shipments[x.ShipmentId].State is not (ShipmentState.Reserved or ShipmentState.Loaded or ShipmentState.InTransit) ||
                x.State == EconomyJobState.RoadInTransit && shipments[x.ShipmentId].State != ShipmentState.InTransit ||
                x.State == EconomyJobState.Completed && shipments[x.ShipmentId].State != ShipmentState.Delivered ||
                x.State == EconomyJobState.Cancelled && contract is null && shipments[x.ShipmentId].State != ShipmentState.Cancelled ||
                x.State == EconomyJobState.Forfeited && shipments[x.ShipmentId].State != ShipmentState.Forfeited)
                throw new InvalidDataException("Job and shipment states disagree.");
        }
        foreach (var x in Shipments)
        {
            EconomyValidation.Id(x.Id, "shipment ID"); EconomyValidation.Reference(jobs, x.JobId, "shipment job");
            EconomyValidation.Reference(resources, x.ResourceId, "shipment resource"); EconomyValidation.Reference(locations, x.OriginLocationId, "shipment origin");
            EconomyValidation.Reference(locations, x.DestinationLocationId, "shipment destination"); EconomyValidation.Positive(x.Quantity, "shipment quantity");
            if (x.CurrentLocationId is not null) EconomyValidation.Reference(locations, x.CurrentLocationId, "shipment location");
            if (jobs[x.JobId].ShipmentId != x.Id) throw new InvalidDataException("Shipment does not match its job.");
            var requirement = requirements[jobs[x.JobId].RequirementId];
            if (x.ResourceId != requirement.ResourceId || x.Quantity != requirement.Quantity || x.OriginLocationId != requirement.OriginLocationId ||
                x.DestinationLocationId != requirement.DestinationLocationId)
                throw new InvalidDataException("Shipment does not match its transport requirement.");
            if (x.State is (ShipmentState.Reserved or ShipmentState.Loaded) && x.CurrentLocationId != x.OriginLocationId ||
                x.State == ShipmentState.InTransit && x.CurrentLocationId is not null)
                throw new InvalidDataException("Shipment state and location disagree.");
            if (x.State == ShipmentState.Delivered && (x.CurrentLocationId != x.DestinationLocationId || x.DeliveryEvidenceId is null || x.DeliveredUtc is null))
                throw new InvalidDataException("Delivered shipment state is incomplete.");
            if (x.State != ShipmentState.Delivered && (x.DeliveryEvidenceId is not null || x.DeliveredUtc is not null))
                throw new InvalidDataException("Undelivered shipment contains delivery evidence.");
            if (x.State == ShipmentState.Cancelled && (x.CurrentLocationId != x.OriginLocationId || x.CancelledUtc is null || string.IsNullOrWhiteSpace(x.CancellationReason)))
                throw new InvalidDataException("Cancelled shipment state is incomplete.");
            if (x.State == ShipmentState.Forfeited && (x.CancelledUtc is null || string.IsNullOrWhiteSpace(x.CancellationReason)))
                throw new InvalidDataException("Forfeited shipment state is incomplete.");
            if (x.State is not (ShipmentState.Cancelled or ShipmentState.Forfeited) && (x.CancelledUtc is not null || x.CancellationReason is not null))
                throw new InvalidDataException("Active shipment contains cancellation data.");
            if (x.LoadedQuantity < 0 || x.DeliveredQuantity < 0 || x.ForfeitedQuantity < 0 ||
                x.LoadedQuantity > x.Quantity || x.DeliveredQuantity > x.Quantity || x.ForfeitedQuantity > x.Quantity)
                throw new InvalidDataException("Shipment physical quantities are invalid.");
            var legacyPhysicalQuantities = x.LoadedQuantity == 0 && x.DeliveredQuantity == 0 && x.ForfeitedQuantity == 0 &&
                x.State is ShipmentState.Loaded or ShipmentState.InTransit or ShipmentState.Delivered;
            if (!legacyPhysicalQuantities && (x.State == ShipmentState.Reserved && (x.LoadedQuantity != 0 || x.DeliveredQuantity != 0 || x.ForfeitedQuantity != 0) ||
                x.State == ShipmentState.Loaded && (x.LoadedQuantity <= 0 || x.DeliveredQuantity != 0 || x.ForfeitedQuantity != 0) ||
                x.State == ShipmentState.InTransit && (x.LoadedQuantity + x.DeliveredQuantity <= 0 || x.ForfeitedQuantity != 0) ||
                x.State == ShipmentState.Delivered && (x.LoadedQuantity != 0 || x.DeliveredQuantity != x.Quantity || x.ForfeitedQuantity != 0) ||
                x.State == ShipmentState.Cancelled && (x.LoadedQuantity != 0 || x.ForfeitedQuantity != 0) ||
                x.State == ShipmentState.Forfeited && (x.LoadedQuantity != 0 || x.DeliveredQuantity + x.ForfeitedQuantity != x.Quantity)))
                throw new InvalidDataException("Shipment state and physical quantities disagree.");
        }
        foreach (var x in CargoTransfers)
        {
            EconomyValidation.Id(x.Id, "cargo transfer ID"); EconomyValidation.Id(x.CommandId, "cargo transfer command ID");
            EconomyValidation.Reference(jobs, x.JobId, "cargo transfer job"); EconomyValidation.Reference(shipments, x.ShipmentId, "cargo transfer shipment");
            EconomyValidation.Positive(x.Quantity, "cargo transfer quantity"); EconomyValidation.Text(x.Details, "cargo transfer details", 512);
            if (x.LocationId is not null) EconomyValidation.Reference(locations, x.LocationId, "cargo transfer location");
            if (!double.IsFinite(x.Latitude) || !double.IsFinite(x.Longitude) || x.Latitude is < -90 or > 90 || x.Longitude is < -180 or > 180 || x.OccurredUtc == default)
                throw new InvalidDataException("Cargo transfer position or time is invalid.");
            if (shipments[x.ShipmentId].JobId != x.JobId) throw new InvalidDataException("Cargo transfer job and shipment disagree.");
        }
        foreach (var x in FinancialLedger)
        {
            EconomyValidation.Id(x.Id, "transaction ID");
            EconomyValidation.Id(x.EvidenceId, "transaction evidence ID"); EconomyValidation.Id(x.FromEntityId, "transaction source"); EconomyValidation.Id(x.ToEntityId, "transaction destination");
            EconomyValidation.Positive(x.Amount, "transaction amount"); EconomyValidation.Currency(x.CurrencyCode);
            if (x.OccurredUtc == default) throw new InvalidDataException("Financial transaction timestamp is missing.");
            if (x.Kind == FinancialTransactionKind.JobPayment)
            {
                EconomyValidation.Reference(jobs, x.JobId!, "transaction job");
                var paidJob = jobs[x.JobId!];
                if ((x.SiteId is not null && !LogisticsContracts.Any(c => c.Id == x.SiteId)) || paidJob.State != EconomyJobState.Completed || !shipments.TryGetValue(paidJob.ShipmentId, out var paidShipment) ||
                    paidShipment.State != ShipmentState.Delivered || paidShipment.DeliveryEvidenceId != x.EvidenceId)
                    throw new InvalidDataException("Financial transaction exists without its completed delivery.");
            }
            else if (x.Kind == FinancialTransactionKind.SiteFounding)
            {
                if (!string.IsNullOrEmpty(x.JobId)) throw new InvalidDataException("A site-founding debit cannot reference a job.");
                EconomyValidation.Id(x.SiteId, "founding transaction site");
                if (!Sites.Any(site => site.Id == x.SiteId) || !Players.Any(player => player.Id == x.FromEntityId) || !Businesses.Any(business => business.Id == x.ToEntityId))
                    throw new InvalidDataException("A site-founding debit refers to an unknown site, player or system funding entity.");
            }
            else if (!LogisticsContracts.Any(contract => contract.Id == x.SiteId))
                throw new InvalidDataException("Logistics financial transaction refers to an unknown contract.");
        }
        foreach (var x in InventoryLedger)
        {
            EconomyValidation.Id(x.Id, "inventory transaction ID"); EconomyValidation.Reference(locations, x.LocationId, "inventory transaction location");
            EconomyValidation.Reference(resources, x.ResourceId, "inventory transaction resource");
            if (x.QuantityDelta == 0) throw new InvalidDataException("Inventory transaction quantity cannot be zero.");
            if (x.JobId is not null) EconomyValidation.Reference(jobs, x.JobId, "inventory transaction job");
            if (x.OccurredUtc == default) throw new InvalidDataException("Inventory transaction timestamp is missing.");
            EconomyValidation.Text(x.Details, "inventory transaction details", 256);
            if (x.OperationId is not null && !Operations.Any(operation => operation.Id == x.OperationId))
                throw new InvalidDataException("Inventory transaction refers to an unknown operation.");
            if (x.CommandId is not null) EconomyValidation.Id(x.CommandId, "inventory transaction command");
            if (x.Kind is InventoryTransactionKind.LegacyOpeningBalance or InventoryTransactionKind.SystemGatewaySupply or InventoryTransactionKind.OperationInitialStock or InventoryTransactionKind.PersistentGatewayOpeningStock && (x.QuantityDelta < 0 || x.JobId is not null) ||
                x.Kind == InventoryTransactionKind.ShipmentReserved && (x.QuantityDelta > 0 || x.JobId is null) ||
                x.Kind == InventoryTransactionKind.ShipmentReservationReleased && (x.QuantityDelta < 0 || x.JobId is null) ||
                x.Kind == InventoryTransactionKind.DeliveryReceived && (x.QuantityDelta < 0 || x.JobId is null) ||
                x.Kind == InventoryTransactionKind.OperationInitialStock && (x.OperationId is null || x.CommandId is null) ||
                x.Kind == InventoryTransactionKind.OperationConsumption && (x.QuantityDelta > 0 || x.OperationId is null || x.CommandId is null) ||
                x.Kind == InventoryTransactionKind.OperationProduction && (x.QuantityDelta < 0 || x.OperationId is null || x.CommandId is null) ||
                x.Kind == InventoryTransactionKind.PersistentTransferOut && (x.QuantityDelta >= 0 || x.JobId is not null || x.CommandId is null) ||
                x.Kind == InventoryTransactionKind.PersistentTransferIn && (x.QuantityDelta <= 0 || x.JobId is not null || x.CommandId is null) ||
                x.Kind == InventoryTransactionKind.PlayerContractReserved && (x.QuantityDelta >= 0 || x.JobId is null || x.CommandId is null) ||
                x.Kind == InventoryTransactionKind.PlayerContractReleased && (x.QuantityDelta <= 0 || x.JobId is null || x.CommandId is null) ||
                x.Kind == InventoryTransactionKind.PlayerContractDelivered && (x.QuantityDelta <= 0 || x.JobId is null || x.CommandId is null))
                throw new InvalidDataException("Inventory transaction kind, quantity and job reference disagree.");
        }
        foreach (var x in RecurringDemand)
        {
            EconomyValidation.Id(x.Id, "recurring demand ID"); EconomyValidation.Reference(needs, x.NeedId, "recurring demand need");
            EconomyValidation.Id(x.IdStem, "recurring demand ID stem"); EconomyValidation.Reference(locations, x.OriginLocationId, "recurring demand origin");
            EconomyValidation.Reference(locations, x.DestinationLocationId, "recurring demand destination"); EconomyValidation.Reference(resources, x.ResourceId, "recurring demand resource");
            EconomyValidation.Reference(businesses, x.FundingBusinessId, "recurring demand funder"); EconomyValidation.Reference(businesses, x.EmployerBusinessId, "recurring demand employer");
            EconomyValidation.Reference(players, x.PlayerId, "recurring demand player"); EconomyValidation.Positive(x.PreferredShipmentQuantity, "recurring shipment quantity");
            EconomyValidation.NonNegative(x.PaymentAmount, "recurring demand payment"); EconomyValidation.Currency(x.CurrencyCode);
            if (x.DesiredAvailableOffers < 0 || x.DesiredAvailableOffers > 20) throw new InvalidDataException("Recurring demand offer count is invalid.");
            var need = needs[x.NeedId];
            if (need.DestinationLocationId != x.DestinationLocationId || need.ResourceId != x.ResourceId)
                throw new InvalidDataException("Recurring demand does not match its customer need.");
        }

        foreach (var player in Players)
        {
            var authoritative = EconomyLedger.Balance(this, player.Id, player.CurrencyCode);
            if (player.CachedBalance != authoritative)
                throw new InvalidDataException($"Cached balance for {player.Id} disagrees with the authoritative financial ledger.");
        }
        foreach (var job in Jobs.Where(x => x.State == EconomyJobState.Completed))
        {
            var shipment = shipments[job.ShipmentId];
            var requirement = requirements[job.RequirementId];
            var payments = FinancialLedger.Where(x => x.Kind == FinancialTransactionKind.JobPayment && x.JobId == job.Id).ToArray();
            var contract = LogisticsContracts.SingleOrDefault(x => x.JobId == job.Id);
            var expectedPayments = contract?.RoadStartedUtc is not null && contract.AcceptedPilotId is null ? 0 : 1;
            if (shipment.State != ShipmentState.Delivered || requirement.State != TransportRequirementState.Fulfilled || payments.Length != expectedPayments ||
                (expectedPayments == 1 && (payments[0].EvidenceId != shipment.DeliveryEvidenceId || payments[0].FromEntityId != requirement.FundingBusinessId ||
                payments[0].ToEntityId != job.PlayerId || payments[0].Amount != requirement.PaymentAmount ||
                payments[0].CurrencyCode != requirement.CurrencyCode || payments[0].OccurredUtc != job.CompletedUtc)) || shipment.DeliveredUtc != job.CompletedUtc)
                throw new InvalidDataException("Completed job cargo and payment records are inconsistent.");
        }
        foreach (var job in Jobs.Where(x => x.State == EconomyJobState.Cancelled && !LogisticsContracts.Any(c => c.JobId == x.Id)))
        {
            var shipment = shipments[job.ShipmentId];
            var release = InventoryLedger.Where(x => x.Kind == InventoryTransactionKind.ShipmentReservationReleased && x.JobId == job.Id).ToArray();
            if (release.Length != 1 || release[0].LocationId != shipment.OriginLocationId || release[0].ResourceId != shipment.ResourceId ||
                release[0].QuantityDelta != shipment.Quantity || release[0].OccurredUtc != job.CancelledUtc || shipment.CancelledUtc != job.CancelledUtc)
                throw new InvalidDataException("Cancelled shipment and reservation-release audit records are inconsistent.");
        }
        ProspectingCampValidation.EnsureValid(this);
        PersistentEconomyValidation.EnsureValid(this);
        PlayerLogisticsValidation.EnsureValid(this);
        return this;
    }
}

public static class EconomyDemand
{
    public static decimal UnmetQuantity(EconomyWorld world, CustomerNeed need)
    {
        world.EnsureValid();
        var available = world.Inventories.SingleOrDefault(x => x.LocationId == need.DestinationLocationId && x.ResourceId == need.ResourceId)?.AvailableQuantity ?? 0;
        return Math.Max(0, need.TargetQuantity - available);
    }
}

/// <summary>Advisory fleet filtering only. It does not create/remove demand and makes no runway, weather or safety decision.</summary>
public static class EconomyDispatch
{
    public static DispatchRecommendation AssessCargoCapacity(EconomyWorld source, string jobId, AircraftDispatchProfile profile)
    {
        var world = source.EnsureValid(); EconomyValidation.Id(jobId, nameof(jobId)); EconomyValidation.Id(profile.Id, "aircraft profile ID");
        EconomyValidation.NonNegative(profile.CargoCapacity, "aircraft cargo capacity");
        var job = world.Jobs.SingleOrDefault(x => x.Id == jobId) ?? throw new InvalidOperationException("Job does not exist.");
        var quantity = world.Requirements.Single(x => x.Id == job.RequirementId).Quantity;
        return profile.CargoCapacity >= quantity
            ? new(true, "Configured cargo capacity can carry this shipment; this is not a flight-safety assessment.")
            : new(false, "Shipment exceeds configured cargo capacity; the underlying customer need remains available.");
    }
}

public static class EconomyLedger
{
    public static decimal Balance(EconomyWorld world, string entityId, string currencyCode) => world.FinancialLedger
        .Where(x => x.CurrencyCode == currencyCode)
        .Sum(x => (x.ToEntityId == entityId ? x.Amount : 0) - (x.FromEntityId == entityId ? x.Amount : 0));
}

public sealed record EconomyTransition(EconomyWorld World, bool Changed);

public static class EconomyKernel
{
    public static EconomyTransition AcceptJob(EconomyWorld source, string jobId, DateTimeOffset acceptedUtc)
    {
        var world = source.EnsureValid(); EconomyValidation.Id(jobId, nameof(jobId));
        if (acceptedUtc == default) throw new ArgumentOutOfRangeException(nameof(acceptedUtc));
        var job = world.Jobs.SingleOrDefault(x => x.Id == jobId) ?? throw new InvalidOperationException("Job does not exist.");
        if (job.State is EconomyJobState.Accepted or EconomyJobState.Completed) return new(world, false);
        var requirement = world.Requirements.Single(x => x.Id == job.RequirementId);
        if (requirement.State != TransportRequirementState.Open) throw new InvalidOperationException("Transport requirement is no longer open.");
        var playerContract = world.LogisticsContracts.SingleOrDefault(x => x.JobId == job.Id);
        if (playerContract is not null)
        {
            var reserved = PlayerLogisticsKernel.AcceptReserved(world, playerContract, acceptedUtc);
            return new((reserved with { Revision = world.Revision + 1 }).EnsureValid(), true);
        }
        var inventoryIndex = world.Inventories.ToList().FindIndex(x => x.LocationId == requirement.OriginLocationId && x.ResourceId == requirement.ResourceId);
        if (inventoryIndex < 0 || world.Inventories[inventoryIndex].AvailableQuantity < requirement.Quantity)
            throw new InvalidOperationException("Origin inventory cannot reserve this shipment.");
        if (world.Shipments.Any(x => x.Id == job.ShipmentId)) throw new InvalidDataException("Planned shipment ID is already in use.");

        var inventories = world.Inventories.ToArray(); inventories[inventoryIndex] = inventories[inventoryIndex] with
        { AvailableQuantity = inventories[inventoryIndex].AvailableQuantity - requirement.Quantity };
        var requirements = world.Requirements.Select(x => x.Id == requirement.Id ? x with { State = TransportRequirementState.Reserved } : x).ToArray();
        var jobs = world.Jobs.Select(x => x.Id == job.Id ? x with { State = EconomyJobState.Accepted, AcceptedUtc = acceptedUtc } : x).ToArray();
        var shipment = new Shipment(job.ShipmentId, job.Id, requirement.ResourceId, requirement.Quantity,
            requirement.OriginLocationId, requirement.DestinationLocationId, ShipmentState.Reserved, requirement.OriginLocationId);
        var inventoryTransaction = new InventoryTransaction(EconomyIds.Derive("inventory-reserve-", shipment.Id), InventoryTransactionKind.ShipmentReserved,
            requirement.OriginLocationId, requirement.ResourceId, -requirement.Quantity, job.Id, acceptedUtc, "Cargo reserved for accepted employed shipment.");
        var next = world with { Revision = world.Revision + 1, Inventories = inventories, Requirements = requirements, Jobs = jobs,
            Shipments = [.. world.Shipments, shipment], InventoryLedger = [.. world.InventoryLedger, inventoryTransaction] };
        return new(next.EnsureValid(), true);
    }

    public static EconomyTransition MarkShipmentLoaded(EconomyWorld source, string jobId)
    {
        var world = source.EnsureValid();
        var job = world.Jobs.Single(x => x.Id == jobId);
        if (job.State != EconomyJobState.Accepted) throw new InvalidOperationException("Job must be accepted before loading.");
        var shipment = world.Shipments.Single(x => x.Id == job.ShipmentId);
        if (shipment.State is ShipmentState.Loaded or ShipmentState.InTransit) return new(world, false);
        if (shipment.State != ShipmentState.Reserved) throw new InvalidOperationException("Shipment is not reserved.");
        return new((world with { Revision = world.Revision + 1, Shipments = world.Shipments.Select(x => x.Id == shipment.Id
            ? x with { State = ShipmentState.Loaded, CurrentLocationId = x.OriginLocationId, LoadedQuantity = x.Quantity } : x).ToArray() }).EnsureValid(), true);
    }

    public static EconomyTransition MarkShipmentInTransit(EconomyWorld source, string jobId)
    {
        var world = source.EnsureValid(); EconomyValidation.Id(jobId, nameof(jobId));
        var job = world.Jobs.SingleOrDefault(x => x.Id == jobId) ?? throw new InvalidOperationException("Job does not exist.");
        if (job.State != EconomyJobState.Accepted) throw new InvalidOperationException("Job must be accepted before cargo can be loaded.");
        var shipment = world.Shipments.Single(x => x.Id == job.ShipmentId);
        if (shipment.State == ShipmentState.InTransit) return new(world, false);
        if (shipment.State is not (ShipmentState.Reserved or ShipmentState.Loaded)) throw new InvalidOperationException("Shipment is not loaded or reserved for departure.");
        var loadedQuantity = shipment.LoadedQuantity > 0 ? shipment.LoadedQuantity : shipment.Quantity;
        var shipments = world.Shipments.Select(x => x.Id == shipment.Id
            ? x with { State = ShipmentState.InTransit, CurrentLocationId = null, LoadedQuantity = loadedQuantity }
            : x).ToArray();
        return new((world with { Revision = world.Revision + 1, Shipments = shipments }).EnsureValid(), true);
    }

    public static EconomyTransition MarkShipmentsInTransit(EconomyWorld source, IReadOnlyList<string> jobIds)
    {
        if (jobIds is null || jobIds.Count == 0) throw new ArgumentException("At least one job is required.", nameof(jobIds));
        if (jobIds.Distinct(StringComparer.Ordinal).Count() != jobIds.Count) throw new ArgumentException("Batch job IDs must be unique.", nameof(jobIds));
        var current = source.EnsureValid(); var changed = false;
        foreach (var jobId in jobIds)
        {
            var result = MarkShipmentInTransit(current, jobId); current = result.World; changed |= result.Changed;
        }
        return changed ? new((current with { Revision = source.Revision + 1 }).EnsureValid(), true) : new(source, false);
    }

    public static EconomyTransition ApplyCargoTransfers(EconomyWorld source, IReadOnlyList<ShipmentCargoTransferRequest> requests)
    {
        if (requests is null || requests.Count == 0) throw new ArgumentException("At least one cargo transfer is required.", nameof(requests));
        if (requests.Select(x => x.JobId).Distinct(StringComparer.Ordinal).Count() != requests.Count ||
            requests.Select(x => x.CommandId).Distinct(StringComparer.Ordinal).Count() != requests.Count)
            throw new ArgumentException("Batch cargo transfers must name unique jobs and commands.", nameof(requests));
        var current = source.EnsureValid(); var changed = false;
        foreach (var request in requests)
        {
            var result = ApplyCargoTransfer(current, request); current = result.World; changed |= result.Changed;
        }
        return changed ? new((current with { Revision = source.Revision + 1 }).EnsureValid(), true) : new(source, false);
    }

    public static EconomyTransition ApplyCargoTransfer(EconomyWorld source, ShipmentCargoTransferRequest supplied)
    {
        var world = source.EnsureValid();
        EconomyValidation.Id(supplied.Id, nameof(supplied.Id)); EconomyValidation.Id(supplied.CommandId, nameof(supplied.CommandId));
        EconomyValidation.Id(supplied.JobId, nameof(supplied.JobId));
        if (supplied.DesiredLoadedQuantity < 0 || !double.IsFinite(supplied.Latitude) || !double.IsFinite(supplied.Longitude) ||
            supplied.Latitude is < -90 or > 90 || supplied.Longitude is < -180 or > 180 || supplied.OccurredUtc == default)
            throw new InvalidDataException("Cargo transfer request is invalid.");
        var prior = world.CargoTransfers.SingleOrDefault(x => x.CommandId == supplied.CommandId);
        if (prior is not null) return new(world, false);
        var job = world.Jobs.SingleOrDefault(x => x.Id == supplied.JobId) ?? throw new InvalidOperationException("Cargo job does not exist.");
        if (job.State is EconomyJobState.Completed or EconomyJobState.Cancelled or EconomyJobState.Forfeited)
            throw new InvalidOperationException("Terminal job cargo cannot be changed through an active shipment.");
        if (job.State != EconomyJobState.Accepted) throw new InvalidOperationException("Cargo job must be accepted before transfer.");
        var requirement = world.Requirements.Single(x => x.Id == job.RequirementId);
        var shipment = world.Shipments.Single(x => x.Id == job.ShipmentId);
        if (shipment.State is ShipmentState.Delivered or ShipmentState.Cancelled or ShipmentState.Forfeited)
            throw new InvalidOperationException("Terminal shipment cargo cannot be changed.");
        if (supplied.DesiredLoadedQuantity > shipment.Quantity - shipment.DeliveredQuantity + .001m)
            throw new InvalidOperationException("Requested aircraft cargo exceeds the shipment cargo that still exists.");
        var delta = supplied.DesiredLoadedQuantity - shipment.LoadedQuantity;
        if (Math.Abs(delta) <= .001m) return new(world, false);

        var atOrigin = supplied.CurrentLocationId == shipment.OriginLocationId;
        var atDestination = supplied.CurrentLocationId == shipment.DestinationLocationId;
        ShipmentCargoTransferKind kind;
        decimal delivered = shipment.DeliveredQuantity, forfeited = shipment.ForfeitedQuantity;
        var jobs = world.Jobs.ToArray(); var requirements = world.Requirements.ToArray();
        var inventories = world.Inventories.ToList(); var inventoryLedger = world.InventoryLedger.ToList();
        var ledger = world.FinancialLedger.ToList(); var players = world.Players.ToArray();
        var quantity = Math.Abs(delta); string details;
        if (delta > 0)
        {
            if (!atOrigin) throw new InvalidOperationException("Reserved shipment cargo is available only at its configured origin.");
            if (shipment.State == ShipmentState.InTransit) throw new InvalidOperationException("An InTransit shipment cannot load additional reserved cargo.");
            kind = ShipmentCargoTransferKind.LoadedToAircraft;
            details = $"{quantity:0.###} kg loaded from reserved origin cargo after verified simulator read-back.";
        }
        else if (atOrigin && shipment.State != ShipmentState.InTransit)
        {
            kind = ShipmentCargoTransferKind.UnloadedAtOrigin;
            details = $"{quantity:0.###} kg unloaded back into the shipment's reserved origin cargo after verified simulator read-back.";
        }
        else if (atDestination)
        {
            kind = ShipmentCargoTransferKind.UnloadedAtDestination; delivered += quantity;
            if (delivered > shipment.Quantity + .001m) throw new InvalidOperationException("Destination delivery would exceed the shipment quantity.");
            var index = inventories.FindIndex(x => x.LocationId == shipment.DestinationLocationId && x.ResourceId == shipment.ResourceId);
            if (index < 0) inventories.Add(new(shipment.DestinationLocationId, shipment.ResourceId, quantity));
            else inventories[index] = inventories[index] with { AvailableQuantity = inventories[index].AvailableQuantity + quantity };
            inventoryLedger.Add(new InventoryTransaction(EconomyIds.Derive("inventory-transfer-", supplied.Id), InventoryTransactionKind.PartialDeliveryReceived,
                shipment.DestinationLocationId, shipment.ResourceId, quantity, job.Id, supplied.OccurredUtc,
                "Verified physical cargo transfer received at the shipment destination.", CommandId: supplied.CommandId));
            details = $"{quantity:0.###} kg unloaded into destination inventory after verified simulator read-back.";
        }
        else
        {
            if (!supplied.ConfirmForfeit) throw new InvalidOperationException("CONFIRM_FORFEIT: This cargo belongs to an active delivery. Unloading it here will forfeit the job and no payment will be made.");
            kind = ShipmentCargoTransferKind.UnloadedAndForfeited; forfeited = shipment.Quantity - shipment.DeliveredQuantity;
            const string reason = "Active shipment cargo was unloaded away from its valid destination and the job was forfeited.";
            var reservedAtOrigin = Math.Max(0, shipment.Quantity - shipment.DeliveredQuantity - shipment.LoadedQuantity);
            if (reservedAtOrigin > 0)
            {
                var originIndex = inventories.FindIndex(x => x.LocationId == shipment.OriginLocationId && x.ResourceId == shipment.ResourceId);
                if (originIndex < 0) inventories.Add(new(shipment.OriginLocationId, shipment.ResourceId, reservedAtOrigin));
                else inventories[originIndex] = inventories[originIndex] with { AvailableQuantity = inventories[originIndex].AvailableQuantity + reservedAtOrigin };
                inventoryLedger.Add(new InventoryTransaction(EconomyIds.Derive("inventory-forfeit-release-", supplied.Id),
                    InventoryTransactionKind.ShipmentReservationReleased, shipment.OriginLocationId, shipment.ResourceId,
                    reservedAtOrigin, job.Id, supplied.OccurredUtc, "Reserved cargo that never left the origin was released after shipment forfeiture.", CommandId: supplied.CommandId));
            }
            jobs = jobs.Select(x => x.Id == job.Id ? x with { State = EconomyJobState.Forfeited, CancelledUtc = supplied.OccurredUtc, CancellationReason = reason } : x).ToArray();
            requirements = requirements.Select(x => x.Id == requirement.Id ? x with { State = TransportRequirementState.Cancelled, CancelledUtc = supplied.OccurredUtc, CancellationReason = reason } : x).ToArray();
            details = $"{quantity:0.###} kg physically unloaded at an invalid location; the complete active shipment was forfeited without payment.";
        }

        var nextState = kind == ShipmentCargoTransferKind.UnloadedAndForfeited ? ShipmentState.Forfeited :
            shipment.State == ShipmentState.InTransit ? ShipmentState.InTransit : supplied.DesiredLoadedQuantity > 0 ? ShipmentState.Loaded : ShipmentState.Reserved;
        string? currentLocation = nextState switch { ShipmentState.Reserved or ShipmentState.Loaded => shipment.OriginLocationId,
            ShipmentState.InTransit => null, ShipmentState.Forfeited => supplied.CurrentLocationId, _ => shipment.CurrentLocationId };
        var updatedShipment = shipment with { State = nextState, CurrentLocationId = currentLocation,
            LoadedQuantity = kind == ShipmentCargoTransferKind.UnloadedAndForfeited ? 0 : supplied.DesiredLoadedQuantity,
            DeliveredQuantity = delivered, ForfeitedQuantity = forfeited,
            CancelledUtc = kind == ShipmentCargoTransferKind.UnloadedAndForfeited ? supplied.OccurredUtc : shipment.CancelledUtc,
            CancellationReason = kind == ShipmentCargoTransferKind.UnloadedAndForfeited ? "Shipment forfeited after cargo was unloaded away from its valid destination." : shipment.CancellationReason };
        var transfer = new ShipmentCargoTransfer(supplied.Id, supplied.CommandId, job.Id, shipment.Id, kind, quantity,
            supplied.CurrentLocationId, supplied.Latitude, supplied.Longitude, supplied.OccurredUtc, details);

        if (kind == ShipmentCargoTransferKind.UnloadedAtDestination && delivered >= shipment.Quantity - .001m && supplied.DesiredLoadedQuantity <= .001m)
        {
            var evidence = supplied.CompletionEvidence?.EnsureValid() ?? throw new InvalidOperationException("Complete physical delivery requires verified flight-attempt evidence.");
            if (evidence.JobId != job.Id || evidence.ShipmentId != shipment.Id || evidence.OriginLocationId != shipment.OriginLocationId || evidence.DestinationLocationId != shipment.DestinationLocationId)
                throw new InvalidOperationException("Delivery evidence does not match the physical cargo transfer.");
            var contract = world.LogisticsContracts.SingleOrDefault(x => x.JobId == job.Id);
            if (contract is null)
            {
                var transactionId = EconomyIds.Derive("payment-", job.Id);
                if (ledger.Any(x => x.Id == transactionId || x.JobId == job.Id)) throw new InvalidDataException("A payment already exists for this job.");
                ledger.Add(new(transactionId, FinancialTransactionKind.JobPayment, job.Id, evidence.Id,
                    requirement.FundingBusinessId, job.PlayerId, requirement.PaymentAmount, requirement.CurrencyCode, evidence.VerifiedUtc));
            }
            jobs = jobs.Select(x => x.Id == job.Id ? x with { State = EconomyJobState.Completed, CompletedUtc = evidence.VerifiedUtc } : x).ToArray();
            requirements = requirements.Select(x => x.Id == requirement.Id ? x with { State = TransportRequirementState.Fulfilled } : x).ToArray();
            updatedShipment = updatedShipment with { State = ShipmentState.Delivered, CurrentLocationId = shipment.DestinationLocationId,
                LoadedQuantity = 0, DeliveredQuantity = shipment.Quantity, DeliveryEvidenceId = evidence.Id, DeliveredUtc = evidence.VerifiedUtc };
            var interim = world with { FinancialLedger = ledger };
            players = players.Select(x => x.Id == job.PlayerId ? x with { CachedBalance = EconomyLedger.Balance(interim, x.Id, x.CurrencyCode) } : x).ToArray();
        }
        var shipments = world.Shipments.Select(x => x.Id == shipment.Id ? updatedShipment : x).ToArray();
        var changedWorld = world with { Inventories = inventories, InventoryLedger = inventoryLedger, Jobs = jobs,
            Requirements = requirements, Shipments = shipments, FinancialLedger = ledger, Players = players,
            CargoTransfers = [.. world.CargoTransfers, transfer] };
        if (updatedShipment.State == ShipmentState.Forfeited && world.LogisticsContracts.SingleOrDefault(x => x.JobId == job.Id) is { } forfeitedContract)
            changedWorld = PlayerLogisticsKernel.Forfeit(changedWorld, forfeitedContract.Id, supplied.OccurredUtc);
        if (updatedShipment.State == ShipmentState.Delivered)
        {
            var contract = world.LogisticsContracts.SingleOrDefault(x => x.JobId == job.Id);
            changedWorld = contract is null
                ? EconomyJobBoard.Maintain(changedWorld, supplied.OccurredUtc, incrementRevision: false).World
                : PlayerLogisticsKernel.SettleAir(changedWorld, contract.Id, updatedShipment.DeliveryEvidenceId!, supplied.OccurredUtc);
        }
        return new((changedWorld with { Revision = world.Revision + 1 }).EnsureValid(), true);
    }

    public static EconomyTransition AbandonIncompletePrototypeShipment(EconomyWorld source, string jobId, DateTimeOffset cancelledUtc)
    {
        var world = source.EnsureValid(); EconomyValidation.Id(jobId, nameof(jobId));
        if (cancelledUtc == default) throw new ArgumentOutOfRangeException(nameof(cancelledUtc));
        var job = world.Jobs.SingleOrDefault(x => x.Id == jobId) ?? throw new InvalidOperationException("Job does not exist.");
        if (world.LogisticsContracts.Any(x => x.JobId == job.Id))
            throw new InvalidOperationException("An accepted player-operation contract cannot be skipped; pilot abandonment and disputes are not implemented in V0.");
        if (job.State == EconomyJobState.Cancelled) return new(world, false);
        if (job.State != EconomyJobState.Accepted) throw new InvalidOperationException("Only the active incomplete prototype shipment can be skipped.");
        if (world.FinancialLedger.Any(x => x.JobId == job.Id)) throw new InvalidOperationException("A paid or completed job cannot be skipped.");
        var requirement = world.Requirements.Single(x => x.Id == job.RequirementId);
        var shipment = world.Shipments.Single(x => x.Id == job.ShipmentId);
        if (shipment.State is not (ShipmentState.Reserved or ShipmentState.Loaded or ShipmentState.InTransit) || shipment.DeliveryEvidenceId is not null)
            throw new InvalidOperationException("Only a reserved or in-transit shipment without delivery evidence can be skipped.");

        const string reason = "Player explicitly skipped an incomplete prototype mission for retesting.";
        var inventories = world.Inventories.ToList();
        var returnQuantity = shipment.Quantity - shipment.DeliveredQuantity;
        var index = inventories.FindIndex(x => x.LocationId == shipment.OriginLocationId && x.ResourceId == shipment.ResourceId);
        if (index < 0) inventories.Add(new(shipment.OriginLocationId, shipment.ResourceId, returnQuantity));
        else inventories[index] = inventories[index] with { AvailableQuantity = inventories[index].AvailableQuantity + returnQuantity };
        var releaseId = EconomyIds.Derive("inventory-release-", shipment.Id);
        if (world.InventoryLedger.Any(x => x.Id == releaseId)) throw new InvalidDataException("Shipment release audit entry already exists without a cancelled job.");
        var release = new InventoryTransaction(releaseId, InventoryTransactionKind.ShipmentReservationReleased,
            shipment.OriginLocationId, shipment.ResourceId, returnQuantity, job.Id, cancelledUtc, reason);
        var requirements = world.Requirements.Select(x => x.Id == requirement.Id ? x with
        { State = TransportRequirementState.Cancelled, CancelledUtc = cancelledUtc, CancellationReason = reason } : x).ToArray();
        var jobs = world.Jobs.Select(x => x.Id == job.Id ? x with
        { State = EconomyJobState.Cancelled, CancelledUtc = cancelledUtc, CancellationReason = reason } : x).ToArray();
        var shipments = world.Shipments.Select(x => x.Id == shipment.Id ? x with
        { State = ShipmentState.Cancelled, CurrentLocationId = shipment.OriginLocationId, LoadedQuantity = 0,
            CancelledUtc = cancelledUtc, CancellationReason = reason } : x).ToArray();
        var cancelled = world with { Inventories = inventories, Requirements = requirements, Jobs = jobs, Shipments = shipments,
            InventoryLedger = [.. world.InventoryLedger, release] };
        var maintained = EconomyJobBoard.Maintain(cancelled, cancelledUtc, incrementRevision: false);
        return new((maintained.World with { Revision = world.Revision + 1 }).EnsureValid(), true);
    }

    public static EconomyTransition CompleteDelivery(EconomyWorld source, VerifiedDeliveryEvidence supplied)
    {
        var world = source.EnsureValid(); var evidence = supplied.EnsureValid();
        var job = world.Jobs.SingleOrDefault(x => x.Id == evidence.JobId) ?? throw new InvalidOperationException("Delivery job does not exist.");
        if (job.ShipmentId != evidence.ShipmentId) throw new InvalidOperationException("Delivery evidence names the wrong shipment.");
        if (job.State == EconomyJobState.Completed)
        {
            var delivered = world.Shipments.Single(x => x.Id == job.ShipmentId);
            if (delivered.DeliveryEvidenceId != evidence.Id || delivered.DeliveredUtc != evidence.VerifiedUtc ||
                delivered.OriginLocationId != evidence.OriginLocationId || delivered.DestinationLocationId != evidence.DestinationLocationId)
                throw new InvalidOperationException("Job was completed with different delivery evidence.");
            return new(world, false);
        }
        if (job.State != EconomyJobState.Accepted) throw new InvalidOperationException("Job must be accepted before delivery.");
        var requirement = world.Requirements.Single(x => x.Id == job.RequirementId);
        var shipment = world.Shipments.Single(x => x.Id == job.ShipmentId);
        if (shipment.State is not (ShipmentState.Reserved or ShipmentState.Loaded or ShipmentState.InTransit)) throw new InvalidOperationException("Shipment is not deliverable.");
        if (evidence.OriginLocationId != shipment.OriginLocationId || evidence.DestinationLocationId != shipment.DestinationLocationId)
            throw new InvalidOperationException("Delivery evidence names the wrong route.");
        var player = world.Players.Single(x => x.Id == job.PlayerId);
        if (player.CurrencyCode != requirement.CurrencyCode) throw new InvalidDataException("Job payment currency does not match the player account.");
        var contract = world.LogisticsContracts.SingleOrDefault(x => x.JobId == job.Id);
        var transactionId = EconomyIds.Derive("payment-", job.Id);
        if (contract is null && world.FinancialLedger.Any(x => x.Id == transactionId || x.JobId == job.Id))
            throw new InvalidDataException("A payment already exists for this job.");

        var inventories = world.Inventories.ToList();
        var destinationIndex = inventories.FindIndex(x => x.LocationId == shipment.DestinationLocationId && x.ResourceId == shipment.ResourceId);
        var remainingDelivery = shipment.Quantity - shipment.DeliveredQuantity;
        if (remainingDelivery <= 0) throw new InvalidDataException("Shipment inventory was already fully received without completion.");
        if (destinationIndex < 0) inventories.Add(new(shipment.DestinationLocationId, shipment.ResourceId, remainingDelivery));
        else inventories[destinationIndex] = inventories[destinationIndex] with { AvailableQuantity = inventories[destinationIndex].AvailableQuantity + remainingDelivery };
        var ledger = contract is null ? world.FinancialLedger.Append(new FinancialTransaction(transactionId, FinancialTransactionKind.JobPayment, job.Id, evidence.Id,
            requirement.FundingBusinessId, job.PlayerId, requirement.PaymentAmount, requirement.CurrencyCode, evidence.VerifiedUtc)).ToArray() : world.FinancialLedger.ToArray();
        var jobs = world.Jobs.Select(x => x.Id == job.Id ? x with { State = EconomyJobState.Completed, CompletedUtc = evidence.VerifiedUtc } : x).ToArray();
        var requirements = world.Requirements.Select(x => x.Id == requirement.Id ? x with { State = TransportRequirementState.Fulfilled } : x).ToArray();
        var shipments = world.Shipments.Select(x => x.Id == shipment.Id ? x with
        { State = ShipmentState.Delivered, CurrentLocationId = shipment.DestinationLocationId, LoadedQuantity = 0,
            DeliveredQuantity = shipment.Quantity, DeliveryEvidenceId = evidence.Id, DeliveredUtc = evidence.VerifiedUtc } : x).ToArray();
        var interim = world with { FinancialLedger = ledger };
        var players = world.Players.Select(x => x.Id == job.PlayerId ? x with { CachedBalance = EconomyLedger.Balance(interim, x.Id, x.CurrencyCode) } : x).ToArray();
        var inventoryTransaction = new InventoryTransaction(EconomyIds.Derive("inventory-delivery-", shipment.Id), InventoryTransactionKind.DeliveryReceived,
            shipment.DestinationLocationId, shipment.ResourceId, remainingDelivery, job.Id, evidence.VerifiedUtc, "Verified shipment received into customer inventory.");
        var deliveredWorld = world with { Inventories = inventories, Requirements = requirements, Jobs = jobs,
            Shipments = shipments, FinancialLedger = ledger, Players = players, InventoryLedger = [.. world.InventoryLedger, inventoryTransaction] };
        deliveredWorld = contract is null ? EconomyJobBoard.Maintain(deliveredWorld, evidence.VerifiedUtc, incrementRevision: false).World
            : PlayerLogisticsKernel.SettleAir(deliveredWorld, contract.Id, evidence.Id, evidence.VerifiedUtc);
        var next = deliveredWorld with { Revision = world.Revision + 1 };
        return new(next.EnsureValid(), true);
    }
}

internal static class EconomyIds
{
    public static string Derive(string prefix, string source)
    {
        var candidate = prefix + source;
        if (candidate.Length <= 64) return candidate;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        return prefix + hash[..(64 - prefix.Length)];
    }
}

internal static partial class EconomyValidation
{
    private static readonly Regex IdPattern = IdRegex();
    public static void Id(string? value, string name) { if (value is null || !IdPattern.IsMatch(value)) throw new InvalidDataException($"{name} is invalid."); }
    public static void Text(string? value, string name, int maximum) { if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > maximum) throw new InvalidDataException($"{name} is invalid."); }
    public static void Currency(string? value) { if (value is null || !Regex.IsMatch(value, "\\A[A-Z][A-Z0-9]{2,7}\\z")) throw new InvalidDataException("Currency code is invalid."); }
    public static void Positive(decimal value, string name) { if (value <= 0) throw new InvalidDataException($"{name} must be positive."); }
    public static void NonNegative(decimal value, string name) { if (value < 0) throw new InvalidDataException($"{name} cannot be negative."); }
    public static void Collection<T>(IReadOnlyList<T>? values, string name) { if (values is null || values.Count > 100_000) throw new InvalidDataException($"Economy {name} collection is invalid."); }
    public static void Unique(IEnumerable<string> ids, string name) { var all = ids.ToArray(); if (all.Distinct(StringComparer.Ordinal).Count() != all.Length) throw new InvalidDataException($"Duplicate {name} ID."); }
    public static void Reference<T>(IReadOnlyDictionary<string, T> values, string id, string name) { Id(id, name); if (!values.ContainsKey(id)) throw new InvalidDataException($"Unknown {name}."); }
    [GeneratedRegex("\\A[a-z0-9][a-z0-9._-]{0,63}\\z", RegexOptions.CultureInvariant)] private static partial Regex IdRegex();
}
