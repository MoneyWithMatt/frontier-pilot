namespace FrontierAirportBuilder.Core;

/// <summary>Pure geometry/motion predicate only. No diagnostic record, flight evidence, dwell, mission or payment authority.</summary>
public static class DeckSurfaceContact
{
    public static bool WithinEnvelope(double distanceMeters, double referenceHeightMeters, double surfaceErrorMeters, double radiusMeters) =>
        double.IsFinite(distanceMeters) && double.IsFinite(referenceHeightMeters) && double.IsFinite(surfaceErrorMeters) &&
        distanceMeters >= 0 && distanceMeters <= radiusMeters && referenceHeightMeters is >= .3 and <= 1.5 && surfaceErrorMeters is >= 0 and <= .5;
    public static bool StableMotion(double groundSpeedKnots, double verticalSpeedFeetPerSecond, double bankDegrees, double pitchDegrees) =>
        double.IsFinite(groundSpeedKnots) && double.IsFinite(verticalSpeedFeetPerSecond) && double.IsFinite(bankDegrees) && double.IsFinite(pitchDegrees) &&
        groundSpeedKnots is >= 0 and <= .5 && Math.Abs(verticalSpeedFeetPerSecond) <= .1 && Math.Abs(bankDegrees) <= 5 && Math.Abs(pitchDegrees) <= 5;
}
