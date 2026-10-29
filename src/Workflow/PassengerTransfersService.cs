using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

/// <summary>Typed passenger lifecycle, sharing the career's atomic economy/ledger without cargo inventory.</summary>
public sealed class PassengerTransfersService
{
    private readonly IEconomyStateStore store;
    private readonly CareerEmployer? employer;
    private readonly EmployerDestinationCatalogue destinations;
    private readonly Func<IReadOnlyList<AircraftTypeBinding>> bindings;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly FlightAttemptTracker.PassengerMonitor monitor = new();
    private EconomyWorld? world;
    public bool Enabled => employer?.Category == "Offshore";
    public Func<FlightAircraftIdentity, PassengerTransfer, string?>? AircraftGuard { get; init; }
    public Func<Guid?>? SelectedAirframe { get; init; }
    public Func<CancellationToken, Task>? BoardingRecoveryGuard { get; init; }
    public string? SelectedId { get; set; }
    public IReadOnlyList<PassengerTransfer> Transfers => world?.PassengerTransfers ?? [];
    public decimal Balance => world?.Players.Single(x => x.Id == EmployedCargoPrototype.PlayerId).CachedBalance ?? 0;
    public PassengerTransfer? Selected => Transfers.FirstOrDefault(x => x.Id == SelectedId);
    private PassengerTransfer? ObservedTransfer => Transfers.FirstOrDefault(x => x.State == PassengerTransferState.Boarded) ?? Selected;
    private AircraftTelemetry? latest;
    public string Reason => monitor.Reason;
    public bool HasBoardedPassengers => Transfers.Any(x => x.State == PassengerTransferState.Boarded);
    public async Task RequireNoBoardedPassengersAsync(CancellationToken token)
    {
        var current = await store.LoadAsync(token).ConfigureAwait(false);
        if (current?.PassengerTransfers.Any(x => x.State == PassengerTransferState.Boarded) == true)
            throw new InvalidOperationException("Disembark the current passenger transfer before loading cargo. Passenger progress is retained.");
    }
    public IReadOnlyList<CompanyAircraftType> AircraftChoices => Selected is {} t ? CompanyAircraft.Types.Where(x => x.Enabled &&
        x.PrototypePassengerSeats >= t.PassengerCount && t.EligibleAircraftTypes.Contains(x.Id) && RouteEligible(t, x.Id)).ToArray() : [];
    public bool CanBoard => Selected is { State: PassengerTransferState.Reserved } t && !HasBoardedPassengers &&
        world?.Shipments.Any(x => x.State is ShipmentState.Loaded or ShipmentState.InTransit) != true &&
        t.AircraftTypeId is {} type && RouteEligible(t, type) && monitor.Ready(t, utcNow(), true);
    public bool CanDisembark => Selected is { State: PassengerTransferState.Boarded } t &&
        (t.AirframeId is null || SelectedAirframe?.Invoke() == t.AirframeId) && monitor.Ready(t, utcNow(), false);

    public PassengerTransfersService(IEconomyStateStore store, CareerEmployer? employer, EmployerDestinationCatalogue destinations,
        Func<IReadOnlyList<AircraftTypeBinding>>? bindings = null, Func<DateTimeOffset>? utcNow = null)
    { this.store = store; this.employer = employer; this.destinations = destinations; this.bindings = bindings ?? (() => []); this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow); }

    private static PassengerEndpoint Endpoint(NamibiaLocation location) => new(location.StableId,
        location.Name + " (" + location.AirportIdentifier.Value + ")", location.ApproximateCoordinates!.Latitude, location.ApproximateCoordinates.Longitude);

    private IReadOnlyList<(PassengerEndpoint Origin, PassengerEndpoint Destination, string[] Types)> Routes()
    {
        if (!Enabled || destinations.Base(employer!) is not {} origin) return [];
        return destinations.Diagnose(employer!.Category).Where(x => x.Enabled)
            .OrderBy(x => CompanyEmployment.TimePenalty(origin, x.Resolved!, employer)).ThenBy(x => x.Identifier, StringComparer.Ordinal).Take(4)
            .Select(x => (Origin: Endpoint(origin), Destination: Endpoint(x.Resolved!), Types: CompanyAircraft.Choices(employer, x, 0)
                .Where(a => a.PrototypePassengerSeats >= 1).Select(a => a.Id).ToArray()))
            .Where(x => x.Origin.CanGenerate && x.Destination.CanGenerate && x.Types.Length > 0).ToArray();
    }
    private bool RouteEligible(PassengerTransfer transfer, string type) => Routes().Any(x => x.Origin == transfer.Origin && x.Destination == transfer.Destination && x.Types.Contains(type));

    public async Task RefreshAsync(CancellationToken token = default)
    {
        if (!Enabled) return;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // Uses the existing career initialization and persisted UTC cadence; no parallel world or clock.
            await new EmployedCargoJobsService(store, employer: employer, destinations: destinations, utcNow: utcNow).LoadAsync(token).ConfigureAwait(false);
            var routes = Routes(); var utc = utcNow();
            world = (await store.ApplyAsync(w =>
            {
                var clock = w.RealTime!;
                var transfers = w.PassengerTransfers.Select(x => x.State == PassengerTransferState.Offered && x.ExpiresUtc <= utc
                    ? x with { State = PassengerTransferState.Expired } : x).ToList();
                foreach (var route in routes)
                {
                    if (transfers.Count(x => x.Origin.Id == route.Origin.Id && x.Destination.Id == route.Destination.Id && x.State == PassengerTransferState.Offered) >= clock.OfferSlotsPerDemand) continue;
                    var id = PassengerTransferKernel.OfferId(employer!.Id, route.Destination.Id, clock.NextOffersUtc);
                    if (transfers.Any(x => x.Id == id)) continue;
                    transfers.Add(new(id, employer.Id, EmployedCargoPrototype.PlayerId, route.Origin, route.Destination, 1, route.Types,
                        employer.HourlyWage, utc, utc.AddHours(clock.OfferLifetimeHours)));
                }
                if (transfers.SequenceEqual(w.PassengerTransfers)) return new(w, false);
                return new((w with { Revision = w.Revision + 1, PassengerTransfers = transfers }).EnsureValid(), true);
            }, token).ConfigureAwait(false)).World;
            SelectedId = world.PassengerTransfers.FirstOrDefault(x => x.State == PassengerTransferState.Boarded)?.Id ?? SelectedId;
        }
        finally { gate.Release(); }
    }

    public IReadOnlyList<PassengerTransfer> VisibleTransfers => Transfers.Where(x => x.State != PassengerTransferState.Expired &&
        (x.State != PassengerTransferState.Offered || x.ExpiresUtc > utcNow() && x.EligibleAircraftTypes.Any(type => RouteEligible(x, type))))
        .OrderBy(x => x.State == PassengerTransferState.Boarded ? 0 : x.State == PassengerTransferState.Reserved ? 1 : x.State == PassengerTransferState.Offered ? 2 : 3)
        .ThenByDescending(x => x.OfferedUtc).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();

    public async Task AcceptAsync(string type, CancellationToken token = default)
    {
        await MutateAsync(w =>
        {
            var t = w.PassengerTransfers.Single(x => x.Id == SelectedId);
            if (t.State == PassengerTransferState.Offered && (!RouteEligible(t, type) || !CompanyAircraft.Types.Any(x => x.Id == type && x.Enabled && x.PrototypePassengerSeats >= t.PassengerCount)))
                throw new InvalidOperationException("Both endpoints must permit this enabled passenger aircraft.");
            return PassengerTransferKernel.Accept(w, t.Id, type, utcNow());
        }, token).ConfigureAwait(false);
    }

    public async Task BoardAsync(CancellationToken token = default)
    {
        if (BoardingRecoveryGuard is not null) await BoardingRecoveryGuard(token).ConfigureAwait(false);
        await MutateAsync(w =>
        {
            var t = w.PassengerTransfers.Single(x => x.Id == SelectedId);
            if (t.State == PassengerTransferState.Boarded) return new(w, false);
            if (t.State != PassengerTransferState.Reserved || t.AircraftTypeId is not {} type || !RouteEligible(t, type) || latest is null)
                throw new InvalidOperationException("Passenger boarding is unavailable for this route/aircraft.");
            var flight = monitor.Board(t, utcNow());
            if (CompanyAircraft.Resolve(flight.Aircraft, bindings()) != type)
                throw new InvalidOperationException("Confirm this exact live aircraft's company type in Career / Settings before boarding.");
            if (w.PassengerTransfers.Any(x => x.State == PassengerTransferState.Boarded) || w.Shipments.Any(x => x.State is ShipmentState.Loaded or ShipmentState.InTransit))
                throw new InvalidOperationException("Complete the current passenger or cargo flight before boarding.");
            if (AircraftGuard?.Invoke(flight.Aircraft, t) is {} error) throw new InvalidOperationException(error);
            return PassengerTransferKernel.Board(w, t.Id, flight, SelectedAirframe?.Invoke());
        }, token).ConfigureAwait(false);
    }

    public async Task DisembarkAsync(CancellationToken token = default)
    {
        await MutateAsync(w =>
        {
            var t = w.PassengerTransfers.Single(x => x.Id == SelectedId);
            if (t.State == PassengerTransferState.Delivered) return new(w, false);
            if (!monitor.Ready(t, utcNow(), false) || t.AirframeId is {} id && SelectedAirframe?.Invoke() != id)
                throw new InvalidOperationException("Disembarkation needs fresh stopped telemetry at the accepted destination in the recorded aircraft.");
            return PassengerTransferKernel.Complete(w, t.Id, utcNow());
        }, token).ConfigureAwait(false);
    }

    public async Task ObserveAsync(AircraftTelemetry? telemetry, CancellationToken token = default)
    {
        if (!Enabled) return;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            world = await store.LoadAsync(token).ConfigureAwait(false);
            if (ObservedTransfer is not {} t) return;
            latest = telemetry;
            if (t.AirframeId is {} id && SelectedAirframe?.Invoke() != id || telemetry is {} sample && (sample.ObservedUtc > utcNow() || utcNow() - sample.ObservedUtc > TimeSpan.FromSeconds(2)))
                telemetry = null;
            var flight = monitor.Observe(t, telemetry);
            var expectedRevision = world!.Revision;
            if (flight is not null && flight != t.Flight)
                world = (await store.ApplyAsync(w =>
                {
                    var current = w.PassengerTransfers.Single(x => x.Id == t.Id);
                    if (w.Revision != expectedRevision || current.State != t.State || current.Flight != t.Flight) throw new IOException("Passenger flight changed in another session. Refresh before continuing.");
                    return PassengerTransferKernel.Replace(w, t with { Flight = flight });
                }, token).ConfigureAwait(false)).World;
        }
        catch { monitor.Reset(); latest = null; throw; }
        finally { gate.Release(); }
    }

    private async Task MutateAsync(Func<EconomyWorld, EconomyTransition> mutation, CancellationToken token)
    {
        if (!Enabled) throw new InvalidOperationException("Passenger transfers are currently available for Offshore employment only.");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { world = (await store.ApplyAsync(mutation, token).ConfigureAwait(false)).World; }
        catch { monitor.Reset(); latest = null; throw; }
        finally { gate.Release(); }
    }
}
