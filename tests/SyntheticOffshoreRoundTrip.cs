using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.TestFixtures;

/// <summary>Invented native frames for unit/WPF tests, never simulator acceptance.</summary>
public static class SyntheticOffshoreRoundTrip
{
    public static readonly DateTimeOffset Epoch = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    public static OffshorePlatform Platform => OffshorePlatformRegistry.Find("ol400-h01");
    public static PassengerEndpoint Origin => new("fywb-test", "SYNTHETIC Walvis Bay", -22.977764792740345, 14.64223399758339);
    public static ConditionFrame Frame(int second, string? problem = null)
    {
        var now = Epoch.AddSeconds(second);
        var deck = second is >= 311 and <= 391;
        var ground = second <= 10 || second >= 692;
        var fraction = ground ? 0 : deck ? 1 : second < 311 ? Math.Clamp((second - 11) / 299d, 0, 1) : 1 - Math.Clamp((second - 392) / 299d, 0, 1);
        var height = Platform.DeckMeters / .3048;
        var values = new Dictionary<string, double>
        {
            ["PLANE LATITUDE"] = Origin.Latitude + (Platform.Latitude - Origin.Latitude) * fraction,
            ["PLANE LONGITUDE"] = Origin.Longitude + (Platform.Longitude - Origin.Longitude) * fraction,
            ["PLANE ALTITUDE"] = deck ? height + 2.46 : 300 + (height + 10 - 300) * fraction,
            ["GROUND ALTITUDE"] = ground ? 300 : height,
            ["GROUND VELOCITY"] = ground || deck ? 0 : 120, ["VERTICAL SPEED"] = ground || deck ? 0 : 2,
            ["SIM ON GROUND"] = ground || deck ? 1 : 0,
            ["CONTACT POINT IS ON GROUND:8"] = ground || deck ? 1 : 0,
            ["SIMULATION RATE"] = 1, ["SIMULATION TIME"] = second
        };
        if (problem == "water") { values["PLANE ALTITUDE"] = 1; values["GROUND ALTITUDE"] = 0; }
        if (problem == "hover") { values["SIM ON GROUND"] = 0; values["CONTACT POINT IS ON GROUND:8"] = 0; }
        if (problem == "freeze") values["IS ATTITUDE FREEZE ON"] = 1;
        if (problem == "slew") values["IS SLEW ACTIVE"] = 1;
        if (problem == "rate") values["SIMULATION RATE"] = 2;
        var identity = new ConditionAircraftIdentity(42, "H125 Passengers", "H125", 1);
        var profile = new PlatformMissionFlightProfile();
        return new(now, now, identity, problem == "diagnostic" ? "experimental-ol400-h125-contact-v1" : profile.Id,
            profile.Fields(identity).Select(f => f.Observe(values.GetValueOrDefault(f.Key), now)).ToArray())
        { Runtime = new(true, problem == "pause", 0) };
    }
}

