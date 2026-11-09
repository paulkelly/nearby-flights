using System.ComponentModel.DataAnnotations;

namespace FlightRadar.Api.Logos;

/// <summary>Settings for airline logos, bound from the "Logos" config section.</summary>
public sealed class LogoOptions
{
    public const string SectionName = "Logos";

    /// <summary>
    /// Where to fetch a logo, tried in order until one has it. "{code}" is replaced by the ICAO airline code.
    /// Each must be an absolute https URL.
    /// </summary>
    [Required, MinLength(1)]
    public List<string> UrlTemplates { get; set; } = [];

    /// <summary>
    /// How long a logo (or "no logo") is kept, here and in browsers. Logos almost never change, and a long browser
    /// cache means a busy list costs the API nothing after the first view.
    /// </summary>
    [Range(1, 24 * 30)]
    public int CacheHours { get; set; } = 24;

    /// <summary>Largest logo accepted. Real ones are a few KB; this guards against a source serving something else.</summary>
    [Range(1_000, 1_000_000)]
    public int MaxBytes { get; set; } = 100_000;

    [Range(1, 60)]
    public int TotalTimeoutSeconds { get; set; } = 5;

    [Range(1, 30)]
    public int AttemptTimeoutSeconds { get; set; } = 2;

    [Range(0, 5)]
    public int MaxRetries { get; set; } = 1;

    /// <summary>Most downloads in flight at once: a first look at a busy list can ask for dozens.</summary>
    [Range(1, 100)]
    public int MaxConcurrentRequests { get; set; } = 4;

    [Range(0, 1000)]
    public int MaxQueuedRequests { get; set; } = 200;
}
