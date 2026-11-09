using System.Net.Http.Json;
using FlightRadar.Api.Flights;
using FlightRadar.Core.Enrichment;
using FlightRadar.Tests.Fakes;
using FlightRadar.Tests.Providers.AdsbLol;
using FlightRadar.Tests.Providers.OpenSky;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FlightRadar.Tests.Providers;

/// <summary>
/// The whole API with the real providers, cache and pipelines; only the network at the bottom is fake. It answers
/// like whichever service was called, so the response shows which provider "Providers:Active" chose.
/// </summary>
public sealed class ProviderSelectionTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Query = "/api/flights?lat=53.42&lon=-6.27&radiusKm=50";

    /// <param name="active">Providers:Active, or null for the default.</param>
    /// <param name="fallback">Providers:Fallback, or null for the default.</param>
    /// <param name="down">Hosts that answer 503, to simulate an outage (e.g. "api.adsb.lol").</param>
    private (HttpClient Client, FakeHttpMessageHandler Network) CreateClient(
        string? active, string? fallback = null, params string[] down)
    {
        var network = new FakeHttpMessageHandler((request, _) =>
        {
            var host = request.RequestUri!.Host;
            return Task.FromResult(down.Contains(host)
                ? new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(host.Contains("opensky") ? OpenSkySamples.Mixed : AdsbLolSamples.Mixed),
                });
        });

        var client = factory.WithWebHostBuilder(builder =>
            {
                // Always set, even to "" for "the default": a developer's user-secrets may choose a provider.
                builder.UseSetting("Providers:Active", active ?? "");
                builder.UseSetting("Providers:Fallback", fallback ?? "");
                builder.UseSetting("ConnectionStrings:Redis", ""); // keep the test off any local Redis
                // Tests run as Development, which loads the developer's user-secrets: blank any real OpenSky
                // credentials so these tests stay anonymous (and never fetch real tokens) on every machine.
                builder.UseSetting("Providers:OpenSky:ClientId", "");
                builder.UseSetting("Providers:OpenSky:ClientSecret", "");
                builder.ConfigureTestServices(services =>
                {
                    services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => network));
                    // Route lookups aren't what these tests are about: keep them off the (fake) network.
                    services.AddSingleton<IRouteProvider>(new FakeRouteProvider());
                });
            })
            .CreateClient();
        return (client, network);
    }

    [Theory]
    [InlineData(null, "api.adsb.lol")]          // the default
    [InlineData("AdsbLol", "api.adsb.lol")]
    [InlineData("OpenSky", "opensky-network.org")]
    [InlineData("opensky", "opensky-network.org")] // not case-sensitive
    public async Task GetFlights_ActiveProvider_IsTheOneCalled(string? active, string expectedHost)
    {
        var (client, network) = CreateClient(active);

        var body = await client.GetFromJsonAsync<FlightsResponse>(Query);

        Assert.All(network.Requests, request => Assert.Equal(expectedHost, request.RequestUri!.Host));
        Assert.NotEmpty(body!.Flights);
    }

    [Fact]
    public async Task GetFlights_OpenSkyActive_ReturnsOpenSkyAircraftTrimmedToTheCircle()
    {
        var (client, _) = CreateClient("OpenSky");

        var body = await client.GetFromJsonAsync<FlightsResponse>(Query);

        // All 5 positioned aircraft in the sample are within 50 km of Dublin; nearest first, as with adsb.lol.
        Assert.Equal(5, body!.Count);
        Assert.Contains(body.Flights, f => f.Hex == "4ca36c" && f.Callsign == "EIGWY" && f.AltitudeFt == 500);
        Assert.Equal(body.Flights.OrderBy(f => f.DistanceKm), body.Flights);
    }

    [Theory]
    [InlineData("FlightRadar24")]
    [InlineData("7")] // a number parses as an enum value, but isn't a provider
    public void Startup_UnknownProvider_FailsFastNamingTheChoices(string active)
    {
        var exception = Assert.ThrowsAny<Exception>(() => CreateClient(active).Client.GetAsync(Query).GetAwaiter().GetResult());

        Assert.Contains("AdsbLol, OpenSky", exception.ToString());
    }

    [Fact]
    public void Startup_OpenSkyClientIdWithoutSecret_FailsFast()
    {
        var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Providers:OpenSky:ClientId", "someone");
            builder.UseSetting("Providers:OpenSky:ClientSecret", ""); // even if user-secrets has one
        });

        var exception = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains("ClientSecret", exception.ToString());
    }

    // ---- Fallback ----

    [Fact]
    public async Task GetFlights_AdsbLolDown_IsServedByOpenSkyByDefault()
    {
        var (client, network) = CreateClient(active: null, down: "api.adsb.lol");

        var response = await client.GetAsync(Query);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FlightsResponse>();
        Assert.Contains(body!.Flights, f => f.Hex == "4ca36c"); // an aircraft from the OpenSky sample
        Assert.Contains(network.Requests, r => r.RequestUri!.Host == "api.adsb.lol");        // tried first...
        Assert.Contains(network.Requests, r => r.RequestUri!.Host == "opensky-network.org");  // ...then fell back
    }

    [Fact]
    public async Task GetFlights_AdsbLolDownAndFallbackOff_Returns502()
    {
        var (client, network) = CreateClient(active: null, fallback: "None", down: "api.adsb.lol");

        var response = await client.GetAsync(Query);

        Assert.Equal(System.Net.HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain(network.Requests, r => r.RequestUri!.Host == "opensky-network.org");
    }

    [Fact]
    public async Task GetFlights_BothDown_Returns502()
    {
        var (client, _) = CreateClient(active: null, down: ["api.adsb.lol", "opensky-network.org"]);

        var response = await client.GetAsync(Query);

        Assert.Equal(System.Net.HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task GetFlights_OpenSkyActive_HasNoFallbackByDefault()
    {
        // adsb.lol isn't a default fallback for OpenSky: OpenSky as primary is an explicit choice.
        var (client, network) = CreateClient(active: "OpenSky", down: "opensky-network.org");

        var response = await client.GetAsync(Query);

        Assert.Equal(System.Net.HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain(network.Requests, r => r.RequestUri!.Host == "api.adsb.lol");
    }

    [Fact]
    public async Task GetFlights_OpenSkyActiveWithAdsbLolFallback_FallsBackToAdsbLol()
    {
        var (client, _) = CreateClient(active: "OpenSky", fallback: "AdsbLol", down: "opensky-network.org");

        var body = await client.GetFromJsonAsync<FlightsResponse>(Query);

        Assert.NotEmpty(body!.Flights);
    }

    [Theory]
    [InlineData("AdsbLol", "AdsbLol", "the same as")]   // falling back to yourself makes no sense
    [InlineData("AdsbLol", "Planefinder", "expected one of")]
    public void Startup_InvalidFallback_FailsFast(string active, string fallback, string expectedMessage)
    {
        var exception = Assert.ThrowsAny<Exception>(
            () => CreateClient(active, fallback).Client.GetAsync(Query).GetAwaiter().GetResult());

        Assert.Contains(expectedMessage, exception.ToString());
    }
}
