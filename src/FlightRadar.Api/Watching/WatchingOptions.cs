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
}
