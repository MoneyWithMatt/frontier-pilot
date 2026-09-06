namespace FrontierAirportBuilder.Core;

public sealed record RunwayEnd(GeoPoint Position, double InwardTrueHeadingDegrees, int Number);
public sealed record RunwayGeometry(double TrueHeadingDegrees, RunwayEnd Primary, RunwayEnd Secondary)
{
    public static RunwayGeometry Calculate(AirstripDefinition airstrip)
    {
        ArgumentNullException.ThrowIfNull(airstrip);
        airstrip.EnsureValid();
        var heading = Headings.Normalize(airstrip.TrueHeadingDegrees);
        var center = new GeoPoint(airstrip.Latitude, airstrip.Longitude);
        var primary = Geodesy.Direct(center, Headings.Reciprocal(heading), airstrip.LengthMeters / 2);
        var secondary = Geodesy.Direct(center, heading, airstrip.LengthMeters / 2);
        var number = Headings.RunwayNumber(heading - airstrip.MagneticVariationDegreesEast);
        return new(heading,
            new(primary.Position, Headings.Reciprocal(primary.ForwardBearingDegrees), number),
            new(secondary.Position, Headings.Reciprocal(secondary.ForwardBearingDegrees), Headings.ReciprocalNumber(number)));
    }
}
