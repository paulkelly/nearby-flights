using System.ComponentModel.DataAnnotations;

namespace FlightRadar.Api.RateLimiting;

/// <summary>
/// Per-client token bucket for /api/flights, bound from "RateLimiting". Like an ability with charges: up to
/// <see cref="TokenLimit"/> stored, <see cref="TokensPerPeriod"/> regained every <see cref="ReplenishmentPeriodSeconds"/>.
/// </summary>
/// <remarks>
/// Defaults: bursts of up to 20 requests, 30 a minute sustained. One tab polls 6 a minute plus bursts from moving
/// the map, so normal use (even several tabs, or a household sharing an IP) never hits it; a script does in seconds.
/// </remarks>
public sealed class ClientRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Bucket capacity: the largest burst. Refilling never goes above it.</summary>
    [Range(1, 1000)]
    public int TokenLimit { get; set; } = 20;

    [Range(1, 1000)]
    public int TokensPerPeriod { get; set; } = 1;

    [Range(1, 3600)]
    public int ReplenishmentPeriodSeconds { get; set; } = 2;
}
