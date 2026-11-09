using FlightRadar.Core.Geo;

namespace FlightRadar.Tests.Geo;

public sealed class BearingTests
{
    [Theory]
    [InlineData(0, 0, 1, 0, 0)]       // north
    [InlineData(0, 0, 0, 1, 90)]      // east along the equator
    [InlineData(10, 0, 0, 0, 180)]    // south
    [InlineData(0, 10, 0, 0, 270)]    // west: 270, not -90
    [InlineData(0, 179, 0, -179, 90)] // east across the antimeridian: the short way round
    public void InitialDeg_CardinalDirections(double lat1, double lon1, double lat2, double lon2, double expected)
    {
        Assert.Equal(expected, Bearing.InitialDeg(new GeoPoint(lat1, lon1), new GeoPoint(lat2, lon2)), tolerance: 1e-9);
    }

    [Fact]
    public void InitialDeg_DublinToHeathrow_IsSouthEast()
    {
        // Reference value from the standard formula (computed independently).
        var dublin = new GeoPoint(53.421299, -6.27007);
        var heathrow = new GeoPoint(51.4706, -0.461941);

        Assert.Equal(116.53, Bearing.InitialDeg(dublin, heathrow), tolerance: 0.01);
    }

    [Fact]
    public void InitialDeg_GreatCircleWest_StartsNorthOfWest()
    {
        // Dublin → Chicago: both are south of 54° N, but the great circle starts out heading north of west
        // (bearing < 270) and bends round, which is why transatlantic flights pass near Greenland.
        var dublin = new GeoPoint(53.421299, -6.27007);
        var chicago = new GeoPoint(41.9786, -87.9048);

        Assert.InRange(Bearing.InitialDeg(dublin, chicago), 280, 300);
    }

    [Theory]
    [InlineData(10, 350, 20)]   // across north
    [InlineData(350, 10, 20)]
    [InlineData(90, 270, 180)]  // opposite
    [InlineData(45, 45, 0)]
    [InlineData(0, 360, 0)]
    [InlineData(720, 90, 90)]   // more than a full turn
    public void DifferenceDeg_TakesTheShorterWay(double a, double b, double expected)
    {
        Assert.Equal(expected, Bearing.DifferenceDeg(a, b), tolerance: 1e-9);
    }
}
