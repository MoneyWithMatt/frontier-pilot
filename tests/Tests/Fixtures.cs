using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Tests;

internal static class Fixtures
{
    public static AirstripDefinition Strip => new()
    {
        Name = "Frontier Test Strip", Identifier = "FPT1", Latitude = 25.035986003223783, Longitude = -77.46942558230664,
        AltitudeMeters = 4.1148001316736, TrueHeadingDegrees = 89.64241865029217, MagneticVariationDegreesEast = -6,
        LengthMeters = 2529.3122612096568, WidthMeters = 46.85408462689318, Surface = RunwaySurface.Asphalt
    };
}

public abstract class TemporaryDirectoryTest : IDisposable
{
    protected string Root { get; } = Path.Combine(Path.GetTempPath(), "FrontierAirportBuilderTests", Guid.NewGuid().ToString("N"));
    protected TemporaryDirectoryTest() => Directory.CreateDirectory(Root);
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}
