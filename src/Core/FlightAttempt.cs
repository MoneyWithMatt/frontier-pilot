namespace FrontierAirportBuilder.Core;

public enum FlightAttemptState { CargoLoaded, Departed, EnRoute, ReadyToUnload, Delivered }
public enum FlightDiagnosticEventType
{
    FlightAttemptCreated, SimConnectConnected, SimConnectDisconnected,
    OriginQualificationStarted, OriginQualificationInterrupted, ReadyToLoad,
    CargoLoaded, ShipmentEnteredInTransit, AirborneEvidenceRecorded,
    PostDepartureMovementRecorded, RequiredMaximumDistanceReached,
    ArrivalQualificationStarted, ArrivalQualificationInterrupted, ReadyToUnload,
    UnloadRequested, DeliveryCommitted, DeliveryRefused, ApplicationRecovery,
    AircraftIdentityMismatchObserved, ShipmentLoaded, CargoLoadReset
}

/// <summary>Read-only diagnostic history. Events describe observations and never qualify a delivery.</summary>
public sealed record FlightDiagnosticEvent(string Id, string JobId, string ShipmentId, DateTimeOffset OccurredUtc,
    FlightDiagnosticEventType Type, IReadOnlyDictionary<string, string> Details)
{
    public FlightDiagnosticEvent EnsureValid()
    {
        EconomyValidation.Id(Id, nameof(Id)); EconomyValidation.Id(JobId, nameof(JobId)); EconomyValidation.Id(ShipmentId, nameof(ShipmentId));
        if (OccurredUtc == default || Details is null || Details.Count > 16) throw new InvalidDataException("Flight diagnostic event is invalid.");
        foreach (var pair in Details)
        {
            EconomyValidation.Text(pair.Key, "diagnostic detail key", 48);
            if (pair.Value is null || pair.Value.Length > 256) throw new InvalidDataException("Flight diagnostic detail value is invalid.");
        }
        return this;
    }
}

public sealed record FlightAircraftIdentity(string Title, string Type)
{
    public FlightAircraftIdentity EnsureValid()
    {
        EconomyValidation.Text(Title, nameof(Title), 128); EconomyValidation.Text(Type, nameof(Type), 128);
        if (Title.Equals("Unknown aircraft", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Aircraft identity is not available yet.");
        return this with { Title = Title.Trim(), Type = Type.Trim() };
    }

    public static FlightAircraftIdentity From(AircraftTelemetry telemetry) =>
        new FlightAircraftIdentity(telemetry.EnsureValid().AircraftTitle, telemetry.AircraftType).EnsureValid();

    public bool ClearlyDiffersFrom(FlightAircraftIdentity other) =>
        !string.Equals(Title, other.Title, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(Type, other.Type, StringComparison.OrdinalIgnoreCase);
}

public sealed record FlightPosition(DateTimeOffset ObservedUtc, double Latitude, double Longitude, double AltitudeFeet,
    double TrueHeadingDegrees, double GroundSpeedKnots, bool IsOnGround)
{
    public FlightPosition EnsureValid()
    {
        _ = new AircraftTelemetry(ObservedUtc, "Flight attempt observation", "validation", Latitude, Longitude,
            AltitudeFeet, TrueHeadingDegrees, GroundSpeedKnots, IsOnGround).EnsureValid();
        return this with { TrueHeadingDegrees = Headings.Normalize(TrueHeadingDegrees), GroundSpeedKnots = Math.Max(0, GroundSpeedKnots) };
    }
    public static FlightPosition From(AircraftTelemetry value) => new FlightPosition(value.ObservedUtc, value.Latitude, value.Longitude,
        value.AltitudeFeet, value.TrueHeadingDegrees, value.GroundSpeedKnots, value.IsOnGround).EnsureValid();
}

/// <summary>Persisted proof assembled by FlightAttemptTracker. Economy receives a VerifiedDeliveryEvidence only when unloading.</summary>
public sealed record FlightDeliveryProof(string EvidenceId, DateTimeOffset ReadyUtc, FlightPosition ArrivalPosition,
    DateTimeOffset FirstAirborneUtc, DateTimeOffset MovementObservedUtc, double MaximumDistanceFromOriginNauticalMiles)
{
    public FlightDeliveryProof EnsureValid()
    {
        EconomyValidation.Id(EvidenceId, nameof(EvidenceId)); ArrivalPosition.EnsureValid();
        if (ReadyUtc == default || FirstAirborneUtc == default || MovementObservedUtc <= FirstAirborneUtc ||
            ReadyUtc < MovementObservedUtc || ReadyUtc != ArrivalPosition.ObservedUtc ||
            !double.IsFinite(MaximumDistanceFromOriginNauticalMiles) || MaximumDistanceFromOriginNauticalMiles < 0)
            throw new InvalidDataException("Flight delivery proof is inconsistent.");
        return this;
    }
}

public sealed record FlightAttempt(string Id, string JobId, string ShipmentId, string OriginLocationId,
    string DestinationLocationId, FlightAttemptState State, DateTimeOffset LoadedUtc, FlightAircraftIdentity Aircraft,
    FlightPosition LoadedPosition, DateTimeOffset? FirstAirborneUtc = null, DateTimeOffset? MovementObservedUtc = null,
    double MaximumDistanceFromOriginNauticalMiles = 0, DateTimeOffset? MaximumDistanceObservedUtc = null,
    FlightDeliveryProof? DeliveryProof = null, DateTimeOffset? DeliveredUtc = null,
    double RequiredMaximumDistanceNauticalMiles = 10, bool TransitBeginsAtDeparture = false,
    DateTimeOffset? OriginDepartedUtc = null)
{
    public FlightAttempt EnsureValid()
    {
        EconomyValidation.Id(Id, nameof(Id)); EconomyValidation.Id(JobId, nameof(JobId)); EconomyValidation.Id(ShipmentId, nameof(ShipmentId));
        EconomyValidation.Id(OriginLocationId, nameof(OriginLocationId)); EconomyValidation.Id(DestinationLocationId, nameof(DestinationLocationId));
        if (OriginLocationId == DestinationLocationId || LoadedUtc == default) throw new InvalidDataException("Flight attempt route or load time is invalid.");
        Aircraft.EnsureValid(); LoadedPosition.EnsureValid();
        if (OriginDepartedUtc is { } departed && (FirstAirborneUtc is null || departed < FirstAirborneUtc || departed <= LoadedUtc))
            throw new InvalidDataException("Origin departure requires a preceding airborne observation.");
        if (LoadedPosition.ObservedUtc != LoadedUtc || !LoadedPosition.IsOnGround)
            throw new InvalidDataException("Flight attempt load observation is inconsistent.");
        if (!double.IsFinite(RequiredMaximumDistanceNauticalMiles) || RequiredMaximumDistanceNauticalMiles is < 0.5 or > 10 ||
            !double.IsFinite(MaximumDistanceFromOriginNauticalMiles) || MaximumDistanceFromOriginNauticalMiles < 0 ||
            (MaximumDistanceFromOriginNauticalMiles > 0) != (MaximumDistanceObservedUtc is not null))
            throw new InvalidDataException("Flight attempt maximum-distance evidence is invalid.");
        if (MaximumDistanceObservedUtc is { } maximumTime && maximumTime < LoadedUtc)
            throw new InvalidDataException("Flight attempt maximum-distance time precedes loading.");
        if (FirstAirborneUtc is { } airborneTime && airborneTime <= LoadedUtc)
            throw new InvalidDataException("Flight attempt airborne evidence must follow loading.");
        if (State == FlightAttemptState.CargoLoaded && FirstAirborneUtc is not null)
            throw new InvalidDataException("Cargo-loaded state contains airborne evidence.");
        if (MovementObservedUtc is not null && (FirstAirborneUtc is null || MovementObservedUtc <= FirstAirborneUtc))
            throw new InvalidDataException("Flight movement evidence did not follow airborne evidence.");
        if (State is FlightAttemptState.Departed or FlightAttemptState.EnRoute or FlightAttemptState.ReadyToUnload or FlightAttemptState.Delivered && FirstAirborneUtc is null)
            throw new InvalidDataException("Flight attempt state requires airborne evidence.");
        if (State is FlightAttemptState.EnRoute or FlightAttemptState.ReadyToUnload or FlightAttemptState.Delivered && MovementObservedUtc is null)
            throw new InvalidDataException("Flight attempt state requires movement evidence.");
        if (State is FlightAttemptState.ReadyToUnload or FlightAttemptState.Delivered && DeliveryProof is null)
            throw new InvalidDataException("Flight attempt state requires delivery proof.");
        DeliveryProof?.EnsureValid();
        if (DeliveryProof is { } proof && (proof.EvidenceId != FlightAttemptIds.DeliveryEvidence(ShipmentId) || proof.FirstAirborneUtc != FirstAirborneUtc ||
            proof.MovementObservedUtc != MovementObservedUtc || proof.MaximumDistanceFromOriginNauticalMiles != MaximumDistanceFromOriginNauticalMiles))
            throw new InvalidDataException("Flight delivery proof does not match its attempt.");
        if ((State == FlightAttemptState.Delivered) != (DeliveredUtc is not null))
            throw new InvalidDataException("Flight attempt delivery state is inconsistent.");
        if (DeliveredUtc is not null && DeliveredUtc != DeliveryProof?.ReadyUtc)
            throw new InvalidDataException("Flight attempt delivery time does not match its proof.");
        return this;
    }
}

public static class FlightAttemptIds
{
    public static string Attempt(string shipmentId) => Derive("flight-", shipmentId);
    public static string DeliveryEvidence(string shipmentId) => Derive("delivery-", shipmentId);
    private static string Derive(string prefix, string source)
    {
        EconomyValidation.Id(source, nameof(source)); var candidate = prefix + source;
        if (candidate.Length <= 64) return candidate;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        return prefix + hash[..(64 - prefix.Length)];
    }
}
