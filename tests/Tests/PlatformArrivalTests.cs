using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class PlatformArrivalTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Airframe = Guid.NewGuid();
    private static OffshorePlatform Qualified => OffshorePlatformRegistry.Find("ol400-h01") with
    {
        Stage = PlatformQualificationStage.MissionDestination,
        Evidence = [new("SYNTHETIC ONLY", "test-sim", "test-scenery", "H125 Passengers", "H125", PlatformQualificationStage.MissionDestination,
            [PlatformOperation.InspectionVisit]) { StaticDeckContact = true, GenuineDepartureAndReturn = true }]
    };
    private static FlightAttempt Flight => new("attempt-test", "job-test", "shipment-test", "fywb-test", "ol400-h01", FlightAttemptState.CargoLoaded,
        Epoch, new("H125 Passengers", "H125"), new(Epoch, -22.977764792740345, 14.64223399758339, 300, 0, 0, true),
        RequiredMaximumDistanceNauticalMiles: 4);
    private static ConditionFrame Frame(int second, bool deck = false, bool water = false, bool hover = false)
    {
        var profile = new PlatformMissionFlightProfile(); var identity = new ConditionAircraftIdentity(42, "H125 Passengers", "H125", 1);
        var fraction = Math.Clamp((second - 11) / 119d, 0, 1);
        var atBase = second <= 10; var now = Epoch.AddSeconds(second);
        var lat = atBase ? Flight.LoadedPosition.Latitude : Flight.LoadedPosition.Latitude + (Qualified.Latitude - Flight.LoadedPosition.Latitude) * fraction;
        var lon = atBase ? Flight.LoadedPosition.Longitude : Flight.LoadedPosition.Longitude + (Qualified.Longitude - Flight.LoadedPosition.Longitude) * fraction;
        var deckFeet = Qualified.DeckMeters / .3048;
        var values = new Dictionary<string, double>
        {
            ["PLANE LATITUDE"] = deck ? Qualified.Latitude : lat, ["PLANE LONGITUDE"] = deck ? Qualified.Longitude : lon,
            ["PLANE ALTITUDE"] = water ? 1 : deck ? deckFeet + (hover ? 3 : 2.46) : 300 + (deckFeet + 2.46 - 300) * fraction,
            ["GROUND ALTITUDE"] = water ? 0 : atBase ? 300 : deckFeet,
            ["GROUND VELOCITY"] = atBase || deck ? 0 : 240, ["VERTICAL SPEED"] = atBase || deck ? 0 : -2,
            ["SIM ON GROUND"] = (atBase || deck) && !hover ? 1 : 0,
            ["CONTACT POINT IS ON GROUND:8"] = (atBase || deck) && !hover ? 1 : 0,
            ["SIMULATION RATE"] = 1, ["SIMULATION TIME"] = second
        };
        return new(now, now, identity, profile.Id, profile.Fields(identity).Select(f => f.Observe(values.GetValueOrDefault(f.Key), now)).ToArray())
        { Runtime = new(true, false, 0) };
    }
    private static FlightAttempt Observe(FlightAttemptTracker.PlatformArrivalMonitor m, ConditionFrame frame, FlightAttempt? flight = null, OffshorePlatform? platform = null) =>
        m.Observe(platform ?? Qualified, PlatformOperation.InspectionVisit, "test-sim", "test-scenery", Airframe, Airframe, flight ?? Flight, frame, frame.CompletedUtc);

    [Fact]
    public void OnlyAQualifiedOperationAndOwnNormalOutboundFlightCanProducePlatformArrival()
    {
        var monitor = new FlightAttemptTracker.PlatformArrivalMonitor(); var flight = Flight;
        for (int i = 0; i <= 130; i++) flight = Observe(monitor, Frame(i), flight);
        for (int i = 131; i < 151; i++) flight = Observe(monitor, Frame(i, true), flight);
        Assert.Null(flight.DeliveryProof);
        flight = Observe(monitor, Frame(151, true), flight);
        Assert.NotNull(flight.DeliveryProof); Assert.Equal(TimeSpan.FromSeconds(119).Ticks, flight.VerifiedAirborneTicks);
        Assert.Same(flight, Observe(monitor, Frame(152, true), flight)); // immutable once-only proof, no payment here
    }
    [Theory]
    [InlineData("static")]
    [InlineData("water")]
    [InlineData("hover")]
    [InlineData("diagnostic")]
    [InlineData("frozen")]
    [InlineData("teleport")]
    public void StaticWaterHoverDiagnosticFrozenAndRepositionedStatesCannotQualify(string problem)
    {
        var monitor = new FlightAttemptTracker.PlatformArrivalMonitor(); var flight = Flight;
        if (problem != "static")
            for (int i = 0; i <= (problem == "teleport" ? 11 : 130); i++) flight = Observe(monitor, Frame(i), flight);
        for (int i = 131; i <= 160; i++)
        {
            var frame = Frame(i, true, problem == "water", problem == "hover");
            if (problem == "diagnostic") frame = frame with { AdapterId = "experimental-ol400-h125-contact-v1" };
            if (problem == "frozen") frame = frame with { Readings = frame.Readings.Select(r => r.Field.Key == "IS ATTITUDE FREEZE ON" ? r.Field.Observe(1, r.ObservedUtc) : r).ToArray() };
            flight = Observe(monitor, frame, flight);
        }
        Assert.Null(flight.DeliveryProof);
    }
    [Fact]
    public void LiveStaticRegistryRemainsBlockedEvenWithSyntheticFullFlight()
    {
        var monitor = new FlightAttemptTracker.PlatformArrivalMonitor(); var flight = Flight;
        for (int i = 0; i <= 160; i++) flight = Observe(monitor, Frame(i, i >= 131), flight, OffshorePlatformRegistry.Find("ol400-h01"));
        Assert.Null(flight.DeliveryProof);
    }
}
