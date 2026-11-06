using System.Net.Http.Json;
using FlightRadar.Api.Flights;
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

    private (HttpClient Client, FakeHttpMessageHandler Network) CreateClient(string? active)
    {
        var network = new FakeHttpMessageHandler((request, _) => Task.FromResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.Host.Contains("opensky")
                    ? OpenSkySamples.Mixed
                    : AdsbLolSamples.Mixed),
            }));

        var client = factory.WithWebHostBuilder(builder =>
            {
                if (active is not null) builder.UseSetting("Providers:Active", active);
                builder.UseSetting("ConnectionStrings:Redis", ""); // keep the test off any local Redis
                // Tests run as Development, which loads the developer's user-secrets: blank any real OpenSky
                // credentials so these tests stay anonymous (and never fetch real tokens) on every machine.
                builder.UseSetting("Providers:OpenSky:ClientId", "");
                builder.UseSetting("Providers:OpenSky:ClientSecret", "");
                builder.ConfigureTestServices(services => services.ConfigureHttpClientDefaults(http =>
                    http.ConfigurePrimaryHttpMessageHandler(() => network)));
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
}
