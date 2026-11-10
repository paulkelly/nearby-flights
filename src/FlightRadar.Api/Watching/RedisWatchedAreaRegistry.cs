using FlightRadar.Api.Caching;
using FlightRadar.Core.Geo;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FlightRadar.Api.Watching;

/// <summary>
/// Watched areas in Redis, shared by every API instance: a request to instance A and the poller on instance B see
/// the same list.
/// </summary>
/// <remarks>
/// One <b>sorted set</b>: each member is an area (<see cref="WatchedAreaKey"/>), its score the time it stops being
/// watched (Unix milliseconds). Redis can expire whole keys but not single members of a set, so the expiry goes in
/// the score instead, and every operation is a range of scores:
/// <list type="bullet">
/// <item>mark: <c>ZADD key GT &lt;until&gt; &lt;area&gt;</c>, adds it, or moves its expiry later (GT: never earlier);</item>
/// <item>read: <c>ZRANGE key (&lt;now&gt; +inf BYSCORE</c>, everything still in the future;</item>
/// <item>prune: remove everything at or before now.</item>
/// </list>
/// Your leaderboard used scores for points; here they're deadlines. The same structure: members kept sorted by a
/// number, and fast range queries over it.
/// </remarks>
public sealed partial class RedisWatchedAreaRegistry(
    IConnectionMultiplexer redis,
    IOptions<WatchingOptions> options,
    TimeProvider timeProvider,
    ILogger<RedisWatchedAreaRegistry> logger,
    string key = RedisWatchedAreaRegistry.DefaultKey) : IWatchedAreaRegistry
{
    public const string DefaultKey = RedisCacheServiceCollectionExtensions.KeyPrefix + "watched-areas";

    private readonly RedisKey _key = key;
    private readonly TimeSpan _watchFor = TimeSpan.FromSeconds(options.Value.WatchSeconds);

    public string Name => "live viewers";

    private long NowMs => timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    public async Task MarkWatchedAsync(SearchArea area, CancellationToken cancellationToken)
    {
        long untilMs = NowMs + (long)_watchFor.TotalMilliseconds;
        try
        {
            // GT: two instances' clocks can differ slightly; never let one shorten what another has already granted.
            await redis.GetDatabase().SortedSetAddAsync(_key, WatchedAreaKey.Format(area), untilMs, SortedSetWhen.GreaterThan);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            // Redis down: the area just isn't polled in the background; the request itself is unaffected (the cache
            // falls back to fetching on demand). Debug, not Warning: this would repeat for every request, and the
            // Redis health check already reports the outage.
            LogMarkFailed(logger, exception.Message);
        }
    }

    public async Task<IReadOnlyCollection<SearchArea>> GetWatchedAreasAsync(CancellationToken cancellationToken)
    {
        // Scores strictly after now (Exclude.Start: "(now" in Redis syntax): an area whose time is exactly now has
        // just expired. Lets Redis failures through: the poller decides what a failed read means.
        var members = await redis.GetDatabase().SortedSetRangeByScoreAsync(
            _key, start: NowMs, stop: double.PositiveInfinity, exclude: Exclude.Start);

        var areas = new List<SearchArea>(members.Length);
        foreach (var member in members)
        {
            if (WatchedAreaKey.TryParse(member, out var area))
            {
                areas.Add(area.Value);
            }
        }
        return areas;
    }

    public async Task PruneExpiredAsync(CancellationToken cancellationToken)
    {
        // ZREMRANGEBYSCORE key -inf <now>: both ends inclusive, so a score of exactly now goes too, the same boundary
        // GetWatchedAreasAsync treats as expired. -inf catches anything however old (a far-behind clock, old data).
        // A request renewing an area at the same moment is safe either way: each command is atomic, so either its
        // ZADD lands first (score in the future, not removed) or the prune does (and the ZADD adds it back).
        await redis.GetDatabase().SortedSetRemoveRangeByScoreAsync(_key, double.NegativeInfinity, NowMs);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Couldn't mark an area as watched: {Reason}")]
    private static partial void LogMarkFailed(ILogger logger, string reason);
}
