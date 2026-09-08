using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Tests;

public sealed class AirportRecognitionTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-09-08T18:00:00Z");
    private static AirstripDefinition Airport(string identifier="FPRA",double latitude=51,double longitude=-1,IReadOnlyList<ParkingStand>? stands=null) =>
        new()
        {
            Name="Frontier Ridge Airport",Identifier=identifier,Latitude=latitude,Longitude=longitude,AltitudeMeters=100,
            TrueHeadingDegrees=0,MagneticVariationDegreesEast=0,LengthMeters=200,WidthMeters=20,Surface=RunwaySurface.Dirt,
            ParkingStands=stands??[new(1,800,0,180,10),new(2,850,0,180,10)]
        };
    private static AircraftTelemetry At(AirstripDefinition airport,LocalPoint offset,double speed=0,bool onGround=true,double seconds=0)
    {
        var point=LocalCoordinates.ToGeographic(new(airport.Latitude,airport.Longitude),offset);
        return new(Now.AddSeconds(seconds),"Test aircraft","TEST",point.Latitude,point.Longitude,1000,0,speed,onGround);
    }

    [Fact]
    public void LocalWgs84RoundTripRemainsMetreAccurateBeyondFormerFiveHundredMetreLimit()
    {
        var anchor=new GeoPoint(-22.5861668,14.5390713);var expected=new LocalPoint(2400,-1700);
        var geographic=LocalCoordinates.ToGeographic(anchor,expected);var actual=LocalCoordinates.FromGeographic(anchor,geographic);
        Assert.InRange(Math.Abs(actual.East-expected.East),0,0.02);Assert.InRange(Math.Abs(actual.North-expected.North),0,0.02);
    }

    [Fact]
    public void RecognisesRunwayAndDistantParkingAsPartsOfSameAirport()
    {
        var airport=Airport();var recognizer=new AirportLocationRecognizer();
        Assert.Equal("FPRA",recognizer.Evaluate(At(airport,new(0,0),speed:5),[airport]).Context!.AirportIdentifier);
        var distant=recognizer.Evaluate(At(airport,new(800,0),speed:5),[airport]);
        Assert.Equal(1,distant.Context!.ParkingStandNumber);Assert.Equal(AircraftParkingState.NearStand,distant.Context.ParkingState);
        Assert.InRange(distant.Context.ParkingStandDistanceMeters!.Value,0,0.02);
    }

    [Fact]
    public void OutsideAllAirportsReportsNearestWithoutInventingAssociation()
    {
        var airport=Airport();var result=new AirportLocationRecognizer().Evaluate(At(airport,new(0,700)),[airport]);
        Assert.Null(result.Context);Assert.Equal("FPRA",result.NearestAirportIdentifier);Assert.True(result.NearestAirportDistanceMeters>250);
    }

    [Fact]
    public void StandThresholdUsesPhysicalRadiusPlusConfiguredMargin()
    {
        var airport=Airport(stands:[new(1,800,0,180,10)]);var recognizer=new AirportLocationRecognizer();
        var inside=recognizer.Evaluate(At(airport,new(814.9,0),speed:4),[airport]);
        var outside=recognizer.Evaluate(At(airport,new(815.1,0),speed:0,seconds:1),[airport]);
        Assert.Equal(AircraftParkingState.NearStand,inside.Context!.ParkingState);Assert.Equal(1,inside.Context.ParkingStandNumber);
        Assert.Equal(AircraftParkingState.NotAtStand,outside.Context!.ParkingState);Assert.Null(outside.Context.ParkingStandNumber);
    }

    [Fact]
    public void TaxiingPastIsNearButFiveContinuousStoppedSecondsBecomesParked()
    {
        var airport=Airport();var recognizer=new AirportLocationRecognizer();
        var moving=recognizer.Evaluate(At(airport,new(800,0),speed:3),[airport]);
        var stopping=recognizer.Evaluate(At(airport,new(800,0),seconds:1),[airport]);
        for(var second=2;second<=5;second++)recognizer.Evaluate(At(airport,new(800,0),seconds:second),[airport]);
        var almost=recognizer.Evaluate(At(airport,new(800,0),seconds:5.9),[airport]);
        var parked=recognizer.Evaluate(At(airport,new(800,0),seconds:6),[airport]);
        Assert.Equal(AircraftParkingState.NearStand,moving.Context!.ParkingState);
        Assert.Equal(AircraftParkingState.NearStand,stopping.Context!.ParkingState);
        Assert.Equal(AircraftParkingState.NearStand,almost.Context!.ParkingState);
        Assert.Equal(AircraftParkingState.Parked,parked.Context!.ParkingState);
    }

    [Fact]
    public void ChoosesNearestStandAndNearestOfMultipleAirports()
    {
        var first=Airport();var secondAnchor=LocalCoordinates.ToGeographic(new(first.Latitude,first.Longitude),new(5000,0));
        var second=Airport("FPRB",secondAnchor.Latitude,secondAnchor.Longitude,[new(7,40,0,180,10)]);
        var recognizer=new AirportLocationRecognizer();
        var stand=recognizer.Evaluate(At(first,new(848,0),speed:2),[first,second]);
        var other=recognizer.Evaluate(At(second,new(40,0),speed:2,seconds:1),[first,second]);
        Assert.Equal(2,stand.Context!.ParkingStandNumber);
        Assert.Equal("FPRB",other.Context!.AirportIdentifier);Assert.Equal(7,other.Context.ParkingStandNumber);
    }

    [Fact]
    public void AirborneAircraftNeverClaimsAStand()
    {
        var airport=Airport();var result=new AirportLocationRecognizer().Evaluate(At(airport,new(800,0),onGround:false),[airport]);
        Assert.Equal("FPRA",result.Context!.AirportIdentifier);Assert.Null(result.Context.ParkingStandNumber);
        Assert.Equal(AircraftParkingState.NotAtStand,result.Context.ParkingState);
    }
}
