using System.Globalization;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Caching;

/// <summary>
/// Decorator: wraps any other <see cref="IFlightDataProvider"/> and reuses its answer for the same area for a few
/// seconds. Neither the wrapped provider nor its callers know the cache exists.
/// </summary>
/// <remarks>
/// <para>
/// Requests are snapped first (<see cref="AreaSnapping"/>): nearby centres and similar radii map to one slightly
/// bigger circle, which is what gets cached and fetched. That's safe because providers only promise "roughly
/// this area" and NearbyFlightsService trims the result to each caller's exact circle.
/// </para>
/// <para>
/// <see cref="HybridCache"/> also gives stampede protection: if 10 requests for the same key arrive while it is
/// empty, the wrapped provider is called once and all 10 await that one call. Failures are never cached; the
/// exception goes to everyone waiting and the next request tries again.
/// </para>
/// </remarks>
public sealed partial class CachingFlightDataProvider(
    IFlightDataProvider inner,
    HybridCache cache,
    IOptions<FlightCacheOptions> options,
    ILogger<CachingFlightDataProvider> logger) : IFlightDataProvider
{
    private readonly HybridCacheEntryOptions _entryOptions = new()
    {
        // Expiration covers a distributed (L2) cache such as Redis, once there is one; LocalCacheExpiration is
        // this process's in-memory (L1) copy. Same value for now.
        Expiration = TimeSpan.FromSeconds(options.Value.TtlSeconds),
        LocalCacheExpiration = TimeSpan.FromSeconds(options.Value.TtlSeconds),
    };

    /// <summary>Whatever the wrapped provider can serve: the cache doesn't change that.</summary>
    public double MaxRadiusKm => inner.MaxRadiusKm;

    public async Task<IReadOnlyList<Flight>> GetFlightsAsync(
        GeoPoint centre, double radiusKm, CancellationToken cancellationToken)
    {
        var area = AreaSnapping.Snap(centre, radiusKm, inner.MaxRadiusKm);
        var key = CacheKey(area.Centre, area.RadiusKm);

        // The factory only runs on a miss. It's `static` and receives everything through the state tuple, so it
        // can't accidentally capture variables, and no closure is allocated on hits.
        // Its CancellationToken is HybridCache's own: the shared call is only cancelled if *every* waiting caller
        // gives up, so one closed browser tab can't fail the request for the others.
        return await cache.GetOrCreateAsync(
            key,
            (self: this, inner, area, key),
            static async (state, ct) =>
            {
                // Only misses are logged: hits are the common case and would drown everything else out.
                state.self.LogCacheMiss(state.key, state.area.RadiusKm);
                return await state.inner.GetFlightsAsync(state.area.Centre, state.area.RadiusKm, ct);
            },
            _entryOptions,
            cancellationToken: cancellationToken);
    }

    // The snapped key is ~10 km coarse, so it's useful for diagnosis without logging anyone's exact location.
    [LoggerMessage(Level = LogLevel.Information, Message = "Cache miss for {CacheKey}: fetching {FetchRadiusKm} km")]
    private partial void LogCacheMiss(string cacheKey, double fetchRadiusKm);

    /// <summary>
    /// The key for a (snapped) area, e.g. "flights:53.4509…:-6.2534…:60". Coordinates use the round-trip format,
    /// so two areas share a key only if they are exactly the same circle.
    /// </summary>
    public static string CacheKey(GeoPoint centre, double radiusKm) =>
        string.Create(CultureInfo.InvariantCulture, $"flights:{centre.Latitude:R}:{centre.Longitude:R}:{radiusKm:R}");
}
