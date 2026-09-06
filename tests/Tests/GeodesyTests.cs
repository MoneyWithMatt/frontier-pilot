using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Tests;

public sealed class GeodesyTests
{
    // Independent GeographicLib 2.1 WGS84 direct reference values; see geodesic-reference.md.
    [Theory]
    [InlineData(0,0,0,1000,0.009043694769749644,0,0)]
    [InlineData(0,0,90,1000,0,0.008983152841195215,90)]
    [InlineData(25.035986003223783,-77.46942558230664,89.64241865029217,1264.6561306048284,25.036056725141236,-77.45689462842093,89.64772159919781)]
    [InlineData(25.035986003223783,-77.46942558230664,269.64241865029217,1264.6561306048284,25.035914224676954,-77.48195652171579,269.6371157216316)]
    [InlineData(-33.9,151.2,217.3,5000,-33.93585333138811,151.16722763505618,217.31828713738167)]
    [InlineData(80,15,90,5000,79.99990078360923,15.257816845970824,90.25390000292754)]
    [InlineData(0,179.999,90,1000,0,-179.99201684715882,90)]
    [InlineData(0,-179.999,270,1000,0,179.99201684715882,270)]
    [InlineData(89.999,0,0,1000,89.99204696596894,-180,180)]
    [InlineData(-89.999,150,180,1000,-89.99204696596894,-30,0)]
    [InlineData(51.5,-0.1,123.456,0.01,51.499999950448746,-0.09999987985264393,123.4560000940283)]
    [InlineData(45,90,359.9,10000,45.08998241423224,89.99977829599415,359.89984310849235)]
    public void MatchesIndependentWgs84Reference(double latitude, double longitude, double heading, double distance,
        double expectedLatitude, double expectedLongitude, double expectedBearing)
    {
        var result = Geodesy.Direct(new(latitude, longitude), heading, distance);
        Assert.InRange(Math.Abs(result.Position.Latitude - expectedLatitude), 0, 1e-8);
        Assert.InRange(Math.Abs(Geodesy.NormalizeLongitude(result.Position.Longitude - expectedLongitude)), 0, 1e-8);
        Assert.InRange(Math.Abs(Geodesy.NormalizeLongitude(result.ForwardBearingDegrees - expectedBearing)), 0, 1e-8);
    }

    [Fact]
    public void ZeroDistancePreservesPositionWithCanonicalAngles()
    {
        var result = Geodesy.Direct(new(20,180), -90, 0);
        Assert.Equal(new GeoPoint(20,-180), result.Position);
        Assert.Equal(270, result.ForwardBearingDegrees);
    }

    [Theory] [InlineData(-1)] [InlineData(10001)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidDistanceRejected(double distance) => Assert.Throws<ArgumentOutOfRangeException>(() => Geodesy.Direct(new(0,0), 0, distance));

    [Theory] [InlineData(90,0)] [InlineData(-90,0)] [InlineData(0,181)] [InlineData(double.NaN,0)] [InlineData(0,double.NaN)]
    public void InvalidOriginRejected(double lat, double lon) => Assert.Throws<ArgumentOutOfRangeException>(() => Geodesy.Direct(new(lat,lon), 0, 10));

    [Fact]
    public void EndpointsFaceEachOtherAndReturnToCenter()
    {
        foreach (var lat in new[] { -80d, -30, 0, 25, 80, 89.99 })
        foreach (var heading in new[] { 0d, 45, 90, 180, 270, 359.9 })
        {
            var d = Fixtures.Strip with { Latitude = lat, Longitude = 179.99, TrueHeadingDegrees = heading, LengthMeters = 10000 };
            var geometry = RunwayGeometry.Calculate(d);
            foreach (var end in new[] { geometry.Primary, geometry.Secondary })
            {
                var back = Geodesy.Direct(end.Position, end.InwardTrueHeadingDegrees, d.LengthMeters / 2).Position;
                Assert.InRange(Math.Abs(back.Latitude - d.Latitude), 0, 1e-8);
                Assert.InRange(Math.Abs(Geodesy.NormalizeLongitude(back.Longitude - d.Longitude)), 0, 1e-7);
            }
            var across = Geodesy.Direct(geometry.Primary.Position, geometry.Primary.InwardTrueHeadingDegrees, d.LengthMeters).Position;
            Assert.InRange(Math.Abs(across.Latitude - geometry.Secondary.Position.Latitude), 0, 1e-8);
            Assert.InRange(Math.Abs(Geodesy.NormalizeLongitude(across.Longitude - geometry.Secondary.Position.Longitude)), 0, 1e-7);
        }
    }

    [Fact]
    public void MagneticVariationAffectsNumbersButNeverGeometry()
    {
        var west = RunwayGeometry.Calculate(Fixtures.Strip);
        var east = RunwayGeometry.Calculate(Fixtures.Strip with { MagneticVariationDegreesEast = 6 });
        Assert.Equal(10, west.Primary.Number); Assert.Equal(28, west.Secondary.Number);
        Assert.Equal(8, east.Primary.Number); Assert.Equal(26, east.Secondary.Number);
        Assert.Equal(west.Primary.Position, east.Primary.Position);
        Assert.Equal(west.TrueHeadingDegrees, east.TrueHeadingDegrees);
        Assert.True(west.Primary.Position.Longitude < Fixtures.Strip.Longitude);
        Assert.True(west.Secondary.Position.Longitude > Fixtures.Strip.Longitude);
    }
}
