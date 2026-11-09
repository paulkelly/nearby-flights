using FlightRadar.Api.Routes;
using FlightRadar.Tests.Fakes;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlightRadar.Tests.Routes;

/// <summary>The decorator over a fake provider, with a real (in-memory) HybridCache.</summary>
public sealed class CachingRouteProviderTests
{
    private static CachingRouteProvider CreateCache(FakeRouteProvider inner)
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return new CachingRouteProvider(
            inner,
            services.BuildServiceProvider().GetRequiredService<HybridCache>(),
            Options.Create(new RouteOptions()),
            NullLogger<CachingRouteProvider>.Instance);
    }

    [Fact]
    public async Task GetRouteAsync_SameCallsignTwice_LooksItUpOnce()
    {
        var inner = new FakeRouteProvider().WithRoute("EIN123", "EIDW", "KORD");
        var cache = CreateCache(inner);

        var first = await cache.GetRouteAsync("EIN123", CancellationToken.None);
        var second = await cache.GetRouteAsync("EIN123", CancellationToken.None);

        Assert.Single(inner.Calls);
        Assert.Equal(first!.Airports, second!.Airports);
    }

    [Fact]
    public async Task GetRouteAsync_NoRouteKnown_IsCachedToo()
    {
        // Otherwise every poll would ask again for every callsign the database doesn't have.
        var inner = new FakeRouteProvider();
        var cache = CreateCache(inner);

        Assert.Null(await cache.GetRouteAsync("EIN999", CancellationToken.None));
        Assert.Null(await cache.GetRouteAsync("EIN999", CancellationToken.None));

        Assert.Single(inner.Calls);
    }

    [Fact]
    public async Task GetRouteAsync_LookupFails_IsNotCached()
    {
        var inner = new FakeRouteProvider().WithRoute("EIN123", "EIDW", "KORD");
        inner.Failing.Add("EIN123");
        var cache = CreateCache(inner);

        await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetRouteAsync("EIN123", CancellationToken.None));
        inner.Failing.Clear(); // the source recovers

        var route = await cache.GetRouteAsync("EIN123", CancellationToken.None);

        Assert.NotNull(route);
        Assert.Equal(2, inner.Calls.Count);
    }

    [Fact]
    public async Task GetRouteAsync_DifferentCallsigns_AreCachedSeparately()
    {
        var inner = new FakeRouteProvider().WithRoute("EIN123", "EIDW", "KORD").WithRoute("RYR4CZ", "LEBL", "EGGW");
        var cache = CreateCache(inner);

        var ein = await cache.GetRouteAsync("EIN123", CancellationToken.None);
        var ryr = await cache.GetRouteAsync("RYR4CZ", CancellationToken.None);

        Assert.Equal("EIDW", ein!.Airports[0].Icao);
        Assert.Equal("LEBL", ryr!.Airports[0].Icao);
    }

    [Fact]
    public void CacheKey_Always_IsPrefixedByType()
    {
        // Shares Redis with the flight cache ("flights:..."), so the prefix keeps the two apart.
        Assert.Equal("route:EIN123", CachingRouteProvider.CacheKey("EIN123"));
    }
}
