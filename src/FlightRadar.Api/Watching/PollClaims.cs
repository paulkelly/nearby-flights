using FlightRadar.Api.Caching;
using FlightRadar.Core.Geo;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FlightRadar.Api.Watching;

/// <summary>
/// Decides which API instance fetches an area on this tick, so two instances never both call adsb.lol for it.
/// </summary>
public interface IPollClaims
{
    /// <summary>True if this instance should fetch the area now; false if another instance already is.</summary>
    Task<bool> TryClaimAsync(SearchArea area, CancellationToken cancellationToken);
}

/// <summary>Without Redis there's only one instance, so it always gets the area.</summary>
public sealed class SingleInstancePollClaims : IPollClaims
{
    public Task<bool> TryClaimAsync(SearchArea area, CancellationToken cancellationToken) => Task.FromResult(true);
}

/// <summary>
/// One short-lived Redis key per area and tick: <c>SET flightradar:poll-claim:&lt;area&gt; &lt;instance&gt; NX PX
/// &lt;ClaimSeconds&gt;</c>. NX = only if nobody holds it, so exactly one instance's SET succeeds; PX = it lets go
/// by itself, so a crashed instance never keeps an area. The simplest real distributed lock.
/// </summary>
/// <remarks>
/// Nobody ever releases a claim early: it just runs out before the next tick (ClaimSeconds &lt; IntervalSeconds).
/// That also stops a fast instance fetching an area twice in one tick. The value (which instance) is only for people
/// looking in redis-cli.
/// </remarks>
public sealed class RedisPollClaims(IConnectionMultiplexer redis, IOptions<WatchingOptions> options) : IPollClaims
{
    public const string KeyPrefix = RedisCacheServiceCollectionExtensions.KeyPrefix + "poll-claim:";

    /// <summary>Container hostname (Docker sets it to the container ID) and process ID: which instance holds it.</summary>
    private static readonly string Instance = $"{Environment.MachineName}:{Environment.ProcessId}";

    private readonly TimeSpan _claimFor = TimeSpan.FromSeconds(options.Value.ClaimSeconds);

    public Task<bool> TryClaimAsync(SearchArea area, CancellationToken cancellationToken) =>
        redis.GetDatabase().StringSetAsync(KeyPrefix + WatchedAreaKey.Format(area), Instance, _claimFor, When.NotExists);
}
