namespace FrontierAirportBuilder.Core;

/// <summary>Existing read-only condition transport supplies all flight/runtime/freeze fields. No setters or pilot controls.</summary>
public sealed class OffshoreFlightProfile : IAircraftConditionProfile
{
    public string Id => "offshore-flight-objectives-v1";
    public ConditionIntegrationLevel Level => ConditionIntegrationLevel.Unverified;
    public IReadOnlyList<ConditionField> Fields(ConditionAircraftIdentity aircraft)
    {
        aircraft.Validate();
        var fields = new List<ConditionField>();
        foreach (var (key, unit, min, max) in new[] {
            ("PLANE LATITUDE", "degrees", -90d, 90d), ("PLANE LONGITUDE", "degrees", -180d, 180d),
            ("PLANE ALTITUDE", "feet", -2000d, 200000d), ("GROUND ALTITUDE", "feet", -2000d, 100000d),
            ("GROUND VELOCITY", "knots", 0d, 10000d), ("VERTICAL SPEED", "feet per second", -10000d, 10000d),
            ("PLANE HEADING DEGREES TRUE", "degrees", 0d, 360d),
            ("SIMULATION TIME", "seconds", 0d, 1e12), ("SIMULATION RATE", "number", 0d, 128d) })
            fields.Add(new(key, key, unit, ConditionKind.OperatingTelemetry, min, max));
        foreach (var name in new[] { "SIM ON GROUND", "IS SLEW ACTIVE", "IS LATITUDE LONGITUDE FREEZE ON", "IS ALTITUDE FREEZE ON", "IS ATTITUDE FREEZE ON" })
            fields.Add(new(name, name, "bool", ConditionKind.OperatingTelemetry, 0, 1, true));
        return fields;
    }
}
public sealed record OffshoreFlightSample(ConditionAircraftIdentity Identity, FlightPosition Position, double SimulationSeconds,
    double GroundAltitudeFeet, double VerticalSpeedFeetPerSecond, long RuntimeDiscontinuity)
{
    public static OffshoreFlightSample? From(ConditionFrame? frame, DateTimeOffset now)
    {
        if (frame is null || frame.Runtime is not { Running: true, Paused: false } || frame.StartedUtc == default ||
            frame.CompletedUtc < frame.StartedUtc || frame.CompletedUtc - frame.StartedUtc > TimeSpan.FromSeconds(1) ||
            now < frame.CompletedUtc || now - frame.StartedUtc > TimeSpan.FromSeconds(2) || frame.AdapterId != new OffshoreFlightProfile().Id) return null;
        try { frame.Aircraft.Validate(); } catch (InvalidDataException) { return null; }
        var expected = new OffshoreFlightProfile().Fields(frame.Aircraft);
        if (frame.Readings.Count != expected.Count || frame.Readings.Select(r => r.Field.Key).Distinct().Count() != expected.Count) return null;
        var values = new Dictionary<string, double>();
        foreach (var field in expected)
        {
            var r = frame.Readings.SingleOrDefault(r => r.Field.Key == field.Key);
            if (r is null || r.Field != field || r.Availability != ConditionAvailability.ReceivedUnverified || r.Value is not {} value ||
                r.ObservedUtc < frame.StartedUtc || r.ObservedUtc > frame.CompletedUtc ||
                field.Observe(value, r.ObservedUtc).Availability != ConditionAvailability.ReceivedUnverified) return null;
            values.Add(field.Key, value);
        }
        if (values["SIMULATION RATE"] != 1 || new[] { "IS SLEW ACTIVE", "IS LATITUDE LONGITUDE FREEZE ON", "IS ALTITUDE FREEZE ON", "IS ATTITUDE FREEZE ON" }.Any(n => values[n] != 0)) return null;
        return new(frame.Aircraft, new(frame.CompletedUtc, values["PLANE LATITUDE"], values["PLANE LONGITUDE"], values["PLANE ALTITUDE"], values["PLANE HEADING DEGREES TRUE"],
            values["GROUND VELOCITY"], values["SIM ON GROUND"] == 1), values["SIMULATION TIME"], values["GROUND ALTITUDE"],
            values["VERTICAL SPEED"], frame.Runtime.Discontinuity);
    }
}
