using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using FlightRadar.Tests.Fakes;
using static FlightRadar.Tests.Fakes.FakeFlightDataProvider;

namespace FlightRadar.Tests.Flights;

public sealed class NearbyFlightsServiceTests
{
    // Points due north of the centre: each 0.1 degree of latitude is ~11.12 km.
    private static readonly GeoPoint Centre = new(53.42, -6.27);
    private static readonly Flight Near = FlightAt("near", 53.52, -6.27); // ~11.1 km
    private static readonly Flight Mid = FlightAt("mid", 53.72, -6.27);   // ~33.4 km
    private static readonly Flight Far = FlightAt("far", 53.92, -6.27);   // ~55.6 km

    [Fact]
    public async Task GetNearbyFlightsAsync_FlightsOutsideRadius_AreExcluded()
    {
        var service = new NearbyFlightsService(new FakeFlightDataProvider(Near, Far, Mid));

        var result = await service.GetNearbyFlightsAsync(Centre, 50, CancellationToken.None);

        Assert.DoesNotContain(result, n => n.Flight.Hex == "far");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetNearbyFlightsAsync_UnorderedInput_ReturnsNearestFirst()
    {
        var service = new NearbyFlightsService(new FakeFlightDataProvider(Mid, Far, Near));

        var result = await service.GetNearbyFlightsAsync(Centre, 100, CancellationToken.None);

        Assert.Equal(["near", "mid", "far"], result.Select(n => n.Flight.Hex));
    }

    [Fact]
    public async Task GetNearbyFlightsAsync_Always_ReturnsHaversineDistance()
    {
        var service = new NearbyFlightsService(new FakeFlightDataProvider(Near));

        var result = await service.GetNearbyFlightsAsync(Centre, 50, CancellationToken.None);

        Assert.Equal(Haversine.DistanceKm(Centre, Near.Position), Assert.Single(result).DistanceKm);
    }

    [Fact]
    public async Task GetNearbyFlightsAsync_FlightExactlyOnRadius_IsIncluded()
    {
        var service = new NearbyFlightsService(new FakeFlightDataProvider(Mid));
        var exactRadius = Haversine.DistanceKm(Centre, Mid.Position);

        var result = await service.GetNearbyFlightsAsync(Centre, exactRadius, CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetNearbyFlightsAsync_Always_PassesCentreAndRadiusToProvider()
    {
        var provider = new FakeFlightDataProvider();
        var service = new NearbyFlightsService(provider);

        await service.GetNearbyFlightsAsync(Centre, 42, CancellationToken.None);

        Assert.Equal((Centre, 42.0), Assert.Single(provider.Calls));
    }
}
