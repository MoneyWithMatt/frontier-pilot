using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed record FlightAttemptTrackerOptions(double AirportRadiusNauticalMiles = 1.5,
    double MaximumGroundSpeedKnots = 5, TimeSpan? StationaryDuration = null,
    double MeaningfulMovementNauticalMiles = 2,
    TimeSpan? MaximumContinuousSampleGap = null)
{
    public TimeSpan RequiredStationaryDuration => StationaryDuration ?? TimeSpan.FromSeconds(10);
    public TimeSpan MaximumSampleGap => MaximumContinuousSampleGap ?? TimeSpan.FromSeconds(2);
    public FlightAttemptTrackerOptions EnsureValid()
    {
        if (!double.IsFinite(AirportRadiusNauticalMiles) || AirportRadiusNauticalMiles <= 0 ||
            !double.IsFinite(MaximumGroundSpeedKnots) || MaximumGroundSpeedKnots < 0 ||
            !double.IsFinite(MeaningfulMovementNauticalMiles) || MeaningfulMovementNauticalMiles <= 0 ||
            RequiredStationaryDuration <= TimeSpan.Zero || MaximumSampleGap <= TimeSpan.Zero)
            throw new InvalidDataException("Flight-attempt tracker options are invalid.");
        return this;
    }
}

public sealed record FlightAttemptStatus(string Stage, string Reason, bool IsConnected, bool CanLoadCargo,
    bool CanUnloadCargo, double? OriginDistanceNauticalMiles, double? DestinationDistanceNauticalMiles,
    FlightAttempt? Attempt, decimal PlayerBalance, string CurrencyCode)
{
    public FlightAttemptDiagnosticSnapshot Diagnostic { get; init; } = FlightAttemptDiagnosticSnapshot.Empty;
    public static FlightAttemptStatus Empty(string reason = "Accept an employed cargo job to begin.") =>
        new("No active cargo job", reason, false, false, false, null, null, null, 0, EmployedCargoPrototype.Currency);
}

public sealed record FlightAttemptDiagnosticSnapshot(string ConnectionState, string TelemetryFreshness,
    DateTimeOffset? LastValidSampleUtc, string Stage, string Origin, string Destination, string JobId, string ShipmentId,
    string RecordedAircraftIdentity, string CurrentAircraftIdentity, string OnGround, string GroundSpeed,
    string OriginDistance, string DestinationDistance, string MaximumOriginDistance, bool AirborneEvidenceRecorded,
    bool MovementEvidenceRecorded, string QualificationDuration, string RequiredQualificationDuration,
    string UnavailableReason, IReadOnlyList<FlightDiagnosticEvent> Timeline)
{
    public static FlightAttemptDiagnosticSnapshot Empty { get; } = new("Disconnected", "No valid live sample", null,
        "No active cargo job", "—", "—", "—", "—", "—", "—", "—", "—", "—", "—", "0.00 NM", false, false,
        "Not qualifying", "10 seconds", "Accept an employed cargo job to begin.", []);
}

/// <summary>Consumes validated live aircraft telemetry and is the sole application service that constructs delivery evidence.</summary>
public interface IFlightAttemptTracker
{
    string StoragePath { get; }
    FlightAttemptStatus Current { get; }
    event Action<FlightAttemptStatus>? Changed;
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task RefreshEconomyAsync(CancellationToken cancellationToken = default);
    Task ObserveAsync(AircraftTelemetry? telemetry, CancellationToken cancellationToken = default);
    Task LoadCargoAsync(CancellationToken cancellationToken = default);
    Task LoadCargoAsync(AircraftLoadingMode mode, CancellationToken cancellationToken = default) => LoadCargoAsync(cancellationToken);
    Task UnloadCargoAsync(CancellationToken cancellationToken = default);
    Task ApplyCargoTransferAsync(string transferId, string commandId, decimal desiredLoadedKilograms,
        string? currentLocationId, double latitude, double longitude, bool confirmForfeit,
        AircraftLoadingMode mode, CancellationToken cancellationToken = default) =>
        desiredLoadedKilograms > 0 ? LoadCargoAsync(mode, cancellationToken) : UnloadCargoAsync(cancellationToken);
    Task<string> ExportDeveloperDiagnosticAsync(CancellationToken cancellationToken = default);
    Task<string> CreateDiagnosticSummaryAsync(CancellationToken cancellationToken = default);
    Task ResetIncompletePrototypeProgressAsync(CancellationToken cancellationToken = default);
    Task AbandonIncompletePrototypeShipmentAsync(string jobId, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Prototype shipment abandonment is unavailable."));
}

public sealed class FlightAttemptTracker : IFlightAttemptTracker
{
    private const double MetersPerNauticalMile = 1852;
    private readonly EconomyService economy;
    private readonly IFlightAttemptStore store;
    private readonly NamibiaLocationCatalogueDocument catalogue;
    private readonly FlightAttemptTrackerOptions options;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Func<DateTimeOffset> utcNow;
    private EconomyWorld? world;
    public Func<object?>? AircraftLoadingDiagnosticProvider { get; set; }
    private FlightAttempt? attempt;
    private AircraftTelemetry? latest;
    private DateTimeOffset? stationarySince, lastSampleUtc, lastValidSampleUtc;
    private bool connected;
    private IReadOnlyList<FlightDiagnosticEvent> timeline = [];
    private string diagnosticWarning = "";
    private string observationReason = "SimConnect is disconnected.";
    public string StoragePath => store.StoragePath;
    public FlightAttemptStatus Current { get; private set; } = FlightAttemptStatus.Empty();
    public event Action<FlightAttemptStatus>? Changed;

    public static double RequiredDistanceForRoute(double directRouteDistanceNauticalMiles)
    {
        if (!double.IsFinite(directRouteDistanceNauticalMiles) || directRouteDistanceNauticalMiles < 0)
            throw new ArgumentOutOfRangeException(nameof(directRouteDistanceNauticalMiles));
        return Math.Min(10, Math.Max(0.5, directRouteDistanceNauticalMiles * 0.5));
    }

    public FlightAttemptTracker(IEconomyStateStore economyStore, IFlightAttemptStore attemptStore,
        NamibiaLocationCatalogueDocument? catalogue = null, FlightAttemptTrackerOptions? options = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        economy = new(economyStore); store = attemptStore; this.catalogue = (catalogue ?? NamibiaLocationCatalogue.Load()).EnsureValid();
        this.options = (options ?? new()).EnsureValid(); this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default) => await RefreshEconomyAsync(cancellationToken);

    public async Task RefreshEconomyAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            world = await economy.LoadAsync(cancellationToken).ConfigureAwait(false);
            var attempts = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
            timeline = await store.LoadDiagnosticTimelineAsync(cancellationToken).ConfigureAwait(false);
            attempt = SelectAttempt(world, attempts);
            await ReconcileAsync(cancellationToken).ConfigureAwait(false);
            if (attempt is not null) await RecordAsync(FlightDiagnosticEventType.ApplicationRecovery, utcNow(), "recovery",
                cancellationToken, ("state", attempt.State.ToString()), ("message", "Existing flight attempt loaded and reconciled.")).ConfigureAwait(false);
            UpdateStatus();
        }
        finally { gate.Release(); }
    }

    public async Task ObserveAsync(AircraftTelemetry? telemetry, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (telemetry is null)
            {
                await InterruptQualificationAsync("SimConnect disconnected.", utcNow(), cancellationToken).ConfigureAwait(false);
                var disconnectedUtc = utcNow();
                if (connected) await RecordAsync(FlightDiagnosticEventType.SimConnectDisconnected, disconnectedUtc, "disconnect-" + disconnectedUtc.UtcTicks, cancellationToken,
                    ("reason", "No live telemetry was supplied.")).ConfigureAwait(false);
                Pause("SimConnect is disconnected."); UpdateStatus(); return;
            }
            AircraftTelemetry sample;
            try { sample = telemetry.EnsureValid(); }
            catch (Exception error) when (error is InvalidDataException or ArgumentException)
            {
                var reason = "Aircraft telemetry is invalid: " + error.Message;
                await InterruptQualificationAsync(reason, utcNow(), cancellationToken).ConfigureAwait(false);
                var invalidUtc = utcNow();
                if (connected) await RecordAsync(FlightDiagnosticEventType.SimConnectDisconnected, invalidUtc, "invalid-telemetry-" + invalidUtc.UtcTicks, cancellationToken,
                    ("reason", reason)).ConfigureAwait(false);
                Pause(reason); UpdateStatus(); return;
            }

            bool continuous = lastSampleUtc is { } previous && sample.ObservedUtc > previous && sample.ObservedUtc - previous <= options.MaximumSampleGap;
            if (!continuous && stationarySince is not null)
                await InterruptQualificationAsync("Telemetry continuity gap exceeded the configured maximum.", sample.ObservedUtc, cancellationToken).ConfigureAwait(false);
            if (lastSampleUtc is { } prior && sample.ObservedUtc <= prior)
            {
                await InterruptQualificationAsync("Aircraft telemetry time did not advance.", sample.ObservedUtc, cancellationToken).ConfigureAwait(false);
                if (connected) await RecordAsync(FlightDiagnosticEventType.SimConnectDisconnected, sample.ObservedUtc,
                    "nonadvancing-" + sample.ObservedUtc.UtcTicks, cancellationToken, ("reason", "Aircraft telemetry time did not advance.")).ConfigureAwait(false);
                Pause("Aircraft telemetry time did not advance."); UpdateStatus(); return;
            }
            var wasConnected = connected;
            connected = true; latest = sample; lastSampleUtc = sample.ObservedUtc; lastValidSampleUtc = sample.ObservedUtc; observationReason = "Live aircraft telemetry received.";
            if (!wasConnected) await RecordAsync(FlightDiagnosticEventType.SimConnectConnected, sample.ObservedUtc, "connect-" + sample.ObservedUtc.UtcTicks,
                cancellationToken, ("aircraftTitle", sample.AircraftTitle), ("aircraftType", sample.AircraftType)).ConfigureAwait(false);
            if (world is null) { UpdateStatus(); return; }
            if (IsTerminalAttempt())
            {
                stationarySince = null;
                observationReason = "Live aircraft telemetry received; completed delivery remains unchanged.";
                UpdateStatus(); return;
            }
            var active = ActiveJob(world);
            if (active is null || attempt is null)
            {
                var before = stationarySince; EvaluateOriginStationary(active);
                if (before is null && stationarySince is { } started)
                    await RecordAsync(FlightDiagnosticEventType.OriginQualificationStarted, started, "origin-start-" + started.UtcTicks,
                        cancellationToken, ("distanceNm", CurrentDistanceToActiveOrigin().ToString("F3")), ("maximumGroundSpeedKnots", options.MaximumGroundSpeedKnots.ToString("F1"))).ConfigureAwait(false);
                else if (before is not null && stationarySince is null)
                    await RecordAsync(FlightDiagnosticEventType.OriginQualificationInterrupted, sample.ObservedUtc, "origin-stop-" + before.Value.UtcTicks,
                        cancellationToken, ("reason", observationReason)).ConfigureAwait(false);
                if (stationarySince is { } since && sample.ObservedUtc - since >= options.RequiredStationaryDuration)
                    await RecordAsync(FlightDiagnosticEventType.ReadyToLoad, sample.ObservedUtc, "ready-load", cancellationToken,
                        ("qualifiedSeconds", (sample.ObservedUtc - since).TotalSeconds.ToString("F1"))).ConfigureAwait(false);
                UpdateStatus(); return;
            }

            var activeShipmentBeforeDeparture = world.Shipments.Single(x => x.Id == active.ShipmentId);
            if (attempt.FirstAirborneUtc is null && activeShipmentBeforeDeparture.State == ShipmentState.Loaded && sample.IsOnGround)
            {
                var before = stationarySince; EvaluateOriginStationary(active);
                if (before is null && stationarySince is { } started)
                    await RecordAsync(FlightDiagnosticEventType.OriginQualificationStarted, started, "origin-reload-start-" + started.UtcTicks,
                        cancellationToken, ("distanceNm", CurrentDistanceToActiveOrigin().ToString("F3")), ("purpose", "additional reserved cargo")).ConfigureAwait(false);
                if (stationarySince is { } since && sample.ObservedUtc - since >= options.RequiredStationaryDuration)
                    await RecordAsync(FlightDiagnosticEventType.ReadyToLoad, sample.ObservedUtc, "ready-load-more", cancellationToken,
                        ("qualifiedSeconds", (sample.ObservedUtc - since).TotalSeconds.ToString("F1"))).ConfigureAwait(false);
                UpdateStatus(); return;
            }

            if (!TryIdentity(sample, out var identity, out var identityError))
            { await InterruptQualificationAsync(identityError, sample.ObservedUtc, cancellationToken).ConfigureAwait(false); observationReason = identityError; UpdateStatus(); return; }
            if (attempt.Aircraft.ClearlyDiffersFrom(identity!))
            {
                await InterruptQualificationAsync("Aircraft identity differs from the loaded aircraft.", sample.ObservedUtc, cancellationToken).ConfigureAwait(false);
                observationReason = $"Aircraft identity differs from the loaded aircraft ({attempt.Aircraft.Title} / {attempt.Aircraft.Type}).";
                await RecordAsync(FlightDiagnosticEventType.AircraftIdentityMismatchObserved, sample.ObservedUtc, "identity-mismatch", cancellationToken,
                    ("recorded", attempt.Aircraft.Title + " / " + attempt.Aircraft.Type), ("observed", identity!.Title + " / " + identity.Type)).ConfigureAwait(false);
                UpdateStatus(); return;
            }
            if (attempt.DeliveryProof is not null)
            { observationReason = "Verified arrival is persisted. Unload cargo when ready."; UpdateStatus(); return; }

            var origin = Coordinates(attempt.OriginLocationId);
            double originDistance = DistanceNm(sample, origin); var updated = attempt;
            bool persist = false, recordedAirborne = false, recordedMovement = false, recordedMaximum = false, recordedReadyToUnload = false;
            if (!sample.IsOnGround && updated.FirstAirborneUtc is null)
            {
                updated = updated with { FirstAirborneUtc = sample.ObservedUtc, State = FlightAttemptState.Departed }; persist = true;
                recordedAirborne = true;
            }
            if (updated.FirstAirborneUtc is { } airborne && sample.ObservedUtc > airborne &&
                originDistance >= options.MeaningfulMovementNauticalMiles && updated.MovementObservedUtc is null)
            {
                updated = updated with { MovementObservedUtc = sample.ObservedUtc, State = FlightAttemptState.EnRoute }; persist = true;
                recordedMovement = true;
            }
            if (originDistance > updated.MaximumDistanceFromOriginNauticalMiles)
            {
                bool crossedRequirement = updated.MaximumDistanceFromOriginNauticalMiles < updated.RequiredMaximumDistanceNauticalMiles && originDistance >= updated.RequiredMaximumDistanceNauticalMiles;
                bool checkpoint = originDistance - updated.MaximumDistanceFromOriginNauticalMiles >= 0.25;
                updated = updated with { MaximumDistanceFromOriginNauticalMiles = originDistance, MaximumDistanceObservedUtc = sample.ObservedUtc };
                persist |= crossedRequirement || checkpoint;
                recordedMaximum = crossedRequirement;
            }
            attempt = updated;

            if (attempt.TransitBeginsAtDeparture && attempt.OriginDepartedUtc is null &&
                attempt.FirstAirborneUtc is not null && !sample.IsOnGround && originDistance > options.AirportRadiusNauticalMiles)
            { attempt = attempt with { OriginDepartedUtc = sample.ObservedUtc }; persist = true; }
            bool evidenceComplete = (!attempt.TransitBeginsAtDeparture || attempt.OriginDepartedUtc is not null) &&
                attempt.FirstAirborneUtc is not null && attempt.MovementObservedUtc is not null &&
                attempt.MaximumDistanceFromOriginNauticalMiles >= attempt.RequiredMaximumDistanceNauticalMiles;
            var destination = Coordinates(attempt.DestinationLocationId);
            double destinationDistance = DistanceNm(sample, destination);
            if (attempt.DeliveryProof is null && evidenceComplete && destinationDistance > options.AirportRadiusNauticalMiles)
            {
                observationReason = $"Aircraft is {destinationDistance:F2} NM from the destination; arrival requires {options.AirportRadiusNauticalMiles:F1} NM or less.";
                await InterruptQualificationAsync(observationReason, sample.ObservedUtc, cancellationToken).ConfigureAwait(false);
            }
            else if (attempt.DeliveryProof is null && evidenceComplete && !sample.IsOnGround)
            {
                observationReason = "Aircraft is airborne; unloading requires a stopped on-ground arrival.";
                await InterruptQualificationAsync(observationReason, sample.ObservedUtc, cancellationToken).ConfigureAwait(false);
            }
            else if (attempt.DeliveryProof is null && evidenceComplete && sample.GroundSpeedKnots > options.MaximumGroundSpeedKnots)
            {
                observationReason = $"Aircraft ground speed is {sample.GroundSpeedKnots:F1} kt; arrival requires {options.MaximumGroundSpeedKnots:F0} kt or less.";
                await InterruptQualificationAsync(observationReason, sample.ObservedUtc, cancellationToken).ConfigureAwait(false);
            }
            else if (attempt.DeliveryProof is null && evidenceComplete)
            {
                if (stationarySince is null)
                {
                    stationarySince = sample.ObservedUtc;
                    await RecordAsync(FlightDiagnosticEventType.ArrivalQualificationStarted, sample.ObservedUtc,
                        "arrival-start-" + sample.ObservedUtc.UtcTicks, cancellationToken, ("destinationDistanceNm", destinationDistance.ToString("F3"))).ConfigureAwait(false);
                }
                if (sample.ObservedUtc - stationarySince >= options.RequiredStationaryDuration)
                {
                    var proof = new FlightDeliveryProof(FlightAttemptIds.DeliveryEvidence(attempt.ShipmentId), sample.ObservedUtc, FlightPosition.From(sample),
                        attempt.FirstAirborneUtc!.Value, attempt.MovementObservedUtc!.Value, attempt.MaximumDistanceFromOriginNauticalMiles).EnsureValid();
                    attempt = attempt with { State = FlightAttemptState.ReadyToUnload, DeliveryProof = proof }; persist = true;
                    observationReason = "Verified arrival is persisted. Unload cargo when ready.";
                    recordedReadyToUnload = true;
                }
                else observationReason = $"Stopped near destination for {(sample.ObservedUtc - stationarySince.Value).TotalSeconds:F1} of {options.RequiredStationaryDuration.TotalSeconds:F0} seconds.";
            }
            else if (attempt.DeliveryProof is null) await InterruptQualificationAsync("Required persisted flight evidence is incomplete.", sample.ObservedUtc, cancellationToken).ConfigureAwait(false);
            if (persist) await store.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);
            if (attempt.TransitBeginsAtDeparture && attempt.OriginDepartedUtc is { } departureUtc &&
                world.Shipments.Single(x => x.Id == attempt.ShipmentId).State == ShipmentState.Loaded)
            {
                world = (await economy.MarkShipmentInTransitAsync(attempt.JobId, cancellationToken).ConfigureAwait(false)).World;
                await RecordAsync(FlightDiagnosticEventType.ShipmentEnteredInTransit, departureUtc, "in-transit", cancellationToken,
                    ("reason", "Airborne outside the origin airport radius.")).ConfigureAwait(false);
            }
            if (recordedAirborne) await RecordAsync(FlightDiagnosticEventType.AirborneEvidenceRecorded, sample.ObservedUtc, "airborne", cancellationToken,
                ("onGround", "false")).ConfigureAwait(false);
            if (recordedMovement) await RecordAsync(FlightDiagnosticEventType.PostDepartureMovementRecorded, sample.ObservedUtc, "movement", cancellationToken,
                ("originDistanceNm", originDistance.ToString("F3"))).ConfigureAwait(false);
            if (recordedMaximum) await RecordAsync(FlightDiagnosticEventType.RequiredMaximumDistanceReached, sample.ObservedUtc,
                "maximum-distance", cancellationToken, ("originDistanceNm", originDistance.ToString("F3")),
                ("requiredNm", attempt.RequiredMaximumDistanceNauticalMiles.ToString("F2"))).ConfigureAwait(false);
            if (recordedReadyToUnload) await RecordAsync(FlightDiagnosticEventType.ReadyToUnload, sample.ObservedUtc, "ready-unload", cancellationToken,
                ("qualifiedSeconds", (sample.ObservedUtc - stationarySince!.Value).TotalSeconds.ToString("F1"))).ConfigureAwait(false);
            UpdateStatus();
        }
        finally { gate.Release(); }
    }

    public Task LoadCargoAsync(CancellationToken cancellationToken = default) => LoadCargoAsync(AircraftLoadingMode.SimulatedFrontierLoading, cancellationToken);

    public async Task LoadCargoAsync(AircraftLoadingMode mode, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (world is not null && attempt is not null)
            {
                var existingJob = ActiveJob(world);
                var existingShipment = existingJob is null ? null : world.Shipments.Single(x => x.Id == existingJob.ShipmentId);
                if (existingJob?.Id == attempt.JobId && existingShipment?.State is (ShipmentState.Loaded or ShipmentState.InTransit)) { UpdateStatus(); return; }
            }
            UpdateStatus(); if (!Current.CanLoadCargo || latest is null || world is null) throw new InvalidOperationException(Current.Reason);
            var job = ActiveJob(world) ?? throw new InvalidOperationException("No accepted shipment is available for loading.");
            var requirement = world.Requirements.Single(x => x.Id == job.RequirementId);
            var shipment = world.Shipments.Single(x => x.Id == job.ShipmentId);
            if (shipment.State is (ShipmentState.Loaded or ShipmentState.InTransit) && attempt is not null) { UpdateStatus(); return; }
            if (shipment.State != ShipmentState.Reserved) throw new InvalidOperationException("The accepted shipment is not reserved for loading.");
            var identity = FlightAircraftIdentity.From(latest);
            var origin = Coordinates(requirement.OriginLocationId); var destination = Coordinates(requirement.DestinationLocationId);
            var directNm = WorldTracking.DistanceMeters(origin.Latitude, origin.Longitude, destination.Latitude, destination.Longitude) / MetersPerNauticalMile;
            attempt ??= new FlightAttempt(FlightAttemptIds.Attempt(shipment.Id), job.Id, shipment.Id, requirement.OriginLocationId,
                requirement.DestinationLocationId, FlightAttemptState.CargoLoaded, latest.ObservedUtc, identity, FlightPosition.From(latest),
                RequiredMaximumDistanceNauticalMiles: RequiredDistanceForRoute(directNm), TransitBeginsAtDeparture: true).EnsureValid();
            await store.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);
            await RecordAsync(FlightDiagnosticEventType.FlightAttemptCreated, latest.ObservedUtc, "created", cancellationToken,
                ("origin", attempt.OriginLocationId), ("destination", attempt.DestinationLocationId)).ConfigureAwait(false);
            await RecordAsync(FlightDiagnosticEventType.CargoLoaded, latest.ObservedUtc, "cargo-loaded", cancellationToken,
                ("aircraft", identity.Title + " / " + identity.Type), ("mode", mode == AircraftLoadingMode.MsfsPayloadIntegration ? "MSFS payload integration; station weights verified" : "simulated; MSFS payload unchanged")).ConfigureAwait(false);
            var transition = await economy.MarkShipmentLoadedAsync(job.Id, cancellationToken).ConfigureAwait(false);
            world = transition.World;
            await RecordAsync(FlightDiagnosticEventType.ShipmentLoaded, latest.ObservedUtc, "shipment-loaded", cancellationToken,
                ("shipmentState", ShipmentState.Loaded.ToString())).ConfigureAwait(false);
            stationarySince = null; observationReason = mode == AircraftLoadingMode.MsfsPayloadIntegration
                ? "Cargo station weights were verified in MSFS and the shipment is Loaded; InTransit begins after departure."
                : "Cargo loaded in Frontier Pilot compatibility mode; MSFS payload stations are unchanged."; UpdateStatus();
        }
        finally { gate.Release(); }
    }

    public async Task ApplyCargoTransferAsync(string transferId, string commandId, decimal desiredLoadedKilograms,
        string? currentLocationId, double latitude, double longitude, bool confirmForfeit,
        AircraftLoadingMode mode, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (world is null) throw new InvalidOperationException("Economy state is unavailable.");
            var job = ActiveJob(world);
            if (job is null)
            {
                var replay = world.CargoTransfers.SingleOrDefault(x => x.CommandId == commandId);
                if (replay is not null)
                {
                    observationReason = "The persisted cargo transfer was already committed; no inventory or payment was repeated.";
                    UpdateStatus();
                    return;
                }
                throw new InvalidOperationException("No accepted shipment is available for cargo transfer.");
            }
            var shipment = world.Shipments.Single(x => x.Id == job.ShipmentId);
            var loading = desiredLoadedKilograms > shipment.LoadedQuantity + .001m;
            if (loading && attempt is null)
            {
                UpdateStatus();
                if (!Current.CanLoadCargo || latest is null) throw new InvalidOperationException(Current.Reason);
                var requirement = world.Requirements.Single(x => x.Id == job.RequirementId);
                var identity = FlightAircraftIdentity.From(latest);
                var origin = Coordinates(requirement.OriginLocationId); var destination = Coordinates(requirement.DestinationLocationId);
                var directNm = WorldTracking.DistanceMeters(origin.Latitude, origin.Longitude, destination.Latitude, destination.Longitude) / MetersPerNauticalMile;
                attempt = new FlightAttempt(FlightAttemptIds.Attempt(shipment.Id), job.Id, shipment.Id, requirement.OriginLocationId,
                    requirement.DestinationLocationId, FlightAttemptState.CargoLoaded, latest.ObservedUtc, identity, FlightPosition.From(latest),
                    RequiredMaximumDistanceNauticalMiles: RequiredDistanceForRoute(directNm), TransitBeginsAtDeparture: true).EnsureValid();
                await store.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);
                await RecordAsync(FlightDiagnosticEventType.FlightAttemptCreated, latest.ObservedUtc, "created", cancellationToken,
                    ("origin", attempt.OriginLocationId), ("destination", attempt.DestinationLocationId)).ConfigureAwait(false);
            }
            VerifiedDeliveryEvidence? evidence = null;
            if (attempt?.DeliveryProof is { } proof)
                evidence = new VerifiedDeliveryEvidence(proof.EvidenceId, "frontier-flight-attempt-v0", attempt.JobId, attempt.ShipmentId,
                    attempt.OriginLocationId, attempt.DestinationLocationId, proof.ReadyUtc,
                    $"Persisted flight proof: airborne {proof.FirstAirborneUtc:O}; movement {proof.MovementObservedUtc:O}; maximum origin distance {proof.MaximumDistanceFromOriginNauticalMiles:F2} NM.").EnsureValid();
            var request = new ShipmentCargoTransferRequest(transferId, commandId, job.Id, desiredLoadedKilograms,
                currentLocationId, latitude, longitude, utcNow(), confirmForfeit, evidence);
            var transition = await economy.ApplyCargoTransferAsync(request, cancellationToken).ConfigureAwait(false);
            world = transition.World;
            var updatedJob = world.Jobs.Single(x => x.Id == job.Id);
            var updatedShipment = world.Shipments.Single(x => x.Id == shipment.Id);
            if (loading && transition.Changed)
            {
                await RecordAsync(FlightDiagnosticEventType.CargoLoaded, request.OccurredUtc, "cargo-transfer-" + commandId, cancellationToken,
                    ("aircraft", attempt!.Aircraft.Title + " / " + attempt.Aircraft.Type), ("mode", mode.ToString()),
                    ("loadedKilograms", updatedShipment.LoadedQuantity.ToString("0.###"))).ConfigureAwait(false);
                await RecordAsync(FlightDiagnosticEventType.ShipmentLoaded, request.OccurredUtc, "shipment-loaded", cancellationToken,
                    ("shipmentState", updatedShipment.State.ToString())).ConfigureAwait(false);
            }
            if (updatedJob.State == EconomyJobState.Completed && attempt is not null && attempt.State != FlightAttemptState.Delivered)
            {
                attempt = attempt with { State = FlightAttemptState.Delivered, DeliveredUtc = evidence?.VerifiedUtc ?? request.OccurredUtc };
                await store.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);
                await RecordAsync(FlightDiagnosticEventType.DeliveryCommitted, attempt.DeliveredUtc!.Value, "delivery-committed", cancellationToken,
                    ("economyChanged", transition.Changed.ToString()), ("evidenceId", evidence?.Id ?? "unavailable")).ConfigureAwait(false);
            }
            stationarySince = null;
            observationReason = updatedJob.State switch
            {
                EconomyJobState.Completed => "All shipment cargo was physically delivered; inventory and payment were committed once.",
                EconomyJobState.Forfeited => "Cargo was unloaded away from its valid destination; the job was forfeited without payment.",
                _ when updatedShipment.DeliveredQuantity > 0 => $"Partial delivery recorded: {updatedShipment.DeliveredQuantity:0.###} of {updatedShipment.Quantity:0.###} kg.",
                _ => $"Aircraft now carries {updatedShipment.LoadedQuantity:0.###} kg of this reserved shipment."
            };
            UpdateStatus();
        }
        finally { gate.Release(); }
    }

    public async Task UnloadCargoAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RecordAsync(FlightDiagnosticEventType.UnloadRequested, utcNow(), "unload-requested", cancellationToken).ConfigureAwait(false);
            var economyCommitted = false;
            try
            {
                if (attempt?.DeliveryProof is null || world is null) throw new InvalidOperationException("Verified arrival evidence is not ready.");
                var proof = attempt.DeliveryProof;
                var evidence = new VerifiedDeliveryEvidence(proof.EvidenceId, "frontier-flight-attempt-v0", attempt.JobId, attempt.ShipmentId,
                    attempt.OriginLocationId, attempt.DestinationLocationId, proof.ReadyUtc,
                    $"Persisted flight proof: airborne {proof.FirstAirborneUtc:O}; movement {proof.MovementObservedUtc:O}; maximum origin distance {proof.MaximumDistanceFromOriginNauticalMiles:F2} NM.").EnsureValid();
                var transition = await economy.CompleteDeliveryAsync(evidence, cancellationToken).ConfigureAwait(false); world = transition.World; economyCommitted = true;
                if (attempt.State != FlightAttemptState.Delivered)
                { attempt = attempt with { State = FlightAttemptState.Delivered, DeliveredUtc = proof.ReadyUtc }; await store.SaveAsync(attempt, cancellationToken).ConfigureAwait(false); }
                await RecordAsync(FlightDiagnosticEventType.DeliveryCommitted, proof.ReadyUtc, "delivery-committed", cancellationToken,
                    ("economyChanged", transition.Changed.ToString()), ("evidenceId", proof.EvidenceId)).ConfigureAwait(false);
                stationarySince = null;
                observationReason = transition.Changed ? "Cargo delivered, customer inventory updated and payment recorded." : "Delivery was already recorded; no cargo or payment was duplicated.";
                UpdateStatus();
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                if (!economyCommitted)
                    await RecordAsync(FlightDiagnosticEventType.DeliveryRefused, utcNow(), "delivery-refused-" + error.GetType().Name,
                        cancellationToken, ("reason", error.Message), ("errorType", error.GetType().Name)).ConfigureAwait(false);
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public async Task<string> ExportDeveloperDiagnosticAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
            var selectedJob = world is null ? null : ActiveJob(world) ?? world.Jobs.Where(x => x.State == EconomyJobState.Completed).OrderByDescending(x => x.CompletedUtc).FirstOrDefault();
            var requirement = selectedJob is null ? null : world!.Requirements.Single(x => x.Id == selectedJob.RequirementId);
            var shipment = selectedJob is null ? null : world!.Shipments.SingleOrDefault(x => x.Id == selectedJob.ShipmentId);
            var payments = selectedJob is null ? [] : world!.FinancialLedger.Where(x => x.JobId == selectedJob.Id).ToArray();
            return JsonSerializer.Serialize(new
            {
                SupportBundleSchemaVersion = 1,
                ExportedUtc = utcNow(),
                Notice = "Read-only, redacted Frontier Pilot flight diagnostic. Diagnostic events are not delivery evidence.",
                ApplicationVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unavailable",
                PersistenceSchemas = new { Economy = LocalEconomyStateStore.CurrentSchemaVersion, FlightAttempt = LocalFlightAttemptStore.CurrentSchemaVersion, AircraftLoading = LocalAircraftLoadingStore.CurrentSchemaVersion },
                Thresholds = new { options.AirportRadiusNauticalMiles, options.MaximumGroundSpeedKnots,
                    StationaryDurationSeconds = options.RequiredStationaryDuration.TotalSeconds, options.MeaningfulMovementNauticalMiles,
                    RouteSensitiveMaximumDistanceFormula = "min(10, max(0.5, directRouteDistanceNm * 0.5))", MaximumContinuousSampleGapSeconds = options.MaximumSampleGap.TotalSeconds },
                LiveTelemetry = latest,
                DerivedDiagnostic = Current.Diagnostic with { Timeline = [] },
                FlightAttempt = attempt,
                PersistedEvidence = attempt is null ? null : new { Airborne = attempt.FirstAirborneUtc is not null,
                    Movement = attempt.MovementObservedUtc is not null, MaximumDistanceNauticalMiles = attempt.MaximumDistanceFromOriginNauticalMiles,
                    ArrivalProof = attempt.DeliveryProof is not null },
                DiagnosticTimeline = Current.Diagnostic.Timeline,
                AircraftLoading = AircraftLoadingDiagnosticProvider?.Invoke(),
                Economy = selectedJob is null ? null : new { SchemaVersion = LocalEconomyStateStore.CurrentSchemaVersion,
                    WorldId = world!.Id, world.Revision, Job = selectedJob, Requirement = requirement, Shipment = shipment,
                    RelatedPayments = payments, PlayerBalance = world.Players.SingleOrDefault(x => x.Id == selectedJob.PlayerId)?.CachedBalance }
            }, jsonOptions);
        }
        finally { gate.Release(); }
    }

    public async Task<string> CreateDiagnosticSummaryAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var d = Current.Diagnostic; var lines = new[]
            {
                "Frontier Pilot Flight Attempt diagnostic",
                $"Connection: {d.ConnectionState}; telemetry: {d.TelemetryFreshness}; last valid UTC: {d.LastValidSampleUtc:O}",
                $"Stage: {d.Stage}; reason: {d.UnavailableReason}",
                $"Route: {d.Origin} -> {d.Destination}",
                $"Job: {d.JobId}; shipment: {d.ShipmentId}",
                $"Recorded aircraft: {d.RecordedAircraftIdentity}; observed aircraft: {d.CurrentAircraftIdentity}",
                $"Live: on ground {d.OnGround}; speed {d.GroundSpeed}; origin {d.OriginDistance}; destination {d.DestinationDistance}",
                $"Persisted: airborne {d.AirborneEvidenceRecorded}; movement {d.MovementEvidenceRecorded}; maximum origin distance {d.MaximumOriginDistance}",
                $"Qualification: {d.QualificationDuration} / required {d.RequiredQualificationDuration}",
                $"Timeline events: {d.Timeline.Count}"
            };
            var summary=string.Join(Environment.NewLine, lines);
            if(AircraftLoadingDiagnosticProvider?.Invoke() is { } loading)summary+=Environment.NewLine+"Aircraft loading: "+JsonSerializer.Serialize(loading);
            return summary;
        }
        finally { gate.Release(); }
    }

    public async Task ResetIncompletePrototypeProgressAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (world is null || attempt is null) throw new InvalidOperationException("No loaded prototype flight attempt is available to reset.");
            var job = world.Jobs.SingleOrDefault(x => x.Id == attempt.JobId);
            var shipment = world.Shipments.SingleOrDefault(x => x.Id == attempt.ShipmentId);
            if (job?.State != EconomyJobState.Accepted || shipment?.State is not (ShipmentState.Loaded or ShipmentState.InTransit) ||
                attempt.State == FlightAttemptState.Delivered || attempt.DeliveredUtc is not null)
                throw new InvalidOperationException("Only observation progress for an incomplete, loaded shipment can be reset. Completed economy state is protected.");
            attempt = await store.ResetIncompleteProgressAsync(attempt.Id, cancellationToken).ConfigureAwait(false);
            stationarySince = null; lastSampleUtc = null; latest = null; connected = false;
            observationReason = "Developer reset cleared incomplete flight observations. Shipment remains loaded and InTransit; no inventory or money changed.";
            UpdateStatus();
        }
        finally { gate.Release(); }
    }

    public async Task AbandonIncompletePrototypeShipmentAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transition = await economy.AbandonIncompletePrototypeShipmentAsync(jobId, utcNow(), cancellationToken).ConfigureAwait(false);
            world = transition.World; attempt = SelectAttempt(world, await store.LoadAsync(cancellationToken).ConfigureAwait(false));
            stationarySince = null; lastSampleUtc = null;
            observationReason = transition.Changed
                ? "Incomplete prototype mission skipped. Reserved goods returned to origin inventory; no delivery or payment was created."
                : "Prototype mission was already skipped; no inventory or money changed.";
            UpdateStatus();
        }
        finally { gate.Release(); }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        if (world is null) return;
        if (attempt is null)
        {
            var activeWithoutAttempt = ActiveJob(world);
            if (activeWithoutAttempt is not null && world.Shipments.Single(x => x.Id == activeWithoutAttempt.ShipmentId).State is (ShipmentState.Loaded or ShipmentState.InTransit))
                throw new IOException("An in-transit shipment has no persisted flight attempt. Files preserved.");
            return;
        }
        var job = world.Jobs.SingleOrDefault(x => x.Id == attempt.JobId) ?? throw new IOException("Flight attempt refers to an unknown job. File preserved.");
        if (job.ShipmentId != attempt.ShipmentId) throw new IOException("Flight attempt refers to the wrong shipment. File preserved.");
        var requirement = world.Requirements.Single(x => x.Id == job.RequirementId);
        if (requirement.OriginLocationId != attempt.OriginLocationId || requirement.DestinationLocationId != attempt.DestinationLocationId)
            throw new IOException("Flight attempt route does not match its job. File preserved.");
        var shipment = world.Shipments.Single(x => x.Id == attempt.ShipmentId);
        if (job.State == EconomyJobState.Accepted && shipment.State == ShipmentState.Reserved && !attempt.TransitBeginsAtDeparture)
            world = (await economy.MarkShipmentInTransitAsync(job.Id, cancellationToken).ConfigureAwait(false)).World;
        else if (job.State == EconomyJobState.Accepted && shipment.State is not (ShipmentState.Loaded or ShipmentState.InTransit))
            throw new IOException("Flight attempt shipment state is inconsistent. Files preserved.");
        if (job.State == EconomyJobState.Accepted && attempt.TransitBeginsAtDeparture && attempt.OriginDepartedUtc is not null)
            world = (await economy.MarkShipmentInTransitAsync(job.Id, cancellationToken).ConfigureAwait(false)).World;
        if (job.State == EconomyJobState.Completed && attempt.DeliveryProof is not null)
        {
            if (attempt.State != FlightAttemptState.Delivered)
            { attempt = attempt with { State = FlightAttemptState.Delivered, DeliveredUtc = job.CompletedUtc }; await store.SaveAsync(attempt, cancellationToken).ConfigureAwait(false); }
            await RecordAsync(FlightDiagnosticEventType.DeliveryCommitted, attempt.DeliveryProof.ReadyUtc, "delivery-committed", cancellationToken,
                ("economyChanged", "false"), ("evidenceId", attempt.DeliveryProof.EvidenceId), ("reconciled", "true")).ConfigureAwait(false);
        }
    }

    private void EvaluateOriginStationary(EconomyJob? active)
    {
        if (active is null || world is null || latest is null) { stationarySince = null; return; }
        var shipment = world.Shipments.Single(x => x.Id == active.ShipmentId);
        if (shipment.State is not (ShipmentState.Reserved or ShipmentState.Loaded)) { stationarySince = null; observationReason = "Accepted shipment is not available for origin loading."; return; }
        if (shipment.LoadedQuantity + shipment.DeliveredQuantity >= shipment.Quantity - .001m) { stationarySince = null; observationReason = "All remaining shipment cargo is already aboard or delivered."; return; }
        var requirement = world.Requirements.Single(x => x.Id == active.RequirementId);
        var origin = Coordinates(requirement.OriginLocationId);
        var originName = PlaceName(requirement.OriginLocationId);
        double distance = DistanceNm(latest, origin);
        if (!latest.IsOnGround) { stationarySince = null; observationReason = $"Aircraft is airborne; loading requires the aircraft on the ground at {originName}."; return; }
        if (distance > options.AirportRadiusNauticalMiles) { stationarySince = null; observationReason = $"Aircraft is {distance:F2} NM from {originName}; loading requires {options.AirportRadiusNauticalMiles:F1} NM or less."; return; }
        if (latest.GroundSpeedKnots > options.MaximumGroundSpeedKnots) { stationarySince = null; observationReason = $"Aircraft ground speed is {latest.GroundSpeedKnots:F1} kt; loading requires {options.MaximumGroundSpeedKnots:F0} kt or less."; return; }
        if (!TryIdentity(latest, out _, out var identityError)) { stationarySince = null; observationReason = identityError; return; }
        stationarySince ??= latest.ObservedUtc;
        var elapsed = latest.ObservedUtc - stationarySince.Value;
        observationReason = elapsed >= options.RequiredStationaryDuration ? $"Aircraft is ready for simulated cargo loading at {originName}." :
            $"Stationary at {originName} for {elapsed.TotalSeconds:F1} of {options.RequiredStationaryDuration.TotalSeconds:F0} seconds.";
    }

    private void UpdateStatus()
    {
        if (world is null) { SetStatus(FlightAttemptStatus.Empty("Economy state is unavailable.")); return; }
        var job = ActiveJob(world); var completed = world.Jobs.Where(x => x.State == EconomyJobState.Completed).OrderByDescending(x => x.CompletedUtc).FirstOrDefault();
        var player = world.Players.SingleOrDefault(x => x.Id == EmployedCargoPrototype.PlayerId);
        decimal balance = player?.CachedBalance ?? 0; string currency = player?.CurrencyCode ?? EmployedCargoPrototype.Currency;
        if (job is null && completed is null) { SetStatus(FlightAttemptStatus.Empty()); return; }
        var currentJob = job ?? completed!; var requirement = world.Requirements.Single(x => x.Id == currentJob.RequirementId);
        double? originDistance = latest is null ? null : DistanceNm(latest, Coordinates(requirement.OriginLocationId));
        double? destinationDistance = latest is null ? null : DistanceNm(latest, Coordinates(requirement.DestinationLocationId));
        if (currentJob.State == EconomyJobState.Completed)
        { SetStatus(new("Delivered and paid", observationReason, connected, false, false, originDistance, destinationDistance, attempt, balance, currency)); return; }
        if (attempt is null)
        {
            bool ready = connected && latest is not null && stationarySince is { } since && latest.ObservedUtc - since >= options.RequiredStationaryDuration &&
                latest.IsOnGround && latest.GroundSpeedKnots <= options.MaximumGroundSpeedKnots && originDistance <= options.AirportRadiusNauticalMiles;
            SetStatus(new(ready ? "Ready to load" : $"Reserved — travel to {PlaceName(requirement.OriginLocationId)}", connected ? observationReason : "SimConnect is disconnected.", connected,
                ready, false, originDistance, destinationDistance, null, balance, currency)); return;
        }
        if (attempt.State == FlightAttemptState.Delivered)
        { SetStatus(new("Delivered and paid", observationReason, connected, false, false, originDistance, destinationDistance, attempt, balance, currency)); return; }
        if (attempt.DeliveryProof is not null)
        { SetStatus(new("Ready to unload", observationReason, connected, false, true, originDistance, destinationDistance, attempt, balance, currency)); return; }
        bool evidence = (!attempt.TransitBeginsAtDeparture || attempt.OriginDepartedUtc is not null) && attempt.FirstAirborneUtc is not null && attempt.MovementObservedUtc is not null && attempt.MaximumDistanceFromOriginNauticalMiles >= attempt.RequiredMaximumDistanceNauticalMiles;
        string stage = attempt.FirstAirborneUtc is null ? "Cargo loaded" : attempt.MovementObservedUtc is null ? "Departed" :
            evidence && destinationDistance <= options.AirportRadiusNauticalMiles ? "Arrived — stop aircraft" : "En route";
        string reason = connected ? observationReason : "SimConnect is disconnected; persisted flight evidence is retained and timers are paused.";
        if (connected && !evidence && attempt.FirstAirborneUtc is not null)
            reason = attempt.MovementObservedUtc is null ? $"Meaningful movement of {options.MeaningfulMovementNauticalMiles:F1} NM after takeoff has not yet been recorded." :
                $"Maximum recorded distance from {PlaceName(requirement.OriginLocationId)} is {attempt.MaximumDistanceFromOriginNauticalMiles:F2} NM; {attempt.RequiredMaximumDistanceNauticalMiles:F2} NM is required.";
        var activeShipment = world.Shipments.Single(x => x.Id == currentJob.ShipmentId);
        var canLoadMore = attempt.FirstAirborneUtc is null && activeShipment.State == ShipmentState.Loaded &&
            activeShipment.LoadedQuantity + activeShipment.DeliveredQuantity < activeShipment.Quantity - .001m &&
            connected && latest is not null && stationarySince is { } loadSince && latest.ObservedUtc - loadSince >= options.RequiredStationaryDuration &&
            latest.IsOnGround && latest.GroundSpeedKnots <= options.MaximumGroundSpeedKnots && originDistance <= options.AirportRadiusNauticalMiles;
        SetStatus(new(stage, reason, connected, canLoadMore, false, originDistance, destinationDistance, attempt, balance, currency));
    }

    private static FlightAttempt? SelectAttempt(EconomyWorld? world, IReadOnlyList<FlightAttempt> attempts)
    {
        if (world is null) return null;
        var current = world.Jobs.Where(x => x.State == EconomyJobState.Accepted).SingleOrDefault() ??
            world.Jobs.Where(x => x.State == EconomyJobState.Completed).OrderByDescending(x => x.CompletedUtc).FirstOrDefault();
        if (current is null) return null;
        var byJob = attempts.SingleOrDefault(x => x.JobId == current.Id);
        var byShipment = attempts.SingleOrDefault(x => x.ShipmentId == current.ShipmentId);
        if ((byJob is not null && byShipment is not null && byJob.Id != byShipment.Id) || (byJob is null && byShipment is not null))
            throw new IOException("Flight-attempt job and shipment identity are inconsistent. File preserved.");
        return byJob;
    }
    private static EconomyJob? ActiveJob(EconomyWorld world) => world.Jobs.SingleOrDefault(x => x.State == EconomyJobState.Accepted);
    private static bool TryIdentity(AircraftTelemetry sample, out FlightAircraftIdentity? identity, out string error)
    {
        try { identity = FlightAircraftIdentity.From(sample); error = ""; return true; }
        catch (InvalidDataException exception) { identity = null; error = "Aircraft identity is not stable enough for flight evidence: " + exception.Message; return false; }
    }
    private static double DistanceNm(AircraftTelemetry sample, ApproximateCoordinates target) =>
        WorldTracking.DistanceMeters(sample.Latitude, sample.Longitude, target.Latitude, target.Longitude) / MetersPerNauticalMile;
    private void Pause(string reason) { connected = false; latest = null; stationarySince = null; lastSampleUtc = null; observationReason = reason; }
    private double CurrentDistanceToActiveOrigin()
    {
        if (world is null || latest is null || ActiveJob(world) is not { } job) return double.NaN;
        var requirement = world.Requirements.Single(x => x.Id == job.RequirementId);
        return DistanceNm(latest, Coordinates(requirement.OriginLocationId));
    }

    private async Task InterruptQualificationAsync(string reason, DateTimeOffset occurredUtc, CancellationToken cancellationToken)
    {
        if (IsTerminalAttempt()) { stationarySince = null; return; }
        if (stationarySince is not { } started) return;
        var type = attempt is null ? FlightDiagnosticEventType.OriginQualificationInterrupted :
            attempt.FirstAirborneUtc is not null && attempt.MovementObservedUtc is not null &&
            attempt.MaximumDistanceFromOriginNauticalMiles >= attempt.RequiredMaximumDistanceNauticalMiles
                ? FlightDiagnosticEventType.ArrivalQualificationInterrupted : (FlightDiagnosticEventType?)null;
        stationarySince = null;
        if (type is { } eventType)
            await RecordAsync(eventType, occurredUtc, "qualification-stop-" + started.UtcTicks, cancellationToken,
                ("reason", reason), ("startedUtc", started.ToString("O"))).ConfigureAwait(false);
    }

    private async Task RecordAsync(FlightDiagnosticEventType type, DateTimeOffset occurredUtc, string stableKey,
        CancellationToken cancellationToken, params (string Key, string Value)[] details)
    {
        if (world is null) return;
        if (IsTerminalAttempt() && type is FlightDiagnosticEventType.OriginQualificationStarted or
            FlightDiagnosticEventType.OriginQualificationInterrupted or FlightDiagnosticEventType.ReadyToLoad or
            FlightDiagnosticEventType.CargoLoaded or FlightDiagnosticEventType.ShipmentEnteredInTransit or
            FlightDiagnosticEventType.AirborneEvidenceRecorded or FlightDiagnosticEventType.PostDepartureMovementRecorded or
            FlightDiagnosticEventType.RequiredMaximumDistanceReached or FlightDiagnosticEventType.ArrivalQualificationStarted or
            FlightDiagnosticEventType.ArrivalQualificationInterrupted or FlightDiagnosticEventType.ReadyToUnload or
            FlightDiagnosticEventType.AircraftIdentityMismatchObserved)
            return;
        var job = ActiveJob(world) ?? (attempt is null ? null : world.Jobs.SingleOrDefault(x => x.Id == attempt.JobId)) ??
            world.Jobs.Where(x => x.State == EconomyJobState.Completed).OrderByDescending(x => x.CompletedUtc).FirstOrDefault();
        if (job is null) return;
        var shipmentId = attempt?.ShipmentId ?? job.ShipmentId;
        var source = string.Join("|", job.Id, shipmentId, type, stableKey);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        var id = "flight-event-" + hash[..40];
        if (timeline.Any(x => x.Id == id)) return;
        var facts = details.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var diagnosticEvent = new FlightDiagnosticEvent(id, job.Id, shipmentId, occurredUtc, type, facts).EnsureValid();
        try
        {
            await store.AppendDiagnosticEventAsync(diagnosticEvent, cancellationToken).ConfigureAwait(false);
            timeline = timeline.Append(diagnosticEvent).OrderBy(x => x.OccurredUtc).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
            diagnosticWarning = "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            diagnosticWarning = "Diagnostic timeline write failed: " + error.Message;
        }
    }

    private FlightAttemptDiagnosticSnapshot BuildDiagnostic(FlightAttemptStatus status)
    {
        EconomyJob? job = world is null ? null : ActiveJob(world) ??
            world.Jobs.Where(x => x.State == EconomyJobState.Completed).OrderByDescending(x => x.CompletedUtc).FirstOrDefault();
        TransportRequirement? requirement = job is null ? null : world!.Requirements.Single(x => x.Id == job.RequirementId);
        string Place(string? id)
        {
            if (id is null) return "—";
            return PlaceName(id);
        }
        string observedIdentity = "—";
        if (latest is not null) observedIdentity = string.IsNullOrWhiteSpace(latest.AircraftType)
            ? latest.AircraftTitle : latest.AircraftTitle + " / " + latest.AircraftType;
        string recordedIdentity = attempt is null ? "Not recorded until cargo loading" : attempt.Aircraft.Title + " / " + attempt.Aircraft.Type;
        var elapsed = stationarySince is { } since && latest is not null ? latest.ObservedUtc - since : (TimeSpan?)null;
        string qualification = elapsed is null ? "Not currently qualifying" : $"{elapsed.Value.TotalSeconds:F1} seconds";
        string unavailableReason = status.Reason;
        if (status.CanLoadCargo) unavailableReason = "Load cargo is available.";
        else if (status.CanUnloadCargo) unavailableReason = "Unload cargo is available.";
        else if (attempt?.State == FlightAttemptState.Delivered) unavailableReason = "Delivery is already committed.";
        else if (attempt is not null && attempt.FirstAirborneUtc is null) unavailableReason = "Unloading is unavailable because airborne evidence has not been recorded.";
        else if (attempt is not null && attempt.MovementObservedUtc is null) unavailableReason = "Unloading is unavailable because post-departure movement evidence has not been recorded.";
        else if (attempt is not null && attempt.MaximumDistanceFromOriginNauticalMiles < attempt.RequiredMaximumDistanceNauticalMiles)
            unavailableReason = $"Unloading is unavailable because maximum origin distance is {attempt.MaximumDistanceFromOriginNauticalMiles:F2} NM; {attempt.RequiredMaximumDistanceNauticalMiles:F2} NM is required.";
        var events = job is null ? [] : timeline.Where(x => x.JobId == job.Id && x.ShipmentId == job.ShipmentId)
            .OrderByDescending(x => x.OccurredUtc).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var freshness = connected && latest is not null ? "Fresh validated live sample" : "No valid live sample";
        if (diagnosticWarning.Length > 0) freshness += " · " + diagnosticWarning;
        return new(connected ? "Connected" : "Disconnected", freshness,
            lastValidSampleUtc, status.Stage, Place(requirement?.OriginLocationId), Place(requirement?.DestinationLocationId), job?.Id ?? "—",
            job?.ShipmentId ?? "—", recordedIdentity, observedIdentity, latest is null ? "—" : latest.IsOnGround.ToString(),
            latest is null ? "—" : $"{latest.GroundSpeedKnots:F1} kt", status.OriginDistanceNauticalMiles is { } origin ? $"{origin:F2} NM" : "—",
            status.DestinationDistanceNauticalMiles is { } destination ? $"{destination:F2} NM" : "—",
            $"{attempt?.MaximumDistanceFromOriginNauticalMiles ?? 0:F2} NM / required {attempt?.RequiredMaximumDistanceNauticalMiles ?? 10:F2} NM", attempt?.FirstAirborneUtc is not null,
            attempt?.MovementObservedUtc is not null, qualification, $"{options.RequiredStationaryDuration.TotalSeconds:F0} seconds",
            unavailableReason, events);
    }

    private bool IsTerminalAttempt()
    {
        if (attempt?.State == FlightAttemptState.Delivered) return true;
        return world is not null && attempt is not null && world.Jobs.Any(x => x.Id == attempt.JobId && x.State == EconomyJobState.Completed);
    }

    private ApproximateCoordinates Coordinates(string locationId)
    {
        var authored = catalogue.Locations.SingleOrDefault(x => x.StableId == locationId);
        if (authored?.ApproximateCoordinates is { } coordinates) return coordinates;
        var site = world?.Sites.SingleOrDefault(x => x.LocationId == locationId) ??
            throw new InvalidDataException("Flight route location has no trustworthy configured coordinates.");
        return new(site.Latitude, site.Longitude);
    }

    private string PlaceName(string locationId)
    {
        if (locationId == EmployedCargoPrototype.ErosId) return "Eros (FYWE)";
        var authored = catalogue.Locations.SingleOrDefault(x => x.StableId == locationId);
        if (authored is not null) return authored.Name + (authored.AirportIdentifier.Value is { } code ? " (" + code + ")" : "");
        var site = world?.Sites.SingleOrDefault(x => x.LocationId == locationId) ??
            throw new InvalidDataException("Flight route location is unknown.");
        return site.Name + " (" + site.AirportIdentifier + ")";
    }

    private void SetStatus(FlightAttemptStatus status)
    {
        status = status with { Diagnostic = BuildDiagnostic(status) };
        Current = status; Changed?.Invoke(status);
    }
}
