using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed record SitePresenceAssessment(VerifiedSitePresenceEvidence? Evidence, string Reason)
{ public bool IsVerified => Evidence is not null; }

public interface ISitePresenceEvidenceProvider
{
    SitePresenceAssessment Assess(SavedAirport airport, DateTimeOffset now);
}

public sealed record SitePresenceOptions(double RadiusMeters = 250, TimeSpan? MaximumAge = null)
{
    public TimeSpan Freshness => MaximumAge ?? TimeSpan.FromSeconds(3);
    public SitePresenceOptions EnsureValid()
    {
        if (!double.IsFinite(RadiusMeters) || RadiusMeters is < 10 or > 2_000 || Freshness <= TimeSpan.Zero || Freshness > TimeSpan.FromSeconds(3))
            throw new InvalidDataException("Site-presence options are invalid.");
        return this;
    }
}

/// <summary>Creates site-presence evidence only from the explicit, currently controlled walking-avatar proxy.</summary>
public sealed class GuardedAvatarSitePresence(Func<TrackingWorldState?> current, SitePresenceOptions? configured = null) : ISitePresenceEvidenceProvider
{
    private readonly SitePresenceOptions options = (configured ?? new()).EnsureValid();
    public SitePresenceAssessment Assess(SavedAirport airport, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(airport); if (now == default) throw new ArgumentOutOfRangeException(nameof(now));
        var world = current();
        if (world is null) return new(null, "MSFS is disconnected; live walking-avatar presence is required.");
        try { world = world.EnsureValid(); } catch (Exception error) { return new(null, "Live tracking state was refused: " + error.Message); }
        if (world.PlayerMode != PlayerControlMode.OnFoot || world.Player is not { } avatar)
            return new(null, "Exit the aircraft; only the guarded live walking-avatar position can establish this site.");
        if (!avatar.IsOnGround) return new(null, "The walking avatar must be on the ground at the site.");
        var age = now - avatar.ObservedUtc;
        if (age < TimeSpan.Zero || age > options.Freshness)
            return new(null, $"Walking-avatar telemetry is stale; a sample no older than {options.Freshness.TotalSeconds:0} seconds is required.");
        var distance = WorldTracking.DistanceMeters(avatar.Latitude, avatar.Longitude,
            airport.Definition.Latitude, airport.Definition.Longitude);
        if (distance > options.RadiusMeters)
            return new(null, string.Create(CultureInfo.InvariantCulture,
                $"Walking avatar is {distance:F1} m from the site; founding requires {options.RadiusMeters:F0} m or less."));
        var source = string.Join("|", airport.Id.ToString("N"), avatar.ObjectId, avatar.ObservedUtc.UtcTicks);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        var evidence = new VerifiedSitePresenceEvidence("site-presence-" + hash[..40], "guarded-msfs-avatar-v0",
            airport.Id.ToString("N"), avatar.ObjectId, avatar.Latitude, avatar.Longitude, distance,
            options.RadiusMeters, avatar.ObservedUtc, now, string.IsNullOrWhiteSpace(world.IntegrationDetails)
                ? "MSFS guarded explicit avatar proxy; integration details unavailable" : world.IntegrationDetails).EnsureValid();
        return new(evidence, string.Create(CultureInfo.InvariantCulture,
            $"Live guarded walking avatar verified {distance:F1} m from the airport anchor."));
    }
}

public sealed record OperationAirportDisplay(Guid AirportId, string Name, string Identifier, string Location,
    bool IsLinked, bool CanFound, string FoundingReason)
{ public override string ToString() => $"{Name} ({Identifier})"; }
public enum OperationsMapLocationKind { Gateway, SystemLocation, PlayerAirport, PlayerOperation }
public sealed record EconomicInventoryDisplay(string ResourceId, string ResourceName, decimal Quantity, string UnitName,
    decimal? Capacity = null, decimal ReservedQuantity = 0)
{
    public decimal TotalQuantity => Quantity + ReservedQuantity;
    public string Summary => ReservedQuantity > 0
        ? $"{ResourceName}: total {TotalQuantity:0.##} · available {Quantity:0.##} · reserved {ReservedQuantity:0.##} {UnitName}"
        : Capacity is null ? $"{ResourceName}: {Quantity:0.##} {UnitName}" : $"{ResourceName}: {Quantity:0.##} / {Capacity:0.##} {UnitName}";
}
public sealed record OperationLogisticsDisplay(string ContractId, string Direction, string Resource, decimal Quantity,
    string Origin, string Destination, decimal PilotPayout, decimal CargoValue, decimal ReservedFunds,
    string Status, string RoadFallback, string Created, int? RoadFallbackMinutes)
{ public string Summary => $"{Resource} {Quantity:0.##} kg · {Origin} → {Destination} · {Status}"; }
public sealed record LogisticsLocationOption(string LocationId, string Name, string TypeLabel, bool IsGateway,
    double Latitude, double Longitude, IReadOnlyList<EconomicInventoryDisplay> Inventory)
{ public override string ToString() => $"{Name} · {TypeLabel}"; }
public sealed record LogisticsCargoOption(string ResourceId, string Name, string UnitName)
{ public override string ToString() => Name; }
public sealed record OperationsMapLocation(string Key, string Name, string Identifier, OperationsMapLocationKind Kind,
    string TypeLabel, string OwnershipLabel, double Latitude, double Longitude, string Description,
    IReadOnlyList<string> Concepts, IReadOnlyList<EconomicInventoryDisplay> Inventory, string? OperationId)
{
    public bool HasInventory => Inventory.Count > 0;
    public bool HasOperation => OperationId is not null;
    public string Coordinates => string.Create(CultureInfo.InvariantCulture, $"{Latitude:F5}, {Longitude:F5}");
}
public sealed record OperationDisplay(string OperationId, string SiteName, string AirportIdentifier, string Status,
    int PrototypeDay, string Rations, string RawMinerals, string LastTickResult,
    IReadOnlyList<string> PendingLogistics, bool AirportDefinitionAvailable, string ArchetypeName,
    string LocationId, double Latitude, double Longitude, IReadOnlyList<EconomicInventoryDisplay> Inventory,
    string ProductionSummary, string ConsumptionSummary, string ConstraintSummary)
{
    public IReadOnlyList<OperationLogisticsDisplay> LogisticsJobs { get; init; } = [];
    public IReadOnlyList<LogisticsLocationOption> LogisticsLocations { get; init; } = [];
    public IReadOnlyList<LogisticsCargoOption> LogisticsCargo { get; init; } = [];
    public string ImportAvailability { get; init; } = "Import availability unavailable.";
    public string ExportAvailability { get; init; } = "Export availability unavailable.";
}
public sealed record OperationsSnapshot(IReadOnlyList<OperationAirportDisplay> Airports,
    IReadOnlyList<OperationDisplay> Operations, decimal PlayerBalance, string CurrencyCode, string StoragePath,
    IReadOnlyList<string> Warnings, IReadOnlyList<OperationsMapLocation> MapLocations);

public interface IProspectingOperations
{
    string StoragePath { get; }
    Task<OperationsSnapshot> LoadAsync(CancellationToken cancellationToken = default);
    Task<OperationsSnapshot> FoundAsync(Guid airportId, string commandId, CancellationToken cancellationToken = default);
    Task<OperationsSnapshot> AdvanceDayAsync(string operationId, string commandId, CancellationToken cancellationToken = default);
    Task<OperationsSnapshot> TransferPrototypeRationsAsync(string operationId, string commandId, CancellationToken cancellationToken = default);
    Task<OperationsSnapshot> PublishLogisticsAsync(string operationId, string originLocationId, string destinationLocationId, string resourceId,
        decimal quantity, decimal pilotPayout, int? fallbackMinutes, CancellationToken cancellationToken = default);
    Task<OperationsSnapshot> EditLogisticsPayoutAsync(string contractId, decimal payout, int? fallbackMinutes, CancellationToken cancellationToken = default);
    Task<OperationsSnapshot> CancelLogisticsAsync(string contractId, CancellationToken cancellationToken = default);
    Task<OperationsSnapshot> ProcessNextRoadEventAsync(CancellationToken cancellationToken = default);
}

internal sealed class UnavailableProspectingOperations : IProspectingOperations
{
    public string StoragePath => "unavailable";
    private static InvalidOperationException Error() => new("Operations require the persistent Frontier airport-management service.");
    public Task<OperationsSnapshot> LoadAsync(CancellationToken cancellationToken = default) => Task.FromException<OperationsSnapshot>(Error());
    public Task<OperationsSnapshot> FoundAsync(Guid airportId, string commandId, CancellationToken cancellationToken = default) => Task.FromException<OperationsSnapshot>(Error());
    public Task<OperationsSnapshot> AdvanceDayAsync(string operationId, string commandId, CancellationToken cancellationToken = default) => Task.FromException<OperationsSnapshot>(Error());
    public Task<OperationsSnapshot> TransferPrototypeRationsAsync(string operationId, string commandId, CancellationToken cancellationToken = default) => Task.FromException<OperationsSnapshot>(Error());
    public Task<OperationsSnapshot> PublishLogisticsAsync(string operationId, string originLocationId, string destinationLocationId, string resourceId, decimal quantity, decimal pilotPayout, int? fallbackMinutes, CancellationToken cancellationToken = default) => Task.FromException<OperationsSnapshot>(Error());
    public Task<OperationsSnapshot> EditLogisticsPayoutAsync(string contractId, decimal payout, int? fallbackMinutes, CancellationToken cancellationToken = default) => Task.FromException<OperationsSnapshot>(Error());
    public Task<OperationsSnapshot> CancelLogisticsAsync(string contractId, CancellationToken cancellationToken = default) => Task.FromException<OperationsSnapshot>(Error());
    public Task<OperationsSnapshot> ProcessNextRoadEventAsync(CancellationToken cancellationToken = default) => Task.FromException<OperationsSnapshot>(Error());
}

public sealed class ProspectingOperationsService : IProspectingOperations
{
    private readonly EconomyService economy; private readonly IAirportManagement airports;
    private readonly ISitePresenceEvidenceProvider presence; private readonly Func<DateTimeOffset> utcNow;
    public string StoragePath => economy.StoragePath;
    public ProspectingOperationsService(IEconomyStateStore store, IAirportManagement airports,
        ISitePresenceEvidenceProvider presence, Func<DateTimeOffset>? utcNow = null)
    { economy = new(store); this.airports = airports; this.presence = presence; this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow); }

    public async Task<OperationsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var world = await EnsureWorldAsync(cancellationToken).ConfigureAwait(false);
        _ = await economy.AdvanceRealTimeAsync(utcNow(), cancellationToken).ConfigureAwait(false);
        world = await economy.LoadAsync(cancellationToken).ConfigureAwait(false) ?? throw new IOException("Economy state disappeared during operation maintenance.");
        return await PresentAsync(world, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationsSnapshot> FoundAsync(Guid airportId, string commandId, CancellationToken cancellationToken = default)
    {
        var world = await EnsureWorldAsync(cancellationToken).ConfigureAwait(false);
        var library = await airports.ListAirportsAsync(cancellationToken).ConfigureAwait(false);
        if (library.Warnings.Count > 0) throw new IOException("Airport library contains unreadable definitions; founding was refused. " + string.Join(" ", library.Warnings));
        var airport = library.Airports.SingleOrDefault(x => x.Id == airportId) ??
            throw new InvalidOperationException("Only a saved Frontier Pilot airport can become an economic site.");
        if (world.Sites.Any(x => x.AirportDefinitionId == airportId.ToString("N")))
            return await PresentAsync(world, cancellationToken).ConfigureAwait(false);
        var candidate = Candidate(airport); var assessment = presence.Assess(airport, utcNow());
        if (!assessment.IsVerified) throw new InvalidOperationException(assessment.Reason);
        _ = await economy.FoundProspectingCampAsync(candidate, assessment.Evidence!, commandId, utcNow(), cancellationToken).ConfigureAwait(false);
        world = await economy.LoadAsync(cancellationToken).ConfigureAwait(false) ?? throw new IOException("Founded economy state could not be reloaded.");
        return await PresentAsync(world, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationsSnapshot> AdvanceDayAsync(string operationId, string commandId, CancellationToken cancellationToken = default)
    {
        _ = await EnsureWorldAsync(cancellationToken).ConfigureAwait(false);
        _ = await economy.AdvanceOperationDayAsync(operationId, commandId, utcNow(), cancellationToken).ConfigureAwait(false);
        var world = await economy.LoadAsync(cancellationToken).ConfigureAwait(false) ?? throw new IOException("Operation state could not be reloaded.");
        return await PresentAsync(world, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationsSnapshot> TransferPrototypeRationsAsync(string operationId, string commandId, CancellationToken cancellationToken = default)
    {
        var world = await EnsureWorldAsync(cancellationToken).ConfigureAwait(false);
        var operation = world.Operations.SingleOrDefault(x => x.Id == operationId) ?? throw new InvalidOperationException("Economic operation does not exist.");
        var site = world.Sites.Single(x => x.Id == operation.SiteId);
        var request = PersistentEconomyKernel.CreateTransferRequest(commandId,
            EmployedCargoPrototype.ErosId, site.LocationId, OperationArchetypeCatalogue.RationsResourceId,
            1m, utcNow(), "DEVELOPER TEST ONLY: persistent 1 kg ration transfer from Eros to the selected operation.");
        _ = await economy.TransferPersistentInventoryAsync(request, cancellationToken).ConfigureAwait(false);
        world = await economy.LoadAsync(cancellationToken).ConfigureAwait(false) ?? throw new IOException("Transferred economy state could not be reloaded.");
        return await PresentAsync(world, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationsSnapshot> PublishLogisticsAsync(string operationId, string originLocationId,
        string destinationLocationId, string resourceId, decimal quantity, decimal pilotPayout, int? fallbackMinutes, CancellationToken cancellationToken = default)
    {
        var world = await EnsureWorldAsync(cancellationToken).ConfigureAwait(false);
        _ = world.Operations.Single(x => x.Id == operationId);
        var origin = LocationCoordinates(world, originLocationId); var destination = LocationCoordinates(world, destinationLocationId);
        var km = WorldTracking.DistanceMeters(origin.Latitude, origin.Longitude, destination.Latitude, destination.Longitude) / 1000d;
        var road = RoadLogisticsPrototype.Quote(km);
        var commandSource = operationId + "|" + Guid.NewGuid().ToString("N");
        var command = "publish-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(commandSource))).ToLowerInvariant()[..40];
        _ = await economy.PublishOperationLogisticsAsync(new(command, operationId, originLocationId, destinationLocationId,
            resourceId, quantity, pilotPayout, fallbackMinutes, road.Cost, road.DurationMinutes, utcNow()), cancellationToken).ConfigureAwait(false);
        return await PresentAsync((await economy.LoadAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken).ConfigureAwait(false);
    }

    private static ApproximateCoordinates LocationCoordinates(EconomyWorld world, string locationId)
    {
        var site = world.Sites.SingleOrDefault(x => x.LocationId == locationId);
        if (site is not null) return new(site.Latitude, site.Longitude);
        return NamibiaLocationCatalogue.Load().Locations.SingleOrDefault(x => x.StableId == locationId)?.ApproximateCoordinates
            ?? throw new InvalidOperationException("The selected logistics location has no usable coordinates.");
    }

    public async Task<OperationsSnapshot> EditLogisticsPayoutAsync(string contractId, decimal payout, int? fallbackMinutes, CancellationToken cancellationToken = default)
    { _ = await economy.EditOperationLogisticsTermsAsync(contractId, payout, fallbackMinutes, utcNow(), cancellationToken).ConfigureAwait(false); return await PresentAsync((await economy.LoadAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken).ConfigureAwait(false); }
    public async Task<OperationsSnapshot> CancelLogisticsAsync(string contractId, CancellationToken cancellationToken = default)
    { _ = await economy.CancelOperationLogisticsAsync(contractId, utcNow(), cancellationToken).ConfigureAwait(false); return await PresentAsync((await economy.LoadAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken).ConfigureAwait(false); }
    public async Task<OperationsSnapshot> ProcessNextRoadEventAsync(CancellationToken cancellationToken = default)
    {
        var world = await EnsureWorldAsync(cancellationToken).ConfigureAwait(false);
        var next = world.LogisticsContracts.Where(x => x.State == OperationLogisticsContractState.Published && x.FallbackAtUtc is not null)
            .Select(x => x.FallbackAtUtc!.Value).Concat(world.LogisticsContracts.Where(x => x.State == OperationLogisticsContractState.RoadInTransit && x.RoadEtaUtc is not null).Select(x => x.RoadEtaUtc!.Value)).Order().FirstOrDefault();
        if (next == default) throw new InvalidOperationException("No pending road fallback event exists.");
        _ = await economy.ProcessRoadLogisticsAsync(next.AddSeconds(1), cancellationToken).ConfigureAwait(false);
        return await PresentAsync((await economy.LoadAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken).ConfigureAwait(false);
    }

    private async Task<EconomyWorld> EnsureWorldAsync(CancellationToken token)
    {
        var world = await economy.LoadAsync(token).ConfigureAwait(false);
        if (world is not null)
        {
            _ = await economy.EnsurePersistentGatewayStockAsync(new(), utcNow(), token).ConfigureAwait(false);
            return await economy.LoadAsync(token).ConfigureAwait(false) ?? throw new IOException("Persistent gateway state could not be reloaded.");
        }
        try { await economy.InitializeAsync(EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load()), token).ConfigureAwait(false); }
        catch (IOException) { world = await economy.LoadAsync(token).ConfigureAwait(false); if (world is null) throw; }
        _ = await economy.EnsurePersistentGatewayStockAsync(new(), utcNow(), token).ConfigureAwait(false);
        return await economy.LoadAsync(token).ConfigureAwait(false) ?? throw new IOException("Economy initialization produced no state.");
    }

    private async Task<OperationsSnapshot> PresentAsync(EconomyWorld world, CancellationToken token)
    {
        world.EnsureValid(); var library = await airports.ListAirportsAsync(token).ConfigureAwait(false); var now = utcNow();
        var rows = library.Airports.Select(airport =>
        {
            var linked = world.Sites.Any(x => x.AirportDefinitionId == airport.Id.ToString("N"));
            string reason; bool can;
            try
            {
                var candidate = Candidate(airport);
                if (linked) { can = false; reason = "This airport is already linked to an economic site."; }
                else if (!candidate.Capabilities.Contains(ConstructionCapability.BaseLodge)) { can = false; reason = "Save a placed object with the Base Lodge capability first."; }
                else if (EconomyLedger.Balance(world, EmployedCargoPrototype.PlayerId, EmployedCargoPrototype.Currency) < OperationArchetypeCatalogue.ProspectingCamp.FoundingCost)
                { can = false; reason = "The ledger-derived balance is below the temporary 150 TPC founding cost."; }
                else { var assessment = presence.Assess(airport, now); can = assessment.IsVerified; reason = assessment.Reason; }
            }
            catch (Exception error) { can = false; reason = error.Message; }
            return new OperationAirportDisplay(airport.Id, airport.Name, airport.Identifier, airport.Location, linked, can, reason);
        }).ToArray();
        var definitions = library.Airports.ToDictionary(x => x.Id.ToString("N"), StringComparer.Ordinal);
        var displays = world.Operations.OrderBy(x => x.Id, StringComparer.Ordinal).Select(operation =>
        {
            var site = world.Sites.Single(x => x.Id == operation.SiteId); var a = OperationArchetypeCatalogue.Get(operation.ArchetypeId);
            var rations = world.Inventories.Single(x => x.LocationId == site.LocationId && x.ResourceId == a.Process.InputResourceId);
            var minerals = world.Inventories.Single(x => x.LocationId == site.LocationId && x.ResourceId == a.Process.OutputResourceId);
            var pending = world.Requirements.Where(x => x.State is TransportRequirementState.Open or TransportRequirementState.Reserved &&
                    (x.NeedId == ProspectingCampKernel.InboundNeed(operation.Id) || x.NeedId == ProspectingCampKernel.OutboundNeed(operation.Id)))
                .Select(x => { var job = world.Jobs.Single(j => j.RequirementId == x.Id); var resource = world.Resources.Single(r => r.Id == x.ResourceId);
                    var origin = world.Locations.Single(p => p.Id == x.OriginLocationId).Name; var destination = world.Locations.Single(p => p.Id == x.DestinationLocationId).Name;
                    return $"{resource.Name}: {x.Quantity:0.##} kg · {origin} → {destination} · {job.State}"; }).ToArray();
            var last = world.OperationTicks.Where(x => x.OperationId == operation.Id).OrderByDescending(x => x.PrototypeDay).FirstOrDefault()?.Result ?? "No prototype day has been advanced.";
            var inventory = a.Storage.Select(storage =>
            {
                var balance = world.Inventories.Single(x => x.LocationId == site.LocationId && x.ResourceId == storage.ResourceId);
                var resource = world.Resources.Single(x => x.Id == storage.ResourceId);
                var reserved = world.LogisticsContracts.Where(x => x.OriginLocationId == site.LocationId && x.ResourceId == storage.ResourceId &&
                    x.State is OperationLogisticsContractState.Published or OperationLogisticsContractState.Accepted or OperationLogisticsContractState.RoadInTransit).Sum(x => x.Quantity);
                return new EconomicInventoryDisplay(resource.Id, resource.Name, balance.AvailableQuantity, resource.UnitName, storage.Capacity, reserved);
            }).ToArray();
            var logistics = world.LogisticsContracts.Where(x => x.OperationId == operation.Id).OrderByDescending(x => x.CreatedUtc).Select(x =>
                new OperationLogisticsDisplay(x.Id, x.Direction.ToString(), world.Resources.Single(r => r.Id == x.ResourceId).Name, x.Quantity,
                    world.Locations.Single(p => p.Id == x.OriginLocationId).Name, world.Locations.Single(p => p.Id == x.DestinationLocationId).Name,
                    x.PilotPayout, x.CargoValue, world.FinancialReservations.Single(r => r.ContractId == x.Id).Amount,
                    x.State.ToString(), x.RoadFallbackDelayMinutes is null ? "No road fallback" : x.State == OperationLogisticsContractState.RoadInTransit ? $"Road ETA {x.RoadEtaUtc:u}" : $"Road after {x.RoadFallbackDelayMinutes} min",
                    x.CreatedUtc.ToString("u", CultureInfo.InvariantCulture), x.RoadFallbackDelayMinutes)).ToArray();
            var rationStorage = a.Storage.Single(x => x.ResourceId == OperationArchetypeCatalogue.RationsResourceId);
            var reservedRationsAtCamp = world.LogisticsContracts.Where(x => x.OriginLocationId == site.LocationId && x.ResourceId == OperationArchetypeCatalogue.RationsResourceId &&
                x.State is OperationLogisticsContractState.Published or OperationLogisticsContractState.Accepted).Sum(x => x.Quantity);
            var incomingRations = world.LogisticsContracts.Where(x => x.DestinationLocationId == site.LocationId && x.ResourceId == OperationArchetypeCatalogue.RationsResourceId &&
                x.State is OperationLogisticsContractState.Published or OperationLogisticsContractState.Accepted or OperationLogisticsContractState.RoadInTransit).Sum(x => x.Quantity);
            var gatewayRations = world.Inventories.SingleOrDefault(x => x.LocationId == EmployedCargoPrototype.ErosId && x.ResourceId == OperationArchetypeCatalogue.RationsResourceId)?.AvailableQuantity ?? 0;
            var maxImport = Math.Max(0, Math.Min(gatewayRations, rationStorage.Capacity - rations.AvailableQuantity - reservedRationsAtCamp - incomingRations));
            var maxExport = Math.Max(0, minerals.AvailableQuantity);
            var logisticsLocations = new List<LogisticsLocationOption>
            {
                new(site.LocationId, site.Name, "Player operation", false, site.Latitude, site.Longitude, inventory)
            };
            foreach (var gateway in world.Locations.Where(x => x.Kind == EconomicLocationKind.Gateway).OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                var coordinates = NamibiaLocationCatalogue.Load().Locations.SingleOrDefault(x => x.StableId == gateway.Id)?.ApproximateCoordinates;
                if (coordinates is null) continue;
                logisticsLocations.Add(new(gateway.Id, gateway.Name, "Gateway", true, coordinates.Latitude, coordinates.Longitude,
                    InventoryFor(world, gateway.Id, null)));
            }
            var logisticsCargo = world.Resources.Where(resource => GatewayPriceCatalogue.All.Any(price => price.ResourceId == resource.Id))
                .OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => new LogisticsCargoOption(x.Id, x.Name, x.UnitName)).ToArray();
            return new OperationDisplay(operation.Id, site.Name, site.AirportIdentifier, operation.Status, operation.PrototypeDay,
                $"{rations.AvailableQuantity:0.##} / {a.Storage.Single(x => x.ResourceId == rations.ResourceId).Capacity:0.##} kg",
                $"{minerals.AvailableQuantity:0.##} / {a.Storage.Single(x => x.ResourceId == minerals.ResourceId).Capacity:0.##} kg",
                last, pending, definitions.ContainsKey(site.AirportDefinitionId), a.DisplayName, site.LocationId,
                site.Latitude, site.Longitude, inventory,
                $"{a.Process.OutputPerTick:0.##} kg {world.Resources.Single(x => x.Id == a.Process.OutputResourceId).Name} per supplied real day",
                $"{a.Process.InputPerTick:0.##} kg {world.Resources.Single(x => x.Id == a.Process.InputResourceId).Name} per real day",
                operation.LastConstraint)
            {
                LogisticsJobs = logistics,
                LogisticsLocations = logisticsLocations,
                LogisticsCargo = logisticsCargo,
                ImportAvailability = $"Manual Rations import: up to {maxImport:0.##} kg now (camp storage and Eros available stock).",
                ExportAvailability = $"Manual Raw Minerals export: up to {maxExport:0.##} kg currently unreserved."
            };
        }).ToArray();
        var player = world.Players.Single(x => x.Id == EmployedCargoPrototype.PlayerId);
        var warnings = library.Warnings.Concat(world.Sites.Where(x => !definitions.ContainsKey(x.AirportDefinitionId))
            .Select(x => $"Economic site {x.Name} requires attention: its airport definition is missing or unreadable and was preserved.")).ToArray();
        return new(rows, displays, player.CachedBalance, player.CurrencyCode, StoragePath, warnings,
            BuildMapLocations(world, library.Airports, displays));
    }

    private static IReadOnlyList<OperationsMapLocation> BuildMapLocations(EconomyWorld world,
        IReadOnlyList<SavedAirport> savedAirports, IReadOnlyList<OperationDisplay> operations)
    {
        var catalogue = NamibiaLocationCatalogue.Load(); var result = new List<OperationsMapLocation>();
        foreach (var authored in catalogue.Locations.Where(x => x.ApproximateCoordinates is not null))
        {
            var c = authored.ApproximateCoordinates!;
            var kind = authored.IsLockedGateway ? OperationsMapLocationKind.Gateway : OperationsMapLocationKind.SystemLocation;
            var inventory = authored.IsLockedGateway ? InventoryFor(world, authored.StableId, null) : [];
            var identifier = authored.AirportIdentifier.Value ?? "Identifier unverified";
            result.Add(new("catalogue:" + authored.StableId, authored.Name, identifier, kind,
                authored.IsLockedGateway ? "Gateway airport" : $"SYSTEM {authored.Category}", "SYSTEM",
                c.Latitude, c.Longitude, authored.EconomicRole,
                authored.IsLockedGateway ? ["Frontier gateway", "Airport"] : ["SYSTEM location", authored.Category.ToString()],
                inventory, null));
        }
        foreach (var airport in savedAirports)
        {
            var operation = operations.SingleOrDefault(x => world.Sites.Any(site => site.AirportDefinitionId == airport.Id.ToString("N") && site.LocationId == x.LocationId));
            var access = "Player-created site";
            var accessConcept = !airport.Definition.HasRunway ? "Airfield not built · no spawn"
                : airport.Definition.ParkingStands.Count == 0 ? "Runway access · no parking spawn" : "Runway + parking access";
            if (operation is null)
                result.Add(new("airport:" + airport.Id.ToString("N"), airport.Name, airport.Identifier,
                    OperationsMapLocationKind.PlayerAirport, access, "Player", airport.Definition.Latitude,
                    airport.Definition.Longitude, "Saved Frontier Pilot operation site.", [access, accessConcept], [], null));
            else
                result.Add(new("operation:" + operation.OperationId, operation.SiteName, operation.AirportIdentifier,
                    OperationsMapLocationKind.PlayerOperation, "Player operation", "Player-owned",
                    operation.Latitude, operation.Longitude, operation.ArchetypeName,
                    [access, accessConcept, operation.ArchetypeName], operation.Inventory, operation.OperationId));
        }
        // Preserve an operation marker even when its editable airport definition needs attention.
        foreach (var operation in operations.Where(x => !savedAirports.Any(a => a.Id.ToString("N") == world.Sites.Single(s => s.LocationId == x.LocationId).AirportDefinitionId)))
            result.Add(new("operation:" + operation.OperationId, operation.SiteName, operation.AirportIdentifier,
                OperationsMapLocationKind.PlayerOperation, "Player operation (airport unavailable)", "Player-owned",
                operation.Latitude, operation.Longitude, operation.ArchetypeName,
                [operation.ArchetypeName, "Linked airport requires attention"], operation.Inventory, operation.OperationId));
        return result.OrderBy(x => x.Kind).ThenBy(x => x.Name, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<EconomicInventoryDisplay> InventoryFor(EconomyWorld world, string locationId, OperationArchetypeDefinition? archetype) =>
        world.Inventories.Where(x => x.LocationId == locationId).OrderBy(x => x.ResourceId, StringComparer.Ordinal).Select(balance =>
        {
            var resource = world.Resources.Single(x => x.Id == balance.ResourceId);
            var capacity = archetype?.Storage.SingleOrDefault(x => x.ResourceId == balance.ResourceId)?.Capacity;
            var reserved = world.LogisticsContracts.Where(x => x.OriginLocationId == locationId && x.ResourceId == balance.ResourceId &&
                x.State is OperationLogisticsContractState.Published or OperationLogisticsContractState.Accepted or OperationLogisticsContractState.RoadInTransit).Sum(x => x.Quantity);
            return new EconomicInventoryDisplay(resource.Id, resource.Name, balance.AvailableQuantity, resource.UnitName, capacity, reserved);
        }).ToArray();

    internal static AirportSiteCandidate Candidate(SavedAirport airport)
    {
        airport.Definition.EnsureValid();
        var capabilities = airport.Definition.PlacedObjects.SelectMany(x => ConstructionObjectCatalogue.All
                .Where(c => c.SceneryAssetId == x.AssetId).SelectMany(c => c.Capabilities))
            .Distinct().ToArray();
        return new AirportSiteCandidate(airport.Id.ToString("N"), airport.Identifier, airport.Name,
            airport.Definition.Latitude, airport.Definition.Longitude, airport.Definition.HasRunway, capabilities).EnsureValid();
    }
}

public sealed class ProspectingOperationsViewModel : INotifyPropertyChanged
{
    private readonly IProspectingOperations service; private bool busy; private string status = "Refresh operations to begin.", error = "";
    private IReadOnlyList<OperationAirportDisplay> airports = []; private IReadOnlyList<OperationDisplay> operations = [];
    private IReadOnlyList<OperationsMapLocation> mapLocations = [];
    private OperationAirportDisplay? selectedAirport; private OperationDisplay? selectedOperation; private OperationsMapLocation? selectedMapLocation;
    private decimal balance; private string currency = "TPC";
    private string logisticsQuantity = "10", logisticsPayout = "20";
    private LogisticsLocationOption? selectedLogisticsOrigin, selectedLogisticsDestination;
    private LogisticsCargoOption? selectedLogisticsCargo;
    private int fallbackMinutes = -1; private OperationLogisticsDisplay? selectedLogistics;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<IReadOnlyList<OperationsMapLocation>>? MapLocationsChanged;
    public bool IsBusy => busy;
    public ActionCommand RefreshCommand { get; } public ActionCommand FoundCommand { get; } public ActionCommand AdvanceDayCommand { get; }
    public ActionCommand TransferPrototypeRationsCommand { get; }
    public ActionCommand PublishLogisticsCommand { get; } public ActionCommand EditLogisticsPayoutCommand { get; }
    public ActionCommand CancelLogisticsCommand { get; } public ActionCommand ProcessNextRoadEventCommand { get; }
    public ProspectingOperationsViewModel(IProspectingOperations service)
    {
        this.service = service;
        RefreshCommand = new(() => _ = RefreshAsync(), () => !busy);
        FoundCommand = new(() => _ = FoundAsync(), () => !busy && selectedAirport?.CanFound == true);
        AdvanceDayCommand = new(() => _ = AdvanceDayAsync(), () => !busy && selectedOperation is not null);
        TransferPrototypeRationsCommand = new(() => _ = TransferPrototypeRationsAsync(), () => !busy && selectedOperation is not null);
        PublishLogisticsCommand = new(() => _ = PublishLogisticsAsync(), () => !busy && selectedOperation is not null);
        EditLogisticsPayoutCommand = new(() => _ = EditLogisticsAsync(), () => !busy && selectedLogistics?.Status == OperationLogisticsContractState.Published.ToString());
        CancelLogisticsCommand = new(() => _ = CancelLogisticsAsync(), () => !busy && selectedLogistics?.Status == OperationLogisticsContractState.Published.ToString());
        ProcessNextRoadEventCommand = new(() => _ = ProcessNextRoadAsync(), () => !busy);
    }
    public IReadOnlyList<OperationAirportDisplay> Airports { get => airports; private set { airports = value; Changed(); } }
    public IReadOnlyList<OperationDisplay> Operations { get => operations; private set { operations = value; Changed(); } }
    public IReadOnlyList<OperationsMapLocation> MapLocations { get => mapLocations; private set { mapLocations = value; Changed(); } }
    public OperationAirportDisplay? SelectedAirport { get => selectedAirport; set { selectedAirport = value; Changed(); Changed(nameof(FoundingReason)); FoundCommand.Refresh(); } }
    public OperationDisplay? SelectedOperation { get => selectedOperation; set { var originId=selectedLogisticsOrigin?.LocationId;var destinationId=selectedLogisticsDestination?.LocationId;var cargoId=selectedLogisticsCargo?.ResourceId;selectedOperation = value; ConfigureLogisticsBuilder(originId,destinationId,cargoId);Changed(); NotifyOperation(); AdvanceDayCommand.Refresh(); } }
    public OperationsMapLocation? SelectedMapLocation { get => selectedMapLocation; private set { selectedMapLocation = value; Changed(); Changed(nameof(SelectedMapTitle)); Changed(nameof(SelectedMapInventory)); Changed(nameof(CanOpenSelectedMapOperation)); } }
    public string FoundingReason => SelectedAirport?.FoundingReason ?? "Select a saved player-created airport.";
    public string OperationTitle => SelectedOperation is null ? "No prospecting camp selected" : $"{SelectedOperation.SiteName} ({SelectedOperation.AirportIdentifier})";
    public string OperationStatus => SelectedOperation?.Status ?? "—"; public string PrototypeDay => SelectedOperation?.PrototypeDay.ToString(CultureInfo.InvariantCulture) ?? "—";
    public string Rations => SelectedOperation?.Rations ?? "—"; public string RawMinerals => SelectedOperation?.RawMinerals ?? "—";
    public string LastTickResult => SelectedOperation?.LastTickResult ?? "—";
    public IReadOnlyList<string> PendingLogistics => SelectedOperation?.PendingLogistics ?? [];
    public IReadOnlyList<EconomicInventoryDisplay> OperationInventory => SelectedOperation?.Inventory ?? [];
    public string OperationArchetype => SelectedOperation?.ArchetypeName ?? "—";
    public string OperationProduction => SelectedOperation?.ProductionSummary ?? "—";
    public string OperationConsumption => SelectedOperation?.ConsumptionSummary ?? "—";
    public string OperationConstraint => SelectedOperation?.ConstraintSummary ?? "—";
    public string ImportAvailability => SelectedOperation?.ImportAvailability ?? "Select an operation.";
    public string ExportAvailability => SelectedOperation?.ExportAvailability ?? "Select an operation.";
    public IReadOnlyList<OperationLogisticsDisplay> OperationLogistics => SelectedOperation?.LogisticsJobs ?? [];
    public IReadOnlyList<string> RoadFallbackOptions { get; } = ["None", "1 minute (development)", "12 hours"];
    public IReadOnlyList<LogisticsLocationOption> LogisticsOrigins => SelectedOperation?.LogisticsLocations ?? [];
    public IReadOnlyList<LogisticsLocationOption> LogisticsDestinations => SelectedOperation?.LogisticsLocations ?? [];
    public LogisticsLocationOption? SelectedLogisticsOrigin { get => selectedLogisticsOrigin; set { selectedLogisticsOrigin=value;RefreshLogisticsRoute();Changed(); } }
    public LogisticsLocationOption? SelectedLogisticsDestination { get => selectedLogisticsDestination; set { selectedLogisticsDestination=value;RefreshLogisticsRoute();Changed(); } }
    public IReadOnlyList<LogisticsCargoOption> AvailableLogisticsCargo => CompatibleCargo();
    public LogisticsCargoOption? SelectedLogisticsCargo { get => selectedLogisticsCargo; set { selectedLogisticsCargo=value;Changed();NotifyLogisticsQuote(); } }
    public string LogisticsResource => SelectedLogisticsCargo?.Name ?? "Select cargo";
    public string LogisticsQuantity { get => logisticsQuantity; set { logisticsQuantity = value; Changed();NotifyLogisticsQuote(); } }
    public string LogisticsPayout { get => logisticsPayout; set { logisticsPayout = value; Changed();NotifyLogisticsQuote(); } }
    public string LogisticsRoute => selectedLogisticsOrigin is null || selectedLogisticsDestination is null ? "Select an origin and destination." :
        $"{selectedLogisticsOrigin.Name} → {selectedLogisticsDestination.Name}";
    public string OriginCargoAvailability
    {
        get
        {
            var cargo=selectedLogisticsCargo;var origin=selectedLogisticsOrigin;if(cargo is null||origin is null)return "Select cargo to inspect origin stock.";
            var stock=origin.Inventory.SingleOrDefault(x=>x.ResourceId==cargo.ResourceId);
            return stock is null?$"{origin.Name} has no persistent {cargo.Name} inventory.":$"Available at origin: {stock.Quantity:0.##} {stock.UnitName} (reserved stock excluded).";
        }
    }
    public string CargoEconomics => DescribeCargoEconomics();
    public string LogisticsCommitment => DescribeCommitment();
    public int RoadFallbackSelection { get => fallbackMinutes < 0 ? 0 : fallbackMinutes == 1 ? 1 : 2; set { fallbackMinutes = value switch { 1 => 1, 2 => 720, _ => -1 }; Changed();NotifyLogisticsQuote(); } }
    public OperationLogisticsDisplay? SelectedLogistics { get => selectedLogistics; set { selectedLogistics = value; Changed(); if (value is not null) { LogisticsPayout = value.PilotPayout.ToString(CultureInfo.InvariantCulture); fallbackMinutes = value.RoadFallbackMinutes ?? -1; Changed(nameof(RoadFallbackSelection)); } EditLogisticsPayoutCommand.Refresh(); CancelLogisticsCommand.Refresh(); } }
    public string SelectedMapTitle => SelectedMapLocation is null ? "Select a map marker" : $"{SelectedMapLocation.Name} ({SelectedMapLocation.Identifier})";
    public IReadOnlyList<EconomicInventoryDisplay> SelectedMapInventory => SelectedMapLocation?.Inventory ?? [];
    public bool CanOpenSelectedMapOperation => SelectedMapLocation?.OperationId is not null;
    public string Balance => $"Ledger balance: {balance:0.##} {currency}"; public string Status { get => status; private set { status = value; Changed(); } }
    public string Error { get => error; private set { error = value; Changed(); } } public string StoragePath => service.StoragePath;
    public async Task RefreshAsync(CancellationToken token = default) => await RunAsync(() => service.LoadAsync(token), "Operation state refreshed.");
    public async Task FoundAsync(CancellationToken token = default)
    {
        var airport = SelectedAirport; if (airport?.CanFound != true) return;
        await RunAsync(() => service.FoundAsync(airport.AirportId, "found-" + airport.AirportId.ToString("N"), token), "Prospecting camp founded. Temporary 150 TPC debit recorded once.");
    }
    public async Task AdvanceDayAsync(CancellationToken token = default)
    {
        var operation = SelectedOperation; if (operation is null) return;
        await RunAsync(() => service.AdvanceDayAsync(operation.OperationId, "tick-" + operation.OperationId + "-" + (operation.PrototypeDay + 1).ToString("D6"), token), "One prototype day advanced atomically.");
    }
    public async Task TransferPrototypeRationsAsync(CancellationToken token = default)
    {
        var operation = SelectedOperation; if (operation is null) return;
        var command = "dev-transfer-rations-" + operation.OperationId + "-" + Guid.NewGuid().ToString("N");
        await RunAsync(() => service.TransferPrototypeRationsAsync(operation.OperationId, command, token), "Developer transfer committed through the authoritative inventory audit ledger.");
    }
    public async Task PublishLogisticsAsync(CancellationToken token = default)
    {
        var operation = SelectedOperation;
        if (operation is null) { Status = "Job was not published."; Error = "Select an owned operation before creating a logistics job."; return; }
        if (!TryNumber(LogisticsQuantity, out var quantity) || !TryNumber(LogisticsPayout, out var payout))
        { Status = "Job was not published."; Error = "Enter valid numeric quantity and payout values."; return; }
        if (quantity <= 0) { Status = "Job was not published."; Error = "Quantity must be greater than zero."; return; }
        if (payout < 0) { Status = "Job was not published."; Error = "Pilot payout cannot be negative."; return; }
        var origin=SelectedLogisticsOrigin;var destination=SelectedLogisticsDestination;var cargo=SelectedLogisticsCargo;
        if(origin is null||destination is null||cargo is null){Status="Job was not published.";Error="Select an origin, destination and cargo.";return;}
        if(!TryRoute(out _,out _,out var routeError)){Status="Job was not published.";Error=routeError;return;}
        Status = $"Publishing {origin.Name} to {destination.Name} contract…";
        await RunAsync(() => service.PublishLogisticsAsync(operation.OperationId, origin.LocationId, destination.LocationId, cargo.ResourceId, quantity, payout, fallbackMinutes < 0 ? null : fallbackMinutes, token), "Logistics contract published with cargo and funds reserved.");
    }
    public async Task EditLogisticsAsync(CancellationToken token = default)
    { var c = SelectedLogistics; if (c is null || !decimal.TryParse(LogisticsPayout, NumberStyles.Number, CultureInfo.CurrentCulture, out var payout)) return; await RunAsync(() => service.EditLogisticsPayoutAsync(c.ContractId, payout, fallbackMinutes < 0 ? null : fallbackMinutes, token), "Pilot payout, road fallback and escrow updated atomically."); }
    public async Task CancelLogisticsAsync(CancellationToken token = default)
    { var c = SelectedLogistics; if (c is null) return; await RunAsync(() => service.CancelLogisticsAsync(c.ContractId, token), "Unaccepted contract cancelled; cargo and funds released."); }
    public async Task ProcessNextRoadAsync(CancellationToken token = default) => await RunAsync(() => service.ProcessNextRoadEventAsync(token), "Next road event processed through the real timeline.");
    public void SelectMapLocation(string key)
    {
        SelectedMapLocation = MapLocations.SingleOrDefault(x => x.Key == key);
    }
    public bool SelectOperationFromMap()
    {
        var id = SelectedMapLocation?.OperationId; if (id is null) return false;
        SelectedOperation = Operations.SingleOrDefault(x => x.OperationId == id); return SelectedOperation is not null;
    }
    private async Task RunAsync(Func<Task<OperationsSnapshot>> action, string success)
    {
        if (busy) return; busy = true; error = ""; RefreshCommands();
        try { Apply(await action()); status = success; }
        catch (Exception exception) { error = exception.Message; status = "Operation action was refused; saved economy and airport data were preserved."; }
        finally { busy = false; RefreshCommands(); }
    }
    private void Apply(OperationsSnapshot snapshot)
    {
        var airportId = SelectedAirport?.AirportId; var operationId = SelectedOperation?.OperationId;
        var mapKey = SelectedMapLocation?.Key;
        Airports = snapshot.Airports; Operations = snapshot.Operations; MapLocations = snapshot.MapLocations; balance = snapshot.PlayerBalance; currency = snapshot.CurrencyCode;
        SelectedAirport = airportId is null ? Airports.FirstOrDefault() : Airports.SingleOrDefault(x => x.AirportId == airportId);
        SelectedOperation = operationId is null ? Operations.FirstOrDefault() : Operations.SingleOrDefault(x => x.OperationId == operationId) ?? Operations.FirstOrDefault();
        SelectedMapLocation = mapKey is null ? MapLocations.FirstOrDefault(x => x.Kind == OperationsMapLocationKind.Gateway) : MapLocations.SingleOrDefault(x => x.Key == mapKey);
        MapLocationsChanged?.Invoke(MapLocations);
        Changed(nameof(Balance)); if (snapshot.Warnings.Count > 0) error = string.Join(" ", snapshot.Warnings);
    }
    private void ConfigureLogisticsBuilder(string? originId,string? destinationId,string? cargoId)
    {
        var locations=SelectedOperation?.LogisticsLocations??[];
        selectedLogisticsOrigin=locations.SingleOrDefault(x=>x.LocationId==originId)??locations.FirstOrDefault(x=>x.IsGateway)??locations.FirstOrDefault();
        selectedLogisticsDestination=locations.SingleOrDefault(x=>x.LocationId==destinationId)??locations.FirstOrDefault(x=>!x.IsGateway)??locations.Skip(1).FirstOrDefault();
        var compatible=CompatibleCargo();
        selectedLogisticsCargo=compatible.SingleOrDefault(x=>x.ResourceId==cargoId)??compatible.FirstOrDefault();
        Changed(nameof(LogisticsOrigins));Changed(nameof(LogisticsDestinations));Changed(nameof(SelectedLogisticsOrigin));Changed(nameof(SelectedLogisticsDestination));
        Changed(nameof(AvailableLogisticsCargo));Changed(nameof(SelectedLogisticsCargo));Changed(nameof(LogisticsRoute));NotifyLogisticsQuote();
    }
    private void RefreshLogisticsRoute()
    {
        var compatible=CompatibleCargo();
        if(selectedLogisticsCargo is null||!compatible.Any(x=>x.ResourceId==selectedLogisticsCargo.ResourceId))selectedLogisticsCargo=compatible.FirstOrDefault();
        Changed(nameof(AvailableLogisticsCargo));Changed(nameof(SelectedLogisticsCargo));Changed(nameof(LogisticsRoute));NotifyLogisticsQuote();
    }
    private IReadOnlyList<LogisticsCargoOption> CompatibleCargo()
    {
        if(!TryRoute(out var direction,out var gateway,out _))return [];
        return (SelectedOperation?.LogisticsCargo??[]).Where(c=>
        {
            try
            {
                var quote=GatewayPriceCatalogue.Quote(gateway.LocationId,c.ResourceId);
                return direction==OperationLogisticsDirection.Import?quote.SellPricePerUnit is not null:quote.BuyPricePerUnit is not null;
            }
            catch(InvalidOperationException){return false;}
        }).ToArray();
    }
    private bool TryRoute(out OperationLogisticsDirection direction,out LogisticsLocationOption gateway,out string error)
    {
        direction=default;gateway=null!;error="";var operation=SelectedOperation;var origin=selectedLogisticsOrigin;var destination=selectedLogisticsDestination;
        if(operation is null||origin is null||destination is null){error="Select an origin and destination.";return false;}
        if(origin.LocationId==destination.LocationId){error="Origin and destination must be different locations.";return false;}
        if(origin.IsGateway&&destination.LocationId==operation.LocationId){direction=OperationLogisticsDirection.Import;gateway=origin;return true;}
        if(origin.LocationId==operation.LocationId&&destination.IsGateway){direction=OperationLogisticsDirection.Export;gateway=destination;return true;}
        error="V0 logistics jobs must connect this operation with a persistent gateway.";return false;
    }
    private string DescribeCargoEconomics()
    {
        if(!TryRoute(out var direction,out var gateway,out var error))return error;
        if(selectedLogisticsCargo is not { } cargo)return "No priced cargo is available for this route.";
        if(!TryNumber(logisticsQuantity,out var quantity)||quantity<=0)return "Enter a quantity to calculate cargo economics.";
        var quote=GatewayPriceCatalogue.Quote(gateway.LocationId,cargo.ResourceId);
        var unitPrice=direction==OperationLogisticsDirection.Import?quote.SellPricePerUnit:quote.BuyPricePerUnit;
        if(unitPrice is null)return "The gateway has no applicable price for this cargo and route.";
        var value=Math.Round(quantity*unitPrice.Value,2);
        return direction==OperationLogisticsDirection.Import
            ? $"Cargo purchase: {quantity:0.##} {cargo.UnitName} × {unitPrice:0.##} {quote.CurrencyCode} = {value:0.##} {quote.CurrencyCode}."
            : $"Expected cargo sale: {quantity:0.##} {cargo.UnitName} × {unitPrice:0.##} {quote.CurrencyCode} = {value:0.##} {quote.CurrencyCode}, credited only after delivery.";
    }
    private string DescribeCommitment()
    {
        if(!TryRoute(out var direction,out var gateway,out _)||selectedLogisticsCargo is not { } cargo||
            !TryNumber(logisticsQuantity,out var quantity)||quantity<=0||!TryNumber(logisticsPayout,out var payout)||payout<0)return "Enter valid cargo quantity and pilot payout values.";
        var quote=GatewayPriceCatalogue.Quote(gateway.LocationId,cargo.ResourceId);
        var unitPrice=direction==OperationLogisticsDirection.Import?quote.SellPricePerUnit:quote.BuyPricePerUnit;
        if(unitPrice is null)return "No applicable gateway price is available.";
        var cargoValue=Math.Round(quantity*unitPrice.Value,2);
        var distanceKm=WorldTracking.DistanceMeters(selectedLogisticsOrigin!.Latitude,selectedLogisticsOrigin.Longitude,selectedLogisticsDestination!.Latitude,selectedLogisticsDestination.Longitude)/1000d;
        var road=RoadLogisticsPrototype.Quote(distanceKm);var serviceReserve=fallbackMinutes<0?payout:Math.Max(payout,road.Cost);
        var reserved=serviceReserve+(direction==OperationLogisticsDirection.Import?cargoValue:0);
        return direction==OperationLogisticsDirection.Import
            ? $"Publish reserves {reserved:0.##} {quote.CurrencyCode}: cargo purchase plus transport commitment."
            : $"Publish reserves {reserved:0.##} {quote.CurrencyCode} for transport. Expected net after an air delivery is {cargoValue-payout:0.##} {quote.CurrencyCode}.";
    }
    private void NotifyLogisticsQuote(){Changed(nameof(LogisticsResource));Changed(nameof(OriginCargoAvailability));Changed(nameof(CargoEconomics));Changed(nameof(LogisticsCommitment));}
    private void NotifyOperation() { Changed(nameof(OperationTitle)); Changed(nameof(OperationStatus)); Changed(nameof(PrototypeDay)); Changed(nameof(Rations)); Changed(nameof(RawMinerals)); Changed(nameof(LastTickResult)); Changed(nameof(PendingLogistics)); Changed(nameof(OperationInventory)); Changed(nameof(OperationLogistics)); Changed(nameof(ImportAvailability)); Changed(nameof(ExportAvailability)); Changed(nameof(OperationArchetype)); Changed(nameof(OperationProduction)); Changed(nameof(OperationConsumption)); Changed(nameof(OperationConstraint));Changed(nameof(LogisticsOrigins));Changed(nameof(LogisticsDestinations));Changed(nameof(AvailableLogisticsCargo)); TransferPrototypeRationsCommand.Refresh(); PublishLogisticsCommand.Refresh(); }
    private void RefreshCommands() { RefreshCommand.Refresh(); FoundCommand.Refresh(); AdvanceDayCommand.Refresh(); TransferPrototypeRationsCommand.Refresh(); PublishLogisticsCommand.Refresh(); EditLogisticsPayoutCommand.Refresh(); CancelLogisticsCommand.Refresh(); ProcessNextRoadEventCommand.Refresh(); }
    private static bool TryNumber(string value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result) ||
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
