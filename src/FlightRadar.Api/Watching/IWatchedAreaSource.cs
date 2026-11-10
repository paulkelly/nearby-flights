using FlightRadar.Core.Geo;

namespace FlightRadar.Api.Watching;

/// <summary>
/// Somewhere areas worth keeping fresh come from. The poller (step 7a) asks every registered source each tick and
/// fetches the union, so new reasons to watch an area plug in as new sources without changing the poller:
/// live viewers now (<see cref="IWatchedAreaRegistry"/>), saved locations with an Android widget later (step 9).
/// </summary>
/// <remarks>Areas are always snapped (<see cref="AreaSnapping"/>), so they line up with the cache's keys.</remarks>
public interface IWatchedAreaSource
{
    /// <summary>For logs: which source an area came from.</summary>
    string Name { get; }

    Task<IReadOnlyCollection<SearchArea>> GetWatchedAreasAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The areas people are looking at right now. Each <c>/api/flights</c> request marks its area as watched for
/// <see cref="WatchingOptions.WatchSeconds"/>; while browsers keep polling it stays watched, and when they stop
/// (tab closed or hidden) it expires on its own. Like an MMO zone that sleeps once its last player leaves.
/// </summary>
public interface IWatchedAreaRegistry : IWatchedAreaSource
{
    /// <summary>Marks the area watched until now + WatchSeconds. Never throws: watching is an optimisation.</summary>
    Task MarkWatchedAsync(SearchArea area, CancellationToken cancellationToken);

    /// <summary>
    /// Forgets areas whose time has passed. Reading already ignores them, so this only stops the registry growing
    /// without limit; the poller calls it now and then.
    /// </summary>
    Task PruneExpiredAsync(CancellationToken cancellationToken);
}
