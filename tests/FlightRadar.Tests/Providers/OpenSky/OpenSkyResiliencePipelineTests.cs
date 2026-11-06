using System.Net;
using FlightRadar.Api.Providers.OpenSky;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using FlightRadar.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlightRadar.Tests.Providers.OpenSky;

/// <summary>The provider as the app wires it (typed client + resilience pipeline), with only the network faked.</summary>
public sealed class OpenSkyResiliencePipelineTests
{
    private static readonly GeoPoint Dublin = new(53.42, -6.27);

    private static OpenSkyFlightDataProvider CreateProvider(FakeHttpMessageHandler network)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Providers:OpenSky:BaseUrl"] = "https://opensky-network.org/api/",
                ["Providers:OpenSky:MaxRetries"] = "1",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddOpenSkyFlightDataProvider();
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => network));
        return services.BuildServiceProvider().GetRequiredService<OpenSkyFlightDataProvider>();
    }

    [Fact]
    public async Task GetFlightsAsync_OutOfCredits_IsNotRetried()
    {
        var network = FakeHttpMessageHandler.Status(HttpStatusCode.TooManyRequests);

        await Assert.ThrowsAsync<FlightDataUnavailableException>(
            () => CreateProvider(network).GetFlightsAsync(Dublin, 50, CancellationToken.None));

        // The standard pipeline would retry a 429; for OpenSky it means "no credits for hours", so one call only.
        Assert.Single(network.Requests);
    }

    [Fact]
    public async Task GetFlightsAsync_TransientServerError_IsRetriedOnce()
    {
        var network = FakeHttpMessageHandler.Status(HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<FlightDataUnavailableException>(
            () => CreateProvider(network).GetFlightsAsync(Dublin, 50, CancellationToken.None));

        Assert.Equal(2, network.Requests.Count); // first attempt + MaxRetries (1)
    }
}
