using System.Collections.Concurrent;
using FlightRadar.Core.Geo;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Watching;

/// <summary>
/// Watched areas in this process's memory: used when Redis isn't configured, where there's only one instance to
/// know about anyway. Same behaviour as <see cref="RedisWatchedAreaRegistry"/>, without sharing.
/// </summary>
public sealed class InMemoryWatchedAreaRegistry(IOptions<WatchingOptions> options, TimeProvider timeProvider)
    : IWatchedAreaRegistry
{
    private readonly ConcurrentDictionary<SearchArea, DateTimeOffset> _watchedUntil = new();
    private readonly TimeSpan _watchFor = TimeSpan.FromSeconds(options.Value.WatchSeconds);

    public string Name => "live viewers";

    public Task MarkWatchedAsync(SearchArea area, CancellationToken cancellationToken)
    {
        var until = timeProvider.GetUtcNow() + _watchFor;
        // Only ever later, never earlier, like ZADD GT in the Redis version.
        _watchedUntil.AddOrUpdate(area, until, (_, existing) => existing > until ? existing : until);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<SearchArea>> GetWatchedAreasAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        IReadOnlyCollection<SearchArea> areas = _watchedUntil
            .Where(entry => entry.Value > now)
            .Select(entry => entry.Key)
            .ToList();
        return Task.FromResult(areas);
    }

    public Task PruneExpiredAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (area, until) in _watchedUntil)
        {
            if (until <= now)
            {
                // Removes only if it still has the value we saw: a request renewing it meanwhile wins.
                _watchedUntil.TryRemove(new KeyValuePair<SearchArea, DateTimeOffset>(area, until));
            }
        }
        return Task.CompletedTask;
    }
}
