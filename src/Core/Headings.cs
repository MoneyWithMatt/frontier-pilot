namespace FrontierAirportBuilder.Core;

public static class Headings
{
    public static double Normalize(double degrees)
    {
        if (!double.IsFinite(degrees)) throw new ArgumentOutOfRangeException(nameof(degrees), "Heading must be finite.");
        var value = degrees % 360;
        if (value < 0) value += 360;
        return value == 0 || value == 360 ? 0 : value; // Canonical positive zero.
    }

    public static double Reciprocal(double degrees) => Normalize(Normalize(degrees) + 180);

    // Round magnetic heading to nearest ten degrees; exact 5-degree ties round upward.
    public static int RunwayNumber(double magneticHeadingDegrees)
    {
        var number = (int)Math.Floor((Normalize(magneticHeadingDegrees) + 5) / 10) % 36;
        return number == 0 ? 36 : number;
    }

    public static int ReciprocalNumber(int number)
    {
        if (number is < 1 or > 36) throw new ArgumentOutOfRangeException(nameof(number));
        return ((number + 17) % 36) + 1;
    }
}
