using FlightRadar.Core.Flights;

namespace FlightRadar.Api.Pushes;

/// <summary>
/// Which aircraft a widget shows, the server's copy of the app's <c>chooseForWidget</c> (step 9d): the nearest in the
/// air, never one on the ground (near an airport that would be a parked plane most of the day; a landing isn't worth
/// showing at one update a minute). Plus hysteresis, like the website's nearest card: two aircraft at about the same
/// distance would otherwise swap every minute, and every swap is a push.
/// </summary>
public static class WidgetNearest
{
    /// <summary>Another aircraft takes over only when it's this much closer than the current one (10%).</summary>
    public const double SwitchFactor = 0.9;

    /// <param name="inCircle">The aircraft inside the location's exact circle, with distances.</param>
    /// <param name="currentHex">The aircraft chosen last time, or null.</param>
    public static NearbyFlight? Choose(IReadOnlyList<NearbyFlight> inCircle, string? currentHex)
    {
        var candidates = inCircle.Where(nearby => !nearby.Flight.OnGround).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var nearest = candidates.MinBy(nearby => nearby.DistanceKm)!;
        // Keep the current one while it's still a candidate (still here and still in the air), unless the nearest is
        // clearly closer.
        var current = currentHex is null ? null : candidates.FirstOrDefault(nearby => nearby.Flight.Hex == currentHex);
        return current is not null && nearest.DistanceKm >= current.DistanceKm * SwitchFactor ? current : nearest;
    }
}

/// <summary>
/// The aircraft each watched location showed last time (by hex; null = nothing near). A singleton, in memory:
/// Azure runs one instance, and after a restart the first result is a fresh starting point that isn't pushed (a
/// deploy shouldn't push to every phone). Like a game server's last-replicated state per client: only deltas go out.
/// Also when each location was last pushed, so refreshes keep to one a minute however often its area is polled.
/// </summary>
public sealed class WidgetNearestState
{
    private readonly Dictionary<Guid, string?> _lastHex = [];
    private readonly Dictionary<Guid, DateTimeOffset> _lastPushedAt = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Chooses the location's aircraft now. <c>Changed</c> is true when it differs from last time; never on the first
    /// observation.
    /// </summary>
    public (bool Changed, NearbyFlight? Chosen) Update(Guid locationId, IReadOnlyList<NearbyFlight> inCircle)
    {
        lock (_lock)
        {
            bool seen = _lastHex.TryGetValue(locationId, out var previous);
            var chosen = WidgetNearest.Choose(inCircle, previous);
            var hex = chosen?.Flight.Hex;
            _lastHex[locationId] = hex;
            return (seen && hex != previous, chosen);
        }
    }

    /// <summary>
    /// Whether a refresh push is due: never pushed, or at least <paramref name="every"/> since the last push (a
    /// change or a refresh). Like a network tick rate: the area may be polled every 10 s for a browser watching it,
    /// but the widget gets one refresh a minute either way.
    /// </summary>
    public bool RefreshDue(Guid locationId, DateTimeOffset now, TimeSpan every)
    {
        lock (_lock)
        {
            return !_lastPushedAt.TryGetValue(locationId, out var last) || now - last >= every;
        }
    }

    /// <summary>Records a push for the location (either kind), restarting its refresh interval.</summary>
    public void MarkPushed(Guid locationId, DateTimeOffset now)
    {
        lock (_lock)
        {
            _lastPushedAt[locationId] = now;
        }
    }

    /// <summary>
    /// Forgets locations no longer watched, so one watched again later starts afresh (it may have moved, or gone
    /// unwatched for days).
    /// </summary>
    public void KeepOnly(IEnumerable<Guid> watched)
    {
        var keep = watched.ToHashSet();
        lock (_lock)
        {
            foreach (var id in _lastHex.Keys.Where(id => !keep.Contains(id)).ToList())
            {
                _lastHex.Remove(id);
            }
            foreach (var id in _lastPushedAt.Keys.Where(id => !keep.Contains(id)).ToList())
            {
                _lastPushedAt.Remove(id);
            }
        }
    }
}
