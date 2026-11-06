using System.ComponentModel.DataAnnotations;

namespace FlightRadar.Api.Providers.OpenSky;

/// <summary>Settings for the OpenSky provider, bound from the "Providers:OpenSky" config section.</summary>
/// <remarks>
/// Without credentials, requests are anonymous: 400 credits a day and 10-second data. With an account (stage 5f
/// piece 3), 4,000 a day and 5-second data. A box up to 25 square degrees (a ~250 km search) costs 1 credit.
/// </remarks>
public sealed class OpenSkyOptions
{
    public const string SectionName = "Providers:OpenSky";

    /// <summary>API root; must end with "/" (see AdsbLolOptions.BaseUrl).</summary>
    [Required]
    public Uri? BaseUrl { get; set; }

    [Range(1, 60)]
    public int TotalTimeoutSeconds { get; set; } = 8;

    [Range(1, 30)]
    public int AttemptTimeoutSeconds { get; set; } = 4;

    /// <summary>
    /// Retries for transient failures. Fewer than adsb.lol: every attempt can cost credits. 429 ("out of credits")
    /// is never retried, whatever this says.
    /// </summary>
    [Range(0, 5)]
    public int MaxRetries { get; set; } = 1;

    /// <summary>Calls in flight at once (see AdsbLolOptions.MaxConcurrentRequests). Lower: credits are scarce.</summary>
    [Range(1, 100)]
    public int MaxConcurrentRequests { get; set; } = 4;

    [Range(0, 1000)]
    public int MaxQueuedRequests { get; set; } = 10;
}