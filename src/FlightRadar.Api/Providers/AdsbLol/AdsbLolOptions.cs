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
}
