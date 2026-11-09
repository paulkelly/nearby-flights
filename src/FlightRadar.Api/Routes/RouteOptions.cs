using System.ComponentModel.DataAnnotations;

namespace FlightRadar.Api.Routes;

/// <summary>Settings for route lookups, bound from the "Routes" config section.</summary>
public sealed class RouteOptions
{
    public const string SectionName = "Routes";

    /// <summary>Root of the route files (one JSON file per callsign). Must end with "/".</summary>
    [Required]
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// How long /api/flights waits for route lookups before answering without the ones still running. Those carry
    /// on in the background and fill the cache, so the next poll (10 s later) has them. Routes never hold up the
    /// flights by more than this.
    /// </summary>
    [Range(0, 5000)]
    public int WaitMilliseconds { get; set; } = 300;

    /// <summary>
    /// How long a route (or "no route known") is cached per callsign. The source updates hourly and a flight keeps
    /// its callsign for the whole flight, so a long cache costs nothing in freshness.
    /// </summary>
    [Range(1, 24 * 60)]
    public int CacheMinutes { get; set; } = 60;

    /// <summary>Budget for one lookup, retries included. Lookups run in the background, so this only bounds them.</summary>
    [Range(1, 60)]
    public int TotalTimeoutSeconds { get; set; } = 5;

    [Range(1, 30)]
    public int AttemptTimeoutSeconds { get; set; } = 2;

    [Range(0, 5)]
    public int MaxRetries { get; set; } = 1;

    /// <summary>
    /// Most lookups in flight at once. A first search over a busy area can need ~100 routes; this keeps that from
    /// arriving at the (free) source as one burst. Cached callsigns don't count: they never reach the network.
    /// </summary>
    [Range(1, 100)]
    public int MaxConcurrentRequests { get; set; } = 8;

    /// <summary>Lookups that may wait for a free slot; beyond that they fail (and are retried on a later poll).</summary>
    [Range(0, 1000)]
    public int MaxQueuedRequests { get; set; } = 200;
}
