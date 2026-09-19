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
    decimal? Capacity = null)
{
    public string Summary => Capacity is null ? $"{ResourceName}: {Quantity:0.##} {UnitName}" :
        $"{ResourceName}: {Quantity:0.##} / {Capacity:0.##} {UnitName}";
}
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
    string ProductionSummary, string ConsumptionSummary, string ConstraintSummary);
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
}

internal sealed class UnavailableProspectingOperations : IProspectingOperations
{
    public string StoragePath => "unavailable";
    private static InvalidOperationException Error() => new("Operations require the persistent Frontier airport-management service.");
    public Task<OperationsSnapshot> LoadAsync(CancellationToken cancellationToken = default) => Task.FromException<OperationsSnapshot>(Error());
    public Task<OperationsSnapshot> FoundAsync(Guid airportId, string commandId, CancellationToken cancellationToken = default) => Task.FromException<OperationsSnapshot>(Error());
    public Task<OperationsSnapshot> AdvanceDayAsync(string operationId, string commandId, CancellationToken cancellationToken = default) => Task.FromException<OperationsSnapshot>(Error());
    public Task<OperationsSnapshot> TransferPrototypeRationsAsync(string operationId, string commandId, CancellationToken cancellationToken = default) => Task.FromException<OperationsSnapshot>(Error());
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
        _ = await economy.MaintainJobBoardAsync(utcNow(), cancellationToken).ConfigureAwait(false);
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
        var request = new PersistentInventoryTransferRequest("prototype-transfer-" + commandId, commandId,
            EmployedCargoPrototype.ErosId, site.LocationId, OperationArchetypeCatalogue.RationsResourceId,
            1m, utcNow(), "DEVELOPER TEST ONLY: persistent 1 kg ration transfer from Eros to the selected operation.");
        _ = await economy.TransferPersistentInventoryAsync(request, cancellationToken).ConfigureAwait(false);
        world = await economy.LoadAsync(cancellationToken).ConfigureAwait(false) ?? throw new IOException("Transferred economy state could not be reloaded.");
        return await PresentAsync(world, cancellationToken).ConfigureAwait(false);
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
                else if (!candidate.HasRunway) { can = false; reason = "A runway is required."; }
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
                return new EconomicInventoryDisplay(resource.Id, resource.Name, balance.AvailableQuantity, resource.UnitName, storage.Capacity);
            }).ToArray();
            return new OperationDisplay(operation.Id, site.Name, site.AirportIdentifier, operation.Status, operation.PrototypeDay,
                $"{rations.AvailableQuantity:0.##} / {a.Storage.Single(x => x.ResourceId == rations.ResourceId).Capacity:0.##} kg",
                $"{minerals.AvailableQuantity:0.##} / {a.Storage.Single(x => x.ResourceId == minerals.ResourceId).Capacity:0.##} kg",
                last, pending, definitions.ContainsKey(site.AirportDefinitionId), a.DisplayName, site.LocationId,
                site.Latitude, site.Longitude, inventory,
                $"{a.Process.OutputPerTick:0.##} kg {world.Resources.Single(x => x.Id == a.Process.OutputResourceId).Name} per supplied prototype day",
                $"{a.Process.InputPerTick:0.##} kg {world.Resources.Single(x => x.Id == a.Process.InputResourceId).Name} per prototype day",
                operation.LastConstraint);
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
            if (operation is null)
                result.Add(new("airport:" + airport.Id.ToString("N"), airport.Name, airport.Identifier,
                    OperationsMapLocationKind.PlayerAirport, "Player-created airport", "Player", airport.Definition.Latitude,
                    airport.Definition.Longitude, "Saved Frontier Pilot airport definition.", ["Player-created airport"], [], null));
            else
                result.Add(new("operation:" + operation.OperationId, operation.SiteName, operation.AirportIdentifier,
                    OperationsMapLocationKind.PlayerOperation, "Player airport + operation", "Player-owned",
                    operation.Latitude, operation.Longitude, operation.ArchetypeName,
                    ["Player-created airport", operation.ArchetypeName], operation.Inventory, operation.OperationId));
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
            return new EconomicInventoryDisplay(resource.Id, resource.Name, balance.AvailableQuantity, resource.UnitName, capacity);
        }).ToArray();

    internal static AirportSiteCandidate Candidate(SavedAirport airport)
    {
        airport.Definition.EnsureValid();
        var capabilities = airport.Definition.PlacedObjects.SelectMany(x => ConstructionObjectCatalogue.All
                .Where(c => c.SceneryAssetId == x.AssetId).SelectMany(c => c.Capabilities))
            .Distinct().ToArray();
        return new AirportSiteCandidate(airport.Id.ToString("N"), airport.Identifier, airport.Name,
            airport.Definition.Latitude, airport.Definition.Longitude, airport.Definition.LengthMeters > 0, capabilities).EnsureValid();
    }
}

public sealed class ProspectingOperationsViewModel : INotifyPropertyChanged
{
    private readonly IProspectingOperations service; private bool busy; private string status = "Refresh operations to begin.", error = "";
    private IReadOnlyList<OperationAirportDisplay> airports = []; private IReadOnlyList<OperationDisplay> operations = [];
    private IReadOnlyList<OperationsMapLocation> mapLocations = [];
    private OperationAirportDisplay? selectedAirport; private OperationDisplay? selectedOperation; private OperationsMapLocation? selectedMapLocation;
    private decimal balance; private string currency = "TPC";
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<IReadOnlyList<OperationsMapLocation>>? MapLocationsChanged;
    public ActionCommand RefreshCommand { get; } public ActionCommand FoundCommand { get; } public ActionCommand AdvanceDayCommand { get; }
    public ActionCommand TransferPrototypeRationsCommand { get; }
    public ProspectingOperationsViewModel(IProspectingOperations service)
    {
        this.service = service;
        RefreshCommand = new(() => _ = RefreshAsync(), () => !busy);
        FoundCommand = new(() => _ = FoundAsync(), () => !busy && selectedAirport?.CanFound == true);
        AdvanceDayCommand = new(() => _ = AdvanceDayAsync(), () => !busy && selectedOperation is not null);
        TransferPrototypeRationsCommand = new(() => _ = TransferPrototypeRationsAsync(), () => !busy && selectedOperation is not null);
    }
    public IReadOnlyList<OperationAirportDisplay> Airports { get => airports; private set { airports = value; Changed(); } }
    public IReadOnlyList<OperationDisplay> Operations { get => operations; private set { operations = value; Changed(); } }
    public IReadOnlyList<OperationsMapLocation> MapLocations { get => mapLocations; private set { mapLocations = value; Changed(); } }
    public OperationAirportDisplay? SelectedAirport { get => selectedAirport; set { selectedAirport = value; Changed(); Changed(nameof(FoundingReason)); FoundCommand.Refresh(); } }
    public OperationDisplay? SelectedOperation { get => selectedOperation; set { selectedOperation = value; Changed(); NotifyOperation(); AdvanceDayCommand.Refresh(); } }
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
    private void NotifyOperation() { Changed(nameof(OperationTitle)); Changed(nameof(OperationStatus)); Changed(nameof(PrototypeDay)); Changed(nameof(Rations)); Changed(nameof(RawMinerals)); Changed(nameof(LastTickResult)); Changed(nameof(PendingLogistics)); Changed(nameof(OperationInventory)); Changed(nameof(OperationArchetype)); Changed(nameof(OperationProduction)); Changed(nameof(OperationConsumption)); Changed(nameof(OperationConstraint)); TransferPrototypeRationsCommand.Refresh(); }
    private void RefreshCommands() { RefreshCommand.Refresh(); FoundCommand.Refresh(); AdvanceDayCommand.Refresh(); TransferPrototypeRationsCommand.Refresh(); }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
