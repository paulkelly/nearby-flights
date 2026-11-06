using System.Globalization;
using FlightRadar.Api.Caching;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using FlightRadar.Tests.Fakes;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static FlightRadar.Tests.Fakes.FakeFlightDataProvider;

namespace FlightRadar.Tests.Caching;

/// <summary>The decorator over a fake provider, with a real (in-memory) HybridCache.</summary>
public sealed class CachingFlightDataProviderTests
{
    private static readonly GeoPoint Dublin = new(53.42, -6.27);
    private static readonly GeoPoint London = new(51.47, -0.45);

    private static CachingFlightDataProvider CreateCache(IFlightDataProvider inner, int ttlSeconds = 5)
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        var hybridCache = services.BuildServiceProvider().GetRequiredService<HybridCache>();

        return new CachingFlightDataProvider(
            inner, hybridCache, Options.Create(new FlightCacheOptions { TtlSeconds = ttlSeconds }));
    }

    [Fact]
    public async Task GetFlightsAsync_SameAreaWithinTtl_CallsProviderOnce()
    {
        var inner = new FakeFlightDataProvider(FlightAt("abc123", 53.5, -6.3));
        var cache = CreateCache(inner);

        var first = await cache.GetFlightsAsync(Dublin, 50, CancellationToken.None);
        var second = await cache.GetFlightsAsync(Dublin, 50, CancellationToken.None);

        Assert.Single(inner.Calls);
        Assert.Equal(first, second); // records compare by value: the same flights came back
    }

    [Fact]
    public async Task GetFlightsAsync_AnyRequest_AsksProviderForTheSnappedArea()
    {
        var inner = new FakeFlightDataProvider { MaxRadiusKm = 463 };
        var cache = CreateCache(inner);

        await cache.GetFlightsAsync(Dublin, 50, CancellationToken.None);

        // Snapped with the *provider's* limit, and the provider is asked for the bigger snapped circle.
        var expected = AreaSnapping.Snap(Dublin, 50, inner.MaxRadiusKm);
        Assert.Equal((expected.Centre, expected.RadiusKm), Assert.Single(inner.Calls));
        Assert.True(expected.RadiusKm > 50);
    }

    [Fact]
    public async Task GetFlightsAsync_NearbyCentresAndSimilarRadii_ShareOneProviderCall()
    {
        var inner = new FakeFlightDataProvider();
        var cache = CreateCache(inner);
        var nearby = new GeoPoint(53.4218, -6.2715); // ~200 m from Dublin
        // Precondition, not the point of the test: these two aren't either side of a cell boundary.
        Assert.Equal(AreaSnapping.Snap(Dublin, 50, inner.MaxRadiusKm), AreaSnapping.Snap(nearby, 50.4, inner.MaxRadiusKm));

        await cache.GetFlightsAsync(Dublin, 50, CancellationToken.None);
        await cache.GetFlightsAsync(nearby, 50.4, CancellationToken.None);

        Assert.Single(inner.Calls);
    }

    [Fact]
    public void MaxRadiusKm_Always_IsTheWrappedProvidersLimit()
    {
        var cache = CreateCache(new FakeFlightDataProvider { MaxRadiusKm = 123 });

        Assert.Equal(123, cache.MaxRadiusKm);
    }

    [Fact]
    public async Task GetFlightsAsync_DifferentCentreOrRadius_CallsProviderAgain()
    {
        var inner = new FakeFlightDataProvider();
        var cache = CreateCache(inner);

        await cache.GetFlightsAsync(Dublin, 50, CancellationToken.None);
        await cache.GetFlightsAsync(Dublin, 60, CancellationToken.None);
        await cache.GetFlightsAsync(London, 50, CancellationToken.None);

        Assert.Equal(3, inner.Calls.Count);
    }

    [Fact]
    public async Task GetFlightsAsync_AfterTtl_CallsProviderAgain()
    {
        var inner = new FakeFlightDataProvider();
        var cache = CreateCache(inner, ttlSeconds: 1);

        await cache.GetFlightsAsync(Dublin, 50, CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await cache.GetFlightsAsync(Dublin, 50, CancellationToken.None);

        Assert.Equal(2, inner.Calls.Count);
    }

    [Fact]
    public async Task GetFlightsAsync_ConcurrentIdenticalRequests_CallsProviderOnce()
    {
        var gate = new TaskCompletionSource();
        var inner = new FakeFlightDataProvider(FlightAt("abc123", 53.5, -6.3)) { Gate = gate.Task };
        var cache = CreateCache(inner);

        // 10 requests arrive while the first upstream call is still in flight...
        var requests = Enumerable.Range(0, 10)
            .Select(_ => cache.GetFlightsAsync(Dublin, 50, CancellationToken.None))
            .ToList();
        gate.SetResult(); // ...then upstream answers.
        var results = await Task.WhenAll(requests);

        Assert.Single(inner.Calls); // stampede protection: one upstream call shared by all 10
        Assert.All(results, flights => Assert.Equal("abc123", Assert.Single(flights).Hex));
    }

    [Fact]
    public async Task GetFlightsAsync_ProviderFails_FailureIsNotCached()
    {
        var inner = new FakeFlightDataProvider
        {
            ExceptionToThrow = new FlightDataUnavailableException("upstream down"),
        };
        var cache = CreateCache(inner);

        await Assert.ThrowsAsync<FlightDataUnavailableException>(
            () => cache.GetFlightsAsync(Dublin, 50, CancellationToken.None));
        await Assert.ThrowsAsync<FlightDataUnavailableException>(
            () => cache.GetFlightsAsync(Dublin, 50, CancellationToken.None));

        Assert.Equal(2, inner.Calls.Count); // the second request tried upstream again
    }

    [Fact]
    public void CacheKey_CommaDecimalCulture_UsesInvariantFormat()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // writes 53.42 as "53,42"
        try
        {
            Assert.Equal("flights:53.42:-6.27:50.5", CachingFlightDataProvider.CacheKey(Dublin, 50.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
