using FlightRadar.Core.Enrichment;
using FlightRadar.Core.Geo;

namespace FlightRadar.Tests.Enrichment;

public sealed class AirportDirectoryTests
{
    private static Airport At(string icao, double lat, double lon) => new(icao, icao[1..], icao, null, null, new GeoPoint(lat, lon), 0);

    [Fact]
    public void Within_Always_ReturnsOnlyThoseInRangeNearestFirst()
    {
        var directory = new AirportDirectory([At("FAR1", 54.5, -6.27), At("NEAR", 53.5, -6.27), At("MIDL", 53.7, -6.27)]);

        var result = directory.Within(new GeoPoint(53.42, -6.27), 50);

        Assert.Equal(["NEAR", "MIDL"], result.Select(match => match.Airport.Icao)); // FAR1 is ~120 km away
        Assert.Equal(8.9, result[0].DistanceKm, tolerance: 0.1);
    }

    [Fact]
    public void Within_AcrossTheAntimeridian_FindsTheAirport()
    {
        var directory = new AirportDirectory([At("EAST", 0, 179.9)]);

        Assert.Single(directory.Within(new GeoPoint(0, -179.9), 50)); // ~22 km apart the short way round
    }

    [Fact]
    public void LoadBundled_Always_LoadsAirportsWithScheduledService()
    {
        var bundled = AirportDirectory.LoadBundled();

        Assert.True(bundled.Count > 6000, $"Expected thousands of airports, got {bundled.Count}.");
        var dublin = Assert.Single(bundled.Within(new GeoPoint(53.4213, -6.2701), 1)).Airport;
        Assert.Equal(("EIDW", "DUB", "Dublin", 242.0), (dublin.Icao, dublin.Iata, dublin.City, dublin.ElevationFt));
    }

    [Fact]
    public void LoadBundled_NearDublin_HasNoAirfieldsWithoutScheduledService()
    {
        // Casement Air Base and Weston Airport are within 15 km of Dublin Airport but have no IATA code, so they're
        // left out of the list and can't be mistaken for where an airliner is going.
        var near = AirportDirectory.LoadBundled().Within(new GeoPoint(53.4213, -6.2701), 20);

        Assert.Equal(["EIDW"], near.Select(match => match.Airport.Icao));
    }
}
