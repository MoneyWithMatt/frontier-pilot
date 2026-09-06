using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Tests;

public sealed class HeadingsTests
{
    [Theory]
    [InlineData(0,0)] [InlineData(360,0)] [InlineData(720,0)] [InlineData(-360,0)] [InlineData(-90,270)]
    [InlineData(450,90)] [InlineData(-721,359)] [InlineData(359.5,359.5)] [InlineData(-0.5,359.5)]
    public void NormalizeWraps(double input, double expected) => Assert.Equal(expected, Headings.Normalize(input));

    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public void NonFiniteIsRejected(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Headings.Normalize(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Headings.RunwayNumber(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Headings.Reciprocal(value));
    }

    [Theory]
    [InlineData(0,36)] [InlineData(4.9999,36)] [InlineData(5,1)] [InlineData(14.9999,1)] [InlineData(15,2)]
    [InlineData(90,9)] [InlineData(95,10)] [InlineData(180,18)] [InlineData(354.9999,35)] [InlineData(355,36)]
    [InlineData(360,36)] [InlineData(-5,36)] [InlineData(-6,35)] [InlineData(725,1)]
    public void NumberRounding(double heading, int number) => Assert.Equal(number, Headings.RunwayNumber(heading));

    [Fact]
    public void EveryReciprocalPairIsEighteenApartAndReversible()
    {
        for (var n = 1; n <= 36; n++)
        {
            var other = Headings.ReciprocalNumber(n);
            Assert.Equal(18, Math.Abs(n - other));
            Assert.Equal(n, Headings.ReciprocalNumber(other));
            Assert.Equal(other, Headings.RunwayNumber(Headings.Reciprocal(n * 10)));
        }
    }

    [Theory] [InlineData(0)] [InlineData(37)] [InlineData(-1)]
    public void InvalidRunwayNumberRejected(int n) => Assert.Throws<ArgumentOutOfRangeException>(() => Headings.ReciprocalNumber(n));

    [Fact]
    public void NormalizeIsIdempotentAndFiniteForLargeInputs()
    {
        foreach (var h in new[] { -0d, double.MaxValue, double.MinValue, 1e20, -1e20, -0.00000001 })
        {
            var normalized = Headings.Normalize(h);
            Assert.InRange(normalized, 0, Math.BitDecrement(360d));
            Assert.Equal(normalized, Headings.Normalize(normalized));
        }
        Assert.Equal(0, BitConverter.DoubleToInt64Bits(Headings.Normalize(-0d)));
    }
}
