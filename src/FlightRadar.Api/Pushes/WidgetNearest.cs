using FlightRadar.Core.Flights;

namespace FlightRadar.Api.Pushes;

/// <summary>
/// Which aircraft a widget shows, the server's copy of the app's <c>chooseForWidget</c> (step 9d): the nearest in the
/// air, else the nearest on the ground. Plus hysteresis, like the website's nearest card: two aircraft at about the
/// same distance would otherwise swap every minute, and every swap is a push.
/// </summary>
public static class WidgetNearest
{
    /// <summary>Another aircraft takes over only when it's this much closer than the current one (10%).</summary>
    public const double SwitchFactor = 0.9;

    /// <param name="inCircle">The aircraft inside the location's exact circle, with distances.</param>
    /// <param name="currentHex">The aircraft chosen last time, or null.</param>
    public static NearbyFlight? Choose(IReadOnlyList<NearbyFlight> inCircle, string? currentHex)
    {
        var airborne = inCircle.Where(nearby => !nearby.Flight.OnGround).ToList();
        IReadOnlyList<NearbyFlight> candidates = airborne.Count > 0 ? airborne : inCircle;
        if (candidates.Count == 0)
        {
            return null;
        }

        var nearest = candidates.MinBy(nearby => nearby.DistanceKm)!;
        // Keep the current one while it's still a candidate (still here, and still in the air if anything is),
        // unless the nearest is clearly closer.
        var current = currentHex is null ? null : candidates.FirstOrDefault(nearby => nearby.Flight.Hex == currentHex);
        return current is not null && nearest.DistanceKm >= current.DistanceKm * SwitchFactor ? current : nearest;
    }
}

/// <summary>
/// The aircraft each watched location showed last time (by hex; null = nothing near). A singleton, in memory:
/// Azure runs one instance, and after a restart the first result is a fresh starting point that isn't pushed (a
/// deploy shouldn't push to every phone). Like a game server's last-replicated state per client: only deltas go out.
/// </summary>
public sealed class WidgetNearestState
{
    private readonly Dictionary<Guid, string?> _lastHex = [];
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
        }
    }
}
