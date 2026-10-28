namespace FrontierAirportBuilder.Core;

public enum ConditionKind { OperatingTelemetry, ReportedCondition, ReportedFailure, EstimatedCondition }
public enum ConditionAvailability { ReceivedUnverified, Unavailable, Invalid }
public enum ConditionIntegrationLevel { Unverified, MonitoringOnly, Partial, Full }

/// <summary>Transport receipt is deliberately not empirical compatibility qualification.</summary>
public sealed record ConditionField(string Key, string SimVar, string Unit, ConditionKind Kind,
    double Minimum, double Maximum, bool Boolean = false)
{
    public ConditionReading Observe(double value, DateTimeOffset utc) =>
        double.IsFinite(value) && value >= Minimum && value <= Maximum && (!Boolean || value is 0 or 1)
            ? new(this, utc, ConditionAvailability.ReceivedUnverified, value, "Receipt only; aircraft semantics unverified.")
            : new(this, utc, ConditionAvailability.Invalid, null, "Non-finite, out-of-range or invalid Boolean reading.");
    public ConditionReading Missing(DateTimeOffset utc, string reason) => new(this, utc, ConditionAvailability.Unavailable, null, reason);
}

public sealed record ConditionReading(ConditionField Field, DateTimeOffset ObservedUtc,
    ConditionAvailability Availability, double? Value, string Diagnostic);
public sealed record ConditionAircraftIdentity(uint ObjectId, string Title, string Model, int EngineCount)
{
    public void Validate()
    {
        if (ObjectId == 0 || string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Model) ||
            Title.Length > 128 || Model.Length > 128 || EngineCount is < 0 or > 16)
            throw new InvalidDataException("Aircraft condition identity is unavailable or invalid.");
    }
}
public sealed record ConditionFrame(DateTimeOffset StartedUtc, DateTimeOffset CompletedUtc,
    ConditionAircraftIdentity Aircraft, string AdapterId, IReadOnlyList<ConditionReading> Readings)
{
    public ConditionRuntimeState? Runtime { get; init; }
}
public sealed record ConditionRuntimeState(bool? Running, bool? Paused, long Discontinuity);
public sealed record ConditionConnectionInfo(string SdkVersion, string SimulatorVersion, string SimConnectSha256);

/// <summary>No mutation operations: custom adapters must preserve this observation-only boundary.</summary>
public interface IAircraftConditionSource
{
    ConditionConnectionInfo ConnectionInfo { get; }
    IAsyncEnumerable<ConditionFrame> ReadAsync(CancellationToken cancellationToken);
}

public interface IAircraftConditionProfile
{
    string Id { get; }
    ConditionIntegrationLevel Level { get; }
    IReadOnlyList<ConditionField> Fields(ConditionAircraftIdentity aircraft);
}

/// <summary>Individual identity is explicitly assigned, never derived from TITLE/model/object ID.</summary>
public sealed record AircraftConditionRecord(int SchemaVersion, Guid AirframeId, string Label,
    string SimulatorTitle, string SimulatorModel, string PackageVersion,
    double? AirframeHours, long? LandingCycles, IReadOnlyList<AircraftComponentCondition> Components)
{
    public static AircraftConditionRecord Create(string label, string title, string model, string packageVersion) =>
        new(1, Guid.NewGuid(), label, title, model, packageVersion, null, null, []);

    public void Validate()
    {
        if (SchemaVersion != 1 || AirframeId == Guid.Empty ||
            new[] { Label, SimulatorTitle, SimulatorModel, PackageVersion }.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 256) ||
            AirframeHours is { } hours && (!double.IsFinite(hours) || hours < 0) || LandingCycles < 0 || Components is null)
            throw new InvalidDataException("Invalid or unsupported aircraft condition record; preserve the original.");
        if (Components.Count > 256 || Components.Any(c => c is null) || Components.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() != Components.Count)
            throw new InvalidDataException("Invalid component identities.");
        foreach (var component in Components) component.Validate();
    }
    public bool Matches(ConditionAircraftIdentity aircraft) =>
        string.Equals(SimulatorTitle, aircraft.Title, StringComparison.Ordinal) &&
        string.Equals(SimulatorModel, aircraft.Model, StringComparison.Ordinal);
}

/// <summary>Frontier estimates only. Native observations remain in diagnostic recordings.</summary>
public sealed record AircraftComponentCondition(string Key, string Type, double? Hours, double? EstimatedHealth,
    IReadOnlyList<string> FaultIds)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key) || Key.Length > 128 || string.IsNullOrWhiteSpace(Type) || Type.Length > 128 ||
            Hours is { } h && (!double.IsFinite(h) || h < 0) ||
            EstimatedHealth is { } health && (!double.IsFinite(health) || health is < 0 or > 1) ||
            FaultIds is null || FaultIds.Count > 256 || FaultIds.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 128) ||
            FaultIds.Distinct(StringComparer.Ordinal).Count() != FaultIds.Count)
            throw new InvalidDataException("Invalid Frontier component estimate.");
    }
}
