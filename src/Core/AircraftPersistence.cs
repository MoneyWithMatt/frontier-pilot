namespace FrontierAirportBuilder.Core;

/// <summary>A durable aircraft position. Id is an opaque prototype identity key; it is
/// deliberately separate from the transient SimConnect object ID.</summary>
public sealed record PersistedAircraftState(string Id, AircraftTelemetry Telemetry)
{
    public PersistedAircraftState EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64) throw new InvalidDataException("Persistent aircraft identity is invalid.");
        return this with { Id = Id.Trim(), Telemetry = Telemetry.EnsureValid() };
    }
}

public interface IAircraftStateStore
{
    string StoragePath { get; }
    Task<PersistedAircraftState?> LoadActiveAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PersistedAircraftState>> LoadAllAsync(CancellationToken cancellationToken = default);
    Task SaveActiveAsync(AircraftTelemetry aircraft, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
