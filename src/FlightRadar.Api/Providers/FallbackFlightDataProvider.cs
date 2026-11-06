using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;

namespace FlightRadar.Api.Providers;

/// <summary>
/// Decorator: asks the primary provider, and only if that fails asks the fallback. With adsb.lol as primary and
/// OpenSky as fallback, users still see aircraft during an adsb.lol outage, and OpenSky's scarce credits are only
/// spent then.
/// </summary>
/// <remarks>
/// <para>
/// Sits inside the cache (cache → fallback → providers), so answers from either source are cached the same way.
/// When adsb.lol's circuit breaker is open, the primary fails instantly, so fallback answers come quickly too.
/// </para>
/// <para>
/// Only <see cref="FlightDataUnavailableException"/> triggers the fallback: that's every upstream failure, after the
/// primary's own retries. Anything else (a bug, or the caller cancelling) isn't something another source can fix.
/// </para>
/// </remarks>
public sealed partial class FallbackFlightDataProvider(
    IFlightDataProvider primary,
    string primaryName,
    IFlightDataProvider fallback,
    string fallbackName,
    ILogger<FallbackFlightDataProvider> logger) : IFlightDataProvider
{
    /// <summary>The smaller limit, so a request can be served by either provider (cache snapping relies on it).</summary>
    public double MaxRadiusKm => Math.Min(primary.MaxRadiusKm, fallback.MaxRadiusKm);

    public async Task<IReadOnlyList<Flight>> GetFlightsAsync(
        GeoPoint centre, double radiusKm, CancellationToken cancellationToken)
    {
        try
        {
            return await primary.GetFlightsAsync(centre, radiusKm, cancellationToken);
        }
        catch (FlightDataUnavailableException primaryFailure)
        {
            LogFallingBack(primaryName, fallbackName, primaryFailure.Message);
        }

        try
        {
            return await fallback.GetFlightsAsync(centre, radiusKm, cancellationToken);
        }
        catch (FlightDataUnavailableException fallbackFailure)
        {
            // Both down: one 502, with the fallback's failure (the most recent) as the cause. The primary's failure
            // was already logged above.
            throw new FlightDataUnavailableException(
                $"{primaryName} and the fallback {fallbackName} are both unavailable.", fallbackFailure);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "{PrimaryProvider} unavailable ({Reason}); trying {FallbackProvider}")]
    private partial void LogFallingBack(string primaryProvider, string fallbackProvider, string reason);
}
