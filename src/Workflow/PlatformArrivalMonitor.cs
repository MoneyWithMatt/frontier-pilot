using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed class PlatformMissionFlightProfile : IAircraftConditionProfile
{
    public string Id => "offshore-platform-mission-flight-v1";
    public ConditionIntegrationLevel Level => ConditionIntegrationLevel.Unverified;
    public IReadOnlyList<ConditionField> Fields(ConditionAircraftIdentity aircraft) => new OffshoreFlightProfile().Fields(aircraft)
        .Concat(Enumerable.Range(0, 20).Select(i => new ConditionField($"CONTACT POINT IS ON GROUND:{i}", $"CONTACT POINT IS ON GROUND:{i}", "bool", ConditionKind.OperatingTelemetry, 0, 1, true)))
        .Concat(new[] { "PLANE BANK DEGREES", "PLANE PITCH DEGREES" }.Select(n => new ConditionField(n, n, "degrees", ConditionKind.OperatingTelemetry, -180, 180))).ToArray();
}

public sealed partial class FlightAttemptTracker
{
    /// <summary>Future normal platform-flight adapter. Requires registry qualification AND its own live outbound sequence.
    /// Static/diagnostic records never become flight evidence. No production platform is currently enabled.</summary>
    public sealed class PlatformArrivalMonitor
    {
        private OffshoreFlightSample? previous;
        private FlightAttempt? observed;
        private DateTimeOffset? originSince;
        private double? contactSince;
        private Guid? airframe;
        public string Reason { get; private set; } = "No qualified platform flight.";
        private FlightAttempt Refuse(FlightAttempt flight, string reason)
        { previous = null; observed = null; originSince = null; contactSince = null; airframe = null; Reason = reason; return flight; }
        public FlightAttempt Observe(OffshorePlatform platform, PlatformOperation operation, string simulatorVersion, string sceneryVersion,
            Guid recordedAirframe, Guid selectedAirframe, FlightAttempt flight, ConditionFrame? frame, DateTimeOffset now)
        {
            flight.EnsureValid();
            if (flight.DeliveryProof is not null) return Refuse(flight, "Existing arrival proof retained; no duplicate evidence or wages created.");
            if (!platform.CanOperate(operation, flight.Aircraft.Title, flight.Aircraft.Type, simulatorVersion, sceneryVersion))
                return Refuse(flight, platform.BlockedReason);
            if (recordedAirframe == Guid.Empty || selectedAirframe != recordedAirframe || flight.DestinationLocationId != platform.Id ||
                airframe is {} bound && bound != selectedAirframe || observed is {} priorFlight && priorFlight.Id != flight.Id)
                return Refuse(flight, "Matching selected physical airframe and accepted platform flight required.");
            var sample = OffshoreFlightSample.From(frame, now, new PlatformMissionFlightProfile());
            if (sample is null || sample.Identity.Title != flight.Aircraft.Title || sample.Identity.Model != flight.Aircraft.Type || sample.Position.ObservedUtc < flight.LoadedUtc)
                return Refuse(flight, "Fresh normal-flight telemetry missing/interrupted. Diagnostic records, slew and frozen physics cannot qualify.");
            var prior = previous;
            var elapsed = OffshoreFlightContinuity.Elapsed(prior, sample);
            if (prior is not null && (elapsed is null || !OffshoreFlightContinuity.Plausible(prior, sample)))
                return Refuse(flight, "Flight continuity interrupted; retain persisted facts but re-establish a fresh origin flight before platform qualification.");
            previous = sample; airframe = selectedAirframe;
            var distanceOrigin = WorldTracking.DistanceMeters(sample.Position.Latitude, sample.Position.Longitude, flight.LoadedPosition.Latitude, flight.LoadedPosition.Longitude) / 1852;
            var atOrigin = sample.Position.IsOnGround && sample.Position.GroundSpeedKnots <= 5 && distanceOrigin <= 1.5 &&
                sample.Position.AltitudeFeet - sample.GroundAltitudeFeet is >= 0 and <= 30;
            if (observed is null)
            {
                if (atOrigin) originSince ??= sample.Position.ObservedUtc; else originSince = null;
                if (originSince is null || sample.Position.ObservedUtc - originSince < TimeSpan.FromSeconds(10))
                { Reason = "Observe ten continuous stopped seconds at the loaded origin before this normal platform flight."; return flight; }
                observed = flight with { State = FlightAttemptState.CargoLoaded, FirstAirborneUtc = null, MovementObservedUtc = null,
                    OriginDepartedUtc = null, MaximumDistanceFromOriginNauticalMiles = 0, MaximumDistanceObservedUtc = null,
                    VerifiedAirborneTicks = 0, WageObservedUtc = null, WageFlightId = flight.Id };
            }
            var telemetry = new AircraftTelemetry(sample.Position.ObservedUtc, sample.Identity.Title, sample.Identity.Model,
                sample.Position.Latitude, sample.Position.Longitude, sample.Position.AltitudeFeet, sample.Position.TrueHeadingDegrees,
                sample.Position.GroundSpeedKnots, sample.Position.IsOnGround);
            AircraftTelemetry? previousTelemetry = prior is null ? null : new(prior.Position.ObservedUtc, prior.Identity.Title, prior.Identity.Model,
                prior.Position.Latitude, prior.Position.Longitude, prior.Position.AltitudeFeet, prior.Position.TrueHeadingDegrees, prior.Position.GroundSpeedKnots, prior.Position.IsOnGround);
            observed = AccumulateAirborneTime(observed, telemetry, previousTelemetry, elapsed is not null, new());
            if (!sample.Position.IsOnGround)
            {
                observed = observed with { FirstAirborneUtc = observed.FirstAirborneUtc ?? sample.Position.ObservedUtc };
                if (distanceOrigin > 1.5) observed = observed with { OriginDepartedUtc = observed.OriginDepartedUtc ?? sample.Position.ObservedUtc };
                if (distanceOrigin >= 2 && sample.Position.ObservedUtc > observed.FirstAirborneUtc)
                    observed = observed with { MovementObservedUtc = observed.MovementObservedUtc ?? sample.Position.ObservedUtc };
                if (distanceOrigin > observed.MaximumDistanceFromOriginNauticalMiles)
                    observed = observed with { MaximumDistanceFromOriginNauticalMiles = distanceOrigin, MaximumDistanceObservedUtc = sample.Position.ObservedUtc };
            }
            var nativeOutbound = observed.FirstAirborneUtc is not null && observed.MovementObservedUtc is not null &&
                observed.OriginDepartedUtc is not null && observed.VerifiedAirborneTicks >= TimeSpan.FromSeconds(5).Ticks &&
                observed.MaximumDistanceFromOriginNauticalMiles >= observed.RequiredMaximumDistanceNauticalMiles;
            var stable = nativeOutbound && PlatformFlightContact.Stable(platform, sample, frame!);
            if (!stable) contactSince = null; else contactSince ??= sample.SimulationSeconds;
            var duration = contactSince is {} since ? sample.SimulationSeconds - since : 0;
            Reason = nativeOutbound ? $"Platform contact {duration:0.#} / 20 seconds; no hover/water or incomplete contact qualifies." : "Normal outbound flight must precede platform contact. No static start or old diagnostic credit.";
            if (duration < 20) return flight;
            return (observed with { State = FlightAttemptState.ReadyToUnload, DeliveryProof = ArrivalProof(observed, telemetry) }).EnsureValid();
        }
    }
}
