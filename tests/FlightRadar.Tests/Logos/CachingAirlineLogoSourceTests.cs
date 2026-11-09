using FlightRadar.Api.Logos;
using FlightRadar.Tests.Fakes;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FlightRadar.Tests.Logos;

public sealed class CachingAirlineLogoSourceTests
{
    private static CachingAirlineLogoSource CreateCache(FakeAirlineLogoSource inner)
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return new CachingAirlineLogoSource(
            inner,
            services.BuildServiceProvider().GetRequiredService<HybridCache>(),
            Options.Create(new LogoOptions()));
    }

    [Fact]
    public async Task GetLogoAsync_SameCodeTwice_FetchesOnce()
    {
        var inner = new FakeAirlineLogoSource { Logos = { ["EIN"] = HttpAirlineLogoSourceTests.TinyPng } };
        var cache = CreateCache(inner);

        Assert.Equal(HttpAirlineLogoSourceTests.TinyPng, await cache.GetLogoAsync("EIN", CancellationToken.None));
        Assert.Equal(HttpAirlineLogoSourceTests.TinyPng, await cache.GetLogoAsync("EIN", CancellationToken.None));

        Assert.Single(inner.Calls);
    }

    [Fact]
    public async Task GetLogoAsync_NoLogo_IsCachedAsNull()
    {
        var inner = new FakeAirlineLogoSource();
        var cache = CreateCache(inner);

        Assert.Null(await cache.GetLogoAsync("XYZ", CancellationToken.None));
        Assert.Null(await cache.GetLogoAsync("XYZ", CancellationToken.None));

        Assert.Single(inner.Calls);
    }

    [Fact]
    public async Task GetLogoAsync_SourceFails_IsNotCached()
    {
        var inner = new FakeAirlineLogoSource { Failing = true, Logos = { ["EIN"] = HttpAirlineLogoSourceTests.TinyPng } };
        var cache = CreateCache(inner);

        await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetLogoAsync("EIN", CancellationToken.None));
        inner.Failing = false;

        Assert.NotNull(await cache.GetLogoAsync("EIN", CancellationToken.None));
        Assert.Equal(2, inner.Calls.Count);
    }
}
