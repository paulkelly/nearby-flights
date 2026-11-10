using FlightRadar.Api.Watching;
using FlightRadar.Core.Geo;
using FlightRadar.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace FlightRadar.Tests.Watching;

public sealed class InMemoryWatchedAreaRegistryTests
{
    private static readonly SearchArea Dublin = new(new GeoPoint(53.4, -6.3), 25);
    private static readonly SearchArea London = new(new GeoPoint(51.5, -0.1), 25);

    private readonly FakeTimeProvider clock = new(DateTimeOffset.UnixEpoch);
    private readonly InMemoryWatchedAreaRegistry registry;

    public InMemoryWatchedAreaRegistryTests() =>
        registry = new InMemoryWatchedAreaRegistry(Options.Create(new WatchingOptions { WatchSeconds = 30 }), clock);

    private Task<IReadOnlyCollection<SearchArea>> WatchedAsync() => registry.GetWatchedAreasAsync(default);

    [Fact]
    public async Task Marked_IsWatchedUntilItsTimeRunsOut()
    {
        await registry.MarkWatchedAsync(Dublin, default);

        clock.Advance(TimeSpan.FromSeconds(29.999));
        Assert.Equal([Dublin], await WatchedAsync());

        clock.Advance(TimeSpan.FromMilliseconds(1)); // exactly 30 s: expired
        Assert.Empty(await WatchedAsync());
    }

    [Fact]
    public async Task MarkedAgain_StaysWatchedLonger()
    {
        await registry.MarkWatchedAsync(Dublin, default);
        clock.Advance(TimeSpan.FromSeconds(20));
        await registry.MarkWatchedAsync(Dublin, default); // the next poll

        clock.Advance(TimeSpan.FromSeconds(20)); // 40 s after the first mark, 20 s after the second

        Assert.Equal([Dublin], await WatchedAsync());
    }

    [Fact]
    public async Task SeveralAreas_ExpireIndependently()
    {
        await registry.MarkWatchedAsync(Dublin, default);
        clock.Advance(TimeSpan.FromSeconds(20));
        await registry.MarkWatchedAsync(London, default);

        clock.Advance(TimeSpan.FromSeconds(15));

        Assert.Equal([London], await WatchedAsync());
    }

    [Fact]
    public async Task PruneExpired_ForgetsOnlyExpiredAreas()
    {
        await registry.MarkWatchedAsync(Dublin, default);
        clock.Advance(TimeSpan.FromSeconds(20));
        await registry.MarkWatchedAsync(London, default);
        clock.Advance(TimeSpan.FromSeconds(15));

        await registry.PruneExpiredAsync(default);
        clock.Advance(TimeSpan.FromSeconds(-30)); // back to when both were watched: only what survived pruning remains

        Assert.Equal([London], await WatchedAsync());
    }
}
