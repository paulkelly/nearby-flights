using FlightRadar.Api.Watching;
using FlightRadar.Core.Geo;

namespace FlightRadar.Api.Streaming;

/// <summary>
/// Which area each WebSocket connection on <em>this</em> instance is watching, plus each connection's
/// <see cref="WatchThrottle"/>. Kept in memory: a connection lives on exactly one instance, and disappears with it.
/// </summary>
/// <remarks>
/// Also a watched-area source for the poller (7a's design paying off: nothing in the poller changes). Each instance's
/// poller sees its own connections' areas, plus the shared Redis registry; per-area claims make sure only one instance
/// fetches each area, and the Redis backplane delivers its push to connections on every instance.
/// </remarks>
public sealed class FlightSubscriptions(TimeProvider timeProvider) : IWatchedAreaSource
{
    private sealed class Connection(DateTimeOffset now)
    {
        public WatchThrottle Throttle { get; } = new(now);
        public SearchArea? Area { get; set; }
    }

    private readonly Dictionary<string, Connection> _connections = [];
    private readonly Lock _lock = new();

    public string Name => "live connections";

    /// <summary>
    /// Records that the connection now watches <paramref name="area"/>, if its throttle allows a change. Returns the
    /// area it watched before (null if none), or <c>Allowed: false</c> and how long to wait.
    /// </summary>
    public (bool Allowed, SearchArea? Previous, TimeSpan RetryAfter) TryWatch(string connectionId, SearchArea area)
    {
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (!_connections.TryGetValue(connectionId, out var connection))
            {
                _connections[connectionId] = connection = new Connection(now);
            }

            if (!connection.Throttle.TryTake(now))
            {
                return (false, connection.Area, connection.Throttle.WaitFor(now));
            }

            var previous = connection.Area;
            connection.Area = area;
            return (true, previous, TimeSpan.Zero);
        }
    }

    /// <summary>Stops watching (the connection stays, with its throttle). Returns the area it was watching.</summary>
    public SearchArea? Unwatch(string connectionId)
    {
        lock (_lock)
        {
            if (!_connections.TryGetValue(connectionId, out var connection))
            {
                return null;
            }
            var previous = connection.Area;
            connection.Area = null;
            return previous;
        }
    }

    /// <summary>The connection closed: forget it entirely.</summary>
    public void Remove(string connectionId)
    {
        lock (_lock)
        {
            _connections.Remove(connectionId);
        }
    }

    public Task<IReadOnlyCollection<SearchArea>> GetWatchedAreasAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyCollection<SearchArea> areas = _connections.Values
                .Select(connection => connection.Area)
                .OfType<SearchArea>()
                .Distinct()
                .ToList();
            return Task.FromResult(areas);
        }
    }
}
