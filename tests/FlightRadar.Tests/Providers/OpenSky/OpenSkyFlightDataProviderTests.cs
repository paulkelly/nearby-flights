using System.Globalization;
using System.Net;
using FlightRadar.Api.Providers.OpenSky;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using FlightRadar.Tests.Fakes;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlightRadar.Tests.Providers.OpenSky;

/// <summary>Mapping, request shape and error handling, with the network faked (no resilience pipeline here).</summary>
public sealed class OpenSkyFlightDataProviderTests
{
    private static readonly GeoPoint Dublin = new(53.42, -6.27);

    private static OpenSkyFlightDataProvider CreateProvider(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://opensky-network.org/api/") },
            NullLogger<OpenSkyFlightDataProvider>.Instance);

    private static async Task<IReadOnlyList<Flight>> GetMixedSampleAsync() =>
        await CreateProvider(FakeHttpMessageHandler.Json(OpenSkySamples.Mixed))
            .GetFlightsAsync(Dublin, 50, CancellationToken.None);

    // ---- Mapping ----

    [Fact]
    public async Task GetFlightsAsync_Sample_ReturnsAircraftWithPositionsOnly()
    {
        var flights = await GetMixedSampleAsync();

        Assert.Equal(["4076e4", "4ca3db", "4ca36c", "4d2502", "def456"], flights.Select(f => f.Hex));
    }

    [Fact]
    public async Task GetFlightsAsync_AirborneAircraft_ConvertsMetricToAviationUnits()
    {
        var eigwy = Assert.Single(await GetMixedSampleAsync(), f => f.Hex == "4ca36c");

        Assert.Equal("EIGWY", eigwy.Callsign);                      // padding trimmed
        Assert.Equal(new GeoPoint(53.3652, -6.4759), eigwy.Position);
        Assert.False(eigwy.OnGround);
        Assert.Equal(500, eigwy.AltitudeFt);                        // 152.4 m
        Assert.Equal(84.1, eigwy.GroundSpeedKt);                    // 43.24 m/s
        Assert.Equal(304.82, eigwy.TrackDeg);
        Assert.Equal(321, eigwy.VerticalRateFpm);                   // 1.63 m/s
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793979637), eigwy.PositionTimestamp);
        Assert.Null(eigwy.Registration);                            // OpenSky doesn't have these
        Assert.Null(eigwy.AircraftType);
    }

    [Fact]
    public async Task GetFlightsAsync_AircraftOnTheGround_HasNoAltitude()
    {
        var taxiing = Assert.Single(await GetMixedSampleAsync(), f => f.Hex == "4076e4");

        Assert.True(taxiing.OnGround);
        Assert.Null(taxiing.AltitudeFt);
    }

    [Fact]
    public async Task GetFlightsAsync_MissingOptionalFields_MapsToNull()
    {
        var flights = await GetMixedSampleAsync();

        Assert.Null(Assert.Single(flights, f => f.Hex == "4ca3db").Callsign); // "" on the wire
        var sparse = Assert.Single(flights, f => f.Hex == "def456");
        Assert.Null(sparse.Callsign);                                          // null on the wire
        Assert.Null(sparse.GroundSpeedKt);
        Assert.Null(sparse.TrackDeg);
        Assert.Null(sparse.VerticalRateFpm);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793979600), sparse.PositionTimestamp); // last_contact
    }

    [Fact]
    public async Task GetFlightsAsync_NoAircraftInTheBox_ReturnsEmpty()
    {
        var flights = await CreateProvider(FakeHttpMessageHandler.Json(OpenSkySamples.Empty))
            .GetFlightsAsync(Dublin, 50, CancellationToken.None);

        Assert.Empty(flights);
    }

    // ---- Request ----

    [Fact]
    public async Task GetFlightsAsync_AnyCircle_RequestsItsBoundingBox()
    {
        var network = FakeHttpMessageHandler.Json(OpenSkySamples.Empty);
        var expected = Assert.Single(BoundingBox.Around(Dublin, 50));

        await CreateProvider(network).GetFlightsAsync(Dublin, 50, CancellationToken.None);

        var request = Assert.Single(network.Requests);
        Assert.Equal("/api/states/all", request.RequestUri!.AbsolutePath);
        var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
        Assert.Equal(expected.LatMin, double.Parse(query["lamin"]!, CultureInfo.InvariantCulture), 1e-9);
        Assert.Equal(expected.LonMin, double.Parse(query["lomin"]!, CultureInfo.InvariantCulture), 1e-9);
        Assert.Equal(expected.LatMax, double.Parse(query["lamax"]!, CultureInfo.InvariantCulture), 1e-9);
        Assert.Equal(expected.LonMax, double.Parse(query["lomax"]!, CultureInfo.InvariantCulture), 1e-9);
    }

    [Fact]
    public async Task GetFlightsAsync_CircleCrossingTheAntimeridian_RequestsBothBoxesAndMergesThem()
    {
        // Every request returns the same sample, so the merge must de-duplicate by hex.
        var network = FakeHttpMessageHandler.Json(OpenSkySamples.Mixed);

        var flights = await CreateProvider(network).GetFlightsAsync(new GeoPoint(0.5, 179.9), 50, CancellationToken.None);

        Assert.Equal(2, network.Requests.Count);
        Assert.Equal(5, flights.Count);
    }

    [Fact]
    public void MaxRadiusKm_Always_IsUnlimited()
    {
        Assert.Equal(double.PositiveInfinity, CreateProvider(FakeHttpMessageHandler.Json(OpenSkySamples.Empty)).MaxRadiusKm);
    }

    // ---- Failures ----

    [Fact]
    public async Task GetFlightsAsync_OutOfCredits_ThrowsUnavailableWithRetryAfter()
    {
        var network = new FakeHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.Add("X-Rate-Limit-Retry-After-Seconds", "3600");
            return Task.FromResult(response);
        });

        var exception = await Assert.ThrowsAsync<FlightDataUnavailableException>(
            () => CreateProvider(network).GetFlightsAsync(Dublin, 50, CancellationToken.None));

        Assert.Contains("3600", exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task GetFlightsAsync_UpstreamFailureOrBadBody_ThrowsUnavailable(HttpStatusCode status, string body)
    {
        await Assert.ThrowsAsync<FlightDataUnavailableException>(
            () => CreateProvider(FakeHttpMessageHandler.Json(body, status)).GetFlightsAsync(Dublin, 50, CancellationToken.None));
    }
}
