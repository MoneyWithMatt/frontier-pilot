using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed partial class FlightAttemptTracker
{
    /// <summary>Multi-stage observation flight monitor. Shares the tracker's gap policy/geography; never manufactures cargo delivery evidence.</summary>
    internal sealed class OffshoreMonitor
    {
        private readonly FlightAttemptTrackerOptions policy = new();
        private OffshoreFlightSample? previous;
        private DateTimeOffset? baseSince;
        private double? loiterSince;
        private double returningAirborne;
        public OffshoreFlightSample? Latest { get; private set; }
        public string Reason { get; private set; } = "Connect mission tracking and stop at base for ten seconds.";
        public double BaseSeconds { get; private set; }
        public void Reset() { previous = Latest = null; baseSince = null; loiterSince = null; returningAirborne = 0; BaseSeconds = 0; }
        public bool ReadyAtBase(PassengerEndpoint airport, DateTimeOffset utc) => Latest is {} latest && utc >= latest.Position.ObservedUtc &&
            utc - latest.Position.ObservedUtc <= policy.MaximumSampleGap && BaseSeconds >= policy.RequiredStationaryDuration.TotalSeconds && AtBase(latest, airport);
        private static bool AtBase(OffshoreFlightSample s, PassengerEndpoint b) => s.Position.IsOnGround && s.Position.GroundSpeedKnots <= 5 &&
            WorldTracking.DistanceMeters(s.Position.Latitude, s.Position.Longitude, b.Latitude, b.Longitude) <= 2778 &&
            s.Position.AltitudeFeet - s.GroundAltitudeFeet is >= 0 and <= 30;
        public OffshoreMission? Observe(OffshoreMission? mission, PassengerEndpoint airport, ConditionFrame? frame, DateTimeOffset now)
        {
            var sample = OffshoreFlightSample.From(frame, now);
            if (sample is null)
            {
                Reset(); Reason = "Mission telemetry paused, disconnected, stale, accelerated, frozen or unavailable. Unconfirmed observation time reset; no wages accrue.";
                return mission is { Active: true, ObservationCompletedUtc: null } ? mission with { ObservationSeconds = 0 } : mission;
            }
            if (mission?.Aircraft is {} aircraft && (aircraft.Title != sample.Identity.Title || aircraft.Type != sample.Identity.Model))
            { Reset(); Reason = "Resume with the recorded aircraft and selected employer airframe."; return mission with { RecoveryRequired = true }; }
            var before = previous;
            if (before is null && mission is { Active: true, LastPosition: {} anchor, LastSimulationSeconds: {} priorSim, RecoveryRequired: false })
            {
                var elapsed = sample.SimulationSeconds - priorSim;
                var distance = WorldTracking.DistanceMeters(anchor.Latitude, anchor.Longitude, sample.Position.Latitude, sample.Position.Longitude);
                if (elapsed < 0 || distance > Math.Max(100, Math.Max(anchor.GroundSpeedKnots, sample.Position.GroundSpeedKnots) * .514444 * Math.Max(0, elapsed) * 1.5 + 20))
                { Reset(); Reason = "Position/time changed across an interruption. Return to base or the last recorded local area before explicit recovery."; return mission with { RecoveryRequired = true }; }
            }
            var wall = before is null ? 0 : (sample.Position.ObservedUtc - before.Position.ObservedUtc).TotalSeconds;
            var sim = before is null ? 0 : sample.SimulationSeconds - before.SimulationSeconds;
            var continuous = before is not null && before.Identity == sample.Identity && before.RuntimeDiscontinuity == sample.RuntimeDiscontinuity &&
                wall is > 0 and <= 2 && sim is > 0 and <= 2 && Math.Abs(wall - sim) <= .5;
            if (continuous && before is not null)
            {
                var delta = WorldTracking.DistanceMeters(before.Position.Latitude, before.Position.Longitude, sample.Position.Latitude, sample.Position.Longitude);
                var maxSpeed = Math.Max(before.Position.GroundSpeedKnots, sample.Position.GroundSpeedKnots);
                if (maxSpeed > 400 || delta > (maxSpeed * .514444 * wall * 1.5 + 20) ||
                    Math.Abs(sample.Position.AltitudeFeet - before.Position.AltitudeFeet) >
                    (Math.Max(Math.Abs(before.VerticalSpeedFeetPerSecond), Math.Abs(sample.VerticalSpeedFeetPerSecond)) * wall * 1.5 + 50))
                {
                    Reset(); Reason = "Position changed discontinuously. Stop at base and explicitly resume; existing facts and wages are preserved.";
                    return mission is { Active: true } ? mission with { RecoveryRequired = true, ObservationSeconds = mission.ObservationCompletedUtc is null ? 0 : mission.ObservationSeconds } : mission;
                }
            }
            if (!continuous) { baseSince = null; loiterSince = null; returningAirborne = 0; }
            previous = Latest = sample;
            if (AtBase(sample, airport)) baseSince ??= sample.Position.ObservedUtc; else baseSince = null;
            BaseSeconds = baseSince is {} since ? (sample.Position.ObservedUtc - since).TotalSeconds : 0;
            if (mission is null || !mission.Active || mission.Aircraft is null) { Reason = $"Base qualification {BaseSeconds:0} / 10 seconds."; return mission; }
            if (mission.ReturnReadyUtc is not null) { Reason = "Return proof retained. Stop at base and complete; no further wages accrue."; return mission; }
            if (mission.RecoveryRequired) { Reason = "Tracking was interrupted. Select the recorded airframe and resume with fresh telemetry."; return mission; }
            var position = sample.Position;
            var m = mission with { LastPosition = position, LastSimulationSeconds = sample.SimulationSeconds };
            // Same gap/airborne-pair rule as other tracker careers; simulation-clock agreement was checked above.
            if (continuous && !position.IsOnGround && before is { Position.IsOnGround: false })
                m = m with { AirborneTicks = checked(m.AirborneTicks + TimeSpan.FromSeconds(Math.Min(sim, wall)).Ticks) };
            var fromBase = WorldTracking.DistanceMeters(position.Latitude, position.Longitude, airport.Latitude, airport.Longitude) / 1852;
            if (!position.IsOnGround && fromBase > policy.AirportRadiusNauticalMiles && m.DepartedUtc is null)
                m = m with { DepartedUtc = position.ObservedUtc, State = OffshoreMissionState.Outbound };
            if (m.DepartedUtc is not null && m.ObservationCompletedUtc is null)
            {
                var inArea = !position.IsOnGround && fromBase >= policy.MeaningfulMovementNauticalMiles &&
                    position.AltitudeFeet - sample.GroundAltitudeFeet >= 100 &&
                    WorldTracking.DistanceMeters(position.Latitude, position.Longitude, m.Area.Latitude, m.Area.Longitude) <= m.Area.RadiusNauticalMiles * 1852;
                if (inArea) loiterSince ??= sample.SimulationSeconds; else loiterSince = null;
                var observed = loiterSince is {} start ? Math.Min(m.Area.ObservationSeconds, sample.SimulationSeconds - start) : 0;
                m = m with { ObservationSeconds = observed, State = inArea ? OffshoreMissionState.Observing : OffshoreMissionState.Outbound };
                if (observed >= m.Area.ObservationSeconds)
                    m = m with { ObservationCompletedUtc = position.ObservedUtc, State = OffshoreMissionState.Returning };
                Reason = inArea ? $"Observation {observed:0} / {m.Area.ObservationSeconds} seconds. Stay airborne in the broad operating area." : "Fly to the operating area. Leaving it resets only the unconfirmed observation interval.";
            }
            else if (m.ObservationCompletedUtc is not null && m.ReturnReadyUtc is null)
            {
                if (continuous && !position.IsOnGround && before is { Position.IsOnGround: false }) returningAirborne += sim;
                if (returningAirborne >= 5 && ReadyAtBase(airport, now))
                    m = m with { ReturnReadyUtc = position.ObservedUtc, State = OffshoreMissionState.ReadyToComplete };
                Reason = m.ReturnReadyUtc is not null ? "Returned and stopped at base. Complete the mission to settle verified airborne wages once." : "Observation complete. Fly back to base and stop for ten seconds.";
            }
            return m.EnsureValid();
        }
    }
}
