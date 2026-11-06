using FlightRadar.Api.Providers;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using FlightRadar.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using static FlightRadar.Tests.Fakes.FakeFlightDataProvider;

namespace FlightRadar.Tests.Providers;

public sealed class FallbackFlightDataProviderTests
{
    private static readonly GeoPoint Dublin = new(53.42, -6.27);

    private static FallbackFlightDataProvider Create(IFlightDataProvider primary, IFlightDataProvider fallback) =>
        new(primary, "Primary", fallback, "Fallback", NullLogger<FallbackFlightDataProvider>.Instance);

    private static FakeFlightDataProvider Down() =>
        new() { ExceptionToThrow = new FlightDataUnavailableException("down") };

    [Fact]
    public async Task GetFlightsAsync_PrimaryWorks_DoesNotCallTheFallback()
    {
        var primary = new FakeFlightDataProvider(FlightAt("primary", 53.5, -6.3));
        var fallback = new FakeFlightDataProvider(FlightAt("fallback", 53.5, -6.3));

        var flights = await Create(primary, fallback).GetFlightsAsync(Dublin, 50, CancellationToken.None);

        Assert.Equal("primary", Assert.Single(flights).Hex);
        Assert.Empty(fallback.Calls); // OpenSky credits are only spent when adsb.lol fails
    }

    [Fact]
    public async Task GetFlightsAsync_PrimaryUnavailable_ReturnsTheFallbacksFlights()
    {
        var fallback = new FakeFlightDataProvider(FlightAt("fallback", 53.5, -6.3));

        var flights = await Create(Down(), fallback).GetFlightsAsync(Dublin, 50, CancellationToken.None);

        Assert.Equal("fallback", Assert.Single(flights).Hex);
        Assert.Equal([(Dublin, 50.0)], fallback.Calls); // the same request, passed on unchanged
    }

    [Fact]
    public async Task GetFlightsAsync_BothUnavailable_ThrowsUnavailable()
    {
        var primary = Down();
        var fallback = Down();

        await Assert.ThrowsAsync<FlightDataUnavailableException>(
            () => Create(primary, fallback).GetFlightsAsync(Dublin, 50, CancellationToken.None));

        Assert.Single(primary.Calls);
        Assert.Single(fallback.Calls);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]  // a bug: another source can't fix it
    [InlineData(typeof(OperationCanceledException))] // the caller gave up: don't start a second request
    public async Task GetFlightsAsync_PrimaryFailsForAnotherReason_DoesNotFallBack(Type exceptionType)
    {
        var primary = new FakeFlightDataProvider { ExceptionToThrow = (Exception)Activator.CreateInstance(exceptionType)! };
        var fallback = new FakeFlightDataProvider();

        await Assert.ThrowsAsync(exceptionType, () => Create(primary, fallback).GetFlightsAsync(Dublin, 50, CancellationToken.None));

        Assert.Empty(fallback.Calls);
    }

    [Fact]
    public void MaxRadiusKm_Always_IsTheSmallerOfTheTwo()
    {
        var adsbLolLike = new FakeFlightDataProvider { MaxRadiusKm = 463 };
        var openSkyLike = new FakeFlightDataProvider { MaxRadiusKm = double.PositiveInfinity };

        // Cache snapping uses this, so a snapped circle must be one both providers can serve.
        Assert.Equal(463, Create(adsbLolLike, openSkyLike).MaxRadiusKm);
        Assert.Equal(463, Create(openSkyLike, adsbLolLike).MaxRadiusKm);
    }
}
