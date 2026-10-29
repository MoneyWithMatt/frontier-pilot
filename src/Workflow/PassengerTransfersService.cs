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
    public bool Enabled => employer?.Category is "Offshore" or "Missionary";
    public PassengerPayloadCoordinator? Payload { get; init; }
    private bool payloadContinuity;
    private DateTimeOffset? payloadCheckedUtc;
    public PassengerTransport? SelectedTransport => world?.PassengerTransports.FirstOrDefault(x => x.Id == Selected?.TransportId);
    public string PayloadStatus => SelectedTransport is {} transport
        ? $"{transport.State} · {transport.Group.Count} people · {transport.Group.WeightKilograms?.ToString("0.##") ?? "weights required"} kg. {transport.Payload?.Problem} " + (payloadContinuity ? "Payload checked this connection." : "Check payload after reconnect before flight qualification.")
        : "Legacy simulated transfer: no physical payload ownership was recorded. Existing aboard transfers can complete; reserved transfers require a manifest before boarding.";
    public string SupportSummary => Payload?.SupportSummary ?? "Physical passenger adapter unavailable; new boarding is blocked.";
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
        if (current is not null && CargoJourneyKernel.Occupied(current)) throw new InvalidOperationException("Use Cargo dispatch / manifest for the active multi-stop journey; legacy cargo writes are blocked.");
        if (current?.PassengerTransfers.Any(x => x.State == PassengerTransferState.Boarded) == true || current?.PassengerTransports.Any(x => x.State is PassengerCarriageState.Boarding or PassengerCarriageState.Aboard or PassengerCarriageState.Arrived or PassengerCarriageState.Disembarking) == true)
            throw new InvalidOperationException("Disembark the current passenger transfer before loading cargo. Passenger progress is retained.");
    }
    public IReadOnlyList<CompanyAircraftType> AircraftChoices => Selected is {} t ? CompanyAircraft.Types.Where(x => x.Enabled &&
        t.EligibleAircraftTypes.Contains(x.Id) && RouteEligible(t, x.Id)).ToArray() : [];
    public bool CanBoard => Selected is { State: PassengerTransferState.Reserved } t && !HasBoardedPassengers &&
        world?.Shipments.Any(x => x.State is ShipmentState.Loaded or ShipmentState.InTransit) != true && (world is null || !CargoJourneyKernel.Occupied(world)) &&
        t.AircraftTypeId is {} type && RouteEligible(t, type) && monitor.Ready(t, utcNow(), true);
    public bool CanDisembark => Selected is { State: PassengerTransferState.Boarded } t &&
        (SelectedTransport?.State == PassengerCarriageState.Disembarked ||
        (t.AirframeId is null || SelectedAirframe?.Invoke() == t.AirframeId) && monitor.Ready(t, utcNow(), false));

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
                .Where(a => EmployedPassengerMissions.SupportsType(employer.Category, a.Id)).Select(a => a.Id).ToArray()))
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
                    var count = EmployedPassengerMissions.Count(employer.Category);
                    transfers.Add(new(id, employer.Id, EmployedCargoPrototype.PlayerId, route.Origin, route.Destination, count, route.Types,
                        employer.HourlyWage, utc, utc.AddHours(clock.OfferLifetimeHours), Purpose: EmployedPassengerMissions.Purpose(employer.Category)));
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
            if (t.State == PassengerTransferState.Offered && (!RouteEligible(t, type) || !CompanyAircraft.Types.Any(x => x.Id == type && x.Enabled)))
                throw new InvalidOperationException("Both endpoints must permit this enabled passenger aircraft.");
            var accepted = PassengerTransferKernel.Accept(w, t.Id, type, utcNow());
            if (!accepted.Changed) return accepted;
            var transport = PassengerTransportKernel.Plan(t.Id, t.PassengerCount, true);
            return new((accepted.World with {
                PassengerTransfers = accepted.World.PassengerTransfers.Select(x => x.Id == t.Id ? x with { TransportId = transport.Id } : x).ToArray(),
                PassengerTransports = accepted.World.PassengerTransports.Append(transport).ToArray() }).EnsureValid(), true);
        }, token).ConfigureAwait(false);
    }

    public async Task BoardAsync(CancellationToken token = default)
    {
        if (BoardingRecoveryGuard is not null) await BoardingRecoveryGuard(token).ConfigureAwait(false);
        PassengerTransfer? authorized = null;
        await MutateAsync(w =>
        {
            var t = w.PassengerTransfers.Single(x => x.Id == SelectedId);
            if (t.State == PassengerTransferState.Boarded) return new(w, false);
            if (t.State != PassengerTransferState.Reserved || t.AircraftTypeId is not {} type || latest is null)
                throw new InvalidOperationException("Passenger boarding is unavailable for this route/aircraft.");
            var physicalStarted = t.TransportId is {} linked && w.PassengerTransports.Single(x => x.Id == linked).Payload is not null;
            if (!physicalStarted && !RouteEligible(t, type)) throw new InvalidOperationException("Both endpoints must permit this aircraft before a new physical boarding operation.");
            var flight = physicalStarted ? t.BoardingAuthorization ?? throw new InvalidOperationException("Pending physical boarding lacks its saved flight authorization.") : monitor.Board(t, utcNow());
            if (CompanyAircraft.Resolve(flight.Aircraft, bindings()) != type)
                throw new InvalidOperationException("Confirm this exact live aircraft's company type in Career / Settings before boarding.");
            if (w.PassengerTransfers.Any(x => x.State == PassengerTransferState.Boarded) || w.Shipments.Any(x => x.State is ShipmentState.Loaded or ShipmentState.InTransit) || CargoJourneyKernel.Occupied(w) ||
                w.PassengerTransports.Any(x => x.Id != t.TransportId && x.State is PassengerCarriageState.Boarding or PassengerCarriageState.Aboard or PassengerCarriageState.Arrived or PassengerCarriageState.Disembarking))
                throw new InvalidOperationException("Complete the current passenger or cargo flight before boarding.");
            if (AircraftGuard?.Invoke(flight.Aircraft, t) is {} error) throw new InvalidOperationException(error);
            if (t.TransportId is null) throw new InvalidOperationException("Enter the passenger manifest weights before physical boarding.");
            authorized = t with { BoardingAuthorization = flight };
            return PassengerTransferKernel.Replace(w, authorized);
        }, token).ConfigureAwait(false);
        if (authorized is null) return;
        var selected = SelectedAirframe?.Invoke() ?? throw new InvalidOperationException("Select a physical fleet airframe before loading passengers.");
        var engine = Payload ?? throw new InvalidOperationException(SupportSummary);
        PassengerTransport carriage;
        try
        {
            carriage = await engine.ApplyAsync(authorized.TransportId!, authorized.AircraftTypeId!, selected, false,
                snapshot => { if (SelectedAirframe?.Invoke() != selected) throw new InvalidOperationException("Selected physical airframe changed during boarding."); Authorize(snapshot, authorized, true); }, token, BoardingRecoveryGuard,
                w => w.Shipments.Any(x => x.State is ShipmentState.Loaded or ShipmentState.InTransit) || CargoJourneyKernel.Occupied(w) ||
                    w.PassengerTransports.Any(x => x.Id != authorized.TransportId && x.State is PassengerCarriageState.Boarding or PassengerCarriageState.Aboard or PassengerCarriageState.Arrived or PassengerCarriageState.Disembarking)
                    ? "Interim career policy: finish the current Frontier load before boarding another assignment." : null).ConfigureAwait(false);
        }
        finally { world = await store.LoadAsync(CancellationToken.None).ConfigureAwait(false); }
        await MutateAsync(w => PassengerTransferKernel.Board(w, authorized.Id,
            authorized.BoardingAuthorization! with { LoadedUtc = carriage.Payload!.VerifiedUtc!.Value, LoadedPosition = carriage.Payload.VerifiedPosition! }, selected), token).ConfigureAwait(false);
        payloadContinuity = true;
        payloadCheckedUtc = utcNow();
    }

    public Task SetManifestAsync(IReadOnlyList<decimal> weights, CancellationToken token = default) => MutateAsync(w =>
    {
        var t = w.PassengerTransfers.Single(x => x.Id == SelectedId);
        if (t.State != PassengerTransferState.Reserved) throw new InvalidOperationException("Manifest weights can only change before the first physical boarding intent.");
        var transport = t.TransportId is {} id ? w.PassengerTransports.Single(x => x.Id == id) : PassengerTransportKernel.Plan(t.Id, t.PassengerCount, true);
        transport = PassengerTransportKernel.WithWeights(transport, weights);
        return new((w with { Revision = w.Revision + 1,
            PassengerTransfers = w.PassengerTransfers.Select(x => x.Id == t.Id ? x with { TransportId = transport.Id, BoardingAuthorization = null } : x).ToArray(),
            PassengerTransports = w.PassengerTransports.Where(x => x.Id != transport.Id).Append(transport).ToArray() }).EnsureValid(), true);
    }, token);

    public async Task CheckPayloadAsync(CancellationToken token = default)
    {
        payloadContinuity = false;
        world = await store.LoadAsync(token).ConfigureAwait(false);
        if (Selected is not { State: PassengerTransferState.Boarded, TransportId: {} id, AircraftTypeId: {} type } t)
            throw new InvalidOperationException("Use Board / retry boarding or Disembark / retry unloading for a pending operation.");
        var airframe = SelectedAirframe?.Invoke() ?? throw new InvalidOperationException("Select the recorded physical airframe.");
        if (airframe != t.AirframeId) throw new InvalidOperationException("Select the recorded passenger airframe.");
        var checkedPayload = await (Payload ?? throw new InvalidOperationException(SupportSummary)).CheckAboardAsync(world!.PassengerTransports.Single(x => x.Id == id), type, airframe, token).ConfigureAwait(false);
        if (checkedPayload.ObservedUtc > utcNow() || utcNow() - checkedPayload.ObservedUtc > TimeSpan.FromSeconds(2) || SelectedAirframe?.Invoke() != airframe ||
            CompanyAircraft.Resolve(checkedPayload.Aircraft, bindings()) != type)
            throw new InvalidOperationException("Payload check needs fresh readback in the recorded selected aircraft and its confirmed company type.");
        payloadContinuity = true;
        payloadCheckedUtc = utcNow();
    }

    private void Authorize(AircraftLoadingSnapshot snapshot, PassengerTransfer mission, bool boarding)
    {
        var endpoint = boarding ? mission.Origin : mission.Destination;
        if (snapshot.ObservedUtc > utcNow() || utcNow() - snapshot.ObservedUtc > TimeSpan.FromSeconds(2) ||
            WorldTracking.DistanceMeters(snapshot.Position.Latitude, snapshot.Position.Longitude, endpoint.Latitude, endpoint.Longitude) > 2778 ||
            CompanyAircraft.Resolve(snapshot.Aircraft, bindings()) != mission.AircraftTypeId || SelectedAirframe?.Invoke() is null ||
            !boarding && SelectedAirframe?.Invoke() != mission.AirframeId)
            throw new InvalidOperationException("Fresh payload telemetry must match the assigned aircraft, selected airframe and mission endpoint.");
        if (boarding && AircraftGuard?.Invoke(snapshot.Aircraft, mission) is {} error) throw new InvalidOperationException(error);
    }

    public async Task DisembarkAsync(CancellationToken token = default)
    {
        world = await store.LoadAsync(token).ConfigureAwait(false);
        if (Selected is { State: PassengerTransferState.Boarded, TransportId: {} id } mission)
        {
            var transport = world!.PassengerTransports.Single(x => x.Id == id);
            if (transport.State != PassengerCarriageState.Disembarked)
            {
                if (!monitor.Ready(mission, utcNow(), false) || SelectedAirframe?.Invoke() != mission.AirframeId)
                    throw new InvalidOperationException("Fresh qualified destination telemetry and the recorded airframe are required before unloading.");
                try
                {
                    await (Payload ?? throw new InvalidOperationException(SupportSummary)).ApplyAsync(id, mission.AircraftTypeId!, mission.AirframeId!.Value, true,
                        snapshot => Authorize(snapshot, mission, false), token).ConfigureAwait(false);
                }
                finally { world = await store.LoadAsync(CancellationToken.None).ConfigureAwait(false); }
            }
        }
        await MutateAsync(w =>
        {
            var t = w.PassengerTransfers.Single(x => x.Id == SelectedId);
            if (t.State == PassengerTransferState.Delivered) return new(w, false);
            var physicallyDisembarked = t.TransportId is {} linked && w.PassengerTransports.Single(x => x.Id == linked).State == PassengerCarriageState.Disembarked;
            if (!physicallyDisembarked && (!monitor.Ready(t, utcNow(), false) || t.AirframeId is {} id && SelectedAirframe?.Invoke() != id))
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
            if (telemetry is null || latest is {} prior && telemetry.ObservedUtc - prior.ObservedUtc > TimeSpan.FromSeconds(2) && (payloadCheckedUtc is null || payloadCheckedUtc <= prior.ObservedUtc) ||
                t.AirframeId is {} physicalId && SelectedAirframe?.Invoke() != physicalId ||
                telemetry is {} identitySample && t.Flight is {} recorded && recorded.Aircraft.ClearlyDiffersFrom(new(identitySample.AircraftTitle, identitySample.AircraftType))) payloadContinuity = false;
            latest = telemetry;
            if (t.State == PassengerTransferState.Boarded && t.TransportId is not null && !payloadContinuity) telemetry = null;
            if (t.AirframeId is {} id && SelectedAirframe?.Invoke() != id || telemetry is {} sample && (sample.ObservedUtc > utcNow() || utcNow() - sample.ObservedUtc > TimeSpan.FromSeconds(2)))
                telemetry = null;
            var flight = monitor.Observe(t, telemetry);
            var expectedRevision = world!.Revision;
            if (flight is not null && flight != t.Flight)
                world = (await store.ApplyAsync(w =>
                {
                    var current = w.PassengerTransfers.Single(x => x.Id == t.Id);
                    if (w.Revision != expectedRevision || current.State != t.State || current.Flight != t.Flight) throw new IOException("Passenger flight changed in another session. Refresh before continuing.");
                    var updated = PassengerTransferKernel.Replace(w, t with { Flight = flight }).World;
                    if (t.TransportId is {} transportId && flight.DeliveryProof is {} proof)
                        updated = updated with { PassengerTransports = updated.PassengerTransports.Select(x => x.Id == transportId && x.State == PassengerCarriageState.Aboard
                            ? x with { State = PassengerCarriageState.Arrived, Arrival = proof.ArrivalPosition } : x).ToArray() };
                    return new(updated.EnsureValid(), true);
                }, token).ConfigureAwait(false)).World;
        }
        catch { monitor.Reset(); latest = null; throw; }
        finally { gate.Release(); }
    }

    private async Task MutateAsync(Func<EconomyWorld, EconomyTransition> mutation, CancellationToken token)
    {
        if (!Enabled) throw new InvalidOperationException("Passenger missions are available for Offshore and Missionary employment.");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { world = (await store.ApplyAsync(mutation, token).ConfigureAwait(false)).World; }
        catch { monitor.Reset(); latest = null; throw; }
        finally { gate.Release(); }
    }
}
