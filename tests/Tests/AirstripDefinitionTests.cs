using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Tests;

public sealed class AirstripDefinitionTests
{
    [Fact] public void ProvenDefinitionIsValid() => Assert.Empty(Fixtures.Strip.Validate());

    public static IEnumerable<object[]> InvalidFields()
    {
        var d = Fixtures.Strip;
        foreach (var name in new[] { "", " ", " spaced", "trailing ", new string('a', 49), "newline\n", "bad\u0000", "bad\uFFFE", "bad\ud800" })
            yield return new object[] { d with { Name = name }, "Name" };
        yield return new object[] { d with { Name = null! }, "Name" };
        foreach (var id in new[] { "", "FP1", "FPT12", "fpt1", "1FPT", "F../", "F P1", "FPT1\n", "ÉPT1" })
            yield return new object[] { d with { Identifier = id }, "Identifier" };
        yield return new object[] { d with { Identifier = null! }, "Identifier" };
        foreach (var value in new[] { -90d, 90d, -91d, 91d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            yield return new object[] { d with { Latitude = value }, "Latitude" };
        foreach (var value in new[] { -180.001, 180.001, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            yield return new object[] { d with { Longitude = value }, "Longitude" };
        foreach (var value in new[] { -501d, 9001d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            yield return new object[] { d with { AltitudeMeters = value }, "AltitudeMeters" };
        foreach (var value in new[] { 0d, -1d, 9.999, 10000.01, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            yield return new object[] { d with { LengthMeters = value }, "LengthMeters" };
        foreach (var value in new[] { 0d, -1d, 0.99, 500.01, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            yield return new object[] { d with { WidthMeters = value }, "WidthMeters" };
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            yield return new object[] { d with { TrueHeadingDegrees = value }, "TrueHeadingDegrees" };
        foreach (var value in new[] { -180.01, 180.01, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            yield return new object[] { d with { MagneticVariationDegreesEast = value }, "MagneticVariationDegreesEast" };
        yield return new object[] { d with { Surface = (RunwaySurface)42 }, "surface" };
        yield return new object[] { d with { LengthMeters = 50, WidthMeters = 50 }, "WidthMeters" };
        yield return new object[] { d with { LengthMeters = 50, WidthMeters = 51 }, "WidthMeters" };
    }

    [Theory, MemberData(nameof(InvalidFields))]
    public void InvalidDefinitionReportsFieldAndRejectsGeometry(AirstripDefinition definition, string field)
    {
        Assert.Contains(definition.Validate(), error => error.Contains(field, StringComparison.OrdinalIgnoreCase));
        Assert.Throws<ArgumentException>(() => definition.EnsureValid());
        Assert.Throws<ArgumentException>(() => RunwayGeometry.Calculate(definition));
    }

    [Fact]
    public void DocumentedBoundariesAndEscapableNamesAreAccepted()
    {
        Assert.Empty((Fixtures.Strip with { Name = "A & B <Airstrip> \"é\"", Latitude = 89.9999, Longitude = 180, AltitudeMeters = -500,
            LengthMeters = 10, WidthMeters = 1, MagneticVariationDegreesEast = -180, TrueHeadingDegrees = -720 }).Validate());
        Assert.Empty((Fixtures.Strip with { Name = new string('a', 48), Latitude = -89.9999, Longitude = -180, AltitudeMeters = 9000,
            LengthMeters = 10000, WidthMeters = 500, MagneticVariationDegreesEast = 180, TrueHeadingDegrees = 720 }).Validate());
    }

    [Fact] public void MultipleErrorsAreCollected() => Assert.True((Fixtures.Strip with { Name = "", Identifier = "", WidthMeters = -1 }).Validate().Count >= 3);
}
