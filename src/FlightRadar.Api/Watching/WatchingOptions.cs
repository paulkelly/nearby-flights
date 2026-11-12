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

    /// <summary>
    /// How often an area with a home-screen widget is fetched when nobody is looking at it (step 9c). Slower than
    /// live viewers: a widget is a glance, and these run around the clock. An area someone is also viewing gets
    /// the viewers' rate anyway. At least <see cref="IntervalSeconds"/> (checked at startup).
    /// </summary>
    [Range(2, 3600)]
    public int WidgetIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// The most widget areas watched around the clock, for everyone together. adsb.lol is a free community service.
    /// The rest fall back to the widget's own refresh (every 15 min, or a tap). 0 = no widget watching.
    /// </summary>
    [Range(0, 100)]
    public int MaxWidgetAreas { get; set; } = 10;

    /// <summary>How many of one owner's locations count towards <see cref="MaxWidgetAreas"/>: their oldest.</summary>
    [Range(1, 20)]
    public int WidgetLocationsPerOwner { get; set; } = 3;

    /// <summary>
    /// A device that hasn't registered for this long (the app does at least daily) is forgotten: uninstalled, or
    /// a phone that's been off. It starts again by itself when the app next registers.
    /// </summary>
    [Range(1, 365)]
    public int WidgetDeviceDays { get; set; } = 7;
}
