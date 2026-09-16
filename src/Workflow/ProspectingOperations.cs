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
public sealed record OperationDisplay(string OperationId, string SiteName, string AirportIdentifier, string Status,
    int PrototypeDay, string Rations, string RawMinerals, string LastTickResult,
    IReadOnlyList<string> PendingLogistics, bool AirportDefinitionAvailable);
public sealed record OperationsSnapshot(IReadOnlyList<OperationAirportDisplay> Airports,
    IReadOnlyList<OperationDisplay> Operations, decimal PlayerBalance, string CurrencyCode, string StoragePath,
    IReadOnlyList<string> Warnings);

public interface IProspectingOperations
{
    string StoragePath { get; }
    Task<OperationsSnapshot> LoadAsync(CancellationToken cancellationToken = default);
    Task<OperationsSnapshot> FoundAsync(Guid airportId, string commandId, CancellationToken cancellationToken = default);
    Task<OperationsSnapshot> AdvanceDayAsync(string operationId, string commandId, CancellationToken cancellationToken = default);
}

internal sealed class UnavailableProspectingOperations : IProspectingOperations
{
    public string StoragePath => "unavailable";
    private static InvalidOperationException Error() => new("Operations require the persistent Frontier airport-management service.");
    public Task<OperationsSnapshot> LoadAsync(CancellationToken cancellationToken = default) => Task.FromException<OperationsSnapshot>(Error());
    public Task<OperationsSnapshot> FoundAsync(Guid airportId, string commandId, CancellationToken cancellationToken = default) => Task.FromException<OperationsSnapshot>(Error());
    public Task<OperationsSnapshot> AdvanceDayAsync(string operationId, string commandId, CancellationToken cancellationToken = default) => Task.FromException<OperationsSnapshot>(Error());
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

    private async Task<EconomyWorld> EnsureWorldAsync(CancellationToken token)
    {
        var world = await economy.LoadAsync(token).ConfigureAwait(false);
        if (world is not null) return world;
        try { await economy.InitializeAsync(EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load()), token).ConfigureAwait(false); }
        catch (IOException) { world = await economy.LoadAsync(token).ConfigureAwait(false); if (world is not null) return world; throw; }
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
            return new OperationDisplay(operation.Id, site.Name, site.AirportIdentifier, operation.Status, operation.PrototypeDay,
                $"{rations.AvailableQuantity:0.##} / {a.Storage.Single(x => x.ResourceId == rations.ResourceId).Capacity:0.##} kg",
                $"{minerals.AvailableQuantity:0.##} / {a.Storage.Single(x => x.ResourceId == minerals.ResourceId).Capacity:0.##} kg",
                last, pending, definitions.ContainsKey(site.AirportDefinitionId));
        }).ToArray();
        var player = world.Players.Single(x => x.Id == EmployedCargoPrototype.PlayerId);
        var warnings = library.Warnings.Concat(world.Sites.Where(x => !definitions.ContainsKey(x.AirportDefinitionId))
            .Select(x => $"Economic site {x.Name} requires attention: its airport definition is missing or unreadable and was preserved.")).ToArray();
        return new(rows, displays, player.CachedBalance, player.CurrencyCode, StoragePath, warnings);
    }

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
    private OperationAirportDisplay? selectedAirport; private OperationDisplay? selectedOperation; private decimal balance; private string currency = "TPC";
    public event PropertyChangedEventHandler? PropertyChanged;
    public ActionCommand RefreshCommand { get; } public ActionCommand FoundCommand { get; } public ActionCommand AdvanceDayCommand { get; }
    public ProspectingOperationsViewModel(IProspectingOperations service)
    {
        this.service = service;
        RefreshCommand = new(() => _ = RefreshAsync(), () => !busy);
        FoundCommand = new(() => _ = FoundAsync(), () => !busy && selectedAirport?.CanFound == true);
        AdvanceDayCommand = new(() => _ = AdvanceDayAsync(), () => !busy && selectedOperation is not null);
    }
    public IReadOnlyList<OperationAirportDisplay> Airports { get => airports; private set { airports = value; Changed(); } }
    public IReadOnlyList<OperationDisplay> Operations { get => operations; private set { operations = value; Changed(); } }
    public OperationAirportDisplay? SelectedAirport { get => selectedAirport; set { selectedAirport = value; Changed(); Changed(nameof(FoundingReason)); FoundCommand.Refresh(); } }
    public OperationDisplay? SelectedOperation { get => selectedOperation; set { selectedOperation = value; Changed(); NotifyOperation(); AdvanceDayCommand.Refresh(); } }
    public string FoundingReason => SelectedAirport?.FoundingReason ?? "Select a saved player-created airport.";
    public string OperationTitle => SelectedOperation is null ? "No prospecting camp selected" : $"{SelectedOperation.SiteName} ({SelectedOperation.AirportIdentifier})";
    public string OperationStatus => SelectedOperation?.Status ?? "—"; public string PrototypeDay => SelectedOperation?.PrototypeDay.ToString(CultureInfo.InvariantCulture) ?? "—";
    public string Rations => SelectedOperation?.Rations ?? "—"; public string RawMinerals => SelectedOperation?.RawMinerals ?? "—";
    public string LastTickResult => SelectedOperation?.LastTickResult ?? "—";
    public IReadOnlyList<string> PendingLogistics => SelectedOperation?.PendingLogistics ?? [];
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
        Airports = snapshot.Airports; Operations = snapshot.Operations; balance = snapshot.PlayerBalance; currency = snapshot.CurrencyCode;
        SelectedAirport = airportId is null ? Airports.FirstOrDefault() : Airports.SingleOrDefault(x => x.AirportId == airportId);
        SelectedOperation = operationId is null ? Operations.FirstOrDefault() : Operations.SingleOrDefault(x => x.OperationId == operationId) ?? Operations.FirstOrDefault();
        Changed(nameof(Balance)); if (snapshot.Warnings.Count > 0) error = string.Join(" ", snapshot.Warnings);
    }
    private void NotifyOperation() { Changed(nameof(OperationTitle)); Changed(nameof(OperationStatus)); Changed(nameof(PrototypeDay)); Changed(nameof(Rations)); Changed(nameof(RawMinerals)); Changed(nameof(LastTickResult)); Changed(nameof(PendingLogistics)); }
    private void RefreshCommands() { RefreshCommand.Refresh(); FoundCommand.Refresh(); AdvanceDayCommand.Refresh(); }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
