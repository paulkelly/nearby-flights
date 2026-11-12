using FlightRadar.Core.Geo;

namespace FlightRadar.Api.Devices;

/// <summary>One device asking for one location, its area already snapped like the cache's keys.</summary>
/// <param name="Since">When the device first asked: the caps go to the oldest first.</param>
public sealed record WidgetWatch(Guid OwnerId, Guid LocationId, SearchArea Area, DateTimeOffset Since);

/// <summary>Which areas the widgets get, and how many requests the caps turned away (for the log).</summary>
public sealed record WidgetWatchPlan(IReadOnlyList<SearchArea> Areas, int LocationsOverOwnerLimit, int AreasOverLimit);

/// <summary>
/// The rules for which widget locations the server keeps fresh, and when, as pure functions (step 9c). Like a game's
/// update scheduler giving a few far-away entities a slow tick: a fixed budget, handed out first come, first served.
/// </summary>
public static class WidgetWatchPlanner
{
    /// <summary>
    /// Applies the caps: each owner gets their <paramref name="perOwner"/> oldest locations, then the first
    /// <paramref name="maxAreas"/> distinct areas in that same order are watched. Two locations in one area (two
    /// people with a widget for the same airport, say) share one slot, since they cost one upstream call.
    /// </summary>
    public static WidgetWatchPlan Choose(IEnumerable<WidgetWatch> watches, int perOwner, int maxAreas)
    {
        // A location shown on two devices (a phone and a tablet restored from it) counts once, as old as its oldest.
        var locations = watches
            .GroupBy(watch => watch.LocationId)
            .Select(group => group.MinBy(watch => watch.Since)!)
            .OrderBy(watch => watch.Since)
            .ThenBy(watch => watch.LocationId); // a fixed order for ties, so the choice doesn't flicker between reloads

        var perOwnerCount = new Dictionary<Guid, int>();
        var seen = new HashSet<SearchArea>();
        var areas = new List<SearchArea>();
        int overOwner = 0, overAreas = 0;
        foreach (var watch in locations)
        {
            int count = perOwnerCount.GetValueOrDefault(watch.OwnerId);
            if (count >= perOwner)
            {
                overOwner++;
                continue;
            }
            perOwnerCount[watch.OwnerId] = count + 1;

            if (!seen.Add(watch.Area))
            {
                continue; // already watched for someone else (or another of this owner's locations): free
            }
            if (areas.Count < maxAreas)
            {
                areas.Add(watch.Area);
            }
            else
            {
                overAreas++;
            }
        }

        return new WidgetWatchPlan(areas, overOwner, overAreas);
    }

    /// <summary>
    /// The areas due this poller tick: never handed out, or handed out at least <paramref name="every"/> ago. Half a
    /// tick of slack, because ticks don't land exactly on the second: an area last handed out 59.98 s ago would
    /// otherwise wait a whole extra tick (70 s instead of 60).
    /// </summary>
    public static IReadOnlyList<SearchArea> Due(
        IEnumerable<SearchArea> areas,
        IReadOnlyDictionary<SearchArea, DateTimeOffset> lastHandedOut,
        DateTimeOffset now,
        TimeSpan every,
        TimeSpan tick) =>
        areas
            .Where(area => !lastHandedOut.TryGetValue(area, out var last) || now - last >= every - tick / 2)
            .ToList();
}
