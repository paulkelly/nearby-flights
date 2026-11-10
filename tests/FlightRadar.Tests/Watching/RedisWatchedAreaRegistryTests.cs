using FlightRadar.Api.Caching;
using FlightRadar.Api.Watching;
using FlightRadar.Core.Geo;
using FlightRadar.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FlightRadar.Tests.Watching;

[Trait("Category", "Database")]
[Collection(RedisCollection.Name)]
public sealed class RedisWatchedAreaRegistryTests(RedisFixture redis)
{
    private static readonly SearchArea Dublin = new(new GeoPoint(53.4, -6.3), 25);
    private static readonly SearchArea London = new(new GeoPoint(51.5, -0.1), 25);

    // A fixed, realistic start time: scores are Unix milliseconds, so the clock's value really reaches Redis.
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly string key = RedisFixture.NewKey();

    private RedisWatchedAreaRegistry CreateRegistry(TimeProvider? timeProvider = null) => new(
        redis.Connection,
        Options.Create(new WatchingOptions { WatchSeconds = 30 }),
        timeProvider ?? clock,
        NullLogger<RedisWatchedAreaRegistry>.Instance,
        key);

    private IDatabase Db => redis.Connection.GetDatabase();

    [Fact]
    public async Task Mark_StoresTheAreaWithItsExpiryAsScore()
    {
        await CreateRegistry().MarkWatchedAsync(Dublin, default);

        var entry = Assert.Single(await Db.SortedSetRangeByRankWithScoresAsync(key));
        Assert.Equal("53.4:-6.3:25", entry.Element.ToString());
        Assert.Equal(clock.GetUtcNow().AddSeconds(30).ToUnixTimeMilliseconds(), entry.Score);
    }

    [Fact]
    public async Task Marked_IsWatchedUntilItsTimeRunsOut()
    {
        var registry = CreateRegistry();
        await registry.MarkWatchedAsync(Dublin, default);

        clock.Advance(TimeSpan.FromSeconds(29.999));
        Assert.Equal([Dublin], await registry.GetWatchedAreasAsync(default));

        clock.Advance(TimeSpan.FromMilliseconds(1)); // score == now: expired (the range excludes its start)
        Assert.Empty(await registry.GetWatchedAreasAsync(default));
    }

    [Fact]
    public async Task TwoInstances_ShareOneList()
    {
        // Two registries on one Redis key = two API instances: a viewer on one is visible to the poller on the other.
        await CreateRegistry().MarkWatchedAsync(Dublin, default);
        await CreateRegistry().MarkWatchedAsync(London, default);

        var watched = await CreateRegistry().GetWatchedAreasAsync(default);

        Assert.Equal([London, Dublin], watched.OrderBy(area => area.Centre.Latitude));
    }

    [Fact]
    public async Task Mark_FromALaggingClock_NeverShortensTheExpiry()
    {
        await CreateRegistry().MarkWatchedAsync(Dublin, default);
        var laggingClock = new FakeTimeProvider(clock.GetUtcNow().AddSeconds(-10)); // an instance 10 s behind

        await CreateRegistry(laggingClock).MarkWatchedAsync(Dublin, default); // ZADD GT: ignored, it's earlier

        clock.Advance(TimeSpan.FromSeconds(25)); // would have expired at 20 s if the lagging mark had won
        Assert.Equal([Dublin], await CreateRegistry().GetWatchedAreasAsync(default));
    }

    [Fact]
    public async Task Read_IgnoresMembersThatAreNotAreas()
    {
        await Db.SortedSetAddAsync(key, "rubbish", double.PositiveInfinity);
        await CreateRegistry().MarkWatchedAsync(Dublin, default);

        Assert.Equal([Dublin], await CreateRegistry().GetWatchedAreasAsync(default));
    }

    [Fact]
    public async Task Mark_RedisUnreachable_DoesNotThrow()
    {
        // Port 1: nothing listens there. Watching is an optimisation; a request must never fail because of it.
        var options = RedisCacheServiceCollectionExtensions.CreateConfigurationOptions("127.0.0.1:1");
        await using var unreachable = await ConnectionMultiplexer.ConnectAsync(options);
        var registry = new RedisWatchedAreaRegistry(
            unreachable, Options.Create(new WatchingOptions()), clock, NullLogger<RedisWatchedAreaRegistry>.Instance, key);

        await registry.MarkWatchedAsync(Dublin, default);
    }

    // ---- Pruning ----

    [Fact]
    public async Task PruneExpired_RemovesExpiredMembersFromRedis()
    {
        var registry = CreateRegistry();
        await registry.MarkWatchedAsync(Dublin, default);
        clock.Advance(TimeSpan.FromSeconds(20));
        await registry.MarkWatchedAsync(London, default);
        clock.Advance(TimeSpan.FromSeconds(15)); // Dublin expired 5 s ago; London has 15 s left

        await registry.PruneExpiredAsync(default);

        // Look at Redis itself, not GetWatchedAreasAsync: that already hides expired areas, pruned or not.
        var remaining = await Db.SortedSetRangeByRankAsync(key);
        Assert.Equal(["51.5:-0.1:25"], remaining.Select(member => member.ToString()));
    }

    [Fact]
    public async Task PruneExpired_AreaExpiringExactlyNow_IsRemoved()
    {
        var registry = CreateRegistry();
        await registry.MarkWatchedAsync(Dublin, default);
        clock.Advance(TimeSpan.FromSeconds(30));

        await registry.PruneExpiredAsync(default);

        // Look at Redis itself, not GetWatchedAreasAsync: that already hides expired areas, pruned or not.
        Assert.Empty(await Db.SortedSetRangeByRankAsync(key));
    }

    [Fact]
    public async Task PruneExpired_AreaExpiringJustAfterNow_IsKept()
    {
        // The other side of the boundary: one millisecond of watching left means it's still watched.
        var registry = CreateRegistry();
        await registry.MarkWatchedAsync(Dublin, default);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await registry.MarkWatchedAsync(London, default);
        clock.Advance(TimeSpan.FromSeconds(30) - TimeSpan.FromMilliseconds(1)); // Dublin: exactly now; London: 1 ms left

        await registry.PruneExpiredAsync(default);

        var remaining = await Db.SortedSetRangeByRankAsync(key);
        Assert.Equal(["51.5:-0.1:25"], remaining.Select(member => member.ToString()));
    }
}
