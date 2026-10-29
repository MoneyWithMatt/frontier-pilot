using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed partial class FlightAttemptTracker
{
    // Both careers use precisely the same contiguous airborne-time calculation.
    private static FlightAttempt AccumulateAirborneTime(FlightAttempt flight, AircraftTelemetry sample,
        AircraftTelemetry? previous, bool continuous, FlightAttemptTrackerOptions policy)
    {
        if (sample.IsOnGround) return flight;
        flight = flight with { WageFlightId = flight.WageFlightId ?? flight.Id };
        if (continuous && previous is { IsOnGround: false } && sample.ObservedUtc > previous.ObservedUtc &&
            sample.ObservedUtc - previous.ObservedUtc <= policy.MaximumSampleGap &&
            (flight.WageObservedUtc is null || previous.ObservedUtc >= flight.WageObservedUtc))
            flight = flight with { VerifiedAirborneTicks = checked(flight.VerifiedAirborneTicks + (sample.ObservedUtc - previous.ObservedUtc).Ticks), WageObservedUtc = sample.ObservedUtc };
        return flight;
    }

    private static FlightDeliveryProof ArrivalProof(FlightAttempt flight, AircraftTelemetry sample) =>
        new FlightDeliveryProof(FlightAttemptIds.DeliveryEvidence(flight.ShipmentId), sample.ObservedUtc, FlightPosition.From(sample),
            flight.FirstAirborneUtc!.Value, flight.MovementObservedUtc!.Value, flight.MaximumDistanceFromOriginNauticalMiles).EnsureValid();

    /// <summary>Passenger observations retain the existing flight-attempt proof/identity/distance rules.
    /// Only this tracker component creates passenger flight proof; UI and diagnostics cannot do so.</summary>
    internal sealed class PassengerMonitor
    {
        private readonly FlightAttemptTrackerOptions policy = new FlightAttemptTrackerOptions().EnsureValid();
        private AircraftTelemetry? latest, paidSample, anchor;
        private DateTimeOffset? stationarySince;
        private string? transferId;
        private bool valid;
        public string Reason { get; private set; } = "Connect tracking to qualify boarding.";

        public void Reset()
        {
            anchor = latest ?? anchor; latest = null; paidSample = null; stationarySince = null; valid = false;
        }

        private static double Distance(AircraftTelemetry sample, PassengerEndpoint endpoint) =>
            WorldTracking.DistanceMeters(sample.Latitude, sample.Longitude, endpoint.Latitude, endpoint.Longitude) / 1852;
        private bool Stopped(AircraftTelemetry sample, PassengerEndpoint endpoint) =>
            endpoint.CanGenerate && sample.IsOnGround && sample.GroundSpeedKnots <= policy.MaximumGroundSpeedKnots && Distance(sample, endpoint) <= policy.AirportRadiusNauticalMiles;

        public bool Ready(PassengerTransfer transfer, DateTimeOffset utc, bool boarding)
        {
            return transferId == transfer.Id && valid && latest is not null && utc >= latest.ObservedUtc && utc - latest.ObservedUtc <= policy.MaximumSampleGap &&
                Stopped(latest, boarding ? transfer.Origin : transfer.Destination) &&
                (transfer.Flight is null || !transfer.Flight.Aircraft.ClearlyDiffersFrom(FlightAircraftIdentity.From(latest))) &&
                (boarding ? stationarySince is {} since && latest.ObservedUtc - since >= policy.RequiredStationaryDuration : transfer.Flight?.DeliveryProof is not null);
        }

        public FlightAttempt Board(PassengerTransfer transfer, DateTimeOffset utc)
        {
            if (!Ready(transfer, utc, true) || latest is null) throw new InvalidOperationException("Boarding needs ten continuous stopped seconds at the origin with fresh tracking.");
            var routeNm = WorldTracking.DistanceMeters(transfer.Origin.Latitude, transfer.Origin.Longitude, transfer.Destination.Latitude, transfer.Destination.Longitude) / 1852;
            return new FlightAttempt(FlightAttemptIds.Attempt(transfer.Id), transfer.Id, transfer.Id, transfer.Origin.Id, transfer.Destination.Id,
                FlightAttemptState.CargoLoaded, latest.ObservedUtc, FlightAircraftIdentity.From(latest), FlightPosition.From(latest),
                RequiredMaximumDistanceNauticalMiles: RequiredDistanceForRoute(routeNm), TransitBeginsAtDeparture: true).EnsureValid();
        }

        public FlightAttempt? Observe(PassengerTransfer transfer, AircraftTelemetry? telemetry)
        {
            if (transferId != transfer.Id) { Reset(); anchor = null; transferId = transfer.Id; }
            if (transfer.State is not (PassengerTransferState.Reserved or PassengerTransferState.Boarded)) { Reset(); return transfer.Flight; }
            if (telemetry is null) { Reset(); Reason = "Tracking disconnected; passengers and proven flight progress are retained."; return transfer.Flight; }
            AircraftTelemetry sample;
            try { sample = telemetry.EnsureValid(); _ = FlightAircraftIdentity.From(sample); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
            { Reset(); Reason = "Invalid aircraft telemetry; qualification interrupted."; return transfer.Flight; }
            if (latest is {} prior && sample.ObservedUtc <= prior.ObservedUtc)
            { Reset(); Reason = "Telemetry time did not advance; qualification interrupted."; return transfer.Flight; }
            var continuous = latest is {} previous && sample.ObservedUtc - previous.ObservedUtc <= policy.MaximumSampleGap;
            if (!continuous) { stationarySince = null; paidSample = null; }
            var flight = transfer.Flight;
            if (flight is not null && (flight.Aircraft.ClearlyDiffersFrom(FlightAircraftIdentity.From(sample)) ||
                (latest ?? anchor) is {} last && WorldTracking.DistanceMeters(last.Latitude, last.Longitude, sample.Latitude, sample.Longitude) / 1852 > policy.MaximumContinuousDisplacementNauticalMiles))
            { Reset(); Reason = "Loaded aircraft identity or position changed discontinuously; resume with the recorded aircraft."; return flight; }
            var previousPaid = paidSample; latest = sample; anchor = null; valid = true;
            if (flight is null)
            {
                if (Stopped(sample, transfer.Origin)) stationarySince ??= sample.ObservedUtc; else stationarySince = null;
                Reason = stationarySince is {} since ? $"Stopped at origin for {(sample.ObservedUtc - since).TotalSeconds:0.#} / 10 seconds. Confirm the manifest and verified payload configuration before boarding." : "Stop on the ground within 1.5 NM of the transfer origin.";
                return null;
            }
            if (flight.DeliveryProof is not null) { paidSample = null; Reason = "Arrival proof retained; stop at the destination and explicitly disembark."; return flight; }
            flight = AccumulateAirborneTime(flight, sample, previousPaid, continuous, policy);
            paidSample = sample.IsOnGround ? null : sample;
            if (!sample.IsOnGround && flight.FirstAirborneUtc is null) flight = flight with { FirstAirborneUtc = sample.ObservedUtc, State = FlightAttemptState.Departed };
            var originDistance = Distance(sample, transfer.Origin);
            if (flight.FirstAirborneUtc is {} airborne && sample.ObservedUtc > airborne && originDistance >= policy.MeaningfulMovementNauticalMiles && flight.MovementObservedUtc is null)
                flight = flight with { MovementObservedUtc = sample.ObservedUtc, State = FlightAttemptState.EnRoute };
            if (originDistance > flight.MaximumDistanceFromOriginNauticalMiles)
                flight = flight with { MaximumDistanceFromOriginNauticalMiles = originDistance, MaximumDistanceObservedUtc = sample.ObservedUtc };
            if (flight.FirstAirborneUtc is not null && !sample.IsOnGround && originDistance > policy.AirportRadiusNauticalMiles && flight.OriginDepartedUtc is null)
                flight = flight with { OriginDepartedUtc = sample.ObservedUtc };
            var complete = flight.OriginDepartedUtc is not null && flight.FirstAirborneUtc is not null && flight.MovementObservedUtc is not null && flight.MaximumDistanceFromOriginNauticalMiles >= flight.RequiredMaximumDistanceNauticalMiles;
            if (complete && Stopped(sample, transfer.Destination)) stationarySince ??= sample.ObservedUtc; else stationarySince = null;
            if (stationarySince is {} arrival && sample.ObservedUtc - arrival >= policy.RequiredStationaryDuration)
                flight = flight with { State = FlightAttemptState.ReadyToUnload, DeliveryProof = ArrivalProof(flight, sample) };
            Reason = flight.DeliveryProof is not null ? "Qualified arrival persisted. Disembark to complete this transfer and receive hourly wages." :
                stationarySince is {} started ? $"Stopped at destination for {(sample.ObservedUtc - started).TotalSeconds:0.#} / 10 seconds." : "Passengers aboard. Fly the route in the recorded aircraft; arrival requires ten stopped seconds at the destination.";
            return flight.EnsureValid();
        }
    }
}
