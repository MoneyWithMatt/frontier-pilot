using System.Text.RegularExpressions;

namespace FrontierAirportBuilder.Core;

// Dirt is the experimental Namibia branch; Asphalt retains the original FPT1 output.
public enum RunwaySurface { Asphalt, Dirt }

public sealed record AirstripDefinition
{
    public required string Name { get; init; }
    public required string Identifier { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required double AltitudeMeters { get; init; }
    public required double TrueHeadingDegrees { get; init; }
    public required double MagneticVariationDegreesEast { get; init; }
    public required double LengthMeters { get; init; }
    public required double WidthMeters { get; init; }
    public required RunwaySurface Surface { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 48 || Name != Name.Trim() ||
            Name.Any(c => char.IsControl(c) || char.IsSurrogate(c) || c is '\uFFFE' or '\uFFFF'))
            errors.Add("Name must be 1–48 printable BMP characters, without surrounding whitespace.");
        if (Identifier is null || !Regex.IsMatch(Identifier, "\\A[A-Z][A-Z0-9]{3}\\z", RegexOptions.CultureInvariant))
            errors.Add("Identifier must be four uppercase ASCII letters/digits, starting with a letter.");
        if (!double.IsFinite(Latitude) || Latitude <= -90 || Latitude >= 90)
            errors.Add("Latitude must be finite and strictly between -90 and 90 (polar heading is undefined).");
        CheckRange(Longitude, -180, 180, nameof(Longitude), errors);
        CheckRange(AltitudeMeters, -500, 9000, nameof(AltitudeMeters), errors);
        if (!double.IsFinite(TrueHeadingDegrees)) errors.Add("TrueHeadingDegrees must be finite.");
        CheckRange(MagneticVariationDegreesEast, -180, 180, nameof(MagneticVariationDegreesEast), errors);
        CheckRange(LengthMeters, 10, 10000, nameof(LengthMeters), errors);
        CheckRange(WidthMeters, 1, 500, nameof(WidthMeters), errors);
        if (WidthMeters >= LengthMeters) errors.Add("WidthMeters must be smaller than LengthMeters.");
        if (!Enum.IsDefined(Surface)) errors.Add("Unsupported runway surface.");
        return errors;
    }

    public void EnsureValid()
    {
        var errors = Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
    }

    private static void CheckRange(double value, double minimum, double maximum, string name, List<string> errors)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            errors.Add($"{name} must be finite and in [{minimum}, {maximum}].");
    }
}
