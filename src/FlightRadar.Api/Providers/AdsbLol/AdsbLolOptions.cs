using System.ComponentModel.DataAnnotations;

namespace FlightRadar.Api.Providers.AdsbLol;

/// <summary>Settings for the adsb.lol provider, bound from the "Providers:AdsbLol" config section.</summary>
public sealed class AdsbLolOptions
{
    public const string SectionName = "Providers:AdsbLol";

    /// <summary>
    /// API root. Must end with "/": relative paths are resolved like links in a web page, so without the
    /// trailing slash the last segment of the base URL would be replaced rather than appended to.
    /// </summary>
    [Required]
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// Budget for the whole call, retries included. The browser polls every 10 s, so an answer later than this
    /// is no longer useful.
    /// </summary>
    [Range(1, 60)]
    public int TotalTimeoutSeconds { get; set; } = 8;

    /// <summary>Budget for a single try, so one hung connection can't use up the whole total.</summary>
    [Range(1, 30)]
    public int AttemptTimeoutSeconds { get; set; } = 3;

    /// <summary>Retries after the first attempt, for transient failures only (5xx, 408, 429, network errors).</summary>
    [Range(0, 5)]
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Most calls to adsb.lol in flight at once, from this instance, whoever they're for. The per-client limit on
    /// /api/flights caps each user; this caps the total, so a crowd of well-behaved users (or a burst of cache misses)
    /// can't flood a free community service. Retries of a call reuse its slot.
    /// </summary>
    [Range(1, 100)]
    public int MaxConcurrentRequests { get; set; } = 10;

    /// <summary>
    /// Calls allowed to wait for a free slot when all are busy; beyond that they fail at once (→ 502). A short queue
    /// smooths a spike without letting waits pile up: the browser asks again in 10 s anyway.
    /// </summary>
    [Range(0, 1000)]
    public int MaxQueuedRequests { get; set; } = 20;
}
