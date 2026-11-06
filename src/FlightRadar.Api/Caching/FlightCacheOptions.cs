using System.ComponentModel.DataAnnotations;

namespace FlightRadar.Api.Caching;

/// <summary>Settings for <see cref="CachingFlightDataProvider"/>, bound from "FlightCache".</summary>
public sealed class FlightCacheOptions
{
    public const string SectionName = "FlightCache";

    /// <summary>
    /// How long an answer for one area is reused. Short on purpose: aircraft move ~250 m/s and the frontend
    /// polls every ~10 s, so this mainly absorbs bursts (several tabs, users or retries asking at once).
    /// </summary>
    [Range(1, 300)]
    public int TtlSeconds { get; set; } = 5;
}
