namespace FrontierAirportBuilder.Core;

/// <summary>Documented candidates, not an aircraft compatibility certificate. No commands or arbitrary RPN.</summary>
public sealed class StandardConditionProfile : IAircraftConditionProfile
{
    public string Id => "standard-simvars-v0";
    public ConditionIntegrationLevel Level => ConditionIntegrationLevel.Unverified;
    public IReadOnlyList<ConditionField> Fields(ConditionAircraftIdentity aircraft)
    {
        aircraft.Validate();
        var fields = new List<ConditionField>();
        void Add(string name, string unit, double min, double max, ConditionKind kind = ConditionKind.OperatingTelemetry) =>
            fields.Add(new(name, name, unit, kind, min, max));
        void Flag(string name, ConditionKind kind = ConditionKind.OperatingTelemetry) =>
            fields.Add(new(name, name, "bool", kind, 0, 1, true));
        Flag("SIM ON GROUND"); Flag("IS SLEW ACTIVE");
        Add("SIMULATION TIME", "seconds", 0, 1e12);
        Add("SURFACE TYPE", "enum", 0, 255);
        Add("ENGINE TYPE", "number", 0, 32);
        Add("SIMULATION RATE", "number", 0, 128);
        Add("AIRSPEED INDICATED", "knots", 0, 4000); Add("AIRSPEED MACH", "mach", 0, 20);
        Add("REFERENCE SPEED MAX IAS", "knots", 0, 4000);
        Add("G FORCE", "Gforce", -100, 100);
        Add("PLANE TOUCHDOWN NORMAL VELOCITY", "feet per second", -1000, 1000);
        Add("PLANE TOUCHDOWN PITCH DEGREES", "degrees", -180, 180);
        Add("PLANE TOUCHDOWN BANK DEGREES", "degrees", -180, 180);
        Add("PLANE TOUCHDOWN LATITUDE", "degrees", -90, 90);
        Add("PLANE TOUCHDOWN LONGITUDE", "degrees", -180, 180);
        foreach (var name in new[] { "GEAR DAMAGE BY SPEED", "FLAP DAMAGE BY SPEED" }) Flag(name, ConditionKind.ReportedFailure);
        Flag("GEAR SPEED EXCEEDED"); Flag("FLAP SPEED EXCEEDED");
        Add("GEAR TOTAL PCT EXTENDED", "percent over 100", 0, 1);
        Add("FLAPS HANDLE INDEX", "number", 0, 100);
        Add("FLAPS CURRENT SPEED LIMITATION", "knots", 0, 4000);
        Add("FUEL TOTAL QUANTITY", "gallons", 0, 1000000);
        Add("ELECTRICAL MAIN BUS VOLTAGE", "volts", 0, 10000); // Legacy candidate; not a modern bus mapping.
        foreach (var side in new[] { "LEFT", "RIGHT" })
        {
            Add($"BRAKE {side} TEMPERATURE", "kelvin", 0, 5000);
            Add($"BRAKE {side} WEIGHT REMAINING", "percent over 100", 0, 1, ConditionKind.ReportedCondition);
            Add($"BRAKE {side} TEMPERATURE EFFECT", "percent over 100", 0, 1, ConditionKind.ReportedCondition);
        }
        foreach (var name in new[] { "WEAR AND TEAR EXPOSED PARTS LEVEL", "WEAR AND TEAR EXPOSED PARTS LOWEST LEVEL" })
            Add(name, "percent over 100", 0, 1, ConditionKind.ReportedCondition);
        for (var i = 1; i <= aircraft.EngineCount; i++)
        {
            Flag($"GENERAL ENG COMBUSTION:{i}"); Flag($"GENERAL ENG FAILED:{i}", ConditionKind.ReportedFailure);
            Add($"GENERAL ENG RPM:{i}", "rpm", 0, 200000);
            Add($"GENERAL ENG OIL PRESSURE:{i}", "psf", 0, 1000000);
            Add($"GENERAL ENG OIL TEMPERATURE:{i}", "rankine", 0, 5000);
            Add($"GENERAL ENG DAMAGE PERCENT:{i}", "percent", 0, 100, ConditionKind.ReportedCondition);
            Add($"GENERAL ENG ELAPSED TIME:{i}", "hours", 0, 10000000);
            Add($"GENERAL ENG HOBBS ELAPSED TIME:{i}", "seconds", 0, 1e12);
            Add($"GENERAL ENG FUEL PRESSURE:{i}", "psi", 0, 100000);
            Add($"FUELSYSTEM ENGINE PRESSURE:{i}", "kilopascal", 0, 1000000);
            Add($"ENG FUEL FLOW GPH:{i}", "gallons per hour", 0, 1000000);
            Add($"GENERAL ENG THROTTLE LEVER POSITION:{i}", "percent", -100, 100);
            Add($"GENERAL ENG MIXTURE LEVER POSITION:{i}", "percent", 0, 100);
            Add($"ENG CYLINDER HEAD TEMPERATURE:{i}", "rankine", 0, 5000);
            Add($"GENERAL ENG EXHAUST GAS TEMPERATURE:{i}", "rankine", 0, 10000);
            Add($"TURB ENG N1:{i}", "percent", 0, 200);
            Add($"TURB ENG N2:{i}", "percent", 0, 200);
            Add($"TURB ENG ITT:{i}", "rankine", 0, 10000);
            Flag($"GENERAL ENG GENERATOR SWITCH:{i}"); Flag($"GENERAL ENG GENERATOR ACTIVE:{i}");
        }
        return fields;
    }
}
