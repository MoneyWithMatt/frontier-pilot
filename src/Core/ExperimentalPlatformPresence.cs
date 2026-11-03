namespace FrontierAirportBuilder.Core;

/// <summary>Read-only experimental observations, never flight or delivery evidence.</summary>
public enum PlatformPresenceStage { Unavailable, OffPlatform, Airborne, Contact, StableDeckContact }

public sealed record PlatformPresenceStatus(PlatformPresenceStage Stage, string Reason,
    DateTimeOffset? ObservedUtc = null, double ContinuousContactSeconds = 0,
    double? DistanceMeters = null, double? AircraftHeightAboveDeckMeters = null)
{
    public bool DiagnosticDeckPresence => Stage == PlatformPresenceStage.StableDeckContact;
    public PlatformContactObservation? Observation { get; init; }
}

/// <summary>Validated diagnostic sample; continuity is relative to the preceding accepted sample.</summary>
public sealed record PlatformContactObservation(double SimulationSeconds, bool Consecutive,
    double Latitude, double Longitude, double HeightAboveDeckMeters, double DistanceMeters,
    double GroundSpeedKnots, double VerticalSpeedFeetPerSecond, double BankDegrees, double PitchDegrees,
    bool OnGround, int Contacts, bool Slewing, double SimulationRate);

/// <summary>Known static OL400/H125 test only. This profile does not enable a career destination.</summary>
public sealed class ExperimentalPlatformContactProfile : IAircraftConditionProfile
{
    public const double Latitude = -22.93892875313759, Longitude = 14.50461745262146;
    public const double DeckMeters = 39.89600372314453;
    public const string AircraftTitle = "H125 Passengers";
    public string Id => "experimental-ol400-h125-contact-v1";
    public ConditionIntegrationLevel Level => ConditionIntegrationLevel.Unverified;

    public IReadOnlyList<ConditionField> Fields(ConditionAircraftIdentity aircraft)
    {
        aircraft.Validate();
        if (aircraft.Title != AircraftTitle)
            throw new InvalidDataException("This experimental contact profile supports only the inspected H125 Passengers.");
        var result = new List<ConditionField>
        {
            Field("PLANE LATITUDE", "degrees", -90, 90),
            Field("PLANE LONGITUDE", "degrees", -180, 180),
            Field("PLANE ALTITUDE", "feet", -2000, 200000),
            Field("GROUND ALTITUDE", "feet", -2000, 100000),
            Field("GROUND VELOCITY", "knots", 0, 10000),
            Field("VERTICAL SPEED", "feet per second", -10000, 10000),
            Field("PLANE BANK DEGREES", "degrees", -180, 180),
            Field("PLANE PITCH DEGREES", "degrees", -180, 180),
            Flag("SIM ON GROUND"), Flag("IS SLEW ACTIVE"),
            Field("SIMULATION RATE", "number", 0, 128),
            Field("SIMULATION TIME", "seconds", 0, 1e12)
        };
        result.AddRange(Enumerable.Range(0, 20).Select(i => Flag($"CONTACT POINT IS ON GROUND:{i}")));
        return result;
    }

    private static ConditionField Field(string name, string unit, double min, double max) =>
        new(name, name, unit, ConditionKind.OperatingTelemetry, min, max);
    private static ConditionField Flag(string name) =>
        new(name, name, "bool", ConditionKind.OperatingTelemetry, 0, 1, true);
}

/// <summary>
/// Independent deck presence, not a mission, a touchdown event, an airframe identity or an arrival adapter.
/// Unknown runtime pause state is exposed in the reason; simulation-clock progress is still required.
/// No consumer may turn this diagnostic result into normal delivery/payment evidence.
/// </summary>
public sealed class ExperimentalPlatformPresence
{
    private ConditionAircraftIdentity? previousAircraft;
    private DateTimeOffset? previousUtc;
    private double? previousSim, contactStart;
    private long? previousDiscontinuity;
    public PlatformPresenceStatus Current { get; private set; } =
        new(PlatformPresenceStage.Unavailable, "No live platform-contact observation.");

    public PlatformPresenceStatus Observe(ConditionFrame? frame, DateTimeOffset now)
    {
        if (frame is null) return Reset("Disconnected or missing observation.");
        try { frame.Aircraft.Validate(); }
        catch (InvalidDataException) { return Reset("Invalid aircraft identity."); }
        if (frame.AdapterId != new ExperimentalPlatformContactProfile().Id ||
            frame.Aircraft.Title != ExperimentalPlatformContactProfile.AircraftTitle ||
            frame.StartedUtc == default || frame.CompletedUtc < frame.StartedUtc ||
            frame.CompletedUtc - frame.StartedUtc > TimeSpan.FromSeconds(1) ||
            now < frame.CompletedUtc || now - frame.StartedUtc > TimeSpan.FromSeconds(2) ||
            frame.Runtime is { Paused: true } or { Running: false })
            return Reset("Wrong profile/aircraft, stale observation or paused/stopped simulator.");

        var expected = new ExperimentalPlatformContactProfile().Fields(frame.Aircraft);
        if (frame.Readings is null || frame.Readings.Any(x => x is null || x.Field is null) || frame.Readings.Count != expected.Count ||
            frame.Readings.Select(x => x.Field.Key).Distinct(StringComparer.Ordinal).Count() != expected.Count)
            return Reset("Incomplete or duplicated contact telemetry.");
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var field in expected)
        {
            var reading = frame.Readings.SingleOrDefault(x => x.Field.Key == field.Key);
            if (reading is null || reading.Field != field ||
                reading.Availability != ConditionAvailability.ReceivedUnverified || reading.Value is not { } value ||
                reading.ObservedUtc < frame.StartedUtc || reading.ObservedUtc > frame.CompletedUtc ||
                field.Observe(value, reading.ObservedUtc).Availability != ConditionAvailability.ReceivedUnverified)
                return Reset("Missing, invalid, wrong-unit or unsynchronised contact telemetry.");
            values.Add(field.Key, value);
        }

        var sim = values["SIMULATION TIME"];
        var wallDelta = previousUtc is { } utc ? (frame.CompletedUtc - utc).TotalSeconds : 0;
        var simDelta = previousSim is { } previous ? sim - previous : 0;
        var consecutive = previousAircraft == frame.Aircraft && previousUtc is not null &&
            wallDelta is > 0 and <= 2 && simDelta is > 0 and <= 2 &&
            Math.Abs(simDelta - wallDelta) <= .5 &&
            previousDiscontinuity == frame.Runtime?.Discontinuity;
        previousAircraft = frame.Aircraft;
        previousUtc = frame.CompletedUtc;
        previousSim = sim;
        previousDiscontinuity = frame.Runtime?.Discontinuity;
        if (!consecutive) contactStart = null;

        var distance = WorldTracking.DistanceMeters(values["PLANE LATITUDE"], values["PLANE LONGITUDE"],
            ExperimentalPlatformContactProfile.Latitude, ExperimentalPlatformContactProfile.Longitude);
        var height = values["PLANE ALTITUDE"] * .3048 - ExperimentalPlatformContactProfile.DeckMeters;
        var surfaceError = Math.Abs(values["GROUND ALTITUDE"] * .3048 - ExperimentalPlatformContactProfile.DeckMeters);
        var observation = new PlatformContactObservation(sim, consecutive,
            values["PLANE LATITUDE"], values["PLANE LONGITUDE"], height, distance,
            values["GROUND VELOCITY"], values["VERTICAL SPEED"], values["PLANE BANK DEGREES"], values["PLANE PITCH DEGREES"],
            values["SIM ON GROUND"] == 1,
            Enumerable.Range(0, 20).Count(i => values[$"CONTACT POINT IS ON GROUND:{i}"] == 1),
            values["IS SLEW ACTIVE"] != 0, values["SIMULATION RATE"]);
        PlatformPresenceStatus NotPresent(PlatformPresenceStage stage, string reason)
        {
            contactStart = null;
            return Current = new(stage, reason, frame.CompletedUtc, 0, distance, height) { Observation = observation };
        }
        if (values["IS SLEW ACTIVE"] != 0 || values["SIMULATION RATE"] != 1)
            return NotPresent(PlatformPresenceStage.Unavailable, "Slew or nonstandard simulation rate; no deck presence.");
        if (distance > 8 || height < .3 || height > 1.5 || surfaceError > .5)
            return NotPresent(PlatformPresenceStage.OffPlatform, "Outside the known deck's horizontal/elevation envelope; water/lower surfaces cannot qualify.");
        var contacts = Enumerable.Range(0, 20).Select(i => values[$"CONTACT POINT IS ON GROUND:{i}"]).ToArray();
        if (values["SIM ON GROUND"] != 1 || !contacts.Any(x => x == 1))
            return NotPresent(PlatformPresenceStage.Airborne, "No confirmed physical contact with the deck.");
        if (values["GROUND VELOCITY"] > .5 || Math.Abs(values["VERTICAL SPEED"]) > .1 ||
            Math.Abs(values["PLANE BANK DEGREES"]) > 5 || Math.Abs(values["PLANE PITCH DEGREES"]) > 5)
            return NotPresent(PlatformPresenceStage.Contact, "Contact observed; motion/attitude is not stable.");

        contactStart ??= sim;
        var duration = sim - contactStart.Value;
        var stage = duration >= 20 ? PlatformPresenceStage.StableDeckContact : PlatformPresenceStage.Contact;
        var reason = stage == PlatformPresenceStage.StableDeckContact
            ? "Diagnostic stable OL400 deck presence. No flight, delivery or payment evidence."
            : "Observing uninterrupted deck contact for 20 simulation seconds.";
        if (frame.Runtime?.Paused is null) reason += " Pause-event state unknown; only clock continuity checked.";
        return Current = new(stage, reason, frame.CompletedUtc, duration, distance, height) { Observation = observation };
    }

    public PlatformPresenceStatus Reset(string reason)
    {
        previousAircraft = null; previousUtc = null; previousSim = contactStart = null;
        previousDiscontinuity = null;
        return Current = new(PlatformPresenceStage.Unavailable, reason);
    }
}
