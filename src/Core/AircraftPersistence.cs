namespace FrontierAirportBuilder.Core;

/// <summary>A durable aircraft position. Id is an opaque prototype identity key; it is
/// deliberately separate from the transient SimConnect object ID.</summary>
public enum AircraftParkingState { NotAtStand, NearStand, Parked }

/// <summary>Derived Frontier-world context recorded alongside coordinates. Coordinates remain authoritative.</summary>
public sealed record AircraftLocationContext(DateTimeOffset ObservedUtc, string AirportIdentifier, string AirportName,
    int? ParkingStandNumber, string? ParkingStandName, double AirportInfrastructureDistanceMeters,
    double? ParkingStandDistanceMeters, AircraftParkingState ParkingState, string Reason)
{
    public AircraftLocationContext EnsureValid()
    {
        if(ObservedUtc==default || AirportIdentifier is null || !System.Text.RegularExpressions.Regex.IsMatch(AirportIdentifier,"\\A[A-Z][A-Z0-9]{2,7}\\z") ||
           string.IsNullOrWhiteSpace(AirportName) || AirportName.Length>48 ||
           !double.IsFinite(AirportInfrastructureDistanceMeters) || AirportInfrastructureDistanceMeters<0 ||
           ParkingStandNumber is <1 or >99 ||
           ParkingStandDistanceMeters is {} distance && (!double.IsFinite(distance)||distance<0) ||
           ParkingStandNumber==null && ParkingState!=AircraftParkingState.NotAtStand ||
           ParkingStandNumber==null && ParkingStandName!=null ||
           ParkingStandNumber!=null && (ParkingState==AircraftParkingState.NotAtStand||ParkingStandDistanceMeters==null) ||
           ParkingStandNumber!=null && (string.IsNullOrWhiteSpace(ParkingStandName)||ParkingStandName.Length>64) ||
           string.IsNullOrWhiteSpace(Reason) || Reason.Length>256)
            throw new InvalidDataException("Persistent Frontier airport location metadata is invalid.");
        return this with {AirportIdentifier=AirportIdentifier.Trim(),AirportName=AirportName.Trim(),ParkingStandName=ParkingStandName?.Trim(),Reason=Reason.Trim()};
    }
}

public sealed record PersistedAircraftState(string Id, AircraftTelemetry Telemetry, AircraftLocationContext? Location = null)
{
    public PersistedAircraftState EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64) throw new InvalidDataException("Persistent aircraft identity is invalid.");
        var telemetry=Telemetry.EnsureValid();
        var location=Location?.EnsureValid();
        if(location is not null && location.ObservedUtc!=telemetry.ObservedUtc)
            throw new InvalidDataException("Persistent aircraft position and location metadata timestamps do not agree.");
        return this with { Id = Id.Trim(), Telemetry = telemetry, Location = location };
    }
}

public interface IAircraftStateStore
{
    string StoragePath { get; }
    Task<PersistedAircraftState?> LoadActiveAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PersistedAircraftState>> LoadAllAsync(CancellationToken cancellationToken = default);
    Task SaveActiveAsync(AircraftTelemetry aircraft, AircraftLocationContext? location = null, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
