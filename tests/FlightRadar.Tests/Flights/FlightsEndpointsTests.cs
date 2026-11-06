using System.Net;
using System.Net.Http.Json;
using FlightRadar.Api.Caching;
using FlightRadar.Api.Flights;
using FlightRadar.Api.Providers.AdsbLol;
using FlightRadar.Core.Flights;
using FlightRadar.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static FlightRadar.Tests.Fakes.FakeFlightDataProvider;

namespace FlightRadar.Tests.Flights;

/// <summary>
/// Runs the real API in memory (routing, binding, validation, DI, JSON, error handling) with a fake provider,
/// so no request ever reaches adsb.lol. IClassFixture shares one factory across the tests in this class.
/// </summary>
public sealed class FlightsEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ValidQuery = "/api/flights?lat=53.42&lon=-6.27&radiusKm=50";

    private HttpClient CreateClient(IFlightDataProvider provider) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton(provider))) // registered last, so it replaces adsb.lol
            .CreateClient();

    // ---- Success ----

    [Fact]
    public async Task GetFlights_ValidRequest_ReturnsFlightsInRadiusNearestFirst()
    {
        var client = CreateClient(new FakeFlightDataProvider(
            FlightAt("mid", 53.72, -6.27),   // ~33 km
            FlightAt("far", 53.92, -6.27),   // ~56 km, outside the 50 km radius
            FlightAt("near", 53.52, -6.27))); // ~11 km

        var response = await client.GetAsync(ValidQuery);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FlightsResponse>();
        Assert.NotNull(body);
        Assert.Equal((53.42, -6.27, 50.0), (body.Lat, body.Lon, body.RadiusKm));
        Assert.Equal(2, body.Count);
        Assert.Equal(["near", "mid"], body.Flights.Select(f => f.Hex));
        Assert.Equal(11.12, body.Flights[0].DistanceKm, tolerance: 0.01);
    }

    [Fact]
    public async Task GetFlights_ValidRequest_UsesCamelCaseJson()
    {
        var client = CreateClient(new FakeFlightDataProvider(FlightAt("abc123", 53.52, -6.27)));

        var json = await client.GetStringAsync(ValidQuery);

        // The frontend depends on these exact names.
        Assert.Contains("\"radiusKm\":50", json);
        Assert.Contains("\"hex\":\"abc123\"", json);
        Assert.Contains("\"distanceKm\":", json);
    }

    // ---- Validation ----

    [Fact]
    public async Task GetFlights_NoParameters_Returns400WithErrorForEach()
    {
        var client = CreateClient(new FakeFlightDataProvider());

        var response = await client.GetAsync("/api/flights");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Equal(["lat", "lon", "radiusKm"], problem!.Errors.Keys.Order());
    }

    [Theory]
    [InlineData("lat=90.1&lon=0&radiusKm=50", "lat")]
    [InlineData("lat=-90.1&lon=0&radiusKm=50", "lat")]
    [InlineData("lat=0&lon=180.1&radiusKm=50", "lon")]
    [InlineData("lat=0&lon=-180.1&radiusKm=50", "lon")]
    [InlineData("lat=0&lon=0&radiusKm=0", "radiusKm")]
    [InlineData("lat=0&lon=0&radiusKm=-5", "radiusKm")]
    [InlineData("lat=0&lon=0&radiusKm=464", "radiusKm")]
    [InlineData("lat=NaN&lon=0&radiusKm=50", "lat")]
    [InlineData("lat=0&lon=Infinity&radiusKm=50", "lon")]
    public async Task GetFlights_InvalidValue_Returns400NamingTheParameter(string query, string invalidParameter)
    {
        var provider = new FakeFlightDataProvider();
        var client = CreateClient(provider);

        var response = await client.GetAsync($"/api/flights?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Equal(invalidParameter, Assert.Single(problem!.Errors.Keys));
        Assert.Empty(provider.Calls); // never called upstream with bad input
    }

    [Theory]
    [InlineData("lat=90&lon=180&radiusKm=463")]
    [InlineData("lat=-90&lon=-180&radiusKm=0.001")]
    public async Task GetFlights_BoundaryValues_Returns200(string query)
    {
        var client = CreateClient(new FakeFlightDataProvider());

        var response = await client.GetAsync($"/api/flights?{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetFlights_NonNumericValue_Returns400()
    {
        var client = CreateClient(new FakeFlightDataProvider());

        var response = await client.GetAsync("/api/flights?lat=abc&lon=0&radiusKm=50");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // ---- Upstream failure ----

    [Fact]
    public async Task GetFlights_ProviderUnavailable_Returns502ProblemDetails()
    {
        var client = CreateClient(new FakeFlightDataProvider
        {
            ExceptionToThrow = new FlightDataUnavailableException("secret upstream detail"),
        });

        var response = await client.GetAsync(ValidQuery);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Flight data unavailable", problem!.Title);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
    }

    // ---- Wiring (real registrations, no HTTP calls made) ----

    [Fact]
    public void Services_DefaultConfiguration_ResolvesCachedAdsbLolProvider()
    {
        using var scope = factory.Services.CreateScope();

        var provider = scope.ServiceProvider.GetRequiredService<IFlightDataProvider>();

        Assert.IsType<CachingFlightDataProvider>(provider);
        Assert.NotNull(scope.ServiceProvider.GetService<AdsbLolFlightDataProvider>()); // what the cache wraps
    }

    [Theory]
    [InlineData("TotalTimeoutSeconds", "0")]   // outside [Range(1, 60)]
    [InlineData("AttemptTimeoutSeconds", "8")] // not less than the default total of 8
    public void Startup_InvalidAdsbLolOptions_FailsFast(string setting, string value)
    {
        var badConfig = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Providers:AdsbLol:{setting}"] = value,
            })));

        var exception = Assert.ThrowsAny<Exception>(() => badConfig.CreateClient());

        // Bad timeouts can break more than one options class (ours and the resilience pipeline's, which is also
        // validated at startup), so startup may throw an AggregateException holding several. Ours must be among them.
        Assert.Contains(AllExceptions(exception), e => e is OptionsValidationException { OptionsType: var type }
                                                        && type == typeof(AdsbLolOptions));
    }

    private static IEnumerable<Exception> AllExceptions(Exception exception) =>
        exception is AggregateException aggregate
            ? aggregate.InnerExceptions.SelectMany(AllExceptions).Prepend(exception)
            : exception.InnerException is { } inner
                ? AllExceptions(inner).Prepend(exception)
                : [exception];
}
