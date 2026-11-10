using System.ComponentModel.DataAnnotations;

namespace FlightRadar.Api.Watching;

/// <summary>Watched areas and the background poller (step 7a), bound from "Poller".</summary>
public sealed class WatchingOptions
{
    public const string SectionName = "Poller";

    /// <summary>
    /// How long one request keeps its area watched. 30 s = three missed 10 s polls, so a single slow or dropped
    /// request doesn't stop the area being refreshed, but a closed tab stops costing upstream calls within half a
    /// minute.
    /// </summary>
    [Range(5, 600)]
    public int WatchSeconds { get; set; } = 30;

    /// <summary>Whether the background poller runs. Off: requests fetch on demand through the cache, as before 7a.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Seconds between poller ticks: each tick fetches every watched area once. 10 s matches the browser's own poll,
    /// so adsb.lol sees at most 6 calls a minute per watched area, however many people watch it.
    /// </summary>
    [Range(2, 300)]
    public int IntervalSeconds { get; set; } = 10;

    /// <summary>
    /// How long an instance's claim on an area lasts (Redis <c>SET NX PX</c>). Shorter than a tick, so it has run out
    /// by the next one and any instance can claim the area again; if the claiming instance dies, someone else takes
    /// the area over within a tick.
    /// </summary>
    [Range(1, 300)]
    public int ClaimSeconds { get; set; } = 8;

    /// <summary>
    /// The most areas one tick fetches. adsb.lol is a free community service: past this, extra areas fall back to
    /// fetching on demand (when someone actually asks), rather than every tick.
    /// </summary>
    [Range(1, 1000)]
    public int MaxWatchedAreas { get; set; } = 50;
}
