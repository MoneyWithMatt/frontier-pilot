using System.Text.Json;
using System.Text.Json.Serialization;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed record FlightAttemptTrackerOptions(double AirportRadiusNauticalMiles = 1.5,
    double MaximumGroundSpeedKnots = 5, TimeSpan? StationaryDuration = null,
    double MeaningfulMovementNauticalMiles = 2, double RequiredMaximumDistanceNauticalMiles = 10,
    TimeSpan? MaximumContinuousSampleGap = null)
{
    public TimeSpan RequiredStationaryDuration => StationaryDuration ?? TimeSpan.FromSeconds(10);
    public TimeSpan MaximumSampleGap => MaximumContinuousSampleGap ?? TimeSpan.FromSeconds(2);
    public FlightAttemptTrackerOptions EnsureValid()
    {
        if (!double.IsFinite(AirportRadiusNauticalMiles) || AirportRadiusNauticalMiles <= 0 ||
            !double.IsFinite(MaximumGroundSpeedKnots) || MaximumGroundSpeedKnots < 0 ||
            !double.IsFinite(MeaningfulMovementNauticalMiles) || MeaningfulMovementNauticalMiles <= 0 ||
            !double.IsFinite(RequiredMaximumDistanceNauticalMiles) || RequiredMaximumDistanceNauticalMiles < MeaningfulMovementNauticalMiles ||
            RequiredStationaryDuration <= TimeSpan.Zero || MaximumSampleGap <= TimeSpan.Zero)
            throw new InvalidDataException("Flight-attempt tracker options are invalid.");
        return this;
    }
}

public sealed record FlightAttemptStatus(string Stage, string Reason, bool IsConnected, bool CanLoadCargo,
    bool CanUnloadCargo, double? OriginDistanceNauticalMiles, double? DestinationDistanceNauticalMiles,
    FlightAttempt? Attempt, decimal PlayerBalance, string CurrencyCode)
{
    public static FlightAttemptStatus Empty(string reason = "Accept an employed cargo job to begin.") =>
        new("No active cargo job", reason, false, false, false, null, null, null, 0, EmployedCargoPrototype.Currency);
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
    Task UnloadCargoAsync(CancellationToken cancellationToken = default);
    Task<string> ExportDeveloperDiagnosticAsync(CancellationToken cancellationToken = default);
    Task ResetIncompletePrototypeProgressAsync(CancellationToken cancellationToken = default);
}

public sealed class FlightAttemptTracker : IFlightAttemptTracker
{
    private const double MetersPerNauticalMile = 1852;
    private readonly EconomyService economy;
    private readonly IFlightAttemptStore store;
    private readonly NamibiaLocationCatalogueDocument catalogue;
    private readonly FlightAttemptTrackerOptions options;
    private readonly SemaphoreSlim gate = new(1, 1);
    private EconomyWorld? world;
    private FlightAttempt? attempt;
    private AircraftTelemetry? latest;
    private DateTimeOffset? stationarySince, lastSampleUtc;
    private bool connected;
    private string observationReason = "SimConnect is disconnected.";
    public string StoragePath => store.StoragePath;
    public FlightAttemptStatus Current { get; private set; } = FlightAttemptStatus.Empty();
    public event Action<FlightAttemptStatus>? Changed;

    public FlightAttemptTracker(IEconomyStateStore economyStore, IFlightAttemptStore attemptStore,
        NamibiaLocationCatalogueDocument? catalogue = null, FlightAttemptTrackerOptions? options = null)
    {
        economy = new(economyStore); store = attemptStore; this.catalogue = (catalogue ?? NamibiaLocationCatalogue.Load()).EnsureValid();
        this.options = (options ?? new()).EnsureValid();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default) => await RefreshEconomyAsync(cancellationToken);

    public async Task RefreshEconomyAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            world = await economy.LoadAsync(cancellationToken).ConfigureAwait(false);
            var attempts = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
            attempt = SelectAttempt(world, attempts);
            await ReconcileAsync(cancellationToken).ConfigureAwait(false); UpdateStatus();
        }
        finally { gate.Release(); }
    }

    public async Task ObserveAsync(AircraftTelemetry? telemetry, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (telemetry is null) { Pause("SimConnect is disconnected."); UpdateStatus(); return; }
            AircraftTelemetry sample;
            try { sample = telemetry.EnsureValid(); }
            catch (Exception error) when (error is InvalidDataException or ArgumentException)
            { Pause("Aircraft telemetry is invalid: " + error.Message); UpdateStatus(); return; }

            bool continuous = lastSampleUtc is { } previous && sample.ObservedUtc > previous && sample.ObservedUtc - previous <= options.MaximumSampleGap;
            if (!continuous) stationarySince = null;
            if (lastSampleUtc is { } prior && sample.ObservedUtc <= prior)
            { Pause("Aircraft telemetry time did not advance."); UpdateStatus(); return; }
            connected = true; latest = sample; lastSampleUtc = sample.ObservedUtc; observationReason = "Live aircraft telemetry received.";
            if (world is null) { UpdateStatus(); return; }
            var active = ActiveJob(world);
            if (active is null || attempt is null) { EvaluateOriginStationary(active); UpdateStatus(); return; }

            if (!TryIdentity(sample, out var identity, out var identityError))
            { stationarySince = null; observationReason = identityError; UpdateStatus(); return; }
            if (attempt.Aircraft.ClearlyDiffersFrom(identity!))
            { stationarySince = null; observationReason = $"Aircraft identity differs from the loaded aircraft ({attempt.Aircraft.Title} / {attempt.Aircraft.Type})."; UpdateStatus(); return; }
            if (attempt.DeliveryProof is not null)
            { observationReason = "Verified arrival is persisted. Unload cargo when ready."; UpdateStatus(); return; }

            var origin = catalogue.Get(attempt.OriginLocationId).ApproximateCoordinates!;
            double originDistance = DistanceNm(sample, origin); var updated = attempt;
            bool persist = false;
            if (!sample.IsOnGround && updated.FirstAirborneUtc is null)
            { updated = updated with { FirstAirborneUtc = sample.ObservedUtc, State = FlightAttemptState.Departed }; persist = true; }
            if (updated.FirstAirborneUtc is { } airborne && sample.ObservedUtc > airborne &&
                originDistance >= options.MeaningfulMovementNauticalMiles && updated.MovementObservedUtc is null)
            { updated = updated with { MovementObservedUtc = sample.ObservedUtc, State = FlightAttemptState.EnRoute }; persist = true; }
            if (originDistance > updated.MaximumDistanceFromOriginNauticalMiles)
            {
                bool crossedRequirement = updated.MaximumDistanceFromOriginNauticalMiles < options.RequiredMaximumDistanceNauticalMiles && originDistance >= options.RequiredMaximumDistanceNauticalMiles;
                bool checkpoint = originDistance - updated.MaximumDistanceFromOriginNauticalMiles >= 0.25;
                updated = updated with { MaximumDistanceFromOriginNauticalMiles = originDistance, MaximumDistanceObservedUtc = sample.ObservedUtc };
                persist |= crossedRequirement || checkpoint;
            }
            attempt = updated;

            bool evidenceComplete = attempt.FirstAirborneUtc is not null && attempt.MovementObservedUtc is not null &&
                attempt.MaximumDistanceFromOriginNauticalMiles >= options.RequiredMaximumDistanceNauticalMiles;
            var destination = catalogue.Get(attempt.DestinationLocationId).ApproximateCoordinates!;
            double destinationDistance = DistanceNm(sample, destination);
            if (attempt.DeliveryProof is null && evidenceComplete && destinationDistance > options.AirportRadiusNauticalMiles)
            { stationarySince = null; observationReason = $"Aircraft is {destinationDistance:F2} NM from the destination; arrival requires {options.AirportRadiusNauticalMiles:F1} NM or less."; }
            else if (attempt.DeliveryProof is null && evidenceComplete && !sample.IsOnGround)
            { stationarySince = null; observationReason = "Aircraft is airborne; unloading requires a stopped on-ground arrival."; }
            else if (attempt.DeliveryProof is null && evidenceComplete && sample.GroundSpeedKnots > options.MaximumGroundSpeedKnots)
            { stationarySince = null; observationReason = $"Aircraft ground speed is {sample.GroundSpeedKnots:F1} kt; arrival requires {options.MaximumGroundSpeedKnots:F0} kt or less."; }
            else if (attempt.DeliveryProof is null && evidenceComplete)
            {
                if (stationarySince is null) stationarySince = sample.ObservedUtc;
                if (sample.ObservedUtc - stationarySince >= options.RequiredStationaryDuration)
                {
                    var proof = new FlightDeliveryProof("delivery-" + attempt.ShipmentId, sample.ObservedUtc, FlightPosition.From(sample),
                        attempt.FirstAirborneUtc!.Value, attempt.MovementObservedUtc!.Value, attempt.MaximumDistanceFromOriginNauticalMiles).EnsureValid();
                    attempt = attempt with { State = FlightAttemptState.ReadyToUnload, DeliveryProof = proof }; persist = true;
                    observationReason = "Verified arrival is persisted. Unload cargo when ready.";
                }
                else observationReason = $"Stopped near destination for {(sample.ObservedUtc - stationarySince.Value).TotalSeconds:F1} of {options.RequiredStationaryDuration.TotalSeconds:F0} seconds.";
            }
            else if (attempt.DeliveryProof is null) stationarySince = null;
            if (persist) await store.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);
            UpdateStatus();
        }
        finally { gate.Release(); }
    }

    public async Task LoadCargoAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (world is not null && attempt is not null)
            {
                var existingJob = ActiveJob(world);
                var existingShipment = existingJob is null ? null : world.Shipments.Single(x => x.Id == existingJob.ShipmentId);
                if (existingJob?.Id == attempt.JobId && existingShipment?.State == ShipmentState.InTransit) { UpdateStatus(); return; }
            }
            UpdateStatus(); if (!Current.CanLoadCargo || latest is null || world is null) throw new InvalidOperationException(Current.Reason);
            var job = ActiveJob(world) ?? throw new InvalidOperationException("No accepted shipment is available for loading.");
            var requirement = world.Requirements.Single(x => x.Id == job.RequirementId);
            var shipment = world.Shipments.Single(x => x.Id == job.ShipmentId);
            if (shipment.State == ShipmentState.InTransit && attempt is not null) { UpdateStatus(); return; }
            if (shipment.State != ShipmentState.Reserved) throw new InvalidOperationException("The accepted shipment is not reserved for loading.");
            var identity = FlightAircraftIdentity.From(latest);
            attempt ??= new FlightAttempt("flight-" + shipment.Id, job.Id, shipment.Id, requirement.OriginLocationId,
                requirement.DestinationLocationId, FlightAttemptState.CargoLoaded, latest.ObservedUtc, identity, FlightPosition.From(latest)).EnsureValid();
            await store.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);
            var transition = await economy.MarkShipmentInTransitAsync(job.Id, cancellationToken).ConfigureAwait(false);
            world = transition.World; stationarySince = null; observationReason = "Cargo loaded in Frontier Pilot; MSFS payload stations are unchanged."; UpdateStatus();
        }
        finally { gate.Release(); }
    }

    public async Task UnloadCargoAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (attempt?.DeliveryProof is null || world is null) throw new InvalidOperationException("Verified arrival evidence is not ready.");
            var proof = attempt.DeliveryProof;
            var evidence = new VerifiedDeliveryEvidence(proof.EvidenceId, "frontier-flight-attempt-v0", attempt.JobId, attempt.ShipmentId,
                attempt.OriginLocationId, attempt.DestinationLocationId, proof.ReadyUtc,
                $"Persisted flight proof: airborne {proof.FirstAirborneUtc:O}; movement {proof.MovementObservedUtc:O}; maximum origin distance {proof.MaximumDistanceFromOriginNauticalMiles:F2} NM.").EnsureValid();
            var transition = await economy.CompleteDeliveryAsync(evidence, cancellationToken).ConfigureAwait(false); world = transition.World;
            if (attempt.State != FlightAttemptState.Delivered)
            { attempt = attempt with { State = FlightAttemptState.Delivered, DeliveredUtc = proof.ReadyUtc }; await store.SaveAsync(attempt, cancellationToken).ConfigureAwait(false); }
            observationReason = transition.Changed ? "Cargo delivered, customer inventory updated and payment recorded." : "Delivery was already recorded; no cargo or payment was duplicated.";
            UpdateStatus();
        }
        finally { gate.Release(); }
    }

    public async Task<string> ExportDeveloperDiagnosticAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
            return JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                ExportedUtc = DateTimeOffset.UtcNow,
                Notice = "Read-only Frontier Pilot prototype diagnostic. Coordinates and economy records are included.",
                Status = Current,
                Attempt = attempt,
                Economy = world
            }, options);
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
            if (job?.State != EconomyJobState.Accepted || shipment?.State != ShipmentState.InTransit ||
                attempt.State == FlightAttemptState.Delivered || attempt.DeliveredUtc is not null)
                throw new InvalidOperationException("Only observation progress for an incomplete, loaded shipment can be reset. Completed economy state is protected.");
            attempt = await store.ResetIncompleteProgressAsync(attempt.Id, cancellationToken).ConfigureAwait(false);
            stationarySince = null; lastSampleUtc = null; latest = null; connected = false;
            observationReason = "Developer reset cleared incomplete flight observations. Shipment remains loaded and InTransit; no inventory or money changed.";
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
            if (activeWithoutAttempt is not null && world.Shipments.Single(x => x.Id == activeWithoutAttempt.ShipmentId).State == ShipmentState.InTransit)
                throw new IOException("An in-transit shipment has no persisted flight attempt. Files preserved.");
            return;
        }
        var job = world.Jobs.SingleOrDefault(x => x.Id == attempt.JobId) ?? throw new IOException("Flight attempt refers to an unknown job. File preserved.");
        if (job.ShipmentId != attempt.ShipmentId) throw new IOException("Flight attempt refers to the wrong shipment. File preserved.");
        var requirement = world.Requirements.Single(x => x.Id == job.RequirementId);
        if (requirement.OriginLocationId != attempt.OriginLocationId || requirement.DestinationLocationId != attempt.DestinationLocationId)
            throw new IOException("Flight attempt route does not match its job. File preserved.");
        var shipment = world.Shipments.Single(x => x.Id == attempt.ShipmentId);
        if (job.State == EconomyJobState.Accepted && shipment.State == ShipmentState.Reserved)
            world = (await economy.MarkShipmentInTransitAsync(job.Id, cancellationToken).ConfigureAwait(false)).World;
        else if (job.State == EconomyJobState.Accepted && shipment.State != ShipmentState.InTransit)
            throw new IOException("Flight attempt shipment state is inconsistent. Files preserved.");
        if (job.State == EconomyJobState.Completed && attempt.DeliveryProof is not null && attempt.State != FlightAttemptState.Delivered)
        { attempt = attempt with { State = FlightAttemptState.Delivered, DeliveredUtc = job.CompletedUtc }; await store.SaveAsync(attempt, cancellationToken).ConfigureAwait(false); }
    }

    private void EvaluateOriginStationary(EconomyJob? active)
    {
        if (active is null || world is null || latest is null) { stationarySince = null; return; }
        var shipment = world.Shipments.Single(x => x.Id == active.ShipmentId);
        if (shipment.State != ShipmentState.Reserved) { stationarySince = null; observationReason = "Accepted shipment is not in its reserved state."; return; }
        var requirement = world.Requirements.Single(x => x.Id == active.RequirementId);
        var origin = catalogue.Get(requirement.OriginLocationId).ApproximateCoordinates!;
        double distance = DistanceNm(latest, origin);
        if (!latest.IsOnGround) { stationarySince = null; observationReason = "Aircraft is airborne; loading requires the aircraft on the ground at Eros."; return; }
        if (distance > options.AirportRadiusNauticalMiles) { stationarySince = null; observationReason = $"Aircraft is {distance:F2} NM from Eros; loading requires {options.AirportRadiusNauticalMiles:F1} NM or less."; return; }
        if (latest.GroundSpeedKnots > options.MaximumGroundSpeedKnots) { stationarySince = null; observationReason = $"Aircraft ground speed is {latest.GroundSpeedKnots:F1} kt; loading requires {options.MaximumGroundSpeedKnots:F0} kt or less."; return; }
        if (!TryIdentity(latest, out _, out var identityError)) { stationarySince = null; observationReason = identityError; return; }
        stationarySince ??= latest.ObservedUtc;
        var elapsed = latest.ObservedUtc - stationarySince.Value;
        observationReason = elapsed >= options.RequiredStationaryDuration ? "Aircraft is ready for simulated cargo loading at Eros." :
            $"Stationary at Eros for {elapsed.TotalSeconds:F1} of {options.RequiredStationaryDuration.TotalSeconds:F0} seconds.";
    }

    private void UpdateStatus()
    {
        if (world is null) { SetStatus(FlightAttemptStatus.Empty("Economy state is unavailable.")); return; }
        var job = ActiveJob(world); var completed = world.Jobs.Where(x => x.State == EconomyJobState.Completed).OrderByDescending(x => x.CompletedUtc).FirstOrDefault();
        var player = world.Players.SingleOrDefault(x => x.Id == EmployedCargoPrototype.PlayerId);
        decimal balance = player?.CachedBalance ?? 0; string currency = player?.CurrencyCode ?? EmployedCargoPrototype.Currency;
        if (job is null && completed is null) { SetStatus(FlightAttemptStatus.Empty()); return; }
        var currentJob = job ?? completed!; var requirement = world.Requirements.Single(x => x.Id == currentJob.RequirementId);
        double? originDistance = latest is null ? null : DistanceNm(latest, catalogue.Get(requirement.OriginLocationId).ApproximateCoordinates!);
        double? destinationDistance = latest is null ? null : DistanceNm(latest, catalogue.Get(requirement.DestinationLocationId).ApproximateCoordinates!);
        if (currentJob.State == EconomyJobState.Completed)
        { SetStatus(new("Delivered and paid", observationReason, connected, false, false, originDistance, destinationDistance, attempt, balance, currency)); return; }
        if (attempt is null)
        {
            bool ready = connected && latest is not null && stationarySince is { } since && latest.ObservedUtc - since >= options.RequiredStationaryDuration &&
                latest.IsOnGround && latest.GroundSpeedKnots <= options.MaximumGroundSpeedKnots && originDistance <= options.AirportRadiusNauticalMiles;
            SetStatus(new(ready ? "Ready to load" : "Reserved — travel to Eros", connected ? observationReason : "SimConnect is disconnected.", connected,
                ready, false, originDistance, destinationDistance, null, balance, currency)); return;
        }
        if (attempt.State == FlightAttemptState.Delivered)
        { SetStatus(new("Delivered and paid", observationReason, connected, false, false, originDistance, destinationDistance, attempt, balance, currency)); return; }
        if (attempt.DeliveryProof is not null)
        { SetStatus(new("Ready to unload", observationReason, connected, false, true, originDistance, destinationDistance, attempt, balance, currency)); return; }
        bool evidence = attempt.FirstAirborneUtc is not null && attempt.MovementObservedUtc is not null && attempt.MaximumDistanceFromOriginNauticalMiles >= options.RequiredMaximumDistanceNauticalMiles;
        string stage = attempt.FirstAirborneUtc is null ? "Cargo loaded" : attempt.MovementObservedUtc is null ? "Departed" :
            evidence && destinationDistance <= options.AirportRadiusNauticalMiles ? "Arrived — stop aircraft" : "En route";
        string reason = connected ? observationReason : "SimConnect is disconnected; persisted flight evidence is retained and timers are paused.";
        if (connected && !evidence && attempt.FirstAirborneUtc is not null)
            reason = attempt.MovementObservedUtc is null ? $"Meaningful movement of {options.MeaningfulMovementNauticalMiles:F1} NM after takeoff has not yet been recorded." :
                $"Maximum recorded distance from Eros is {attempt.MaximumDistanceFromOriginNauticalMiles:F2} NM; {options.RequiredMaximumDistanceNauticalMiles:F0} NM is required.";
        SetStatus(new(stage, reason, connected, false, false, originDistance, destinationDistance, attempt, balance, currency));
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
    private void SetStatus(FlightAttemptStatus status) { Current = status; Changed?.Invoke(status); }
}
