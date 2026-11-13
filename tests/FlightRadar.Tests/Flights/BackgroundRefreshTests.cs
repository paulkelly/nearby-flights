using System.Net;
using System.Net.Http.Json;
using FlightRadar.Api.Caching;
using FlightRadar.Api.Flights;
using FlightRadar.Api.Watching;
using FlightRadar.Core.Enrichment;
using FlightRadar.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static FlightRadar.Tests.Fakes.FakeFlightDataProvider;

namespace FlightRadar.Tests.Flights;

/// <summary>
/// A background refresh (the Android widget's) is served like any request but doesn't mark its area as watched, so it
/// never starts the poller's 10 s polling. The app's real chain (watching → cache) runs here; only the provider at the
/// bottom is fake. Swapping IFlightDataProvider itself would skip the watching decorator, and these tests couldn't fail.
/// </summary>
public sealed class BackgroundRefreshTests : IDisposable
{
    private const string Url = "/api/flights?lat=53.4213&lon=-6.2701&radiusKm=10";

    private readonly FakeFlightDataProvider provider = new(FlightAt("abc123", 53.43, -6.27));
    private readonly WebApplicationFactory<Program> app;

    public BackgroundRefreshTests()
    {
        app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.UseSetting("ConnectionStrings:Postgres", "");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IRouteProvider>(new FakeRouteProvider());
                // Registered after the app's, so it wins, for the endpoint's background branch and inside the
                // app's own WatchingFlightDataProvider alike.
                services.AddTransient(serviceProvider => new CachingFlightDataProvider(
                    provider,
                    serviceProvider.GetRequiredService<HybridCache>(),
                    serviceProvider.GetRequiredService<IOptions<FlightCacheOptions>>(),
                    NullLogger<CachingFlightDataProvider>.Instance));
            });
        });
    }

    public void Dispose() => app.Dispose();

    private Task<IReadOnlyCollection<Core.Geo.SearchArea>> WatchedAreasAsync() =>
        app.Services.GetRequiredService<IWatchedAreaRegistry>().GetWatchedAreasAsync(default);

    private async Task<HttpResponseMessage> GetAsync(string? backgroundRefresh)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Url);
        if (backgroundRefresh is not null)
        {
            request.Headers.Add(FlightsEndpoints.BackgroundRefreshHeader, backgroundRefresh);
        }
        return await app.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task Get_Normal_MarksTheAreaWatched()
    {
        var response = await GetAsync(backgroundRefresh: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(await WatchedAreasAsync()); // someone is looking: the poller keeps it fresh
    }

    [Fact]
    public async Task Get_BackgroundRefresh_SameAnswerButNotWatched()
    {
        var response = await GetAsync(backgroundRefresh: "true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var flights = (await response.Content.ReadFromJsonAsync<FlightsResponse>())!;
        Assert.Equal("abc123", Assert.Single(flights.Flights).Hex); // trimmed and enriched as usual
        Assert.Single(provider.Calls); // fetched through the cache on a miss
        Assert.Empty(await WatchedAreasAsync());
    }

    [Fact]
    public async Task Get_BackgroundRefreshAfterANormalOne_ServedFromTheCache()
    {
        await GetAsync(backgroundRefresh: null);

        await GetAsync(backgroundRefresh: "true");

        Assert.Single(provider.Calls); // the same cache entry: the background read added no upstream call
    }

    [Fact]
    public async Task Get_BackgroundRefreshFalse_IsANormalRequest()
    {
        await GetAsync(backgroundRefresh: "false");

        Assert.Single(await WatchedAreasAsync());
    }
}
