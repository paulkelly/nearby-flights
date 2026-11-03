using System.Diagnostics;
using System.Globalization;
using System.Net;
using FlightRadar.Api.Providers.AdsbLol;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using FlightRadar.Tests.Fakes;

namespace FlightRadar.Tests.Providers.AdsbLol;

public sealed class AdsbLolFlightDataProviderTests
{
    private static readonly GeoPoint DublinAirport = new(53.42, -6.27);

    private static AdsbLolFlightDataProvider CreateProvider(HttpMessageHandler handler, TimeSpan? timeout = null) =>
        new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.adsb.lol/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
        });

    private static async Task<IReadOnlyList<Flight>> GetMixedSampleFlightsAsync()
    {
        var provider = CreateProvider(FakeHttpMessageHandler.Json(AdsbLolSamples.Mixed));
        return await provider.GetFlightsAsync(DublinAirport, 50, CancellationToken.None);
    }

    // ---- Request ----

    [Theory]
    [InlineData(50.0, 27)]     // 26.998 nm, rounded up
    [InlineData(1.852, 1)]     // exactly 1 nm
    [InlineData(0.1, 1)]       // tiny radius still asks for at least 1 nm
    [InlineData(1000.0, 250)]  // clamped to adsb.lol's maximum
    public async Task GetFlightsAsync_RadiusInKm_RequestsRadiusInNauticalMiles(double radiusKm, int expectedNm)
    {
        var handler = FakeHttpMessageHandler.Json(AdsbLolSamples.Empty);

        await CreateProvider(handler).GetFlightsAsync(DublinAirport, radiusKm, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"https://api.adsb.lol/v2/point/53.42/-6.27/{expectedNm}", request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetFlightsAsync_CultureUsesDecimalComma_StillFormatsCoordinatesWithDots()
    {
        var handler = FakeHttpMessageHandler.Json(AdsbLolSamples.Empty);
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); // formats 53.42 as "53,42"
        try
        {
            await CreateProvider(handler).GetFlightsAsync(DublinAirport, 50, CancellationToken.None);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        Assert.Equal("/v2/point/53.42/-6.27/27", Assert.Single(handler.Requests).RequestUri!.AbsolutePath);
    }

    // ---- Mapping ----

    [Fact]
    public async Task GetFlightsAsync_AirborneAircraft_MapsAllFields()
    {
        var flights = await GetMixedSampleFlightsAsync();

        var expected = new Flight
        {
            Hex = "4cad7b",
            Callsign = "EIN58P", // trailing padding trimmed
            Registration = "EI-NSB",
            AircraftType = "A20N", // from "t", not "type"
            Position = new GeoPoint(53.448257, -6.495510),
            PositionTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(AdsbLolSamples.Now - 500), // now - seen_pos
            AltitudeFt = 2200,
            OnGround = false,
            GroundSpeedKt = 165.0,
            TrackDeg = 89.65,
            VerticalRateFpm = -704, // baro_rate preferred over geom_rate
        };
        // Records compare by value, so one assertion checks every field.
        Assert.Equal(expected, flights.Single(f => f.Hex == "4cad7b"));
    }

    [Fact]
    public async Task GetFlightsAsync_AltitudeIsGround_SetsOnGroundAndNullAltitude()
    {
        var flights = await GetMixedSampleFlightsAsync();

        var onGround = flights.Single(f => f.Hex == "4ca9f1");
        Assert.True(onGround.OnGround);
        Assert.Null(onGround.AltitudeFt);
    }

    [Fact]
    public async Task GetFlightsAsync_NoCallsign_ReturnsNullCallsign()
    {
        var flights = await GetMixedSampleFlightsAsync();

        Assert.Null(flights.Single(f => f.Hex == "4ca9f1").Callsign);
    }

    [Fact]
    public async Task GetFlightsAsync_OnlyGeometricRate_FallsBackToIt()
    {
        var flights = await GetMixedSampleFlightsAsync();

        Assert.Equal(64, flights.Single(f => f.Hex == "4ca4dc").VerticalRateFpm);
    }

    [Fact]
    public async Task GetFlightsAsync_AircraftWithoutPosition_IsSkipped()
    {
        var flights = await GetMixedSampleFlightsAsync();

        Assert.Equal(3, flights.Count);
        Assert.DoesNotContain(flights, f => f.Hex == "4ca123");
    }

    [Theory]
    [InlineData(AdsbLolSamples.Empty)]
    [InlineData("""{ "msg": "No error", "now": 1793713435002 }""")] // "ac" missing entirely
    public async Task GetFlightsAsync_NoAircraft_ReturnsEmptyList(string json)
    {
        var provider = CreateProvider(FakeHttpMessageHandler.Json(json));

        var flights = await provider.GetFlightsAsync(DublinAirport, 50, CancellationToken.None);

        Assert.Empty(flights);
    }

    // ---- Failures ----

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task GetFlightsAsync_ErrorStatus_ThrowsFlightDataUnavailable(HttpStatusCode status)
    {
        var provider = CreateProvider(FakeHttpMessageHandler.Status(status));

        await Assert.ThrowsAsync<FlightDataUnavailableException>(
            () => provider.GetFlightsAsync(DublinAirport, 50, CancellationToken.None));
    }

    [Fact]
    public async Task GetFlightsAsync_InvalidJson_ThrowsFlightDataUnavailable()
    {
        var provider = CreateProvider(FakeHttpMessageHandler.Json("<html>Bad gateway</html>"));

        await Assert.ThrowsAsync<FlightDataUnavailableException>(
            () => provider.GetFlightsAsync(DublinAirport, 50, CancellationToken.None));
    }

    [Fact]
    public async Task GetFlightsAsync_NetworkError_ThrowsFlightDataUnavailable()
    {
        var provider = CreateProvider(FakeHttpMessageHandler.Throws(new HttpRequestException("No such host")));

        await Assert.ThrowsAsync<FlightDataUnavailableException>(
            () => provider.GetFlightsAsync(DublinAirport, 50, CancellationToken.None));
    }

    [Fact]
    public async Task GetFlightsAsync_UpstreamTooSlow_ThrowsFlightDataUnavailable()
    {
        // A "server" that never answers in time, with a real (short) HttpClient timeout.
        var neverAnswers = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        });
        var provider = CreateProvider(neverAnswers, timeout: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<FlightDataUnavailableException>(
            () => provider.GetFlightsAsync(DublinAirport, 50, CancellationToken.None));
    }

    [Fact]
    public async Task GetFlightsAsync_CallerCancels_ThrowsOperationCanceledNotUnavailable()
    {
        var provider = CreateProvider(FakeHttpMessageHandler.Json(AdsbLolSamples.Empty));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // ThrowsAnyAsync also accepts derived types such as TaskCanceledException.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetFlightsAsync(DublinAirport, 50, cts.Token));
    }
}
