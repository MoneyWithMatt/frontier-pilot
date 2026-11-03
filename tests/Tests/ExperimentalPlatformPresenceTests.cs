using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Tests;

public sealed class ExperimentalPlatformPresenceTests
{
    private static readonly ConditionAircraftIdentity Aircraft = new(42, "H125 Passengers", "synthetic-h125-model", 1);
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ConditionFrame Frame(int second, params (string Name, double Value)[] changes)
    {
        var data = new Dictionary<string, double>
        {
            ["PLANE LATITUDE"] = ExperimentalPlatformContactProfile.Latitude,
            ["PLANE LONGITUDE"] = ExperimentalPlatformContactProfile.Longitude,
            ["PLANE ALTITUDE"] = (ExperimentalPlatformContactProfile.DeckMeters + .75) / .3048,
            ["GROUND ALTITUDE"] = ExperimentalPlatformContactProfile.DeckMeters / .3048,
            ["SIM ON GROUND"] = 1, ["IS SLEW ACTIVE"] = 0,
            ["SIMULATION TIME"] = second, ["SIMULATION RATE"] = 1,
            ["CONTACT POINT IS ON GROUND:8"] = 1
        };
        foreach (var change in changes) data[change.Name] = change.Value;
        var profile = new ExperimentalPlatformContactProfile();
        var start = Epoch.AddSeconds(second);
        return new(start, start.AddSeconds(.1), Aircraft, profile.Id,
            profile.Fields(Aircraft).Select(f => f.Observe(data.GetValueOrDefault(f.Key), start)).ToArray())
        { Runtime = new(true, false, 0) };
    }

    private static PlatformPresenceStatus Observe(ExperimentalPlatformPresence tracker, ConditionFrame frame) =>
        tracker.Observe(frame, frame.CompletedUtc);

    private static ExperimentalPlatformPresence Stable()
    {
        var tracker = new ExperimentalPlatformPresence();
        for (var second = 0; second <= 21; second++) Observe(tracker, Frame(second));
        Assert.True(tracker.Current.DiagnosticDeckPresence);
        return tracker;
    }

    [Fact]
    public void NativeStartCanEstablishDiagnosticPresenceWithoutInventingAFlight()
    {
        var tracker = new ExperimentalPlatformPresence();
        for (var second = 0; second < 20; second++)
            Assert.False(Observe(tracker, Frame(second)).DiagnosticDeckPresence);
        var status = Observe(tracker, Frame(20));
        Assert.True(status.DiagnosticDeckPresence);
        Assert.Equal(20, status.ContinuousContactSeconds);
        Assert.Contains("No flight, delivery or payment evidence", status.Reason);
        Assert.Equal(ConditionIntegrationLevel.Unverified, new ExperimentalPlatformContactProfile().Level);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-3)]
    public void WaterAtIdenticalLatitudeLongitudeAndWithContactFlagsNeverQualifies(double seaAltitudeFeet)
    {
        var tracker = new ExperimentalPlatformPresence();
        for (var second = 0; second <= 25; second++)
        {
            var status = Observe(tracker, Frame(second, ("PLANE ALTITUDE", seaAltitudeFeet), ("GROUND ALTITUDE", 0)));
            Assert.Equal(PlatformPresenceStage.OffPlatform, status.Stage);
            Assert.False(status.DiagnosticDeckPresence);
            Assert.Equal(0d, status.DistanceMeters);
        }
    }

    [Theory]
    [InlineData("SIM ON GROUND", 0)]
    [InlineData("CONTACT POINT IS ON GROUND:8", 0)]
    [InlineData("GROUND VELOCITY", .6)]
    [InlineData("VERTICAL SPEED", .2)]
    [InlineData("PLANE BANK DEGREES", 6)]
    [InlineData("PLANE PITCH DEGREES", 6)]
    [InlineData("IS SLEW ACTIVE", 1)]
    [InlineData("SIMULATION RATE", 2)]
    [InlineData("PLANE LONGITUDE", 14.5048)]
    [InlineData("GROUND ALTITUDE", 0)]
    public void ContactMotionGeometryAndSlewRevokeCurrentPresence(string field, double value)
    {
        var tracker = Stable();
        Assert.False(Observe(tracker, Frame(22, (field, value))).DiagnosticDeckPresence);
        Assert.Equal(0, tracker.Current.ContinuousContactSeconds);
        Assert.False(Observe(tracker, Frame(23)).DiagnosticDeckPresence);
    }

    [Theory]
    [InlineData("duplicate-time")]
    [InlineData("backward-time")]
    [InlineData("gap")]
    [InlineData("discontinuity")]
    [InlineData("aircraft")]
    [InlineData("paused")]
    [InlineData("stopped")]
    public void ClockRuntimeAndIdentityInterruptionsResetDwell(string reason)
    {
        var tracker = Stable();
        var frame = reason switch
        {
            "duplicate-time" => Frame(22, ("SIMULATION TIME", 21)),
            "backward-time" => Frame(22, ("SIMULATION TIME", 0)),
            "gap" => Frame(30),
            "discontinuity" => Frame(22) with { Runtime = new(true, false, 1) },
            "aircraft" => Frame(22) with { Aircraft = Aircraft with { ObjectId = 43 } },
            "paused" => Frame(22) with { Runtime = new(true, true, 0) },
            _ => Frame(22) with { Runtime = new(false, false, 0) }
        };
        Assert.False(Observe(tracker, frame).DiagnosticDeckPresence);
        Assert.Equal(0, tracker.Current.ContinuousContactSeconds);
    }

    [Fact]
    public void MissingInvalidWrongUnitAndStaleReadingsFailClosed()
    {
        var frame = Frame(22);
        var cases = new[]
        {
            frame with { Readings = frame.Readings.Skip(1).ToArray() },
            frame with { Readings = frame.Readings.Select((r,i) => i == 0 ? r.Field.Missing(r.ObservedUtc,"unsupported") : r).ToArray() },
            frame with { Readings = frame.Readings.Select((r,i) => i == 0 ? r with { Value = double.NaN } : r).ToArray() },
            frame with { Readings = frame.Readings.Select((r,i) => i == 0 ? r with { Field = r.Field with { Unit = "feet" } } : r).ToArray() },
            frame with { Readings = frame.Readings.Select((r,i) => i == 0 ? r with { ObservedUtc = Epoch } : r).ToArray() },
            frame with { AdapterId = "some-other-profile" }
        };
        foreach (var invalid in cases)
            Assert.Equal(PlatformPresenceStage.Unavailable, Observe(Stable(), invalid).Stage);
        Assert.False(Stable().Observe(frame, frame.CompletedUtc.AddSeconds(3)).DiagnosticDeckPresence);
        Assert.False(Stable().Observe(null, frame.CompletedUtc).DiagnosticDeckPresence);
    }

    [Fact]
    public void ProfilesDoNotInferSupportForOtherAircraft()
    {
        var other = Aircraft with { Title = "Caravan" };
        Assert.Throws<InvalidDataException>(() => new ExperimentalPlatformContactProfile().Fields(other));
        Assert.False(Observe(Stable(), Frame(22) with { Aircraft = other }).DiagnosticDeckPresence);
    }
}
