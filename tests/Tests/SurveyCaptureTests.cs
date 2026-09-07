using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Tests;

public class SurveyCaptureTests
{
    private static List<SurveyFrame> Stable() => Enumerable.Range(0, 21).Select(i => {
        var avatar = new SurveyObject(32, -22.4, 14.4, 20.01, 359.9, 20, 1, .01, 0, -10);
        return new SurveyFrame(i * .25, avatar with { ObjectId = 52, Latitude = -22.401, AltitudeMeters = 21, AboveGroundMeters = 1 }, avatar, avatar);
    }).ToList();
    [Fact] public void CapturesGroundRatherThanObjectAltitude()
    {
        var p = SurveyCapture.Create(Stable());
        Assert.Equal(20, p.AltitudeMeters); Assert.Equal(20.01, p.AvatarObjectAltitudeMeters); Assert.Equal(359.9, p.TrueHeadingDegrees, 8);
    }
    [Fact] public void RequiresFullFiveSeconds() => Assert.NotNull(SurveyCapture.Validate(Stable().Take(20).ToArray()));
    [Theory]
    [InlineData("aircraft")][InlineData("alias")][InlineData("airborne")][InlineData("crouched")]
    [InlineData("nan")][InlineData("ground-offset")][InlineData("above-ground")][InlineData("moving")]
    [InlineData("heading")][InlineData("ground-jump")][InlineData("stale")][InlineData("current-mismatch")]
    public void RejectsInvalidCapture(string fault)
    {
        var frames = Stable(); var f = frames[10];
        frames[10] = fault switch {
            "aircraft" => f with { Current = f.Aircraft },
            "alias" => f with { Aircraft = f.Avatar },
            "airborne" => f with { Avatar = f.Avatar with { OnGround = 0 } },
            "crouched" => f with { Avatar = f.Avatar with { Crouched = 1 } },
            "nan" => f with { Avatar = f.Avatar with { Latitude = double.NaN } },
            "ground-offset" => f with { Avatar = f.Avatar with { AltitudeMeters = 21 } },
            "above-ground" => f with { Avatar = f.Avatar with { AboveGroundMeters = 2 } },
            "moving" => f with { Avatar = f.Avatar with { Latitude = -22.4001 } },
            "heading" => f with { Avatar = f.Avatar with { TrueHeadingDegrees = 20 } },
            "ground-jump" => f with { Avatar = f.Avatar with { GroundAltitudeMeters = 19 } },
            "stale" => f with { Seconds = 20 },
            _ => f with { Current = f.Current with { GroundAltitudeMeters = 30 } }
        };
        Assert.NotNull(SurveyCapture.Validate(frames)); Assert.Throws<ArgumentException>(() => SurveyCapture.Create(frames));
    }
    [Fact] public void NorthCrossingIsStable()
    {
        var frames = Stable().Select((f,i) => {var a = f.Avatar with { TrueHeadingDegrees = i%2==0 ? 359.9 : .1 }; return f with { Avatar=a, Current=a };}).ToArray();
        Assert.Null(SurveyCapture.Validate(frames));
    }
    [Fact] public void AircraftGroundChangeIsRejected()
    {
        var frames=Stable(); frames[^1]=frames[^1] with { Aircraft=frames[^1].Aircraft with { GroundAltitudeMeters=19 } };
        Assert.Contains("Aircraft", SurveyCapture.Validate(frames));
    }
}
