using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public enum OffshoreTestStage { AwaitingBase, Outbound, EstablishingDeck, Inspecting, ReadyToDepart, Returning, Completed, Interrupted }
public enum OffshoreTestMilestone { BaseQualified, OutboundObserved, DeckArrival, InspectionObserved, PlatformDeparture, BaseReturn }
public sealed record OffshoreTestEvent(OffshoreTestMilestone Milestone, DateTimeOffset ObservedUtc);
public sealed record OffshoreRoundTripStatus(OffshoreTestStage Stage, string Reason, double InspectionSeconds,
    bool CurrentDeckPresence, bool CurrentBasePresence, IReadOnlyList<OffshoreTestEvent> Events)
{
    public bool Completed => Stage == OffshoreTestStage.Completed;
    public string Authority => "Experimental read-only qualification only. No delivery, career reward or platform activation.";
}

/// <summary>Native qualification sequence, deliberately disconnected from economy, loading and fleet saves.
/// Reuses the shared tracker route monitor and pure contact predicate, never returns its internal flight proofs.</summary>
public sealed class ExperimentalOffshoreRoundTrip
{
    private readonly FlightAttemptTracker.PlatformRoundTripMonitor monitor;
    public OffshoreRoundTripStatus Current => monitor.Current;
    public ExperimentalOffshoreRoundTrip(PassengerEndpoint origin, OffshorePlatform platform, int inspectionSeconds = 60) =>
        monitor = new(origin, platform, inspectionSeconds);
    public OffshoreRoundTripStatus Observe(ConditionFrame? frame, DateTimeOffset now) => monitor.Observe(frame, now);
}

internal static class PlatformFlightContact
{
    public static bool Stable(OffshorePlatform platform, OffshoreFlightSample sample, ConditionFrame frame)
    {
        var values = frame.Readings.ToDictionary(r => r.Field.Key, r => r.Value!.Value);
        return sample.Position.IsOnGround && Enumerable.Range(0, 20).Any(i => values[$"CONTACT POINT IS ON GROUND:{i}"] == 1) &&
            DeckSurfaceContact.WithinEnvelope(WorldTracking.DistanceMeters(sample.Position.Latitude, sample.Position.Longitude,
                platform.Latitude, platform.Longitude), sample.Position.AltitudeFeet * .3048 - platform.DeckMeters,
                Math.Abs(sample.GroundAltitudeFeet * .3048 - platform.DeckMeters), platform.ContactRadiusMeters) &&
            DeckSurfaceContact.StableMotion(sample.Position.GroundSpeedKnots, sample.VerticalSpeedFeetPerSecond,
                values["PLANE BANK DEGREES"], values["PLANE PITCH DEGREES"]);
    }
    public static bool Separated(OffshorePlatform platform, OffshoreFlightSample sample, ConditionFrame frame) =>
        !sample.Position.IsOnGround && sample.Position.AltitudeFeet * .3048 - platform.DeckMeters > 1.5 &&
        frame.Readings.Where(r => r.Field.Key.StartsWith("CONTACT POINT IS ON GROUND:", StringComparison.Ordinal)).All(r => r.Value == 0);
    public static AircraftTelemetry Telemetry(OffshoreFlightSample s) => new(s.Position.ObservedUtc, s.Identity.Title, s.Identity.Model,
        s.Position.Latitude, s.Position.Longitude, s.Position.AltitudeFeet, s.Position.TrueHeadingDegrees, s.Position.GroundSpeedKnots, s.Position.IsOnGround);
}

public sealed partial class FlightAttemptTracker
{
    internal sealed class PlatformRoundTripMonitor
    {
        private readonly PassengerEndpoint origin, destination;
        private readonly OffshorePlatform platform;
        private readonly int requiredInspection;
        private readonly TransportMonitor outbound = new(), returning = new();
        private readonly List<OffshoreTestEvent> events = [];
        private readonly string journeyId = "platform-test-" + Guid.NewGuid().ToString("N");
        private OffshoreFlightSample? previous;
        private ConditionAircraftIdentity? identity;
        private FlightAttempt? outboundFlight, returnFlight;
        private double? contactSince, inspectionSince, departureSince, baseReturnSince;
        private bool armed, closed;
        public OffshoreRoundTripStatus Current { get; private set; } = new(OffshoreTestStage.AwaitingBase,
            "Stop at the resolved Walvis Bay base for ten continuous seconds. Diagnostic only.", 0, false, false, []);
        public PlatformRoundTripMonitor(PassengerEndpoint origin, OffshorePlatform platform, int seconds)
        {
            origin.EnsureValid();
            // This allowlist permits observation of the known static test site, never production destination activation.
            if (!origin.CanGenerate || platform.Id != "ol400-h01" || platform.Stage < PlatformQualificationStage.StaticDeckContact ||
                seconds is < 10 or > 3600 || WorldTracking.DistanceMeters(origin.Latitude, origin.Longitude, platform.Latitude, platform.Longitude) < 3704)
                throw new InvalidOperationException("Use a resolved airport base, known OL400 static-contact test platform and 10–3600 second visit.");
            this.origin = origin; this.platform = platform; requiredInspection = seconds;
            destination = new(platform.Id, platform.Name, platform.Latitude, platform.Longitude, PassengerEndpointKind.Platform);
        }
        private bool Has(OffshoreTestMilestone milestone) => events.Any(e => e.Milestone == milestone);
        private void Record(OffshoreTestMilestone milestone, DateTimeOffset utc)
        { if (!Has(milestone)) events.Add(new(milestone, utc)); }
        private OffshoreRoundTripStatus Status(OffshoreTestStage stage, string reason, double inspection = 0, bool deck = false, bool atBase = false) =>
            Current = new(stage, reason, inspection, deck, atBase, events.ToArray());
        public OffshoreRoundTripStatus Observe(ConditionFrame? frame, DateTimeOffset now)
        {
            if (closed) return Current;
            var sample = OffshoreFlightSample.From(frame, now, new PlatformMissionFlightProfile());
            bool interrupted = sample is null || sample.Identity.Title != ExperimentalPlatformContactProfile.AircraftTitle || sample.Identity.Model != "H125" ||
                identity is not null && sample.Identity != identity || previous is not null &&
                (OffshoreFlightContinuity.Elapsed(previous, sample) is null || !OffshoreFlightContinuity.Plausible(previous, sample));
            if (interrupted)
            {
                previous = null; outbound.Reset(); returning.Reset(); contactSince = inspectionSince = departureSince = null;
                if (Current.Completed) { closed = true; return Status(OffshoreTestStage.Completed, "Completed historical experiment; recording closed. No live permission.", requiredInspection); }
                if (armed) { closed = true; return Status(OffshoreTestStage.Interrupted, "Native continuity/identity/physics interrupted. Preserve history; a new test must start at base. No stitched flight or reward."); }
                return Status(OffshoreTestStage.AwaitingBase, "Fresh exact H125 native telemetry required. No paused, frozen, accelerated or diagnostic-profile credit.");
            }
            identity ??= sample!.Identity; previous = sample;
            var telemetry = PlatformFlightContact.Telemetry(sample!);
            var atBase = sample!.Position.IsOnGround && sample.Position.GroundSpeedKnots <= 5 &&
                WorldTracking.DistanceMeters(sample.Position.Latitude, sample.Position.Longitude, origin.Latitude, origin.Longitude) <= 2778 &&
                sample.Position.AltitudeFeet - sample.GroundAltitudeFeet is >= 0 and <= 30;
            var onDeck = PlatformFlightContact.Stable(platform, sample, frame!);
            if (Current.Completed) return Status(OffshoreTestStage.Completed, "Round trip observed once. Historical diagnostic only; no career reward or automatic qualification.", requiredInspection, onDeck, atBase);
            if (!Has(OffshoreTestMilestone.InspectionObserved))
            {
                var context = new TransportFlightContext(journeyId, origin, destination, outboundFlight);
                outboundFlight = outbound.Observe(context, !armed && !atBase ? null : telemetry);
                if (!armed)
                {
                    if (atBase && outbound.Ready(context, now, true))
                    { outboundFlight = outbound.Board(context, now); armed = true; Record(OffshoreTestMilestone.BaseQualified, now); }
                    return Status(armed ? OffshoreTestStage.Outbound : OffshoreTestStage.AwaitingBase,
                        armed ? "Base qualified. Fly normally to OL400; no static start can establish the outbound leg." : "Observe ten continuous stopped seconds at base.");
                }
                var f = outboundFlight!;
                var flown = f.OriginDepartedUtc is not null && f.MovementObservedUtc is not null && f.VerifiedAirborneTicks >= TimeSpan.FromSeconds(5).Ticks &&
                    f.MaximumDistanceFromOriginNauticalMiles >= f.RequiredMaximumDistanceNauticalMiles;
                if (!flown) return Status(OffshoreTestStage.Outbound, "Genuine native departure and route movement required before deck arrival.");
                Record(OffshoreTestMilestone.OutboundObserved, now);
                if (!onDeck)
                { contactSince = inspectionSince = null; return Status(OffshoreTestStage.EstablishingDeck, "Fly to OL400 and land on the deck. Hover, sea-level contact and proximity do not count."); }
                contactSince ??= sample.SimulationSeconds;
                if (sample.SimulationSeconds - contactSince < 20)
                    return Status(OffshoreTestStage.EstablishingDeck, $"Stable deck contact {sample.SimulationSeconds - contactSince:0} / 20 seconds.", deck: true);
                Record(OffshoreTestMilestone.DeckArrival, now);
                inspectionSince ??= sample.SimulationSeconds;
                var duration = Math.Min(requiredInspection, sample.SimulationSeconds - inspectionSince.Value);
                if (duration < requiredInspection) return Status(OffshoreTestStage.Inspecting, "Remain on the deck for the timed experimental visit.", duration, true);
                Record(OffshoreTestMilestone.InspectionObserved, now);
                // Only the tracker constructs the internal return attempt, from current confirmed deck presence.
                // This record is never returned as cargo/passenger/economy evidence.
                var id = journeyId + "-return";
                returnFlight = new(FlightAttemptIds.Attempt(id), id, id, destination.Id, origin.Id, FlightAttemptState.CargoLoaded,
                    now, FlightAircraftIdentity.From(telemetry), FlightPosition.From(telemetry),
                    RequiredMaximumDistanceNauticalMiles: RequiredDistanceForRoute(WorldTracking.DistanceMeters(origin.Latitude, origin.Longitude, platform.Latitude, platform.Longitude) / 1852), TransitBeginsAtDeparture: true);
                returning.Observe(new TransportFlightContext(id, destination, origin, returnFlight), telemetry);
                return Status(OffshoreTestStage.ReadyToDepart, "Timed visit observed. Depart normally and return to base; no wages are issued.", requiredInspection, true);
            }
            returnFlight = returning.Observe(new TransportFlightContext(journeyId + "-return", destination, origin, returnFlight), telemetry)!;
            if (atBase) baseReturnSince ??= sample.SimulationSeconds; else baseReturnSince = null;
            if (!Has(OffshoreTestMilestone.PlatformDeparture))
            {
                if (PlatformFlightContact.Separated(platform, sample, frame!)) departureSince ??= sample.SimulationSeconds; else departureSince = null;
                if (departureSince is {} start && sample.SimulationSeconds - start >= 3) Record(OffshoreTestMilestone.PlatformDeparture, now);
            }
            if (Has(OffshoreTestMilestone.PlatformDeparture) && returnFlight.DeliveryProof is not null && baseReturnSince is {} arrived && sample.SimulationSeconds - arrived >= 10)
            { Record(OffshoreTestMilestone.BaseReturn, now); return Status(OffshoreTestStage.Completed, "Experimental flown visit and return observed once. No career reward or platform activation.", requiredInspection, atBase: true); }
            return Status(Has(OffshoreTestMilestone.PlatformDeparture) ? OffshoreTestStage.Returning : OffshoreTestStage.ReadyToDepart,
                "Return normally and stop at base for ten continuous seconds. No missing time is reconstructed.", requiredInspection, onDeck, atBase);
        }
    }
}
