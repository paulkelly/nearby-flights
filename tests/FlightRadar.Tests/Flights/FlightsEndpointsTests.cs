using System.Net;
using System.Net.Http.Json;
using FlightRadar.Api.Caching;
using FlightRadar.Api.Flights;
using FlightRadar.Api.Providers.AdsbLol;
using FlightRadar.Core.Enrichment;
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

    private HttpClient CreateClient(IFlightDataProvider provider, FakeRouteProvider? routes = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(provider); // registered last, so it replaces adsb.lol
                services.AddSingleton<IRouteProvider>(routes ?? new FakeRouteProvider()); // and the route source
            }))
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

    [Fact]
    public async Task GetFlights_AirlineCallsign_IncludesTheAirlineName()
    {
        var client = CreateClient(new FakeFlightDataProvider(
            FlightAt("4cad7b", 53.52, -6.27) with { Callsign = "EIN58P" },
            FlightAt("4ca4dc", 53.53, -6.27) with { Callsign = "EIFLS" }));  // a registration, not an airline

        var body = await client.GetFromJsonAsync<FlightsResponse>(ValidQuery);

        Assert.Equal("Aer Lingus", body!.Flights.Single(f => f.Hex == "4cad7b").Airline); // from the bundled list
        Assert.Null(body.Flights.Single(f => f.Hex == "4ca4dc").Airline);
    }

    [Fact]
    public async Task GetFlights_RouteKnown_IncludesItsAirportsInOrder()
    {
        var routes = new FakeRouteProvider { Routes = { ["EIN123"] = FakeRouteProvider.RouteVia("EIDW", "KORD") } };
        var client = CreateClient(
            new FakeFlightDataProvider(
                FlightAt("4cad7b", 53.52, -6.27) with { Callsign = "EIN123" },
                FlightAt("4ca4dc", 53.53, -6.27) with { Callsign = "EIN999" }), // no route known
            routes);

        var body = await client.GetFromJsonAsync<FlightsResponse>(ValidQuery);

        var route = body!.Flights.Single(f => f.Hex == "4cad7b").Route;
        Assert.Equal(["EIDW", "KORD"], route!.Airports.Select(a => a.Icao));
        Assert.Equal("EIDW Airport", route.Airports[0].Name);
        Assert.Null(body.Flights.Single(f => f.Hex == "4ca4dc").Route);
    }

    [Fact]
    public async Task GetFlights_RouteLookupFails_StillReturnsTheFlights()
    {
        var routes = new FakeRouteProvider { Failing = { "EIN123" } };
        var client = CreateClient(
            new FakeFlightDataProvider(FlightAt("4cad7b", 53.52, -6.27) with { Callsign = "EIN123" }), routes);

        var response = await client.GetAsync(ValidQuery);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FlightsResponse>();
        Assert.Null(Assert.Single(body!.Flights).Route);
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

    // The radius limit comes from FlightsEndpoints.MaxRadiusKm rather than a literal, so changing it doesn't
    // mean hunting through tests.
    public static TheoryData<string> BoundaryQueries => new()
    {
        $"lat=90&lon=180&radiusKm={FlightsEndpoints.MaxRadiusKm}",
        "lat=-90&lon=-180&radiusKm=0.001",
    };

    [Theory]
    [MemberData(nameof(BoundaryQueries))]
    public async Task GetFlights_BoundaryValues_Returns200(string query)
    {
        var client = CreateClient(new FakeFlightDataProvider());

        var response = await client.GetAsync($"/api/flights?{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetFlights_RadiusJustOverTheLimit_Returns400()
    {
        var provider = new FakeFlightDataProvider();
        var client = CreateClient(provider);

        var response = await client.GetAsync($"/api/flights?lat=0&lon=0&radiusKm={FlightsEndpoints.MaxRadiusKm + 0.1}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Equal(
            [$"radiusKm must be greater than 0 and at most {FlightsEndpoints.MaxRadiusKm}."],
            problem!.Errors["radiusKm"]);
        Assert.Empty(provider.Calls);
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
