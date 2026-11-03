using FlightRadar.Core.Geo;

namespace FlightRadar.Tests.Geo;

public sealed class HaversineTests
{
    // Expected values assume Haversine.EarthRadiusKm = 6371.0.
    private const double Tolerance = 0.01; // km, i.e. 10 m

    [Fact]
    public void DistanceKm_SamePoint_ReturnsZero()
    {
        var dublin = new GeoPoint(53.3498, -6.2603);

        Assert.Equal(0.0, Haversine.DistanceKm(dublin, dublin), Tolerance);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 1.0, 111.1949)]                  // 1 degree of longitude on the equator
    [InlineData(0.0, 0.0, 1.0, 0.0, 111.1949)]                  // 1 degree of latitude
    [InlineData(53.3498, -6.2603, 51.5074, -0.1278, 463.3111)]  // Dublin to London
    [InlineData(40.6413, -73.7781, 51.4700, -0.4543, 5540.0113)] // JFK to Heathrow
    public void DistanceKm_KnownPairs_ReturnsExpected(
        double lat1, double lon1, double lat2, double lon2, double expectedKm)
    {
        var distance = Haversine.DistanceKm(new GeoPoint(lat1, lon1), new GeoPoint(lat2, lon2));

        Assert.Equal(expectedKm, distance, Tolerance);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 180.0)]  // opposite sides of the equator
    [InlineData(90.0, 0.0, -90.0, 0.0)] // north pole to south pole
    public void DistanceKm_AntipodalPoints_ReturnsHalfCircumference(
        double lat1, double lon1, double lat2, double lon2)
    {
        var distance = Haversine.DistanceKm(new GeoPoint(lat1, lon1), new GeoPoint(lat2, lon2));

        Assert.Equal(Math.PI * Haversine.EarthRadiusKm, distance, Tolerance); // ≈ 20015.09 km
    }

    [Fact]
    public void DistanceKm_AcrossAntimeridian_ReturnsShortWayRound()
    {
        // 179°E to 179°W is 2 degrees apart, not 358. A naive "lon2 - lon1" approach gets this wrong.
        var east = new GeoPoint(0.0, 179.0);
        var west = new GeoPoint(0.0, -179.0);

        Assert.Equal(222.3899, Haversine.DistanceKm(east, west), Tolerance);
    }

    [Fact]
    public void DistanceKm_SwappedArguments_ReturnsSameDistance()
    {
        var dublin = new GeoPoint(53.3498, -6.2603);
        var london = new GeoPoint(51.5074, -0.1278);

        Assert.Equal(Haversine.DistanceKm(dublin, london), Haversine.DistanceKm(london, dublin), 1e-9);
    }
}
