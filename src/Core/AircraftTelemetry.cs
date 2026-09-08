namespace FrontierAirportBuilder.Core;

/// <summary>A read-only observation of the simulator's user aircraft. This deliberately does
/// not represent the walking avatar, camera, or a generic player position.</summary>
public sealed record AircraftTelemetry(
    DateTimeOffset ObservedUtc,
    string AircraftTitle,
    string AircraftType,
    double Latitude,
    double Longitude,
    double AltitudeFeet,
    double TrueHeadingDegrees,
    double GroundSpeedKnots,
    bool IsOnGround)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (ObservedUtc == default) errors.Add("Aircraft observation time is missing.");
        if (!double.IsFinite(Latitude) || Latitude is < -90 or > 90) errors.Add("Aircraft latitude is invalid.");
        if (!double.IsFinite(Longitude) || Longitude is < -180 or > 180) errors.Add("Aircraft longitude is invalid.");
        if (!double.IsFinite(AltitudeFeet) || AltitudeFeet is < -2_000 or > 200_000) errors.Add("Aircraft altitude is invalid.");
        if (!double.IsFinite(TrueHeadingDegrees)) errors.Add("Aircraft heading is invalid.");
        if (!double.IsFinite(GroundSpeedKnots) || GroundSpeedKnots is < 0 or > 10_000) errors.Add("Aircraft ground speed is invalid.");
        if (AircraftTitle.Length > 128) errors.Add("Aircraft title is invalid.");
        if (AircraftType.Length > 128) errors.Add("Aircraft type is invalid.");
        return errors;
    }

    public AircraftTelemetry EnsureValid()
    {
        var errors = Validate();
        if (errors.Count != 0) throw new InvalidDataException(string.Join(" ", errors));
        return this with
        {
            AircraftTitle = string.IsNullOrWhiteSpace(AircraftTitle) ? "Unknown aircraft" : AircraftTitle.Trim(),
            AircraftType = AircraftType.Trim(),
            TrueHeadingDegrees = Headings.Normalize(TrueHeadingDegrees),
            GroundSpeedKnots = Math.Max(0, GroundSpeedKnots)
        };
    }
}

public enum PlayerControlMode { Unknown, InAircraft, OnFoot }

/// <summary>A position reported by the explicit MSFS user-avatar proxy. It is never
/// synthesized from the aircraft or camera.</summary>
public sealed record PlayerTelemetry(
    DateTimeOffset ObservedUtc,
    uint ObjectId,
    double Latitude,
    double Longitude,
    double AltitudeFeet,
    double TrueHeadingDegrees,
    bool IsOnGround)
{
    public PlayerTelemetry EnsureValid()
    {
        if (ObservedUtc == default || ObjectId == 0 ||
            !double.IsFinite(Latitude) || Latitude is < -90 or > 90 ||
            !double.IsFinite(Longitude) || Longitude is < -180 or > 180 ||
            !double.IsFinite(AltitudeFeet) || AltitudeFeet is < -2_000 or > 200_000 ||
            !double.IsFinite(TrueHeadingDegrees))
            throw new InvalidDataException("Avatar telemetry is invalid.");
        return this with { TrueHeadingDegrees = Headings.Normalize(TrueHeadingDegrees) };
    }
}

/// <summary>Independent aircraft and player world state. Player is populated only when
/// the current-controlled-object identity proves that the explicit avatar is controlled.</summary>
public sealed record TrackingWorldState(
    AircraftTelemetry Aircraft,
    uint AircraftObjectId,
    uint CurrentObjectId,
    uint? AvatarObjectId,
    PlayerControlMode PlayerMode,
    PlayerTelemetry? Player,
    string PlayerTrackingStatus,
    string IntegrationDetails = "")
{
    public double? DistanceFromAircraftMeters => Player is null ? null :
        WorldTracking.DistanceMeters(Aircraft.Latitude, Aircraft.Longitude, Player.Latitude, Player.Longitude);

    public TrackingWorldState EnsureValid()
    {
        var aircraft = Aircraft.EnsureValid();
        if (AircraftObjectId == 0) throw new InvalidDataException("MSFS did not identify the aircraft object.");
        if (PlayerMode == PlayerControlMode.OnFoot)
        {
            var player = Player?.EnsureValid() ?? throw new InvalidDataException("On-foot state has no avatar position.");
            if (AvatarObjectId != player.ObjectId || CurrentObjectId != player.ObjectId || player.ObjectId == AircraftObjectId)
                throw new InvalidDataException("Avatar identity does not agree with the current controlled object.");
            return this with { Aircraft = aircraft, Player = player };
        }
        if (Player is not null) throw new InvalidDataException("An unverified avatar position must not be exposed as player position.");
        if (PlayerMode == PlayerControlMode.InAircraft && CurrentObjectId != AircraftObjectId)
            throw new InvalidDataException("In-aircraft state does not match the current controlled object.");
        return this with { Aircraft = aircraft };
    }
}

public interface ITrackingTelemetrySource
{
    Task RunAsync(string sdkRoot, IProgress<TrackingWorldState> updates, CancellationToken cancellationToken);
}

public static class WorldTracking
{
    private const double EarthRadiusMeters = 6_371_008.8;

    public static double DistanceMeters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        foreach (var value in new[] { latitude1, longitude1, latitude2, longitude2 })
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(latitude1));
        var radians = Math.PI / 180;
        var dLat = (latitude2 - latitude1) * radians;
        var dLon = (longitude2 - longitude1) * radians;
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(latitude1 * radians) * Math.Cos(latitude2 * radians) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    public static double HeadingDifference(double first, double second) =>
        Math.Abs((Headings.Normalize(first) - Headings.Normalize(second) + 540) % 360 - 180);
}
