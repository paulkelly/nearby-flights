using FlightRadar.Api.Caching;
using FlightRadar.Core.Geo;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Watching;

/// <summary>What one tick did, for the log line and the tests.</summary>
public sealed record PollerTickResult(int Watched, int Fetched, int ClaimedElsewhere, int Failed, int OverLimit);

/// <summary>
/// One poller tick: gather the watched areas from every source, then fetch each one this instance claims and put
/// the result in the cache. The loop around it (timing, scopes) is <see cref="FlightPoller"/>; keeping the tick
/// separate means tests can run single ticks directly instead of waiting for a timer.
/// </summary>
public sealed partial class PollerTick(
    IEnumerable<IWatchedAreaSource> sources,
    IWatchedAreaRegistry liveViewers,
    IPollClaims claims,
    CachingFlightDataProvider cache,
    IOptions<WatchingOptions> options,
    IOptions<FlightCacheOptions> cacheOptions,
    ILogger<PollerTick> logger,
    IDistributedCache? sharedCache = null) // optional: only registered when Redis is configured
{
    /// <summary>
    /// Areas fetched at the same time. Low on purpose: adsb.lol's client allows 10 calls in flight for the whole
    /// instance, and user requests (cache misses, areas over the limit) need some of them too.
    /// </summary>
    private const int MaxParallelFetches = 4;

    public async Task<PollerTickResult> RunAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var areas = await GatherAreasAsync(cancellationToken);

        int overLimit = Math.Max(0, areas.Count - settings.MaxWatchedAreas);
        if (overLimit > 0)
        {
            LogOverLimit(logger, areas.Count, settings.MaxWatchedAreas);
            areas = areas.Take(settings.MaxWatchedAreas).ToList();
        }

        // Entries outlive two ticks, so one slow or failed tick doesn't empty the cache. With Redis shared between
        // instances, this process's own in-memory copy stays short-lived (the usual TTL): another instance's poller
        // may be the one refreshing Redis, and a long local copy would keep serving older data than Redis has.
        var keepFor = TimeSpan.FromSeconds(settings.IntervalSeconds * 2 + 5);
        var keepLocallyFor = sharedCache is not null ? TimeSpan.FromSeconds(cacheOptions.Value.TtlSeconds) : keepFor;

        int fetched = 0, claimedElsewhere = 0, failed = 0;
        await Parallel.ForEachAsync(
            areas,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelFetches, CancellationToken = cancellationToken },
            async (area, ct) =>
            {
                try
                {
                    if (!await claims.TryClaimAsync(area, ct))
                    {
                        Interlocked.Increment(ref claimedElsewhere);
                        return;
                    }

                    await cache.RefreshAsync(area, keepFor, keepLocallyFor, ct);
                    Interlocked.Increment(ref fetched);
                }
                catch (Exception exception) when (!ct.IsCancellationRequested)
                {
                    // One area failing (adsb.lol down, Redis hiccup) never stops the others. Requests for it still
                    // work: they fetch on demand through the cache, as before 7a. Debug: during an outage this would
                    // be every area every tick; the tick's summary (FlightPoller) reports the count as a Warning.
                    Interlocked.Increment(ref failed);
                    LogAreaFailed(logger, CachingFlightDataProvider.CacheKey(area.Centre, area.RadiusKm), exception.Message);
                }
            });

        await PruneAsync(cancellationToken);

        return new PollerTickResult(areas.Count + overLimit, fetched, claimedElsewhere, failed, overLimit);
    }

    /// <summary>The union of every source's areas. A source that fails is skipped for this tick.</summary>
    private async Task<List<SearchArea>> GatherAreasAsync(CancellationToken cancellationToken)
    {
        var areas = new HashSet<SearchArea>();
        foreach (var source in sources)
        {
            try
            {
                areas.UnionWith(await source.GetWatchedAreasAsync(cancellationToken));
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogSourceFailed(logger, source.Name, exception.Message);
            }
        }
        return areas.ToList();
    }

    /// <summary>Every tick: cheap (one command), and with several instances it doesn't matter who does it.</summary>
    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        try
        {
            await liveViewers.PruneExpiredAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogPruneFailed(logger, exception.Message);
        }
    }

    // Warning: worth noticing (the limit may need raising, or something is creating lots of areas).
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "{Watched} watched areas, over the limit of {Limit}: the rest are fetched on demand only")]
    private static partial void LogOverLimit(ILogger logger, int watched, int limit);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Poller couldn't refresh {CacheKey}: {Reason}")]
    private static partial void LogAreaFailed(ILogger logger, string cacheKey, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Poller couldn't read watched areas from {Source}: {Reason}")]
    private static partial void LogSourceFailed(ILogger logger, string source, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Poller couldn't prune expired areas: {Reason}")]
    private static partial void LogPruneFailed(ILogger logger, string reason);
}
