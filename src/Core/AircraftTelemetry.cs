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

public interface IAircraftTelemetrySource
{
    Task RunAsync(string sdkRoot, IProgress<AircraftTelemetry> updates, CancellationToken cancellationToken);
}
