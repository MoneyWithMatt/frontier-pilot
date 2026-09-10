using System.Text.RegularExpressions;

namespace FrontierAirportBuilder.Core;

public enum EconomicOwnerKind { System, Player }
public enum EconomicLocationKind { Gateway, Mine, Community, Lodge }
public enum EconomicLocationStatus { PrototypeOnly, ResearchCandidate, MsfsVerified, ActiveCustomer, DormantCandidate }
public enum TransportRequirementState { Open, Reserved, Fulfilled }
public enum EconomyJobState { Offered, Accepted, Completed }
public enum ShipmentState { Available, Reserved, InTransit, Delivered, Consumed, Sold }
public enum FinancialTransactionKind { JobPayment }

public sealed record EconomicLocation(string Id, string Name, EconomicLocationKind Kind, EconomicOwnerKind Owner,
    EconomicLocationStatus Status);
public sealed record EconomicBusiness(string Id, string Name, EconomicOwnerKind Owner);
public sealed record ResourceType(string Id, string Name, string Category, string UnitName);
public sealed record InventoryBalance(string LocationId, string ResourceId, decimal AvailableQuantity);
public sealed record CustomerNeed(string Id, string DestinationLocationId, string ResourceId, decimal TargetQuantity);
public sealed record TransportRequirement(string Id, string NeedId, string OriginLocationId, string DestinationLocationId,
    string ResourceId, decimal Quantity, decimal PaymentAmount, string CurrencyCode, string FundingBusinessId,
    TransportRequirementState State);
public sealed record EconomyJob(string Id, string RequirementId, string EmployerBusinessId, string PlayerId,
    string ShipmentId, EconomyJobState State, DateTimeOffset? AcceptedUtc = null, DateTimeOffset? CompletedUtc = null);
public sealed record Shipment(string Id, string JobId, string ResourceId, decimal Quantity, string OriginLocationId,
    string DestinationLocationId, ShipmentState State, string? CurrentLocationId, string? DeliveryEvidenceId = null,
    DateTimeOffset? DeliveredUtc = null);
public sealed record PlayerAccount(string Id, string EmployerBusinessId, string CurrencyCode, decimal CachedBalance);
public sealed record FinancialTransaction(string Id, FinancialTransactionKind Kind, string JobId, string EvidenceId,
    string FromEntityId, string ToEntityId, decimal Amount, string CurrencyCode, DateTimeOffset OccurredUtc);
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
    public EconomyWorld EnsureValid()
    {
        EconomyValidation.Id(Id, nameof(Id));
        if (Revision < 1) throw new InvalidDataException("Economy revision is invalid.");
        EconomyValidation.Collection(Locations, nameof(Locations)); EconomyValidation.Collection(Businesses, nameof(Businesses));
        EconomyValidation.Collection(Resources, nameof(Resources)); EconomyValidation.Collection(Inventories, nameof(Inventories));
        EconomyValidation.Collection(Needs, nameof(Needs)); EconomyValidation.Collection(Requirements, nameof(Requirements));
        EconomyValidation.Collection(Players, nameof(Players)); EconomyValidation.Collection(Jobs, nameof(Jobs));
        EconomyValidation.Collection(Shipments, nameof(Shipments)); EconomyValidation.Collection(FinancialLedger, nameof(FinancialLedger));

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
            EconomyValidation.Id(x.Id, "job ID"); EconomyValidation.Reference(requirements, x.RequirementId, "job requirement");
            EconomyValidation.Reference(businesses, x.EmployerBusinessId, "job employer"); EconomyValidation.Reference(players, x.PlayerId, "job player");
            EconomyValidation.Id(x.ShipmentId, "job shipment ID");
            if (x.State == EconomyJobState.Offered && (x.AcceptedUtc != null || x.CompletedUtc != null || shipments.ContainsKey(x.ShipmentId)))
                throw new InvalidDataException("Offered job has accepted or shipment state.");
            if (x.State == EconomyJobState.Accepted && (x.AcceptedUtc == null || x.CompletedUtc != null || !shipments.ContainsKey(x.ShipmentId)))
                throw new InvalidDataException("Accepted job state is incomplete.");
            if (x.State == EconomyJobState.Completed && (x.AcceptedUtc == null || x.CompletedUtc == null || !shipments.ContainsKey(x.ShipmentId)))
                throw new InvalidDataException("Completed job state is incomplete.");
            var requirement = requirements[x.RequirementId];
            if (x.State == EconomyJobState.Offered && requirement.State != TransportRequirementState.Open ||
                x.State == EconomyJobState.Accepted && requirement.State != TransportRequirementState.Reserved ||
                x.State == EconomyJobState.Completed && requirement.State != TransportRequirementState.Fulfilled)
                throw new InvalidDataException("Job and transport requirement states disagree.");
            if (x.State == EconomyJobState.Accepted && shipments[x.ShipmentId].State is not (ShipmentState.Reserved or ShipmentState.InTransit) ||
                x.State == EconomyJobState.Completed && shipments[x.ShipmentId].State != ShipmentState.Delivered)
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
            if (x.State == ShipmentState.Reserved && x.CurrentLocationId != x.OriginLocationId ||
                x.State == ShipmentState.InTransit && x.CurrentLocationId is not null)
                throw new InvalidDataException("Shipment state and location disagree.");
            if (x.State == ShipmentState.Delivered && (x.CurrentLocationId != x.DestinationLocationId || x.DeliveryEvidenceId is null || x.DeliveredUtc is null))
                throw new InvalidDataException("Delivered shipment state is incomplete.");
            if (x.State != ShipmentState.Delivered && (x.DeliveryEvidenceId is not null || x.DeliveredUtc is not null))
                throw new InvalidDataException("Undelivered shipment contains delivery evidence.");
        }
        foreach (var x in FinancialLedger)
        {
            EconomyValidation.Id(x.Id, "transaction ID"); EconomyValidation.Reference(jobs, x.JobId, "transaction job");
            EconomyValidation.Id(x.EvidenceId, "transaction evidence ID"); EconomyValidation.Id(x.FromEntityId, "transaction source"); EconomyValidation.Id(x.ToEntityId, "transaction destination");
            EconomyValidation.Positive(x.Amount, "transaction amount"); EconomyValidation.Currency(x.CurrencyCode);
            if (x.OccurredUtc == default) throw new InvalidDataException("Financial transaction timestamp is missing.");
            var paidJob = jobs[x.JobId];
            if (paidJob.State != EconomyJobState.Completed || !shipments.TryGetValue(paidJob.ShipmentId, out var paidShipment) ||
                paidShipment.State != ShipmentState.Delivered || paidShipment.DeliveryEvidenceId != x.EvidenceId)
                throw new InvalidDataException("Financial transaction exists without its completed delivery.");
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
            if (shipment.State != ShipmentState.Delivered || requirement.State != TransportRequirementState.Fulfilled || payments.Length != 1 ||
                payments[0].EvidenceId != shipment.DeliveryEvidenceId || payments[0].FromEntityId != requirement.FundingBusinessId ||
                payments[0].ToEntityId != job.PlayerId || payments[0].Amount != requirement.PaymentAmount ||
                payments[0].CurrencyCode != requirement.CurrencyCode || payments[0].OccurredUtc != job.CompletedUtc || shipment.DeliveredUtc != job.CompletedUtc)
                throw new InvalidDataException("Completed job cargo and payment records are inconsistent.");
        }
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
        if (world.Jobs.Any(x => x.PlayerId == job.PlayerId && x.State == EconomyJobState.Accepted))
            throw new InvalidOperationException("The employed player already has an active shipment.");
        var requirement = world.Requirements.Single(x => x.Id == job.RequirementId);
        if (requirement.State != TransportRequirementState.Open) throw new InvalidOperationException("Transport requirement is no longer open.");
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
        var next = world with { Revision = world.Revision + 1, Inventories = inventories, Requirements = requirements, Jobs = jobs, Shipments = [.. world.Shipments, shipment] };
        return new(next.EnsureValid(), true);
    }

    public static EconomyTransition MarkShipmentInTransit(EconomyWorld source, string jobId)
    {
        var world = source.EnsureValid(); EconomyValidation.Id(jobId, nameof(jobId));
        var job = world.Jobs.SingleOrDefault(x => x.Id == jobId) ?? throw new InvalidOperationException("Job does not exist.");
        if (job.State != EconomyJobState.Accepted) throw new InvalidOperationException("Job must be accepted before cargo can be loaded.");
        var shipment = world.Shipments.Single(x => x.Id == job.ShipmentId);
        if (shipment.State == ShipmentState.InTransit) return new(world, false);
        if (shipment.State != ShipmentState.Reserved) throw new InvalidOperationException("Shipment is not reserved for loading.");
        var shipments = world.Shipments.Select(x => x.Id == shipment.Id
            ? x with { State = ShipmentState.InTransit, CurrentLocationId = null }
            : x).ToArray();
        return new((world with { Revision = world.Revision + 1, Shipments = shipments }).EnsureValid(), true);
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
        if (shipment.State is not (ShipmentState.Reserved or ShipmentState.InTransit)) throw new InvalidOperationException("Shipment is not deliverable.");
        if (evidence.OriginLocationId != shipment.OriginLocationId || evidence.DestinationLocationId != shipment.DestinationLocationId)
            throw new InvalidOperationException("Delivery evidence names the wrong route.");
        var player = world.Players.Single(x => x.Id == job.PlayerId);
        if (player.CurrencyCode != requirement.CurrencyCode) throw new InvalidDataException("Job payment currency does not match the player account.");
        var transactionId = "payment-" + job.Id;
        if (world.FinancialLedger.Any(x => x.Id == transactionId || x.JobId == job.Id))
            throw new InvalidDataException("A payment already exists for this job.");

        var inventories = world.Inventories.ToList();
        var destinationIndex = inventories.FindIndex(x => x.LocationId == shipment.DestinationLocationId && x.ResourceId == shipment.ResourceId);
        if (destinationIndex < 0) inventories.Add(new(shipment.DestinationLocationId, shipment.ResourceId, shipment.Quantity));
        else inventories[destinationIndex] = inventories[destinationIndex] with { AvailableQuantity = inventories[destinationIndex].AvailableQuantity + shipment.Quantity };
        var transaction = new FinancialTransaction(transactionId, FinancialTransactionKind.JobPayment, job.Id, evidence.Id,
            requirement.FundingBusinessId, job.PlayerId, requirement.PaymentAmount, requirement.CurrencyCode, evidence.VerifiedUtc);
        var ledger = world.FinancialLedger.Append(transaction).ToArray();
        var jobs = world.Jobs.Select(x => x.Id == job.Id ? x with { State = EconomyJobState.Completed, CompletedUtc = evidence.VerifiedUtc } : x).ToArray();
        var requirements = world.Requirements.Select(x => x.Id == requirement.Id ? x with { State = TransportRequirementState.Fulfilled } : x).ToArray();
        var shipments = world.Shipments.Select(x => x.Id == shipment.Id ? x with
        { State = ShipmentState.Delivered, CurrentLocationId = shipment.DestinationLocationId, DeliveryEvidenceId = evidence.Id, DeliveredUtc = evidence.VerifiedUtc } : x).ToArray();
        var interim = world with { FinancialLedger = ledger };
        var players = world.Players.Select(x => x.Id == job.PlayerId ? x with { CachedBalance = EconomyLedger.Balance(interim, x.Id, x.CurrencyCode) } : x).ToArray();
        var next = world with { Revision = world.Revision + 1, Inventories = inventories, Requirements = requirements, Jobs = jobs,
            Shipments = shipments, FinancialLedger = ledger, Players = players };
        return new(next.EnsureValid(), true);
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
